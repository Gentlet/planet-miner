# Planet Miner 코드 지도

## 프로젝트 개요

`planet miner`는 격자 기반 2D 채굴·공장 시뮬레이션 Unity 프로젝트다. 현재 소스는 Architecture V2로 전환 중이며, Unity Entities의 unmanaged 컴포넌트와 `ISystem`/Burst Job을 중심으로 구성한다. 설정 로드 일부와 시스템 그룹, 개발용 검증에는 managed 시스템을 사용한다.

이 지도는 2026-09-28의 `Assets/Scripts/`, `Assets/Editor/` 소스와 설정·장면 연결을 기준으로 갱신했다. 현재 구현 범위는 아이템 수명주기, 벨트·저장·채굴·제작·분배/합류, 청크 자원 생성과 바닥 선택, 프리팹 DB 베이킹, 건물 배치 검증·공사 현장 생성·자재 수령·완공 건물 직접 스폰이다.

`Assets/Scenes/V2 Test Scene.unity`는 `Assets/Scenes/V2 Test Scene/sub.unity`를 자동 로드하며, 이 SubScene에 건물·아이템·자원 프리팹 DB Authoring이 있다. 메인 장면의 `V2FloorBiomePreview`는 진단용 색상 텍스처를 표시한다. 한편 `ProjectSettings/EditorBuildSettings.asset`의 활성 빌드 장면은 아직 `Assets/Scenes/SampleScene.unity`다. V2 테스트 장면과 빌드 진입 장면이 같다고 가정하지 않는다.

2026-09-29 F-037 검증에서 공사 완료의 완공 건물 생성과 제작기 입력·생산 연결을 확인했다. 주 시설 자동 부트스트랩, 전력망, 드론 운송, 연구 진행, 게임 입력·카메라 제어·UI Toolkit 컨트롤러, 실제 바닥 청크 렌더링 시스템은 이 지도의 기존 미구현 범위다. enum·요청 필드·프리팹·JSON·계획이 존재하는 것과 해당 기능이 구현된 것은 구분한다.

현재 사용자 지시와 검증한 소스를 기억·문서보다 우선한다. `.agents/` 및 `Docs/architecture v2 plan/`의 계획은 설계 배경으로 참고하고, 현재 동작은 컴포넌트, 쿼리, 업데이트 순서, 실제 호출 관계로 확인한다. 코드 확인 결과와 실제 실행으로 검증한 결과를 구분해 보고한다.

## 문서 기준과 탐색·출력 규칙

- 이 `AGENTS.md`를 프로젝트 공통 설계·안전 규칙, 현재 코드 지도, 검색·출력 및 Unity CLI 검증 절차의 기준 문서(canonical source)로 둔다. 외부 `planet miner.md`는 시작 경로·Git 환경과 여기서 다루지 않는 고유 제약을 보관한다. 아래 상세 문서의 적용 범위를 확인하고, 같은 규칙의 전문을 다른 파일에 복제하지 않는다.
- 이미 컨텍스트에 제공되거나 이번 작업에서 읽은 동일한 지침은 변경되지 않았다면 다시 전문 출력하지 않는다. 지침 변경이나 컨텍스트 소실이 있으면 필요한 부분을 다시 확인한다.
- 대상 파일이나 심볼을 알고 있으면 저장소 전체 검색부터 시작하지 않는다. 기본 순서는 **파일명/경로 확인 → 정확한 심볼 검색 → 해당 메서드·초기화·직접 의존 구간 확인 → 필요한 경우에만 범위 확대**다. `rg --files`로 후보 경로를 좁히고, 리터럴 심볼에는 `rg -n -F`를 사용한다.
- 일반 코드 탐색은 대상이 속한 `Assets/Scripts/` 하위 경로와 관련 `Assets/Editor/` 테스트로 제한한다. 현재 `Assets/UI/`는 없다. 범위를 넓힐 때는 누락된 호출자, 변경된 데이터 계약, 실패한 테스트 등 이유를 정한다. 파일 목록·크기는 후보 선정에 사용하며 목록 전체의 내용을 읽을 이유로 삼지 않는다.
- `.meta`, `.unity`, `.prefab`, `.asset`, `Packages/`, `UIElementsSchema/`는 해당 형식·참조·직렬화·패키지 문제가 작업과 직접 관련 있을 때만 탐색한다. `Library/`, `Temp/`, `Logs/`, `.git/`도 일반 소스 검색에서 제외하고 진단 근거가 있을 때 필요한 경로만 연다. `Assets/Resources/Config/`는 관련 설정 의미를 확인할 때 추가한다. 파일 삭제나 Git 추적 제외를 뜻하지 않는다.
- 검색 결과가 많거나 출력이 잘리면 출력 한도를 늘리지 말고 경로·심볼·검색어를 좁힌다. `Get-Content -Raw ... | Select-String`은 사용하지 않는다. 줄 단위 `rg -n` 또는 `Select-String -Path`로 검색한 뒤 필요한 구간만 읽는다.
- 이미 읽은 대형 파일이 변경되지 않았다면 다시 전문 출력하지 않는다. 심볼·변경 구간과 필요한 직접 의존 부분만 확인한다. 다른 작업자의 변경이나 컨텍스트 소실이 의심되면 해당 파일의 변경 여부를 확인한다. 긴 작업의 인계 요약에는 읽은 경로·심볼, 확인한 계약과 남은 의문을 남겨 재탐색을 줄인다.
- CodeMemory 전체를 읽지 않는다. 현재 작업 영역에 직접 대응하는 문서만 읽고, 다른 영역은 변경된 계약이나 실패 근거가 생겼을 때 관련 절만 추가한다. 구버전 문서의 심볼은 현재 소스에 존재하는지 먼저 확인한다. 문서의 "함께 확인할 영역"은 해당 변경 종류에 적용하며, 모든 의존 문서를 연쇄적으로 읽으라는 뜻이 아니다. 테스트도 관련 사례의 초기화·헬퍼·assertion부터 확인한다.
- 대량 원본 로그는 컨텍스트에 재출력하지 않고 필요한 경우 파일로 보존한다. 성공 결과는 최종 상태·통계만, 실패는 실패명·메시지·관련 스택만 출력한다. 요약 과정에서 실패·경고·생략·미완료 상태를 숨기지 않는다.
- 코드 구조나 계약이 바뀌면 그 변경에 해당하는 AGENTS/CodeMemory 절의 갱신 필요성을 확인한다. 국소 변경을 이유로 모든 문서와 과거 기록을 재감사하지 않는다.

## 실행 단계와 시스템 지도

`InitializationSystemGroup`에서 설정과 초기 청크 요청을 준비한다. `SimulationSystemGroup` 아래 `GameSimulationGroup`은 다음 여섯 그룹을 순서대로 실행한다.

```text
Command → Decision → Reservation → Execution → StateApply → Synchronization
```

| 단계 / `Assets/Scripts/Systems/` 하위 폴더 | 현재 시스템과 책임 |
| --- | --- |
| `0_Initialization/` | `ItemConfigInitSystem`, `RecipeInitSystem`, `BuildingConfigInitSystem`, `WorldGenerationConfigLoadSystem`이 설정을 게시한다. `InitialChunkLoadBootstrapSystem`은 월드 설정 이후 초기 청크를 요청한다. |
| `1_Command/` | `ChunkLoadCommandSystem`은 청크 생성 수명주기를 관리하고 `ResourceGenerationCommandSystem`이 그 뒤에 자원 스폰을 기록한다. `CrafterRecipeCommandSystem`은 레시피를 변경하고 `BuildingPlacementCommandSystem`은 배치 요청을 검증하고, `BuildingDemolitionCommandSystem`은 철거 요청을 검증한다. `EndCommandEntityCommandBufferSystem`이 Command 끝(OrderLast)에서 구조 변경을 재생하여 동일 프레임 Phase 2~4에서 물리적 실체로 상호작용할 수 있게 한다. |
| `2_Decision/` | `BeltMovementDecisionSystem`, `BuildingItemInputDecisionSystem`, `StorageItemOutputDecisionSystem`, `ProductItemOutputDecisionSystem`, `MinerDecisionSystem`, `CrafterDecisionSystem`, `SplitterDecisionSystem`, `MergerDecisionSystem`이 실행 후보를 계산한다. |
| `3_Reservation/` | `BuildingStorageInputReservationSystem`이 저장 슬롯을 배정하고 `BeltDestinationReservationSystem`이 건물 출고와 라우팅 전달의 목적지 경합을 중재한다. |
| `4_Execution/` | `BeltMovementExecutionSystem`이 위치·진행도를 반영한다. `MinerExecutionSystem`과 `CrafterExecutionSystem`은 작업 진행과 생산 결과를 기록하며 제작 재료를 선소비한다. |
| `5_StateApply/` | `BuildingItemStorageApplySystem`, `ItemOwnershipApplySystem`, `ItemLifecycleApplySystem`, `CrafterStateApplySystem`, `RoutingApplySystem`, `BuildingLifecycleApplySystem`, `ConstructionLifecycleApplySystem`이 각 결과를 반영한다. `BuildingLifecycleApplySystem`은 물류 전송 이후에 실행(`UpdateAfter(RoutingApplySystem, BuildingItemStorageApplySystem)`)된다. `EndStateApplyEntityCommandBufferSystem`이 마지막(OrderLast)에 구조 변경을 재생한다. |
| `6_Synchronization/` | `BeltSpatialSyncSystem`, `BuildingSpatialSyncSystem`, `ItemSpatialSyncSystem`, `ResourceSpatialSyncSystem`이 공간 인덱스를 재구축한다. |

- 같은 그룹 안의 순서를 파일명이나 위 표의 나열 순서로 추정하지 않는다. 실제 `UpdateBefore`/`UpdateAfter`/`OrderLast`와 Job 의존성을 확인한다.
- `BuildingItemStorageApplySystem`과 `RoutingApplySystem`은 `BuildingLifecycleApplySystem` 및 `ItemOwnershipApplySystem`보다 먼저 실행한다. `WorldInvariantValidationSystem`은 Synchronization의 `OrderLast`다.
- `BuildingDemolitionCommandSystem`은 대상 유효성·타입·철거 불가 조건을 검증하고 중복을 제거한다. 거부/중복 `DemolishBuildingRequest`는 EndCommand에서 삭제하며, 남은 요청을 `BuildingLifecycleApplySystem`과 `ItemLifecycleApplySystem`이 StateApply에서 읽기 전용으로 사용한다. 두 시스템 사이의 철거 승인 전달용 `UpdateAfter`는 없다. Item Lifecycle은 요청의 임시 비동기 복사본을 사용 후 해제하고, 유효 요청은 EndStateApply에서 삭제한다. 요청은 Command 검증 전에 실체화해야 하며 이후 대상·철거 가능 조건을 변경하지 않는다. 철거 대상의 완료 생산물은 생성 없이 폐기하고 Storage/Product Spawn은 거부한다. 선소비 재료·광물은 보상하지 않으며 기존 실물 반환·건축 비용 환급·World Spawn은 유지한다. 이는 기존 실물의 동시 입고·철거 렌더 태그 경합(F-004) 해결을 뜻하지 않는다.
- 계획·테스트 이름의 `Phase7` 등은 개발 마일스톤 번호다. 런타임에 일곱 번째 실행 그룹이 있는 것은 아니다.

## 핵심 데이터와 변경 규칙

### 공간과 작업 의존성

- ECS 컴포넌트가 원본 상태다. `GridPosition.Value`가 격자 좌표이며 건물·공사 현장은 좌하단 기준점으로 사용한다. `LocalTransform`만 바꾸어 공간 이동을 전달하는 기존 방식은 현재 구조에 없다.
- `BeltSpatialIndex`는 셀→`BeltInfo`, `BuildingSpatialIndex`는 점유 셀→`BuildingInfo`, `ResourceSpatialIndex`는 셀→자원 엔티티, `ItemSpatialIndex`는 셀→복수 월드 아이템을 저장한다. 소유자는 각 `*SpatialSyncSystem`이며 매 동기화 단계에서 Clear 후 재등록한다. `ChunkMapSystem`은 현재 소스에 없다.
- 각 맵의 `*SpatialIndexFence`는 NativeContainer 읽기·쓰기 Job 의존성을 관리한다. Reader는 마지막 Writer에 의존하고 자신의 핸들을 등록하며, Writer는 이전 Writer와 모든 Reader를 기다린다. 메인 스레드 직접 접근·용량 변경·Dispose 때도 해당 Fence를 확인한다. ECS 컴포넌트 의존성만으로 맵 접근이 동기화된다고 가정하지 않는다.
- 인덱스는 Synchronization 이전의 구조 변경을 즉시 반영하지 않는다. 동일 프레임 배치·입출고·스폰 경합은 해당 요청/예약 경계에서 처리하며, 인덱스만 조회하고 이미 반영됐다고 간주하지 않는다.
- 청크 변환은 `Chunks/ChunkUtility`의 floor division, 방향 오프셋은 `DirectionExtensions`, 라우팅 회전·포트 계산은 `RoutingDirectionUtility`를 사용한다. Footprint 계산은 `BuildingFootprint.GetEffectiveSize`와 `BuildingPlacementValidationUtility.GetEffectiveSize`를 확인한다. 크기 변경은 요청→생성→인덱스의 회전 적용 횟수까지 추적한다.
- 구조 변경은 2-Sync Point 모델로 분리 관리한다. Phase 1 명령의 구조적 변경은 `EndCommandEntityCommandBufferSystem`에 기록하여 Command 종료 시점에 재생되며, Phase 4~5의 고갈 자원 파괴·수명주기 전이·철거·아이템 스폰/소멸은 `EndStateApplyEntityCommandBufferSystem`에 기록하여 StateApply 종료 시점에 재생된다. 실제 엔티티 생성·삭제 및 렌더 태그 변경은 각 Playback 시점에 확정된다. 구조 변경 전후에 `DynamicBuffer`를 계속 보관하지 말고, 필요하면 `ToNativeArray` 등으로 복사한 뒤 버퍼를 다시 얻는다. 기존 문서의 `DynamicBufferCopyUtility`는 현재 소스에 없다.

### 아이템·결정·요청

- 아이템 하나는 엔티티 하나다. `ItemIdentity.Type`이 종류, `ItemOwnership.Owner == Entity.Null`이면 월드 아이템이고 그 외에는 해당 소유자에 수납된 아이템이다. ECS `Disabled`를 소유권 표현으로 사용하지 않는다.
- `StoredItemElement`는 저장품/제작 재료/공사 도착 자재, `ProductItemElement`는 생산품 출력 대기 버퍼다. 버퍼의 엔티티 참조와 `ItemOwnership`을 함께 유지한다. `Storage.SlotCount`와 `ItemRegistry`의 품목별 `MaxStack`은 서로 다른 제한이다.
- `BuildingInputSlotElement`는 슬롯별 허용 품목을 정하고 버퍼 길이는 `Storage.SlotCount`와 같다. Crafter는 생성 시 0슬롯/빈 Whitelist로 시작하며, 레시피 변경 시 `BuildingInputSlotUtility`로 품목별 요구량을 합산하여 `ceil(요구량/MaxStack)`개의 전용 슬롯을 구성한다. 입고 예약은 해당 품목 슬롯만 사용한다. 버퍼가 없는 일반 창고에는 이 전용 슬롯 규칙을 적용하지 않는다.
- 일반 입출고는 `BuildingItemStorageApplySystem`이 버퍼·위치·벨트 상태를 갱신하고 `TransferOwnershipRequest`를 발행한다. `ItemOwnershipApplySystem`이 소유권과 `DisableRendering`을 반영한다. 공사 자재 수령은 현재 `ConstructionMaterialApplySystem`이 별도로 ECB에 소유권·렌더 태그·현장 버퍼 변경을 기록하므로 함께 확인한다.
- 아이템 생성/삭제는 `ItemLifecycleApplySystem`이 담당한다. 저장된 아이템을 소비할 때는 소유 버퍼에서 먼저 제거한 뒤 `DestroyItemRequest`를 활성화한다. `TransferOwnershipRequest`만으로 소유 버퍼의 추가/제거까지 이루어지지는 않는다.
- `ProductResult`는 Execution이 기록하고 Item Lifecycle이 소비하는 임시 생산 결과 버퍼다. 생산량 `Count`는 실제 아이템 엔티티 수로 변환된다. 생산물의 슬롯 0은 주생산품이고 후속 슬롯은 부산물에 사용한다.
- `BeltMovementState`는 enableable 실제 이동 상태, `BeltMovementDecision`은 일반 컴포넌트인 프레임 이동 계획이다. 그 밖의 입출고·채굴·제작·라우팅 결정에는 enable 상태가 처리 대상 여부를 나타낸다. 컴포넌트 존재와 활성 상태를 구분하고, `IgnoreComponentEnabledState` 쿼리는 의도한 범위를 확인한다.
- 일회성 요청은 처리 후 엔티티 삭제, enableable 요청은 비활성화, 임시 결과 버퍼는 Clear로 소비한다. 준비 대기 중인 청크 요청처럼 재시도가 필요한 데이터는 소비 조건이 충족될 때까지 유지한다. 프레임 결정과 `IRequestComponent`를 혼동하지 않는다.
- `GameConstants`가 아이템 간격, 타일 수용량, 저장 슬롯 상한, 시뮬레이션 delta time 상한을 제공한다. 각 시스템에 별도 숫자 규칙을 복제하지 않는다.

### 베이킹·프리팹·코드 관례

- Baker는 프리팹 참조와 정적 식별 데이터를 제공하고, 생성 시스템이 위치·소유권·진행도·요청의 런타임 상태를 초기화한다. `ItemAuthoring`은 `ItemIdentity`를 베이킹한다. 건물·아이템·자원 DB는 각각 별도 엔티티와 버퍼를 사용한다.
- 건물과 아이템 생성은 DB가 없으면 테스트용 fallback archetype을 사용한다. DB가 있는데 해당 프리팹이 누락되면 오류를 기록하고 그 스폰을 거부한다. 자원 생성은 필요한 DB/프리팹이 준비될 때까지 청크 전체를 대기시킨다. 서로 다른 누락 정책을 하나의 fallback 규칙으로 바꾸지 않는다.
- `DirectionEnum`의 `Up, Right, Down, Left, Count` 순서는 회전과 직렬화 의미를 가진다. 기존 enum의 값·순서·`None`·정의된 terminal `Count`를 보존하고, 모든 enum에 `Count`가 있다고 가정하지 않는다. 새 도메인 enum은 기존 `Enum` 접미사 관례를 우선한다.
- MonoBehaviour는 Authoring·표시·입력/요청 생성에 집중하고, 게임플레이 상태와 작업 진행은 ECS가 소유한다. 같은 역할의 새 공간 캐시나 병렬 구현을 중복 생성하지 않는다.
- 읽기 쉬운 작은 메서드와 설명적인 이름을 사용한다. null guard는 각각의 조기 반환으로 분리하고, null 검사와 행동 조건을 한 조건문에 섞지 않는다. 롤백과 소유권 경계를 축약으로 숨기지 않는다.
- UI Toolkit을 추가할 때 콜백은 `OnEnable`에서 연결하고 `OnDisable`에서 해제하며, 월드 입력 차단 요소는 `blocking-ui` USS 클래스를 사용한다. 이는 현재 UI 구현이 존재한다는 뜻은 아니다.

## 주요 실행 흐름과 구현 경계

### 초기화·월드 생성

| 설정 입력 | 게시 시스템 / 데이터 |
| --- | --- |
| `Assets/StreamingAssets/ItemConfig.json` | `ItemConfigInitSystem` → `ItemRegistry` / `ItemRegistryBlob`. 설정 부재 시 기본값 경로가 있다. |
| `Assets/Resources/Config/CrafterRecipeConfig.json` | `RecipeInitSystem` / `RecipeConfigLoader` → `RecipeRegistry` / `RecipeRegistryBlob`. 기본 레시피 경로가 있다. |
| `Assets/Resources/Config/BuildingConfig.json` | `BuildingConfigInitSystem` / `BuildingConfigLoader` → `BuildingConfigElement`, `BuildingConstructionMaterialElement`, 호환용 `BuildingRuntimeConfigElement`. 검증 실패 시 게시하지 않는다. |
| `Assets/Resources/Config/WorldGenerationConfig.json` | `WorldGenerationConfigLoadSystem` / `WorldGenerationConfigLoader` → 자원 설정과 `FloorGenerationSettings`, `FloorBiomeElement`, `FloorVariantElement`를 같은 엔티티에 게시한다. 자원·바닥·Sprite 참조 검증 실패 시 부분 게시하지 않는다. |

`BuildingConfigLoadSystem.cs`에는 실제 로더인 managed `BuildingConfigInitSystem`과, `BuildingConfig`를 요구하고 한 번 실행 후 비활성화되는 `BuildingConfigLoadSystem`이 함께 있다. 이름만 보고 로드 책임을 잘못 배정하지 않는다. 기존 전력·드론·연구·시작 아이템 JSON의 존재는 현재 로더가 사용한다는 증거가 아니다.

1. `InitialChunkLoadBootstrapSystem`이 월드 설정의 `InitialChunkSize`로 원점 주변 N×N 청크를 `ChunkLoadRequestQueue`에 한 번 넣는다.
2. `ChunkLoadCommandSystem`이 `GeneratedChunkTracker.Map`(완료)과 `Pending`(접수/반영 대기)으로 중복을 제거하고 `GeneratedChunkReadyElement`로 넘긴다. 이 Tracker는 생성 수명주기이며 공간 점유 인덱스가 아니다.
3. `ResourceGenerationCommandSystem`은 활성 자원 설정의 프리팹 전체가 유효할 때 `ResourceGenerationUtility`로 자원을 생성한다. 월드 시드·후보 청크·품목을 사용하며, 이웃 청크에서 시작된 광맥 중 대상 청크의 셀만 생성한다.
4. 자원 스폰과 `GeneratedChunkCompletedElement`를 같은 EndStateApply ECB에 기록한다. 다음 Command가 완료 알림을 받아야 Pending에서 Map으로 이동한다. 정상적인 빈 청크도 이 완료 절차를 거친다.
5. `ResourceSpatialSyncSystem`이 생성된 자원을 등록한다. 다음 프레임의 채굴 판단이 이 인덱스를 읽는다.

`FloorBiomeSampler`는 같은 `WorldSeed`와 월드 셀 좌표로 바이옴·전이·변형 Sprite 경로를 선택하는 순수 계산이다. `V2FloorBiomePreview`는 결과를 진단용 텍스처로 보여 주며, 실제 Sprite 청크 메시·표시 수명주기는 구현하지 않는다.

### 배치·공사·완공 건물 스폰

- 현재 연결된 흐름은 `BuildingPlacementRequest` + `PlacementRequestCandidateElement` → `BuildingPlacementCommandSystem` → `ConstructionSite` 생성이다. 검증은 `BuildingPlacementValidationUtility`가 건물/공사 점유, 채굴기 하부 자원, 해금, 바닥 아이템을 확인한다.
- 한 배치 묶음은 기본 `StrictAllOrNothing` 또는 `AllowPartialPlacement` 정책을 사용한다. 유틸리티의 임시 `claimedCells`는 해당 묶음 내부의 선점 검사다. 여러 요청 엔티티 전체에 걸친 영속 예약으로 설명하지 않는다.
- 승인된 현장은 `BuildingTypeEnum.ConstructionSite`, 위치·크기·방향·`PlacementStamp`, 자재 요구 버퍼와 보관 버퍼를 가지며 건물 공간 인덱스에 포함된다. 바닥 아이템은 배치를 막지 않고 `AwaitingItemClearance`를 표시한다. 기존 같은 타입 벨트 덮어쓰기는 새 현장 대신 방향 변경을 ECB에 기록한다.
- `SupplyConstructionMaterialRequest` → `ConstructionMaterialApplySystem`은 현장·품목·잔여 요구량을 확인하고 도착량/예약량·보관 소유권을 갱신한다. 현재 `ConstructionSite.Progress`는 자재 수령 비율이다. 별도 건설 작업 시간이 누적된다고 설명하지 않는다.
- 직접 생성은 `SpawnBuildingRequest` → `BuildingLifecycleApplySystem`, 공사 완료는 `ConstructionLifecycleApplySystem`에서 별도 Spawn 요청 없이 공통 `BuildingLifecycleUtility.SpawnBuilding`을 호출한다. 두 경로 모두 타입별 런타임 구성을 주입한다. F-037은 자재 요구가 충족된 현장에서 완공한 Crafter의 생성·제작 연결을 검증했으며 전체 배치·자재 배송이나 취소·철거를 검증한 것은 아니다.
- `BuildingConfigElement.IsUnlocked`와 해금 변경 유틸리티는 있지만 연구 진행 시스템은 없다. 전력·드론·연구 건물 종류와 프리팹 등록도 해당 시뮬레이션 구현을 의미하지 않는다.

### 물류·생산

- 입고: `BuildingItemInputDecisionSystem`이 벨트 종단의 다음 셀, `Storage`, 필터, 제작기의 잔여물 대기를 확인한다. `BuildingStorageInputReservationSystem`이 슬롯을 확정하고, `BuildingItemStorageApplySystem` → `ItemOwnershipApplySystem`이 반영한다.
- 출고: 일반 창고는 `StorageItemOutputDecisionSystem`이 첫 보관품을, 생산 건물은 `ProductItemOutputDecisionSystem`이 슬롯 0 우선으로 생산품을 선택한다. 외향 벨트의 입구 여유를 검사하고 공통 예약/반영 경로를 사용한다.
- 벨트 이동: `BeltMovementDecisionSystem`이 현재/다음 셀의 간격과 수용량으로 이동량을 계산하고 `BeltMovementExecutionSystem`이 격자·진행도·시각 위치를 변경한 뒤 계획을 소비한다. 현재 `BeltDestinationReservationSystem`의 후보는 건물 출고와 라우팅 전달뿐이다. 일반 벨트 이동까지 통합 예약한다고 가정하지 않는다.
- 분배/합류: `SplitterDecisionSystem`은 입력 벨트 기준 forward→right→left, `MergerDecisionSystem`은 출력 벨트 기준 back→left→right 순환 후보를 선택한다. 예약을 통과한 `RoutingTransferDecision`은 `RoutingApplySystem`이 실제 이동과 커서 갱신에 사용한다. 설치 우선순위는 `PlacementStamp`와 해당 비교 구현을 따른다.
- 채굴: Decision이 footprint 아래 첫 유효 자원과 동일 품목 1스택 출력 여유를 검사한다. Execution은 진행도·자원량과 `ProductResult`를 갱신하며 유한 자원 고갈은 ECB로 삭제한다. `ResourceConfig` 부재 시 기본은 유한 자원이다.
- 제작: `CrafterDecisionSystem`이 실행 결정과 `CrafterStateDecision`을 나누어 기록한다. Execution은 재료를 선소비하고 진행/출력 결과를 만들며, `CrafterStateApplySystem`이 상태를 반영한다. 출력은 주생산품과 모든 부산물 슬롯의 여유를 함께 검사한다.
- 레시피 변경: `CrafterRecipeCommandSystem`이 새 입력 슬롯 계산을 검증한 뒤 진행을 초기화하고 슬롯 수·배정·필터를 함께 갱신한다. 남은 입력 재료는 기존 입력 슬롯 구분을 유지하여 생산물 버퍼의 빈 후속 슬롯으로 옮긴다. 설정 미게시 시 선택 요청을 대기시키고, 무효 레시피/계산 실패는 기존 상태를 보존한다. 해제는 0슬롯/빈 Whitelist이며 설정 없이도 처리한다. `WaitingForByproductOutput` 동안 입고와 새 제작을 막는다. 이 책임을 Execution 시스템의 오래된 주석만 보고 중복 구현하지 않는다.

## 주요 폴더와 테스트 탐색

| 경로 | 역할 |
| --- | --- |
| `Assets/Scripts/Authoring/` | 건물·아이템·자원 프리팹 DB 및 아이템 정적 식별 Baker |
| `Assets/Scripts/Components/` | 격자·설정·요청·실제 상태·프레임 결정·버퍼·공간 인덱스 계약 |
| `Assets/Scripts/Phases/`, `Assets/Scripts/Systems/` | 위 실행 단계 및 각 단계의 시스템 |
| `Assets/Scripts/Common/` | 상수, 요청 인터페이스, 방향·라우팅·배치 검증 |
| `Assets/Scripts/Chunks/` | 청크 좌표 변환, 결정론적 자원 생성, 바닥 선택 |
| `Assets/Scripts/Config/` | 건물·월드·레시피 설정 파싱/검증/게시 |
| `Assets/Scripts/Debug/`, `Assets/Scripts/Validation/` | 바닥 진단 표시와 ECS 불변식 검사 |
| `Assets/Scenes/`, `Assets/Resources/Prefabs/` | 장면/SubScene 및 도메인별 프리팹 |
| `Assets/Resources/Config/`, `Assets/StreamingAssets/` | 위 설정 입력과 기존 설정 파일 |
| `Assets/Editor/` | 건물/아이템 DB의 Resources 목록 채우기 Inspector와 EditMode 테스트 |
| `Tools/Codex/Verify-Unity.ps1` | PowerShell 7 이상용 Unity 컴파일/선택 테스트 검증 래퍼 |

관련 테스트는 `Assets/Editor/Tests/`에서 다음 범위로 먼저 좁힌다. 이름은 존재하는 검증 코드의 위치이며 최신 통과 결과를 뜻하지 않는다.

| 변경 영역 | 우선 확인할 테스트 클래스/파일 묶음 |
| --- | --- |
| 베이킹·아이템 생성·소유권 | `Phase0AuthoringPrefabContractTests`, `Phase1ItemIntegrationTests`, `Phase1ItemAuthoringPrefabTests` |
| 벨트 이동 | `Phase2BeltDecisionTests`, `Phase2BeltExecutionTests`, `Phase2BeltIntegrationTests` |
| 저장·입출고·공간·설정 | `Phase3Storage*`, `Phase3Building*`, `Phase3ItemConfigTests` |
| 월드·청크·자원·바닥 생성 | `Phase4WorldGenerationConfigTests`, `Phase4ChunkLifecycleTests`, `Phase4ResourceGenerationTests`, `Phase4ResourceAuthoringAndSpawnTests`, `Phase4FloorBiomeGenerationTests` |
| 채굴 통합 | `Phase4MinerComponentTests`, `Phase4MinerPipelineTests`, `Phase4EndToEndPipelineTests` |
| 제작·레시피 | `Phase5RecipeBlobTests`, `Phase5CrafterExecutionTests`, `Phase5RecipeChangePipelineTests`, `Phase5CrafterInputSlotTests`, `Phase5CrafterInputPipelineTests` |
| 분배·합류·목적지 예약 | `Phase6*` |
| 배치·현장·자재·건물 스폰 | `Phase7ConstructionContractTests`, `Phase7PlacementCommandTests`, `Phase7ConstructionMaterialTests`, `Phase7BuildingLifecycleTests`, `Phase7BuildingAuthoringPrefabTests` |

`TestSupport/EcsWorldTestFixture`가 독립 World를, `TestEntityFactory`가 테스트용 엔티티를, `TestSimulationDriver`가 시간·시스템 실행·ECB 경계를 제공한다. Factory 구성과 실제 Spawn/Baker 구성이 같은지 확인한다. 개별 시스템 직접 호출 테스트와 실제 정렬된 그룹 테스트, 테스트용 ECS 프리팹과 실제 SubScene 베이킹 결과를 구분한다.

`WorldInvariantValidationSystem`은 `UNITY_EDITOR || DEVELOPMENT_BUILD`에서만 동작하며 기본적으로 매 프레임 아이템·공간·요청 소비·벨트 간격·저장 버퍼·자원 정합성을 검사한다. 위반은 `Logs/InvariantErrors/`에 기록하고 `Debug.Break()`를 호출한다. 현재 검사 범위를 모든 건설/후속 도메인에 대한 완전한 보증으로 확대 해석하지 않는다.

## 상세 문서와 적용 범위

| 문서 | 용도와 주의 |
| --- | --- |
| [V2 코드 지도](<Docs/architecture v2 plan/CodeMemory/README.md>), [C# 파일 색인](<Docs/architecture v2 plan/CodeMemory/CSharpFileIndex.md>) | V2 탐색 보조. 과거 파일 수·미구현 설명·예약 범위 등이 일부 남아 있으므로 현재 소스로 확인한다. |
| [V2 계획](<Docs/architecture v2 plan/Architecture V2 Plan_0.2.md>), [구현 작업 목록](<Docs/architecture v2 plan/Architecture V2 Tasks.md>) | 설계 의도와 후속 작업. 체크 표시를 실행 증거나 현재 전체 구현으로 간주하지 않는다. |
| [Command/Event 규약](<Docs/architecture v2 plan/Architecture V2 Command Event Standard.md>) | 요청·결정·이벤트의 설계 배경. 현재 Producer/Consumer와 소비 시점을 함께 확인한다. |
| [Authoring/Prefab 규약](<Docs/architecture v2 plan/Architecture V2 Authoring Prefab Contract.md>) | 베이킹/스폰 책임 분리. 프리팹 누락 정책은 위 현재 코드의 도메인별 동작을 우선한다. |
| `Docs/CodeMemory/RuntimeBootstrap.md`, `WorldSpatialAndResources.md`, `BuildingLifecycle.md`, `ItemLogisticsAndProduction.md` | 주로 이전 구조의 상세 계약이다. `ChunkMapSystem`, `ItemStorageSystem` 등의 설명을 V2에 그대로 적용하지 않는다. |
| `Docs/CodeMemory/PowerGrid.md`, `DroneLogistics.md`, `ResearchSystem.md`, `UIAndPresentation.md` | 이전 도메인의 설계 배경. 현재 V2에 해당 시스템이 존재한다는 증거로 사용하지 않는다. |

이전 문서 전체를 이번 작업과 무관하게 동기화하지 않는다. 관련 영역을 실제로 변경할 때 해당 절의 갱신 필요성을 확인한다.

## 기획 불명확성 및 확인

- 기능 구현에 필요한 기획이 부족하거나 선택지에 따라 게임 동작 또는 설계가 달라지는 경우 임의로 결정하지 않는다.
- 구현 전에 현재 코드, 이 문서, 위 적용 범위에 맞는 V2 설계/코드 지도에서 의도와 기존 규칙을 확인한다. 이전 `Docs/CodeMemory/`는 해당 설계 배경이 필요할 때만 추가로 확인한다.
- 위 자료에서도 의도를 확인할 수 없으면 구현을 진행하지 말고, 동작이나 설계를 결정하는 선택지를 사용자에게 질문하여 확인받는다.
- 명확하게 정의되지 않은 게임 규칙이나 동작을 새로 추가하지 않는다.
- 기존 규칙 안에서 판단할 수 있는 일반적인 코드 구현 세부사항은 사용자에게 묻지 않고 진행하되, 기존 동작과 책임 경계를 유지한다.

## 변경 및 검증 시 주의

- Unity 버전은 `ProjectSettings/ProjectVersion.txt`, 패키지 버전은 `Packages/manifest.json`을 따른다.
- C# 또는 에셋을 변경한 경우에는 변경 범위에 맞는 Unity CLI 컴파일/재컴파일 확인을 수행한다. C# 변경은 크기와 무관하게 항상 확인한다. 읽기 전용 분석이나 문서만의 변경에는 새 컴파일을 실행하지 않는다. 비동기 명령은 제출만으로 완료로 간주하지 말고 최종 상태를 확인한다.
- 관련된 기존 EditMode 테스트가 있으면 전체 테스트보다 해당 테스트를 먼저 실행한다. 검증에서 문제가 발견되었을 때만 의존 시스템이나 더 넓은 테스트 범위로 확대한다.
- 단순한 코드 변경은 컴파일 확인으로 충분할 수 있다. 단순하지 않은 게임 로직, 라이프사이클·소유권 규칙, 계산 로직, 회귀 가능성이 높은 동작을 변경한 경우에만 관련 테스트를 추가하거나 수정한다.
- 실제 플레이 동작, UI·입력, 시각적 결과 등의 수동 작동 테스트는 사용자가 직접 수행한다. 명시적으로 요청받지 않은 경우 Play Mode 기반 작동 검증을 수행하지 않는다.
- 명시적으로 요청받지 않은 경우 변경 내용과 무관한 테스트 범위나 검증 절차를 확장하지 않는다. 검증 중 문제가 발견된 경우에만 추가적인 코드 분석과 검증을 진행한다.
- Unity CLI 명령 경로와 검증 순서는 아래 "Unity CLI 검증 절차"를 재사용한다.
- 관련 시스템을 수정하면 해당 영역 문서의 "함께 확인할 영역"과 `Assets/Editor/`의 연관 테스트를 먼저 확인한다.
- 컴파일/EditMode 테스트 성공은 Play Mode 입력, UI, 시각 결과, WebGL/브라우저 동작이나 persistence를 증명하지 않는다. 검증 보고에서 관련된 미수행 항목을 구분한다.
- 이 문서와 상세 문서는 현재 구조를 찾기 위한 지도다. 개별 필드와 분기 조건은 항상 실제 소스를 다시 확인한다.

### Unity CLI 검증 절차

Unity CLI 작업에는 설치된 스킬의 적용 지침을 따르되, 매 작업마다 전체 명령 목록이나 고급 참고 문서를 다시 출력하지 않는다. 아래 검증된 경로를 사용하고, 실행 파일 부재·버전/스키마 변경·명령 오류가 확인될 때만 해당 명령의 도움말이나 관련 참고 절을 조회한다.

```powershell
$unityCli = 'C:\Users\cyc07\AppData\Local\Unity\bin\unity.exe'
$planetMinerProject = 'C:\Projects\unity\PlanetMiner\planet miner'
```

일반 검증에서는 원본 상태 JSON을 반복 출력하지 않도록 `Tools/Codex/Verify-Unity.ps1`을 우선 사용한다. `-CompileOnly`는 컴파일만, `-TestFilter '<관련 테스트>'`는 컴파일 후 지정한 EditMode 테스트만 실행한다. 전체 EditMode 테스트는 사용자가 명시적으로 요청한 경우에만 `-AllEditModeTests`로 실행한다. 래퍼가 실행되지 않거나 결과 해석에 필요한 오류가 발생한 경우에만 아래 개별 명령 절차로 진단한다.

1. 코드·에셋 변경을 한 묶음으로 마친 뒤 검증한다. 대상 프로젝트가 열린 Editor에 연결되어 있으면 `command` 경로를 사용하며 standalone `unity test`를 실행하지 않는다. 연결이 없으면 그 상태를 확인하고 설치된 CLI의 해당 연결/오프라인 절차만 조회한다. Editor 설치·교체나 무조건적인 명령 재시도로 우회하지 않는다.
2. 아래 명령으로 재컴파일을 시작한 뒤 종료 상태를 확인한다. 명령들은 순차적으로 실행하며, 다음 테스트 명령은 컴파일 확인을 마친 뒤 실행한다.

   ```powershell
   & $unityCli command recompile --focus false --project-path $planetMinerProject --format json
   & $unityCli command recompile_status --project-path $planetMinerProject --format json
   ```

3. 상태 응답은 외부 JSON의 성공/오류부터 확인한다. `data.result`가 문자열이면 한 번 더 JSON으로 파싱하고, 객체이면 그대로 사용한다. null·파싱 실패·연결 오류는 완료로 간주하지 않는다. `completed` 또는 `up_to_date`라는 이름만으로 통과시키지 말고 `failed:false`, `errors:[]`, Editor가 준비되어 있고 컴파일/리로드 중이 아님을 확인한다. `up_to_date`이면 `Library/ScriptAssemblies/Assembly-CSharp.dll`이 최신 프로젝트 C# 소스보다 오래되지 않았는지 확인하고, 테스트 변경 시 테스트 어셈블리의 최신성도 확인한다.
4. 비동기 작업은 polling하되 상태 변경·새 오류·최종 결과만 출력하고 동일한 전체 응답을 반복 출력하지 않는다. 짧은 연속 polling 대신 간격을 늘리며 기다리고, 한 번의 대기는 60초 이내로 제한한다. 예상 시간을 넘기면 연결/Editor 상태와 필요한 로그만 진단한다. 재연결 중 일시 오류는 상태 조회를 복구하며, 작업 상태가 불명확하다는 이유만으로 컴파일이나 테스트를 중복 제출하지 않는다.
5. 컴파일 확인 후 관련 테스트 필터를 지정해 실행한다. `<관련 테스트 클래스 또는 필터>`는 실제 대상 이름으로 바꾼다.

   ```powershell
   & $unityCli command run_tests --mode editor --filter '<관련 테스트 클래스 또는 필터>' --async_tests true --project-path $planetMinerProject --format json
   & $unityCli command test_status --project-path $planetMinerProject --format json
   ```

6. 테스트도 최종 완료를 확인한다. 성공 시 필터와 Total/Passed/Failed/Skipped/Inconclusive 등 제공된 통계만 보고하고 전체 통과 목록·원본 로그를 다시 넣지 않는다. 실패 시 해당 테스트의 메시지와 필요한 스택을 확인한다. 수정 전 assertion이 실행되었다면 테스트 어셈블리 반영 여부부터 확인한다.
7. 추가 코드/에셋 변경, 검증 실패, 오래된 어셈블리 등 결과를 무효화하는 근거가 없다면 같은 컴파일·테스트를 반복하지 않는다. 새 변경이 있으면 그 변경 이후의 컴파일과 관련 테스트를 확인한다. 완료된 결과를 다시 보고하려는 목적만으로 재실행하거나 전체 테스트로 확대하지 않는다.
