# Architecture V2 C# 파일 색인

2026-10-06 도메인 그룹 분리 후 모든 등재 경로를 현재 파일과 대조하고 새 그룹·ECB·공개 대기 예약·통합 테스트를 추가했다. 경로는 저장소 루트 기준이다. 주요 파일의 탐색 색인이며 전체 C# 파일을 빠짐없이 나열하거나 모든 본문을 재감사한 목록은 아니다. 역할·타입 목록은 테스트 실행 결과가 아니다. 위쪽 설계·흐름은 [README.md](README.md)를, 폴더 분류 기준은 [AGENTS.md](../../../AGENTS.md)를 참조한다.

2026-10-04 공사 운송·Progress 제거와 취소/완공의 단계 분리를 반영했다. 현장 취소 요청은 ConstructionCancelCommandSystem이 Command에서 처리하고 Lifecycle Apply는 완공만 담당한다. `TransferOwnershipRequest`의 같은 틱 처리 표시도 제거되었다. 실행 결과와 한계는 [공사 운송 제거 검증](../V2%20Quality%20Evaluation%20Plan/Results/ConstructionTransportRemoval-Verification.md)과 [취소 Command 이동 검증](../V2%20Quality%20Evaluation%20Plan/Results/ConstructionCancelCommand-Verification.md)을 따른다.

## 공통, 데이터, 설정, Phase

현재 인계 계획은 같은 틱 건물 종료 실물/수량/슬롯을 사용하고 드론 단계 중 새 수집품·공간은 추가하지 않는다. 마지막 납품·회수에 따른 완공은 다음 건물 틱이다. 실제 관측 원본 Writer·수행자 이동은 후속이다.

| 파일 | 현재 역할 |
| --- | --- |
| `Assets/Scripts/Common/DirectionExtensions.cs` | `DirectionEnum`을 인접 셀 오프셋으로 변환한다. |
| `Assets/Scripts/Common/BeltEntryUtility.cs` | 출고·Routing·예약의 현재 벨트 입구 점유/간격 판정을 공유한다. 신규 후보 중재와 상태 반영은 수행하지 않는다. |
| `Assets/Scripts/Common/GameConstants.cs` | 청크 크기, 아이템 최소 간격 `0.25f`, 간격·델타타임에서 파생한 벨트 속도 상한, 슬롯/타일 홉/델타타임 상한을 정의한다. |
| `Assets/Scripts/Common/BuildingInputSlotUtility.cs` | 재료 목록의 요구량/최대 스택으로 건물 공통 품목 전용 입력 슬롯을 계산하고 실패 사유를 반환한다. Crafter 레시피 변경에서 사용한다. |
| `Assets/Scripts/Common/BuildingConfigLookupUtility.cs` | 건물 통합/호환 런타임 설정 조회, footprint·해금·건설 자재 조회를 제공한다. |
| `Assets/Scripts/Common/BuildingFootprintUtility.cs` | 점유 크기의 최소 1칸 정규화와 방향 회전을 계산하며 BuildingFootprint 확장 메서드도 제공한다. |
| `Assets/Scripts/Common/BuildingPlacementRequestUtility.cs` | 플레이어 배치 묶음의 World 접수번호를 발급하고 요청/후보를 함께 준비한다. 최종 승인과 임시 셀 중재는 기존 Command가 담당한다. |
| `Assets/Scripts/Common/BuildingTypeExtensions.cs` | 건물 종류의 Burst 호환 FixedString 변환을 제공한다. |
| `Assets/Scripts/Common/ItemTypeExtensions.cs` | 아이템 종류의 Burst 호환 FixedString 변환을 제공한다. |
| `Assets/Scripts/Common/ItemLifecycleUtility.cs` | 일반 Spawn·생산물·철거 환급의 프리팹 런타임 초기화를 호출자의 ECB에 기록한다. 조회/실패 정책/버퍼 등록은 호출자가 소유한다. |
| `Assets/Scripts/Common/SimulationFailureUtility.cs` | Spawn 오류를 로그와 `SimulationFatalError`로 ECB에 게시한다. |
| `Assets/Scripts/Common/PrefabLookupUtility.cs` | 건물·아이템·자원 프리팹 버퍼 조회를 제공하며 누락/Null 프리팹 실패 정책을 유지한다. |
| `Assets/Scripts/Common/ResourceGenerationConfigLookupUtility.cs` | 품목별 자원 생성 설정 버퍼를 조회한다. |
| `Assets/Scripts/Components/Belts/BeltComponents.cs` | `BeltComponent`, `BeltMovementState` 정의. |
| `Assets/Scripts/Components/Belts/BeltDecisions.cs` | `BeltMovementDecision` 정의. |
| `Assets/Scripts/Components/Belts/BeltSpatialIndex.cs` | `BeltInfo`, `BeltSpatialIndex`, `BeltSpatialIndexFence` 정의. |
| `Assets/Scripts/Components/Buildings/BuildingComponents.cs` | `BuildingTypeEnum`, `BuildingType`, `BuildingFootprint`, `IndestructibleBuilding`, `PendingBuildingDemolition` 정의. |
| `Assets/Scripts/Components/Buildings/BuildingConfigComponents.cs` | `BuildingConfig`, `BuildingConfigElement`, `BuildingConstructionMaterialElement` 정의. |
| `Assets/Scripts/Components/Buildings/BuildingRequests.cs` | `SpawnBuildingRequest`, `DemolishBuildingRequest` 정의. |
| `Assets/Scripts/Components/Buildings/BuildingRuntimeConfigComponents.cs` | `BuildingRuntimeConfig`, `BuildingRuntimeConfigElement` 정의. |
| `Assets/Scripts/Components/Buildings/BuildingSpatialIndex.cs` | `BuildingInfo`, `BuildingSpatialIndex`, `BuildingSpatialIndexFence` 정의. |
| `Assets/Scripts/Components/Buildings/PlacementStamp.cs` | `PlacementStamp` 정의. |
| `Assets/Scripts/Components/Common/GridComponents.cs` | `DirectionEnum`, `GridPosition`, `Direction` 정의. |
| `Assets/Scripts/Components/Common/IRequestComponent.cs` | `IRequestComponent`, `IEnableableRequest` 정의. |
| `Assets/Scripts/Components/Construction/BuildingPlacementRequests.cs` | `BuildingPlacementRequest`, `PlacementRequestCandidateElement`, World 접수번호 원본 `BuildingPlacementReceiptSequence` 정의. |
| `Assets/Scripts/Components/Construction/ConstructionComponents.cs` | 현장/자재 요구 데이터. Reservation/Publish/Lifecycle이 예약·실제 도착량을 정산하고 Construction이 현재 월드 Owner/GridPosition·활성 Destroy로 차단을 갱신한다. |
| `Assets/Scripts/Components/Construction/ConstructionRequests.cs` | 현장 취소 요청 `CancelConstructionRequest`만 정의한다. |
| `Assets/Scripts/Components/Construction/ConstructionSupplyReservation.cs` | Reservation이 일반 해제·확보하고 Publish가 최종 재검사 후 공개한다. |
| `Assets/Scripts/Components/DroneLogistics/DroneTaskComponents.cs` | DroneTaskExecutionSystem/DroneTaskAssignmentPublishSystem이 작업·배정을 생성하고 DroneTaskLifecycleApplySystem이 행동 성공분과 진행·종료 상태를 반영한다. 실물 이전은 ItemOwnershipApplySystem의 공통 API에 위임한다. |
| `Assets/Scripts/Components/DroneLogistics/DroneWorkerComponents.cs` | 수행자 표시·관측·배정 연결·적재 출처. 등록/생성/이동 기능은 없다. |
| `Assets/Scripts/Components/DroneLogistics/DroneCapacityState.cs` | World 공통 최대 적재량. Decision 후보·Reservation 승인·Execution 경로 검사·Publish 공개에서 읽으며 초기화·연구 Writer는 후속이다. |
| `Assets/Scripts/Components/DroneLogistics/DroneRouteContracts.cs` | Decision의 생성 의도를 Execution이 EndSimulation에 실체화한 경로 요청과 같은 엔티티의 외부 결과. 실제 경로 계산기는 없다. |
| `Assets/Scripts/Components/DroneLogistics/DroneActionContracts.cs` | 행동 식별 값·World 접수 번호를 가진 인계 요청과 실제 인계 결과. 배정 행동 번호는 중복 거절 기준이다. |
| `Assets/Scripts/Components/DroneLogistics/DroneActionReceiptSequence.cs` | World 행동 접수 순번 원본. 외부 입력 Submit만 발급한다. |
| `Assets/Scripts/Components/DroneLogistics/DroneItemTransferDecisions.cs` | 요청별 자격·수량 계획, 건물 종료 기준 실물 ID·슬롯 품목/개수의 파생 입력 3타입. EndSimulation에 제거한다. |
| `Assets/Scripts/Common/DroneActionRequestUtility.cs` | 외부 드론 행동 완료 신호의 World 접수 순번과 요청→결과 소비 수명을 소유한다. |
| `Assets/Scripts/Common/DroneItemTransferUtility.cs` | 실물 조회는 기존 Ownership API에 위임하고 단일 품목 검사·현장 도착량/예약 정산을 공유한다. 원본 버퍼·위치·Owner를 직접 쓰지 않는다. |
| `Assets/Scripts/Common/DroneItemTransferValidationUtility.cs` | 인계 Decision/Execution/StateApply가 공유하는 읽기 전용 배정·행동·관측 버전·출처 자격 검사. |
| `Assets/Scripts/Components/DroneLogistics/DroneTaskSequence.cs` | 공급·회수 작업의 생성 순번 원본. |
| `Assets/Scripts/Components/DroneLogistics/DroneRecoveryPending.cs` | 드론 방출 실물의 회수 필요 표시. DroneTaskLifecycleApplySystem이 방출 시 추가·회수 성공 시 제거를 EndSimulation에 기록한다. |
| `Assets/Scripts/Components/DroneLogistics/DroneTaskCandidateDecisionElement.cs` | Decision이 계산한 신규 배정·적재품 재배정 후보를 한 틱 동안 보관한다. |
| `Assets/Scripts/Components/DroneLogistics/DroneRouteDecisionElement.cs` | 경로 요청 Create/Retain/Remove 의도. Execution이 소비한다. |
| `Assets/Scripts/Components/DroneLogistics/DroneTaskCreationDecisionElement.cs` | 공급·회수 작업 생성 의도. Execution이 순번을 부여하고 ECB에 기록한다. |
| `Assets/Scripts/Components/DroneLogistics/DroneTaskInvalidationDecisionElement.cs` | 작업·배정 무효화 판단 의도. Lifecycle Apply가 상태·연결·삭제 보류를 반영한다. |
| `Assets/Scripts/Common/DroneTaskCreationUtility.cs` | 작업 생성과 공통 순번 발급. |
| `Assets/Scripts/Common/DroneSchedulingUtility.cs` | 드론 후보/경로/배정의 현재성과 현장·공급원·보관처·실물 자격을 같은 규칙으로 조회한다. |
| `Assets/Scripts/Common/ConstructionSupplyReservationUtility.cs` | 현장 예약 합계 확보·해제와 자기 예약을 제외한 부족량 계산. |
| `Assets/Scripts/Systems/Items/Decision/ItemSpawnAdmissionDecisionSystem.cs` | 철거 예정 Storage/Product와 활성 현장 내부 World Spawn을 앞단에서 거부하고 EndBuilding에 요청 삭제를 기록한다. Item Lifecycle에는 철거 정책 분기가 없다. |
| `Assets/Scripts/Systems/Command/ConstructionCancelCommandSystem.cs` | 취소 요청을 단일 워커 Job으로 소비한다. Cancelled로 중복 반환을 막고 기존 실물의 월드 반환 및 현장/요청 삭제를 EndCommand에 확정한다. 공간 인덱스의 조기 갱신은 없다. |
| `Assets/Scripts/Systems/Construction/StateApply/ConstructionLifecycleApplySystem.cs` | 남은 현장의 바닥 정리 상태와 실제 도착 자재로 완공 여부를 판단한다. 취소는 Command에서 먼저 확정된다. |
| `Assets/Scripts/Common/ConstructionSiteWorldItemUtility.cs` | 현재 활성·비취소 현장의 회전 footprint를 캡처해 내부 월드 생성/방출을 읽기 검사한다. 원본·공간 인덱스를 변경하지 않는다. |
| `Assets/Scripts/Components/Construction/PlacementComponents.cs` | `PlacementFlags`, `PlacementValidationCode`, `PlacementValidationResult`, `PlacementCandidate` 정의. |
| `Assets/Scripts/Components/Items/ItemComponents.cs` | `ItemTypeEnum`, `ItemIdentity`, `ItemOwnership` 정의. |
| `Assets/Scripts/Components/Items/ItemConfigComponents.cs` | `ItemConfigElement`, `ItemRegistry` 정의. |
| `Assets/Scripts/Components/Items/ItemRequests.cs` | `ItemSpawnDestination`, `SpawnItemRequest`, `DestroyItemRequest`, `TransferOwnershipRequest` 정의. |
| `Assets/Scripts/Components/Items/ItemSpatialIndex.cs` | `ItemSpatialIndex`, `ItemSpatialIndexFence` 정의. |
| `Assets/Scripts/Components/Prefabs/PrefabDatabaseReadiness.cs` | `PrefabDatabaseReady`, `SimulationFatalError` 정의. |
| `Assets/Scripts/Components/Prefabs/PrefabDatabaseComponents.cs` | `BuildingPrefabDatabase`, `BuildingPrefabElement`, `ItemPrefabDatabase`, `ItemPrefabElement`, `ResourcePrefabDatabase`, `ResourcePrefabElement`, `DronePrefabDatabase`, `DronePrefab` 정의. |
| `Assets/Scripts/Components/Production/Crafting/CrafterComponents.cs` | `CrafterStatusEnum`, `CrafterState` 정의. |
| `Assets/Scripts/Components/Production/Crafting/CrafterDecisions.cs` | `CrafterDecision`, `CrafterStateDecision` 정의. |
| `Assets/Scripts/Components/Production/Crafting/CrafterRequests.cs` | `ChangeCrafterRecipeRequest` 정의. |
| `Assets/Scripts/Components/Production/Crafting/RecipeConfigComponents.cs` | `RecipeIngredientElement`, `RecipeOutputElement`, `RecipeConfigElement`, `RecipeRegistry` 정의. |
| `Assets/Scripts/Components/Production/Mining/MinerComponents.cs` | `MinerState` 정의. |
| `Assets/Scripts/Components/Production/Mining/MinerDecisions.cs` | `MinerDecision` 정의. |
| `Assets/Scripts/Components/Production/ProductComponents.cs` | `ProductItemElement` 정의. |
| `Assets/Scripts/Components/Production/ProductResults.cs` | `ProductResult` 정의. |
| `Assets/Scripts/Components/Resources/ResourceComponents.cs` | `ResourceNode` 정의. |
| `Assets/Scripts/Components/Resources/ResourceConfigComponents.cs` | `ResourceConfig` 정의. |
| `Assets/Scripts/Components/Resources/ResourceSpatialIndex.cs` | `ResourceSpatialIndex`, `ResourceSpatialIndexFence` 정의. |
| `Assets/Scripts/Components/Routing/RoutingComponents.cs` | `SplitterRoutingState`, `MergerRoutingState` 정의. |
| `Assets/Scripts/Components/Routing/RoutingDecisions.cs` | `RoutingTransferDecision` 정의. |
| `Assets/Scripts/Components/Storage/BuildingInputSlotElement.cs` | `BuildingInputSlotElement` 정의. |
| `Assets/Scripts/Components/Storage/BuildingItemDecisions.cs` | `BuildingItemInputDecision`, `BuildingItemOutputDecision` 정의. |
| `Assets/Scripts/Components/Storage/StorageComponents.cs` | `Storage`, `StoredItemElement` 정의. |
| `Assets/Scripts/Components/Storage/StorageFilterComponents.cs` | `StorageFilterMode`, `FixedBitSet`, `StorageFilter` 정의. |
| `Assets/Scripts/Components/World/ChunkLifecycleComponents.cs` | `GeneratedChunkTracker`, `GeneratedChunkReadyElement`, `GeneratedChunkCompletedElement` 정의. |
| `Assets/Scripts/Components/World/ChunkRequests.cs` | `ChunkLoadRequestQueue`, `ChunkLoadRequestElement` 정의. |
| `Assets/Scripts/Components/World/FloorGenerationConfigComponents.cs` | `FloorGenerationSettings`, `FloorBiomeElement`, `FloorVariantElement` 정의. |
| `Assets/Scripts/Components/World/ResourceGenerationConfigComponents.cs` | `ResourceGenerationConfigElement` 정의. |
| `Assets/Scripts/Components/World/WorldGenerationConfigComponents.cs` | `ResourceGenerationSettings` 정의. |
| `Assets/Scripts/Common/RoutingDirectionUtility.cs` | Splitter/Merger 포트 순서, 회전, 인접 방향, 연결 방향, 커서 갱신을 계산한다. |
| `Assets/Scripts/Common/RecipeConfigLookupUtility.cs` | 읽기 전용 레시피 버퍼의 ID/주생산품 첫 일치 조회와 Decision/Execution의 전체 재료 소비 가능 여부 검사. |
| `Assets/Scripts/Config/RecipeConfigLoader.cs` | JSON/Resources 레시피를 파싱하고 World 소유 버퍼로 1회 게시한다. 중복 재료와 JSON 수량의 int 범위 초과를 거부하며 기본 5개 레시피를 제공한다. |
| `Assets/Scripts/Phases/CommandGroup.cs` | 명령 처리 그룹을 선언한다. |
| `Assets/Scripts/Phases/Buildings/BuildingDecisionGroup.cs` | 건물 단계 시작에 확정 상태를 읽어 벨트·입출고·채굴·제작·라우팅 후보를 계산한다. |
| `Assets/Scripts/Phases/Buildings/BuildingExecutionGroup.cs` | 건물 Reservation 뒤에 승인된 벨트 이동·채굴·제작을 한 번 실행한다. |
| `Assets/Scripts/Phases/GameSimulationGroup.cs` | Unity SimulationSystemGroup 안에서 Command→Building→Drone→Commit→Synchronization을 실행한다. |
| `Assets/Scripts/Phases/Buildings/BuildingReservationGroup.cs` | 건물 Decision 뒤에 입고 슬롯·벨트 진입 목적지의 경합을 중재한다. |
| `Assets/Scripts/Phases/Buildings/BuildingStateApplyGroup.cs` | 건물 Execution 뒤에 일반 물류·생산 결과·아이템/건물 수명주기를 반영하고 마지막에 완공을 판단한다. |
| `Assets/Scripts/Phases/SynchronizationGroup.cs` | 최종 구조 변경 확정 뒤 현재 ECS 원본에서 공간 인덱스를 재구축한다. |
| `Assets/Scripts/Phases/Buildings/BuildingSimulationGroup.cs` | 건물·벨트·생산·아이템 수명주기를 한 번 실행하고 EndBuilding에서 결과를 확정한다. |
| `Assets/Scripts/Phases/Drones/DroneSimulationGroup.cs` | 건물 결과 확정 뒤 드론 판단·예약·실행·반영을 한 번 처리한다. |
| `Assets/Scripts/Phases/Drones/DroneDecisionGroup.cs` | 건물 종료 시 확정된 상태로 작업·경로 의도, 배정 후보와 행동 자격을 계산한다. |
| `Assets/Scripts/Phases/Drones/DroneReservationGroup.cs` | 이전 미공개 예약을 정산하고 이번 후보에서 배정과 현장 공급 수량을 선택한다. |
| `Assets/Scripts/Phases/Drones/DroneExecutionGroup.cs` | 확정된 작업·경로 의도를 실행하고 OrderFirst에서 드론 단계의 인계 계획을 준비한다. |
| `Assets/Scripts/Phases/Drones/DroneStateApplyGroup.cs` | 드론 계획을 현재 원본으로 재검사해 실물·도착량·예약·배정 상태를 정산한다. |
| `Assets/Scripts/Phases/SimulationCommitGroup.cs` | 드론 작업·경로·배정·행동 결과와 종료 정리의 구조 변경을 최종 확정한다. |
| `Assets/Scripts/Components/DroneLogistics/DroneTaskPendingPublicationElement.cs` | Reservation이 선택한 배정의 스냅샷과 공개 전 현장 예약을 Publish/다음 틱 정산까지 보관한다. |

## 런타임 시스템과 검증

드론 2단계 추가 파일:

| 파일 | 현재 역할 |
| --- | --- |
| `Assets/Scripts/Systems/Drones/Execution/DroneTaskExecutionSystem.cs` | Execution에서 Decision의 작업 생성과 경로 Create/Retain/Remove 의도를 실행한다. |
| `Assets/Scripts/Systems/Drones/Decision/DroneTaskDecisionSystem.cs` | 한 본체에서 생성·신규/적재 후보·ViaSource/Direct 경로·무효화를 판단한다. 적재품은 현장/보관/방출 우선순위와 거리/Stamp를 적용하며 None은 대기한다. 원본을 쓰지 않는다. |
| `Assets/Scripts/Systems/Drones/StateApply/DroneTaskLifecycleApplySystem.cs` | StateApply에서 행동 신호와 작업/배정 무효화 의도를 반영하고 실제 인계량을 정산한다. |
| `Assets/Scripts/Systems/Drones/Reservation/ConstructionSupplyReservationSystem.cs` | Reservation에서 수행자별 실행 가능한 후보를 선택하고 현장 공급 수량 경합을 중재한다. |
| `Assets/Scripts/Systems/Drones/StateApply/DroneTaskAssignmentPublishSystem.cs` | StateApply 마지막에 ConstructionSupplyReservationSystem의 공개 대기 기록을 재검사하고 수행자가 이용할 배정을 공개한다. |
| `Assets/Scripts/Systems/Drones/Decision/DroneItemTransferDecisionSystem.cs` | 외부 행동 요청의 초기 자격을 읽기 검사해 CanExecute만 판단한다. 원본 실물·예약·배정·행동 번호를 쓰지 않는다. |
| `Assets/Scripts/Systems/Drones/Execution/DroneItemTransferExecutionSystem.cs` | DroneExecution 시작에 건물 종료 확정 상태와 Command 차단으로 드론 인계 계획을 준비한다. |

기존 파일 지도:

| 파일 | 현재 역할 |
| --- | --- |
| `Assets/Scripts/Systems/Initialization/PrefabDatabaseInitializationSystem.cs` | SubScene 로딩 이후 세 DB를 검증하고 게임 시작 허용 또는 중단을 게시한다. |
| `Assets/Scripts/Systems/Initialization/ItemConfigInitSystem.cs` | StreamingAssets의 스택 설정을 읽어 `ItemRegistry`와 아이템 버퍼를 한 번 게시한다. |
| `Assets/Scripts/Systems/Initialization/RecipeInitSystem.cs` | `RecipeRegistry`와 레시피·재료·출력 버퍼를 한 번 게시한다. 입력 검증 실패는 설정 미게시·오류 로그·즉시 SimulationFatalError로 처리한다. |
| `Assets/Scripts/Systems/Command/CrafterRecipeCommandSystem.cs` | 새 입력 슬롯 계산 검증 후 진행도·슬롯 수·품목 배정·필터를 갱신하고 잔여 재료를 스택 구분을 유지해 Product로 옮긴다. Decision 데이터는 수정하지 않는다. |
| `Assets/Scripts/Systems/Buildings/Decision/BeltMovementDecisionSystem.cs` | 벨트 속도, 프레임 시간, 같은/다음 타일의 아이템 간격을 이용해 `PlannedProgress`를 계산한다. |
| `Assets/Scripts/Systems/Buildings/Decision/BuildingItemInputDecisionSystem.cs` | 벨트 끝에서 다음 셀의 건물, Storage, 필터, 제작기 부산물 대기 상태를 검사한다. |
| `Assets/Scripts/Systems/Buildings/Decision/CrafterDecisionSystem.cs` | 레시피·재료·출력 슬롯을 검사해 제작 실행 결정과 다음 상태를 분리해 기록한다. |
| `Assets/Scripts/Systems/Buildings/Decision/MinerDecisionSystem.cs` | footprint 아래 유효 자원, 품목 일치, 출력 스택 공간을 검사한다. |
| `Assets/Scripts/Systems/Buildings/Decision/ProductItemOutputDecisionSystem.cs` | 생산 건물의 Product 버퍼에서 Slot 0을 우선해 외향 벨트 출고를 결정한다. |
| `Assets/Scripts/Systems/Buildings/Decision/StorageItemOutputDecisionSystem.cs` | Product 버퍼가 없는 일반 저장 건물의 첫 Stored 아이템 출고를 결정한다. |
| `Assets/Scripts/Systems/Buildings/Reservation/BeltDestinationReservationSystem.cs` | 건물 출고·라우팅 후보의 공통 공간 검사 후 목표별 한 개를 승인한다. 일반 벨트 이동은 중재하지 않는다. |
| `Assets/Scripts/Systems/Buildings/Reservation/BuildingStorageInputReservationSystem.cs` | 현재 버퍼와 프레임 내 예약 수를 합산하여 품목별 저장 슬롯을 하나씩 확정한다. |
| `Assets/Scripts/Systems/Buildings/Execution/BeltMovementExecutionSystem.cs` | 계획된 타일 내 진행도, 경계 횡단, 격자 위치와 시각 위치를 반영하고 계획을 소비한다. |
| `Assets/Scripts/Systems/Buildings/Execution/CrafterExecutionSystem.cs` | 재료 선소비 및 삭제 요청, 진행도 누적, 주생산품/부산물 `ProductResult` 기록을 수행한다. |
| `Assets/Scripts/Systems/Buildings/Execution/MinerExecutionSystem.cs` | 채굴 진행, 자원 차감/고갈 삭제 요청과 `ProductResult` 기록을 수행한다. |
| `Assets/Scripts/Systems/Buildings/StateApply/BuildingItemStorageApplySystem.cs` | 입고/출고 버퍼·벨트 상태·위치와 소유권 이전 요청을 반영한다. |
| `Assets/Scripts/Systems/Buildings/StateApply/CrafterStateApplySystem.cs` | `CrafterStateDecision.NextStatus`를 적용하고 결정을 비활성화한다. |
| `Assets/Scripts/Systems/Buildings/StateApply/EndBuildingEntityCommandBufferSystem.cs` | 건물 단계 마지막에서 물류·생산·실물/건물 수명주기·완공의 구조 변경을 확정한다. |
| `Assets/Scripts/Systems/Items/StateApply/ItemLifecycleApplySystem.cs` | 생산 결과/Spawn·Destroy를 처리하고 최종 World Spawn의 현장 내부 위치를 다시 거부한다. 예정 월드 기록을 만들지 않는다. |
| `Assets/Scripts/Systems/Items/StateApply/ItemOwnershipApplySystem.cs` | 한 본체 파일에서 일반 Transfer 요청 적용과 EffectiveOwner/CanTransferItem/TryTransferItem 공통 API를 소유한다. API는 성공 실물의 버퍼·Owner·위치·벨트·렌더를 함께 반영하고 새 Transfer 요청을 만들지 않는다. |
| `Assets/Scripts/Systems/Synchronization/BeltSpatialSyncSystem.cs` | 벨트 셀 인덱스를 비우고 현재 벨트 엔티티에서 재구축한다. |
| `Assets/Scripts/Systems/Synchronization/BuildingSpatialSyncSystem.cs` | footprint의 모든 점유 셀을 건물 인덱스로 재구축한다. |
| `Assets/Scripts/Systems/Synchronization/ItemSpatialSyncSystem.cs` | `Owner == Entity.Null`인 아이템만 공간 인덱스로 재구축한다. |
| `Assets/Scripts/Systems/Synchronization/ResourceSpatialSyncSystem.cs` | 자원 노드 셀 인덱스를 재구축한다. |
| `Assets/Scripts/Validation/WorldInvariantValidationSystem.cs` | Editor/개발 빌드에서 프레임 말 정합성을 검사하고 위반 보고서를 기본 `Logs/InvariantErrors`에 기록한다. World별 `SetLogDirectory`로 테스트 출력을 격리한다. |
| `Assets/Scripts/Systems/Commit/EndSimulationEntityCommandBufferSystem.cs` | 드론 작업·경로·배정·행동 결과 및 종료 정리의 ECB를 최종 재생한다. |

## 주요 EditMode 테스트와 공통 도우미

| 파일 | 검증 대상으로 작성된 범위 |
| --- | --- |
| `Assets/Editor/Tests/DroneLifecycleIntegrationTests.cs` | 실제 정렬된 건물→드론 루트에서 입고/출고 공간·취소·철거·다음 틱 완공과 예약 정산을 검증한다. 관측·경로·행동은 입력 fixture다. |
| `Assets/Editor/Tests/Phase1ItemIntegrationTests.cs` | 스폰, 소유권 이전, 공간 반영, 삭제, 잘못된 목적지/버퍼 정합성. |
| `Assets/Editor/Tests/Phase2BeltExecutionTests.cs` | 회전, 연속 홉, 큰 이동량의 종단 정지와 delta time 제한. |
| `Assets/Editor/Tests/Phase2BeltIntegrationTests.cs` | 다중 타일 흐름, 후방 정체, 4개 수용, 간격 위반 탐지. 사례별 전용 경로의 진단 로그를 성공·실패 모두 보존한다. |
| `Assets/Editor/Tests/Phase3ItemConfigTests.cs` | 시스템 초기화와 커스텀 JSON 스택 설정 게시. |
| `Assets/Editor/Tests/Phase3StorageDecisionTests.cs` | 입출고 결정 통합, 복수 벨트, 혼잡 후 재개. |
| `Assets/Editor/Tests/Phase3StorageOwnershipTests.cs` | 입출고 소유권과 단일 남은 슬롯 경합, 스택 병합/새 슬롯. |
| `Assets/Editor/Tests/Phase4EndToEndPipelineTests.cs` | 채굴→벨트→저장 루프와 연속 생산/역압. |
| `Assets/Editor/Tests/Phase4MinerPipelineTests.cs` | 자원/출력 여유가 없는 경우의 차단, 무한 자원, 품목 불일치, 스택과 시간 상한. |
| `Assets/Editor/Tests/Phase5CrafterExecutionTests.cs` | 비활성 결정의 실행 제외, 다중 부산물 생성과 전체 용량 판정, 재료 부족 시 무소비·정확/초과 재고의 소비와 삭제. |
| `Assets/Editor/Tests/Phase5RecipeConfigTests.cs` | 실제 레시피 설정 로드와 부산물 1개/2개의 JSON 출력 구성, 중복 재료/수량 범위 초과의 게시 거부와 초기화 중단. |
| `Assets/Editor/Tests/Phase5CrafterInputSlotTests.cs` | F-037 1단계: 슬롯 계산의 올림, 중복 합산, 품목 구분, 상한/실패 및 Burst Job 호출. |
| `Assets/Editor/Tests/Phase5CrafterInputPipelineTests.cs` | F-037: 개별 회귀 15개와 실제 정렬 Command→Building→Synchronization 통합 4개. 직접 Spawn/공사 완료 × 기본/사용자 지정 ECS 테스트 프리팹, 미선택 차단·입고·선소비·생산·후속 출고·레시피 변경/해제 및 불변식. |
| `Assets/Editor/Tests/Phase5RecipeChangePipelineTests.cs` | 레시피 변경 잔여물 배출, 입고 차단과 재개. |
| `Assets/Editor/Tests/Phase6BeltDestinationReservationTests.cs` | 외부 후보 한 개 승인/패자 보존과 출고·Routing Decision→예약→반영의 점유·간격·소유권·결정 소비를 검증한다. |
| `Assets/Editor/Tests/TestSupport/EcsWorldTestFixture.cs` | 독립 ECS World/EntityManager 생성과 정리. |
| `Assets/Editor/Tests/TestSupport/TestEntityFactory.cs` | 자원, 벨트/아이템, 저장, 채굴기, 제작기, 일반 건물 테스트 엔티티 생성. |
| `Assets/Editor/Tests/TestSupport/TestSimulationDriver.cs` | 시간·개별 시스템·ECB 경계 지원. 자원 생성/채굴 및 제작기 생성·물류의 실제 정렬 Command→Building→Synchronization 테스트 그룹을 구성한다. |
