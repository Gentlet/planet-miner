# 드론 생성·경로 명령의 Execution 분리 검증

이 문서는 Execution 분리 당시 실행 기록이다. 이후 중복·불필요한 StateApply 순서 속성을 제거했으며 현재 순서와 후속 실행 근거는 [드론 Apply 순서 최소화 검증](DroneApplyOrdering-Verification.md)을 따른다. 아래 당시 컴파일·테스트 결과는 보존한다.

## 범위와 결과

2026-10-05 사용자 승인에 따라 기존 StateApply의 작업 생성과 경로 요청 처리를 Decision의 의도 작성과 Execution의 명령 소비로 분리했다. 작업·배정 상태, 수행자 연결, 삭제 보류와 최종 배정 공개는 StateApply에 유지했다.

최종 Unity 컴파일은 `completed`, `failed:false`, `compilationFailed:false`, `errors:[]`이다. 선별 EditMode `DroneTaskSchedulingTests`는 **31/31 통과**했고 실패·건너뜀·Inconclusive는 모두 0이다. 실제 드론 이동·경로 계산·자재 인계는 구현하거나 실행 검증하지 않았다.

## 책임 배치

| 단계 | 소유자와 처리 |
| --- | --- |
| Decision | [DroneTaskDecisionSystem](../../../../Assets/Scripts/Systems/2_Decision/DroneTaskDecisionSystem.cs)이 생성·종료·경로 의도와 배정 후보를 작성한다. [Creation partial](../../../../Assets/Scripts/Systems/2_Decision/DroneTaskDecisionSystem.cs)은 같은 시스템의 공급·회수 필요 탐색이다. 원본 작업·배정·예약·수행자 연결과 ECB를 변경하지 않는다. |
| Reservation | [ConstructionSupplyReservationSystem](../../../../Assets/Scripts/Systems/3_Reservation/ConstructionSupplyReservationSystem.cs)이 일반 무효 예약·이전 미공개 예약을 해제하고 신규 현장 수량을 확보한다. |
| Execution | [DroneTaskExecutionSystem](../../../../Assets/Scripts/Systems/4_Execution/DroneTaskExecutionSystem.cs)이 생성 의도에 순번을 부여하고 작업 생성, 경로 요청 Create/Retain/Remove 명령을 소비한다. 구조 변경은 EndStateApply ECB에 기록한다. |
| StateApply | [DroneTaskLifecycleApplySystem](../../../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs)이 종료 의도의 대상·revision·현재 조건을 검사하고 상태·연결을 반영한다. 예약·행동 결과·참조가 남으면 삭제를 보류한다. [DroneTaskAssignmentPublishSystem](../../../../Assets/Scripts/Systems/5_StateApply/DroneTaskAssignmentPublishSystem.cs)은 최종 재검사·수량 조정/롤백과 배정 공개를 유지한다. |
| EndStateApply | 기록한 작업·경로·배정의 생성/삭제를 재생한다. 생성된 작업·경로 요청은 다음 시뮬레이션 Decision부터 조회한다. |

`DroneTaskCreationApplySystem`은 `DroneTaskExecutionSystem`으로 이동하고 기존 `.meta` GUID `466b934ec5b742d0b2d4de4899091b4e`를 유지했다. Execution에는 다른 StateApply 시스템과의 실행 순서 속성이 없다. Lifecycle/Publish는 최신 보관·소유권·현장 상태를 반영하는 경계이므로 기존 StateApply 순서를 유지한다.

## 의도와 소비 경계

- [DroneTaskCreationDecisionElement](../../../../Assets/Scripts/Components/DroneLogistics/DroneTaskCreationDecisionElement.cs)는 공급·회수 종류, 대상, 품목과 회수 이유를 전달한다. Execution은 대상 생존·타입과 중복을 확인하고, 명령 소비 후 버퍼를 비운다. 필요 작업의 전체 탐색은 Decision이 담당한다.
- 당시 `DroneTaskLifecycleDecisionElement`는 작업 종료 또는 배정 무효화와 배정 revision을 전달했다. 이후 이름을 [DroneTaskInvalidationDecisionElement](../../../../Assets/Scripts/Components/DroneLogistics/DroneTaskInvalidationDecisionElement.cs)로 변경했다. Lifecycle은 최신 적재품을 보존하고, 미정산 예약·행동 요청/결과의 정리 근거를 삭제하지 않는다. 공통 적재량 계약을 포함한 후속 변경은 [현재 검증 기록](DroneCapacityAndInvalidation-Verification.md)을 따른다.
- [DroneRouteDecisionElement](../../../../Assets/Scripts/Components/DroneLogistics/DroneRouteDecisionElement.cs)는 Create/Retain/Remove를 명시한다. Execution이 경로 스냅샷을 검사하고 생성·삭제를 기록한 뒤 의도를 소비한다. 실제 경로 계산은 외부 수행부의 후속 책임이다.
- 생성 기록 뒤 StateApply에서 대상 위치·소유권 등이 바뀔 수 있다. 다음 Decision이 작업·경로의 현재 유효성을 검사하여 오래된 대상을 배정 후보에서 제외하고 종료/경로 삭제 의도를 만든다. 같은 틱에 생성부터 배정까지 끝낼 필요는 없다는 기존 결정을 따른다.
- 현장 공급 수량만 예약한다. 공급원 재고·보관 공간 예약과 벨트 입출고 정책은 추가하지 않았다. Publish의 기존 최종 재고·공간·현장 검사와 실패 시 예약 롤백은 유지했다.

## 실행 검증

Unity 6000.4.11f1의 연결된 편집기에서 최종 재컴파일 후 `run_tests --mode editor --filter DroneTaskSchedulingTests --async_tests true`로 실행했다. 원본은 프로젝트의 `Logs/Codex/DroneExecutionSplit-20261005/`에 있다.

| 확인 | 최종 결과 | 기록 |
| --- | --- | --- |
| 컴파일 | completed, failed:false, compilationFailed:false, errors:[] | `recompile-final.json`, `recompile-status-final.json` |
| 선별 EditMode | total 31, passed 31, failed/skipped/inconclusive 0 | `scheduling-run.json`, `scheduling-status.json` |
| 편집기 | ready, compiling:false, domainReloadInProgress:false, playMode:stopped | `editor-status-final.json` |
| 런타임 어셈블리 | 최신 런타임 C#보다 Assembly-CSharp.dll이 최신 | `assembly-freshness.json` |
| 테스트 어셈블리 | 최신 Editor C#보다 Assembly-CSharp-Editor.dll이 최신 | `assembly-freshness.json` |
| 컴포넌트·메타 | ECS 타입 97개, 컴포넌트 인덱스 누락 0, 변경 대상 메타 GUID 각각 1개 | `component-meta-check.json` |
| 문서·차이 검사 | 관련 문서 10개의 로컬 링크 405개 누락 0, git diff --check 종료 0, 대상 소스 공백/충돌 표시 0 | `final-static-check.json`, `diff-check.txt` |

`git diff --check`에는 LF→CRLF 변환 안내 7건이 있으며 공백 오류는 없다. 기존 staged/unstaged 작업은 유지했고 이번 작업에서 커밋하거나 스테이징하지 않았다.

[DroneTaskSchedulingTests](../../../../Assets/Editor/Tests/DroneTaskSchedulingTests.cs)에서 확인한 주요 경계:

- Decision에서는 순번을 발급하지 않고 Execution에서 발급한다. Execution을 반복 호출해도 작업·경로 명령이 중복 기록되지 않는다.
- 작업·배정은 EndStateApply 전에는 실체화하지 않으며 새 작업은 다음 Decision부터 배정 판단한다.
- 무효 배정의 예약은 Reservation에서 해제하고 작업·배정 상태와 수행자 연결은 StateApply에서만 변경한다. Execution은 이 원본 상태를 변경하지 않는다.
- 생성 또는 경로 명령 기록 이후 회수품의 위치/소유권이 바뀌는 4개 조합에서 다음 Decision이 오래된 작업·경로를 배정에 사용하지 않는다.
- 기존 우선순위, 전체 경로 거리 비교, 여러 수행자의 현장 잔량 예약, 회수 중복 방지, 경로 revision/거리 유효성, 최종 공개 전 현장 충족 시 롤백을 유지한다.

## 검증 한계와 후속

테스트는 준비한 ECS 월드와 경로 결과를 사용한 선별 EditMode 검증이다. 전체 EditMode, Play Mode, 실제 SubScene 베이킹·화면·성능·실제 드론 동작은 실행하지 않았다. 이전 배치 테스트 7개의 과거 통과 기록을 이번 31개 실행 결과에 합산하지 않는다.

실제 수행자 등록·경로 계산·이동·충전, 수집/공급/보관/방출의 실물 인계, 현장 바닥 아이템 차단 갱신은 당시 구현 계획의 후속 범위였다. 현재 컴포넌트 계약은 [드론 작업 관리 문서](../../../CodeMemory/Components/DroneLogistics.md), 앞선 분리 당시 실행 근거는 [이전 검증 기록](DronePhaseSeparation-Verification.md)을 따른다.
