# 프리팹 DB·시뮬레이션 준비 상태 컴포넌트

[전체 색인](README.md)

최초 조사 2026-10-03, 2026-10-06 도메인 분리의 실행 게이트와 ECB 경계를 현재 소스와 대조했다. 이번 문서 감사에서 베이킹·SubScene 실행·컴파일·테스트·Play Mode를 실행하지 않았다. Baker의 존재와 실제 장면의 베이킹 결과는 구분한다.

건물·아이템·자원 DB는 각각 별도 엔티티의 태그와 매핑 버퍼로 구성된다. `PrefabDatabaseInitializationSystem`은 요청된 SubScene 로딩을 기다린 뒤 DB를 검증한다. 성공하면 `PrefabDatabaseReady`, 실패하면 `SimulationFatalError`를 게시한다. `GameSimulationGroup`은 틱 시작에 Ready가 있고 Fatal이 없으면 Command → BuildingSimulation → DroneSimulation → SimulationCommit → Synchronization을 진행한다. 검증 후 DB는 불변으로 사용하는 계약이며, 런타임 소비자는 DB를 재작성하지 않는다.

## BuildingPrefabDatabase

- **목적·종류:** 건물 프리팹 DB를 식별하는 필드 없는 `IComponentData` 태그다. 완공 건물 인스턴스마다 부착하는 태그가 아니라 매핑 버퍼를 보관하는 DB 엔티티에 부착된다.
- **부착·생성:** `BuildingPrefabDatabaseAuthoring.BuildingPrefabDatabaseBaker`가 Authoring 엔티티(`TransformUsageFlags.None`)에 태그와 `BuildingPrefabElement` 버퍼를 함께 추가한다. Inspector 목록 또는 `Resources/Prefabs/Building` 자동 탐색 결과가 입력이다.
- **Reader·단계:** Initialization 마지막의 `PrefabDatabaseInitializationSystem`이 이 태그 엔티티와 매핑 버퍼 엔티티가 각각 정확히 하나이며 같은 엔티티인지 확인한다. 건물 스폰을 수행하는 StateApply 두 시스템은 태그 대신 `BuildingPrefabElement` 버퍼 쿼리로 DB를 찾는다.
- **처리·실패:** 초기 검증에서 건물 필수 종류·중복/금지 종류 및 참조 프리팹을 검사한다. 실패하면 Fatal을 직접 게시하고 검증 시스템을 비활성화한다. 태그 존재만으로 게임 시뮬레이션이 허용되지는 않는다.
- **생명주기·경계:** 스폰 때 태그를 소비·비활성화하지 않는다. 제품 코드에 DB 재로드·제거 경로는 없고 검증 이후 World 수명 동안 유지하는 계약이다. 버퍼 및 엔티티 메모리는 ECS가 관리한다.
- **소스:** [정의](../../../Assets/Scripts/Components/Prefabs/PrefabDatabaseComponents.cs), [Baker](../../../Assets/Scripts/Authoring/BuildingPrefabDatabaseAuthoring.cs), [초기 검증](../../../Assets/Scripts/Systems/Initialization/PrefabDatabaseInitializationSystem.cs), [직접 스폰 DB 쿼리](../../../Assets/Scripts/Systems/Buildings/StateApply/BuildingLifecycleApplySystem.cs), [완공 DB 쿼리](../../../Assets/Scripts/Systems/Construction/StateApply/ConstructionLifecycleApplySystem.cs).

## BuildingPrefabElement

- **목적·필드:** `Type → Prefab` 매핑과 정적 `FootprintSize`를 담는 `IBufferElementData`다. 내부 버퍼 용량 힌트는 16이다. 타입별 여러 건물 인스턴스가 같은 매핑을 반복 조회한다.
- **부착·초기화:** Building DB Baker가 DB 엔티티에 버퍼를 만들고 각 GameObject 프리팹의 Renderable 엔티티 참조를 등록한다. Inspector 크기는 축별 최소 1로 정규화하고 자동 탐색은 타입별 기본 크기를 기록한다.
- **Reader·검증:** Initialization에서 None/Count/ConstructionSite·범위 밖/중복 등록을 거부하고 나머지 모든 건물 종류를 요구한다. 프리팹은 생존하며 `Prefab`·`LocalTransform`을 가져야 한다. StateApply의 `BuildingLifecycleApplySystem` 및 `ConstructionLifecycleApplySystem`은 읽기 전용 BufferLookup을 `BuildingLifecycleUtility.SpawnBuilding`에 전달한다.
- **처리·ECB:** 공통 스폰 유틸리티가 매핑을 찾아 프리팹 Instantiate와 위치·방향·타입별 런타임 구성을 EndBuilding ECB에 기록한다. DB/매핑 누락이면 Fatal을 기록하고 `Entity.Null`을 반환한다. 대체 프리팹이나 빈 엔티티를 생성하지 않는다.
- **필드 결합·생명주기:** 조회 함수는 `FootprintSize`도 반환하지만 현재 `SpawnBuilding`은 반환된 `dbFootprint`를 실제 크기에 사용하지 않는다. 실제 `BuildingFootprint`는 전달 크기→건물 설정→타입 기본값 순서로 정한다. 매핑 버퍼는 조회 후 유지하며 런타임 Writer·Clear·별도 Dispose는 없다.
- **소스:** [정의](../../../Assets/Scripts/Components/Prefabs/PrefabDatabaseComponents.cs), [Baker와 크기 초기값](../../../Assets/Scripts/Authoring/BuildingPrefabDatabaseAuthoring.cs), [검증](../../../Assets/Scripts/Systems/Initialization/PrefabDatabaseInitializationSystem.cs), [조회](../../../Assets/Scripts/Common/PrefabLookupUtility.cs), [실제 스폰·크기 선택](../../../Assets/Scripts/Common/BuildingLifecycleUtility.cs).

## ItemPrefabDatabase

- **목적·종류:** 아이템 프리팹 매핑 엔티티를 찾는 필드 없는 `IComponentData` 태그다. 실제 아이템의 품목·소유권 상태는 이 태그가 아니라 개별 아이템 컴포넌트에 있다.
- **부착·생성:** `ItemPrefabDatabaseAuthoring.ItemPrefabDatabaseBaker`가 Authoring 엔티티에 태그와 `ItemPrefabElement` 버퍼를 함께 만든다. Inspector 목록 또는 `Resources/Prefabs/Item` 자동 탐색을 사용한다.
- **Reader·단계:** Initialization의 `PrefabDatabaseInitializationSystem`이 DB와 버퍼의 유일성·동일 엔티티 부착을 검사한다. StateApply의 `ItemLifecycleApplySystem`과 `BuildingLifecycleApplySystem`은 `ItemPrefabDatabase + ItemPrefabElement` 쿼리로 DB를 얻는다.
- **처리 경로:** Item Lifecycle은 일반 Spawn 요청 및 생산 결과에, Building Lifecycle은 철거 비용 환급용 새 아이템에 DB를 사용한다. 기존 실물을 월드로 반환하는 취소·철거 경로는 신규 프리팹 생성과 구분한다.
- **생명주기·경계:** 매 스폰 후 태그를 유지한다. 검증 이후 불변 DB 계약이며 제품 코드의 재게시·전용 파괴·변경 Writer는 없다. DB가 없어도 대체 아이템을 만드는 경로는 없다.
- **소스:** [정의](../../../Assets/Scripts/Components/Prefabs/PrefabDatabaseComponents.cs), [Baker](../../../Assets/Scripts/Authoring/ItemPrefabDatabaseAuthoring.cs), [검증](../../../Assets/Scripts/Systems/Initialization/PrefabDatabaseInitializationSystem.cs), [생산·일반 생성](../../../Assets/Scripts/Systems/Items/StateApply/ItemLifecycleApplySystem.cs), [환급 생성](../../../Assets/Scripts/Systems/Buildings/StateApply/BuildingLifecycleApplySystem.cs).

## ItemPrefabElement

- **목적·필드:** `ItemTypeEnum Type`과 `Entity Prefab`의 매핑을 가진 `IBufferElementData`다. 내부 버퍼 용량 힌트는 16이고 Item DB 엔티티에 부착된다.
- **생성·초기화:** Item DB Baker가 Inspector/Resources 목록에서 None과 null GameObject를 제외하고 Dynamic 엔티티 참조를 기록한다. 제품 런타임에서는 이를 다시 채우지 않는다.
- **Reader·검증:** 초기 검증은 None·미정의 enum·중복을 거부하고 None을 제외한 모든 아이템 종류를 요구한다. 프리팹의 생존·`Prefab`·`LocalTransform`뿐 아니라 `ItemIdentity` 존재와 품목 일치도 확인한다.
- **처리·단계:** StateApply의 Item Lifecycle이 생산/Spawn 품목과 일치하는 항목을 조회하고, Building Lifecycle은 환급 품목을 `PrefabLookupUtility`로 찾는다. 공통 `ItemLifecycleUtility.SpawnPrefabItem`은 선택된 프리팹의 런타임 초기화를 EndBuilding ECB에 기록한다. 소유 버퍼 등록과 요청/결과 소비는 각 호출자가 담당한다.
- **실패·생명주기:** 항목 누락 시 Fatal을 기록한다. 생산 결과는 Clear되고 직접 Spawn 요청은 소비되며 환급 항목은 건너뛴다. DB 버퍼는 소비하지 않고 다음 생성에 재사용한다. 검증 후 갱신·전용 삭제 경로는 없고 ECS가 메모리를 정리한다.
- **소스:** [정의](../../../Assets/Scripts/Components/Prefabs/PrefabDatabaseComponents.cs), [Baker](../../../Assets/Scripts/Authoring/ItemPrefabDatabaseAuthoring.cs), [검증](../../../Assets/Scripts/Systems/Initialization/PrefabDatabaseInitializationSystem.cs), [Item Lifecycle](../../../Assets/Scripts/Systems/Items/StateApply/ItemLifecycleApplySystem.cs), [환급](../../../Assets/Scripts/Systems/Buildings/StateApply/BuildingLifecycleApplySystem.cs), [공통 초기화](../../../Assets/Scripts/Common/ItemLifecycleUtility.cs).

## ResourcePrefabDatabase

- **목적·종류:** 월드 자원 노드 프리팹 DB를 식별하는 필드 없는 `IComponentData` 태그다. 자원 인스턴스의 잔량을 가진 `ResourceNode`와 별도 엔티티에 부착된다.
- **생성·초기화:** `ResourcePrefabDatabaseAuthoring.ResourcePrefabDatabaseBaker`가 Authoring 엔티티에 태그와 `ResourcePrefabElement` 버퍼를 추가한다. Inspector 목록 또는 `Resources/Prefabs/Resource` 자동 탐색이 입력이다.
- **Reader·단계:** Initialization의 DB 검증이 유일성·버퍼 부착을 확인한다. Command의 `ResourceGenerationCommandSystem`은 이 태그를 업데이트 필수 조건으로 요구하고 싱글톤 엔티티에서 매핑 버퍼를 가져온다.
- **처리·대기:** DB가 준비되어야 Ready 청크의 자원 생성 기록이 가능하다. 초기 검증에 실패하면 GameSimulationGroup 전체가 시작되지 않는다. 생성 시스템 자체도 DB 버퍼가 없거나 필수 프리팹을 해결하지 못하면 Ready를 유지하고 반환한다.
- **생명주기:** 청크가 생성되거나 자원이 고갈되어도 DB 태그·엔티티는 유지한다. 제품 코드에 런타임 DB 변경·제거 경로는 없고 검증 이후 World 수명 동안 유지하는 계약이다.
- **소스:** [정의](../../../Assets/Scripts/Components/Prefabs/PrefabDatabaseComponents.cs), [Baker](../../../Assets/Scripts/Authoring/ResourcePrefabDatabaseAuthoring.cs), [초기 검증](../../../Assets/Scripts/Systems/Initialization/PrefabDatabaseInitializationSystem.cs), [생성 시스템](../../../Assets/Scripts/Systems/Command/ResourceGenerationCommandSystem.cs).

## ResourcePrefabElement

- **목적·필드:** `ResourceType → Prefab` 매핑을 가진 `IBufferElementData`다. Resource DB 엔티티의 버퍼이며 내부 용량 힌트는 8이다.
- **생성·초기화:** Resource DB Baker가 품목과 Renderable 프리팹 엔티티를 등록한다. 매장량과 셀 좌표는 여기서 보관하지 않고 자원 인스턴스를 만드는 시점에 주입한다.
- **Reader·검증:** 초기 검증은 빈 DB, None·미정의 enum·중복, 사라진/null 프리팹, `Prefab`·`LocalTransform` 누락을 거부한다. 모든 아이템 종류가 아니라 활성 `ResourceGenerationConfigElement`에 필요한 품목을 요구한다. `ResourceNode`의 베이킹 여부는 필수 검사 항목이 아니다.
- **처리·ECB:** Command에서 `ResourceGenerationUtility.TryResolvePrefabs`가 활성 설정 전체의 프리팹을 임시 배열에 한 번 해결한다. 필요한 하나라도 없으면 생성 기록 없이 반환한다. 이후 셀별 Instantiate·`GridPosition`·`ResourceNode`·Transform 기록이 EndCommand에 재생된다.
- **생명주기·결합:** 매핑은 읽기만 하고 Clear하지 않는다. 임시 resolved 배열은 호출자/유틸리티가 Dispose하고 ECS 버퍼는 계속 유지한다. 현재 생성 시스템의 추가 사전 검사는 엔티티 생존과 Transform이며, 전체 DB 계약은 Initialization 검증 및 이후 불변 조건에 의존한다.
- **소스:** [정의](../../../Assets/Scripts/Components/Prefabs/PrefabDatabaseComponents.cs), [Baker](../../../Assets/Scripts/Authoring/ResourcePrefabDatabaseAuthoring.cs), [검증](../../../Assets/Scripts/Systems/Initialization/PrefabDatabaseInitializationSystem.cs), [프리팹 해결·생성](../../../Assets/Scripts/Chunks/ResourceGenerationUtility.cs), [Command 경계](../../../Assets/Scripts/Systems/Command/ResourceGenerationCommandSystem.cs).

## DronePrefabDatabase

- **목적·종류:** 드론 프리팹 DB 식별용으로 정의된 필드 없는 `IComponentData` 태그다.
- **부착·생성 여부:** 현재 제품 소스(`Assets/Scripts`)에서 정의 외의 참조가 없다. 이를 부착하는 Baker·Init·생성 시스템 또는 실제 부착 엔티티는 확인되지 않는다.
- **Reader·Writer·단계:** 현재 런타임 Reader/Writer와 처리 단계가 없다. `PrefabDatabaseInitializationSystem`은 건물·아이템·자원 DB만 검증하고 이 태그를 검사하지 않는다.
- **결합 계약:** 같은 파일의 `DronePrefab`과 함께 쓰려는 데이터 형태는 있으나 두 컴포넌트를 같은 엔티티에 구성하거나 드론을 Instantiate하는 연결은 현재 소스에 없다. DroneStation 건물 프리팹의 존재와 별개의 타입이다.
- **생명주기·현재 범위:** 정의만 있어 현재 생성→소비→삭제 수명주기는 없다. 별도 Dispose 자원도 없다. 드론 생성 기능이 구현되어 있다는 근거로 사용하지 않는다.
- **소스:** [정의](../../../Assets/Scripts/Components/Prefabs/PrefabDatabaseComponents.cs), [현재 검증 대상](../../../Assets/Scripts/Systems/Initialization/PrefabDatabaseInitializationSystem.cs).

## DronePrefab

- **목적·필드:** 단일 드론 프리팹 엔티티를 가리키는 `Prefab` 필드와 생성자를 가진 `IComponentData`다. 종류별 버퍼 형태의 다른 DB와 다르다.
- **부착·생성 여부:** 현재 제품 소스에서 정의 외의 참조가 없다. 생성자를 호출하거나 ECS에 부착하는 Baker·Init·런타임 경로는 확인되지 않는다.
- **Reader·Writer·단계:** 프리팹 조회·Instantiate를 수행하는 소비 시스템이 없고, 특정 실행 단계에 연결되어 있지 않다. DB 초기 검증도 이 필드의 유효성을 확인하지 않는다.
- **결합 계약:** `DronePrefabDatabase`와 같이 선언되어 있으나 실제 동일 엔티티 부착 계약을 실행하는 코드가 없다. 건물 종류 `DroneStation`을 생성하는 Building DB 경로는 이 컴포넌트를 사용하지 않는다.
- **생명주기·현재 범위:** 현재 확인할 수 있는 것은 타입 정의와 Entity 참조 형태다. 소비·재사용·삭제·World 종료 훅은 구현되어 있지 않으며 자체 NativeContainer/Dispose도 없다.
- **소스:** [정의](../../../Assets/Scripts/Components/Prefabs/PrefabDatabaseComponents.cs), [현재 DB 검증 범위](../../../Assets/Scripts/Systems/Initialization/PrefabDatabaseInitializationSystem.cs), [건물 생성 경로](../../../Assets/Scripts/Common/BuildingLifecycleUtility.cs).

## PrefabDatabaseReady

- **목적·종류:** 프리팹 초기 검증을 통과했다는 사실을 나타내는 필드 없는 `IComponentData` 태그다. DB마다 붙이지 않고 별도 준비 상태 엔티티에 부착한다.
- **생성·초기화:** `PrefabDatabaseInitializationSystem`이 Initialization 마지막에 요청된 SubScene들의 성공 로딩을 확인하고 건물·아이템·자원 DB 검증을 전부 통과하면 `EntityManager.CreateEntity`로 즉시 만든다. 이후 검증 시스템은 비활성화한다.
- **Reader·처리:** `GameSimulationGroup.OnUpdate`가 Ready 쿼리가 비었는지 확인한다. Ready가 없으면 Command·BuildingSimulation·DroneSimulation·SimulationCommit·Synchronization 자식 그룹 전체를 실행하지 않는다. Ready가 있어도 Fatal 엔티티가 하나 이상 있으면 실행하지 않는다.
- **대기·실패:** SubScene이 아직 로딩 중이면 Ready를 만들지 않고 다음 Initialization에서 다시 확인한다. 로딩 실패 또는 DB 검증 실패는 Fatal 게시와 시스템 비활성화로 끝나며 자동 재시도하지 않는다.
- **생명주기·경계:** Ready 자체를 소비하거나 제거하는 제품 경로는 없다. 검증 후 DB를 매 틱 재검사하는 표식이 아니므로 이후 불변 DB 계약이 연결된다. World 종료 때 일반 ECS 엔티티로 정리된다.
- **소스:** [정의](../../../Assets/Scripts/Components/Prefabs/PrefabDatabaseReadiness.cs), [게시·로딩 대기](../../../Assets/Scripts/Systems/Initialization/PrefabDatabaseInitializationSystem.cs), [실행 게이트](../../../Assets/Scripts/Phases/GameSimulationGroup.cs).

## SimulationFatalError

- **목적·필드:** 복구 없이 게임 시뮬레이션 실행을 막는 `IComponentData` 진단 기록이다. `Message`는 `FixedString128Bytes`이며 오류마다 별도 엔티티에 기록될 수 있다.
- **생성 경로 1:** Initialization의 `PrefabDatabaseInitializationSystem.Fail`은 상세 원인을 로그로 남기고 일반화된 메시지를 가진 오류 엔티티를 즉시 생성한다. 그 후 검증 시스템을 비활성화한다.
- **생성 경로 2:** StateApply 스폰/환급 경로가 프리팹을 찾지 못하면 `SimulationFailureUtility.Record`가 메시지를 로그로 남기고 오류 엔티티 생성·컴포넌트 추가를 호출자의 EndBuilding ECB에 기록한다. 호출자는 `BuildingLifecycleUtility`, `ItemLifecycleApplySystem`, `BuildingLifecycleApplySystem`이다.
- **Reader·반영 시점:** `GameSimulationGroup`은 매 그룹 진입 때 Fatal 쿼리가 비었는지 확인한다. 틱 시작에만 검사하므로 EndBuilding에서 오류가 확정되어도 현재 틱 드론·Commit·Synchronization은 수행하고 다음 틱 GameSimulationGroup 진입부터 차단한다. 현재 틱 전체의 롤백이나 이미 기록한 구조 변경 취소는 제공하지 않는다.
- **생명주기·현재 범위:** 오류를 소비·Clear·삭제해 자동 복구시키는 제품 경로는 없다. Ready가 남아 있어도 Fatal이 실행을 막는다. 새 World에서는 초기 검증부터 다시 수행하며, 원인이 남으면 다시 실패한다. 일반 ECS 데이터이므로 자체 Dispose는 없다.
- **소스:** [정의](../../../Assets/Scripts/Components/Prefabs/PrefabDatabaseReadiness.cs), [초기 실패](../../../Assets/Scripts/Systems/Initialization/PrefabDatabaseInitializationSystem.cs), [ECB 오류 기록](../../../Assets/Scripts/Common/SimulationFailureUtility.cs), [실행 게이트](../../../Assets/Scripts/Phases/GameSimulationGroup.cs), [건물 스폰](../../../Assets/Scripts/Common/BuildingLifecycleUtility.cs), [아이템 스폰](../../../Assets/Scripts/Systems/Items/StateApply/ItemLifecycleApplySystem.cs), [환급](../../../Assets/Scripts/Systems/Buildings/StateApply/BuildingLifecycleApplySystem.cs).
