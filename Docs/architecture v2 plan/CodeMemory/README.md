# Architecture V2 코드 지도

기준: 2026-09-26 작업 트리의 `Assets/Scripts/**/*.cs` 54개와 `Assets/Editor/Tests/**/*.cs` 24개. Unity가 생성하는 `Library/`의 C# 및 패키지 코드는 범위에 포함하지 않는다. 이 문서는 현재 파일의 역할과 연결 관계를 기록한다. 계획의 완료 표시나 과거 테스트 결과를 현재 실행 증거로 취급하지 않는다. 파일별 경로와 역할은 [CSharpFileIndex.md](CSharpFileIndex.md)를 참조한다.

## 실행 경계

`GameSimulationGroup`은 Unity `SimulationSystemGroup`에 속한다. 그룹 속성으로 명시된 순서는 다음과 같다.

```text
InitializationSystemGroup: ItemConfigInitSystem, RecipeInitSystem
SimulationSystemGroup
  GameSimulationGroup
    CommandGroup → DecisionGroup → ReservationGroup → ExecutionGroup
      → StateApplyGroup → SynchronizationGroup
```

- `CommandGroup`: `ChangeCrafterRecipeRequest`를 처리하고 제작기의 레시피·재료/배출 버퍼·필터를 갱신한다.
- `DecisionGroup`: 벨트 이동량, 건물 입출고, 채굴, 제작 가능 여부를 결정 컴포넌트에 기록한다. `CrafterDecisionSystem`은 상태 변경을 `CrafterStateDecision`으로 넘긴다.
- `ReservationGroup`: 건물 입고의 슬롯 경합과 벨트 목적지 경합을 처리한다. `BeltDestinationReservationSystem`은 같은 목표 셀로 들어오는 본선 벨트 이동을 외부 출고보다 우선하고, 같은 부류 내에서는 `PlacementStamp`와 엔티티 인덱스를 사용한다.
- `ExecutionGroup`: 승인된 벨트 이동을 `BeltMovementState`·`GridPosition`·`LocalTransform`에 반영한다. 채굴/제작은 진행도를 누적하고 `ProductResult`를 기록한다. 제작 시작 시 재료 버퍼에서 아이템을 즉시 제거하고 `DestroyItemRequest`를 활성화한다.
- `StateApplyGroup`: 건물 입출고 버퍼와 소유권 요청, 제작기 상태, 아이템 생성/삭제를 반영한다. `BuildingItemStorageApplySystem`은 `ItemOwnershipApplySystem`보다 먼저 실행하도록 지정되어 있다. `EndStateApplyEntityCommandBufferSystem`은 그룹 마지막에 구조 변경을 재생한다.
- `SynchronizationGroup`: 벨트·건물·아이템·자원 공간 인덱스를 재구축한다. `WorldInvariantValidationSystem`은 개발 빌드/Editor에서 그룹 마지막에 검증한다.

같은 그룹 내 개별 시스템의 순서는 위에서 명시한 속성 외에 파일명 숫자나 이 문서의 나열 순서로 보장되지 않는다. 테스트 도우미가 개별 시스템을 직접 업데이트하는 경우와 실제 그룹 실행은 구분해야 한다.

## 상태 소유권과 읽기/쓰기 계약

| 데이터 | 원본/작성 위치 | 주요 소비자 |
| --- | --- | --- |
| `ItemOwnership.Owner` | 저장 아이템의 소유 건물, `Entity.Null`은 월드. `ItemOwnershipApplySystem`이 `TransferOwnershipRequest`를 소비해 변경 | `ItemSpatialSyncSystem`, 벨트/저장 결정, 불변식 검사 |
| `StoredItemElement` | 일반 저장 및 제작 재료 버퍼. 입출고 반영, 제작 시작, 레시피 변경 명령이 갱신 | 입고 예약, 일반 출고, 제작 결정, 불변식 검사 |
| `ProductItemElement` | 채굴기/제작기의 출력 대기 버퍼. `ItemLifecycleApplySystem`이 생산 결과를 추가하고 출고 반영이 제거 | 생산 가능량 판단, 생산품 출고, 불변식 검사 |
| `ProductResult` | Miner/Crafter Execution의 생산 의도. `ItemLifecycleApplySystem`이 실제 아이템 엔티티로 바꾸고 비움 | Item Lifecycle Apply |
| `BeltMovementState.Progress` | Execution의 타일 내 실제 진행도 | Decision, 입고 판단, 공간 검증 |
| `BeltMovementDecision.PlannedProgress` | Decision의 프레임 계획, Reservation이 경합 시 제한, Execution이 소비 | Reservation, Execution, 불변식 검사 |
| 공간 인덱스 4종 | `*SpatialSyncSystem`이 각 인덱스를 `ClearJob` 후 populate. 아이템 인덱스는 월드 아이템만 포함 | 각 Decision/Execution/Reservation 시스템의 조회 및 검증 |

`BeltSpatialIndex`, `BuildingSpatialIndex`, `ResourceSpatialIndex`는 셀당 하나의 값을 저장하고, `ItemSpatialIndex`는 여러 아이템을 저장한다. 각 인덱스는 별도 `*Fence`를 통해 reader/writer `JobHandle`을 연결한다. `Complete()`는 재할당·종료와 프레임 말의 메인 스레드 검증에서 호출된다. 공간 인덱스는 원본 ECS 컴포넌트로부터 만든 파생 상태이므로 구조 변경 직후의 조회 시점에 주의한다.

## 주요 흐름

1. `ItemConfigInitSystem`은 `StreamingAssets/ItemConfig.json` 또는 기본값을 `ItemRegistryBlob`으로 만든다. `RecipeInitSystem`은 `Resources/Config/CrafterRecipeConfig` 또는 기본 레시피를 `RecipeRegistryBlob`으로 만든다.
2. 월드 아이템 생성 요청과 Miner/Crafter의 `ProductResult`는 `ItemLifecycleApplySystem`에서 아이템 엔티티가 된다. 요청의 Storage/Product 목적지에 대상 버퍼가 없으면 아이템을 만들지 않고 요청을 소비한다.
3. `BeltMovementDecisionSystem`은 벨트 속도와 앞 아이템 간격으로 이동량을 계산한다. `BeltDestinationReservationSystem`은 같은 목표 셀에 대한 진입/출고 경합을 줄이고, `BeltMovementExecutionSystem`이 이동과 위치를 확정한다.
4. 벨트 끝 아이템의 입고는 `BuildingItemInputDecisionSystem` → `BuildingStorageInputReservationSystem` → `BuildingItemStorageApplySystem` → `ItemOwnershipApplySystem` 순서로 처리한다. 일반 저장품 및 생산품 출고는 서로 다른 Decision 시스템이 작성하고 공통 Apply 시스템이 처리한다.
5. `MinerDecisionSystem`은 footprint 아래의 자원과 출력 용량을 확인한다. `MinerExecutionSystem`은 자원량과 진행도를 갱신하고 결과를 기록한다. `CrafterDecisionSystem`은 재료와 모든 출력 슬롯을 검사하고, `CrafterExecutionSystem`은 재료 선소비·진행·결과 기록을 한다. `CrafterStateApplySystem`은 결정된 상태를 반영한다.
6. StateApply의 ECB 재생 후 공간 인덱스가 갱신된다. 개발용 `WorldInvariantValidationSystem`은 아이템·공간·요청·벨트 간격·저장 버퍼·결정 소비·자원 인덱스 정합성을 확인한다.

## 현재 구현 범위와 증거의 한계

- 현재 `Assets/Scripts`에는 아이템, 벨트, 건물 입출고, 채굴기, 제작기의 코어와 Splitter/Merger의 데이터/방향 유틸리티가 있다. `RoutingTransferDecision`을 생성해 실제 전송을 적용하는 런타임 시스템은 이 54개 파일에 없다. `BeltDestinationReservationSystem`은 이미 활성화된 라우팅 결정의 목적지 경합만 처리한다.
- 현재 C# 집합에는 건설, 전력, 드론, 연구, UI, 월드 로딩/렌더링 구현이 없다. `BuildingTypeEnum`에 종류가 정의되어 있는 사실은 해당 기능의 런타임 구현을 뜻하지 않는다. 후속 계획은 상위 `Architecture V2 Tasks.md`를 참조한다.
- `Assets/Editor/Tests`의 24개 파일은 Phase 1~6 코어와 일부 연결/불변식을 테스트하도록 작성되어 있다. 이번 작업은 소스 읽기와 문서화이며 Unity 컴파일, EditMode, Play Mode 테스트를 새로 실행하지 않았다. 테스트 메서드가 존재한다는 사실은 현재 통과 증거가 아니다.
