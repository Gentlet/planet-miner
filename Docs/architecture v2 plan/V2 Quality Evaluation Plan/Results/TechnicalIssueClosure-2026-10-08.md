# 기술 검토에 따른 F-010·F-042·F-009·F-021·F-028 종료 기록

작업일: 2026-10-08. 최신 소스, 원본 평가, 기존 실행 기록을 대조했다. 이번 작업은 이 문서와 Task만 수정했으며 제품 코드·테스트·에셋·Config 변경, 새 컴파일·EditMode·Play Mode 실행은 없다. F-046의 다른 대화 변경은 이번 범위에 포함하지 않는다.

다섯 항목의 체크는 모두 이번 개선 추적의 종료를 뜻한다. F-010·F-042는 기존 결함의 원인 경로 제거 확인, F-009·F-021은 선택 검증으로 분리한 뒤 추적 종료, F-028은 현 구조 유지 결정이다. 새 코드 수정으로 다섯 결함을 해결했거나 다섯 실행 검증을 통과한 것으로 집계하지 않는다. 과거 결과의 통과 수와 원본 Q 문서는 그대로 보존한다.

## F-010 — 여러 자재 행 중 첫 행만 채우던 기존 결함 종료

예전에는 현장에 철 1개 요구가 두 행 있어도 첫 행만 찾아 수령했다. 첫 행이 채워지면 두 번째 행에 공급할 수 없어 모든 행을 요구하는 완공 검사가 계속 대기할 수 있었다. [Q05](Q05.md)의 당시 코드 분석이며 실행 재현 기록은 아니다.

현재는 다음 경로를 확인했다.

- [ConstructionSupplyReservationUtility](../../../../Assets/Scripts/Common/ConstructionSupplyReservationUtility.cs)의 Remaining은 같은 품목의 모든 RemainingToReserve를 합산하고, RemainingIncludingOwn은 전체 Required/Delivered/Reserved를 합산해 자기 예약을 제외한 수령 가능량을 계산한다. Reserve와 Release는 남은 수량이 있는 동안 같은 품목의 후속 행까지 순회한다.
- [DroneItemTransferUtility.RecordDelivered](../../../../Assets/Scripts/Common/DroneItemTransferUtility.cs)는 실제 인계 성공 수량을 각 행의 RemainingRequired만큼 나눠 반영하고 남은 수량을 후속 행에 계속 넣는다. 첫 품목 일치에서 멈추지 않는다.
- [DroneTaskLifecycleApplySystem.SupplySite/MoveCargo](../../../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskLifecycleApplySystem.cs)는 공통 Ownership API가 옮긴 실물 수만 도착량에 반영한다. [ConstructionLifecycleApplySystem](../../../../Assets/Scripts/Systems/Construction/StateApply/ConstructionLifecycleApplySystem.cs)은 모든 요구 행의 IsSatisfied를 검사한다. 예약만으로 완공하지 않고, 드론의 마지막 납품은 다음 Building 틱의 검사에 반영한다.

판정: 옛 첫 행 전용 수령은 [공사 운송 제거](ConstructionTransportRemoval-Verification.md) 때 제거됐고 현재 여러 행 소비 계약이 완공 검사와 맞는다. 기존 결함 추적을 종료한다. 중복 행 거부나 Loader 합산이라는 새 정책을 추가하지 않는다. F-014의 제작 레시피 중복 거부를 공사 자재에 적용하지 않는다.

한계: 중복 행을 가진 현장의 반복 공급·취소/거부·예약 정산·완공을 이번에 실행하지 않았다. 일반 공급·완공의 기존 실행도 이 조합의 통과로 확대하지 않는다. 큰 양수 행들의 int 합산 한계는 [현재 재검토 R-01](CurrentCodeReview-2026-10-06.md)의 별도 입력 범위 문제로 남긴다.

## F-042 — Stored 없는 현장의 옛 부분 수령과 assertion 추적 종료

[Q24](Q24.md)의 문제는 실물 보관 버퍼가 없는 현장에도 도착량·예약·Progress·Owner·렌더 변경을 먼저 승인하던 옛 수령 Job이었다. 정상 배치가 항상 그런 현장을 만든다는 근거는 없었다.

현재 소스 전체의 관련 C# 심볼을 확인해 ConstructionMaterialDelivery, SupplyConstructionMaterialRequest, CancelConstructionMaterialDeliveryRequest와 기존 Operations가 없고 Phase7ConstructionMaterialTests.cs도 제거됐음을 확인했다. ConstructionSite에는 Progress가 없다. 제거와 당시 실행은 [운송 제거](ConstructionTransportRemoval-Verification.md), [Progress 제거](ConstructionProgressRemoval-Verification.md)에 보존돼 있으며 새 드론의 실패 경로 실행 근거로 쓰지 않는다.

- [BuildingPlacementCommandSystem](../../../../Assets/Scripts/Systems/Command/BuildingPlacementCommandSystem.cs)은 정상 현장에 요구 버퍼와 StoredItemElement 버퍼를 함께 생성한다. 진단을 통과시키려고 Storage를 추가할 필요가 없다.
- [DroneItemTransferExecutionSystem.PrepareSupply](../../../../Assets/Scripts/Systems/Drones/Execution/DroneItemTransferExecutionSystem.cs)는 목적지가 유효 현장이고 Stored 버퍼를 가진 경우에만 실물 목록과 공급 상한을 준비한다. 버퍼가 없으면 0을 반환한다.
- 최종 [SupplySite](../../../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskLifecycleApplySystem.cs)도 목적지 Stored 존재를 MoveCargo/RecordDelivered 전에 재검사한다. 준비 후 버퍼가 없어져도 이 함수는 실물을 옮기거나 도착량을 올리지 않는다.
- 공통 [ItemOwnershipApplySystem.TryTransferItem](../../../../Assets/Scripts/Systems/Items/StateApply/ItemOwnershipApplySystem.cs)은 출발 참조·실제 Owner와 목적지 버퍼·슬롯·중복을 모두 확인한 뒤 출발 버퍼를 제거하고 목적지 등록·Owner·위치·렌더 처리를 한다. 목적지 버퍼 누락은 이 변경들 전에 false를 반환한다.

판정: 기존 부분 승인 원인과 그 전용 테스트가 제거됐고 새 실물 인계는 선행 검사한다. 기존 결함 및 삭제된 Progress/InvalidSite/운송별 예약 assertion 보강 추적을 종료한다. 새 계약의 행동 결과와 예약 정산은 [드론 계약](../../../CodeMemory/Components/DroneLogistics.md), [건설 명세](../../../Specifications/ConstructionAndDroneSupply.md)를 따른다.

거부 시 모든 배정/예약 상태가 그대로라는 뜻은 아니다. 유효 행동이 0개를 옮기면 현재 ApplyRequest/ApplyDeliveryState는 행동 번호·Unavailable 결과와 적재 유무에 따른 재목표화/완료 및 개별 예약 해제를 처리할 수 있다. 이를 옛 InvalidSite 결과·운송 예약 규칙으로 설명하지 않는다. 목적지 누락에 따른 실물/도착량 선행 거부는 코드 확인이고, 준비 전/후 누락과 모든 새 드론 수명 조합의 실행 검증은 미수행이다.

## F-009 — 설정 실패의 선택 실행 검증으로 분리하고 추적 종료

[Q04](Q04.md)는 실제 파일을 읽는 테스트를 순수 fallback 기본값 검증으로 설명하던 검증 공백이었다. 그 Test02/Test04/Test06은 이미 삭제됐고 현재 테스트는 명시 JSON 또는 정상 파일 연동을 검사한다. 삭제된 테스트를 복원할 제품 결함은 확인하지 않았다.

현재 [ItemConfigInitSystem](../../../../Assets/Scripts/Systems/Initialization/ItemConfigInitSystem.cs)은 파일을 읽거나 명시 JSON을 파싱한 뒤 게시한다. 파일 부재는 FileNotFoundException, 읽기/파싱 예외는 ReportFailure와 Entity.Null로 전달한다. 정상 Item 공통값/품목별 기본 규칙은 성공 입력의 규칙이며 파일 실패 대체와 구분한다. [RecipeConfigLoader.LoadFromResources](../../../../Assets/Scripts/Config/RecipeConfigLoader.cs)는 누락 파일을 거부하고 파싱·필수 ID 검사 뒤 반환한다. [RecipeInitSystem](../../../../Assets/Scripts/Systems/Initialization/RecipeInitSystem.cs)은 실패 시 Fatal을 게시하고 자동 Init을 종료한다. 파일 실패 뒤 CreateDefaultConfig를 호출하지 않는다.

[BuildingConfigLoader](../../../../Assets/Scripts/Config/BuildingConfigLoader.cs)와 [WorldGenerationConfigLoader](../../../../Assets/Scripts/Config/WorldGenerationConfigLoader.cs)는 누락·읽기·파싱 실패에서 미완성 out 데이터를 게시하지 않는다. 각각의 [건물 Init](../../../../Assets/Scripts/Systems/Initialization/BuildingConfigLoadSystem.cs), [월드 Init](../../../../Assets/Scripts/Systems/Initialization/WorldGenerationConfigLoadSystem.cs)은 실패를 [SimulationFailureUtility.RecordInitializationFailure](../../../../Assets/Scripts/Common/SimulationFailureUtility.cs)의 상세 로그·즉시 Fatal로 전달한다. 새 복구/재시도나 기존 기본값 계약 변경은 필요하지 않다.

기존 실행 근거는 [F-011 기록](F011-Verification.md)이다. 이번에 원본 [Item 2/2](../../../../Logs/Codex/F011-20261008/Phase3ItemConfigTests-complete-status.json), [Recipe 7/7](../../../../Logs/Codex/F011-20261008/Phase5RecipeConfigTests-status.json)의 completed·실패/생략/Inconclusive 0을 다시 읽었다. Item은 명시 JSON·자동 초기화, Recipe는 정상 연동/명시 JSON 및 F-014 중복·수량 오류의 범위다. 파일 부재/읽기/일반 JSON 문법 오류나 새 필수 누락의 독립 실행 증거로 확대하지 않는다. F-011 전체 79건에 추가하거나 새 재실행 수치로 기록하지 않는다.

판정: 실패 전달 구현은 F-011에서 끝났고 남은 범위는 선택 검증이다. 사용자 방침에 따라 이번 활성 개선 추적을 종료한다. 검증 공백이 모두 해소됐다는 의미는 아니다. 향후 선택 실행은 실제 Config를 삭제/편집하지 않는 격리 방식으로 파일 부재·읽기/파싱 실패·빈 목록/필수 누락을 확인하는 범위이며 새 fixture/assertion을 현재 필수 작업으로 남기지 않는다.

F-007 후속 주석(2026-10-08): 위 종료 재검토 시점의 정상 Item 공통값/품목별 기본값 규칙은 최신 사용자 선택과 F-007 구현으로 제거됐다. 현재 실제 모든 품목의 명시적 양수 MaxStack을 요구하고 무효 값·이름·None·중복·누락도 기존 실패 전달을 재사용한다. F-009의 선택 검증 종료는 유지하며 삭제된 기본값 테스트 복원이나 새 테스트를 필수로 두지 않는다. 위 Item 2/2·Recipe 7/7는 F-007 이후 입력/API의 새 실행 증거가 아니다. 이번에는 문서만 변경했으며 [관련 영향 정리](F007-Verification.md#2026-10-08-관련-이슈-후속-정리)를 따른다.

## F-021 — 실제 베이킹 근거를 반영하고 나머지 선택 검증 추적 종료

[Q08](Q08.md)의 기존 테스트는 실제 Baker 회귀를 검출하지 못하는 범위였다. 현재도 [Phase1ItemAuthoringPrefabTests](../../../../Assets/Editor/Tests/Phase1ItemAuthoringPrefabTests.cs)의 PopulateFromResources와 [Phase7BuildingAuthoringPrefabTests](../../../../Assets/Editor/Tests/Phase7BuildingAuthoringPrefabTests.cs)의 임시 Authoring 목록 수집은 실제 Bake가 아니다. [Phase4ResourceAuthoringAndSpawnTests](../../../../Assets/Editor/Tests/Phase4ResourceAuthoringAndSpawnTests.cs)는 수동 ECS 자원/DB를 준비한다. 이들과 F-011 프리팹 초기화 15/15를 실제 Baker 실행으로 바꾸어 설명하지 않는다.

코드로는 [ItemAuthoringBaker](../../../../Assets/Scripts/Authoring/ItemAuthoring.cs)가 Dynamic 엔티티에 ItemIdentity를 추가하고, [BuildingPrefabDatabaseBaker](../../../../Assets/Scripts/Authoring/BuildingPrefabDatabaseAuthoring.cs)가 실제 prefab 참조를 GetEntity로 변환해 별도 DB 버퍼에 등록하는 경로를 확인했다. 정적 입력과 런타임 초기화의 책임을 새로 구현하지 않았다.

실제 실행 근거는 [도메인 분리 기록의 일회 Play Mode 절](BuildingDroneDomainSplit-Verification.md)에 있다. V2 Test Scene와 자동 로드 sub.unity, 실제 Default Game World와 Player Loop에서 베이킹 DB의 창고·벨트·철 프리팹을 제품 경로로 이용했다. 수행자·용량·현장·경로/관측/행동 신호는 제어 입력이었다. 자동 드론 생성·비행의 증거는 아니다.

이번에 다음 원본 결과와 검증 입력 코드를 대조했다.

- [Play 시작](../../../../Logs/Codex/BuildingDroneDomainSplitPlayMode/play-started.json): playing=true, 실제 Default World(Game), PrefabDatabaseReady=1, Fatal=0 및 도메인 그룹 순서를 기록한다.
- [공급 틱](../../../../Logs/Codex/BuildingDroneDomainSplitPlayMode/supply-tick-result.json): Completed/1, Delivered=1, Reserved=0, cargo=0, 현장 생존과 실물 Owner=현장이다.
- [다음 Building 틱](../../../../Logs/Codex/BuildingDroneDomainSplitPlayMode/next-building-tick-completion.json): 베이킹된 완공 창고가 존재하고 현장/자재는 삭제됐으며 불변식 위반은 0이다.

판정: 실제 베이킹 결과를 전혀 이용하지 않았다는 현황 설명은 갱신한다. 확인한 정상 제품 경로 근거를 반영하고, 사용자 방침에 따라 남은 자동 회귀/입력 조합 검증을 선택 검증으로 분리해 이번 개선 추적을 종료한다. F-019의 기존 식별·필수 구성 구현이나 에셋 보완을 다시 수행하지 않는다.

한계: 모든 등록 타입의 구성·런타임 상태 미포함을 전수 검사하거나 잘못된 Authoring, key/식별 불일치, None/범위 밖 입력, 자동 수집 제외와 최종 DB 진단을 모두 실제 Bake로 실행한 기록은 아니다. SampleScene·Player 빌드·지속 자동 회귀는 미검증이다. 실제 V2 정상 경로 기록과 수동 ECS 오류 검사, 코드 확인을 구분하며 선택 공백 해소를 주장하지 않는다.

## F-028 — 추가 Reader 의존을 확인하고 현 구조 유지로 종료

공장은 같은 공간 맵을 여러 판단에서 읽는다. 읽기끼리는 맵을 바꾸지 않지만, 각 Reader가 자기 JobHandle을 Fence에 기록하기 위해 같은 Fence singleton을 RW로 사용한다. 이 메타데이터 선언 때문에 맵 읽기 외에 ECS 타입 의존이 추가되는 구조를 확인했다. [Q12](Q12.md)의 성능 위험을 실제 병목 발생으로 승격하지 않는다.

현재 제품 C#에서 네 인덱스와 Fence의 모든 참조를 찾아 실제 Map 접근과 단순 주석/공통 helper를 구분했다. Reader/Writer 범위는 다음과 같다.

- Belt Map: [벨트 이동 Decision](../../../../Assets/Scripts/Systems/Buildings/Decision/BeltMovementDecisionSystem.cs), [건물 입고 Decision](../../../../Assets/Scripts/Systems/Buildings/Decision/BuildingItemInputDecisionSystem.cs), [Storage 출고](../../../../Assets/Scripts/Systems/Buildings/Decision/StorageItemOutputDecisionSystem.cs), [Product 출고](../../../../Assets/Scripts/Systems/Buildings/Decision/ProductItemOutputDecisionSystem.cs), [Splitter](../../../../Assets/Scripts/Systems/Buildings/Decision/SplitterDecisionSystem.cs), [Merger](../../../../Assets/Scripts/Systems/Buildings/Decision/MergerDecisionSystem.cs), [목적지 Reservation](../../../../Assets/Scripts/Systems/Buildings/Reservation/BeltDestinationReservationSystem.cs), [벨트 Execution](../../../../Assets/Scripts/Systems/Buildings/Execution/BeltMovementExecutionSystem.cs), [저장 Apply의 출고 Job](../../../../Assets/Scripts/Systems/Buildings/StateApply/BuildingItemStorageApplySystem.cs)이 Reader다.
- Item Map: 위 벨트 이동 Decision, Storage/Product 출고, Splitter/Merger와 목적지 Reservation이 Reader다. Building Map의 Job Reader는 건물 입고 Decision, Resource Map의 Job Reader는 [Miner Decision](../../../../Assets/Scripts/Systems/Buildings/Decision/MinerDecisionSystem.cs)이다. [BeltEntryUtility](../../../../Assets/Scripts/Common/BeltEntryUtility.cs)는 호출자의 read-only 맵을 사용하는 계산이며 별도 Job/Owner가 아니다.
- [Placement Command](../../../../Assets/Scripts/Systems/Command/BuildingPlacementCommandSystem.cs)는 Building/Resource/Item 맵을 메인 스레드에서 읽기 전에 세 Fence의 마지막 Writer를 완료한다. 직접 조회 중 새 Job을 발행하지 않는다. [Validator](../../../../Assets/Scripts/Validation/WorldInvariantValidationSystem.cs)는 검사 때 네 Fence를 완료하고 맵을 읽는다. BuildingLifecycle의 ItemSpatialIndex 언급은 최신 ECS 원본을 직접 보는 이유의 주석이며 별도 Map Reader가 아니다. 드론은 이 맵들을 읽지 않는다.
- Writer/소유자는 각각 [BeltSpatialSyncSystem](../../../../Assets/Scripts/Systems/Synchronization/BeltSpatialSyncSystem.cs), [ItemSpatialSyncSystem](../../../../Assets/Scripts/Systems/Synchronization/ItemSpatialSyncSystem.cs), [BuildingSpatialSyncSystem](../../../../Assets/Scripts/Systems/Synchronization/BuildingSpatialSyncSystem.cs), [ResourceSpatialSyncSystem](../../../../Assets/Scripts/Systems/Synchronization/ResourceSpatialSyncSystem.cs)이다. Persistent 맵을 생성하고 빈 쿼리도 Clear하며, 기존 Writer+Reader 대기 → Clear → ECS 의존을 결합한 Populate → SetWriter를 수행한다. 용량 변경 및 OnDestroy의 Dispose 전에 Fence를 완료한다.

네 [Fence](../../../../Assets/Scripts/Components/Belts/BeltSpatialIndex.cs)의 공통 계약은 GetReaderDependency가 마지막 Writer만 반환하고, AddReader는 Reader 핸들을 합치며, GetWriterDependency/Complete는 마지막 Writer와 모든 등록 Reader를 기다리는 것이다. 추가 Reader 직렬화는 AddReader가 다음 Reader의 직접 선행으로 반환돼서 생기는 것이 아니다. 각 Reader가 RW Fence를 가진 상태로 state.Dependency와 맵 Writer 핸들을 합쳐 스케줄하고 최종 핸들을 state.Dependency에 게시하는 ECS 경로가 원인이다.

현재 PackageCache의 Entities 소스도 대조했다. SystemContextSystemModule의 GetSingletonRW 후보는 RO 플래그가 없고, SystemApiContextSyntaxWalker.CodeReplacements는 해당 타입의 RW query를 생성한다. SystemState.AddReaderWriters는 쿼리 타입을 등록하며 Dependency getter는 ComponentDependencyManager.GetDependency를 호출한다. 그 writerTypes 경로는 선행 WriteFence와 ReadFence를 결합한다. 검토 위치는 com.unity.entities@73a2af2e76de의 Unity.Entities/SystemState.cs:407,719, Unity.Entities/ComponentDependencyManager.cs:230과 Unity.Entities/SourceGenerators/Source~/SystemGenerator.SystemAPI의 해당 두 파일이다. 이 소스 계약 확인을 런타임 Job 타임라인 측정으로 표현하지 않는다.

독립 사례는 벨트 이동 Decision과 건물 입고 Decision이다. 둘은 Belt Map과 위치/이동/Owner를 RO로 읽고 각각 BeltMovementDecision과 BuildingItemInputDecision을 작성한다. 게임 데이터상 서로의 결정을 기다릴 이유가 없지만 공통 Belt Fence의 RW 선언이 추가 의존을 만든다. 먼저 갱신된 Reader의 핸들이 뒤 Reader의 ECS 의존에 포함되는 경로다. 반대로 Storage/Product 출고는 둘 다 BuildingItemOutputDecision을 쓰고 Splitter/Merger는 RoutingTransferDecision을 쓴다. Reservation도 그 결정을 RW로 소비하며 Execution/Apply는 실제 이동 상태·좌표·보관 버퍼를 쓴다. 이들의 타입/phase 의존을 전부 Fence 탓으로 돌리거나 제거할 수 있다고 판단하지 않는다. Resource Fence만 쓰는 Miner를 모든 Belt Reader와 같은 직렬화 사슬로 단정하지 않는다.

판정: 추가 의존은 확인됐지만 현재 변경할 실익을 입증한 부하/비교 측정은 없다. 안전한 현 구조를 유지하는 기술 판단으로 이번 개선 추적을 종료한다. 새 Fence owner·공간 캐시·병렬 구현을 만들거나 필요한 Writer 대기/Reader 등록을 제거하지 않는다. 병목이 있다/없다 또는 비용이 0이라고 결론 내리지 않는다.

남은 측정과 재검토 조건: 활성 물류의 대표 건물/아이템 규모에서 목표 틱 예산을 넘고, Job 타임라인에 독립 Reader의 Fence 의존이 주요 대기 원인으로 나타날 때 검토를 다시 연다. 기존 구현을 기준으로 같은 입력/부하의 비교에서 Reader 중첩·메인 스레드 대기·전체 틱 시간이 개선되는지, Writer/재할당/종료 안전성이 유지되는지 확인해야 한다. [기존 326개 유휴 표본](BuildingDroneDomainSplit-Verification.md)은 작은 장면의 그룹 업데이트 시간이며 Reader 병렬성 비교나 대규모 비용 근거가 아니다. 이번에는 새 측정·목표 성능 결정·최적화 구현을 수행하지 않았다.

## Task 반영과 남은 범위

[최신 Task](<../V2 Quality Improvement Tasks.md>)의 다섯 체크, 종료 이유·날짜·추천 설명·집계를 갱신한다. 완료 열에는 구현 완료와 이번 기술/선택 검증 추적 종료가 함께 있으므로 실행 통과 수로 해석하지 않는다. F-046을 포함한 다른 항목의 최신 상태는 그대로 보존하고 체크 상태로 집계한다. F-010의 산술 한계 R-01이나 다른 게임 정책 항목은 이번 종료로 해결된 것이 아니다.

## 직접 영향을 받은 Task의 후속 정리 (2026-10-08)

이번 후속도 문서만 수정했다. 위 다섯 종료 판단과 현재 소스가 직접 연결되는 여덟 항목을 확인했다. 기존 완료 일곱 건은 유지하고 F-025는 미완료를 유지한다. 추가 완료/종료는 0건이며 33/43 집계에 중복 합산하지 않는다. 원본 Q 및 과거 검증 문서는 수정하지 않았다.

- **F-003·F-001:** 옛 ConstructionMaterialApplyJob/운송·Progress·ProcessedInStateApply 설명과 남은 작업 안내를 당시 기록으로 구분했다. F-042·F-010 종료를 현재 공통 인계 검사와 여러 요구 행의 도착량 처리에 연결한다. 현재 취소는 Command/EndCommand, 인계와 정산은 Drone Lifecycle·Ownership/EndSimulation, 완공은 BuildingStateApply 마지막/EndBuilding이다. 기존 완료를 유지하며 옛 운송 Producer나 삭제된 assertion을 재구현할 작업으로 남기지 않는다. 실제 자동 비행·전체 실패/재배정·시각·성능의 검증 공백은 유지한다.
- **F-011:** F-009의 독립 파일 실패 실행을 선택 검증으로 분리한 종료를 연결한다. 로그·설정 미게시·즉시 Fatal·시작 차단 구현은 기존 완료이고 선택 검증 종료가 그 실패 실행을 통과시킨 것은 아니다. 79/79를 새 실행/추가 해결로 합산하지 않는다.
- **F-018·F-019:** 현재 [PrefabDatabaseInitializationSystem](../../../../Assets/Scripts/Systems/Initialization/PrefabDatabaseInitializationSystem.cs)의 유일 DB/버퍼, 중복·무효 등록 거부와 ItemIdentity 일치 검사를 재확인했다. 실제 V2 베이킹 정상 시작·생성/완공의 기존 기록을 F-021 종료와 연결한다. F-019의 'F-021이 계속 필수 추적' 안내는 당시 이관 기록으로 구분하고, 중복 자산·복수 DB·행 순서·Authoring 없음·key 불일치·None/범위 밖 값의 실제 Bake 전 조합은 선택 검증으로 남긴다. 기존 구현/에셋 보완의 완료를 유지하고 이 실패 조합이 모두 통과했다고 쓰지 않는다.
- **F-024:** 현재 모든 요구 행 충족 검사·Spawn Null이면 현장/자재 보존은 그대로다. 실제 V2 베이킹 창고의 공급 후 다음 Building 틱 완공·소비는 정상 완료의 기존 근거로 추가한다. F-010의 중복 행 전 조합이나 실제 베이킹 실패 주입·전체 ECB rollback 검증은 아니며 기존 실패 보존 정책과 완료를 유지한다.
- **F-026:** F-028은 추가 Job Reader 의존의 현 구조 유지 판단이다. Placement의 메인 스레드 Map 조회 전에 세 마지막 Writer를 완료하는 안전성 경계는 유지한다. 지연 Writer 강제 주입·Validator 없는 실행은 여전히 미검증이며 F-028 종료로 이를 통과 처리하거나 필요한 Writer 대기를 제거하지 않는다.
- **F-025:** EditorBuildSettings의 활성 빌드 진입은 현재 SampleScene이다. 기존 실제 실행은 V2 Test Scene/sub.unity이므로 SampleScene의 직렬화/DB 이관·Player 빌드 근거로 대체하지 않는다. 진입 장면 선택 및 해당 경로 이관이 남아 미완료를 유지한다.

F-037의 실제 베이킹 Crafter 제작, F-020의 실제 베이킹 철거 환급, F-017 및 R-01의 입력 산술 범위는 위 V2 정상 창고 기록/종료 판단으로 해결되지 않는다. 해당 코드를 수정하거나 새 완료로 집계하지 않았다.

후속 도중 새 AGENTS.md의 Serena 우선 탐색 규칙을 적용했다. initial_instructions를 한 번 읽고 절대 경로로 프로젝트를 활성화한 뒤, find_referencing_symbols(ItemOwnershipApplySystem/TryTransferItem)와 find_symbol(PrefabDatabaseInitializationSystem/ValidateItems)을 실제 성공 응답으로 확인했다. 제품 인계 호출자는 DroneTaskLifecycleApplySystem의 CollectStorage·RecoverWorldItem·StoreCargo·MoveCargo이며 별도 옛 공사 수령 호출자는 발견되지 않았다. 반환된 테스트 참조는 현재 코드 관계 근거이며 새 테스트 실행 결과가 아니다. 새 지침 이전의 focused 소스 확인과 이후 Serena 호출을 구분하며 onboarding·Serena 메모리 작성·소스 편집은 수행하지 않았다.

프로젝트 활성화 과정에서 Serena가 .serena/project.yml·project.local.yml·.gitignore 설정을 자동 생성했다. 이는 지시된 탐색 도구 설정이며 게임 코드/에셋 변경이나 별도 onboarding은 아니다. 문서의 후속 변경은 Task와 이 기록에 한정했다.
