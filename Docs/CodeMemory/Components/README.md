# ECS 컴포넌트 색인

이 문서는 컴포넌트를 출발점으로 Planet Miner의 현재 데이터 흐름을 따라가기 위한 지도다. 각 하위 문서에서 **목적 → 부착 엔티티 → 생성과 초기화 → 읽고 쓰는 시스템 → 처리와 반영 시점 → 소비와 종료**를 확인할 수 있다. 함께 유지하는 데이터 관계와 조건도 해당 컴포넌트에 기록했다.

최초 조사는 **2026-10-03**, 조사 시작 커밋은 `0998016624ee15ee035beb8dc306d68990a69bb0`이다. **2026-10-06 현장 외부 방출·예정 월드 기록 제거까지**의 정의와 경로로 갱신했다. **프로젝트 ECS 컴포넌트/버퍼 요소 102개를 9개 하위 문서에 정리**하며 드론은 22개다. 현장 내부의 새 월드 생성·방출은 금지하고 완공은 현재 월드 실물과 활성 Destroy만 조회한다. 실제 수행부·관측/경로 계산·공통 능력/연구 Writer는 후속이다. 소스와 실행 검증을 구분한다.

드론 관리 1~4단계의 작업·예약·인계·Direct 재배정·현장 외부 방출·완공 차단과 5단계 선별 통합 회귀를 연결했다. [기존 142사례](../../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/DroneSafeDropAndJournalRemoval-Verification.md)와 [신규 4사례](../../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/DroneLifecycleIntegration-Verification.md)는 별도 실행이다. 실제 수행부·관측/경로·초기 능력/연구 Writer는 후속이며 다음 추천은 [공통 적재량 초기화 검토안](../../Specifications/DroneCapacityInitializationPlan.md)이다. 설정 출처·오류·기존 singleton 정책은 미확정이므로 구현 승인으로 해석하지 않는다. 세부 계약은 [명세](../../Specifications/ConstructionAndDroneSupply.md)와 [드론 컴포넌트 계약](DroneLogistics.md)을 따른다.

## 읽는 방법

5단계의 신규 수명주기 통합 4사례와 정상/방어 fixture 범위는 [통합 검증 기록](../../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/DroneLifecycleIntegration-Verification.md)을 따른다. 앞선 실행 기록과 합산하지 않으며 실제 이동·경로/관측 Producer는 후속이다.

1. 이름을 알고 있으면 아래 전체 색인에서 타입을 선택한다. 각 링크가 그 타입의 독립 설명으로 이동한다.
2. 이름을 모르면 도메인 목록이나 엔티티별 진입점을 사용한다.
3. 처리 경계는 다음 단계 설명과 함께 읽는다. 같은 그룹의 시스템 이름을 나열한 것만으로 실행 순서가 보장되지는 않는다.
4. 코드 확인은 각 항목의 소스 링크를 사용한다. 문서 이후 코드가 변경되면 현재 소스를 우선한다.

## 문서 구성

| 하위 문서 | 타입 수 | 설명 범위 |
| --- | ---: | --- |
| [공통 좌표와 공간 인덱스](CommonAndSpatial.md) | 10 | GridPosition/Direction, 공간 조회 맵과 Job 의존성 |
| [건물과 건물 설정](Buildings.md) | 12 | 건물 식별·크기·배치 순번, 설정, 직접 생성과 철거 요청 |
| [배치와 공사](Construction.md) | 5 | 배치 묶음, 현장, 자재 요구 데이터, 현장 취소·완공 |
| [드론 작업 관리 계약](DroneLogistics.md) | 22 | 작업·배정·예약·공통 적재량, 인계 계획·정산·적재품 Direct 재배정·외부 결과 소비 |
| [아이템과 저장](ItemsAndStorage.md) | 13 | 아이템·소유권·요청, 보관·입출고·입력 슬롯, 현장 내부 World Spawn 거부 |
| [벨트와 분배 및 합류](BeltsAndRouting.md) | 6 | 벨트 속도·실제 이동·프레임 계획, 라우팅 상태·전달 결정 |
| [채굴과 제작](Production.md) | 12 | 작업 상태·결정, 레시피 설정, 결과와 출력 대기 실물 |
| [월드와 청크 및 자원](WorldAndResources.md) | 12 | 월드·바닥 설정, 청크 요청·생성 기록, 자원 노드 |
| [프리팹과 시뮬레이션 시작 상태](Prefabs.md) | 10 | 프리팹 DB, 준비 태그, 중단 오류, 드론 프리팹 정의 |

## 엔티티에서 찾아가기

| 엔티티 범주 | 대표 부착 데이터와 진입점 |
| --- | --- |
| 월드 또는 보관 아이템 | [ItemIdentity](ItemsAndStorage.md#itemidentity), [ItemOwnership](ItemsAndStorage.md#itemownership), [GridPosition](CommonAndSpatial.md#gridposition), 아이템 자체의 [이동 상태](BeltsAndRouting.md#beltmovementstate)와 [입고 결정](ItemsAndStorage.md#buildingiteminputdecision) |
| 완공 건물 | [BuildingType](Buildings.md#buildingtype), [BuildingFootprint](Buildings.md#buildingfootprint), [PlacementStamp](Buildings.md#placementstamp), GridPosition/Direction과 타입별 저장·생산·라우팅 데이터 |
| 공사 현장 | [ConstructionSite](Construction.md#constructionsite), [ConstructionMaterialRequirementElement](Construction.md#constructionmaterialrequirementelement), StoredItemElement, 건물 공통 위치·크기·방향 |
| 드론 작업·배정·최소 수행자 계약 | [DroneLogisticsTask](DroneLogistics.md#dronelogisticstask)는 Execution이, [DroneTaskAssignment](DroneLogistics.md#dronetaskassignment)는 Publish가 생성 명령을 기록하며 EndStateApply에 실체화한다. [DroneWorker](DroneLogistics.md#droneworker)는 수행자 표시이고 [DroneCapacityState](DroneLogistics.md#dronecapacitystate)는 World 공통 적재량이다. 실제 등록/생성과 용량 초기화·연구 갱신은 후속이다. |
| 건물의 저장품과 생산품 목록 | 건물에 붙는 [StoredItemElement](ItemsAndStorage.md#storeditemelement)와 [ProductItemElement](Production.md#productitemelement). 버퍼 요소가 참조하는 실물 아이템은 별도 엔티티다. |
| 일회성 요청 엔티티 | [SpawnBuildingRequest](Buildings.md#spawnbuildingrequest), [DemolishBuildingRequest](Buildings.md#demolishbuildingrequest), [SpawnItemRequest](ItemsAndStorage.md#spawnitemrequest), [BuildingPlacementRequest](Construction.md#buildingplacementrequest), 공사·레시피 요청 |
| 자원 노드 | [ResourceNode](WorldAndResources.md#resourcenode), GridPosition. 현재 매장량은 설정/프리팹 DB와 분리된다. |
| 설정·프리팹·공간 조회 싱글톤 | [ItemRegistry](ItemsAndStorage.md#itemregistry), [RecipeRegistry](Production.md#reciperegistry), [BuildingConfig](Buildings.md#buildingconfig), [월드 설정](WorldAndResources.md#resourcegenerationsettings), [프리팹 DB](Prefabs.md), [공간 인덱스](CommonAndSpatial.md#공간-인덱스의-공통-처리) |

대표 조합을 보여 주는 표이며 모든 엔티티의 전체 archetype 목록은 아니다. 타입별 부착 조건과 생성 경로는 하위 문서에 명시한다.

## 실행 단계와 데이터가 확정되는 시점

Initialization은 설정을 게시하고 프리팹 DB를 검증한다. [GameSimulationGroup](../../../Assets/Scripts/Phases/GameSimulationGroup.cs)은 PrefabDatabaseReady가 있고 SimulationFatalError가 없을 때 다음 여섯 단계를 실행한다.

```mermaid
flowchart LR
    C["Command"] --> D["Decision"]
    D --> R["Reservation"]
    R --> E["Execution"]
    E --> A["StateApply"]
    A --> S["Synchronization"]
```

| 경계 | 컴포넌트를 읽을 때 구분할 현재 동작 |
| --- | --- |
| Command 종료 | EndCommand ECB가 자원·공사 현장 생성과 해당 요청 소비 등을 재생한다. 엔티티가 실체화되는 시점과 공간 인덱스 등록 시점은 다르다. |
| Decision → Reservation | 프레임 후보를 작성한 뒤 저장 슬롯/외부 벨트 목적지 경합을 중재한다. 모든 Decision이 예약 단계를 거치는 것은 아니다. |
| Execution | OrderFirst의 드론 인계 시스템이 이번 물류 쓰기 전 실물·수량·슬롯 계획을 확정한다. 기존 실행 시스템은 벨트 좌표·진행도, 채굴/제작 진행·선소비와 ProductResult를 기록한다. |
| StateApply 내부 | 일반 입출고·Ownership 요청 적용 이후 DroneLifecycle의 행동 반영이 이전 계획을 현재 원본으로 검사한다. 공통 Ownership API가 실물의 버퍼·Owner·위치·벨트·렌더를 반영하고 Lifecycle이 성공분·배정·도착량/예약을 정산한다. 이후 완공을 판정한다. 이번 틱 새 입력을 계획에 더하지 않는다. 전체 Apply를 한 고정 체인으로 가정하지 않는다. |
| StateApply 종료 | EndStateApply ECB가 생성·삭제·지연 컴포넌트 변경을 재생한다. ECB로 넘긴 소유권과 보관 버퍼의 최종 일치는 이 경계 이후에 해석한다. |
| Synchronization | 공간 인덱스를 원본 ECS 상태로 재구축한다. 개발용 WorldInvariantValidationSystem이 마지막에 현재 구현된 검사를 수행한다. |

StateApply는 저장/라우팅 → 일반 Ownership → DroneLifecycle 인계를 유지한다. Construction은 DroneLifecycle 이후 현재 월드 Owner/GridPosition과 활성 Destroy로 차단/완공을 판단한다. Item/Building Lifecycle 선행 제약과 예정 위치 버퍼는 제거했다. 현장 내부 World Spawn은 Decision·최종 Apply가 거부한다. Publish는 일반 Apply 뒤·ECB 재생 전이다. 근거: [드론](../../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs), [완공](../../../Assets/Scripts/Systems/5_StateApply/ConstructionLifecycleApplySystem.cs), [위치 허용 검사](../../../Assets/Scripts/Common/ConstructionSiteWorldItemUtility.cs), [World Spawn](../../../Assets/Scripts/Systems/2_Decision/ItemSpawnAdmissionDecisionSystem.cs).

## 생명주기 표기

- **일반 컴포넌트:** 엔티티 상태/설정/참조를 유지한다. 이름이 Decision이어도 enableable이 아닐 수 있다.
- **enableable:** 부착 여부와 활성 여부가 별개다. 처리 후 비활성화하는 타입과 다음 Decision에서 덮어쓰는 타입을 각 항목에서 구분한다.
- **버퍼 요소:** 버퍼가 붙은 엔티티가 목록을 소유한다. 버퍼 Clear, 요소 제거, 버퍼 참조 대상 엔티티 파괴는 서로 다른 동작이다.
- **요청:** 별도 요청 엔티티를 삭제하는 경로와 기존 엔티티에서 요청 컴포넌트를 비활성화하는 경로가 있다. 준비 대기로 유지하는 조건도 별도다.
- **정의만 존재 또는 생성 경로 없음:** 현재 `Assets/Scripts`에서 확인한 연결 상태다. 테스트의 직접 구성은 제품 런타임 Producer가 구현되어 있다는 증거로 사용하지 않았다.

`IRequestComponent`/`IEnableableRequest` 인터페이스와 enum·일반 struct는 83개에 세지 않았다. Unity 제공 `LocalTransform`/`Prefab`/`DisableRendering` 등은 연결 설명에 포함하고 독립 조사 타입에서는 제외했다. MonoBehaviour/Authoring은 ECS 생성 출처나 Reader일 때만 설명한다.

## 전체 컴포넌트 색인

종류의 **상태**는 일반 IComponentData, **활성 상태**는 IEnableableComponent 구현, **버퍼**는 IBufferElementData를 뜻한다. 활성 상태에는 IEnableableRequest의 간접 구현도 포함했다.

### 공통 좌표와 공간 인덱스

| 컴포넌트 상세 | 종류 | 정의 파일 |
| --- | --- | --- |
| [BeltSpatialIndex](CommonAndSpatial.md#beltspatialindex) | 상태 | [BeltSpatialIndex.cs](../../../Assets/Scripts/Components/Belts/BeltSpatialIndex.cs) |
| [BeltSpatialIndexFence](CommonAndSpatial.md#beltspatialindexfence) | 상태 | [BeltSpatialIndex.cs](../../../Assets/Scripts/Components/Belts/BeltSpatialIndex.cs) |
| [BuildingSpatialIndex](CommonAndSpatial.md#buildingspatialindex) | 상태 | [BuildingSpatialIndex.cs](../../../Assets/Scripts/Components/Buildings/BuildingSpatialIndex.cs) |
| [BuildingSpatialIndexFence](CommonAndSpatial.md#buildingspatialindexfence) | 상태 | [BuildingSpatialIndex.cs](../../../Assets/Scripts/Components/Buildings/BuildingSpatialIndex.cs) |
| [Direction](CommonAndSpatial.md#direction) | 상태 | [GridComponents.cs](../../../Assets/Scripts/Components/Common/GridComponents.cs) |
| [GridPosition](CommonAndSpatial.md#gridposition) | 상태 | [GridComponents.cs](../../../Assets/Scripts/Components/Common/GridComponents.cs) |
| [ItemSpatialIndex](CommonAndSpatial.md#itemspatialindex) | 상태 | [ItemSpatialIndex.cs](../../../Assets/Scripts/Components/Items/ItemSpatialIndex.cs) |
| [ItemSpatialIndexFence](CommonAndSpatial.md#itemspatialindexfence) | 상태 | [ItemSpatialIndex.cs](../../../Assets/Scripts/Components/Items/ItemSpatialIndex.cs) |
| [ResourceSpatialIndex](CommonAndSpatial.md#resourcespatialindex) | 상태 | [ResourceSpatialIndex.cs](../../../Assets/Scripts/Components/Resources/ResourceSpatialIndex.cs) |
| [ResourceSpatialIndexFence](CommonAndSpatial.md#resourcespatialindexfence) | 상태 | [ResourceSpatialIndex.cs](../../../Assets/Scripts/Components/Resources/ResourceSpatialIndex.cs) |

### 건물과 건물 설정

| 컴포넌트 상세 | 종류 | 정의 파일 |
| --- | --- | --- |
| [BuildingConfig](Buildings.md#buildingconfig) | 상태 | [BuildingConfigComponents.cs](../../../Assets/Scripts/Components/Buildings/BuildingConfigComponents.cs) |
| [BuildingConfigElement](Buildings.md#buildingconfigelement) | 버퍼 | [BuildingConfigComponents.cs](../../../Assets/Scripts/Components/Buildings/BuildingConfigComponents.cs) |
| [BuildingConstructionMaterialElement](Buildings.md#buildingconstructionmaterialelement) | 버퍼 | [BuildingConfigComponents.cs](../../../Assets/Scripts/Components/Buildings/BuildingConfigComponents.cs) |
| [BuildingFootprint](Buildings.md#buildingfootprint) | 상태 | [BuildingComponents.cs](../../../Assets/Scripts/Components/Buildings/BuildingComponents.cs) |
| [BuildingRuntimeConfig](Buildings.md#buildingruntimeconfig) | 상태 | [BuildingRuntimeConfigComponents.cs](../../../Assets/Scripts/Components/Buildings/BuildingRuntimeConfigComponents.cs) |
| [BuildingRuntimeConfigElement](Buildings.md#buildingruntimeconfigelement) | 버퍼 | [BuildingRuntimeConfigComponents.cs](../../../Assets/Scripts/Components/Buildings/BuildingRuntimeConfigComponents.cs) |
| [BuildingType](Buildings.md#buildingtype) | 상태 | [BuildingComponents.cs](../../../Assets/Scripts/Components/Buildings/BuildingComponents.cs) |
| [DemolishBuildingRequest](Buildings.md#demolishbuildingrequest) | 상태 | [BuildingRequests.cs](../../../Assets/Scripts/Components/Buildings/BuildingRequests.cs) |
| [IndestructibleBuilding](Buildings.md#indestructiblebuilding) | 상태 | [BuildingComponents.cs](../../../Assets/Scripts/Components/Buildings/BuildingComponents.cs) |
| [PendingBuildingDemolition](Buildings.md#pendingbuildingdemolition) | 상태 태그 | [BuildingComponents.cs](../../../Assets/Scripts/Components/Buildings/BuildingComponents.cs) |
| [PlacementStamp](Buildings.md#placementstamp) | 상태 | [PlacementStamp.cs](../../../Assets/Scripts/Components/Buildings/PlacementStamp.cs) |
| [SpawnBuildingRequest](Buildings.md#spawnbuildingrequest) | 상태 | [BuildingRequests.cs](../../../Assets/Scripts/Components/Buildings/BuildingRequests.cs) |

### 배치와 공사

| 컴포넌트 상세 | 종류 | 정의 파일 |
| --- | --- | --- |
| [BuildingPlacementRequest](Construction.md#buildingplacementrequest) | 상태 | [BuildingPlacementRequests.cs](../../../Assets/Scripts/Components/Construction/BuildingPlacementRequests.cs) |
| [CancelConstructionRequest](Construction.md#cancelconstructionrequest) | 상태 | [ConstructionRequests.cs](../../../Assets/Scripts/Components/Construction/ConstructionRequests.cs) |
| [ConstructionMaterialRequirementElement](Construction.md#constructionmaterialrequirementelement) | 버퍼 | [ConstructionComponents.cs](../../../Assets/Scripts/Components/Construction/ConstructionComponents.cs) |
| [ConstructionSite](Construction.md#constructionsite) | 상태 | [ConstructionComponents.cs](../../../Assets/Scripts/Components/Construction/ConstructionComponents.cs) |
| [PlacementRequestCandidateElement](Construction.md#placementrequestcandidateelement) | 버퍼 | [BuildingPlacementRequests.cs](../../../Assets/Scripts/Components/Construction/BuildingPlacementRequests.cs) |

### 드론 작업 관리 계약

작업·후보·현장 예약·배정 경로와 실제 수행부·행동 인계의 미구현 범위를 각 항목에서 구분한다.

| 컴포넌트 상세 | 종류 | 정의 파일 |
| --- | --- | --- |
| [DroneLogisticsTask](DroneLogistics.md#dronelogisticstask) | 상태 | [DroneTaskComponents.cs](../../../Assets/Scripts/Components/DroneLogistics/DroneTaskComponents.cs) |
| [DroneTaskSequence](DroneLogistics.md#dronetasksequence) | 상태 | [DroneTaskSequence.cs](../../../Assets/Scripts/Components/DroneLogistics/DroneTaskSequence.cs) |
| [DroneRecoveryPending](DroneLogistics.md#dronerecoverypending) | 표시 | [DroneRecoveryPending.cs](../../../Assets/Scripts/Components/DroneLogistics/DroneRecoveryPending.cs) |
| [DroneTaskCandidateDecisionElement](DroneLogistics.md#dronetaskcandidatedecisionelement) | 후보·공개 대기 예약 버퍼 | [DroneTaskCandidateDecisionElement.cs](../../../Assets/Scripts/Components/DroneLogistics/DroneTaskCandidateDecisionElement.cs) |
| [DroneRouteDecisionElement](DroneLogistics.md#droneroutedecisionelement) | 결정 버퍼 | [DroneRouteDecisionElement.cs](../../../Assets/Scripts/Components/DroneLogistics/DroneRouteDecisionElement.cs) |
| [DroneTaskCreationDecisionElement](DroneLogistics.md#dronetaskcreationdecisionelement) | 결정 버퍼 | [DroneTaskCreationDecisionElement.cs](../../../Assets/Scripts/Components/DroneLogistics/DroneTaskCreationDecisionElement.cs) |
| [DroneTaskInvalidationDecisionElement](DroneLogistics.md#dronetaskinvalidationdecisionelement) | 무효화 결정 버퍼 | [DroneTaskInvalidationDecisionElement.cs](../../../Assets/Scripts/Components/DroneLogistics/DroneTaskInvalidationDecisionElement.cs) |
| [DroneTaskAssignment](DroneLogistics.md#dronetaskassignment) | 상태 | [DroneTaskComponents.cs](../../../Assets/Scripts/Components/DroneLogistics/DroneTaskComponents.cs) |
| [ConstructionSupplyReservation](DroneLogistics.md#constructionsupplyreservation) | 상태 | [ConstructionSupplyReservation.cs](../../../Assets/Scripts/Components/Construction/ConstructionSupplyReservation.cs) |
| [DroneWorker](DroneLogistics.md#droneworker) | 수행자 표시 | [DroneWorkerComponents.cs](../../../Assets/Scripts/Components/DroneLogistics/DroneWorkerComponents.cs) |
| [DroneCapacityState](DroneLogistics.md#dronecapacitystate) | 공통 적재량 상태 | [DroneCapacityState.cs](../../../Assets/Scripts/Components/DroneLogistics/DroneCapacityState.cs) |
| [DroneWorkerObservation](DroneLogistics.md#droneworkerobservation) | 관측 상태 | [DroneWorkerComponents.cs](../../../Assets/Scripts/Components/DroneLogistics/DroneWorkerComponents.cs) |
| [DroneWorkerAssignment](DroneLogistics.md#droneworkerassignment) | 상태 | [DroneWorkerComponents.cs](../../../Assets/Scripts/Components/DroneLogistics/DroneWorkerComponents.cs) |
| [DroneCargoState](DroneLogistics.md#dronecargostate) | 상태 | [DroneWorkerComponents.cs](../../../Assets/Scripts/Components/DroneLogistics/DroneWorkerComponents.cs) |
| [DroneRouteEvaluationRequest](DroneLogistics.md#dronerouteevaluationrequest) | 요청 | [DroneRouteContracts.cs](../../../Assets/Scripts/Components/DroneLogistics/DroneRouteContracts.cs) |
| [DroneRouteEvaluationResult](DroneLogistics.md#dronerouteevaluationresult) | 결과 | [DroneRouteContracts.cs](../../../Assets/Scripts/Components/DroneLogistics/DroneRouteContracts.cs) |
| [DroneActionReadyRequest](DroneLogistics.md#droneactionreadyrequest) | 요청 | [DroneActionContracts.cs](../../../Assets/Scripts/Components/DroneLogistics/DroneActionContracts.cs) |
| [DroneItemTransferDecision](DroneLogistics.md#droneitemtransferdecision) | 판단·수량 계획 | [DroneItemTransferDecisions.cs](../../../Assets/Scripts/Components/DroneLogistics/DroneItemTransferDecisions.cs) |
| [DroneItemTransferItemDecisionElement](DroneLogistics.md#droneitemtransferitemdecisionelement) | 실물 ID 계획 버퍼 | [DroneItemTransferDecisions.cs](../../../Assets/Scripts/Components/DroneLogistics/DroneItemTransferDecisions.cs) |
| [DroneItemTransferSlotDecisionElement](DroneLogistics.md#droneitemtransferslotdecisionelement) | 기존 슬롯 계획 버퍼 | [DroneItemTransferDecisions.cs](../../../Assets/Scripts/Components/DroneLogistics/DroneItemTransferDecisions.cs) |
| [DroneActionReceiptSequence](DroneLogistics.md#droneactionreceiptsequence) | 접수 순번 상태 | [DroneActionReceiptSequence.cs](../../../Assets/Scripts/Components/DroneLogistics/DroneActionReceiptSequence.cs) |
| [DroneItemTransferResult](DroneLogistics.md#droneitemtransferresult) | 결과 | [DroneActionContracts.cs](../../../Assets/Scripts/Components/DroneLogistics/DroneActionContracts.cs) |

### 아이템과 저장

| 컴포넌트 상세 | 종류 | 정의 파일 |
| --- | --- | --- |
| [BuildingInputSlotElement](ItemsAndStorage.md#buildinginputslotelement) | 버퍼 | [BuildingInputSlotElement.cs](../../../Assets/Scripts/Components/Storage/BuildingInputSlotElement.cs) |
| [BuildingItemInputDecision](ItemsAndStorage.md#buildingiteminputdecision) | 활성 상태 | [BuildingItemDecisions.cs](../../../Assets/Scripts/Components/Storage/BuildingItemDecisions.cs) |
| [BuildingItemOutputDecision](ItemsAndStorage.md#buildingitemoutputdecision) | 활성 상태 | [BuildingItemDecisions.cs](../../../Assets/Scripts/Components/Storage/BuildingItemDecisions.cs) |
| [DestroyItemRequest](ItemsAndStorage.md#destroyitemrequest) | 활성 상태 | [ItemRequests.cs](../../../Assets/Scripts/Components/Items/ItemRequests.cs) |
| [ItemConfigElement](ItemsAndStorage.md#itemconfigelement) | 버퍼 | [ItemConfigComponents.cs](../../../Assets/Scripts/Components/Items/ItemConfigComponents.cs) |
| [ItemIdentity](ItemsAndStorage.md#itemidentity) | 상태 | [ItemComponents.cs](../../../Assets/Scripts/Components/Items/ItemComponents.cs) |
| [ItemOwnership](ItemsAndStorage.md#itemownership) | 상태 | [ItemComponents.cs](../../../Assets/Scripts/Components/Items/ItemComponents.cs) |
| [ItemRegistry](ItemsAndStorage.md#itemregistry) | 상태 | [ItemConfigComponents.cs](../../../Assets/Scripts/Components/Items/ItemConfigComponents.cs) |
| [SpawnItemRequest](ItemsAndStorage.md#spawnitemrequest) | 상태 | [ItemRequests.cs](../../../Assets/Scripts/Components/Items/ItemRequests.cs) |
| [Storage](ItemsAndStorage.md#storage) | 상태 | [StorageComponents.cs](../../../Assets/Scripts/Components/Storage/StorageComponents.cs) |
| [StorageFilter](ItemsAndStorage.md#storagefilter) | 상태 | [StorageFilterComponents.cs](../../../Assets/Scripts/Components/Storage/StorageFilterComponents.cs) |
| [StoredItemElement](ItemsAndStorage.md#storeditemelement) | 버퍼 | [StorageComponents.cs](../../../Assets/Scripts/Components/Storage/StorageComponents.cs) |
| [TransferOwnershipRequest](ItemsAndStorage.md#transferownershiprequest) | 활성 상태 | [ItemRequests.cs](../../../Assets/Scripts/Components/Items/ItemRequests.cs) |

### 벨트와 분배 및 합류

| 컴포넌트 상세 | 종류 | 정의 파일 |
| --- | --- | --- |
| [BeltComponent](BeltsAndRouting.md#beltcomponent) | 상태 | [BeltComponents.cs](../../../Assets/Scripts/Components/Belts/BeltComponents.cs) |
| [BeltMovementDecision](BeltsAndRouting.md#beltmovementdecision) | 상태 | [BeltDecisions.cs](../../../Assets/Scripts/Components/Belts/BeltDecisions.cs) |
| [BeltMovementState](BeltsAndRouting.md#beltmovementstate) | 활성 상태 | [BeltComponents.cs](../../../Assets/Scripts/Components/Belts/BeltComponents.cs) |
| [MergerRoutingState](BeltsAndRouting.md#mergerroutingstate) | 상태 | [RoutingComponents.cs](../../../Assets/Scripts/Components/Routing/RoutingComponents.cs) |
| [RoutingTransferDecision](BeltsAndRouting.md#routingtransferdecision) | 활성 상태 | [RoutingDecisions.cs](../../../Assets/Scripts/Components/Routing/RoutingDecisions.cs) |
| [SplitterRoutingState](BeltsAndRouting.md#splitterroutingstate) | 상태 | [RoutingComponents.cs](../../../Assets/Scripts/Components/Routing/RoutingComponents.cs) |

### 채굴과 제작

| 컴포넌트 상세 | 종류 | 정의 파일 |
| --- | --- | --- |
| [ChangeCrafterRecipeRequest](Production.md#changecrafterreciperequest) | 상태 | [CrafterRequests.cs](../../../Assets/Scripts/Components/Production/Crafting/CrafterRequests.cs) |
| [CrafterDecision](Production.md#crafterdecision) | 활성 상태 | [CrafterDecisions.cs](../../../Assets/Scripts/Components/Production/Crafting/CrafterDecisions.cs) |
| [CrafterState](Production.md#crafterstate) | 상태 | [CrafterComponents.cs](../../../Assets/Scripts/Components/Production/Crafting/CrafterComponents.cs) |
| [CrafterStateDecision](Production.md#crafterstatedecision) | 활성 상태 | [CrafterDecisions.cs](../../../Assets/Scripts/Components/Production/Crafting/CrafterDecisions.cs) |
| [MinerDecision](Production.md#minerdecision) | 활성 상태 | [MinerDecisions.cs](../../../Assets/Scripts/Components/Production/Mining/MinerDecisions.cs) |
| [MinerState](Production.md#minerstate) | 상태 | [MinerComponents.cs](../../../Assets/Scripts/Components/Production/Mining/MinerComponents.cs) |
| [ProductItemElement](Production.md#productitemelement) | 버퍼 | [ProductComponents.cs](../../../Assets/Scripts/Components/Production/ProductComponents.cs) |
| [ProductResult](Production.md#productresult) | 버퍼 | [ProductResults.cs](../../../Assets/Scripts/Components/Production/ProductResults.cs) |
| [RecipeConfigElement](Production.md#recipeconfigelement) | 버퍼 | [RecipeConfigComponents.cs](../../../Assets/Scripts/Components/Production/Crafting/RecipeConfigComponents.cs) |
| [RecipeIngredientElement](Production.md#recipeingredientelement) | 버퍼 | [RecipeConfigComponents.cs](../../../Assets/Scripts/Components/Production/Crafting/RecipeConfigComponents.cs) |
| [RecipeOutputElement](Production.md#recipeoutputelement) | 버퍼 | [RecipeConfigComponents.cs](../../../Assets/Scripts/Components/Production/Crafting/RecipeConfigComponents.cs) |
| [RecipeRegistry](Production.md#reciperegistry) | 상태 | [RecipeConfigComponents.cs](../../../Assets/Scripts/Components/Production/Crafting/RecipeConfigComponents.cs) |

### 월드와 청크 및 자원

| 컴포넌트 상세 | 종류 | 정의 파일 |
| --- | --- | --- |
| [ChunkLoadRequestElement](WorldAndResources.md#chunkloadrequestelement) | 버퍼 | [ChunkRequests.cs](../../../Assets/Scripts/Components/World/ChunkRequests.cs) |
| [ChunkLoadRequestQueue](WorldAndResources.md#chunkloadrequestqueue) | 상태 | [ChunkRequests.cs](../../../Assets/Scripts/Components/World/ChunkRequests.cs) |
| [FloorBiomeElement](WorldAndResources.md#floorbiomeelement) | 버퍼 | [FloorGenerationConfigComponents.cs](../../../Assets/Scripts/Components/World/FloorGenerationConfigComponents.cs) |
| [FloorGenerationSettings](WorldAndResources.md#floorgenerationsettings) | 상태 | [FloorGenerationConfigComponents.cs](../../../Assets/Scripts/Components/World/FloorGenerationConfigComponents.cs) |
| [FloorVariantElement](WorldAndResources.md#floorvariantelement) | 버퍼 | [FloorGenerationConfigComponents.cs](../../../Assets/Scripts/Components/World/FloorGenerationConfigComponents.cs) |
| [GeneratedChunkCompletedElement](WorldAndResources.md#generatedchunkcompletedelement) | 버퍼 | [ChunkLifecycleComponents.cs](../../../Assets/Scripts/Components/World/ChunkLifecycleComponents.cs) |
| [GeneratedChunkReadyElement](WorldAndResources.md#generatedchunkreadyelement) | 버퍼 | [ChunkLifecycleComponents.cs](../../../Assets/Scripts/Components/World/ChunkLifecycleComponents.cs) |
| [GeneratedChunkTracker](WorldAndResources.md#generatedchunktracker) | 상태 | [ChunkLifecycleComponents.cs](../../../Assets/Scripts/Components/World/ChunkLifecycleComponents.cs) |
| [ResourceConfig](WorldAndResources.md#resourceconfig) | 상태 | [ResourceConfigComponents.cs](../../../Assets/Scripts/Components/Resources/ResourceConfigComponents.cs) |
| [ResourceGenerationConfigElement](WorldAndResources.md#resourcegenerationconfigelement) | 버퍼 | [ResourceGenerationConfigComponents.cs](../../../Assets/Scripts/Components/World/ResourceGenerationConfigComponents.cs) |
| [ResourceGenerationSettings](WorldAndResources.md#resourcegenerationsettings) | 상태 | [WorldGenerationConfigComponents.cs](../../../Assets/Scripts/Components/World/WorldGenerationConfigComponents.cs) |
| [ResourceNode](WorldAndResources.md#resourcenode) | 상태 | [ResourceComponents.cs](../../../Assets/Scripts/Components/Resources/ResourceComponents.cs) |

### 프리팹과 시뮬레이션 시작 상태

| 컴포넌트 상세 | 종류 | 정의 파일 |
| --- | --- | --- |
| [BuildingPrefabDatabase](Prefabs.md#buildingprefabdatabase) | 상태 | [PrefabDatabaseComponents.cs](../../../Assets/Scripts/Components/Prefabs/PrefabDatabaseComponents.cs) |
| [BuildingPrefabElement](Prefabs.md#buildingprefabelement) | 버퍼 | [PrefabDatabaseComponents.cs](../../../Assets/Scripts/Components/Prefabs/PrefabDatabaseComponents.cs) |
| [DronePrefab](Prefabs.md#droneprefab) | 상태 | [PrefabDatabaseComponents.cs](../../../Assets/Scripts/Components/Prefabs/PrefabDatabaseComponents.cs) |
| [DronePrefabDatabase](Prefabs.md#droneprefabdatabase) | 상태 | [PrefabDatabaseComponents.cs](../../../Assets/Scripts/Components/Prefabs/PrefabDatabaseComponents.cs) |
| [ItemPrefabDatabase](Prefabs.md#itemprefabdatabase) | 상태 | [PrefabDatabaseComponents.cs](../../../Assets/Scripts/Components/Prefabs/PrefabDatabaseComponents.cs) |
| [ItemPrefabElement](Prefabs.md#itemprefabelement) | 버퍼 | [PrefabDatabaseComponents.cs](../../../Assets/Scripts/Components/Prefabs/PrefabDatabaseComponents.cs) |
| [PrefabDatabaseReady](Prefabs.md#prefabdatabaseready) | 상태 | [PrefabDatabaseReadiness.cs](../../../Assets/Scripts/Components/Prefabs/PrefabDatabaseReadiness.cs) |
| [ResourcePrefabDatabase](Prefabs.md#resourceprefabdatabase) | 상태 | [PrefabDatabaseComponents.cs](../../../Assets/Scripts/Components/Prefabs/PrefabDatabaseComponents.cs) |
| [ResourcePrefabElement](Prefabs.md#resourceprefabelement) | 버퍼 | [PrefabDatabaseComponents.cs](../../../Assets/Scripts/Components/Prefabs/PrefabDatabaseComponents.cs) |
| [SimulationFatalError](Prefabs.md#simulationfatalerror) | 상태 | [PrefabDatabaseReadiness.cs](../../../Assets/Scripts/Components/Prefabs/PrefabDatabaseReadiness.cs) |

## 확인 범위와 문서 유지

최초 작성은 정적 소스 분석이며, 현재도 타입 선언과 문서 항목·링크를 대조한다. 2026-10-04 운송 제거 변경은 컴파일과 관련 5개 EditMode 클래스 41개를 확인했다. 해당 결과는 [검증 기록](../../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/ConstructionTransportRemoval-Verification.md)의 범위에 한정한다. Play Mode·실제 SubScene 베이킹·시각·성능 검증은 수행하지 않았다.

기존 [AGENTS.md](../../../AGENTS.md)는 프로젝트 공통 계약과 탐색 기준이며, [V2 코드 지도](../../architecture%20v2%20plan/CodeMemory/README.md)는 시스템 중심 탐색 보조다. 이 문서 묶음은 컴포넌트별 상세 진입점이다. 이전 구조를 다루는 다른 CodeMemory 문서의 시스템 이름을 현재 V2의 구현으로 전제하지 않았다.

컴포넌트의 정의·생성 위치·Writer·쿼리·소비 방식이 바뀌면 해당 타입 절과 이 색인의 종류/연결을 함께 갱신한다. 엔티티 삭제와 enable 상태 변경, ECB 반영 전후의 설명을 우선 대조하면 생명주기를 이어서 추적할 수 있다.
