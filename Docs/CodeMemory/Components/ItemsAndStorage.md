# 아이템·보관 컴포넌트

[전체 색인](README.md) · 최초 조사 2026-10-03 · 2026-10-06 도메인 분리·드론 인계 계약 대조

현재 `Assets/Scripts`의 선언, 생성 코드, Job 쿼리와 실제 읽기·쓰기 경로를 대조한 정적 분석이다. 이번 문서 작업에서는 Unity 컴파일, EditMode/Play Mode, 장면·프리팹 실행을 수행하지 않았다. 테스트 파일은 요청을 만드는 사례의 근거이며 실행 성공의 근거가 아니다. enum, `FixedBitSet` 같은 비 ECS 보조 값은 별도 컴포넌트로 세지 않는다.

2026-10-04 기존 공사 운송·등록·수령·예약 처리와 Transfer 처리 표시를 제거했다. 2026-10-05 드론 인계의 보관 버퍼·위치·벨트 상태·소유권·렌더 전환은 기존 ItemOwnershipApplySystem.TryTransferItem 공통 API로 통합했다. DroneTaskLifecycleApplySystem은 실제 성공 수량으로 현장 수량·배정·결과를 정산하며 새 TransferOwnershipRequest를 발행하지 않는다. 실제 드론 이동·행동 신호 생성은 후속이다. 소유자 통합 당시 근거는 [소유자 통합 검증](../../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/DroneTransferOwnerConsolidation-Verification.md), 과거 제거의 실행 근거는 [공사 운송 제거 기록](../../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/ConstructionTransportRemoval-Verification.md)에 보존한다.

일반 입고는 `BuildingItemInputDecisionSystem` → `BuildingStorageInputReservationSystem` → `BuildingItemStorageApplySystem` → `ItemOwnershipApplySystem`을 거친다. 일반 출고는 Storage/Product 두 Decision 중 해당 버퍼를 가진 경로 → `BeltDestinationReservationSystem` → Storage Apply → Ownership Apply다. Storage Apply 내부에서는 입고 Job 뒤 출고 Job을 연결한다. 보관 버퍼·이동 상태는 직접 바꾸고 렌더 태그 구조 변경은 EndBuilding에서 재생하므로, 버퍼·Owner·렌더 상태의 최종 일치는 그 이후에 본다.

공사 취소와 건물 철거는 별도 수명주기 경로에서 Owner와 렌더 태그도 ECB로 변경한다. Storage/Routing Apply는 일반 Ownership과 Building Lifecycle보다 먼저 실행한다. Construction Lifecycle은 BuildingStateApply 마지막에서 완공을 검사한다. EndBuilding 뒤 별도 드론 그룹이 실행되며 공유 실물 API의 드론 렌더 기록은 EndSimulation에서 확정된다. Editor/Development의 `WorldInvariantValidationSystem`은 Synchronization 마지막에 소유권·버퍼·공간·미소비 요청 등을 진단한다. [일반 반영](../../../Assets/Scripts/Systems/Buildings/StateApply/BuildingItemStorageApplySystem.cs), [공사](../../../Assets/Scripts/Systems/Construction/StateApply/ConstructionLifecycleApplySystem.cs), [철거](../../../Assets/Scripts/Systems/Buildings/StateApply/BuildingLifecycleApplySystem.cs), [진단](../../../Assets/Scripts/Validation/WorldInvariantValidationSystem.cs).

2026-10-04 철거 승인 상태와 앞단 동작 중단 계약을 반영했다. 아래 현재 설명의 컴파일·핵심 회귀 실행 근거는 [철거 상태·동작 중단 검증](../../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/BuildingDemolitionStop-Verification.md)을 따른다. 이전 조사 당시의 정적 분석과 이번 실행 검증의 범위를 구분한다.

## ItemIdentity

드론 인계는 건물 종료 상태로 확정한 실물 ID 목록과 공간 계획을 사용한다. 같은 틱 건물의 새 입고·생산·철거 반환/환급과 출고 공간은 입력으로 보이나 기존 공급원·품목 제한은 유지한다. 드론 처리 중 새 수집품·새 공간을 기존 계획에 더하지 않는다. 현재 소유권·생존·Destroy·공간은 실제 반영 전에 다시 검사한다. 계획은 파생 데이터이고 ItemOwnership/StoredItemElement는 원본이다. [현재 경계](../../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/BuildingDroneDomainSplit-Verification.md)를 따른다.

- **종류·부착 대상:** 일반 `IComponentData`. 아이템 프리팹과 그 인스턴스인 개별 실물 아이템 엔티티에 붙는다. `Type`은 `ItemTypeEnum` 품목이며 수량 필드는 없다. 실물 하나가 엔티티 하나다.
- **생성:** `ItemAuthoringBaker`가 프리팹에 타입을 베이킹한다. 런타임 `ItemLifecycleUtility.SpawnPrefabItem`은 프리팹을 인스턴스화하고 요청/생산 결과의 타입으로 값을 설정한다. Item Lifecycle의 일반 스폰·생산 결과와 Building Lifecycle의 철거 비용 환급이 이 유틸리티를 사용한다.
- **Reader:** Initialization의 `PrefabDatabaseInitializationSystem`이 DB 품목과 프리팹 타입 일치를 검사한다. Decision의 `BuildingItemInputDecisionSystem`, Reservation의 `BuildingStorageInputReservationSystem`, StateApply의 `BuildingItemStorageApplySystem`은 입고 품목을 읽는다. 드론 인계는 ItemOwnershipApplySystem 공통 API가 실제 ItemIdentity·소유자·소유 버퍼를 검사한다.
- **Writer·처리:** 공통 스폰의 `SetComponent`는 호출자의 EndBuilding ECB에 기록된다. 확인한 정상 이동·수납·제작 재료 소비 경로는 같은 실물의 품목을 바꾸지 않는다. `ItemSpatialSyncSystem`은 `ItemIdentity`가 있는 아이템을 쿼리하되 실제 맵 등록에는 위치·소유권을 읽는다.
- **수명·결합:** 월드/보관 전환 뒤에도 유지되고 실물 삭제 시 사라진다. `StoredItemElement.ItemType`, `ProductItemElement.ItemType`과 일치해야 하며 진단 시스템이 보관 버퍼의 타입 불일치를 검사한다.
- **근거:** [선언](../../../Assets/Scripts/Components/Items/ItemComponents.cs), [Baker](../../../Assets/Scripts/Authoring/ItemAuthoring.cs), [공통 생성](../../../Assets/Scripts/Common/ItemLifecycleUtility.cs), [DB 검사](../../../Assets/Scripts/Systems/Initialization/PrefabDatabaseInitializationSystem.cs), [수명주기](../../../Assets/Scripts/Systems/Items/StateApply/ItemLifecycleApplySystem.cs).

## ItemOwnership

- **종류·부착 대상:** 아이템 엔티티의 일반 `IComponentData`. `Owner == Entity.Null`은 월드 아이템, 그 외는 해당 엔티티에 수납된 실물이다. `IsWorldItem`/`IsStored`는 이 값에서 계산한다.
- **생성:** 공통 아이템 스폰이 목적지에 따라 `WorldItem` 또는 `Stored(owner)`를 추가한다. 보관 생성에는 `DisableRendering`도 함께 기록한다. 생산품은 생산 건물 소유, 철거 비용 환급은 월드 소유로 시작한다.
- **Reader:** Decision의 벨트 이동·건물 입고·두 출고·Splitter/Merger와 Reservation의 벨트 목적지 검사가 월드 점유를 판정한다. Execution의 벨트 이동은 월드 소유만 이동시킨다. Synchronization의 `ItemSpatialSyncSystem`은 월드 소유만 맵에 등록한다.
- **Writer·순서:** 일반 입출고 뒤 `ItemOwnershipApplySystem`이 직접 Owner를 바꾸며 렌더 태그는 EndBuilding ECB에 기록한다. 이후 드론 Lifecycle이 같은 소유자의 `TryTransferItem`을 호출해 실물 버퍼·Owner·좌표·벨트 정지를 즉시 반영하고 렌더 구조 변경은 호출자가 전달한 EndSimulation ECB에 기록한다. 공사 취소는 `ConstructionCancelCommandSystem`이 Owner·렌더·좌표를 EndCommand ECB에 기록하고, 건물 철거 반환은 `BuildingLifecycleApplySystem`이 EndBuilding ECB에 기록한다. 따라서 Ownership Apply만 유일한 Writer라고 해석하지 않는다.
- **수명·충돌:** 아이템과 함께 존속한다. 철거 승인 시 Command가 대상 Stored/Product 버퍼에 남은 실물의 이전 Transfer 요청을 비활성화한다. 이후 입고·출고 Decision은 철거 대상을 제외하므로 새 Transfer를 만들지 않는다. ItemOwnershipApplySystem은 철거 요청/상태를 조회하지 않으며 활성 Destroy는 기존처럼 요청만 소비한다.
- **결합·근거:** Owner 변경 요청 자체는 소유 버퍼를 옮기지 않는다. Stored/Product 참조, 월드 공간 등록, `DisableRendering`, 벨트 활성 상태를 각 경로가 함께 유지한다. [선언](../../../Assets/Scripts/Components/Items/ItemComponents.cs), [소유권 반영](../../../Assets/Scripts/Systems/Items/StateApply/ItemOwnershipApplySystem.cs), [현장 취소](../../../Assets/Scripts/Systems/Command/ConstructionCancelCommandSystem.cs), [철거](../../../Assets/Scripts/Systems/Buildings/StateApply/BuildingLifecycleApplySystem.cs), [공간 등록](../../../Assets/Scripts/Systems/Synchronization/ItemSpatialSyncSystem.cs).

### 공통 실물 API의 책임

`ItemOwnershipApplySystem.TryTransferItem`은 한 실물의 품목·생존·활성 Destroy와 실제 출발 Owner, 출발 Stored 참조의 유일성, 도착 Stored 버퍼·슬롯 번호·중복 참조를 변경 전에 검사한다. 성공하면 동일 엔티티의 출발/도착 Stored 참조, Owner, GridPosition/표시 위치, 벨트 정지를 함께 반영하고 남은 일반 Transfer 요청을 비활성화한다. Product 버퍼에서의 이동이나 목적지 용량·필터·공사 요구량·배정 정책을 직접 결정하는 API는 아니다.

품목·목적지·슬롯·허용 수량 선택과 성공분의 드론 예약·도착량·배정·결과 정산은 호출자가 담당한다. `CanTransferItem`/`EffectiveOwner`는 활성 Transfer의 예정 Owner를 포함한 조회이지만 `TryTransferItem`은 실제 Owner가 출발 Owner와 일치해야 성공한다. 렌더 태그의 ECB도 호출자가 제공하며 드론은 EndSimulation을 사용한다. 일반 요청 Job은 EndBuilding을 사용한다. [공통 API](../../../Assets/Scripts/Systems/Items/StateApply/ItemOwnershipApplySystem.cs), [드론 호출·정산](../../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskLifecycleApplySystem.cs).

ECB 재생으로 구조가 변경되는 경계 전후에 기존 DynamicBuffer 참조를 계속 사용하지 않는다. 복사가 필요한 경우 임시 NativeArray로 보존하고, 구조 변경 뒤에는 EntityManager/Lookup에서 버퍼를 다시 얻는다. 이 메모리 수명 규칙은 실물의 Owner·버퍼 정합성 계약과 별개로 유지한다.

## ItemRegistry

- **종류·부착 대상:** 설정 엔티티 하나에 붙는 데이터 없는 `IComponentData` 태그다. 같은 엔티티의 `ItemConfigElement` 버퍼를 식별하며 공통 기본값을 보관하지 않는다. 개별 아이템이나 창고에 붙지 않는다.
- **생성:** Initialization의 managed `ItemConfigInitSystem`이 `StreamingAssets/ItemConfig.json`에 실제 모든 품목의 명시적 양수 MaxStack이 있는지 검증한 뒤 태그+버퍼를 한 번 게시한다. 파일 누락·읽기/파싱 실패·무효 값·품목 누락·무효 이름·None 항목·중복은 로그·설정 미게시·즉시 SimulationFatalError다. `InitializeItemRegistry`로 사전 등록할 수 있으며 기존 Registry가 있으면 입력 처리 전에 예외로 거부한다.
- **Reader:** Command의 `CrafterRecipeCommandSystem`/`BuildingInputSlotUtility`가 입력 슬롯 수를 계산한다. Decision의 `MinerDecisionSystem`/`CrafterDecisionSystem`은 생산품 스택 한도를, Reservation의 `BuildingStorageInputReservationSystem`은 입고 슬롯 스택 한도를 읽는다. 개발용 불변식 진단도 설정을 참조한다.
- **처리 방법:** 정적 `ItemRegistry.GetMaxStack`은 품목 enum 값을 원본 버퍼 인덱스로 사용한다. None·범위 밖·인덱스의 품목 불일치는 용량 0이며 정상 기본값으로 대체하지 않는다. 태그의 값 조회 API를 호출하지 않고 singleton 엔티티와 읽기 전용 버퍼로 접근한다. 생산·예약·드론·진단도 설정 부재를 50으로 보충하지 않는다.
- **수명·결합:** 자동 Init은 기존 Registry의 전체 버퍼·품목 번호·실제 품목의 양수 값·내부 None/0을 확인한다. 실패해도 사전 등록 원본을 삭제/보충하지 않고 Fatal을 게시한다. 초기화 실패나 기존 Fatal 뒤 비활성화하며 자동 재시도하지 않는다. 설정은 World 수명 동안 읽기 전용이며 Init 제거와 함께 삭제/Dispose하지 않는다. 게시 실패 시 이번 호출의 미완성 엔티티만 회수한다.
- **근거:** [선언·조회](../../../Assets/Scripts/Components/Items/ItemConfigComponents.cs), [게시](../../../Assets/Scripts/Systems/Initialization/ItemConfigInitSystem.cs), [입력 구성](../../../Assets/Scripts/Common/BuildingInputSlotUtility.cs), [채굴 판단](../../../Assets/Scripts/Systems/Buildings/Decision/MinerDecisionSystem.cs), [제작 판단](../../../Assets/Scripts/Systems/Buildings/Decision/CrafterDecisionSystem.cs), [입고 예약](../../../Assets/Scripts/Systems/Buildings/Reservation/BuildingStorageInputReservationSystem.cs).

## ItemConfigElement

- **종류·부착 대상:** `IBufferElementData`, `InternalBufferCapacity(0)`. `ItemRegistry` 엔티티의 동적 버퍼이며 원소는 `ItemType`, `MaxStack`을 가진다. `MaxStack`은 해당 품목 한 슬롯의 실물 개수 한도다.
- **생성:** `ItemConfigInitSystem.InitializeItemRegistry`가 실제 모든 품목의 JSON 값을 검증하고 숫자 순으로 품목 번호와 같은 위치에 넣는다. 설정에 없는 품목의 값을 채우지 않는다. JSON의 None은 거부하며 버퍼 0번의 내부 None/0만 인덱스 대응을 위해 만든다. 생성·버퍼 채우기는 Initialization의 직접 EntityManager 작업이다.
- **Reader:** `CrafterRecipeCommandSystem`의 입력 슬롯 산정, `MinerDecisionSystem`/`CrafterDecisionSystem`의 출력 여유 판단, `BuildingStorageInputReservationSystem`의 입고 예약, `WorldInvariantValidationSystem`의 용량 진단에서 읽는다. 실행 중 정상 Writer는 없다.
- **현재 명시 설정:** 광석·석탄·돌은 50, 철·구리와 각 막대는 100, Drone은 1이다. 코드의 품목별 switch·공통 DefaultMaxStack·생략값 보충은 제거했다. 실제 품목의 0·음수·값 누락은 오류이며 사용 금지 기능으로 해석하지 않는다. 슬롯 계산 유틸리티와 일반 입고 예약은 사용하는 품목의 양수 한도를 확인한다.
- **수명·결합:** Registry와 함께 World가 소유하며 게시 후 Clear/재로드/재등록 경로를 제공하지 않는다. 버퍼 인덱스와 `ItemType`의 대응은 입력 슬롯 계산이 검증하는 계약이며, `Storage.SlotCount`와는 다른 제한이다.
- **근거:** [선언](../../../Assets/Scripts/Components/Items/ItemConfigComponents.cs), [파싱·게시](../../../Assets/Scripts/Systems/Initialization/ItemConfigInitSystem.cs), [슬롯 유효성](../../../Assets/Scripts/Common/BuildingInputSlotUtility.cs), [예약](../../../Assets/Scripts/Systems/Buildings/Reservation/BuildingStorageInputReservationSystem.cs).

## SpawnItemRequest

현장 내부의 새 월드 아이템은 Decision과 StateApply에서 ConstructionSiteWorldItemUtility로 거부한다. 완공은 현재 월드 Owner/GridPosition과 활성 Destroy만 조회하며 예정 위치 기록 버퍼는 제거했다. 현재 근거는 [안전 방출·기록 제거 검증](../../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/DroneSafeDropAndJournalRemoval-Verification.md)을 따른다.

- **종류·부착 대상:** IRequestComponent/IEnableableComponent. 새 실물과 별도의 일회성 요청 엔티티에 부착한다. ItemType/Position/Destination/TargetOwner/TargetSlotIndex가 생성을 지시한다. 기본 활성은 생성 후보이며 사전 거부는 비활성화한 뒤 삭제한다. 재사용하지 않는다.
- **Producer·계약:** 현재 Assets/Scripts에는 실제 발행 호출자가 없고 Editor 테스트가 생성한다. Miner/Crafter는 ProductResult를 사용한다. Storage/Product 요청은 ItemSpawnAdmissionDecisionSystem 실행 전 실체화하며 이후 발행하는 향후 Producer는 철거 상태를 확인해 요청 생성을 막아야 한다. EndCommand에 생성된 요청도 같은 틱 Decision에서 검사한다.
- **Decision·거부:** Admission은 철거 예정 Storage/Product 대상 또는 현재 활성·비취소 현장의 회전 footprint 내부 World 목적지이면 즉시 요청을 비활성화하고 EndBuilding에 삭제를 기록한다. 비활성 요청은 Item Lifecycle 생성 쿼리에서 제외한다. World 생성은 Item Lifecycle의 최종 Apply에서도 현재 현장 내부를 다시 검사한다.
- **StateApply·적용:** 정상 활성 요청만 ItemLifecycleApplySystem이 처리한다. ProductResult→Spawn→Destroy 순서는 유지한다. 대상 버퍼와 프리팹을 확인하고 아이템·소유권·버퍼 참조를 EndBuilding에 기록한다. 일반 벨트 입고/슬롯 예약과 별도 경로이며 철거 요청/상태를 조회하지 않는다.
- **소비·검증:** 승인/거부 요청 모두 EndBuilding에서 물리적으로 삭제한다. 비활성 요청의 실제 삭제는 IgnoreComponentEnabledState 쿼리로 확인한다. 프리팹 DB/항목 누락의 중단 정책과 일반 실패 Drop 정책은 유지한다.
- **근거:** [정의](../../../Assets/Scripts/Components/Items/ItemRequests.cs), [사전 입고 검사](../../../Assets/Scripts/Systems/Items/Decision/ItemSpawnAdmissionDecisionSystem.cs), [일반 생성 적용](../../../Assets/Scripts/Systems/Items/StateApply/ItemLifecycleApplySystem.cs), [핵심 생성·철거 회귀](../../../Assets/Editor/Tests/Phase7ItemCreationDemolitionTests.cs).

## DestroyItemRequest

- **종류·부착 대상:** `IEnableableRequest`를 구현한 데이터 없는 요청. 독립 요청 엔티티가 아니라 삭제할 실물 아이템에 붙으며 활성 상태 자체가 삭제 의도다.
- **초기화·Producer:** 공통 스폰이 미리 부착하고 비활성화한다. `CrafterExecutionSystem`은 새 제작 착수 시 재료를 `StoredItemElement`에서 먼저 제거하고 해당 아이템의 요청을 활성화한다.
- **Consumer·단계:** StateApply의 `ItemLifecycleApplySystem`은 활성 요청이 붙은 엔티티를 쿼리하여 EndBuilding ECB의 `DestroyEntity`를 기록한다. 요청만 끄는 재사용 소비가 아니라 대상 아이템과 요청이 함께 소멸한다.
- **Reader·충돌:** `BuildingItemStorageApplySystem`은 해당 실물 입출고를 거부하고, `ItemOwnershipApplySystem`은 Transfer만 비활성화한다. Construction Cancel Command의 취소 반환과 Building Lifecycle의 철거 반환·벨트 정리도 활성 Destroy를 확인한다. 일반 Routing Apply는 이 요청을 조회하지 않는다.
- **결합·경계:** 삭제 Producer가 소유 버퍼의 참조를 먼저 제거해야 한다. Lifecycle Destroy Job은 소유 버퍼를 검색해 정리하지 않는다. 공사 완공은 이 요청을 활성화하지 않고 현장 보관 자재와 현장을 같은 EndBuilding ECB에서 직접 삭제하는 별도 경로다.
- **근거:** [선언](../../../Assets/Scripts/Components/Items/ItemRequests.cs), [초기화](../../../Assets/Scripts/Common/ItemLifecycleUtility.cs), [제작 선소비](../../../Assets/Scripts/Systems/Buildings/Execution/CrafterExecutionSystem.cs), [삭제 Consumer](../../../Assets/Scripts/Systems/Items/StateApply/ItemLifecycleApplySystem.cs), [공사 삭제](../../../Assets/Scripts/Systems/Construction/StateApply/ConstructionLifecycleApplySystem.cs), [입출고 방어](../../../Assets/Scripts/Systems/Buildings/StateApply/BuildingItemStorageApplySystem.cs).

## TransferOwnershipRequest

- **종류·부착 대상:** 아이템에 붙는 `IEnableableRequest`. 유일한 데이터 필드 `TargetOwner`는 새 소유자이며 Null이면 월드 반환이다. 별도의 같은 틱 처리 표시 필드는 없다.
- **초기화·Producer:** 공통 스폰이 비활성 요청을 미리 붙인다. BuildingStateApply의 BuildingItemStorageApplySystem이 버퍼·위치·벨트 상태를 먼저 바꾸고 새 TargetOwner와 활성 상태를 기록한다. RoutingApplySystem은 월드 아이템의 위치·벨트 상태만 변경하며 Transfer 요청을 만들지 않는다. 드론의 공통 실물 API는 기존 요청 적용 이후 실제 Owner·버퍼를 함께 변경하며 요청을 새로 발행하지 않는다. 남은 활성 요청은 소비하여 기존 Consumer가 재적용하지 않게 한다.
- **Consumer:** `ItemOwnershipApplySystem`은 TargetOwner가 Null이면 월드 소유, 존재하는 엔티티면 보관 소유로 즉시 갱신한다. 소유권 종류가 바뀔 때 `DisableRendering` 추가/제거를 EndBuilding ECB에 기록한다. 존재하지 않는 소유자는 Owner를 유지하고 요청만 소비한다.
- **소비·재사용:** 일반 처리와 활성 Destroy는 요청을 즉시 비활성화한다. 철거 대상 보관 실물의 이전 Transfer는 Command가 먼저 비활성화하며 Ownership Consumer가 철거 상태를 다시 조회하지 않는다. 요청 데이터를 초기화하는 별도 ECB는 기록하지 않는다. 다음 입출고 Producer가 새 TargetOwner를 쓰고 다시 활성화한다.
- **결합·철거:** 철거 버퍼에 남은 실물은 Command에서 이전 Transfer를 차단한 뒤 Building Lifecycle이 반환한다. 활성 Destroy 실물도 소유권을 덮어쓰지 않는다. 일반 요청은 보관 버퍼나 위치를 스스로 옮기지 않는다. 드론 공급의 버퍼·Owner는 같은 ItemOwnership 소유자의 공통 API가 함께 반영하고 도착량·예약은 DroneTaskLifecycleApplySystem이 정산한다.
- **근거:** [선언](../../../Assets/Scripts/Components/Items/ItemRequests.cs), [Producer](../../../Assets/Scripts/Systems/Buildings/StateApply/BuildingItemStorageApplySystem.cs), [Consumer](../../../Assets/Scripts/Systems/Items/StateApply/ItemOwnershipApplySystem.cs).

## Storage

- **종류·부착 대상:** 일반 `IComponentData`, `SlotCount`는 보관 슬롯 개수다. 공통 완공 생성에서 Storage·DroneStation·MainFacility와 Crafter에 붙는다. 공사 현장은 `StoredItemElement`를 갖지만 이 `Storage`를 생성하지 않는다.
- **초기화:** Storage·DroneStation·MainFacility는 설정 용량이 양수면 그 값, 아니면 20슬롯으로 생성된다. Crafter는 0슬롯에서 시작하고 Command의 레시피 변경이 슬롯 계산 성공 후 `Storage(slots.Length)`로 갱신한다.
- **Reader:** BuildingDecision의 `BuildingItemInputDecisionSystem`은 존재와 양수 슬롯을 검사한다. BuildingReservation의 `BuildingStorageInputReservationSystem`은 실제 보관 내용과 프레임 임시 예약을 합산해 슬롯을 배정한다. 드론은 `DroneSchedulingUtility.FindStorageSlot/CanStoreInSlot`으로 보관처 용량을 검사하며 Execution의 인계 계획이 건물 종료 상태의 공간 상한을 기록한다. 개발용 진단은 슬롯 범위·보관량을 검사한다.
- **처리:** 일반 입고 예약은 `GameConstants.MaxStorageSlots`(현재 120)까지 슬롯을 집계한다. `BuildingInputSlotElement`가 있으면 버퍼 길이와 SlotCount 일치, 상한, 아이템 설정 존재를 확인한다. 슬롯당 적재 수는 `ItemRegistry` 품목별 MaxStack으로 별도 계산한다.
- **수명·결합:** 보관 실물 수가 변해도 SlotCount는 감소하지 않는다. Crafter 레시피 변경은 용량·전용 슬롯·Whitelist를 함께 갱신하고 건물 삭제 시 컴포넌트가 소멸한다. SpawnItemRequest의 직접 보관 생성은 일반 Storage 예약을 거치지 않는다. 드론의 현장 공급은 Storage 없이 현장 Stored 버퍼·자재 요구/개별 예약을 기준으로 처리한다.
- **근거:** [선언](../../../Assets/Scripts/Components/Storage/StorageComponents.cs), [타입별 생성](../../../Assets/Scripts/Common/BuildingLifecycleUtility.cs), [현장 생성](../../../Assets/Scripts/Systems/Command/BuildingPlacementCommandSystem.cs), [레시피 변경](../../../Assets/Scripts/Systems/Command/CrafterRecipeCommandSystem.cs), [예약](../../../Assets/Scripts/Systems/Buildings/Reservation/BuildingStorageInputReservationSystem.cs).

## StoredItemElement

- **종류·부착 대상:** `IBufferElementData`, `InternalBufferCapacity(16)`. Storage·DroneStation·MainFacility·Crafter·ConstructionSite와 드론 수행자의 실물 보관 버퍼다. 원소의 `ItemEntity`는 실물, `ItemType`은 품목 캐시, `SlotIndex`는 슬롯이다. 버퍼 길이는 슬롯 수가 아니라 보관한 실물 수다. 실제 수행자 생성 Producer는 후속이며 현재 테스트/외부 구성에서 드론 보관 버퍼를 준비한다.
- **생성:** 완공 건물은 공통 생성에서 빈 버퍼를 EndBuilding에 만든다. 현장은 `BuildingPlacementCommandSystem`이 빈 버퍼를 EndCommand에 만든다. `SpawnItemRequest(Storage)`는 Item Lifecycle이 버퍼 append를 EndBuilding ECB에 기록한다.
- **일반 입출고:** 입고 예약은 읽기만 하고, Storage Apply가 입고 append 및 출고 대상 제거를 직접 수행한다. `StorageItemOutputDecisionSystem`은 Product 버퍼 없는 건물의 첫 원소를 출고 후보로 선택한다. Owner는 후속 Transfer 경로가 맞춘다.
- **제작·드론 Writer:** `CrafterExecutionSystem`은 제작 착수 재료를 RemoveAt한 뒤 Destroy 요청을 켠다. `CrafterRecipeCommandSystem`은 잔여 재료를 같은 엔티티의 Product 버퍼로 옮긴 뒤 Clear한다. 드론 수집·현장 공급·보관·방출은 `DroneTaskLifecycleApplySystem`이 공통 `TryTransferItem`에 위임하며 성공 실물의 출발 참조 제거/도착 참조 추가를 직접 반영한다. 현장 도착량과 예약 정산은 실물 성공분으로 후속 처리한다.
- **Reader·종료:** `CrafterDecisionSystem`은 재료량, 입력 예약은 슬롯 점유를 읽는다. 철거·공사 취소는 남은 실물을 월드로 반환하고 소유 건물/현장을 삭제한다. 완공 성공은 현장 자재 실물까지 삭제한다. Spawn 실패 시 현장과 자재를 보존한다. 공사 시스템은 이 취소/완공 처리에서 현장 보관 버퍼를 읽는다.
- **결합·근거:** 버퍼 참조와 아이템 Owner/Identity가 일치해야 한다. 같은 틱 입고·철거는 본 문서의 Ownership/Transfer 계약을 따른다. [선언](../../../Assets/Scripts/Components/Storage/StorageComponents.cs), [일반 반영](../../../Assets/Scripts/Systems/Buildings/StateApply/BuildingItemStorageApplySystem.cs), [제작](../../../Assets/Scripts/Systems/Buildings/Execution/CrafterExecutionSystem.cs), [레시피 변경](../../../Assets/Scripts/Systems/Command/CrafterRecipeCommandSystem.cs), [공사](../../../Assets/Scripts/Systems/Construction/StateApply/ConstructionLifecycleApplySystem.cs), [철거](../../../Assets/Scripts/Systems/Buildings/StateApply/BuildingLifecycleApplySystem.cs).

## StorageFilter

- **종류·부착 대상:** 보관 건물의 일반 `IComponentData`. `Mode`는 AllowAll/Whitelist/Blacklist, `Mask`는 128개 품목 비트를 가진 값 타입 `FixedBitSet`이다. Mask 자체는 ECS 컴포넌트가 아니다.
- **초기화:** 공통 생성은 Storage·DroneStation·MainFacility에 기본값인 AllowAll, Crafter에 빈 Whitelist를 추가한다. Crafter가 0슬롯인 시작 상태에서는 필터도 어떤 품목도 허용하지 않는다.
- **Writer:** `CrafterRecipeCommandSystem`이 성공적으로 계산한 전용 슬롯의 모든 품목 비트를 켜서 새 Whitelist를 직접 기록한다. 레시피 해제는 슬롯 0개와 빈 Whitelist를 적용한다. 현재 runtime 코드에서 일반 창고 필터를 변경하는 별도 요청 시스템은 확인되지 않는다.
- **Reader·처리:** `BuildingItemInputDecisionSystem`이 대상 건물의 Storage/제작기 대기 상태를 검사한 뒤 필터를 읽는다. 필터가 없으면 이 단계의 품목 제한을 적용하지 않는다. 드론 보관처의 `DroneSchedulingUtility` 슬롯 검사도 부착된 필터를 읽는다. AllowAll은 true, Whitelist는 켜진 비트만, Blacklist는 꺼진 비트만 허용한다.
- **수명·결합:** 프레임마다 소비하지 않고 건물에 유지한다. 필터 통과는 입고 후보 조건이며 용량·전용 슬롯 최종 승인과 별개다. 직접 Spawn 보관은 이 필터를 통한 일반 벨트 입고 경로가 아니다. 드론의 공사 공급은 현장 자재 요구·개별 예약을 따르며 현장에 이 필터를 자동 부착하지 않는다.
- **근거:** [선언·판정](../../../Assets/Scripts/Components/Storage/StorageFilterComponents.cs), [생성](../../../Assets/Scripts/Common/BuildingLifecycleUtility.cs), [레시피 Writer](../../../Assets/Scripts/Systems/Command/CrafterRecipeCommandSystem.cs), [입고 Reader](../../../Assets/Scripts/Systems/Buildings/Decision/BuildingItemInputDecisionSystem.cs).

## BuildingInputSlotElement

- **종류·부착 대상:** `IBufferElementData`, `InternalBufferCapacity(0)`. 현재 공통 생성에서 Crafter에 붙는 입력 슬롯 구성 버퍼다. 각 인덱스의 `ItemType`이 해당 슬롯에 허용되는 품목을 정한다.
- **생성·Writer:** Crafter 생성 시 빈 버퍼이며 Command의 `CrafterRecipeCommandSystem`이 유효한 레시피 변경 때 Clear 후 새 슬롯을 넣는다. 계산 실패는 기존 구성·진행도·소유 버퍼를 유지하고, 설정 게시 대기인 요청은 유지한다.
- **계산 방법:** `BuildingInputSlotUtility`는 같은 품목의 재료 요구량을 합산하고 `ceil(요구량/MaxStack)`개 슬롯을 만든다. 품목 최초 등장 순서를 유지하고 같은 품목 슬롯은 연속한다. 잘못된 요구량·품목·MaxStack·슬롯 상한 초과는 부분 결과 없이 실패한다.
- **Reader:** Reservation의 `BuildingStorageInputReservationSystem`은 구성 버퍼가 있는 건물에만 전용 품목 규칙을 적용한다. `StoredItemElement`와 같은 프레임 예약량을 함께 집계하여 일치 품목의 기존 스택 또는 빈 전용 슬롯을 배정한다.
- **수명·결합:** 버퍼 길이는 `Storage.SlotCount`와 같아야 하며 Stored의 SlotIndex와 대응한다. 레시피 해제 시 빈 버퍼, 건물 삭제 시 소멸한다. 실제 보관량이 요구량에 도달해도 슬롯은 해당 품목 MaxStack까지 수용한다.
- **근거:** [선언](../../../Assets/Scripts/Components/Storage/BuildingInputSlotElement.cs), [계산](../../../Assets/Scripts/Common/BuildingInputSlotUtility.cs), [Command 갱신](../../../Assets/Scripts/Systems/Command/CrafterRecipeCommandSystem.cs), [예약](../../../Assets/Scripts/Systems/Buildings/Reservation/BuildingStorageInputReservationSystem.cs).

## BuildingItemInputDecision

- **종류·부착 대상:** 아이템에 붙는 `IComponentData, IEnableableComponent`. `TargetBuilding`, `CanDeposit`, `TargetSlotIndex`로 이번 틱 입고 의도를 전달하며 독립 일회성 요청이 아니다. 공통 아이템 스폰은 비활성 상태로 미리 붙인다.
- **Decision Writer:** `BuildingItemInputDecisionSystem`은 매 판단 시작에 대상 Null·CanDeposit=false·슬롯 -1·결정 비활성으로 초기화한다. 이동 활성, 월드 소유, 현재 벨트 존재, 종단 진행도, 철거 승인 부재, 다음 셀의 양수 Storage, Crafter 부산물 대기 여부, 필터를 검사한다. 성공하면 활성화하고 `CanDeposit=true`, `TargetSlotIndex=-1`을 기록한다.
- **조회 조건:** 비활성 프레임 결정도 다음 틱에 다시 판단하기 위해 `IgnoreComponentEnabledState`를 유지한다. Execute의 `EnabledRefRO<BeltMovementState>`는 실제 이동 활성을 별도로 읽으며 비활성 원본의 잔여 Progress가 0 또는 1이어도 거절한다. 수납 실물은 이동 활성 값과 무관하게 거절한다. 정체로 이번 틱 전진량이 0인 활성 실물은 제외하지 않는다. 모든 거절 분기는 시작 시 초기화한 결정을 유지한다. [F-032 구현·검증](../../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/F032-Verification.md).
- **Reservation Writer:** `BuildingStorageInputReservationSystem`은 활성 미배정 결정을 순차 처리하여 슬롯을 확정한다. 같은 틱 예약의 품목·개수와 현재 Stored 버퍼를 합산하며 배정 실패는 CanDeposit=false/slot=-1/비활성으로 처리한다.
- **Apply·소비:** `BuildingItemStorageApplySystem`은 승인된 슬롯에 실물을 추가하고 벨트 상태를 끄며 Transfer를 켠 뒤 결정을 비활성화한다. 활성 Destroy 또는 대상 Stored 버퍼 누락도 결정을 초기화·비활성화한다. 컴포넌트는 남아 이후 틱에 재사용되며 성공 여부를 오래 보관하는 결과 데이터가 아니다.
- **근거:** [선언](../../../Assets/Scripts/Components/Storage/BuildingItemDecisions.cs), [초기화](../../../Assets/Scripts/Common/ItemLifecycleUtility.cs), [Decision](../../../Assets/Scripts/Systems/Buildings/Decision/BuildingItemInputDecisionSystem.cs), [Reservation](../../../Assets/Scripts/Systems/Buildings/Reservation/BuildingStorageInputReservationSystem.cs), [Apply](../../../Assets/Scripts/Systems/Buildings/StateApply/BuildingItemStorageApplySystem.cs).

## BuildingItemOutputDecision

- **종류·부착 대상:** 보관/생산 건물의 `IComponentData, IEnableableComponent`. `CanOutput`, `ItemToOutput`, `TargetBeltPosition`을 담는다. 공통 생성에서 Storage·DroneStation·MainFacility·Miner·Crafter에 비활성 상태로 추가한다.
- **Decision Writer:** `StorageItemOutputDecisionSystem`은 Product 버퍼가 없는 건물의 Stored 첫 실물을 선택한다. `ProductItemOutputDecisionSystem`은 Product 슬롯 0을 우선하고 없으면 버퍼 첫 실물을 선택한다. 두 시스템은 비활성 결정도 다시 계산한다.
- **대상 선택:** 회전된 Footprint 둘레를 하단→우측→상단→좌측 순으로 훑어 외향 벨트와 입구 여유를 찾는다. `BeltEntryUtility.HasEntrySpace`는 공간 스냅샷의 월드 소유·활성 이동 아이템만 점유로 센다. 후보가 없으면 값을 초기화하고 비활성화한다.
- **Reservation Writer:** `BeltDestinationReservationSystem`이 Routing 후보와 함께 목적지별 경합을 처리한다. 공간이 없으면 모두, 경합이면 `PlacementStamp` 우선순위에서 진 후보를 비활성화한다. 승자는 별도 승인 컴포넌트 없이 활성 결정을 유지한다.
- **Apply·소비:** StateApply Storage Apply는 Product가 있으면 그 버퍼, 아니면 Stored에서 대상 참조를 제거하고 벨트 입구 위치·진행도 0·활성 이동 상태 및 월드 Transfer를 기록한다. 성공 후 CanOutput=false/대상 Null/비활성으로 소비한다. Destroy 대상이나 목적지 벨트 소실도 거부·소비하며 건물당 한 후보를 다음 틱 다시 계산한다.
- **근거:** [선언](../../../Assets/Scripts/Components/Storage/BuildingItemDecisions.cs), [생성](../../../Assets/Scripts/Common/BuildingLifecycleUtility.cs), [Storage Decision](../../../Assets/Scripts/Systems/Buildings/Decision/StorageItemOutputDecisionSystem.cs), [Product Decision](../../../Assets/Scripts/Systems/Buildings/Decision/ProductItemOutputDecisionSystem.cs), [Reservation](../../../Assets/Scripts/Systems/Buildings/Reservation/BeltDestinationReservationSystem.cs), [Apply](../../../Assets/Scripts/Systems/Buildings/StateApply/BuildingItemStorageApplySystem.cs).
