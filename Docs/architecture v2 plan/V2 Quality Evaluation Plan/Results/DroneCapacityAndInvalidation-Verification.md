# 드론 공통 적재량과 무효화 의도 계약

## 승인 내용과 변경 범위

2026-10-05 사용자 결정에 따라 무효화 의도의 이름을 변경하고, 모든 드론의 최대 적재량을 공통 상태로 옮겼다. 연구로 바뀐 적재량은 새 배정부터 적용하고 이미 배정한 수량·예약·적재품은 소급 변경하지 않는다. Candidate 버퍼의 역할은 유지하면서 Decision의 후보 결과와 이후 공개 대기 기록을 구분해 설명했다.

- [DroneTaskInvalidationDecisionElement](../../../../Assets/Scripts/Components/DroneLogistics/DroneTaskInvalidationDecisionElement.cs): 기존 `DroneTaskLifecycleDecisionElement`와 Kind enum을 이름 변경했다. `CloseTask`·`InvalidateAssignment` 값과 무효화 판단/반영 동작은 유지하며 기존 `.meta` GUID `1803cf8f128543c1ae6757cf18fd4322`를 보존했다.
- [DroneWorker](../../../../Assets/Scripts/Components/DroneLogistics/DroneWorkerComponents.cs): 수행자 식별 태그로 변경하고 드론별 CarryingCapacity를 제거했다. 실제 적재품은 기존 StoredItemElement/ItemOwnership과 DroneCargoState가 관리한다.
- [DroneCapacityState](../../../../Assets/Scripts/Components/DroneLogistics/DroneCapacityState.cs): World 단일 일반 IComponentData로 공통 CarryingCapacity를 제공한다. 초기 능력 게시 및 연구 Writer는 후속이다. 이번 구현에서 임의 초기값이나 연구 시스템을 추가하지 않았다.
- 당시 `DroneTaskCandidateElement`는 Decision이 만든 배정 후보와 Reservation의 Selected/CommittedQuantity, Publish의 Published가 함께 유지되는 기록이라는 설명을 보완했다. 이후 이름을 [DroneTaskCandidateDecisionElement](../../../../Assets/Scripts/Components/DroneLogistics/DroneTaskCandidateDecisionElement.cs)로 변경했으며 데이터 역할은 유지한다.

## 소유자·단계와 가시성

공통 적재량 원본은 DroneCapacityState singleton이다. Decision, Reservation, Execution, Publish가 각각 ReadOnly EntityQuery를 캐시하고 업데이트에서 현재 값을 읽는다. [DroneSchedulingUtility](../../../../Assets/Scripts/Common/DroneSchedulingUtility.cs)의 수행자·경로·후보 검사는 호출자가 읽은 같은 값을 명시적으로 전달받는다. 개별 드론이나 별도 static 필드에 능력치를 복제하지 않는다.

공통 상태가 없거나 적재량이 0 이하이면 새로운 배정을 허용하지 않는다. 시스템 전체를 RequireForUpdate나 조기 반환으로 막지 않으므로 작업 생성, 무효화 의도 작성, 낡은 경로 정리, 기존/미공개 예약 해제는 계속 실행된다. Publish는 현재 능력을 사용할 수 없는 후보를 거절하고 확보했던 현장 예약을 되돌린다.

공통 값 변경은 다음 새 후보 계산·배정에서 읽는다. 이미 생성한 DroneTaskAssignment.AssignedQuantity, 개별 ConstructionSupplyReservation과 실제 적재품은 변경하지 않는다. 새 배정 수량은 현장 잔량·현재 재고·승인 후보 수량과 공통 최대량으로 제한되며, 능력 증가만으로 이전 후보/배정의 수량을 자동 확대하지 않는다.

Candidate의 생명주기는 `Decision 후보 작성 → Reservation 선택/현장 예약 → Publish 재검사/ECB 기록 → EndStateApply 배정·개별 예약 실체화`다. Published는 ECB 기록 여부이며 즉시 엔티티 생성 완료를 뜻하지 않는다. 미공개 CommittedQuantity가 남으면 다음 틱 Reservation 정산까지 버퍼를 보존하므로 순수한 한 틱 Decision 결과보다 수명이 길 수 있다.

## 실행 검증

Unity 6000.4.11f1 연결 편집기에서 최종 컴파일 후 `run_tests --mode editor --filter DroneTaskSchedulingTests --async_tests true`를 실행했다. 원본 출력은 프로젝트의 `Logs/Codex/DroneCapacityAndInvalidation-20261005/`에 보관했다.

| 확인 | 최종 결과 | 기록 |
| --- | --- | --- |
| 컴파일 | completed, failed:false, compilationFailed:false, errors:[] | `recompile.json`, `recompile-status.json` |
| 선별 EditMode | total 36, passed 36, failed/skipped/inconclusive 0 | `scheduling-run.json`, `scheduling-status.json` |
| 편집기 | ready, compiling:false, domainReloadInProgress:false, playMode:stopped | `editor-status.json` |
| 어셈블리 | 런타임·Editor DLL 모두 해당 최신 C# 소스보다 최신 | `assembly-freshness.json` |
| 컴포넌트·메타 | ECS 타입 98개, 색인 누락 0, 변경 메타 GUID 각각 1개, 구 파일/메타 제거 | `component-meta-check.json` |
| 문서·차이 검사 | 관련 문서 9개의 로컬 링크 413개 누락 0, git diff --check 종료 0, 대상 소스 공백/충돌 및 구 심볼 0 | `final-static-check.json`, `diff-check.txt` |

`git diff --check`에는 LF→CRLF 안내 9건이 있다. 기존 staged/unstaged 작업은 유지했으며 이번 작업에서 스테이징하거나 커밋하지 않았다.

[DroneTaskSchedulingTests](../../../../Assets/Editor/Tests/DroneTaskSchedulingTests.cs)의 기존 32개와 신규 4개 조합을 이번 실행에서 모두 확인했다.

- 공통 적재량 2에서 기존 배정·예약 2를 만든 뒤 운반 중 실물 2개를 명시적으로 준비하고 공통량을 5로 변경했다. 새 수행자의 새 배정·예약은 5이며 기존 배정 수량·revision·예약·연결·실물 소유권·출처는 유지됐다.
- Reservation에서 선택한 후보가 Publish 전에 공통 상태를 잃거나 0으로 바뀌는 두 경우에 미공개 현장 예약 전량 롤백과 배정 거절을 확인했다.
- 공통 상태 부재 시 신규 배정 대기를 확인했다. 기존 회수 작업 생성 사례도 능력·수행자 준비 없이 작업 생성이 진행되는 것을 확인한다.
- 기존 다중 수행자, 우선순위, 경로 스냅샷, 단계별 상태 변경, 소유권 순서와 ECB 공개 회귀를 유지했다.

## 검증 한계

연구 진행/완료 및 공통 능력 초기화·게시자는 아직 없다. 테스트는 singleton 변경과 운반 중 적재품을 직접 준비했으며 실제 연구나 수집·이동·인계를 실행한 증거가 아니다. 전체 EditMode, Play Mode, 실제 프리팹 베이킹·화면·성능은 검증하지 않았다.

현재 계약은 [드론 컴포넌트 문서](../../../CodeMemory/Components/DroneLogistics.md)를 따른다. 이 검증 당시 후속 구현 범위는 당시 구현 계획을 기준으로 했다. 앞선 단계의 실행 근거는 [Execution 분리](DroneExecutionSplit-Verification.md)와 [Apply 순서 최소화](DroneApplyOrdering-Verification.md)에 보존한다.
