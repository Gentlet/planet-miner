# PlanetMiner `architecture-v2` 통합 개선 피드백

기준 브랜치: `architecture-v2`  
기준 HEAD: `8b4c83c114ff9371d25fac650a0cb59a1a756b13`  
현재 Task 진행 상태: **Phase 5 Crafter 완료 → Phase 6 Splitter / Merger 진입 전**  
현재 EditMode 테스트: **97 / 97 Pass**

이 문서는 기존 Architecture V2 피드백과 최신 코드 리뷰 결과를 통합한 문서다.

이미 코드에 반영되어 해결된 피드백은 제외했으며, **현재 시점에도 남아 있는 문제만 포함한다.**

또한 현재 Task 진행 상태를 기준으로 피드백을 다음 두 범주로 나눈다.

1. **Phase 6 진입 전에 적용할 항목**
   - 이미 완료된 Phase 1~5의 기능 정합성 문제
   - Architecture V2 핵심 원칙 위반
   - 향후 Task가 의존하게 될 데이터 계약/기반 문제

2. **향후 Task 진행 또는 Profiler 결과에 따라 적용할 항목**
   - 현재 기능 정합성에는 직접 문제가 없는 최적화
   - 실제 규모와 병목을 측정한 뒤 결정해야 하는 구조 변경

> 현재 단계의 목표는 **Phase 1~5 기반을 안정화한 뒤 Phase 6으로 넘어가는 것**이다.  
> 성능을 추측해서 미리 최적화하는 작업은 후순위로 둔다.

---

# 적용 시점별 요약

## A. Phase 6 진입 전에 적용

### A-1. 우선 수정 — 기능 정합성 / Architecture 원칙

1. 레시피 변경 시 `StorageFilter` 갱신 Phase 순서 수정
2. Miner 생산물의 Pending Capacity 예약 문제 해결
3. Crafter 다중 부산물 처리 규칙 통일
4. `CrafterDecisionSystem`의 Persistent State 직접 수정 제거

### A-2. 구조 계약 정리

5. `SpawnItemRequest`의 목적지(Storage/Product/World) 명시
6. `DestroyItemRequest`의 Storage/Product Buffer 정합성 계약 명확화
7. Simulation DeltaTime 정책 통일
8. `MaxStorageSlots` 제한의 실제 검증/강제
9. `CrafterSlotConfig`의 현재 구조와 의미 정리

### A-3. 기반 안정화

10. `BuildingSpatialIndex` Capacity 계산 개선

---

## B. 향후 Task / Profiler 단계에서 적용

11. 정적 Spatial Index의 매 프레임 전체 재구축 개선
12. `WorldInvariantValidationSystem`의 Profiler 영향 분리
13. 실제 병목 확인 후 추가 병렬화 검토

---

# 권장 수정 순서

현재 Phase 5까지 완료된 상태에서 다음 순서로 처리한다.

```text
[Phase 1~5 기능 정합성]

1. Recipe 변경 / StorageFilter Phase 순서 수정
2. Miner Pending Product Capacity 해결
3. Crafter 다중 부산물 처리 통일
4. CrafterDecision의 State 직접 Write 제거

        ↓

[공통 데이터 계약 정리]

5. SpawnItemRequest 목적지 명시
6. DestroyItemRequest 계약 명확화
7. Simulation DeltaTime 정책 통일
8. MaxStorageSlots 검증 추가
9. CrafterSlotConfig 정리

        ↓

[기반 안정화]

10. BuildingSpatialIndex Capacity 개선

        ↓

[Phase 6 Splitter / Merger 진입]

        ↓

[후속 기능 구현]

Phase 7 Construction
Phase 8 Power
Phase 9 Drone
Phase 10 Research / Remaining

        ↓

[실제 규모 기반 성능 측정]

11. 정적 Spatial Index Dirty/Event Update 검토
12. Validation ON/OFF Profiler 측정
13. 실제 병목 시스템만 추가 병렬화
```

---

# A. Phase 6 진입 전에 적용할 피드백

# 피드백 1. 레시피 변경 시 `StorageFilter` 갱신 Phase 순서 수정 ( 적용 )

**적용 시점: 지금**  
**우선순위: 매우 높음**

현재 `CrafterExecutionSystem`에서 레시피 변경을 감지하고 `StorageFilter`를 갱신한다.

현재 실행 순서는 다음과 같다.

```text
DecisionGroup
    BuildingItemInputDecisionSystem
        ↓
ReservationGroup
        ↓
ExecutionGroup
    CrafterExecutionSystem
        ↓
StateApplyGroup
```

`BuildingItemInputDecisionSystem`은 Decision 단계에서 기존 `StorageFilter`를 읽고 입고 여부를 판정한다.

따라서 같은 프레임에 레시피가 변경되면 다음 상황이 가능하다.

```text
기존 Recipe = Iron
StorageFilter = Iron 허용

↓ SelectedRecipeId를 Copper로 변경

DecisionGroup
    기존 Filter 기준으로 Iron 입고 승인

ReservationGroup
    Iron 입고 슬롯 예약

ExecutionGroup
    CrafterExecutionSystem이 Copper용 Filter로 변경
    기존 Iron 재료 Purge

StateApplyGroup
    앞서 승인된 Iron이 다시 Crafter에 입고
```

최종적으로:

```text
StorageFilter = Copper Only
StoredItemElement = Iron
```

상태가 만들어질 수 있다.

이는 현재 Phase 5 Crafter 구현 자체의 정합성 문제이므로 Phase 6로 넘어가기 전에 수정한다.

## 개선 방향

레시피 변경 감지와 Filter 동기화를 Decision 이전으로 이동한다.

예:

```text
CommandGroup
    CrafterRecipeSyncSystem
        ├ SelectedRecipeId 변경 감지
        ├ StorageFilter 갱신
        └ RecipeChange 상태 기록

        ↓

DecisionGroup
    BuildingItemInputDecisionSystem
```

핵심 규칙:

```text
입고 판단 전에 StorageFilter가 반드시 최신 Recipe 상태와 일치해야 한다.
```

## 추가 테스트

```text
Recipe Iron → Copper 변경
+
같은 프레임에 Iron 입고 시도

Expected:
Iron 입고가 승인되지 않아야 함
```

---

# 피드백 2. Miner 생산물의 Pending Capacity 예약 문제

**적용 시점: 지금**  
**우선순위: 높음**

현재 `MinerDecisionSystem`은 생산물 버퍼 공간을 다음처럼 판단한다.

```csharp
bool hasSpace = productItems.Length < maxStack;
```

하지만 Miner가 생산한 Item은 같은 프레임에 바로 `ProductItemElement`에 추가되지 않는다.

현재 흐름:

```text
MinerExecution
    ↓
SpawnItemRequest 생성

StateApply
    ↓
Request Entity 생성 / Playback

다음 처리 시점
    ↓
ItemLifecycleApply
    ↓
ProductItemElement에 실제 Item 추가
```

Decision 시점에는 이미 발행된 Pending Spawn이 `productItems.Length`에 포함되지 않을 수 있다.

예:

```text
MaxStack = 50
현재 ProductBuffer = 49

Frame N
    공간 있음 → Spawn A 요청

Frame N+1
    ProductBuffer가 아직 49로 보임
    → Spawn B 요청

이후
    A 반영 → 50
    B 반영 → 51
```

처럼 MaxStack을 초과할 가능성이 있다.

Phase 4 Mining은 이미 완료된 Vertical Slice이므로 이 문제는 지금 닫고 넘어가는 것이 좋다.

## 개선 방향

생산 Capacity에도 Reservation/Pending 개념을 적용한다.

```text
Decision
    생산 공간 필요

Reservation
    Product Capacity 예약

Execution
    생산

StateApply
    실제 Item 생성
```

간단한 형태:

```csharp
productItems.Length + pendingProductCount < maxStack
```

## 추가 테스트

```text
ProductBuffer = MaxStack - 1
+
이미 Pending Spawn 1개 존재
+
Miner가 추가 생산 시도

Expected:
추가 생산이 차단되어야 함
```

---

# 피드백 3. Crafter 다중 부산물 처리 규칙 통일

**적용 시점: 지금**  
**우선순위: 높음**

현재 Recipe 데이터 모델은 여러 부산물을 지원한다.

```csharp
BlobArray<RecipeOutputBlob> Outputs;
```

JSON 구조도 여러 `byproducts`를 지원한다.

하지만 Crafter 구현은 사실상 첫 번째 부산물만 처리한다.

```csharp
recipe.Outputs[1]
```

형태로 Decision과 Execution 모두 첫 번째 부산물만 검사/생성한다.

예:

```text
Primary
Byproduct A
Byproduct B
Byproduct C
```

현재 실제 동작:

```text
Primary      ✅
Byproduct A  ✅
Byproduct B  ❌
Byproduct C  ❌
```

Recipe 모델과 실제 Crafter 계약이 서로 다르므로 Phase 5 완료 시점에 정책을 확정한다.

## 개선 방향

다음 둘 중 하나를 선택한다.

### 방법 A — 다중 부산물 지원

```text
Outputs 전체 순회
→ 각 Output별 공간 확인
→ 각 Output별 Spawn
```

### 방법 B — 부산물 최대 1개로 제한

- Config Loader에서 1개 초과를 거부
- 테스트에서 제약 검증
- 데이터 구조와 문서에서도 최대 1개 정책 명시

현재 데이터 모델은 다중 부산물을 전제로 만들어져 있으므로 **방법 A가 구조상 더 자연스럽다.**

---

# 피드백 4. `CrafterDecisionSystem`의 State 직접 수정 제거

**적용 시점: 지금**  
**우선순위: 높음**

Architecture V2의 핵심 원칙:

```text
State
    ↓ Read

DecisionSystem

    ↓ Write

Decision Component
```

Belt는 이 규칙을 지키고 있다.

```text
BeltMovementState
    ↓
BeltMovementDecisionSystem
    ↓
BeltMovementDecision
```

하지만 Crafter는 Decision 단계에서:

```csharp
ref CrafterState state
```

를 받고 Persistent State를 직접 변경한다.

```csharp
state.Status = CrafterStatusEnum.Crafting;
state.Status = CrafterStatusEnum.WaitingForInput;
state.Status = CrafterStatusEnum.WaitingForOutput;
```

## 문제점

- Decision과 State 변경 책임이 다시 결합된다.
- V2의 State/Decision 분리 원칙이 Crafter에서 깨진다.
- Recipe 변경 같은 Execution 상태 변화와 한 프레임 내 의미 충돌 가능성이 생긴다.

현재 Architecture V2의 구조 자체를 검증하는 단계이므로 이 예외를 남긴 채 다음 도메인으로 확장하지 않는다.

## 개선 방향

`CrafterDecision`이 다음 상태를 표현하도록 한다.

예:

```csharp
public CrafterStatusEnum NextStatus;
```

흐름:

```text
CrafterState
    ↓ Read

CrafterDecisionSystem
    ↓

CrafterDecision.NextStatus
    ↓

Execution / StateApply
    ↓

CrafterState.Status
```

---

# 피드백 5. `SpawnItemRequest`의 목적지(Storage/Product/World) 명시

**적용 시점: 지금 계약 확정 / 후속 Task에서 확장**  
**우선순위: 중간~높음**

현재 `SpawnItemRequest`는 다음 정도만 가진다.

```csharp
ItemType
TargetOwner
TargetSlotIndex
```

하지만 `TargetOwner`가 다음 두 버퍼를 모두 가질 수 있다.

```text
StoredItemElement
ProductItemElement
```

현재 `ItemLifecycleApplySystem`은:

```csharp
if (ProductBufferLookup.HasBuffer(request.TargetOwner))
{
    ProductItemElement에 추가
}
else if (StoredBufferLookup.HasBuffer(request.TargetOwner))
{
    StoredItemElement에 추가
}
```

즉 Product Buffer가 존재하면 무조건 Product 쪽이 우선된다.

현재 Miner/Crafter에는 맞더라도 Request 자체의 의미는 모호하다.

향후 Producer가 늘어나는 Task:

```text
Phase 7 Construction
Phase 9 Drone
Phase 10 Research / Remaining
Save / Load
Debug Spawn
초기 아이템 지급
```

때 이 모호성이 확대될 수 있다.

## 개선 방향

현재 단계에서 목적지 계약을 먼저 명시한다.

예:

```csharp
public enum ItemSpawnDestination : byte
{
    World,
    Storage,
    Product
}
```

또는 Request를 분리한다.

```text
SpawnWorldItemRequest
SpawnStoredItemRequest
SpawnProductItemRequest
```

후속 Task에서는 이 계약을 재설계하지 않고 필요 시 목적지만 확장하는 방향이 좋다.

---

# 피드백 6. `DestroyItemRequest`의 Storage/Product Buffer 정합성 계약 명확화

**적용 시점: 지금 계약 확정 / 후속 Task에서 준수**  
**우선순위: 중간~높음**

현재 Crafter 재료 소비 경로는 다음 순서를 사용한다.

```text
StoredItemElement에서 제거
    ↓
DestroyItemRequest 활성화
    ↓
ItemLifecycleApplySystem에서 Entity 파괴
```

하지만 `DestroyItemRequest` 자체는:

```csharp
ECB.DestroyEntity(entity);
```

만 수행한다.

향후 다른 시스템이:

```text
Stored/Product Buffer에 Item이 남아 있는 상태
    ↓
DestroyItemRequest
```

를 수행하면 Buffer에 파괴된 Entity 참조가 남을 수 있다.

후속 Task에서 Destroy Producer가 증가할 가능성이 높다.

예:

```text
Phase 7 Construction 철거/자원 처리
Phase 9 Drone Cargo 처리
Phase 10 Research 소비
```

## 개선 방향

프로젝트 전체 계약을 지금 확정한다.

### 방법 A — Producer 책임

```text
DestroyItemRequest 발행 전
Owner Buffer에서 반드시 제거
```

Invariant / 테스트로 규칙을 강제한다.

### 방법 B — Lifecycle 책임

`ItemLifecycleApplySystem`이 Owner 정보를 확인하고:

```text
Owner Buffer에서 제거
    ↓
Entity Destroy
```

까지 수행한다.

현재 구현은 방법 A에 가깝기 때문에, 유지한다면 **DestroyItemRequest API 계약을 코드/문서/테스트로 명시한다.**

---

# 피드백 7. Simulation DeltaTime 정책 통일

**적용 시점: 지금**  
**우선순위: 중간**

공통 상수:

```csharp
GameConstants.MaxSimulationDeltaTime = 0.1f;
```

가 존재하고 Belt와 Crafter에서는 사용하고 있다.

하지만 Miner가 다른 dt 정책을 사용하면 프레임 hitch 시:

```text
Belt    → 0.1s 진행
Crafter → 0.1s 진행
Miner   → 실제 dt 전체 진행
```

처럼 도메인별 시뮬레이션 시간이 달라질 수 있다.

Phase 4/5가 완료된 상태에서 시뮬레이션 시간 정책은 공통 기반으로 확정하고 넘어가는 편이 좋다.

## 개선 방향

모든 게임 시뮬레이션 시스템에서 동일한 dt 정책을 적용한다.

최소:

```csharp
float dt = math.min(
    SystemAPI.Time.DeltaTime,
    GameConstants.MaxSimulationDeltaTime
);
```

장기적으로는 시스템마다 clamp하지 않고 **공통 Simulation Time 데이터**를 제공하는 방식도 고려할 수 있다.

---

# 피드백 8. `MaxStorageSlots` 제한의 실제 검증/강제

**적용 시점: 지금**  
**우선순위: 중간**

현재:

```csharp
GameConstants.MaxStorageSlots = 120;
```

으로 제한이 명시되어 있다.

하지만 Reservation이:

```csharp
int safeSlotCount =
    math.min(slotCount, GameConstants.MaxStorageSlots);
```

처럼 동작하면 잘못된 데이터가 들어와도 조용히 120으로 잘린다.

예:

```text
Storage.SlotCount = 200
→ 실제 처리 = 120
```

Storage는 이미 Phase 3에서 완료된 기반 도메인이므로 이 제한이 실제 게임 규칙이라면 지금 강제한다.

## 개선 방향

```text
Storage.SlotCount <= MaxStorageSlots
```

를 다음 중 하나 이상에서 검증한다.

- Authoring / Config Validation
- 생성 시점 Validation
- `WorldInvariantValidationSystem`
- 테스트

숨은 clamp보다는 명시적인 실패/검증이 낫다.

---

# 피드백 9. `CrafterSlotConfig`의 현재 구조와 의미 정리

**적용 시점: 지금**  
**우선순위: 낮음~중간**

기존 `CrafterSlotConfig`:

```text
Input Slot  = 0 ~ 3
Output Slot = 4 ~ 5
```

하지만 현재 Crafter 출력은 별도 `ProductItemElement` Buffer를 사용하며:

```text
Product Slot 0 = Primary
Product Slot 1 = Byproduct
Product Slot 2+ = Recipe Change Purge
```

형태다.

즉 기존 `CrafterSlotConfig.OutputSlotStart/OutputSlotCount`의 의미가 현재 구현과 맞지 않는다.

Phase 5를 완료한 지금이 과거 설계 흔적을 정리하기 가장 좋은 시점이다.

## 개선 방향

- 현재 사용하지 않는 Output Slot 설정이면 제거
- 또는 Input Buffer 설정과 Product Buffer 설정으로 의미를 재정의
- 현재 Recipe / Product Buffer 정책과 테스트를 기준으로 문서화

---

# 피드백 10. `BuildingSpatialIndex` Capacity 계산 개선

**적용 시점: Phase 6 진입 전 기반 안정화**  
**우선순위: 중간**

현재 Capacity 추정이:

```csharp
int requiredCapacity = count * 4;
```

처럼 건물 Entity 수를 기준으로 한다면 실제 Index Entry 수와 차이가 날 수 있다.

실제 Entry 수는 **점유 타일 수 총합**이다.

예:

```text
100 Buildings
각 Building = 3x3

Entity Count = 100
실제 Index Entry = 900
```

Phase 7 Construction에서는 다양한 크기의 건물이 실제 생성/철거되므로 그 전에 기반 계산을 안정화하는 편이 좋다.

## 개선 방향

가능하면:

```text
Σ BuildingFootprint 점유 Tile 수
```

기준으로 Capacity를 계산한다.

또는 프로젝트의 최대 Footprint 규칙이 명확하다면 그 기준으로 충분한 여유 Capacity를 확보한다.

---

# B. 향후 Task / Profiler 단계에서 처리할 피드백

# 피드백 11. 정적 Spatial Index의 매 프레임 전체 재구축 개선

**적용 시점: 후속 Task 진행 후 / 대규모 Profiler 결과 확인 시**  
**우선순위: 보류**

현재 Synchronization 단계에서 다음 Spatial Index를 매 프레임:

```text
Clear
    ↓
전체 Entity 재등록
```

한다.

대상:

```text
BeltSpatialIndex
BuildingSpatialIndex
ResourceSpatialIndex
ItemSpatialIndex
```

`ItemSpatialIndex`는 아이템이 자주 이동하므로 전체 갱신이 자연스럽다.

하지만:

```text
Belt
Building
Resource
```

는 대부분 정적이다.

## 지금 수정하지 않는 이유

현재 방식은:

- 구현이 단순하다.
- 정합성 검증이 쉽다.
- Phase 6~9 구현 중 Entity 규모가 실제로 얼마나 커질지 아직 모른다.
- 현재 Architecture V2 원칙은 성능 추측 기반 최적화를 Non-Goal로 둔다.

따라서 지금 Dirty/Event 기반 구조로 바꾸지 않는다.

## 향후 검토 조건

Profiler에서 Spatial Sync/Rebuild가 실제 병목으로 확인될 때 검토한다.

예:

```text
Belt 설치/철거
    ↓
BeltSpatialIndex Dirty Update

Building 설치/철거
    ↓
BuildingSpatialIndex Dirty Update

Resource 생성/고갈
    ↓
ResourceSpatialIndex Dirty Update
```

특히 Phase 7 Construction 완료 이후가 좋은 측정 시점이다.

---

# 피드백 12. `WorldInvariantValidationSystem`의 Profiler 영향 분리

**적용 시점: 본격적인 성능 측정 직전**  
**우선순위: 보류**

현재 Validation은 검증 정확성을 위해 Spatial Index Fence를:

```csharp
Complete()
```

한다.

예:

```csharp
beltFence.Complete();
itemFence.Complete();
buildingFence.Complete();
resourceFence.Complete();
```

이 방식은 개발 단계 정합성 검증에는 적합하지만 Job 비동기 실행을 메인 스레드에서 기다리게 하므로 실제 Simulation 성능 측정을 왜곡할 수 있다.

## 지금 수정하지 않는 이유

현재는 Architecture V2 구조 안정화가 우선이며, 강한 Invariant Validation은 디버깅 가치가 높다.

## 적용 시점

`Task 10.3: 전체 시스템 통합 프로파일링 및 최적화`에 맞춰 다음을 적용한다.

- `CheckIntervalFrames` 조절
- Validation 활성/비활성 옵션
- Profiler 측정 시 Validation ON/OFF 결과 분리
- Release 성능과 Development Validation 비용 분리 기록

Invariant Validation 자체는 유지한다.

---

# 피드백 13. 실제 병목 확인 후 추가 병렬화 검토

**적용 시점: Profiler에서 병목 확인 후**  
**우선순위: 보류**

현재 일부 시스템은 안전성을 위해 `Schedule()` 단일 Worker로 실행된다.

예:

- Storage Reservation
- 일부 Apply 시스템

공유 Storage 상태에 대한 경합이 존재하는 시스템은 직렬화가 자연스럽다.

반면 Item 단위로 완전히 독립적인 Apply 시스템은 향후 `ScheduleParallel()` 후보가 될 수 있다.

예:

```text
ItemOwnershipApplySystem
```

## 지금 수정하지 않는 이유

Architecture V2 Plan의 Non-Goal:

```text
모든 시스템을 병렬 Job으로 만들기
성능 추측만으로 구조를 과도하게 최적화하기
```

와 직접 연결된다.

## 적용 원칙

```text
구조 안정화
    ↓
후속 도메인 구현
    ↓
Profiler 측정
    ↓
실제 병목 확인
    ↓
병목 시스템만 병렬화
```

정합성과 유지보수성이 미세 성능 최적화보다 우선이다.

---

# 후속 Task와 연결되는 피드백

일부 항목은 **지금 기본 계약을 확정하되**, 후속 Task에서 사용 범위가 확대된다.

## Phase 6 — Splitter / Merger

직접적으로 새로운 피드백을 요구하지는 않는다.

다만 다음 기반이 안정되어 있어야 한다.

```text
Belt Decision / Execution 분리
Item Spatial Index 정합성
Simulation DeltaTime 정책
```

---

## Phase 7 — Construction

다음 피드백과 강하게 연결된다.

```text
SpawnItemRequest 목적지 계약
DestroyItemRequest 정합성 계약
BuildingSpatialIndex Capacity
정적 Spatial Index 업데이트 전략
```

특히 건물 설치/철거가 들어오면 Spatial Index Dirty Update를 실제로 평가하기 좋은 시점이 된다.

---

## Phase 8 — Power

직접적으로 현재 13개 피드백과 연결되는 항목은 적다.

다만 Architecture V2 원칙:

```text
Decision은 다른 Domain의 State를 직접 수정하지 않는다.
Domain은 다른 Domain의 public contract만 읽는다.
```

를 Power Supply 데이터 계약에도 그대로 적용한다.

---

## Phase 9 — Drone

다음 계약의 중요성이 크게 올라간다.

```text
SpawnItemRequest 목적지
DestroyItemRequest 정합성
Reservation 개념
State Owner
Consume-on-Apply
```

Drone Task 구현 전에 현재 Item Lifecycle 계약이 명확해야 후속 수정 범위가 줄어든다.

---

## Phase 10 — Research / Remaining / Profiling

다음 피드백의 본격적인 처리 시점이다.

```text
정적 Spatial Index Dirty/Event Update
WorldInvariantValidationSystem Profiler 영향 분리
추가 병렬화
```

특히 `Task 10.3: 전체 시스템 통합 프로파일링 및 최적화`에서 실제 측정값을 기준으로 결정한다.

---

# Phase 6 진입 전 완료 체크리스트

Phase 6 작업을 시작하기 전에 다음 항목을 닫는 것을 권장한다.

## 기능 정합성

- [x] Recipe 변경 프레임에서 이전 Recipe 재료가 입고되지 않는다. (완료: CommandGroup 원자적 동기화, WaitingForPurgeOutput 입고 차단)
- [ ] Miner Pending Spawn을 포함해 Product Capacity가 MaxStack을 초과하지 않는다.
- [ ] Crafter 부산물 지원 범위가 데이터 모델과 실제 구현에서 일치한다.
- [ ] Crafter Decision이 Persistent `CrafterState`를 직접 수정하지 않는다.

## 데이터 계약

- [ ] `SpawnItemRequest`가 World / Storage / Product 목적지를 명확히 표현한다.
- [ ] `DestroyItemRequest` 발행 전 Owner Buffer 처리 책임이 명확하다.
- [ ] 모든 Core Simulation 시스템이 같은 DeltaTime 정책을 사용한다.
- [ ] `Storage.SlotCount`가 `MaxStorageSlots` 범위를 벗어나면 명확히 검출된다.
- [x] `CrafterSlotConfig`가 현재 Buffer 구조와 일치한다. (완료: 과거 설계 흔적 제거 및 CrafterComponents / RecipeConfigComponents 분리 정리)

## 기반

- [ ] `BuildingSpatialIndex` Capacity가 실제 점유 타일 규모를 안전하게 처리한다.
- [ ] 관련 신규/변경 테스트가 모두 통과한다.
- [ ] 기존 EditMode 테스트가 회귀 없이 통과한다.

---

# 현재는 보류할 체크리스트

다음 항목은 현재 Phase 6 진입 조건으로 잡지 않는다.

- [ ] Belt / Building / Resource Spatial Index를 Dirty/Event 기반으로 변경
- [ ] Invariant Validation의 `Complete()` 제거/최적화
- [ ] Apply / Reservation 시스템의 추가 병렬화

이 항목들은 **실제 Profiler 결과가 확보된 뒤 판단한다.**

---

# 유지해야 할 Architecture V2 원칙

현재 Architecture V2의 큰 방향은 유지한다.

```text
Command
    ↓
Decision
    ↓
Reservation
    ↓
Execution
    ↓
StateApply
    ↓
Synchronization
```

특히 다음 규칙을 계속 유지한다.

- Decision 시스템은 Persistent State를 직접 변경하지 않는다.
- State와 Decision Component를 분리한다.
- 공유 자원 경합은 Reservation 단계에서 해결한다.
- StateApply는 최종 상태 변경의 명확한 책임자를 가진다.
- Spatial Index는 Source of Truth가 아닌 파생 데이터로 취급한다.
- `IEnableableComponent`를 활용해 불필요한 Structural Change를 줄인다.
- Request/Decision은 명확한 생성자·소비자·수명주기 계약을 가진다.
- Invariant Validation으로 Phase 간 데이터 정합성을 검증한다.
- 성능 최적화는 실제 Profiler 결과가 있을 때 수행한다.
- 후속 Task가 기존 계약을 우회하지 않고 현재 확정한 public data contract를 사용하도록 한다.

---

# 최종 진행 방향

현재 Architecture V2 진행 상태는 다음과 같이 본다.

```text
Phase 0~5 구현 완료
        ↓
현재: Phase 1~5 기반 안정화
        ↓
Phase 6 Splitter / Merger
        ↓
Phase 7 Construction
        ↓
Phase 8 Power
        ↓
Phase 9 Drone
        ↓
Phase 10 Research / Remaining
        ↓
전체 Profiler 측정 / 최적화
```

따라서 현재는:

```text
기능 버그
Architecture 원칙 위반
공통 데이터 계약 모호성
후속 Task가 의존할 기반 문제
```

를 먼저 해결한다.

반대로:

```text
Dirty Spatial Index
Validation 성능 최적화
추가 Job 병렬화
```

는 실제 게임 규모와 Profiler 결과가 확보될 때까지 보류한다.
