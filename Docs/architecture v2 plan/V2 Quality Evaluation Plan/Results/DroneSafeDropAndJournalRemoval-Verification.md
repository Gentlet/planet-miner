# 현장 외부 드론 배출과 예정 월드 기록 제거

## 변경과 결과

2026-10-06 사용자 승인에 따라 드론의 현장 내부 월드 배출과 일반 World Spawn을 금지했다. 이 규칙을 기존 관리·인계 경계에 연결하고 PendingWorldItemChangeElement와 WorldItemChangeJournalUtility 및 메타를 제거했다. 최종 Unity 컴파일은 `completed`, `failed:false`, `compilationFailed:false`, `errors:[]`이며 선별 EditMode **142/142 통과**다. 실패·건너뜀·Inconclusive는 0이다.

## 배출 위치 선택과 도착 검사

- [ConstructionSiteWorldItemUtility](../../../../Assets/Scripts/Common/ConstructionSiteWorldItemUtility.cs)는 현재 활성·비취소 현장의 GridPosition·Direction·BuildingFootprint로 회전된 영역을 검사한다. 금지 범위는 건설 현장이며 완공 건물·벨트까지 확대하지 않았다. 원본 ECS에서 호출 시점의 임시 footprint 배열을 만들며 영속 공간 맵을 추가하지 않는다.
- [DroneTaskDecisionSystem](../../../../Assets/Scripts/Systems/2_Decision/DroneTaskDecisionSystem.cs)은 공급·보관 목적지가 없을 때 현재 관측 셀이 허용되면 그 셀을 선택한다. 현장 안이면 기존 Direct 경로 요청의 IsDropPositionSearch 변형을 게시한다. Source/Destination은 Null이고 기존 배정·관측·평가 revision을 포함한다. 외부 평가자가 충전 경유를 포함한 실제 경로 기준으로 가장 가까운 도달 가능한 현장 밖 셀을 선택할 계약이다. 관리층에서 임의 격자 범위·기본 거리·실제 이동을 만들지 않았다.
- 검색 결과는 [DroneRouteEvaluationResult](../../../../Assets/Scripts/Components/DroneLogistics/DroneRouteContracts.cs)의 Reachable·유한한 비음수 TotalDistance·HasDropPosition·DropPosition을 요구한다. 기본 0 좌표를 검색 성공으로 해석하지 않는다. None/Unreachable이면 적재품을 유지하며 평가를 기다린다. 외부 평가자는 조건 변화 시 같은 요청의 결과를 갱신할 수 있다. 성공 위치가 현장에 막히면 해당 경로를 무효화하고 재검색한다.
- [Publish](../../../../Assets/Scripts/Systems/5_StateApply/DroneTaskAssignmentPublishSystem.cs)는 선택한 DropPosition을 기존 배정에 보존하고 MovingToDestination/NextAction=DropCargo로 공개한다. 선택 목표는 실제 위치의 원본을 대체하지 않는다.
- [인계 검증](../../../../Assets/Scripts/Common/DroneItemTransferValidationUtility.cs)은 Assignment.DropPosition, 신호의 WorldPosition, 현재 관측 셀이 일치하고 여전히 현장 밖일 때만 배출을 허용한다. 도착 전 신호는 Rejected이며 배정·실물은 유지한다. 새 현장이 선택 셀을 점유하면 [Lifecycle](../../../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs)과 다음 Decision의 정리 검사가 Retargeting으로 연결한다. D/E 계획 뒤 새 점유도 최종 Apply에서 검사한다.

기존 실물 엔티티, 적재 출처, 현장 수량 예약·해제, 공개 실패 롤백, 최초 작업 순서와 revision 보호는 유지한다. 실제 배출은 공통 ItemOwnership API로 수행하며 회수 작업 표시는 EndStateApply에 반영한다. 현장 안에 다시 내려놓는 동작은 생성하지 않는다.

## 일반 생성과 완공

[ItemSpawnAdmissionDecisionSystem](../../../../Assets/Scripts/Systems/2_Decision/ItemSpawnAdmissionDecisionSystem.cs)은 현장 내부 World Spawn 요청을 비활성화하고 삭제를 기록한다. [ItemLifecycleApplySystem](../../../../Assets/Scripts/Systems/5_StateApply/ItemLifecycleApplySystem.cs)도 같은 검사를 호출 시점의 footprint로 수행하여 Admission을 거치지 않은 요청이나 뒤늦은 현장 생성에 대해 생성하지 않고 요청을 소비한다. 두 검사에서 사용하는 임시 NativeArray는 해당 Job 완료 이후 Dispose한다.

[ConstructionLifecycleApplySystem](../../../../Assets/Scripts/Systems/5_StateApply/ConstructionLifecycleApplySystem.cs)은 이미 존재하는 월드 아이템의 실제 ItemOwnership/GridPosition과 활성 Destroy 여부로 차단을 설정·해제한다. After(DroneTaskLifecycleApplySystem)만 유지하며 Item/Building Lifecycle과 예정 월드 기록의 의존은 제거했다. 자재 수령·회수의 실제 상태 반영 뒤 완공을 검사하고, 성공한 건물 Spawn 이후에만 자재·현장을 삭제한다.

철거의 기존 실물 반환·비용 환급은 기존 위치와 조건으로 유지했다. 정상적인 비중첩 배치에서는 철거 위치가 다른 현장의 footprint 안에 있지 않는다. 현재 사용되지 않는 직접 생성 API나 서로 다른 배치 요청의 입력 중첩까지 새 점유 정책으로 변경하지 않았으며, 이 기록 제거를 모든 임의 입력의 비중첩을 검증한 결과로 취급하지 않는다. 같은 틱 철거 후 배치 거부 정책과 두 ECB·Synchronization 경계는 유지한다.

## 실행 근거

Unity 6000.4.11f1 연결 Editor에서 컴파일 후 다음 클래스를 순차 실행했다. 원본은 `Logs/Codex/DroneSafeDropAndJournalRemoval-20261006/`에 보존한다.

| 확인 | 최종 결과 | 기록 |
| --- | --- | --- |
| 컴파일 | completed, failed:false, compilationFailed:false, errors:[] | `recompile.json`, `recompile-status.json` |
| DroneRetargetingTests | 17/17 | `retarget-run.json`, `retarget-status.json` |
| ConstructionClearanceTests | 13/13 | `clearance-run.json`, `clearance-status.json` |
| DroneItemTransferTests | 33/33 | `transfer-run.json`, `transfer-status.json` |
| DroneTaskSchedulingTests | 36/36 | `scheduling-run.json`, `scheduling-status.json` |
| Phase7ConstructionCompletionTests | 8/8 | `completion-run.json`, `completion-status.json` |
| Phase7BuildingDemolishTests | 19/19 | `demolition-run.json`, `demolition-status.json` |
| Phase7ItemCreationDemolitionTests | 16/16 | `item-demolition-run.json`, `item-demolition-status.json` |
| Editor | ready, compiling:false, domainReloadInProgress:false, playMode:stopped | `editor-status.json` |
| 합계·선택 범위 | 142/142, 지정 클래스와 결과 일치, 실패·건너뜀·Inconclusive 0 | `test-summary.json` |
| 어셈블리·메타 | 런타임/Editor DLL 최신, 새 helper GUID 정상·유일, ECS 102개/드론 22개, 분리 시스템 파일 0 | `freshness-and-meta.json` |
| 문서·차이 | 문서 11개·로컬 링크 578개 누락 0, git diff --check 종료 0, 새 helper 공백/충돌 0 | `static-check.json`, `diff-check.txt` |

새 회귀는 현재 셀 배출 유지, 회전된 현장 안에서 외부 위치 검색, 미평가/도달 불가 대기와 결과 갱신 후 재개, 도착 전 신호 거부, 도착 후 배출, 선택 셀의 새 현장 점유 전후 재탐색을 확인한다. World Spawn의 내부 거부·외부/경계 허용과 Admission 없이 최종 Consumer에서 거부되는 경우도 확인한다. 기존 인계·예약·완공·철거 회귀를 유지했다.

저널 소스·메타 4개 삭제는 처음 기본 샌드박스에서 접근 거부됐다. 실제 절대 경로가 프로젝트 내부인지 확인한 뒤 승인된 해당 파일 삭제 명령으로 마무리했으며 잔여 파일과 코드 참조가 없다. 권한 변경이나 다른 경로 삭제는 수행하지 않았다. 기존 staged/unstaged 작업을 유지하며 스테이징·커밋하지 않았다.

## 검증 범위

수행자·경로 결과·행동 신호와 테스트 프리팹 DB는 fixture가 제공했다. 검색 결과의 실제 최단거리 계산과 실제 드론 이동·충전·관측 위치 SoT 연결은 후속이며 임의 성공 처리로 대체하지 않았다. 전체 EditMode, Play Mode, 실제 SubScene 베이킹·화면·성능은 검증하지 않았다.

이전 예정 월드 기록과 131개 검증은 [당시 4단계 기록](DroneRetargetingAndClearance-Verification.md)에 보존한다. 현재 계약은 이 기록과 갱신된 명세·컴포넌트 문서를 따른다.
