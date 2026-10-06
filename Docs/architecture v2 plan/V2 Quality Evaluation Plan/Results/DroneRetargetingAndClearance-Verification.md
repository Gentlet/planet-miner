# 적재 드론 재배정과 완공 차단 갱신

## 변경과 결과

2026-10-06 사용자 승인에 따라 4단계의 자동 재배정·종료 연결과 완공 차단 갱신을 구현했다. 기존 시스템을 확장했으며 보조 partial 파일이나 새 실행 시스템을 추가하지 않았다.

최종 Unity 컴파일은 `completed`, `failed:false`, `compilationFailed:false`, `errors:[]`다. 선별 EditMode **131/131 통과**, 실패·건너뜀·Inconclusive는 0이다.

## 적재품 재배정

- [DroneTaskDecisionSystem](../../../../Assets/Scripts/Systems/2_Decision/DroneTaskDecisionSystem.cs)은 기존 Retargeting 배정과 실제 StoredItemElement를 읽어 Direct 경로 후보를 작성한다. 공급 자재는 다른 필요 현장을 먼저 찾고, 가능한 현장이 없으면 보관 장소를 찾는다. 회수품은 보관 전 현장에 직접 공급하지 않는다. 같은 목적 종류 안에서는 충전 경유를 포함한 외부 경로 거리, 동률이면 PlacementStamp를 사용한다.
- 아직 평가되지 않은 경로는 대기한다. 모든 수행 가능한 목적지 경로가 도달 불가로 확정되거나 목적지가 없을 때 DropCargo 후보를 작성한다. 실제 경로 계산이나 임의 기본 거리는 추가하지 않았다.
- [ConstructionSupplyReservationSystem](../../../../Assets/Scripts/Systems/3_Reservation/ConstructionSupplyReservationSystem.cs)은 이전 현장 예약을 정산하고 적재 재배정을 빈 수행자의 최초 배정보다 먼저 처리한다. 적재 그룹 안에서는 최초 작업의 CreationSequence 순서를 사용한다. 공급원 재고·보관 공간 예약은 추가하지 않았다.
- [DroneTaskAssignmentPublishSystem](../../../../Assets/Scripts/Systems/5_StateApply/DroneTaskAssignmentPublishSystem.cs)은 기존 배정 엔티티의 revision을 증가시키고 다음 행동·목적지·개별 예약을 EndStateApply에 함께 공개한다. 실제 적재품과 최초 순서는 유지하고 최종 검사 실패/수량 축소 시 해당 후보의 예약만 롤백한다. 연구로 바뀐 공통 적재 한도는 이미 보유한 실물에 소급 적용하지 않는다.
- [DroneSchedulingUtility](../../../../Assets/Scripts/Common/DroneSchedulingUtility.cs)는 배정·수행자 연결, 실제 소유 적재품, 관측 revision과 경로 스냅샷을 재검사한다. OriginalTaskCreationSequence는 현재 목적 Task가 바뀌어도 최초 배정 순서를 보존하는 이력 값이다. 실물 종류·수량의 원본은 StoredItemElement/ItemIdentity/ItemOwnership이다.
- [DroneTaskLifecycleApplySystem](../../../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs)은 기존 인계 결과와 무효화 의도를 처리한다. 미소비 결과 때문에 남은 Completed/Cancelled는 유지한다. 정리 대상과 현재 수행자 연결이 일치할 때만 적재품을 해당 배정의 보존 근거로 사용하여 새 배정의 실물을 이전 배정이 참조하지 않게 했다.

취소·완공·철거로 대상이 소실되면 기존 무효화/예약 해제 뒤 적재품을 보존한 Retargeting을 다음 Decision의 목적지 탐색으로 연결한다. 부분 보관 후 남은 회수품도 보관 목적지 탐색 규칙을 유지한다. 이전 틱 인계 계획·행동 접수 순서와 실제 인계 성공 수량 정산 계약은 유지한다.

## 완공 차단과 월드 변경 가시성

[ConstructionLifecycleApplySystem](../../../../Assets/Scripts/Systems/5_StateApply/ConstructionLifecycleApplySystem.cs)은 방향이 적용된 footprint 아래의 현재 월드 실물과 예정 월드 변경을 확인하여 AwaitingItemClearance를 매번 설정·해제한다. 현재 GridPosition/ItemOwnership을 읽으며 활성 Destroy 대상은 제외한다. 마지막 실물이 실제 회수되면 같은 틱 완공을 허용한다.

당시 EndStateApply에 기록만 된 World Spawn·철거 실물 반환·환급은 일회성 결과 버퍼 PendingWorldItemChangeElement로 위치를 게시했다. [ItemLifecycleApplySystem](../../../../Assets/Scripts/Systems/5_StateApply/ItemLifecycleApplySystem.cs)과 [BuildingLifecycleApplySystem](../../../../Assets/Scripts/Systems/5_StateApply/BuildingLifecycleApplySystem.cs)이 성공 기록을 추가하고 생산자의 ECB에 Clear를 기록했다. 실제 Clear는 완공 검사 이후 EndStateApply에 수행했으며 다음 틱으로 이월하지 않았다.

이 문단은 당시 구현과 아래 검증의 근거다. 이후 예정 위치 버퍼/기록 helper를 삭제하고 [현재 현장 내부 생성·방출 허용 검사](../../../../Assets/Scripts/Common/ConstructionSiteWorldItemUtility.cs)와 현재 월드 실물만 읽는 [Construction](../../../../Assets/Scripts/Systems/5_StateApply/ConstructionLifecycleApplySystem.cs)으로 변경했다. 현재 정책은 [안전 방출·기록 제거 검증](DroneSafeDropAndJournalRemoval-Verification.md)을 따르며 이 보고서의 실행 수치는 당시 기록으로 보존한다.

직접 Storage/Ownership UpdateAfter 두 개는 제거했다. Construction은 Drone Lifecycle의 실제 인계·수량 정산과 Item/Building Lifecycle의 월드 변경 기록 이후에 읽는다. 두 월드 변경 생산자 사이에는 상대 순서를 추가하지 않았다. 새 영속 공간 맵이나 병렬 소유권 구현은 없다. 드론 방출/회수와 Command 취소 반환은 그 경계에서 이미 갱신된 실제 상태를 읽는다. 아직 실체화되지 않은 Spawn은 예정 위치로 차단하고, 실체화 이후 다음 Decision이 기존 회수 작업 생성 흐름을 사용한다.

완공 건물 Spawn 성공 이후에만 자재·현장을 삭제하는 계약을 유지한다. Spawn 실패이면 자재·현장을 보존한다. 동일 틱 철거/취소 후 배치 거부 정책과 공간 인덱스 갱신 시점도 유지한다.

## 실행 근거

Unity 6000.4.11f1 연결 Editor에서 동일한 최종 제품 소스를 대상으로 다음 클래스를 순차 실행했다. 마지막 방출 통합 테스트를 추가한 뒤 다시 컴파일하여 재배정 12개를 재실행했다. 이때 제품 코드는 변경하지 않았으므로 나머지 클래스의 검증 결과는 유지했다. 원본은 `Logs/Codex/DroneRetargetingAndClearance-20261006/`에 보존한다.

| 확인 | 최종 결과 | 원본 기록 |
| --- | --- | --- |
| 컴파일 | completed, failed:false, compilationFailed:false, errors:[] | `recompile-final.json`, `recompile-status-final.json` |
| DroneRetargetingTests | 12/12 | `retarget-run-final.json`, `retarget-status-final.json` |
| ConstructionClearanceTests | 7/7 | `clearance-run-final.json`, `clearance-status-final.json` |
| DroneItemTransferTests | 33/33 | `transfer-run.json`, `transfer-status.json` |
| DroneTaskSchedulingTests | 36/36 | `scheduling-run.json`, `scheduling-status.json` |
| Phase7ConstructionCompletionTests | 8/8 | `completion-run.json`, `completion-status.json` |
| Phase7BuildingDemolishTests | 19/19 | `demolition-run.json`, `demolition-status.json` |
| Phase7ItemCreationDemolitionTests | 16/16 | `item-demolition-run.json`, `item-demolition-status.json` |
| 합계·선택 범위 | 131/131, 모든 결과가 지정한 클래스와 일치 | `test-summary.json` |
| Editor | ready, compiling:false, domainReloadInProgress:false, playMode:stopped | `editor-status.json` |
| 어셈블리·메타 | 런타임/Editor DLL 최신, 신규 GUID 4개 정상·유일, ECS 103개/드론 22개, 분리 시스템 파일 0 | `freshness-and-meta.json` |
| 문서·차이 | 문서 10개·로컬 링크 578개 누락 0, git diff --check 종료 0 | `static-check.json`, `diff-check.txt` |

모든 최종 사례의 실패·건너뜀·Inconclusive는 0이다. 경로 결과는 테스트가 명시적으로 게시하고 실제 이동으로 대신하지 않았다. 새 회귀는 목적 종류 우선순위·거리/동률·미평가 대기·도달 불가 방출·부분 보관·적재 예약 우선·이전 예약 정산·최종 실패 롤백·이전 행동 revision 거부를 확인한다. 마지막 실제 회수와 예정 World Spawn/반환, 회전 footprint, 공간 동기화 전 이동, 기록 Clear도 확인한다. 현장 아래로 실제 DropCargo 신호를 적용하면 같은 Apply에서 완공을 차단하고 다음 Decision에서 해당 실물의 회수 작업 하나가 생성되는 통합 사례도 확인했다.

초기 완공 차단 테스트 한 건은 빈 태그인 DestroyItemRequest에 SetComponentData를 호출하여 실패했다(초기 6/7). 활성화만 하도록 테스트 초기화를 수정하고 최종 7/7을 확인했다. 최초 실패는 `clearance-status-initial.json`에 보존한다. 제품 컴파일 실패는 없었다.

소스 검토에서 이전 완료 배정이 현재 수행자의 다른 배정 적재품을 정리 보류 근거로 사용할 수 있는 조건을 확인해 연결 검사를 보완했다. 수정 후 실제 완료 결과를 미소비 상태로 유지하고 같은 수행자에 새 배정/실물을 연결하는 회귀로 Completed 유지·이전 결과 소비 뒤 이전 배정만 삭제·새 실물/연결 보존을 확인했다. 수정 이전 버전으로 이 사례를 실행한 증거와는 구분한다.

## 검증 한계

명시적인 ECS 테스트 프리팹 DB·수행자·행동 신호·경로 결과로 검증했다. 실제 드론 등록/생성·이동·배터리·충전·경로 계산·관측 위치 SoT 연결, 초기 공통 능력 게시/연구 Writer, 실제 SubScene 베이킹·화면·성능, 전체 EditMode와 Play Mode는 검증하지 않았다. 이 후속 수행부 범위를 완료된 드론 운송 기능으로 취급하지 않는다.

이전 파일 통합과 이전 틱 인계의 실행 근거는 [시스템 파일 통합](SystemFileConsolidation-Verification.md)과 [소유자 통합](DroneTransferOwnerConsolidation-Verification.md)에 보존한다. 기존 staged/unstaged 작업을 유지했고 이번 작업에서 스테이징·커밋하지 않았다.
