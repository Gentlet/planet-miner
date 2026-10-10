# 현재 전체 제품 코드 검토와 미완료 이슈 재분류

> 2026-10-10 F-041 후속 안내: 아래 F-041 유지/미결정 판정은 2026-10-06의 원본 검토다. 현재는 설치 접수번호 승계·공통 좌표 동률 비교 구현과 Unity 컴파일·선별 EditMode 92/92로 완료했고, 관련 완료 이슈의 회귀 근거와 남은 F-032/F-022/F-025/F-006 경계를 정리했다. 추가 완료/종료는 0건이며 최신 Task는 38/43·미완료 5건이다. F-033 제외 상태도 유지한다. [현재 직접 영향과 한계](F041-Verification.md#2026-10-10-관련-이슈-후속-정리)를 따르며 이번 문서 후속에서 전체 코드 검토·성능·컴파일·테스트를 새로 실행하지 않았다.

검토일: 2026-10-06  
Git HEAD: `4207234e37256a980803b1e811b4d8f2555f3813`  
검토 대상: 현재 작업 폴더의 소스. 시작과 결과 정리 시 Git 작업 트리는 clean이었다.

> 2026-10-08 후속 상태 안내: F-014와 F-027은 이후 구현·검증을 완료했다. F-033은 현재 정상 실행에서 발생 경로를 확인하지 못한 조건부 방어 위험으로 사용자 요청에 따라 개선 이슈에서 제외했으며 코드 수정 완료로 집계하지 않는다. 아래 미완료 20개/개선 대상 14개 및 F-033 유지·개선 제안과 코드 근거는 2026-10-06 당시의 검토 기록으로 보존하며 현재 미완료 목록으로 사용하지 않는다. 최신 상태는 [품질 개선 Tasks](../V2%20Quality%20Improvement%20Tasks.md), 실행 근거는 [F-014 기록](F014-Verification.md)·[F-027 및 관련 이슈 영향 정리](F027-Verification.md)를 따른다. F-041의 배치 승인 순서 결정은 F-027에서 확정됐지만 Stamp 중복/후속 소비자 동률은 남는다. F-023은 제한된 비정사각형 배치 회귀 근거를 보강했고 F-026의 지연 Writer 실행 제한은 유지한다. 이번 후속 정리는 문서만 변경했으며 원래 전역 검토를 새로 실행한 것이 아니다.

> 2026-10-08 F-013 후속 안내: 아래 F-007/F-013 입력 검증 공백은 당시 코드 판정이다. 현재 두 이슈는 각각 명시적 양수 Item 설정과 정식 레시피 품목/필수 주생산품 검증을 구현하고 .NET 빌드를 확인했으나 Unity 검증 대기 상태여서 완료 체크를 올리지 않았다. F-011의 기존 실패 경로를 사용하며 F-015 ID와 F-006 실행 중 결과 보존은 별도로 남긴다. 이번 변경은 관련 문서 안내이며 원래 전역 검토·성능/실행 검증을 새로 수행하지 않았다. [F-013 직접 영향과 현재 한계](F013-Verification.md#2026-10-08-관련-이슈-후속-정리)를 따른다.

> 2026-10-09 F-015 후속 안내: F-015의 명시적 양수·유일 정의 번호 검증은 구현·컴파일 완료됐고, 같은 실제 Unity 컴파일/어셈블리 최신성으로 F-007/F-013의 컴파일 대기도 해소해 완료 처리했다. 아래 입력 검증 공백과 앞선 2026-10-08 대기 설명은 각 시점의 기록이다. 관련 기존 완료/선택 검증 종료와 원래 전역 검토·실행 통계는 유지하며 입력 반례·성능·실제 게임을 새로 실행한 것은 아니다. F-006의 실행 중 생산 결과 정책과 F-017 산술은 별도 남은 범위다. [F-015 영향·완료 근거](F015-Verification.md#2026-10-09-관련-이슈-후속-정리), [최신 Task](../V2%20Quality%20Improvement%20Tasks.md)를 따른다.

## 결론과 검토 범위

현재 제품 소스를 도메인 전체로 검토했다. 런타임 C# 154개(17,751줄), 에디터 도구 C# 2개(54줄)의 실행 구현·데이터 계약·생성/소비 관계를 확인했다. 테스트 C# 43개(13,036줄)는 전체 목록·fixture 구성·시스템 등록·사례 목적을 조사하고 관련 초기화/헬퍼/검사 구간을 선택적으로 읽었다. 테스트 43개 모든 메서드의 모든 assertion을 줄별로 재감사했다는 의미는 아니다. 사용자 요청의 초점인 현재 제품 구조와 미완료 문제의 유효성에 검토를 집중했다.

[파일 목록과 SHA256](CurrentCodeReview-2026-10-06-Inventory.csv)에 199개 C# 파일의 경로·분류·줄 수·해시를 기록했다. Unity 생성 코드, 패키지 내부 코드, 외부 라이브러리는 제품 소스 검토 대상에서 제외했다. AGENTS.md, 현재 건설 명세, 새 도메인 분리 검증 기록을 배경으로 사용했고 실제 현재 구현을 우선했다.

이번에 컴파일·EditMode·Play Mode·Player 빌드·성능 측정을 새로 실행하지 않았다. 제품 코드·테스트·Config·에셋·Task 완료 상태는 변경하지 않았다. 이 결과와 대상 파일 목록만 새로 작성했다. 결과는 코드 확인, 조건부 영향 추정, 기존 실행 기록, 실행 미확인을 구분한다. 소스를 읽었다는 사실을 모든 런타임 경우의 안전성 보장으로 사용하지 않는다.

문서상 미완료 20개는 다음과 같이 재분류한다.

- 현재 개선 대상: 14개 — F-007, F-011, F-013, F-014, F-015, F-016, F-017, F-022, F-025, F-027, F-032, F-033, F-041, F-046.
- 기존 결함의 구현 경로가 해소되거나 제거됨: 2개 — F-010, F-042. 기존 실행 재현/새 구조 전체 검증 통과를 주장하는 완료 판정은 아니다.
- 테스트/검증 보강 선택 작업: 2개 — F-009, F-021.
- 유지/변경 정책 또는 측정 이후 결정: 2개 — F-006, F-028.

새 입력 범위 검토 후보 2건도 발견했다. 아래 R-01/R-02는 이번 검토용 식별자이며 기존 F 번호를 새로 발급하거나 자동 구현을 승인하지 않는다.

## 현재 구조 전체의 연결 확인

### 실행 경계

실제 그룹 선언은 다음 순서를 구성한다.

```text
Initialization
Command → EndCommand
BuildingSimulation: Decision → Reservation → Execution → StateApply → EndBuilding
DroneSimulation: Decision → Reservation → Execution → StateApply
SimulationCommit: EndSimulation
Synchronization
```

GameSimulationGroup은 틱 시작에 Ready/Fatal을 검사한다. 중간 오류에서 현재 틱 전체를 되돌리는 기능은 없다. EndBuilding은 BuildingSimulationGroup의 OrderLast 직접 자식이다. ConstructionLifecycleApplySystem은 BuildingStateApplyGroup의 OrderLast이고 SynchronizationGroup은 최상위 OrderLast다. 건물 확정 상태를 드론이 같은 틱에 읽되, 드론의 납품으로 완공하는 판단은 다음 틱에 진행하는 구조다. 그룹 순서와 Job/Fence 완료는 서로 다른 계약으로 확인했다.

근거: [GameSimulationGroup.cs:22](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Phases/GameSimulationGroup.cs:22>), [BuildingSimulationGroup.cs:7](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Phases/Buildings/BuildingSimulationGroup.cs:7>), [DroneSimulationGroup.cs:7](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Phases/Drones/DroneSimulationGroup.cs:7>), [EndBuildingEntityCommandBufferSystem.cs:9](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Systems/Buildings/StateApply/EndBuildingEntityCommandBufferSystem.cs:9>), [SynchronizationGroup.cs:9](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Phases/SynchronizationGroup.cs:9>).

### 건물·물류·생산

Command에서 철거를 승인하고 PendingBuildingDemolition을 게시한다. 보관 실물의 이전 Transfer를 비활성화하고 생산 결과를 비운 뒤 Decision에서 입고·출고·채굴·제작·라우팅 후보 생성을 차단한다. 과거 F-004의 같은 틱 입고 후 반환 계약은 현재 정책으로 소급 적용하지 않는다.

일반 입출고는 Storage Apply의 버퍼/위치 변경과 Ownership Apply의 Owner/렌더 적용을 사용한다. 드론 실물 인계는 같은 Ownership 소유자의 공통 API를 이용한다. API는 이전 보관 버퍼의 정확한 실물 참조와 현재 Owner, 목적지 중복을 확인한 뒤 함께 변경한다. 따라서 새 드론 인계의 방어가 일반 출고의 F-033까지 해결했다는 해석은 하지 않는다.

Crafter 입력 슬롯 계산은 재료를 품목별로 합산한다. 그러나 실제 제작 Decision/Execution의 각 재료 행 처리에는 그 합산이 적용되지 않아 F-014가 남는다. 생산 결과 생성 실패 후 Clear 정책인 F-006도 유지된다.

벨트 속도 상한과 일반 T형 직접 합류 제외는 기존 사용자 선택을 유지한다. 공통 목적지 예약은 건물 출고·라우팅만 중재한다. 일반 벨트 이동 통합 예약을 새 해결책으로 추가할 근거로 사용하지 않는다.

### 공사·드론

기존 공사 운송/공급 요청·등록/수령·Progress 경로는 제거됐다. 취소는 Command, 완공은 BuildingStateApply 마지막, 납품과 예약 정산은 Drone Lifecycle이 담당한다.

드론 Decision은 생성/무효화/경로 의도와 후보를 작성한다. Reservation은 순수 후보와 공개 대기 기록을 분리하여 현장 예약량을 확보한다. Execution은 작업/경로 명령과 실물·목적지 공간 계획을 준비한다. Lifecycle은 준비된 실물만 현재 자격으로 다시 검사하고 실제 성공량을 정산한다. Publish는 최종 자격을 확인하며 공개 실패분의 예약을 회수한다. 공개는 EndSimulation이고 다음 틱부터 새 배정을 이용한다.

동일 실물의 중복 행동은 작업 revision·행동 sequence·접수 순서·현재 Owner·이전 버퍼 검사를 함께 사용한다. 목적지 공간은 준비된 예산을 요청들이 공유하며 실제 성공분만 소비한다. 안전 방출은 현장 회전 footprint 밖을 요구하고, 막혔으면 실물을 보존한 채 다시 목적지를 찾는다.

근거: [ConstructionSupplyReservationSystem.cs:32](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Systems/Drones/Reservation/ConstructionSupplyReservationSystem.cs:32>), [DroneItemTransferExecutionSystem.cs:72](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Systems/Drones/Execution/DroneItemTransferExecutionSystem.cs:72>), [DroneTaskLifecycleApplySystem.cs:212](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Systems/Drones/StateApply/DroneTaskLifecycleApplySystem.cs:212>), [DroneTaskAssignmentPublishSystem.cs:25](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Systems/Drones/StateApply/DroneTaskAssignmentPublishSystem.cs:25>), [ItemOwnershipApplySystem.cs:95](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Systems/Items/StateApply/ItemOwnershipApplySystem.cs:95>).

현재 자동 드론 기능의 연결은 아직 완성되지 않았다. 수행자 생성/등록·공통 적재량 초기화·이동·경로 평가·관측·행동 신호 Producer는 후속이다. 용량이 없으면 ReadCarryingCapacity가 0을 반환해 신규 배정이 생기지 않는다. 이 미구현 범위를 새 공급 계약의 오류나 자동 운송 완료로 잘못 기록하지 않는다.

### 초기화·Authoring·공간·진단

Item/Recipe는 ECS 버퍼로 시작 시 한 번 게시하고 런타임 교체를 거부한다. 따라서 과거 Blob 소유권 문제 F-008을 재작업할 필요는 없다. 최초 입력값 검증 F-007/F-013/F-015와는 별개다.

프리팹 준비 검사는 요청된 SubScene 로딩 이후 세 DB의 유일성·필수 등록·Prefab/LocalTransform·ItemIdentity를 확인하고 실패 시 시작을 차단한다. 실제 Baker 산출물 검증은 수동 ECS 테스트와 구분한다. 건물 생성은 요청→Config→기본 크기를 선택하며 DB 크기를 읽고도 사용하지 않는 F-022가 남는다.

네 공간 인덱스는 Synchronization에서 ECS 원본으로 재구축하며 Fence가 Reader/Writer 수명을 관리한다. 드론은 이전 공간 인덱스를 당장 최신이라고 가정하지 않고 직접 원본 상태를 검사한다. Fence 메타데이터의 RW 접근과 병렬성 평가 F-028은 남지만 잘못된 게임 결과가 확인된 항목은 아니다.

개발용 Validator는 소유 버퍼 유일성·Owner·품목·공간·벨트 간격·요청 소비를 검사한다. 새 드론 작업/예약/행동 상태 전체를 검증하는 완전한 검사기는 아니다. 위반 파일의 세션 ID·순번·CreateNew는 유지되지만 F-046 테스트의 공용 삭제가 이를 지울 수 있다.

## 미완료 이슈별 판정

### 아직 개선 대상인 14개

- **F-007 — 유지.** Item 설정은 사용자 MaxStack을 양수 검사 없이 게시한다. 일반 창고는 빈 슬롯을 선택할 때 MaxStack을 별도로 거부하지 않고, 드론 보관은 0 이하를 거부한다. 설정이 ECS 버퍼로 바뀌었어도 이 의미 차이는 남는다. [ItemConfigInitSystem.cs:111](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Systems/Initialization/ItemConfigInitSystem.cs:111>), [BuildingStorageInputReservationSystem.cs:214](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Systems/Buildings/Reservation/BuildingStorageInputReservationSystem.cs:214>), [DroneSchedulingUtility.cs:229](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Common/DroneSchedulingUtility.cs:229>).
- **F-011 — 유지.** 건물 설정 실패는 미게시/오류 기록뿐이고 배치는 무설정 overload를 사용하여 자재 요구 행 없이 현장을 만들 수 있다. DB Ready는 건물 Config 준비를 보장하지 않는다. [BuildingConfigLoadSystem.cs:53](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Systems/Initialization/BuildingConfigLoadSystem.cs:53>), [BuildingPlacementCommandSystem.cs:103](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Systems/Command/BuildingPlacementCommandSystem.cs:103>).
- **F-013 — 유지.** 재료·주생산품·부산물의 Enum.TryParse 성공을 검사하지 않는다. 입력 슬롯의 재료 방어는 주생산품/부산물 검증을 대체하지 못한다. [RecipeConfigLoader.cs:117](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Config/RecipeConfigLoader.cs:117>).
- **F-014 — 유지, 먼저 처리할 후보.** Decision은 각 재료 행을 같은 전체 재고와 독립 비교하고 Execution은 부분 소비 후에도 제작을 시작한다. 예를 들어 같은 재료 요구가 [2,3]이고 재고가 3이면 각 검사에 통과할 수 있으나 총 비용 5는 소비할 수 없다. 이 예시는 코드로 구성한 반례이며 이번에 Unity로 재현하지 않았다. [CrafterDecisionSystem.cs:297](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Systems/Buildings/Decision/CrafterDecisionSystem.cs:297>), [CrafterExecutionSystem.cs:131](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Systems/Buildings/Execution/CrafterExecutionSystem.cs:131>).
- **F-015 — 유지.** ID를 그대로 저장하며 Publish는 버퍼 범위만 검사한다. ID 조회는 첫 일치를 반환한다. 최초 게시의 무효/중복 문제를 런타임 재게시 금지로 해결했다고 보지 않는다. [RecipeConfigLoader.cs:104](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Config/RecipeConfigLoader.cs:104>), [RecipeConfigLoader.cs:230](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Config/RecipeConfigLoader.cs:230>), [RecipeConfigLookupUtility.cs:10](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Common/RecipeConfigLookupUtility.cs:10>).
- **F-016 — 유지.** 자원 전용 Publish는 floor=null로 게시하고 Init는 ResourceGenerationSettings 존재만으로 멈춘다. production 전체 게시 경로와 외부 부분 게시 API 문제를 구분한다. [WorldGenerationConfigLoader.cs:399](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Config/WorldGenerationConfigLoader.cs:399>), [WorldGenerationConfigLoadSystem.cs:24](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Systems/Initialization/WorldGenerationConfigLoadSystem.cs:24>).
- **F-017 — 유지.** 반경/초기 크기/전이 폭에 산술과 실행량을 고려한 상한이 없고, 반경의 max+1·제곱과 전이 폭×청크 크기의 int 연산이 남는다. 정상 현재 설정에서 실패한 실행 결과는 아니다. [WorldGenerationConfigLoader.cs:232](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Config/WorldGenerationConfigLoader.cs:232>), [ResourceGenerationUtility.cs:157](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Chunks/ResourceGenerationUtility.cs:157>), [FloorBiomeSampler.cs:96](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Chunks/FloorBiomeSampler.cs:96>).
- **F-022 — 유지.** 공통 Spawn은 요청/Config/기본 크기를 선택하고 DB의 dbFootprint는 읽기만 한다. 기본 크기/회전 표현 수정 F-023과 다른 문제다. [BuildingLifecycleUtility.cs:33](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Common/BuildingLifecycleUtility.cs:33>), [BuildingLifecycleUtility.cs:61](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Common/BuildingLifecycleUtility.cs:61>).
- **F-025 — 유지.** 활성 빌드 장면은 SampleScene이다. 하위 SampleScene/Sub.unity의 세 DB GUID는 현재 Authoring .cs.meta와 다르다. V2 장면의 기존 제어 입력 Play Mode 통과는 SampleScene 수리 증거가 아니다. [EditorBuildSettings.asset:8](<C:/Projects/unity/PlanetMiner/planet miner/ProjectSettings/EditorBuildSettings.asset:8>), [Sub.unity:269](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scenes/SampleScene/Sub.unity:269>), [Sub.unity:322](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scenes/SampleScene/Sub.unity:322>), [Sub.unity:386](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scenes/SampleScene/Sub.unity:386>).
- **F-027 — 유지, 먼저 처리할 후보.** Command가 요청별로 같은 이전 공간 맵을 검사하며 claimedCells는 각 ValidateBatchPlacement 호출 안에서 새로 만들어진다. EndCommand 이전 별도 요청의 승인 점유를 공유하지 않는다. [BuildingPlacementCommandSystem.cs:71](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Systems/Command/BuildingPlacementCommandSystem.cs:71>), [BuildingPlacementValidationUtility.cs:300](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Common/BuildingPlacementValidationUtility.cs:300>).
- **F-032 — 유지.** 건물 입고는 이동 enable을 무시하는 쿼리를 사용하고 Job에서 별도로 활성 여부를 검사하지 않는다. 두 Router의 원본 선택도 Progress만 확인한다. 새 드론의 현재 Owner 검사가 이 기존 물류 선택을 바꾸지는 않았다. [BuildingItemInputDecisionSystem.cs:32](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Systems/Buildings/Decision/BuildingItemInputDecisionSystem.cs:32>), [SplitterDecisionSystem.cs:265](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Systems/Buildings/Decision/SplitterDecisionSystem.cs:265>), [MergerDecisionSystem.cs:266](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Systems/Buildings/Decision/MergerDecisionSystem.cs:266>).
- **F-033 — 유지, 먼저 처리할 후보.** 일반 출고는 removeIndex=-1이어도 아래 위치/이동/Transfer 갱신으로 내려간다. 새 드론의 공통 인계 API는 sourceIndex<0을 거부하지만 일반 출고가 그 API를 쓰는 것은 아니다. 정상 정렬 흐름에서 이 무효 승인을 실제로 만드는 경로는 이번 실행으로 재현하지 않았다. [BuildingItemStorageApplySystem.cs:225](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Systems/Buildings/StateApply/BuildingItemStorageApplySystem.cs:225>), [BuildingItemStorageApplySystem.cs:260](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Systems/Buildings/StateApply/BuildingItemStorageApplySystem.cs:260>).
- **F-041 — 유지, 새 드론 소비자 영향 추가.** 요청별 후보 인덱스로 같은 tick/Order가 발급된다. 벨트 예약은 Entity.Index, Router는 방향 탐색의 첫 후보, 드론의 ComparePlacement는 Stamp 동률이면 0으로 처리한다. 드론 공급 대표/공급원 동률 선택까지 공개 동률 계약의 영향 범위에 포함해야 한다. [BuildingPlacementCommandSystem.cs:158](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Systems/Command/BuildingPlacementCommandSystem.cs:158>), [BeltDestinationReservationSystem.cs:350](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Systems/Buildings/Reservation/BeltDestinationReservationSystem.cs:350>), [DroneSchedulingUtility.cs:413](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Common/DroneSchedulingUtility.cs:413>).
- **F-046 — 유지.** Phase2BeltIntegrationTests의 SetUp/TearDown은 여전히 공용 invariant_error_*.txt 전체를 삭제한다. 테스트 보강과 별개로 기존 로그를 보호하는 수정이다. 이번에 해당 테스트를 실행하거나 파일을 삭제하지 않았다. [Phase2BeltIntegrationTests.cs:46](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Editor/Tests/Phase2BeltIntegrationTests.cs:46>).

### 기존 결함의 경로가 해소·제거된 2개

- **F-010 — 기존 완료 대기 결함 종료 후보.** 새 ReservationUtility는 같은 품목의 모든 요구 행을 합산/순회하고 Reserve/Release를 여러 행에 분배한다. RecordDelivered도 첫 행에서 멈추지 않고 남은 수량을 후속 행에 반영한다. 완료는 모든 행의 IsSatisfied를 확인한다. 따라서 옛 수령의 첫 행만 채우는 원인을 그대로 적용할 수 없다. 중복 행 합산의 극단적 int overflow는 별도 입력 범위 문제이며 이 결함의 재현 증거로 혼합하지 않는다. [ConstructionSupplyReservationUtility.cs:12](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Common/ConstructionSupplyReservationUtility.cs:12>), [DroneItemTransferUtility.cs:47](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Common/DroneItemTransferUtility.cs:47>), [ConstructionLifecycleApplySystem.cs:195](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Systems/Construction/StateApply/ConstructionLifecycleApplySystem.cs:195>).
- **F-042 — 옛 구현 대상 제거/새 사전 검사로 대체.** 기존 운송·Supply 요청·수령 Job·Progress가 없다. 새 PrepareSupply와 최종 SupplySite는 현장의 Stored 버퍼가 없으면 MoveCargo/도착량 변경 전에 거부하고, 공통 인계 API도 목적지 버퍼를 확인한다. 옛 InvalidSite/운송별 예약 assertion 목록은 현재 API로 복원할 대상이 아니다. 새 공급의 전체 잘못된 현장 수명·재배정 경우를 모두 실행 증명했다는 의미는 아니다. [DroneItemTransferExecutionSystem.cs:156](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Systems/Drones/Execution/DroneItemTransferExecutionSystem.cs:156>), [DroneTaskLifecycleApplySystem.cs:336](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Systems/Drones/StateApply/DroneTaskLifecycleApplySystem.cs:336>), [ItemOwnershipApplySystem.cs:113](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Systems/Items/StateApply/ItemOwnershipApplySystem.cs:113>).

### 선택 검증 또는 보류 항목 4개

- **F-009 — 필수 제품 수정에서 검증 선택 작업으로 분리.** 현재 ItemConfig 테스트에는 명시 JSON과 정상 자동 초기화가 있으나 파일 부재/읽기 실패를 독립 주입하는 증거는 부족하다. 잘못된 fallback 동작을 실행 재현한 결함은 아니다. 사용자 테스트 방침상 새 테스트를 의무화하지 않고 미확인 범위 기록으로 관리할 수 있다.
- **F-021 — 기존 설명의 검증 범위 갱신 필요.** 기존 Authoring 테스트는 수동 ECS/Resources 수집 중심이다. 하지만 최신 도메인 검증 기록에는 V2 장면의 실제 베이킹 DB를 쓴 제어 입력 Play Mode 실행 근거가 생겼다. '실제 Baker/베이킹 결과를 전혀 확인하지 않았다'는 표현은 현재 근거에 맞지 않는다. 전체 등록 타입·SampleScene·Player 빌드·지속 회귀 자동화를 보증한 것은 아니다. 새 자동 테스트는 별도 선택이다.
- **F-028 — 성능 측정/정책 이후 결정.** Map Reader가 Fence를 GetSingletonRW로 갱신하는 구조는 남는다. 도메인 분리로 불필요한 Reader 직렬화가 없어졌다고 보지 않는다. 다만 그 비용이 실제 문제인지와 변경 이익은 이번에 측정하지 않았다. 기존 유휴 Play Mode 샘플을 Reader 병렬성 비교 실험으로 해석하지 않는다.
- **F-006 — 중단된 월드의 결과 보존 정책 선택.** 프리팹 누락 시 오류를 기록하고 ProductResult는 Clear한다. 정상 DB 검증/불변 계약으로 통상 누락은 차단하지만 예상 밖 오류의 결과 보존은 별개다. 새 도메인에서도 틱 시작 차단·현재 틱 완주는 사용자 승인 계약이다. 중단된 월드를 계속 쓰지 않으면 현재 소비를 문서화할 수 있고, 보존을 원하면 성공분/미생성분 경계를 설계해야 한다. 자동 복구/보상을 이미 요청받은 기능으로 추가하지 않는다.

## 추가 발견 후보

### R-01 — 건물 크기와 새 현장 자재 합산의 산술 한계

코드 확인: 건물 설정의 Footprint는 양수와 2개 차원만 검사한다. BuildingSpatialSyncSystem은 size.x*size.y와 requiredCapacity*2를 int로 계산한다. 예를 들어 양수 50,000×50,000은 int 양수 범위를 초과한다. 배치/등록 루프의 실행량 제한도 없다. 새 ConstructionSupplyReservationUtility는 같은 품목의 Required/Delivered/Reserved를 int로 합산하므로 여러 개의 큰 정상 양수 행을 입력할 때도 합산 한계가 생긴다.

현재 제공 설정에서 발생한 오류나 새로 실행한 재현은 아니다. F-017의 월드 설정 범위와 비슷하지만 건물 footprint와 새 드론 자재 합산이라는 직접 영향 영역이 다르다. 지원할 입력 범위를 정하고 계산/상한을 한 경계에서 맞추는 검토가 필요하다. 단순히 모든 곳에 임의 숫자를 복제하거나 큰 맵을 생성해 확인할 필요는 없다.

근거: [BuildingConfigLoader.cs:155](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Config/BuildingConfigLoader.cs:155>), [BuildingSpatialSyncSystem.cs:81](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Systems/Synchronization/BuildingSpatialSyncSystem.cs:81>), [ConstructionSupplyReservationUtility.cs:46](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Common/ConstructionSupplyReservationUtility.cs:46>).

### R-02 — 벨트 이외 생산 속도·제작 시간의 비유한 값 검증

코드 확인: BuildingConfigLoader는 벨트 속도에만 isfinite를 적용한다. Miner/Crafter 속도에는 음수 검사만 있으며 +Infinity를 공통 생성에서 그대로 사용할 수 있다. RecipeConfigLoader도 craftTime>0 비교와 Publish의 버퍼 범위 검사만으로 비유한 제작 시간을 막지 않는다.

+Infinity 속도/시간이 설정 모델이나 공개 게시 API에 들어오면 진행도가 즉시 상한에 도달하거나 증가하지 않는 등 정상 유한 값과 다른 동작을 만들 수 있다. 실제 JsonUtility가 특정 JSON 숫자를 어떻게 파싱하는지는 이번에 실행하지 않았고, 현재 제공 JSON에서 오류를 관측한 것도 아니다. 최초 설정 경계에서 유한·양수/허용 범위를 정리할 후보이며 F-013 입력 검증과 함께 검토할 수 있다.

근거: [BuildingConfigLoader.cs:136](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Config/BuildingConfigLoader.cs:136>), [BuildingLifecycleUtility.cs:109](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Common/BuildingLifecycleUtility.cs:109>), [RecipeConfigLoader.cs:105](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Config/RecipeConfigLoader.cs:105>), [CrafterExecutionSystem.cs:162](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Systems/Buildings/Execution/CrafterExecutionSystem.cs:162>).

## 의존성·유지보수 평가

현재 건물/드론 분리는 공개되는 시점과 상태 소유자를 더 분명하게 만들었다. 드론이 자체 Owner Writer나 별도 실물 캐시를 만들지 않고 기존 Ownership API를 사용하고, 실제 성공량으로 예약/도착량을 정산하는 연결은 V2 의도에 부합한다.

여전히 검토할 부분은 다음과 같다.

- F-041의 공통 설치 순서 계약은 새 드론까지 확장해 맞춰야 한다.
- 공통 인계 API의 방어와 일반 물류의 방어 수준이 달라 F-033이 남는다. 기존 API의 책임을 유지하면서 통일할 수 있는지 먼저 확인한다.
- 배치 검증 3개 overload의 충돌 처리와 저장/생산물 출고 둘의 외곽 벨트 탐색이 복제돼 있다. 현재 동작 차이를 새로 발견한 것은 아니며 유지보수 관찰이다. 이번에 무조건 공통화하거나 새 시스템으로 바꾸지 않는다.
- DroneSchedulingUtility와 DroneTaskLifecycleApplySystem은 여러 계약을 연결한다. 공개 계약을 확인하는 관계와 타 시스템 내부 상태에 대한 결합을 구분해야 한다. 외부 이동/경로/행동 Producer를 연결할 때 내부 메서드를 복제하는 대신 현재 요청·결과 계약을 이용한다.
- Map Fence와 개발용 Validator가 만드는 동기화 비용, 드론 후보/경로의 여러 반복 탐색 비용은 실제 규모에서 측정하기 전 병목으로 단정하지 않는다.
- 사용자가 명시하지 않은 시스템 partial 파일 분리나 새 공간 캐시 추가는 권하지 않는다.

## 기존 검증 근거와 이번 한계

[BuildingDroneDomainSplit-Verification.md](BuildingDroneDomainSplit-Verification.md)는 새 구조에 대해 당시 선택 EditMode 181/181 및 실제 V2 장면에서 제어 입력 Play Mode 기록을 제공한다. 자연 실행에는 드론 용량·수행자가 없었으며 외부 경로·관측·행동 신호를 직접 제공했다. V2 베이킹 DB·Player Loop에서의 인계/완공 일부 사례와 자동 비행/자연 플레이를 구분한다. 기존 네 통합 사례나 이전 142개 실행 기록을 새 그룹의 통과 수로 합산하지 않았다.

이번 검토는 읽기 전용 분석이다. 이 기존 실행 수치를 이번에 재실행한 결과로 표시하지 않는다. 성능·실제 드론 이동·브라우저·UI·입력·저장/복원·Player 빌드와 모든 오류 주입은 이번 범위에서 실행 확인하지 않았다. 미구현 기능을 결함이나 완료로 바꾸지 않는다.

현재 Task 체크박스를 일괄 닫거나 이슈를 삭제하지 않았다. F-010/F-042는 현재 근거로 종료/재분류할 후보이고 F-009/F-021/F-028/F-006은 정책에 따라 선택 작업 또는 보류로 관리할 수 있다. 나머지 14개와 새 R-01/R-02 후보는 현재 소스 근거를 전달한 뒤 필요한 정책을 정해 작은 단위로 개선한다.
