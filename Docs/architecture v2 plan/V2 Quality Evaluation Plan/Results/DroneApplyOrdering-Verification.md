# 드론 StateApply 실행 순서 최소화

## 변경 범위

2026-10-05 사용자 승인에 따라 두 드론 시스템의 중복되거나 현재 데이터 가시성을 보장하지 않는 실행 순서 속성 8개를 제거했다.

- [DroneTaskAssignmentPublishSystem](../../../../Assets/Scripts/Systems/5_StateApply/DroneTaskAssignmentPublishSystem.cs): Construction Lifecycle, Building Lifecycle, Building Storage Apply, Item Ownership Apply에 대한 `UpdateAfter` 4개를 제거했다. 일반 Apply 이후 실행하는 `OrderLast`와 EndStateApply ECB 재생 전에 기록하는 `UpdateBefore`를 유지한다.
- [DroneTaskLifecycleApplySystem](../../../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs): Storage Apply, Construction Lifecycle, Building Lifecycle에 대한 `UpdateAfter`와 Publish에 대한 `UpdateBefore`를 제거했다. 최신 소유권을 검사하는 `UpdateAfter(ItemOwnershipApplySystem)`을 유지한다. 기존 Storage→Ownership 순서와 Publish의 OrderLast가 중복 관계를 대신 보장한다.

실행 단계, 후보/예약량 계산, 종료 상태, 최종 배정 재검사·롤백과 ECB 재생 시점은 기존 계약을 따른다. Decision의 partial 파일 구성은 이번 순서 정리의 변경 대상이 아니다.

## 데이터 의존 근거

Lifecycle은 종료 의도의 현재 유효성을 다시 판단하면서 아이템 소유권을 읽는다. ItemOwnershipApplySystem은 TransferOwnershipRequest를 소비해 ItemOwnership을 직접 갱신하므로 이 시스템 이후 실행해야 한다.

Publish는 최신 보관량·소유권·경로·현장 잔량을 읽지만 `OrderLast`가 이미 일반 StateApply 시스템 이후 실행을 보장한다. ECB도 OrderLast이므로 Publish→ECB 관계는 명시적으로 유지한다.

Construction/Building Lifecycle은 현장 완공·철거·반환·삭제를 EndStateApply ECB에 기록한다. 두 시스템 뒤에 실행한다는 선언으로 재생 전의 완료 결과를 볼 수 없으므로 개별 선행 제약은 제거했다. EndStateApply 이후 대상이 바뀌는 경우 다음 Decision에서 다시 검사하는 계약을 유지한다.

## 검증

Unity 6000.4.11f1의 연결된 편집기에서 최종 컴파일과 선별 EditMode를 실행했다. 원본 출력 위치는 프로젝트의 `Logs/Codex/DroneApplyOrdering-20261005/`다.

| 확인 | 최종 결과 | 기록 |
| --- | --- | --- |
| 컴파일 | completed, failed:false, compilationFailed:false, errors:[] | `recompile-final.json`, `recompile-status-final.json` |
| DroneTaskSchedulingTests | total 32, passed 32, failed/skipped/inconclusive 0 | `scheduling-run.json`, `scheduling-status.json` |
| 편집기 | ready, compiling:false, domainReloadInProgress:false, playMode:stopped | `editor-status.json` |
| 어셈블리 | 런타임·Editor DLL 모두 해당 최신 C# 소스보다 최신 | `final-static-check.json` |
| 차이·소스 검사 | git diff --check 종료 0, 대상 소스 공백/충돌 표시 0 | `diff-check.txt`, `final-static-check.json` |
| 관련 문서 링크 | 로컬 링크 98개 누락 0 | `document-links.json` |

`git diff --check`에는 LF→CRLF 안내 4건이 있다. 기존 staged/unstaged 작업은 유지했고 이번 작업에서 커밋하거나 스테이징하지 않았다. 기존 31개와 신규 소유권 순서 사례 1개를 포함한 이번 실행은 총 32개이며 이전 결과를 합산한 수치가 아니다.

기존 [DroneTaskSchedulingTests](../../../../Assets/Editor/Tests/DroneTaskSchedulingTests.cs)는 실제 StateApplyGroup을 정렬하고 실행하여 종료·예약·연결과 Publish→ECB 공개 경계를 검사한다. `SortedStateApply_RechecksTaskClosureAfterOwnershipReturnsItemToWorld`를 추가하여 소유권 반영 후 Lifecycle이 종료 의도를 다시 검사하는 실제 데이터 사례를 검증한다.

신규 사례는 보관 상태의 회수품에 Decision이 종료 의도를 만든 뒤, 버퍼 제거·위치 갱신·활성 Transfer 요청을 준비한다. 기존 Lifecycle/Publish/ECB 뒤에 실제 Ownership 시스템을 등록하고 그룹을 정렬해 실행한다. 소유권이 월드로 바뀌고 요청·렌더 태그가 소비되면서 회수 작업이 Open으로 살아 있는지 확인한다. Ownership보다 Lifecycle이 먼저 실행되면 작업이 닫혀 삭제되므로 순서 오류를 실제 상태로 검출한다. 보관 버퍼 제거·위치는 테스트가 준비했으며 실제 Storage 입출고 전체 흐름을 검증한 것은 아니다.

실제 드론 이동·경로 계산·실물 인계, 전체 EditMode와 Play Mode는 이번 검증 범위에 포함하지 않는다. 앞선 Execution 분리 당시 근거는 [기존 검증 기록](DroneExecutionSplit-Verification.md)에 보존한다.
