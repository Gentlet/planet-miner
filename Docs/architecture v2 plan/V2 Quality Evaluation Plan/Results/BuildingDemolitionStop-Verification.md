# 철거 승인 상태와 앞단 동작 중단 검증 기록

## 결과와 확정된 정책

2026-10-04 사용자 승인에 따라 철거 요청과 승인 상태를 분리했다. DemolishBuildingRequest 이름은 유지하고 현장 취소 요청과의 분리도 유지한다. Command에서 철거가 승인되면 EndCommand에 건물 상태를 게시하고 요청을 삭제하며, 해당 틱의 후속 입고·생산·출고·벨트 이동/진입·분배/합류를 앞단에서 중단한다. 실제 반환·환급·철거는 StateApply/EndStateApply에 유지한다.

- Unity 재컴파일: completed, failed:false, 오류 0.
- 관련 기존 EditMode 7개 필터: **63/63 통과**, Failed/Skipped/Inconclusive 0.
- Item Lifecycle/Ownership의 철거 요청·상태 조회 및 요청 목록 복사를 제거했다. 실제 시스템에서 DemolishBuildingRequest를 읽는 곳은 Command뿐이다.
- 같은 틱 재배치는 기존 공간 인덱스 점유로 거부한다. 공간 인덱스와 Fence의 소유권·갱신 시점, 일반 벨트 예약 정책은 변경하지 않았다.
- 기존 실물 반환, 건축 비용 환급, 활성 Destroy 제외와 실패 계약을 유지했다. 이전 틱에 이미 소비한 제작 재료·광물의 보상은 추가하지 않았다.
- 최종 Editor ready, compiling:false, domainReloadInProgress:false, Play Mode stopped. 런타임·Editor 어셈블리 모두 수정 소스보다 최신이다.

## 변경과 소유권 경계

| 영역 | 변경 |
| --- | --- |
| [BuildingComponents.cs](../../../../Assets/Scripts/Components/Buildings/BuildingComponents.cs) | 승인된 완공 건물에 부착하는 일반 상태 태그 PendingBuildingDemolition 추가. 프로젝트 ECS 타입은 79→80개다. |
| [BuildingDemolitionCommandSystem.cs](../../../../Assets/Scripts/Systems/1_Command/BuildingDemolitionCommandSystem.cs) | 검증·중복 제거 후 태그 부착과 모든 요청 삭제를 EndCommand에 기록한다. 이전 ProductResult를 Clear하고 Stored/Product 실물의 기존 Transfer를 비활성화한다. 이미 승인된 대상과 불변 DB의 Prefab 원형을 보호한다. |
| [BuildingRequests.cs](../../../../Assets/Scripts/Components/Buildings/BuildingRequests.cs) | 요청의 단독 Consumer, 게시/삭제 시점과 승인 상태 인계 계약을 갱신했다. |
| [BuildingLifecycleApplySystem.cs](../../../../Assets/Scripts/Systems/5_StateApply/BuildingLifecycleApplySystem.cs) | 요청 대신 승인 태그가 있는 건물을 조회한다. 기존 Disabled 대상 처리 범위도 유지한다. BuildingType 소실만 방어하고 정책 검증은 반복하지 않는다. 실제 내용물 반환/환급/벨트 정지/건물 삭제는 기존 ECB 시점에 유지한다. |
| [입고 Decision](../../../../Assets/Scripts/Systems/2_Decision/BuildingItemInputDecisionSystem.cs) | 철거 예정 출발 벨트와 목적 건물을 제외하고 이전 입고 계획을 초기화·비활성화한다. 슬롯 예약이나 Transfer 요청이 생성되지 않는다. |
| [저장품 출고](../../../../Assets/Scripts/Systems/2_Decision/StorageItemOutputDecisionSystem.cs), [생산품 출고](../../../../Assets/Scripts/Systems/2_Decision/ProductItemOutputDecisionSystem.cs) | 철거 owner의 이전 계획을 초기화·비활성화하고 철거 목적 벨트를 제외한다. |
| [채굴 Decision](../../../../Assets/Scripts/Systems/2_Decision/MinerDecisionSystem.cs), [제작 Decision](../../../../Assets/Scripts/Systems/2_Decision/CrafterDecisionSystem.cs) | 철거 대상의 실행 결정을 비활성화하여 새 소비·진행·완료 결과를 막는다. 제작 상태 전이 결정도 비활성화하며 기존 지속 상태를 임의로 바꾸지 않는다. |
| [벨트 Decision](../../../../Assets/Scripts/Systems/2_Decision/BeltMovementDecisionSystem.cs) | 현재 철거 벨트에서 이동량 0. 다음 철거 벨트로는 1−AlignmentEpsilon 경계 이전까지만 접근해 Execution의 Progress≥1 전환을 막는다. |
| [Splitter](../../../../Assets/Scripts/Systems/2_Decision/SplitterDecisionSystem.cs), [Merger](../../../../Assets/Scripts/Systems/2_Decision/MergerDecisionSystem.cs) | 철거 라우터·입력 벨트·출력 벨트를 제외하며 이전 활성 결정을 초기화한다. |
| [ItemSpawnAdmissionDecisionSystem.cs](../../../../Assets/Scripts/Systems/2_Decision/ItemSpawnAdmissionDecisionSystem.cs) | 철거 예정 Storage/Product 목적지의 Spawn 요청을 Decision에서 즉시 비활성화하고 EndStateApply에 삭제한다. EndCommand 재생에서 생성된 요청도 검사한다. 새 파일의 .meta를 추가했다. |
| [ItemRequests.cs](../../../../Assets/Scripts/Components/Items/ItemRequests.cs) | SpawnItemRequest를 enableable 일회성 요청으로 바꿔 사전 거부를 생성 쿼리에서 즉시 제외한다. 실제 요청 엔티티 삭제는 기존 EndStateApply다. |
| [ItemLifecycleApplySystem.cs](../../../../Assets/Scripts/Systems/5_StateApply/ItemLifecycleApplySystem.cs), [ItemOwnershipApplySystem.cs](../../../../Assets/Scripts/Systems/5_StateApply/ItemOwnershipApplySystem.cs) | 철거 전용 쿼리·임시 요청 목록·Lookup/분기를 제거했다. 일반 생성/소유권 처리와 Destroy 보호를 유지했다. |

사용하지 않는 Common/DemolishBuildingRequestLookup.cs와 .meta를 제거했다. 새로운 공간 캐시나 별도 철거 승인 목록의 소유 시스템은 추가하지 않았다. 승인 태그는 EndCommand 이후 StateApply까지 불변이며 건물 삭제와 함께 소비한다. 대상의 BuildingType이 소실되면 태그만 제거해 기존 무효 대상 소비 의미를 유지한다. 요청은 Command 검증 전에 실체화해야 하며 승인 대상/철거 조건의 외부 변경은 지원하지 않는다.

## 기존 회귀의 조정과 실행

기존 사례를 새 정책에 맞게 확장했다. 단순 필드/이름 검사용 테스트나 별도 테스트 클래스를 추가하지 않았다.

- [Phase7BuildingDemolishTests](../../../../Assets/Editor/Tests/Phase7BuildingDemolishTests.cs): 실제 Input Decision/Reservation, Belt Decision/Execution, Splitter/Merger 및 Stored/Product Output Decision으로 앞단 차단을 검증한다. 이전 활성 결정/Transfer도 검사하며 반환/환급·중복·거부 대조군은 유지한다.
- [Phase7ItemCreationDemolitionTests](../../../../Assets/Editor/Tests/Phase7ItemCreationDemolitionTests.cs): EndCommand 요청 삭제와 승인 태그, 미착수 제작의 재료 소비 중단, 진행 중 제작/채굴 정지, 광물 수량 유지, EndCommand에서 만들어진 생성 요청의 Decision 거부, 두 Apply 순서 및 비활성 요청의 실제 삭제를 확인한다. Prefab 원형 거부는 기존 무효 대상 검사에 포함했다.
- [TestSimulationDriver](../../../../Assets/Editor/Tests/TestSupport/TestSimulationDriver.cs)와 [실제 공사 그룹](../../../../Assets/Editor/Tests/Phase7EndToEndConstructionPipelineTests.cs)에 새 Admission 시스템을 등록했다. 정상 아이템·벨트·제작·공사 취소 대조군도 실행했다.

연결된 Unity 6000.4.11f1 Editor에서 재컴파일 최종 상태를 확인한 뒤 아래 EditMode 필터를 순차 실행하고 completed 결과를 확인했다.

| 필터 | Total | Passed | Failed | Skipped | Inconclusive |
| --- | ---: | ---: | ---: | ---: | ---: |
| Phase7BuildingDemolishTests | 19 | 19 | 0 | 0 | 0 |
| Phase7ItemCreationDemolitionTests | 16 | 16 | 0 | 0 | 0 |
| Phase1ItemIntegrationTests | 13 | 13 | 0 | 0 | 0 |
| Phase2BeltIntegrationTests | 4 | 4 | 0 | 0 | 0 |
| Phase7EndToEndConstructionPipelineTests | 4 | 4 | 0 | 0 | 0 |
| Phase5CrafterInputPipelineTests.SortedGroups_ActualCreation_Transport_Production_RecipeChange_AndClear | 4 | 4 | 0 | 0 | 0 |
| Phase7ConstructionCancelTests | 3 | 3 | 0 | 0 | 0 |
| 합계 | 63 | 63 | 0 | 0 | 0 |

의도된 불변식 오류 회귀 뒤 Editor 상태 API가 playing/paused로 표시했다. 별도 읽기 전용 조회에서 isPlaying:false, isPaused:true, isPlayingOrWillChangePlaymode:false를 확인했다. Play Mode가 아닐 때만 일시정지를 해제했으며 최종 ready/stopped를 확인했다. Play Mode 검증은 실행하지 않았다.

## 원본 실행 근거

Logs/Codex/BuildingDemolitionStop-20261004/에 원본 CLI 응답을 보존했다.

- [재컴파일](../../../../Logs/Codex/BuildingDemolitionStop-20261004/recompile-status.json)
- [철거](../../../../Logs/Codex/BuildingDemolitionStop-20261004/demolition-status.json), [생성·생산 중단](../../../../Logs/Codex/BuildingDemolitionStop-20261004/creation-status.json)
- [일반 아이템](../../../../Logs/Codex/BuildingDemolitionStop-20261004/items-status.json), [일반 벨트](../../../../Logs/Codex/BuildingDemolitionStop-20261004/belts-status.json)
- [공사 그룹](../../../../Logs/Codex/BuildingDemolitionStop-20261004/pipeline-status.json), [정상 제작](../../../../Logs/Codex/BuildingDemolitionStop-20261004/crafter-status.json), [현장 취소](../../../../Logs/Codex/BuildingDemolitionStop-20261004/cancel-status.json)
- [어셈블리 최신성](../../../../Logs/Codex/BuildingDemolitionStop-20261004/assembly-freshness.json), [실제 Play 상태](../../../../Logs/Codex/BuildingDemolitionStop-20261004/editor-play-state.json), [조건부 일시정지 복구](../../../../Logs/Codex/BuildingDemolitionStop-20261004/editor-pause-restoration.json), [최종 Editor](../../../../Logs/Codex/BuildingDemolitionStop-20261004/editor-status-final.json)
- [요약](../../../../Logs/Codex/BuildingDemolitionStop-20261004/summary.json)

## 남은 범위와 한계

Storage/Product Spawn 요청은 Admission 실행 전에 실체화하는 계약이다. 현재 실제 runtime Producer는 없으며 향후 Admission 이후 발행 경로를 추가할 때 그 Producer에서 철거 상태를 확인해 생성을 막아야 한다. 이번 결과는 그러한 미래 경로나 새 드론 운송을 검증하지 않는다.

전체 EditMode, Play Mode, 실제 SubScene 베이킹·프리팹, 화면·입력·성능 검증은 수행하지 않았다. 기존 F-004/F-005 기록은 이전 정책의 실행 근거로 보존하며 현재의 입출고·생산·운송 중단 계약을 대신하지 않는다. 코드와 현재 문서는 [AGENTS.md](../../../../AGENTS.md), [건물 컴포넌트](../../../CodeMemory/Components/Buildings.md), [합의 명세](../../../Specifications/ConstructionAndDroneSupply.md)를 따른다. 새 드론 공급/회수 구현은 후속 작업이다.
