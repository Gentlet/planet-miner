# Architecture V2 C# 파일 색인

컴포넌트 파일 항목은 2026-09-30 도메인별 재배치 후 현재 소스의 선언을 기준으로 갱신했다. 경로는 저장소 루트 기준이다. 그 외 항목은 기존 조사 기록이며 전체 C# 파일을 빠짐없이 나열한 목록은 아니다. 역할·타입 목록은 테스트 실행 결과가 아니다. 위쪽 설계·흐름은 [README.md](README.md)를, 폴더 분류 기준은 [AGENTS.md](../../../AGENTS.md)를 참조한다.

## 공통, 데이터, 설정, Phase

| 파일 | 현재 역할 |
| --- | --- |
| `Assets/Scripts/Common/DirectionExtensions.cs` | `DirectionEnum`을 인접 셀 오프셋으로 변환한다. |
| `Assets/Scripts/Common/GameConstants.cs` | 청크 크기, 아이템 최소 간격 `0.25f`, 슬롯/타일 홉/델타타임 상한을 정의한다. |
| `Assets/Scripts/Common/BuildingInputSlotUtility.cs` | 재료 목록의 요구량/최대 스택으로 건물 공통 품목 전용 입력 슬롯을 계산하고 실패 사유를 반환한다. Crafter 레시피 변경에서 사용한다. |
| `Assets/Scripts/Common/BuildingConfigLookupUtility.cs` | 건물 통합/호환 런타임 설정 조회, footprint·해금·건설 자재 조회를 제공한다. |
| `Assets/Scripts/Common/BuildingFootprintUtility.cs` | 점유 크기의 최소 1칸 정규화와 방향 회전을 계산하며 BuildingFootprint 확장 메서드도 제공한다. |
| `Assets/Scripts/Common/BuildingTypeExtensions.cs` | 건물 종류의 Burst 호환 FixedString 변환을 제공한다. |
| `Assets/Scripts/Common/ItemTypeExtensions.cs` | 아이템 종류의 Burst 호환 FixedString 변환을 제공한다. |
| `Assets/Scripts/Common/SimulationFailureUtility.cs` | Spawn 오류를 로그와 `SimulationFatalError`로 ECB에 게시한다. |
| `Assets/Scripts/Common/PrefabLookupUtility.cs` | 건물·아이템·자원 프리팹 버퍼 조회를 제공하며 누락/Null 프리팹 실패 정책을 유지한다. |
| `Assets/Scripts/Common/ResourceGenerationConfigLookupUtility.cs` | 품목별 자원 생성 설정 버퍼를 조회한다. |
| `Assets/Scripts/Components/Belts/BeltComponents.cs` | `BeltComponent`, `BeltMovementState` 정의. |
| `Assets/Scripts/Components/Belts/BeltDecisions.cs` | `BeltMovementDecision` 정의. |
| `Assets/Scripts/Components/Belts/BeltSpatialIndex.cs` | `BeltInfo`, `BeltSpatialIndex`, `BeltSpatialIndexFence` 정의. |
| `Assets/Scripts/Components/Buildings/BuildingComponents.cs` | `BuildingTypeEnum`, `BuildingType`, `BuildingFootprint`, `IndestructibleBuilding` 정의. |
| `Assets/Scripts/Components/Buildings/BuildingConfigComponents.cs` | `BuildingConfig`, `BuildingConfigElement`, `BuildingConstructionMaterialElement` 정의. |
| `Assets/Scripts/Components/Buildings/BuildingRequests.cs` | `SpawnBuildingRequest`, `DemolishBuildingRequest` 정의. |
| `Assets/Scripts/Components/Buildings/BuildingRuntimeConfigComponents.cs` | `BuildingRuntimeConfig`, `BuildingRuntimeConfigElement` 정의. |
| `Assets/Scripts/Components/Buildings/BuildingSpatialIndex.cs` | `BuildingInfo`, `BuildingSpatialIndex`, `BuildingSpatialIndexFence` 정의. |
| `Assets/Scripts/Components/Buildings/PlacementStamp.cs` | `PlacementStamp` 정의. |
| `Assets/Scripts/Components/Common/GridComponents.cs` | `DirectionEnum`, `GridPosition`, `Direction` 정의. |
| `Assets/Scripts/Components/Common/IRequestComponent.cs` | `IRequestComponent`, `IEnableableRequest` 정의. |
| `Assets/Scripts/Components/Construction/BuildingPlacementRequests.cs` | `BuildingPlacementRequest`, `PlacementRequestCandidateElement` 정의. |
| `Assets/Scripts/Components/Construction/ConstructionComponents.cs` | `ConstructionSiteFlags`, `ConstructionSite`, `ConstructionMaterialRequirementElement` 정의. |
| `Assets/Scripts/Components/Construction/ConstructionRequests.cs` | `SupplyConstructionMaterialRequest`, `CancelConstructionRequest` 정의. |
| `Assets/Scripts/Components/Construction/PlacementComponents.cs` | `PlacementFlags`, `PlacementValidationCode`, `PlacementValidationResult`, `PlacementCandidate` 정의. |
| `Assets/Scripts/Components/Items/ItemComponents.cs` | `ItemTypeEnum`, `ItemIdentity`, `ItemOwnership` 정의. |
| `Assets/Scripts/Components/Items/ItemConfigComponents.cs` | `ItemDataBlob`, `ItemRegistryBlob`, `ItemRegistry` 정의. |
| `Assets/Scripts/Components/Items/ItemRequests.cs` | `ItemSpawnDestination`, `SpawnItemRequest`, `DestroyItemRequest`, `TransferOwnershipRequest` 정의. |
| `Assets/Scripts/Components/Items/ItemSpatialIndex.cs` | `ItemSpatialIndex`, `ItemSpatialIndexFence` 정의. |
| `Assets/Scripts/Components/Prefabs/PrefabDatabaseReadiness.cs` | `PrefabDatabaseReady`, `SimulationFatalError` 정의. |
| `Assets/Scripts/Components/Prefabs/PrefabDatabaseComponents.cs` | `BuildingPrefabDatabase`, `BuildingPrefabElement`, `ItemPrefabDatabase`, `ItemPrefabElement`, `ResourcePrefabDatabase`, `ResourcePrefabElement`, `DronePrefabDatabase`, `DronePrefab` 정의. |
| `Assets/Scripts/Components/Production/Crafting/CrafterComponents.cs` | `CrafterStatusEnum`, `CrafterState` 정의. |
| `Assets/Scripts/Components/Production/Crafting/CrafterDecisions.cs` | `CrafterDecision`, `CrafterStateDecision` 정의. |
| `Assets/Scripts/Components/Production/Crafting/CrafterRequests.cs` | `ChangeCrafterRecipeRequest` 정의. |
| `Assets/Scripts/Components/Production/Crafting/RecipeConfigComponents.cs` | `RecipeIngredientBlob`, `RecipeOutputBlob`, `RecipeBlob`, `RecipeRegistryBlob`, `RecipeRegistry` 정의. |
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
| `Assets/Scripts/Config/RecipeConfigLoader.cs` | JSON/Resources 레시피를 Blob으로 변환하고 기본 5개 레시피를 제공한다. |
| `Assets/Scripts/Phases/CommandGroup.cs` | 명령 처리 그룹을 선언한다. |
| `Assets/Scripts/Phases/DecisionGroup.cs` | Command 이후 판단 그룹을 선언한다. |
| `Assets/Scripts/Phases/ExecutionGroup.cs` | Reservation 이후 실행 그룹을 선언한다. |
| `Assets/Scripts/Phases/GameSimulationGroup.cs` | 프리팹 준비 전/중단 오류 후 실행을 차단하는 V2 최상위 그룹이다. |
| `Assets/Scripts/Phases/ReservationGroup.cs` | Decision 이후 예약 그룹을 선언한다. |
| `Assets/Scripts/Phases/StateApplyGroup.cs` | Execution 이후 상태 반영 그룹을 선언한다. |
| `Assets/Scripts/Phases/SynchronizationGroup.cs` | StateApply 이후 동기화 그룹을 선언한다. |

## 런타임 시스템과 검증 (24개)

| 파일 | 현재 역할 |
| --- | --- |
| `Assets/Scripts/Systems/0_Initialization/PrefabDatabaseInitializationSystem.cs` | SubScene 로딩 이후 세 DB를 검증하고 게임 시작 허용 또는 중단을 게시한다. |
| `Assets/Scripts/Systems/0_Initialization/ItemConfigInitSystem.cs` | StreamingAssets의 스택 설정을 읽어 `ItemRegistry`를 한 번 게시하고 Blob을 해제한다. |
| `Assets/Scripts/Systems/0_Initialization/RecipeInitSystem.cs` | `RecipeRegistry`를 한 번 게시하고 소유 Blob을 해제한다. |
| `Assets/Scripts/Systems/1_Command/CrafterRecipeCommandSystem.cs` | 새 입력 슬롯 계산 검증 후 진행도·슬롯 수·품목 배정·필터를 갱신하고 잔여 재료를 스택 구분을 유지해 Product로 옮긴다. Decision 데이터는 수정하지 않는다. |
| `Assets/Scripts/Systems/2_Decision/BeltMovementDecisionSystem.cs` | 벨트 속도, 프레임 시간, 같은/다음 타일의 아이템 간격을 이용해 `PlannedProgress`를 계산한다. |
| `Assets/Scripts/Systems/2_Decision/BuildingItemInputDecisionSystem.cs` | 벨트 끝에서 다음 셀의 건물, Storage, 필터, 제작기 부산물 대기 상태를 검사한다. |
| `Assets/Scripts/Systems/2_Decision/CrafterDecisionSystem.cs` | 레시피·재료·출력 슬롯을 검사해 제작 실행 결정과 다음 상태를 분리해 기록한다. |
| `Assets/Scripts/Systems/2_Decision/MinerDecisionSystem.cs` | footprint 아래 유효 자원, 품목 일치, 출력 스택 공간을 검사한다. |
| `Assets/Scripts/Systems/2_Decision/ProductItemOutputDecisionSystem.cs` | 생산 건물의 Product 버퍼에서 Slot 0을 우선해 외향 벨트 출고를 결정한다. |
| `Assets/Scripts/Systems/2_Decision/StorageItemOutputDecisionSystem.cs` | Product 버퍼가 없는 일반 저장 건물의 첫 Stored 아이템 출고를 결정한다. |
| `Assets/Scripts/Systems/3_Reservation/BeltDestinationReservationSystem.cs` | 본선·건물 출고·라우팅 결정이 같은 목표 벨트를 요구할 때 공간과 우선순위를 판정한다. |
| `Assets/Scripts/Systems/3_Reservation/BuildingStorageInputReservationSystem.cs` | 현재 버퍼와 프레임 내 예약 수를 합산하여 품목별 저장 슬롯을 하나씩 확정한다. |
| `Assets/Scripts/Systems/4_Execution/BeltMovementExecutionSystem.cs` | 계획된 타일 내 진행도, 경계 횡단, 격자 위치와 시각 위치를 반영하고 계획을 소비한다. |
| `Assets/Scripts/Systems/4_Execution/CrafterExecutionSystem.cs` | 재료 선소비 및 삭제 요청, 진행도 누적, 주생산품/부산물 `ProductResult` 기록을 수행한다. |
| `Assets/Scripts/Systems/4_Execution/MinerExecutionSystem.cs` | 채굴 진행, 자원 차감/고갈 삭제 요청과 `ProductResult` 기록을 수행한다. |
| `Assets/Scripts/Systems/5_StateApply/BuildingItemStorageApplySystem.cs` | 입고/출고 버퍼·벨트 상태·위치와 소유권 이전 요청을 반영한다. |
| `Assets/Scripts/Systems/5_StateApply/CrafterStateApplySystem.cs` | `CrafterStateDecision.NextStatus`를 적용하고 결정을 비활성화한다. |
| `Assets/Scripts/Systems/5_StateApply/EndStateApplyEntityCommandBufferSystem.cs` | StateApply 끝에서 누적된 구조 변경을 재생한다. |
| `Assets/Scripts/Systems/5_StateApply/ItemLifecycleApplySystem.cs` | 생산 결과와 명시적 스폰 요청을 실제 아이템/버퍼로 바꾸고 삭제 요청을 처리한다. |
| `Assets/Scripts/Systems/5_StateApply/ItemOwnershipApplySystem.cs` | 유효한 대상에 소유권을 옮기고 이전 요청을 비활성화한다. |
| `Assets/Scripts/Systems/6_Synchronization/BeltSpatialSyncSystem.cs` | 벨트 셀 인덱스를 비우고 현재 벨트 엔티티에서 재구축한다. |
| `Assets/Scripts/Systems/6_Synchronization/BuildingSpatialSyncSystem.cs` | footprint의 모든 점유 셀을 건물 인덱스로 재구축한다. |
| `Assets/Scripts/Systems/6_Synchronization/ItemSpatialSyncSystem.cs` | `Owner == Entity.Null`인 아이템만 공간 인덱스로 재구축한다. |
| `Assets/Scripts/Systems/6_Synchronization/ResourceSpatialSyncSystem.cs` | 자원 노드 셀 인덱스를 재구축한다. |
| `Assets/Scripts/Validation/WorldInvariantValidationSystem.cs` | Editor/개발 빌드에서 프레임 말 정합성을 검사하고 위반 보고서를 `Logs/InvariantErrors`에 기록한다. |

## EditMode 테스트와 공통 도우미 (24개)

| 파일 | 검증 대상으로 작성된 범위 |
| --- | --- |
| `Assets/Editor/Tests/Phase1ItemIntegrationTests.cs` | 스폰, 소유권 이전, 공간 반영, 삭제, 잘못된 목적지/버퍼 정합성. |
| `Assets/Editor/Tests/Phase2BeltDecisionTests.cs` | 자유 이동, 벨트 끝, 같은/다음 타일 간격, 판단 단계의 무변경. |
| `Assets/Editor/Tests/Phase2BeltExecutionTests.cs` | 타일 이동/회전, 연속 홉, 경계 정지, 계획 소비, 큰 delta time. |
| `Assets/Editor/Tests/Phase2BeltIntegrationTests.cs` | 다중 타일 흐름, 후방 정체, 4개 수용, 간격 위반 탐지. |
| `Assets/Editor/Tests/Phase3BuildingDecisionComponentTests.cs` | 건물 입출고 결정의 데이터와 enable 상태. |
| `Assets/Editor/Tests/Phase3BuildingInputDecisionTests.cs` | 벨트 끝 입고, 필터, 비저장 건물, 다중 타일 건물. |
| `Assets/Editor/Tests/Phase3BuildingOutputDecisionTests.cs` | 외향 벨트와 진입 공간, 다중 벨트 선택. |
| `Assets/Editor/Tests/Phase3BuildingSpatialIndexTests.cs` | 단일/다중 타일, 회전, 삭제 후 재구축, 용량 증가. |
| `Assets/Editor/Tests/Phase3ItemConfigTests.cs` | 스택 설정 로드, 기본값/커스텀 JSON, Blob 조회, Burst 접근. |
| `Assets/Editor/Tests/Phase3StorageComponentTests.cs` | 비트셋/필터, 저장 슬롯 경계와 위반 탐지. |
| `Assets/Editor/Tests/Phase3StorageDecisionTests.cs` | 입출고 결정 통합, 복수 벨트, 혼잡 후 재개. |
| `Assets/Editor/Tests/Phase3StorageInvariantTests.cs` | 고아 저장품, 버퍼 참조, 슬롯/스택/필터, 미소비 결정. |
| `Assets/Editor/Tests/Phase3StorageOwnershipTests.cs` | 입출고 소유권과 단일 남은 슬롯 경합, 스택 병합/새 슬롯. |
| `Assets/Editor/Tests/Phase4EndToEndPipelineTests.cs` | 채굴→벨트→저장 루프와 연속 생산/역압. |
| `Assets/Editor/Tests/Phase4MinerComponentTests.cs` | 자원 컴포넌트/공간 인덱스, 채굴 결정 데이터, 고갈 후 갱신. |
| `Assets/Editor/Tests/Phase4MinerPipelineTests.cs` | 자원 탐색·채굴·생산·출고, 무한/고갈, 다중 footprint, 스택과 시간 상한. |
| `Assets/Editor/Tests/Phase5CrafterExecutionTests.cs` | 재료 선소비, 진행/완료, 출력 역압, 다중 부산물의 전체 용량 판정. |
| `Assets/Editor/Tests/Phase5RecipeBlobTests.cs` | 레시피 JSON/기본값, 다중 재료/부산물, 조회와 Burst 접근. |
| `Assets/Editor/Tests/Phase5CrafterInputSlotTests.cs` | F-037 1단계: 슬롯 계산의 올림, 중복 합산, 품목 구분, 상한/실패 및 Burst Job 호출. |
| `Assets/Editor/Tests/Phase5CrafterInputPipelineTests.cs` | F-037: 개별 회귀 15개와 실제 정렬 6단계 통합 4개. 직접 Spawn/공사 완료 × ECS 테스트 프리팹/fallback, 미선택 차단·입고·선소비·생산·후속 출고·레시피 변경/해제 및 불변식. |
| `Assets/Editor/Tests/Phase5RecipeChangePipelineTests.cs` | 레시피 변경 잔여물 배출, 입고 차단과 재개. |
| `Assets/Editor/Tests/Phase6BeltDestinationReservationTests.cs` | 본선 우선, 배치 순서 경합, 외부 출고/라우팅 경합과 목표 공간. |
| `Assets/Editor/Tests/Phase6RoutingContractTests.cs` | Splitter/Merger 방향·포트·커서·전송 결정·배치 순서 계약. |
| `Assets/Editor/Tests/TestSupport/EcsWorldTestFixture.cs` | 독립 ECS World/EntityManager 생성과 정리. |
| `Assets/Editor/Tests/TestSupport/TestEntityFactory.cs` | 자원, 벨트/아이템, 저장, 채굴기, 제작기, 일반 건물 테스트 엔티티 생성. |
| `Assets/Editor/Tests/TestSupport/TestSimulationDriver.cs` | 시간·개별 시스템·ECB 경계 지원. 자원 생성/채굴 및 제작기 생성·물류의 실제 정렬 6단계 테스트 그룹을 구성한다. |
