# PlanetMiner Architecture V2 - Transient Command & Event Standard

> 본 문서는 Architecture V2에서 시스템 간 결합도를 낮추고 데이터 무결성을 보장하기 위해 사용하는  
> **1회성 명령(Request) 및 이벤트(Event) 컴포넌트의 표준 작성 규약 및 수명주기 가이드라인**입니다.

---

## 1. 핵심 설계 원칙

### 1.1 Consume-on-Apply (소비자 수명주기 책임)
* **원칙**: 1회성 Request의 수명주기 관리(삭제/비활성화) 책임은 **해당 Request를 처리하는 소비자(Consumer) 시스템**에 있습니다.
* 요청이 처리되었는데도 프레임 끝까지 방치되거나 여러 프레임 동안 남아있는 상태를 금지합니다.
* 성공이든 실패든, Consumer가 요청을 검토한 즉시 반드시 해당 요청을 비활성화하거나 파괴해야 합니다.

### 1.2 SynchronizationGroup의 역할 (안전망)
* 6단계 `SynchronizationGroup`은 모든 Request를 일괄 삭제하는 곳이 아닙니다.
* **안전망(Safety Net)**으로서, Consumer가 누락되었거나 대상 엔티티가 파괴되어 정상적으로 소비되지 못한 '고아(Orphan) Request'나 '타임아웃된 Request'만을 탐지 및 격리/정리합니다.

---

## 2. 하이브리드 Request 모델 (선택 기준)

DOTS의 성능 특성을 극대화하기 위해, 요청의 성격에 따라 두 가지 모델을 구분하여 사용합니다.

```text
Request의 성격
   │
   ├─ 대상 엔티티가 아직 없거나, 완전히 독립적인 사건인가?
   │      └─ [모델 A] 독립 Request 엔티티 (IRequestComponent)
   │
   └─ 이미 존재하는 특정 엔티티의 상태 변경/요청인가?
          └─ [모델 B] IEnableableRequest (IEnableableComponent)
```

### [모델 A] 독립 Request 엔티티 (`IRequestComponent`)
* **개념**: 명령 하나마다 빈 Entity를 생성하고 Request 컴포넌트를 붙임.
* **적용 대상 (저빈도/독립 사건)**:
  * 건물 배치 요청 (`BuildCommand`)
  * 건물 철거 요청 (`DemolishCommand`)
  * 월드 아이템 스폰 요청 (`SpawnItemRequest`)
  * 드론 태스크 생성 요청 (`CreateDroneTaskRequest`)
* **장점**: 기존 엔티티의 컴포넌트 구조(Archetype)를 변경하지 않음.
* **소비 패턴**: Consumer가 처리 후 `ecb.DestroyEntity(requestEntity)`로 즉시 파괴.

### [모델 B] 대상 부착형 Enableable 컴포넌트 (`IEnableableRequest`)
* **개념**: 대상 엔티티(예: Item, Drone)에 컴포넌트를 미리 붙여두고, 평소에는 비활성화(`false`) 상태로 두었다가 요청 시 활성화(`true`)함.
* **적용 대상 (고빈도/기존 엔티티 상태 변경)**:
  * 아이템 소유권 이전 요청 (`TransferOwnershipRequest`)
  * 컨베이어 벨트 아이템 이동 요청 (`MoveItemRequest`)
  * 드론 작업 취소 요청 (`CancelDroneTaskRequest`)
* **장점**: **엔티티 생성/삭제 및 아키타입 변경이 전혀 발생하지 않음 (Structural Change 0)**. 대규모 아이템(수천~수만 개) 환경에서도 프레임 드랍 없이 극도로 빠름.
* **소비 패턴**: Consumer가 처리 후 `SystemAPI.SetComponentEnabled<T>(entity, false)`로 즉시 비활성화.

---

## 3. 8대 필수 메타데이터 주석 규약

모든 Request 컴포넌트 선언 상단에는 다음 **8대 항목**을 의무적으로 명시해야 합니다:

```csharp
/// <summary>
/// [1. 역할]            : 요청의 목적 및 수행할 작업 한 줄 요약
/// [2. Producer (생성자)] : 이 요청을 발생시키는 시스템 이름
/// [3. Consumer (소비자)] : 이 요청을 받아 실제 상태를 변경하는 소유자(State Owner) 시스템
/// [4. Create Phase]    : 요청이 생성되는 Phase (CommandGroup 또는 DecisionGroup)
/// [5. Consume Phase]   : 요청이 소비/적용되는 Phase (주로 StateApplyGroup)
/// [6. 수명주기 원칙]    : Consume-on-Apply (즉시 Destroy 또는 Disable)
/// [7. 실패 정책]        : 처리 불가(재료 부족, 대상 무효 등) 시의 대응 정책 (Drop, Retry 등)
/// [8. 안전망 정책]      : 비정상 고아 상태 발생 시 SynchronizationGroup 처리 방침
/// </summary>
```

---

## 4. 코드 구현 템플릿 예시

### 4.1 Request 컴포넌트 정의 (예: 소유권 이전)

```csharp
using Unity.Entities;

/// <summary>
/// [1. 역할]            : 벨트 상의 아이템을 창고 내부로 수납하기 위한 소유권 이전 요청
/// [2. Producer (생성자)] : StorageInputDecisionSystem
/// [3. Consumer (소비자)] : ItemOwnershipApplySystem
/// [4. Create Phase]    : DecisionGroup
/// [5. Consume Phase]   : StateApplyGroup
/// [6. 수명주기 원칙]    : Consume-on-Apply (처리 즉시 컴포넌트 비활성화)
/// [7. 실패 정책]        : 대상 아이템 또는 창고 엔티티가 무효한 경우 무시하고 비활성화(Drop)
/// [8. 안전망 정책]      : 타임아웃된 요청은 WorldInvariantValidationSystem에서 감지
/// </summary>
public struct TransferOwnershipRequest : IEnableableRequest
{
    public Entity TargetItem;
    public Entity TargetOwner;
}
```

### 4.2 Producer 시스템 작성 패턴 (발행 단계)

```csharp
using Unity.Entities;

[UpdateInGroup(typeof(DecisionGroup))]
public partial class StorageInputDecisionSystem : SystemBase
{
    protected override void OnUpdate()
    {
        // 1. 입고 조건 판단 (창고에 여유 슬롯이 있고, 벨트 끝에 아이템이 도착했는가?)
        // ...
        
        // 2. 조건 만족 시 아이템 엔티티의 TransferOwnershipRequest 활성화 및 데이터 세팅
        // (모델 B: IEnableableRequest 패턴)
        SystemAPI.SetComponent(itemEntity, new TransferOwnershipRequest
        {
            TargetItem = itemEntity,
            TargetOwner = storageEntity
        });
        SystemAPI.SetComponentEnabled<TransferOwnershipRequest>(itemEntity, true);
    }
}
```

### 4.3 Consumer 시스템 작성 패턴 (소비 및 상태 반영 단계)

```csharp
using Unity.Entities;

[UpdateInGroup(typeof(StateApplyGroup))]
public partial class ItemOwnershipApplySystem : SystemBase
{
    protected override void OnUpdate()
    {
        // 1. 활성화된 Request만 쿼리하여 순회
        foreach (var (request, ownership, entity) in 
                 SystemAPI.Query<RefRO<TransferOwnershipRequest>, RefRW<ItemOwnership>>()
                          .WithEntityAccess())
        {
            Entity targetOwner = request.ValueRO.TargetOwner;

            // 2. 상태 무결성 검증 및 실제 소유권 반영 (State Owner 책임)
            if (SystemAPI.Exists(targetOwner))
            {
                ownership.ValueRW.Owner = targetOwner;
            }

            // 3. [Consume-on-Apply] 처리 완료 즉시 비활성화 (모델 B)
            SystemAPI.SetComponentEnabled<TransferOwnershipRequest>(entity, false);
        }
    }
}
```

---

## 5. Structural Change & ECB 사용 가이드

1. **단순 데이터 값 변경은 일반 컴포넌트 write 사용**:
   * 컴포넌트 값 변경에 불필요하게 ECB를 거치지 않습니다.
2. **Structural Change 발생 시에만 ECB 사용**:
   * `ecb.CreateEntity`, `ecb.DestroyEntity`, `ecb.AddComponent`, `ecb.RemoveComponent`
3. **독립 Entity(모델 A) 요청 파괴**:
   * Consumer 시스템은 프레임 끝 또는 해당 Phase 끝에 재생되는 `EndSimulationEntityCommandBufferSystem` 등을 통해 `ecb.DestroyEntity(requestEntity)`를 호출합니다.
4. **안전성 검증**:
   * `WorldInvariantValidationSystem`이 프레임 맨 마지막(`SynchronizationGroup`)에서 남아있는 활성 Request가 없는지 지속적으로 감시합니다.
