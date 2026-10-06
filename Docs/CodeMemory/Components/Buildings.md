# 건물 식별·설정·생성·철거 컴포넌트

[전체 색인](README.md) · [배치·공사 컴포넌트](Construction.md)

최초 조사 2026-10-03, 2026-10-06 도메인 분리·드론 인계의 직접 계약을 현재 소스와 대조했다. 아래의 처리·순서는 구현된 호출과 쿼리 기준이며, 이번 문서 감사에서 컴파일·테스트·Play Mode는 실행하지 않았다. `BuildingSpatialIndex`와 Fence는 전체 색인의 공간 인덱스 문서에서 다룬다.

2026-10-04 기존 공사 운송의 전용 공급원 등록·자재 수령·운송 예약 처리를 제거하고 현장 취소·완공을 유지했다. 이후 새 드론 계약의 공급원 선택·현장 공급 예약·신호 기반 실물 인계가 연결되었으며 [드론 문서](DroneLogistics.md)를 따른다. 제거 당시의 실행 결과와 한계는 [공사 운송 제거 검증 기록](../../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/ConstructionTransportRemoval-Verification.md)에 보존한다.

건물의 공통 데이터는 `BuildingType + BuildingFootprint + GridPosition + Direction + PlacementStamp`다. 배치 Command가 만드는 엔티티는 공사 현장이고, 완공 시에는 `ConstructionLifecycleApplySystem`이 공통 생성 유틸리티로 별도의 완공 건물 엔티티를 만든다. `SpawnBuildingRequest`도 같은 유틸리티를 호출한다. 현장은 EndCommand, 완공 건물의 생성·철거는 EndBuilding에서 실체화되고, 그 후 Synchronization에서 공간 인덱스에 반영된다.

현재 건물 타입별 공통 생성 구성을 읽으면 다음과 같다. 아래 구성이 해당 기능의 전체 게임 흐름이나 자동 생성 Producer까지 존재한다는 의미는 아니다.

| `BuildingTypeEnum` | 공통 데이터 외에 생성 시 붙는 주요 데이터 |
| --- | --- |
| Belt | `BeltComponent` |
| Miner | `MinerState`, 비활성 `MinerDecision`, 생산품/생산 결과 버퍼, 비활성 출고 결정 |
| Crafter | `CrafterState`, 비활성 제작/상태 결정, 0슬롯 `Storage`, 빈 Whitelist, 전용 입력 슬롯/보관품/생산품/생산 결과 버퍼, 비활성 출고 결정 |
| Storage, DroneStation | `Storage`, `StoredItemElement`, `StorageFilter`, 비활성 출고 결정 |
| MainFacility | 위 저장 구성과 `IndestructibleBuilding` |
| Splitter, Merger | 해당 Routing 상태, 비활성 `RoutingTransferDecision` |
| PowerPole, CoalGenerator, ResearchBuilding | 공통 생성 함수에 전력·연구 전용 런타임 컴포넌트 주입 없음 |
| ConstructionSite | 완공 생성 함수의 대상에서 제외. 배치 Command가 현장 구성 생성 |

근거: [BuildingLifecycleUtility](../../../Assets/Scripts/Common/BuildingLifecycleUtility.cs), [BuildingPlacementCommandSystem](../../../Assets/Scripts/Systems/Command/BuildingPlacementCommandSystem.cs), [ConstructionLifecycleApplySystem](../../../Assets/Scripts/Systems/Construction/StateApply/ConstructionLifecycleApplySystem.cs).

## BuildingType

- **종류·부착 대상·목적:** 일반 `IComponentData`. 완공 건물과 공사 현장 엔티티의 종류를 `Type: BuildingTypeEnum`으로 식별한다. 타입만으로 저장·채굴·전력 등의 구성이 자동으로 붙는 것은 아니다.
- **생성:** 배치 Command는 현장에 `ConstructionSite` 값을 EndCommand ECB로 붙인다. `BuildingLifecycleUtility.SpawnBuilding`은 등록된 프리팹을 인스턴스화하고 대상 타입을 EndBuilding ECB로 붙인다. 현재 Building DB Baker는 프리팹 참조 DB를 제공하며 이 런타임 구성은 공통 생성 함수가 담당한다.
- **읽기:** Command의 `BuildingDemolitionCommandSystem`이 철거 대상 종류를 판정한다. Decision의 `SplitterDecisionSystem`/`MergerDecisionSystem`이 대상 라우터 쿼리·타입을 확인한다. StateApply의 건물 철거가 종류를 확인하고, Synchronization의 `BuildingSpatialSyncSystem`이 각 점유 셀의 `BuildingInfo.Type`으로 복사한다.
- **결합 조건:** 공간 등록에는 `BuildingFootprint`, `GridPosition`, `Direction`도 필요하다. 드론은 `DroneSchedulingUtility.IsStorage`로 활성 Storage/DroneStation/MainFacility의 보관 구성을 검사하여 공급원·보관처로 선택한다. 별도의 공급원 등록 요청을 사용하지 않으며 Miner/Crafter의 Product 버퍼는 현재 드론 공급원이 아니다. MainFacility는 태그 유무와 별개로 철거 Command에서 거부한다.
- **수명:** 생성 후 타입을 교체하는 일반 Writer는 없다. 공사 완료는 기존 현장의 타입 변경이 아니라 새 건물 생성과 기존 현장 삭제다. 철거 또는 현장 취소/완료 때 엔티티와 함께 제거되며, 남은 엔티티는 World 종료 수명을 따른다.
- **근거:** [정의](../../../Assets/Scripts/Components/Buildings/BuildingComponents.cs), [공통 생성](../../../Assets/Scripts/Common/BuildingLifecycleUtility.cs), [철거 검증](../../../Assets/Scripts/Systems/Command/BuildingDemolitionCommandSystem.cs), [공간 등록](../../../Assets/Scripts/Systems/Synchronization/BuildingSpatialSyncSystem.cs), [공사 소비](../../../Assets/Scripts/Systems/Construction/StateApply/ConstructionLifecycleApplySystem.cs), [드론 공급원·보관처 제한](../../../Assets/Scripts/Common/DroneSchedulingUtility.cs).

## BuildingFootprint

- **종류·부착 대상·목적:** 일반 `IComponentData`. 현장·완공 건물에 붙는 `Size: int2`는 방향 적용 전 기본 폭·높이다. `GridPosition`은 회전 후 점유 사각형의 좌하단 기준점으로 사용한다.
- **생성:** 배치 Command는 후보 크기를 각 축 최소 1로 정규화하여 저장한다. 완공 생성은 전달받은 크기가 유효하면 그대로 사용하고, 유효하지 않으면 통합 설정의 Footprint, 이어 타입별 기본 크기를 사용한다. 프리팹 DB 조회로 얻은 크기를 이 선택에 사용하지 않는다. 공사 완료는 현장의 `Size`를 새 건물로 전달한다.
- **읽기·계산:** `BuildingFootprintUtility.GetEffectiveSize`가 각 축 최소 1 및 Left/Right에서 폭·높이 교환을 처리한다. Decision의 채굴기는 이 영역의 자원을 찾고, Storage/Product 출고 Decision은 회전된 크기로 출구를 계산한다. Synchronization의 건물 공간 등록도 같은 계산으로 점유 셀을 등록한다.
- **단계 경계:** 현장 생성은 EndCommand, 완공 건물 생성은 EndBuilding에 반영된다. 이후 매 Synchronization에서 점유 크기를 다시 읽는다. 저장된 크기에 미리 방향 회전을 적용하지 않는다.
- **수명·결합:** 생성 이후 크기를 갱신하는 런타임 시스템은 현재 없다. 엔티티 삭제 시 함께 제거된다. 배치 후보의 크기 검증, 현장의 기본 크기, 완공으로 전달되는 크기, 각 Reader의 한 번 회전이 연결되어 있다.
- **근거:** [정의](../../../Assets/Scripts/Components/Buildings/BuildingComponents.cs), [크기 계산](../../../Assets/Scripts/Common/BuildingFootprintUtility.cs), [공통 생성](../../../Assets/Scripts/Common/BuildingLifecycleUtility.cs), [채굴](../../../Assets/Scripts/Systems/Buildings/Decision/MinerDecisionSystem.cs), [보관품 출고](../../../Assets/Scripts/Systems/Buildings/Decision/StorageItemOutputDecisionSystem.cs), [생산품 출고](../../../Assets/Scripts/Systems/Buildings/Decision/ProductItemOutputDecisionSystem.cs), [공간 등록](../../../Assets/Scripts/Systems/Synchronization/BuildingSpatialSyncSystem.cs).

## IndestructibleBuilding

- **종류·부착 대상·목적:** 필드 없는 일반 `IComponentData` 태그. 부착된 건물을 철거 요청의 승인 대상에서 제외한다.
- **생성:** `BuildingLifecycleUtility.AttachTypeSpecificComponents`가 MainFacility 생성 시 EndBuilding ECB로 부착한다. 다른 타입에 자동 부착하는 현재 생산 경로는 없다.
- **읽기:** Command의 `BuildingDemolitionCommandSystem`이 태그 존재를 조회한다. 유효 타입이어도 이 태그가 있으면 요청 삭제를 EndCommand에 기록한다.
- **처리 경계:** MainFacility 타입은 태그와 별도로 명시적으로 거부한다. StateApply의 철거 Job은 Command에서 끝난 정책 검증을 다시 수행하지 않으므로, 승인 이후 대상·철거 조건을 유지하는 요청 계약이 적용된다.
- **수명:** enableable이 아니며 켜고 끄는 상태도 아니다. 런타임 부착 해제 시스템은 없고, 대상 엔티티 또는 World 수명까지 유지된다. 게임 내 철거 요청이 아닌 모든 외부 삭제를 방지하는 장치는 아니다.
- **근거:** [정의](../../../Assets/Scripts/Components/Buildings/BuildingComponents.cs), [MainFacility 구성](../../../Assets/Scripts/Common/BuildingLifecycleUtility.cs), [Command 검증](../../../Assets/Scripts/Systems/Command/BuildingDemolitionCommandSystem.cs), [StateApply 소비](../../../Assets/Scripts/Systems/Buildings/StateApply/BuildingLifecycleApplySystem.cs).

## PlacementStamp

- **종류·부착 대상·목적:** 일반 `IComponentData`. 현장·완공 건물에 붙는 `Tick: ulong`, `Order: uint`로 배치 순서 비교에 쓰는 메타데이터다. `IsEarlierThan`은 Tick, 이어 Order가 작은지 비교한다.
- **생성:** 배치 Command는 후보 `RequestTick > 0`, 요청 헤더 `RequestTick > 0`, 시스템 내부 `_currentTick` 순으로 Tick을 선택하고, 후보 버퍼의 인덱스를 Order로 저장한다. 내부 Tick은 1로 시작해 배치 시스템이 인덱스 준비 조건을 통과한 업데이트마다 증가한다. 여러 요청 전체를 위한 별도 전역 Order 발급은 없다.
- **전달:** 현장 완공은 존재하는 Stamp를 그대로 새 건물에 전달하고, 없으면 default를 전달한다. 직접 스폰은 요청의 `Stamp`를 사용한다. 동일 벨트 덮어쓰기 경로는 Direction만 바꾸며 Stamp를 새로 부여하지 않는다.
- **읽기:** BuildingDecision의 Splitter/Merger는 인접 벨트 후보의 Stamp를 읽는다. BuildingReservation의 `BeltDestinationReservationSystem`은 출고·라우팅 후보의 출처 Stamp로 목적지 경합 우선순위를 정한다. 드론의 `DroneSchedulingUtility`도 공급 현장 우선순위와 경로 거리 동률의 현장/보관처 비교에 Stamp를 사용한다. 드론 작업 생성 순번·적재품의 최초 순서는 별도 데이터다.
- **비교 경계:** 목적지 예약은 Stamp가 있는 후보를 우선하고 Tick/Order 동률 또는 둘 다 미부착이면 `SourceEntity.Index`를 사용한다. 따라서 모든 비교가 Entity 값과 완전히 독립적이라고 설명할 수 없다. 생성 후 Stamp를 변경하는 런타임 Writer는 없다.
- **수명·근거:** 건물/현장 삭제까지 유지된다. [정의](../../../Assets/Scripts/Components/Buildings/PlacementStamp.cs), [발급](../../../Assets/Scripts/Systems/Command/BuildingPlacementCommandSystem.cs), [완공 승계](../../../Assets/Scripts/Systems/Construction/StateApply/ConstructionLifecycleApplySystem.cs), [Splitter](../../../Assets/Scripts/Systems/Buildings/Decision/SplitterDecisionSystem.cs), [Merger](../../../Assets/Scripts/Systems/Buildings/Decision/MergerDecisionSystem.cs), [목적지 예약과 동률 비교](../../../Assets/Scripts/Systems/Buildings/Reservation/BeltDestinationReservationSystem.cs), [드론 순서·거리 동률 비교](../../../Assets/Scripts/Common/DroneSchedulingUtility.cs).

## BuildingConfig

- **종류·부착 대상·목적:** 필드 없는 일반 `IComponentData`. 건물 인스턴스가 아니라 건물 통합 설정 엔티티를 식별하는 싱글톤 태그다.
- **생성:** Initialization의 managed `BuildingConfigInitSystem.OnCreate`가 기존 싱글톤이 없을 때 Resources의 BuildingConfig를 로드·검증하고 `BuildingConfigLoader.PublishConfig`로 즉시 생성한다. 실패하면 게시하지 않는다. 같은 파일의 `BuildingConfigLoadSystem`은 이 태그를 기다렸다가 한 번 실행 후 비활성화할 뿐 실제 파일을 읽지 않는다.
- **결합:** 한 엔티티에 `BuildingConfigElement`, `BuildingConstructionMaterialElement`, 호환 태그 `BuildingRuntimeConfig`와 `BuildingRuntimeConfigElement`가 함께 붙는다. `PublishConfig` 자체는 기존 싱글톤 중복 검사를 하지 않으며, 자동 초기화 진입점이 기존 태그를 검사한다.
- **읽기:** Command 배치는 이 엔티티에서 해금·자재 버퍼를 얻는다. StateApply의 건물 직접 생성/철거와 공사 완료는 태그와 설정 버퍼를 가진 싱글톤 엔티티를 얻어 스펙·환급 자재를 조회한다.
- **수명·처리 경계:** 프레임 요청처럼 소비하지 않으며 ECB를 거치지 않고 초기 게시한다. Init 시스템이 비활성화되어도 데이터는 남는다. 런타임 삭제/재로드 시스템은 현재 없고 ECS 엔티티·버퍼가 World 종료 시 정리된다. 이 태그만으로 배치/생성 전체를 강제 차단하지 않으며 각 소비자에 설정 부재 분기가 있다.
- **근거:** [정의](../../../Assets/Scripts/Components/Buildings/BuildingConfigComponents.cs), [초기화 두 시스템](../../../Assets/Scripts/Systems/Initialization/BuildingConfigLoadSystem.cs), [게시 함수](../../../Assets/Scripts/Config/BuildingConfigLoader.cs), [배치](../../../Assets/Scripts/Systems/Command/BuildingPlacementCommandSystem.cs), [건물 수명주기](../../../Assets/Scripts/Systems/Buildings/StateApply/BuildingLifecycleApplySystem.cs).

## BuildingConfigElement

- **종류·부착 대상·목적:** `IBufferElementData`. 통합 설정 엔티티에 건물 종류별 `Speed`, `StorageCapacity`, `IsUnlocked`, `RequiredResearch`, 방향 적용 전 `Footprint`를 보관한다.
- **생성:** `BuildingConfigLoader`가 JSON을 파싱·검증한 목록을 `PublishConfig`에서 버퍼에 복사한다. 초기 게시 시 호환 런타임 버퍼에도 Speed/StorageCapacity를 복사한다. 생성자의 기본 Footprint 처리는 `int2.zero`일 때 `(1,1)`로 바꾸는 규칙이다.
- **읽기:** Command의 배치 검증은 IsUnlocked를 조회한다. StateApply의 공통 건물 생성은 Speed/StorageCapacity/Footprint를 읽어 타입별 런타임 상태를 초기화한다. 이미 생성된 건물이 매 프레임 이 버퍼의 Speed를 다시 읽는 구조는 아니다.
- **쓰기·현재 연결:** 공개 `BuildingConfigLookupUtility.SetBuildingUnlocked`가 특정 행의 IsUnlocked를 변경할 수 있지만 `Assets/Scripts`에 이를 호출하는 연구 진행 시스템은 없다. RequiredResearch 문자열의 존재만으로 연구가 진행되거나 자동 해금되지는 않는다.
- **조회·수명 경계:** 조회 유틸리티는 BuildingType의 첫 일치 행을 사용한다. 실제 배치 호출의 설정 NativeArray가 비어 있으면 해금 검사를 통과시키며, 비어 있지 않을 때 미등록 타입은 미해금이다. 생성 시 설정이 없거나 타입을 찾지 못하면 공통 생성의 기본 스펙을 사용한다. 버퍼는 소비·Clear되지 않고 설정 엔티티와 함께 유지된다.
- **근거:** [정의](../../../Assets/Scripts/Components/Buildings/BuildingConfigComponents.cs), [로드·게시](../../../Assets/Scripts/Config/BuildingConfigLoader.cs), [조회·해금 변경 API](../../../Assets/Scripts/Common/BuildingConfigLookupUtility.cs), [배치 검증](../../../Assets/Scripts/Common/BuildingPlacementValidationUtility.cs), [생성 시 스펙 사용](../../../Assets/Scripts/Common/BuildingLifecycleUtility.cs).

## BuildingConstructionMaterialElement

- **종류·부착 대상·목적:** `IBufferElementData`. 통합 설정 엔티티에서 `(BuildingType, ItemType, Quantity)`로 종류별 건축 비용을 정의한다. 현장별 Delivered/Reserved 상태와 구별되는 원본 요구량이다.
- **생성:** 설정 로더가 검증한 자재 목록을 `PublishConfig`가 초기화 시 직접 게시한다. 건물 인스턴스마다 붙이지 않는다.
- **배치에서 읽기:** Command는 설정 버퍼를 임시 NativeArray로 복사하고, 승인된 현장마다 `PopulateRequirements`로 해당 건물의 행을 `ConstructionMaterialRequirementElement`로 만든다. 새 현장의 도착량·예약량은 0이며 EndCommand에 현장과 함께 생성된다.
- **철거에서 읽기:** StateApply의 `DemolishBuildingApplyJob`은 철거 대상 종류의 각 행을 조회하고 Quantity만큼 신규 월드 아이템을 환급 생성한다. 기존 Stored/Product 실물 반환과 별도의 처리다. 환급 프리팹이 없으면 해당 환급 생성은 생략하고 중단 오류를 기록하며 철거 명령 자체는 계속 기록한다.
- **수명·결합:** 설정 버퍼를 소비하거나 차감하지 않는다. 현장의 요구량은 배치 때 복사된 상태이고 철거 환급은 철거 시점 설정을 조회한다. 설정 엔티티/World 수명 동안 유지되며, 품목별 프리팹 DB와 결합해 환급 실물을 만든다.
- **근거:** [정의](../../../Assets/Scripts/Components/Buildings/BuildingConfigComponents.cs), [게시](../../../Assets/Scripts/Config/BuildingConfigLoader.cs), [요구량 복사](../../../Assets/Scripts/Common/BuildingConfigLookupUtility.cs), [배치](../../../Assets/Scripts/Systems/Command/BuildingPlacementCommandSystem.cs), [철거 환급](../../../Assets/Scripts/Systems/Buildings/StateApply/BuildingLifecycleApplySystem.cs).

## BuildingRuntimeConfig

- **종류·부착 대상·목적:** 필드 없는 일반 `IComponentData`. 통합 설정 엔티티에 함께 붙는 호환용 런타임 설정 식별 태그다.
- **생성:** `BuildingConfigLoader.PublishConfig`가 BuildingConfig 및 세 버퍼와 같은 엔티티에 직접 생성한다. 별도 런타임 설정 엔티티를 자동으로 만들지 않는다.
- **현재 처리:** `Assets/Scripts`의 현재 시스템은 이 태그를 쿼리해서 스펙을 소비하지 않는다. 건물 생성은 `BuildingConfig + BuildingConfigElement`를 조회한다. 따라서 생성되는 호환 데이터이지만 현재 게임 시스템의 스펙 조회 진입점은 아니다.
- **쓰기·수명:** 초기 부착 이후 값을 쓸 필드가 없고 제거·재생성·Clear 과정도 없다. 일반 설정 엔티티와 World 수명을 따른다.
- **결합·근거:** `BuildingRuntimeConfigElement`가 같은 엔티티에 있어야 호환 버퍼를 읽을 수 있다. [정의](../../../Assets/Scripts/Components/Buildings/BuildingRuntimeConfigComponents.cs), [게시 경로](../../../Assets/Scripts/Config/BuildingConfigLoader.cs), [실제 생성 소비자의 쿼리](../../../Assets/Scripts/Systems/Buildings/StateApply/BuildingLifecycleApplySystem.cs).

## BuildingRuntimeConfigElement

- **종류·부착 대상·목적:** `IBufferElementData`, `InternalBufferCapacity(8)`. 통합 설정 엔티티에서 건물 종류별 Speed/StorageCapacity의 호환 표현을 보관한다. 용량 8은 전체 행 개수 상한이 아니다.
- **생성:** 통합 설정을 게시할 때 `BuildingConfigElement`마다 BuildingType/Speed/StorageCapacity를 복사하여 버퍼에 추가한다. Footprint·해금·건축 비용 필드는 포함하지 않는다.
- **현재 읽기:** `BuildingConfigLookupUtility.TryGetConfig`의 호환 버퍼 오버로드가 존재하지만, 현재 `Assets/Scripts` 시스템에서 이 버퍼를 얻어 호출하는 소비 경로는 없다. 테스트의 직접 구성/조회와 런타임 호출을 구분한다.
- **쓰기·동기화:** 초기 복사 이후 두 설정 버퍼를 계속 동기화하는 시스템은 없다. 현재 공통 건물 생성의 원본은 BuildingConfigElement다.
- **수명·근거:** 일회성 결과 버퍼가 아니므로 Clear·소비되지 않고 설정 엔티티/World 수명을 따른다. [정의](../../../Assets/Scripts/Components/Buildings/BuildingRuntimeConfigComponents.cs), [복사 게시](../../../Assets/Scripts/Config/BuildingConfigLoader.cs), [호환 조회 API](../../../Assets/Scripts/Common/BuildingConfigLookupUtility.cs), [설정 테스트 소스](../../../Assets/Editor/Tests/Phase3BuildingRuntimeConfigTests.cs).

## SpawnBuildingRequest

- **종류·부착 대상·목적:** 일반 `IComponentData`이자 `IRequestComponent`. 별도 일회성 요청 엔티티에 TargetType/Position/Direction/FootprintSize/Stamp를 담아 완공 건물을 직접 생성한다. 배치 타당성 검사나 공사 비용 납부를 수행하는 요청은 아니다.
- **생성 경로:** 현재 `Assets/Scripts`에는 이 요청을 만드는 게임플레이 Producer가 없다. 직접 생성하는 호출은 테스트에서 확인된다. 실제 공사 완료는 요청을 발행하지 않고 `BuildingLifecycleUtility.SpawnBuilding`을 바로 호출한다.
- **소비·순서:** StateApply의 `BuildingLifecycleApplySystem`이 철거 Job 및 벨트 아이템 정리 Job 뒤에 Spawn Job을 연결한다. 공통 생성 함수가 프리팹 인스턴스화와 공통/타입별 구성을 같은 EndBuilding ECB에 기록한다. 시스템은 Routing/Storage Apply 이후이며 Construction Lifecycle은 같은 건물 StateApply의 OrderLast다. 건물 생성/삭제 결과는 EndBuilding 뒤 같은 틱 드론에 보인다.
- **실패·소비:** None/ConstructionSite 대상은 Null을 반환한다. 등록 프리팹을 찾지 못하면 SimulationFatalError 기록 후 Null을 반환한다. Spawn Job은 반환 성공 여부에 관계없이 요청 삭제를 기록하므로 이 요청 자체는 자동 재시도하지 않는다.
- **수명·반영:** 새 건물과 요청 삭제는 EndBuilding에 반영되고 다음 Synchronization이 공간을 등록한다. 요청이 소비되기 전까지는 일반 컴포넌트이며 enable/disable로 재사용하지 않는다. 입력 Stamp와 기본 Footprint는 공통 생성의 선택 규칙에 따라 전달된다.
- **근거:** [정의](../../../Assets/Scripts/Components/Buildings/BuildingRequests.cs), [Spawn 소비](../../../Assets/Scripts/Systems/Buildings/StateApply/BuildingLifecycleApplySystem.cs), [공통 생성](../../../Assets/Scripts/Common/BuildingLifecycleUtility.cs), [공사 완료 직접 호출](../../../Assets/Scripts/Systems/Construction/StateApply/ConstructionLifecycleApplySystem.cs), [테스트 생성 사례](../../../Assets/Editor/Tests/Phase7BuildingLifecycleTests.cs).

## DemolishBuildingRequest

- **종류·부착 대상·목적:** 일반 IComponentData/IRequestComponent. 별도 요청 엔티티의 TargetBuilding으로 완공 건물 철거를 요청한다. 이름 변경 제안은 철회했으며 현장 취소의 CancelConstructionRequest와 분리한다.
- **생성·검증:** 현재 제품 입력/UI Producer는 없고 테스트가 직접 만든다. Command 검증 전 실체화한다. Null/BuildingType 부재, Prefab 원형, None/ConstructionSite/MainFacility, IndestructibleBuilding과 이미 승인된 대상은 거부한다. 같은 업데이트의 승인 대상 HashSet으로 중복을 막는다.
- **Producer→상태:** BuildingDemolitionCommandSystem이 승인 건물에 PendingBuildingDemolition 부착을 기록한다. Stored/Product 실물의 기존 Transfer 요청을 즉시 비활성화하고 이전 ProductResult를 Clear한다. EndCommand에서 상태 부착과 승인/거부/중복 요청 삭제를 확정한다.
- **동작 경계:** EndCommand 이후 입고/생산/출고/벨트 이동·진입/분배·합류 계획을 만들지 않는다. 같은 틱 재배치는 기존 공간 인덱스 점유로 거부한다. Item Lifecycle/Ownership은 이 요청을 조회하거나 복사하지 않는다.
- **수명·변경 금지:** 승인 대상과 철거 조건은 StateApply까지 유지한다. 구조적 삭제가 즉시 일어나는 것이 아니며 실제 반환/환급/철거는 승인 상태의 수명주기를 따른다. 거부된 요청은 자동 재시도하지 않는다.
- **근거:** [요청 정의](../../../Assets/Scripts/Components/Buildings/BuildingRequests.cs), [Command 승인·소비](../../../Assets/Scripts/Systems/Command/BuildingDemolitionCommandSystem.cs), [핵심 회귀](../../../Assets/Editor/Tests/Phase7BuildingDemolishTests.cs), [생성 입고·생산 중단](../../../Assets/Editor/Tests/Phase7ItemCreationDemolitionTests.cs).

## PendingBuildingDemolition

- **종류·부착 대상·목적:** 필드 없는 일반 IComponentData. Command에서 철거가 승인된 완공 건물에 붙어 요청과 승인 상태를 구분한다. 공사 현장에는 부착하지 않는다.
- **Writer·게시:** BuildingDemolitionCommandSystem만 승인 태그를 기록하며 EndCommand부터 Reader에 보인다. 동기화 이전에도 ECS Lookup으로 상태를 읽을 수 있다. 같은 틱 재배치를 위해 공간 맵을 조기에 수정하지 않는다.
- **Reader·중단:** 입고 Decision은 철거 목적지와 철거 출발 벨트를 제외한다. 저장/생산품 출고와 채굴/제작 Decision은 승인 건물의 이전 결정을 초기화하고 비활성화한다. 벨트 이동은 현재 철거 벨트에서 정지하고 다음 철거 벨트의 경계 직전까지만 접근한다. Splitter/Merger는 철거 라우터·입력/출력 벨트를 후보에서 제외한다. ItemSpawnAdmissionDecisionSystem은 해당 Storage/Product 요청을 비활성화해 생성 후보에서 빼고 EndBuilding에 삭제한다.
- **StateApply·소비:** BuildingLifecycleApplySystem이 승인 태그로 대상을 조회하며 철거 정책을 재검증하지 않는다. BuildingType 소실만 방어하고 이 경우 태그만 제거한다. 유효 대상의 Stored/Product 실물 반환, 건축 비용 환급, 벨트 아이템 정지를 기록하고 EndBuilding에서 건물·태그를 함께 삭제한다. 기존 Disabled 대상의 적용 범위도 유지한다.
- **반환·실패:** 실물은 새로 생성하지 않고 Owner/렌더/좌표를 건물 위치의 월드 상태로 되돌린다. 활성 Destroy는 반환하지 않는다. 환급은 설정된 비용을 신규 생성하며 프리팹 누락 실패 계약은 기존과 같다. 앞선 틱에 소비한 제작 재료·광물의 별도 보상이나 취소/승인 철회는 추가하지 않았다.
- **근거:** [상태 정의](../../../Assets/Scripts/Components/Buildings/BuildingComponents.cs), [승인](../../../Assets/Scripts/Systems/Command/BuildingDemolitionCommandSystem.cs), [반환·철거](../../../Assets/Scripts/Systems/Buildings/StateApply/BuildingLifecycleApplySystem.cs), [생성 후보 차단](../../../Assets/Scripts/Systems/Items/Decision/ItemSpawnAdmissionDecisionSystem.cs), [검증 기록](../../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/BuildingDemolitionStop-Verification.md).
