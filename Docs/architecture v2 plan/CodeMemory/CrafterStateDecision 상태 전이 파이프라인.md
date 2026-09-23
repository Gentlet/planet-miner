# CrafterStateDecision 상태 전이 파이프라인

작성 기준: Architecture V2  
관련 Phase: Decision → Execution → StateApply  
현재 검증 상태: EditMode 99 / 99 Pass

---

## 1. 목적

Crafter의 논리적 상태 전이를 `CrafterDecisionSystem` 내부의 Persistent State 직접 수정에서 분리한다.

기존 문제:

```text
CrafterDecisionSystem
    ↓
CrafterState.Status 직접 수정
```

이는 Architecture V2의 다음 원칙을 깨뜨렸다.

```text
Decision
→ Persistent State 직접 변경 금지
→ Decision Component에 결과 기록
```

현재는 실행 결정과 상태 전이 결정을 분리한다.

```text
CrafterDecision
→ Execution용

CrafterStateDecision
→ StateApply용
```

---

## 2. CrafterStateDecision

```csharp
public struct CrafterStateDecision : IComponentData, IEnableableComponent
{
    public CrafterStatusEnum NextStatus;

    public CrafterStateDecision(CrafterStatusEnum nextStatus)
    {
        NextStatus = nextStatus;
    }
}
```

Enable 상태의 의미:

```text
Disabled
→ 적용할 상태 전이 없음

Enabled
→ StateApply에서 NextStatus를 CrafterState.Status에 반영해야 함
```

별도 `HasStatusUpdate` bool은 사용하지 않는다.

`IEnableableComponent`의 Enabled 상태 자체가 Pending State Transition 여부를 표현한다.

---

## 3. 전체 흐름

```text
DecisionGroup

CrafterDecisionSystem
    │
    ├─ CrafterState Read Only
    │
    ├─ CrafterDecision
    │    → 제작 실행 관련 결정
    │
    └─ CrafterStateDecision
         → NextStatus 기록
         → Enabled

        ↓

ExecutionGroup

CrafterExecutionSystem
    ├─ 재료 소비
    ├─ Progress 변경
    ├─ IsCraftingActive 변경
    └─ ProductResult 기록

        ↓

StateApplyGroup

CrafterStateApplySystem
    ├─ CrafterState.Status = NextStatus
    └─ CrafterStateDecision Disabled
```

---

## 4. CrafterDecisionSystem 책임

`CrafterState`는 Read Only다.

```csharp
in CrafterState state
```

상태 전이가 필요하면:

```csharp
stateDecision.NextStatus = CrafterStatusEnum.WaitingForInput;
stateDecisionEnabled.ValueRW = true;
```

형태로 기록한다.

예:

```text
NoRecipe
WaitingForInput
Crafting
WaitingForOutput
WaitingForByproductOutput
Idle
```

모든 논리 상태 판정은 `CrafterStateDecision`에 기록하고
Decision 단계에서는 `CrafterState.Status`를 직접 수정하지 않는다.

---

## 5. CrafterStateApplySystem

현재 시스템은 별도 수동 EntityQuery를 만들지 않는다.

```csharp
[BurstCompile]
public void OnUpdate(ref SystemState state)
{
    var job = new CrafterStateApplyJob();
    state.Dependency = job.ScheduleParallel(state.Dependency);
}
```

`IJobEntity.Execute` 시그니처를 기준으로 자동 Query를 사용한다.

현재 Apply Job:

```csharp
[BurstCompile]
public partial struct CrafterStateApplyJob : IJobEntity
{
    public void Execute(
        ref CrafterState state,
        ref CrafterStateDecision decision,
        EnabledRefRW<CrafterStateDecision> decisionEnabled)
    {
        state.Status = decision.NextStatus;
        decisionEnabled.ValueRW = false;
    }
}
```

Consume-on-Apply 규칙:

```text
Enabled CrafterStateDecision
    ↓
CrafterState.Status 적용
    ↓
CrafterStateDecision Disabled
```

ECB는 사용하지 않는다.

---

## 6. EnabledRefRW와 aliasing 결론

검토 과정에서 다음 형태를 시도했다.

```csharp
in CrafterStateDecision decision,
EnabledRefRW<CrafterStateDecision> decisionEnabled
```

현재 프로젝트의 Entities 6.4 Source Generator에서는 실제 Job Schedule 시
다음 두 TypeHandle이 생성되어 aliasing 오류가 발생했다.

```text
CrafterStateDecision_RO_ComponentTypeHandle
CrafterStateDecision_RW_ComponentTypeHandle
```

오류 형태:

```text
The writeable ComponentTypeHandle<CrafterStateDecision>
is the same ... as
CrafterStateDecision_RO_ComponentTypeHandle
(two containers may not be the same / aliasing)
```

수동 Query 제거 후 자동 Query에서도 동일하게 재현됐다.

반면 다음 형태는 정상 동작한다.

```csharp
ref CrafterStateDecision decision,
EnabledRefRW<CrafterStateDecision> decisionEnabled
```

이 조합으로 전체 EditMode 테스트 99 / 99 Pass를 확인했다.

따라서 현재 프로젝트 계약은 다음과 같다.

```text
CrafterStateApplyJob에서
CrafterStateDecision 데이터는 ref
Enable 상태는 EnabledRefRW
```

실제로 `decision.NextStatus` 값 자체를 수정하지 않더라도
현재 Source Generator 동작과의 호환성을 위해 `ref`를 사용한다.

---

## 7. WaitingForByproductOutput의 1프레임 지연

상태 전이는 StateApply에서 확정되므로
같은 프레임의 다른 Decision 시스템은 아직 이전 Persistent State를 읽을 수 있다.

예:

```text
Frame N Decision
    ProductBuffer empty
    NextStatus = WaitingForInput

같은 Frame N Decision
    BuildingItemInputDecisionSystem
    기존 Status = WaitingForByproductOutput
    → 입고 차단

Frame N StateApply
    Status = WaitingForInput

Frame N+1 Decision
    입고 허용
```

새 재료 입고 재개가 최대 1프레임 늦어지는 것은 허용한다.

이를 해결하기 위해 Decision 시스템끼리 서로의 Decision 데이터를 읽게 만들지 않는다.

---

## 8. Fence 정책

`CrafterStateApplySystem` 전용 Fence는 사용하지 않는다.

현재 다루는 데이터:

```text
CrafterState
CrafterStateDecision
```

두 데이터 모두 일반 ECS Component다.

시스템은:

```csharp
state.Dependency = job.ScheduleParallel(state.Dependency);
```

로 ECS Dependency Chain에 연결된다.

따라서 Component Read/Write dependency는 Entities가 자동 추적한다.

Spatial Index 계열 Fence와는 성격이 다르다.

```text
BuildingSpatialIndex / BeltSpatialIndex
→ 내부 NativeParallelHashMap 공유
→ 별도 Reader/Writer Fence 사용

CrafterState / CrafterStateDecision
→ 일반 ECS Component
→ ECS Dependency Tracking 사용
→ 별도 Fence 없음
```

향후 별도의 NativeContainer를 여러 시스템이 공유하는 구조로 변경될 경우에만
해당 공유 데이터에 대한 Fence를 다시 검토한다.

---

## 9. 최종 State 소유권

```text
SelectedRecipeId
ActiveRecipeId
→ Command

CrafterDecision
→ Decision에서 생성
→ Execution에서 소비

CrafterStateDecision
→ Decision에서 생성/활성화
→ StateApply에서 소비/비활성화

Progress
IsCraftingActive
→ Execution

CrafterState.Status
→ StateApply
```

핵심 규칙:

> CrafterDecisionSystem은 CrafterState를 읽지만 Persistent State를 직접 수정하지 않는다.

> Status 전이는 CrafterStateDecision이라는 별도 일회성 Decision Component로 전달한다.

> CrafterStateApplySystem이 상태 전이의 최종 State Owner다.
