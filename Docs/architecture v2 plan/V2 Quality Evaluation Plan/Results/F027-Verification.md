# F-027 배치 접수 순서와 요청 간 중복 승인 방지

날짜: 2026-10-08. 현재 상태: 구현·소스 확인·Unity 컴파일·선별 EditMode 24/24 통과. F-027 완료, F-041 별도 유지.

## 확정한 게임 규칙

플레이어만 건물 배치를 요청하며 입력 단계에서 가능 여부를 확인한다. Command는 기존 점유를 최종 검사하고 같은 틱의 다른 요청은 실제 접수 순서에서 앞 요청의 최종 승인 우선으로 처리한다. 새 현장 생성과 기존 벨트 방향 변경에 같은 규칙을 적용한다. StrictAllOrNothing과 AllowPartialPlacement는 기존 요청 단위 의미를 유지한다.

레거시는 삭제 직전 Git 커밋 f85d2ca에서 BuildingPlacementController.CreateConstructionRequests → BuildingPlacementReservationUtility.TryReserve → ChunkMapSystem.TryReserveBuilding을 확인했다. 요청 생성 전에 후보 묶음 전체의 footprint를 공용 예약에 넣으며 실패하면 해당 묶음의 예약을 모두 해제한다. 뒤 입력이 앞 예약에 막히는 동작과 묶음 원자성은 코드·기존 문서로 확인했다. 여러 입력원의 동시 접수 순서를 정의한 정책은 확인되지 않았다. 과거 코드 복원이나 작업 트리 되돌리기는 하지 않았다.

## 구현과 책임

- [접수 API](../../../../Assets/Scripts/Common/BuildingPlacementRequestUtility.cs): 기존 드론 행동 Submit의 World별 순번 패턴을 따른다. 플레이어 입력의 요청·후보를 함께 준비하고 요청의 ReceiptSequence를 발급한다. 준비 실패 시 이번 요청 엔티티만 회수한다. 런타임 UI 연결은 후속이다.
- [요청/접수번호 상태](../../../../Assets/Scripts/Components/Construction/BuildingPlacementRequests.cs): 요청 헤더에 ReceiptSequence를 추가하고 같은 파일의 BuildingPlacementReceiptSequence는 다음 번호만 World 수명 동안 유지한다. 전역 static 번호·공간 점유·설치 Stamp를 저장하지 않는다. 번호 0/ulong.MaxValue 상태는 접수 전에 예외로 거부한다.
- [기존 Command](../../../../Assets/Scripts/Systems/Command/BuildingPlacementCommandSystem.cs): 요청을 접수번호 오름차순으로 정렬한다. 번호 0/중복 입력은 오류 기록 후 요청 전체를 EndCommand에 소비하며 임의의 Query/Entity 우선순위를 적용하지 않는다. 후보 버퍼는 처리 직전에 다시 얻는다.
- [기존 Validation](../../../../Assets/Scripts/Common/BuildingPlacementValidationUtility.cs): 기존 점유와 묶음 내부 claimedCells에 더해 Command의 approvedCells를 읽는다. 기존 세 검증 오버로드 모두 같은 추가 입력을 지원한다. 묶음 정책 적용 후 Command가 실제 승인 후보의 회전 셀만 공유한다. Strict 실패는 앞 승인 셀을 해제하거나 임시 선점을 남기지 않는다.
- 기존 같은 타입 벨트는 현장/자재 요구 없이 Direction만 EndCommand에 기록하지만 승인 셀은 동일하게 공유한다. 뒤 요청의 같은 벨트 후보가 실패하면 그 요청의 Strict/Partial 규칙을 적용한다.

접수 순서가 없던 기존 헤더만으로 실제 도착 순서를 복원할 수 없으므로 작은 접수 API와 번호 원본을 추가했다. 새 배치 시스템·공간 캐시·영속 점유 예약은 추가하지 않았고 시스템을 partial 파일로 분리하지 않았다. 실제 현장/건물은 ECS가 소유하고 공간 인덱스는 기존 Synchronization이 재구축한다.

## 보존한 경계와 별도 과제

F-026의 필요한 세 Writer 완료와 F-023의 회전 전 기본 크기 저장/Reader 한 번 회전을 유지한다. 승인/방향 변경/요청 삭제는 EndCommand, 완공/철거는 EndBuilding, 드론 결과는 EndSimulation, 공간 등록은 마지막 Synchronization이다. 같은 틱 취소·철거 뒤 재배치는 기존 인덱스 점유로 거부한다.

PlacementStamp의 기존 RequestTick 우선순위와 요청 내부 Order는 유지한다. ReceiptSequence는 접수 중재용이며 Stamp로 승계하지 않는다. 비중첩 요청의 동일 Stamp와 일반 출고/Routing/드론의 동률 처리 차이는 F-041에 남는다. 기존 일반 출고·Routing의 설치 순서 우선정책을 바꾸지 않는다.

## 관련 이슈 영향 정리 (2026-10-08)

현재 소스와 아래 원본 실행 기록을 대조하고 [품질 개선 Tasks](../V2%20Quality%20Improvement%20Tasks.md)에 반영했다. 추가로 완전히 닫을 이슈는 확인하지 않았으며 F-027 외의 완료 수를 늘리지 않는다. 이번 영향 정리는 문서만 변경하며 새 컴파일·테스트를 실행하지 않았다.

- **F-027 완료 유지:** 요청 간 동일/부분 footprint 중복, Strict 실패의 선점 누출, 기존 벨트 방향 변경의 중복 쓰기를 같은 접수 순서로 중재했다. 원본 회귀와 현재 승인 코드가 일치한다.
- **F-041 결정 범위 축소, 미완료 유지:** 배치 승인 순서의 정책·접수번호 발급 경로·중재는 해결됐다. [Command의 Stamp 기록](../../../../Assets/Scripts/Systems/Command/BuildingPlacementCommandSystem.cs:199)은 여전히 candidateTick/후보 인덱스이며 [Completion](../../../../Assets/Scripts/Systems/Construction/StateApply/ConstructionLifecycleApplySystem.cs:205)은 그대로 승계한다. [목적지 예약](../../../../Assets/Scripts/Systems/Buildings/Reservation/BeltDestinationReservationSystem.cs:350)은 Entity.Index, [Routing](../../../../Assets/Scripts/Systems/Buildings/Decision/SplitterDecisionSystem.cs:227)은 방향 순회 첫 후보, [드론 비교](../../../../Assets/Scripts/Common/DroneSchedulingUtility.cs:413)는 동률 0을 유지한다. 남은 정책은 후속 설치 Stamp의 표현/승계와 공통 동률 계약이며 승인 순서를 다시 결정하는 일이 아니다.
- **F-023 기존 완료의 회귀 근거 보강:** Test10의 8사례에서 (2,3) Right 후보를 EndCommand에 현장으로 만들고 Sync가 (3,2)의 6셀을 같은 Entity에 등록함을 확인했다. 각 정책·동일/부분 겹침 조합의 반복이며 방향 전체나 비정사각형 완료 경로 검증이 아니다. 기존 4방향·직접 Spawn·Completion 실행 제한을 보존한다.
- **F-026 기존 완료 경계 유지:** 필요한 세 공간 Writer 대기는 유지됐고 F-027은 아직 미생성 현장의 승인 점유만 별도 전달한다. 일반 배치/Sync 통과로 지연 Writer 강제 주입이나 Validator 없는 실행까지 검증했다고 주장하지 않는다.

관련되지만 이번 변경으로 해결되지 않은 범위도 구분한다. F-022의 request→Config→기본 크기 선택과 DB 크기 권위는 [공통 Spawn](../../../../Assets/Scripts/Common/BuildingLifecycleUtility.cs:33)에 그대로 남는다. F-028의 다른 Reader들이 Fence 메타데이터를 RW로 등록하는 구조도 변경하지 않았다. R-01의 큰 footprint는 [Sync의 int 면적/용량 산술](../../../../Assets/Scripts/Systems/Synchronization/BuildingSpatialSyncSystem.cs:85)과 배치의 셀 순회 제한 문제를 유지하며 임시 승인 셀이 상한/overflow 해결책은 아니다. 따라서 이들 항목을 F-027로 자동 종료하거나 새 정책을 확정하지 않는다. 테스트용 World의 실행은 F-021/F-025의 실제 Bake·빌드 진입 장면 문제를 해결하는 근거도 아니다.

Task 현황은 실제 44개 체크박스를 다시 집계해 완료 26·미완료 18(P2 완료 16·미완료 17)로 정리했다. 이전 24/20 집계 이후 이미 완료한 F-014·F-027을 반영한 것으로, 이번 문서 정리에서 두 이슈를 새로 구현한 것은 아니다. Q12·Q23과 2026-10-06 재검토의 본문은 당시 근거로 보존한다.

## 검증 범위와 현재 결과

소스 확인과 git diff --check는 수행했다. 최종 컴파일은 completed/failed:false/errors:[]이며 Editor ready/compiling:false/domainReloadInProgress:false, 실행/테스트 어셈블리의 각 소스 최신성을 확인했다. UI/입력·Play Mode·실제 SubScene 베이킹·성능은 실행하지 않았다.

[기존 배치 테스트](../../../../Assets/Editor/Tests/Phase7PlacementCommandTests.cs)에 실제 결함의 회귀 11사례를 추가했다. 별도 요청 동일 위치/회전 footprint 일부 겹침의 Strict/Partial 8조합, Strict 실패 뒤 후속 승인 1사례, 기존 벨트 방향 변경의 뒤 요청 Strict/Partial 2사례다. EndCommand 후 현장 수와 Sync 후 셀→Entity/방향을 검사한다. 첫 요청을 다른 archetype으로 옮기고 뒤 후보에 더 작은 RequestTick을 주어 쿼리 순서나 Tick이 접수 순서를 대체하지 않는 흐름도 포함한다. 새 테스트 추가를 승인/착수의 필수 조건으로 삼은 것은 아니다.

기존 배치·철거·완공·드론 작업 테스트의 요청 생성부를 같은 Submit API로 변경했다. 실행한 선별 EditMode 결과는 다음과 같으며 failed/skipped/inconclusive는 모두 0이다.

- Phase7PlacementCommandTests: 18/18. 신규 회귀 11사례를 포함하며 실제 EndCommand 뒤 현장 수와 Sync 뒤 셀→Entity/방향을 검사했다. [원본 결과](../../../../Logs/QualityImprovement/F027/placement-status.json).
- Phase7EndToEndConstructionPipelineTests: 4/4. 기존 공사→완공·물류·철거, 취소, 벨트 변경, 즉시 재배치 경계를 확인했다. [원본 결과](../../../../Logs/QualityImprovement/F027/construction-status.json).
- Phase7BuildingDemolishTests.Test13_EndCommandECB_Playback_ImmediateAvailabilityForDownstreamPhases: 1/1. [원본 결과](../../../../Logs/QualityImprovement/F027/endcommand-status.json).
- DroneTaskSchedulingTests.ApprovedPlacement_PublishesTasksAtEndSimulation_AndSchedulesThemOnTheNextDecision: 1/1. [원본 결과](../../../../Logs/QualityImprovement/F027/drone-status.json).

합계 24/24는 이번 선별 실행만의 결과다. 실제 런타임 플레이어 Producer/UI 연결이나 전체 그룹·Play Mode 검증으로 확대 해석하지 않는다. 접수번호 0/중복 거부, 번호 소진, Submit 준비 실패 시 엔티티 회수는 소스로 확인했으며 별도 실행 반례를 추가하지 않았다. 마지막 C# 주석의 한국어 정리 후 컴파일을 재확인했고 행동 코드는 바꾸지 않아 테스트를 반복하지 않았다.

초기에는 열린 Editor의 Pipeline 연결이 없어서 컴파일 명령이 실패했으며 공식 open은 기존 Editor를 재사용했다. 사용자가 Window → Pipeline → Start Server를 실행한 뒤 연결·컴파일·테스트를 확인했다. 다른 Editor 종료·패키지 업그레이드·설정 변경·대체 headless 실행은 하지 않았다.

최종 실행 기록: [컴파일 상태](../../../../Logs/QualityImprovement/F027/final-recompile_status.json), [Editor 상태](../../../../Logs/QualityImprovement/F027/final-editor_status.json), [어셈블리 최신성](../../../../Logs/QualityImprovement/F027/final-assembly-freshness.json).
