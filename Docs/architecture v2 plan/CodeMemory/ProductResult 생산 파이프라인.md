# ProductResult 기반 생산 파이프라인

작성 기준: Architecture V2  
적용 대상: Miner, Crafter  
관련 Phase: Decision → Execution → StateApply  
현재 검증 상태: EditMode 99 / 99 Pass

---

## 1. 목적

생산 건물의 생산 완료 결과를 `SpawnItemRequest Entity`로 우회하지 않고,
생산 건물 자신이 보유한 `DynamicBuffer<ProductResult>`에 기록한 뒤
같은 프레임의 `StateApply` 단계에서 실제 Item Entity로 변환한다.

핵심 목적은 다음과 같다.

- Execution 단계에서는 **논리적 생산 결과만 기록**한다.
- 실제 Item Entity 생성 책임은 `ItemLifecycleApplySystem`으로 통일한다.
- 생산 완료와 실제 `ProductItemElement` 반영 사이의 불필요한 1프레임 지연을 제거한다.
- Miner와 Crafter가 동일한 생산 결과 계약을 사용한다.
- 생산 수량이 여러 개인 경우에도 Request Entity를 여러 개 만들지 않는다.
- Architecture V2의 Phase 책임 분리를 유지한다.

---

## 2. 전체 흐름

```text
Decision
    ↓
생산 가능 여부 판정
    ↓
Execution
    ↓
DynamicBuffer<ProductResult>에 결과 기록
    ↓
StateApply
    ↓
ItemLifecycleApplySystem
    ↓
ProductResult 소비
    ↓
실제 Item Entity 생성
    ↓
ProductItemElement Append
    ↓
ProductResult Clear
```

생산 건물별로 표현하면:

```text
MinerExecutionSystem ──────┐
                           │
                           ├─> ProductResult Buffer
                           │
CrafterExecutionSystem ────┘
                                   ↓
                         ItemLifecycleApplySystem
                                   ↓
                         실제 Item Entity 생성
                                   ↓
                         ProductItemElement
```

---

## 3. ProductResult

파일:

```text
Assets/Scripts/Components/Buildings/ProductComponent.cs
```

현재 구조:

```csharp
[InternalBufferCapacity(4)]
public struct ProductResult : IBufferElementData
{
    public ItemTypeEnum ItemType;
    public int Count;
    public int SlotIndex;

    public ProductResult(ItemTypeEnum itemType, int count = 1, int slotIndex = 0)
    {
        ItemType = itemType;
        Count = count;
        SlotIndex = slotIndex;
    }
}
```

### 의미

`ProductResult`는 실제 Item Entity가 아니다.

다음 의미를 가진 **Execution 결과 데이터**다.

> 생산은 완료됐지만 아직 StateApply에서 실제 Item Entity로 반영되지 않은 생산 결과.

### 필드

#### ItemType

생산된 아이템 종류.

예:

```text
Iron_Ore
Iron
Copper
Stone
```

#### Count

해당 생산 결과의 수량.

예:

```text
ProductResult
ItemType = Iron
Count    = 5
```

이면 StateApply에서 Iron Item Entity를 5개 생성한다.

수량만큼 `ProductResult` Element를 여러 개 추가하지 않는다.

#### SlotIndex

생산 건물 내부의 출력 슬롯 의미를 전달한다.

현재 정책:

```text
Miner
Slot 0 = 채굴 생산물

Crafter
Slot 0 = Primary Output
Slot 1+ = Byproduct (레시피 부산물 및 레시피 변경 잔여 배출) 영역
```

레시피 변경으로 인한 Byproduct 배출은 현재 `ProductResult`를 사용하지 않고 기존 Item Entity를
`StoredItemElement → ProductItemElement`로 직접 이동시킨다.

---

## 4. Buffer 수명주기

생산 가능한 Entity는 `DynamicBuffer<ProductResult>`를 가진다.

정상적인 프레임에서 Buffer 수명주기는 다음과 같다.

```text
Frame 시작
ProductResult.Length == 0

        ↓

Execution
생산 완료

        ↓

ProductResult.Length > 0

        ↓

같은 Frame StateApply

        ↓

실제 Item 생성
ProductItemElement 추가

        ↓

ProductResult.Clear()

        ↓

Frame 종료
ProductResult.Length == 0
```

따라서 정상적인 Simulation 프레임 종료 후에는
생산 건물의 `ProductResult`가 비어 있어야 한다.

---

## 5. Miner 적용

관련 파일:

```text
Assets/Scripts/Systems/Buildings/MinerDecisionSystem.cs
Assets/Scripts/Systems/Buildings/MinerExecutionSystem.cs
```

---

### 5.1 Miner ProductBuffer 정책

Miner의 `ProductItemElement`는 **단일 ResourceType 1스택**으로 취급한다.

예:

```text
현재 ProductBuffer
Iron_Ore × 20

현재 채굴 대상
Copper_Ore
```

이면 Capacity가 남아 있어도 Copper 채굴을 시작하지 않는다.

기존 Iron_Ore가 모두 외부로 배출된 뒤에 Copper_Ore 채굴을 시작할 수 있다.

---

### 5.2 MinerDecisionSystem

Decision 단계에서 다음을 확인한다.

1. Footprint 아래에 유효 Resource가 존재하는가.
2. ProductBuffer가 현재 Resource와 같은 ItemType인가.
3. ProductBuffer 수량이 ItemRegistry의 MaxStack보다 작은가.
4. 미소비 `ProductResult`가 존재하지 않는가.

현재 핵심 조건:

```csharp
bool sameItemType = true;
for (int i = 0; i < productItems.Length; i++)
{
    if (productItems[i].ItemType != resourceType)
    {
        sameItemType = false;
        break;
    }
}

bool hasPendingProduction = productResults.Length > 0;

bool hasSpace =
    !hasPendingProduction &&
    sameItemType &&
    productItems.Length < maxStack;
```

`ProductResult.Length > 0`인 경우 추가 채굴을 차단한다.

정상 프레임에서는 StateApply가 같은 프레임에 Buffer를 비우므로,
이 조건은 주로 Phase 누락/지연/비정상 실행에 대한 안전장치다.

---

### 5.3 MinerExecutionSystem

기존 방식:

```text
채굴 완료
    ↓
SpawnItemRequest Entity 생성 예약
    ↓
ECB Playback
    ↓
다음 프레임 ItemLifecycleApply
```

현재 방식:

```text
채굴 완료
    ↓
ProductResult.Add()
    ↓
같은 프레임 StateApply
```

생산 완료 시:

```csharp
productResults.Add(
    new ProductResult(
        resNode.ResourceType,
        count: 1,
        slotIndex: 0));
```

Miner는 한 생산 완료당 현재 1개를 기록한다.

Resource가 유한 자원인 경우 이 시점에 `ResourceNode.Amount`를 감소시키며,
고갈 시 Resource Entity 파괴는 ECB를 통해 처리한다.

---

## 6. Crafter 적용

관련 파일:

```text
Assets/Scripts/Systems/Buildings/CrafterDecisionSystem.cs
Assets/Scripts/Systems/Buildings/CrafterExecutionSystem.cs
```

---

### 6.1 CrafterDecisionSystem

Crafter도 `ProductResult` Buffer를 읽는다.

StateApply에서 아직 소비되지 않은 결과가 존재하면
새로운 제작/출력을 차단한다.

현재 안전장치:

```csharp
if (productResults.Length > 0)
{
    decision.CanCraft = false;
    decision.CanStartCraft = false;
    decision.CanAdvance = false;
    decision.CanProduceOutput = false;

    stateDecision.NextStatus = CrafterStatusEnum.WaitingForOutput;
    stateDecisionEnabled.ValueRW = true;
    decisionEnabled.ValueRW = false;
    return;
}
```

정상 프레임에서는 같은 프레임 StateApply에서 결과가 소비되므로
다음 Decision에서는 Buffer가 비어 있어야 한다.

---

### 6.2 CrafterExecutionSystem

제작 완료 후 더 이상 `SpawnItemRequest` Entity를 만들지 않는다.

Primary Output:

```csharp
if (recipe.TryGetPrimaryOutput(out var primary) && primary.Amount > 0)
{
    productResults.Add(
        new ProductResult(
            primary.ItemType,
            primary.Amount,
            slotIndex: 0));
}
```

현재 Crafter는 다중 Output을 지원하며 `recipe.Outputs` 전체를 순회한다.

```csharp
for (int outIdx = 0; outIdx < recipe.Outputs.Length; outIdx++)
{
    ref var output = ref recipe.Outputs[outIdx];

    if (output.ItemType != ItemTypeEnum.None && output.Amount > 0)
    {
        productResults.Add(
            new ProductResult(
                output.ItemType,
                output.Amount,
                slotIndex: outIdx));
    }
}
```

Slot 계약:

```text
Slot 0   = Primary Output
Slot 1+  = Byproducts
```

예:

```text
Recipe Output

Iron × 3
Stone × 2
Copper × 1
```

Execution 결과:

```text
ProductResult[0]
ItemType  = Iron
Count     = 3
SlotIndex = 0

ProductResult[1]
ItemType  = Stone
Count     = 2
SlotIndex = 1

ProductResult[2]
ItemType  = Copper
Count     = 1
SlotIndex = 2
```

Execution에서는 실제 Item Entity를 생성하지 않는다.

---

## 7. ItemLifecycleApplySystem

관련 파일:

```text
Assets/Scripts/Systems/Item/ItemLifecycleApplySystem.cs
```

StateApply 단계에서 생산 결과를 실제 Item Entity로 변환하는 최종 책임자다.

현재 처리 순서:

```text
1. ProductResultApplyJob
2. SpawnItemApplyJob
3. DestroyItemApplyJob
```

---

### 7.1 ProductResult Query

```csharp
_productResultQuery = SystemAPI.QueryBuilder()
    .WithAllRW<ProductResult>()
    .WithAll<ProductItemElement>()
    .Build();
```

즉 다음 두 Buffer를 가진 Entity가 생산 결과 처리 대상이다.

```text
DynamicBuffer<ProductResult>
DynamicBuffer<ProductItemElement>
```

현재 Miner와 Crafter가 이 조건을 만족한다.

---

### 7.2 ProductResultApplyJob

각 `ProductResult`에 대해 `Count` 수만큼 실제 Item Entity를 만든다.

개념:

```text
ProductResult
ItemType = Iron
Count = 3
Slot = 0

        ↓

Item Entity A
Item Entity B
Item Entity C

        ↓

ProductItemElement A
ProductItemElement B
ProductItemElement C
```

생성되는 Item의 기본 상태:

```text
ItemIdentity       = ProductResult.ItemType
ItemOwnership      = Stored(producerEntity)
GridPosition       = (0, 0)
LocalTransform     = zero

DestroyItemRequest       Disabled
TransferOwnershipRequest Disabled
BeltMovementState        Disabled
BeltMovementDecision     Disabled
BuildingItemInputDecision Disabled
```

각 Item은 생산 건물의 ProductBuffer에 추가된다.

```csharp
ECB.AppendToBuffer(
    producerEntity,
    new ProductItemElement(
        newItem,
        result.ItemType,
        result.SlotIndex));
```

모든 결과 기록이 끝난 뒤:

```csharp
productResults.Clear();
```

를 수행한다.

이 규칙이 `ProductResult`의 Consume-on-Apply 계약이다.

---

## 8. ProductResult와 SpawnItemRequest의 차이

두 구조의 책임은 다르다.

### ProductResult

생산 건물이 반복적으로 만들어내는 생산 결과용.

특징:

- 생산 Entity에 항상 존재하는 DynamicBuffer.
- Execution → StateApply 전달용.
- 같은 프레임 소비가 기본 계약.
- 복수 생산량을 `Count` 하나로 표현 가능.
- Miner/Crafter 공통 생산 계약.

사용 예:

```text
Miner 채굴 완료
Crafter 제작 완료
```

### SpawnItemRequest

독립적인 Item 생성 명령이 필요한 경우 사용한다.

특징:

- 독립 Request Entity.
- 특정 생산 건물에 종속되지 않는 Spawn 명령에 적합.
- 현재 ItemLifecycleApplySystem에서 계속 지원한다.

따라서 `ProductResult` 도입은
`SpawnItemRequest` 전체 폐기를 의미하지 않는다.

---

## 9. Architecture V2 관점의 책임

현재 생산 파이프라인의 책임은 다음처럼 나뉜다.

### Decision

질문:

> 이번 프레임에 생산을 진행하거나 완료된 결과를 배출할 수 있는가?

예:

- MinerDecisionSystem
- CrafterDecisionSystem

실제 Item Entity를 만들지 않는다.

### Execution

질문:

> Decision이 허용한 작업을 실행했을 때 어떤 생산 결과가 발생했는가?

결과:

```text
ProductResult
```

실제 Item Entity를 만들지 않는다.

### StateApply

질문:

> Execution이 만든 논리적 생산 결과를 실제 ECS 상태로 어떻게 반영할 것인가?

처리:

```text
ProductResult
    ↓
Item Entity
    ↓
ItemOwnership
    ↓
ProductItemElement
```

담당:

```text
ItemLifecycleApplySystem
```

---

## 10. 핵심 Invariant

### Invariant 1

정상 프레임 종료 후 생산 건물의 `ProductResult`는 비어 있어야 한다.

```text
ProductResult.Length == 0
```

### Invariant 2

`ProductResult`가 남아 있는 동안 같은 생산 건물은 새로운 생산 결과를 추가하지 않는다.

Miner:

```text
productResults.Length > 0
→ 채굴 차단
```

Crafter:

```text
productResults.Length > 0
→ 새 제작/출력 차단
```

### Invariant 3

실제 생산 Item Entity 생성은 생산 시스템이 직접 하지 않는다.

```text
MinerExecutionSystem     X Item Entity 생성
CrafterExecutionSystem   X Item Entity 생성

ItemLifecycleApplySystem O Item Entity 생성
```

### Invariant 4

`ProductItemElement`는 실제 Item Entity가 생성되는 StateApply에서 추가한다.

Execution에서는 직접 ProductBuffer에 새 Item Entity를 넣지 않는다.

---

## 11. 현재 테스트

ProductResult 관련 핵심 테스트:

### Miner

`Phase4MinerPipelineTests`

검증 내용:

- Execution 후 ProductResult가 기록되는지.
- StateApply 전 ProductBuffer가 변경되지 않는지.
- StateApply 후 ProductResult가 소비되는지.
- ProductBuffer 49개 상태에서 생산 후 정확히 50개가 되는지.
- 동일 결과가 중복 소비되지 않는지.
- MaxStack 도달 후 다음 생산이 차단되는지.
- 다른 ResourceType이 ProductBuffer에 남아 있으면 채굴이 차단되는지.

### Crafter

`Phase5CrafterExecutionTests`

검증 내용:

- 제작 완료 후 Execution에서 ProductResult가 기록되는지.
- StateApply 전 ProductBuffer가 변경되지 않는지.
- StateApply에서 ProductResult가 소비되는지.
- Primary Output이 올바른 SlotIndex로 ProductBuffer에 생성되는지.
- 기존 Backpressure 로직이 유지되는지.

### 전체 회귀 검증

현재 EditMode 테스트:

```text
Total  : 99
Passed : 99
Failed : 0
Skipped: 0
```

---

## 12. 현재 제약

### Crafter 다중 Byproduct

다중 Byproduct 지원은 완료되었다.

현재 계약:

```text
Primary      → Slot 0
Byproduct 1  → Slot 1
Byproduct 2  → Slot 2
...
```

Decision 단계에서는 모든 Output Slot의 Capacity를 확인하며,
하나라도 공간이 부족하면 All-or-Nothing 정책으로 전체 배출을 대기한다.

Execution 단계에서는 `recipe.Outputs` 전체를 순회하여
각 Output별 `ProductResult`를 기록한다.

따라서 현재 ProductResult 파이프라인 자체에는 다중 부산물 관련 미해결 제약이 없다.

---

## 13. 확장 규칙

새 생산 건물을 추가할 때 다음 계약을 따른다.

### 생산 Entity 필수 Buffer

```text
DynamicBuffer<ProductItemElement>
DynamicBuffer<ProductResult>
```

### Decision

- 생산 가능 여부 계산.
- ProductBuffer Capacity 계산.
- 미소비 ProductResult가 있으면 추가 생산 차단.

### Execution

생산 완료 시:

```csharp
productResults.Add(
    new ProductResult(
        itemType,
        count,
        slotIndex));
```

실제 Item Entity는 생성하지 않는다.

### StateApply

별도 생산 건물용 Item 생성 시스템을 만들지 않는다.

공통:

```text
ItemLifecycleApplySystem
    ↓
ProductResultApplyJob
```

을 사용한다.

---

## 14. 현재 설계 요약

```text
[Producer Domain]

Miner
Crafter
Future Producer
      │
      │ Execution
      ▼
DynamicBuffer<ProductResult>
      │
      │ StateApply
      ▼
ItemLifecycleApplySystem
      │
      ├─ Item Entity Create
      ├─ ItemIdentity
      ├─ ItemOwnership.Stored
      └─ ProductItemElement Append
      │
      ▼
ProductResult.Clear()
```

핵심 원칙:

> 생산 시스템은 "무엇이 몇 개 생산되었는가"만 기록한다.

> 실제 Item Entity를 생성하고 생산 건물의 ProductBuffer에 반영하는 책임은 StateApply가 가진다.
