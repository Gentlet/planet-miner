# 공통 좌표와 공간 인덱스 컴포넌트

[전체 색인](README.md) · 기준일 2026-10-03 · 현재 소스 정적 분석, 이번 작업에서 실행 검증은 하지 않았다.

이 문서는 위치·방향 원본 2개와 공간 인덱스·Fence 8개를 다룬다. 원본 컴포넌트의 변경과 인덱스 재구축은 별도 과정이다. 공간 인덱스는 Synchronization에서 갱신되므로 Command 또는 StateApply에서 엔티티가 생성되어도 그 순간 새 점유가 맵에 등록되지는 않는다.

## GridPosition

- **종류와 목적:** 일반 `IComponentData`. `int2 Value`가 엔티티의 격자 좌표다. 건물·공사 현장에서는 좌하단 기준점, 아이템에서는 현재 소속 셀, 자원에서는 자원 셀이다.
- **부착과 생성:** 완공 건물은 `BuildingLifecycleUtility.SpawnBuilding`, 공사 현장은 `BuildingPlacementCommandSystem`, 아이템은 `ItemLifecycleUtility.SpawnPrefabItem`, 자원은 `ResourceGenerationUtility`가 추가한다. 현장·자원 생성은 EndCommand, 건물·아이템 생성은 호출자의 EndStateApply ECB에서 실체화된다.
- **변경:** Execution의 `BeltMovementExecutionSystem`이 셀 횡단 시 아이템 좌표를 바꾼다. StateApply의 `BuildingItemStorageApplySystem` 출고 Job과 `RoutingApplySystem`은 목적 벨트 좌표를 기록한다. 공사 취소와 건물 철거 반환은 현장/건물 좌표를 ECB에 기록한다.
- **읽기와 처리:** Decision은 현재/다음 셀·입출력 포트·채굴기 하부를 계산한다. 배치 Command는 점유를 검증하고, 공사·건물 Lifecycle은 생성/반환 위치로 사용한다. 네 SpatialSync 시스템은 이 값을 키로 맵을 재구축한다.
- **수명과 결합:** 부착 엔티티가 살아 있는 동안 유지되며 프레임마다 소비되지 않는다. `LocalTransform`은 표시 위치이고 공간 인덱스의 원본 좌표는 `GridPosition`이다. 보관 아이템에 좌표가 남아 있어도 `ItemOwnership`이 월드 소유가 아니면 ItemSpatialIndex에 등록되지 않는다.
- **근거:** [정의](../../../Assets/Scripts/Components/Common/GridComponents.cs#L20), [건물 생성](../../../Assets/Scripts/Common/BuildingLifecycleUtility.cs#L85), [아이템 생성](../../../Assets/Scripts/Common/ItemLifecycleUtility.cs#L22), [자원 생성](../../../Assets/Scripts/Chunks/ResourceGenerationUtility.cs#L181), [벨트 이동](../../../Assets/Scripts/Systems/4_Execution/BeltMovementExecutionSystem.cs#L70), [출고 반영](../../../Assets/Scripts/Systems/5_StateApply/BuildingItemStorageApplySystem.cs#L265).

## Direction

- **종류와 목적:** 일반 `IComponentData`. `dir`은 `DirectionEnum`의 Up/Right/Down/Left 방향이다. 건물 Footprint 회전, 출입력 포트와 벨트 흐름 방향 계산에 사용한다.
- **부착과 생성:** 완공 건물의 공통 Spawn 및 공사 현장 생성이 추가한다. 일반 아이템 공통 Spawn은 이 컴포넌트를 추가하지 않는다. 출고·라우팅에는 아이템에 이미 Direction이 있을 때만 갱신하는 분기가 있다.
- **변경:** 배치 Command에서 기존 같은 타입 벨트의 방향을 덮어쓸 때 EndCommand ECB에 기록한다. 출고/라우팅의 조건부 아이템 방향 갱신은 StateApply에서 이루어진다. 벨트 이동 Execution은 아이템 Direction 대신 공간 인덱스의 `BeltInfo.Direction`을 읽는다.
- **읽기와 처리:** Building/Belt SpatialSync가 방향을 요약 정보에 복사한다. BuildingSpatialSync 및 배치/생산/물류 계산은 `BuildingFootprintUtility`·`DirectionExtensions`·`RoutingDirectionUtility`를 통해 방향을 해석한다.
- **수명과 결합:** 엔티티와 함께 유지된다. `BuildingFootprint.Size`는 회전 전 기본 크기이며 각 점유 Reader가 방향을 한 번 적용한다. `DirectionEnum`은 컴포넌트가 아닌 값 타입이며 Count는 방향 선택값이 아니다.
- **근거:** [정의](../../../Assets/Scripts/Components/Common/GridComponents.cs#L41), [건물 초기화](../../../Assets/Scripts/Common/BuildingLifecycleUtility.cs#L86), [기존 벨트 변경](../../../Assets/Scripts/Systems/1_Command/BuildingPlacementCommandSystem.cs#L133), [조건부 아이템 갱신](../../../Assets/Scripts/Systems/5_StateApply/RoutingApplySystem.cs#L115), [Footprint 계산](../../../Assets/Scripts/Common/BuildingFootprintUtility.cs).

## 공간 인덱스의 공통 처리

각 SpatialSync 시스템의 OnCreate가 별도 싱글톤 엔티티 하나에 Index와 Fence를 함께 붙이고, Persistent 맵을 할당한다. OnUpdate는 이전 Writer와 Reader를 기다린 뒤 Clear Job → Populate Job을 연결한다. 대상이 0개여도 Clear Job을 실행한다. 용량 부족 시 Fence를 완료하고 확장하며, 시스템 OnDestroy에서 Fence 완료 후 맵을 Dispose한다. 컴포넌트 값 복사본이 맵 메모리를 따로 소유하는 것은 아니다.

Reader는 Fence의 마지막 Writer를 자신의 Job 의존성에 결합하고 새 Reader 핸들을 등록한다. Writer는 마지막 Writer와 누적 Reader를 모두 기다린다. `SetWriter`가 새 Writer를 보관하고 Reader 누적값을 초기화한다. 이 의존성은 ECS 컴포넌트 접근 의존성과 함께 사용한다.

| Index | Map 값 | 등록 조건 | 주요 조회 단계 |
| --- | --- | --- | --- |
| BeltSpatialIndex | `BeltInfo` | BeltComponent + GridPosition + Direction | Decision, Reservation, Execution, StateApply |
| BuildingSpatialIndex | `BuildingInfo` | BuildingType + BuildingFootprint + GridPosition + Direction | 배치 Command, 입고 Decision |
| ItemSpatialIndex | `Entity` 여러 개 | ItemIdentity + GridPosition + ItemOwnership, Owner가 Null | 배치 Command, 물류 Decision/Reservation |
| ResourceSpatialIndex | `Entity` 한 개 | ResourceNode + GridPosition | 배치 Command, 채굴 Decision |

동일 그룹 안에서 네 SpatialSync 시스템의 나열 순서가 실행 순서를 뜻하지 않는다. 각 맵은 자기 Fence로 연결된다. 개발용 `WorldInvariantValidationSystem`은 Synchronization의 OrderLast에서 Fence를 완료한 뒤 현재 구현된 불변식 검사를 수행한다.

## BeltSpatialIndex

- **종류와 부착:** 일반 `IComponentData`, `BeltSpatialSyncSystem`이 만든 싱글톤에 BeltSpatialIndexFence와 함께 부착된다.
- **목적과 필드:** `NativeParallelHashMap<int2, BeltInfo> Map`으로 셀에서 벨트 엔티티·방향·속도를 찾는다. BeltInfo는 맵에 들어가는 일반 struct이며 ECS 컴포넌트가 아니다.
- **생성·갱신:** OnCreate에서 용량 1024로 생성한다. Synchronization은 BeltComponent/GridPosition/Direction을 가진 엔티티를 셀당 `TryAdd`한다. 필요 용량은 쿼리 엔티티 수 기준이다.
- **Reader:** BeltMovementDecision, BuildingItemInputDecision, StorageItemOutputDecision, ProductItemOutputDecision, SplitterDecision, MergerDecision 시스템이 Decision에서 읽는다. BeltDestinationReservation, BeltMovementExecution, BuildingItemStorageApply도 각 단계에서 읽는다. 개발 Validator는 벨트 검사를 위해 읽는다.
- **수명과 결합:** 프레임마다 내용만 재구축하고 맵은 재사용한다. 벨트 생성/철거/방향 변경은 다음 Synchronization에 반영된다. OnDestroy에서 Fence 완료 후 Dispose한다.
- **근거:** [정의](../../../Assets/Scripts/Components/Belts/BeltSpatialIndex.cs#L31), [생성·재구축·해제](../../../Assets/Scripts/Systems/6_Synchronization/BeltSpatialSyncSystem.cs), [이동 Reader](../../../Assets/Scripts/Systems/4_Execution/BeltMovementExecutionSystem.cs#L53), [출고 Reader](../../../Assets/Scripts/Systems/5_StateApply/BuildingItemStorageApplySystem.cs#L111).

## BeltSpatialIndexFence

- **종류와 목적:** 일반 `IComponentData`. `_lastWriter`와 `_readers` JobHandle로 BeltSpatialIndex 맵 작업의 의존성을 보관한다.
- **부착·초기화:** BeltSpatialSyncSystem이 Index와 같은 싱글톤에 기본값으로 추가한다.
- **Writer와 Reader:** BeltSpatialSyncSystem이 Clear/Populate 핸들을 SetWriter한다. 위 BeltSpatialIndex의 비동기 Reader 시스템들이 GetReaderDependency/AddReader를 사용한다. 게임 데이터나 엔티티 점유를 저장하지 않는다.
- **처리:** Reader는 마지막 Writer만 기다리며, 다음 Writer는 누적 Reader까지 기다린다. 용량 변경·시스템 종료·개발 검증의 메인 스레드 접근은 Complete를 사용한다.
- **수명과 결합:** 프레임 요청처럼 삭제하지 않는다. SetWriter/Complete가 보관 핸들을 갱신·초기화하며, 직접 Dispose할 NativeContainer는 없다. Index의 메모리 해제 전에 완료된다.
- **근거:** [정의와 메서드](../../../Assets/Scripts/Components/Belts/BeltSpatialIndex.cs#L66), [소유 시스템](../../../Assets/Scripts/Systems/6_Synchronization/BeltSpatialSyncSystem.cs), [Reader 등록](../../../Assets/Scripts/Systems/2_Decision/BeltMovementDecisionSystem.cs#L73).

## BuildingSpatialIndex

- **종류와 부착:** 일반 `IComponentData`, BuildingSpatialSyncSystem이 만든 싱글톤에 BuildingSpatialIndexFence와 함께 부착된다.
- **목적과 필드:** `NativeParallelHashMap<int2, BuildingInfo> Map`. BuildingInfo는 엔티티·건물 타입·방향을 가진 비컴포넌트 struct다. 하나의 다중 셀 건물은 모든 점유 셀에서 같은 엔티티로 조회된다.
- **생성·갱신:** OnCreate에서 용량 1024로 생성한다. Synchronization에서 BuildingType/BuildingFootprint/GridPosition/Direction을 읽고 회전된 유효 크기의 모든 셀에 TryAdd한다. 용량은 Footprint 면적 합으로 산정한다. 완공 건물과 ConstructionSite가 모두 대상이다.
- **Reader와 단계:** BuildingPlacementCommandSystem이 Command에서 점유를 조회하고 BuildingPlacementValidationUtility에 맵을 넘긴다. BuildingItemInputDecisionSystem이 Decision에서 벨트 종단의 다음 셀에 입고 가능한 건물이 있는지 조회한다.
- **수명과 결합:** 매 Synchronization에 Clear 후 재등록한다. 점유 원본은 건물 컴포넌트이며 맵 자체가 배치 예약이나 구조 변경을 수행하지 않는다. OnDestroy에서 Fence 완료 후 Dispose한다.
- **근거:** [정의](../../../Assets/Scripts/Components/Buildings/BuildingSpatialIndex.cs#L30), [소유 시스템과 점유 등록](../../../Assets/Scripts/Systems/6_Synchronization/BuildingSpatialSyncSystem.cs), [입고 조회](../../../Assets/Scripts/Systems/2_Decision/BuildingItemInputDecisionSystem.cs), [배치 조회](../../../Assets/Scripts/Systems/1_Command/BuildingPlacementCommandSystem.cs#L42).

## BuildingSpatialIndexFence

- **종류와 목적:** 일반 `IComponentData`. BuildingSpatialIndex의 마지막 Writer와 누적 Reader 핸들을 보관한다.
- **부착·생성:** BuildingSpatialSyncSystem이 Index와 동일한 싱글톤에 기본값으로 추가한다.
- **Reader와 Writer:** BuildingItemInputDecisionSystem은 Reader 핸들을 등록한다. BuildingPlacementCommandSystem은 메인 스레드 조회 전에 GetReaderDependency가 반환한 마지막 Writer를 완료한다. BuildingSpatialSyncSystem은 Clear/Populate를 Writer로 등록한다.
- **처리:** 배치 Command는 Building/Resource/Item의 마지막 Writer 세 개를 결합해 완료한다. 다른 Reader 전체를 완료하는 방식은 아니다. 맵 재할당·종료와 Validator의 완료 경계에서는 Complete를 사용한다.
- **수명:** 매 틱 유지되며 핸들만 교체한다. 맵 Dispose 전에 완료되고 Fence 자체에 별도 메모리 해제는 없다.
- **근거:** [정의](../../../Assets/Scripts/Components/Buildings/BuildingSpatialIndex.cs#L65), [배치 완료 경계](../../../Assets/Scripts/Systems/1_Command/BuildingPlacementCommandSystem.cs#L42), [Reader 등록](../../../Assets/Scripts/Systems/2_Decision/BuildingItemInputDecisionSystem.cs#L77), [소유 시스템](../../../Assets/Scripts/Systems/6_Synchronization/BuildingSpatialSyncSystem.cs).

## ItemSpatialIndex

- **종류와 부착:** 일반 `IComponentData`, ItemSpatialSyncSystem이 만든 싱글톤에 ItemSpatialIndexFence와 함께 부착된다.
- **목적과 필드:** `NativeParallelMultiHashMap<int2, Entity> Map`으로 한 셀의 여러 월드 아이템을 조회한다. 셀의 아이템 수와 벨트 진입을 막는 점유 아이템 수는 같다고 가정하지 않는다.
- **생성·갱신:** 용량 1024의 Persistent 맵을 생성한다. ItemIdentity/GridPosition/ItemOwnership 쿼리를 사용하고 Populate Job 내부에서 `ownership.IsWorldItem`인 엔티티만 Add한다. BeltMovementState 활성 여부는 맵 등록 조건이 아니다.
- **Reader와 단계:** 배치 Command가 바닥 아이템을 검사한다. BeltMovementDecision, StorageItemOutputDecision, ProductItemOutputDecision, SplitterDecision, MergerDecision과 BeltDestinationReservation이 점유를 조회한다. Validator는 소유권·위치 정합성을 검사한다. 벨트 철거 정리는 이 맵을 읽지 않고 아이템의 최신 GridPosition을 직접 대조한다.
- **수명과 결합:** 소유권 변경·이동·아이템 파괴 후 Synchronization에서 재구축된다. 보관 아이템은 제외한다. 벨트 진입 판정은 조회 결과에서 월드 소유권과 활성 BeltMovementState를 추가 확인한다. OnDestroy에서 Fence 완료 후 Dispose한다.
- **근거:** [정의](../../../Assets/Scripts/Components/Items/ItemSpatialIndex.cs#L14), [등록 필터](../../../Assets/Scripts/Systems/6_Synchronization/ItemSpatialSyncSystem.cs#L135), [진입 조건](../../../Assets/Scripts/Common/BeltEntryUtility.cs), [철거 조회](../../../Assets/Scripts/Systems/5_StateApply/BuildingLifecycleApplySystem.cs).

## ItemSpatialIndexFence

- **종류와 목적:** 일반 `IComponentData`. ItemSpatialIndex의 Writer/Reader Job 의존성을 누적한다.
- **부착·생성:** ItemSpatialSyncSystem이 Index와 같은 싱글톤에 기본값으로 추가한다.
- **Reader와 Writer:** 물류 Decision과 BeltDestinationReservation은 Reader 핸들을 등록한다. 배치 Command는 마지막 Writer를 완료한 뒤 직접 조회한다. ItemSpatialSyncSystem이 Clear/Populate Writer를 등록한다. BuildingLifecycleApply의 철거 정리에는 이 Fence를 사용하는 경로가 없다.
- **처리:** 여러 Reader가 동시에 맵을 읽을 수 있고 다음 재구축은 등록된 Reader들을 기다린다. 맵 용량 변경·OnDestroy·Validator는 Complete를 사용한다.
- **수명:** 프레임마다 소비되는 요청이 아니며 Index와 함께 유지한다. NativeContainer를 소유하지 않고 맵을 해제할 때 선행 작업 완료에 사용된다.
- **근거:** [정의](../../../Assets/Scripts/Components/Items/ItemSpatialIndex.cs#L65), [예약 Reader 등록](../../../Assets/Scripts/Systems/3_Reservation/BeltDestinationReservationSystem.cs#L104), [철거의 직접 좌표 조회](../../../Assets/Scripts/Systems/5_StateApply/BuildingLifecycleApplySystem.cs#L173), [소유 시스템](../../../Assets/Scripts/Systems/6_Synchronization/ItemSpatialSyncSystem.cs).

## ResourceSpatialIndex

- **종류와 부착:** 일반 `IComponentData`, ResourceSpatialSyncSystem이 만든 싱글톤에 ResourceSpatialIndexFence와 함께 부착된다.
- **목적과 필드:** `NativeParallelHashMap<int2, Entity> Map`으로 자원 셀에서 ResourceNode 엔티티를 찾는다. 자원 종류와 잔량은 엔티티 컴포넌트에서 읽는다.
- **생성·갱신:** OnCreate에서 용량 1024로 생성한다. Synchronization에서 ResourceNode/GridPosition을 가진 엔티티를 셀당 TryAdd한다. 맵 쿼리 자체에는 잔량 양수 조건이 없다.
- **Reader와 단계:** 배치 Command/BuildingPlacementValidationUtility가 채굴기 하부 자원을 확인한다. MinerDecisionSystem이 채굴 대상을 고르며, Validator가 자원 엔티티와 맵의 일치를 검사한다.
- **수명과 결합:** 자원 생성은 EndCommand에서 실체화되고 해당 프레임 Synchronization에서 등록된다. 그 인덱스를 이용한 채굴 판단은 다음 프레임부터 가능하다. 고갈 자원 삭제도 EndStateApply 후 재구축에서 반영된다. OnDestroy에서 Fence 완료 후 Dispose한다.
- **근거:** [정의](../../../Assets/Scripts/Components/Resources/ResourceSpatialIndex.cs#L13), [소유 시스템](../../../Assets/Scripts/Systems/6_Synchronization/ResourceSpatialSyncSystem.cs), [채굴 조회](../../../Assets/Scripts/Systems/2_Decision/MinerDecisionSystem.cs), [자원 생성 Command](../../../Assets/Scripts/Systems/1_Command/ResourceGenerationCommandSystem.cs).

## ResourceSpatialIndexFence

- **종류와 목적:** 일반 `IComponentData`. ResourceSpatialIndex의 마지막 Writer와 Reader 의존성을 보관한다.
- **부착·생성:** ResourceSpatialSyncSystem이 Index와 동일 싱글톤에 기본값으로 추가한다.
- **Reader와 Writer:** MinerDecisionSystem이 Reader 핸들을 등록한다. 배치 Command는 마지막 Writer를 완료하고 자원을 직접 조회한다. ResourceSpatialSyncSystem이 재구축 Writer를 등록한다.
- **처리:** Reader는 마지막 Writer를, Writer는 누적 Reader까지 기다린다. 맵 용량 확장·시스템 종료·개발 검증 시 Complete가 현재 작업을 완료한다.
- **수명:** Index와 함께 유지되며 SetWriter/Complete로 핸들이 갱신된다. 별도 Dispose할 메모리는 없고 Resource 맵 해제 전에 완료된다.
- **근거:** [정의](../../../Assets/Scripts/Components/Resources/ResourceSpatialIndex.cs#L48), [채굴 Reader 등록](../../../Assets/Scripts/Systems/2_Decision/MinerDecisionSystem.cs#L79), [배치 완료 경계](../../../Assets/Scripts/Systems/1_Command/BuildingPlacementCommandSystem.cs#L42), [소유 시스템](../../../Assets/Scripts/Systems/6_Synchronization/ResourceSpatialSyncSystem.cs).

## 요청 인터페이스와 Unity 제공 타입

`IRequestComponent`와 `IEnableableRequest`는 컴포넌트를 분류하는 인터페이스이며 엔티티에 직접 부착하지 않는다. IEnableableRequest는 IRequestComponent와 IEnableableComponent를 상속한다. 실제 삭제/비활성화/대기 조건은 각 구현 타입의 Consumer에서 정한다. 이름에 Request가 있어도 이 인터페이스를 구현하지 않는 데이터가 있으므로 전체 목록은 인터페이스 검색만으로 한정하지 않았다. [정의](../../../Assets/Scripts/Components/Common/IRequestComponent.cs)

프로젝트가 직접 정의하지 않은 `LocalTransform`, `Prefab`, `DisableRendering` 등은 이 컴포넌트 색인에 포함하지 않았다. LocalTransform은 표시 위치와 프리팹 구성, Prefab은 DB 검증과 인스턴스 원형, DisableRendering은 보관 아이템의 표시 억제와 연결된다. `Disabled`는 현재 아이템 소유권 표현이 아니다. 구체적인 사용은 [아이템과 저장](ItemsAndStorage.md), [프리팹과 시작 상태](Prefabs.md)에 연결해 설명한다. 전체 타입 수는 2026-10-04 공사 운송 타입 제거를 반영한 [전체 색인](README.md)을 따른다.
