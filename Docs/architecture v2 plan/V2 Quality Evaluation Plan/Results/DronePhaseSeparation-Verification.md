# 드론 작업 관리 phase 책임 분리 검증

이 문서는 최초 phase 분리 당시 기록이다. 2026-10-05 생성·경로 명령을 Execution으로 추가 분리했다. 현재 구조는 [Execution 분리 검증](DroneExecutionSplit-Verification.md)을 따르며 아래 당시 처리 시점과 실행 결과는 보존한다.

## 변경 범위

2026-10-04 사용자 피드백에 따라 배치 시스템의 드론 의존을 제거하고 자동 작업 생성·예약 정산·수명주기 반영을 단계별로 분리했다. 새 작업은 이번 StateApply에 생성하고 다음 시뮬레이션부터 배정 판단해도 된다는 결정을 반영했다. 실제 이동·자재 인계는 이번 변경에 포함하지 않았다.

- BuildingPlacementCommandSystem에서 드론 순번 엔티티 참조와 작업 생성 호출을 제거했다. 배치 요청·현장·자재 요구 처리만 남겼다.
- DroneTaskCreationCommandSystem을 당시 `DroneTaskCreationApplySystem`으로 이동하고 GUID를 유지했다. 공급·회수 작업과 순번을 이 시스템이 소유했다. 살아 있는 동일 현장·품목 root와 회수 실물을 중복 생성하지 않았다. 이후 생성 판단은 Decision으로, 명령 소비는 [DroneTaskExecutionSystem](../../../../Assets/Scripts/Systems/4_Execution/DroneTaskExecutionSystem.cs)으로 분리했다.
- [DroneTaskDecisionSystem](../../../../Assets/Scripts/Systems/2_Decision/DroneTaskDecisionSystem.cs)은 읽기 판단과 파생 후보/경로 의도만 작성한다. 예약 해제·작업/배정 상태 변경·수행자 연결 변경·ECB 생성/삭제 기록을 제거했다.
- [ConstructionSupplyReservationSystem](../../../../Assets/Scripts/Systems/3_Reservation/ConstructionSupplyReservationSystem.cs)은 일반 무효 예약과 이전 미공개 예약을 해제한 뒤 신규 수량을 확보한다. Decision은 해제 예정량을 ProjectedRemaining으로 읽기 계산하며 원본을 쓰지 않는다.
- [DroneTaskLifecycleApplySystem](../../../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs)은 작업 종료, 배정 상태, 수행자 연결 해제, 작업/배정 삭제 및 경로 요청 생성/삭제를 반영한다. 예약이 남은 배정은 삭제를 보류하여 다음 Reservation의 정산 근거를 보존한다.
- [DroneTaskAssignmentPublishSystem](../../../../Assets/Scripts/Systems/5_StateApply/DroneTaskAssignmentPublishSystem.cs)의 최종 재검사·공개 실패 롤백은 StateApply의 반영 경계로 유지한다.

## 현재 처리 순서

```text
Command: 외부 배치 요청 → EndCommand 현장·자재 요구 생성
Decision: 기존 작업의 후보·유효성·경로 의도 판단
Reservation: 일반 예약 해제·확보
StateApply: 수명주기 반영 → 자동 작업 생성 → 최종 배정 공개/롤백
EndStateApply: 새 작업·경로 요청·배정 및 삭제 실체화
다음 시뮬레이션: 새 작업을 Decision에서 조회·배정 판단
```

같은 시뮬레이션에서 자동 작업 생성과 배정을 모두 완료하지 않는다. 외부 경로 결과가 없으면 이후에도 대기한다. 공급 선두의 PlacementStamp→CreationSequence와 공급/회수 선두 간 CreationSequence 규칙, 공급원·보관 공간 무예약, 기존 벨트 처리 규칙은 유지했다.

## 실행 검증

| 필터 | Total | Passed | Failed | Skipped | Inconclusive |
| --- | ---: | ---: | ---: | ---: | ---: |
| DroneTaskSchedulingTests | 27 | 27 | 0 | 0 | 0 |
| Phase7PlacementCommandTests | 7 | 7 | 0 | 0 | 0 |

동작 변경 후 작업 배정 27개가 통과했다. 계약 주석 정리 후 최종 컴파일도 `completed`, `failed=false`, `errors=[]`로 완료했으며 기존 배치 7개가 통과했다. 선별 EditMode 합계는 34/34다. 새 테스트 파일을 늘리지 않고 기존 작업 배정 테스트를 변경했다.

확인한 핵심 경계는 다음과 같다.

- 배치 Command 이후에는 작업이 없고, EndStateApply 이후 전체 품목 작업이 보인다.
- 새 작업의 경로 판단은 다음 Decision에서 시작하며 실제 경로 요청 엔티티는 StateApply에서 게시된다.
- 무효 현장/공급원에서 Decision 실행 후 ECB만 재생해도 예약량·작업 상태·배정 상태·수행자 연결·엔티티가 그대로다.
- Reservation 이후에는 예약만 해제되고, StateApply 이후 배정 상태·연결·삭제가 반영된다.
- 기존 과예약 방지, 회수 중복 방지, 우선순위, 오래된 경로 결과 거부 및 고아 경로 요청 정리 회귀가 유지된다.

원본 기록은 로컬 `Logs/Codex/DronePhaseSeparation-20261004/`에 보관한다. `scheduling-status-1.json`, `placement-status.json`, `recompile-status-final.json`이 각 선별 테스트와 최종 컴파일 근거다. 마지막 Editor 상태 및 어셈블리 최신성은 별도 JSON으로 보관한다.

Editor는 ready·컴파일/리로드 없음·Play Mode stopped였고, 런타임/Editor 어셈블리가 각각 해당 소스보다 최신임을 확인했다. ECS 정의 95개가 색인에 포함되며 관련 로컬 링크 397개가 모두 유효했다. 이동/추가한 meta GUID도 유일하다. `git diff --check`의 공백 오류는 없었고 LF→CRLF 정규화 안내는 컴파일 오류와 구분한다.

## 한계

실제 수행자 등록·비행·경로 계산·충전·행동·자재 인계·완공 차단 갱신은 구현하거나 실행 검증하지 않았다. 테스트가 제공한 최소 수행자와 경로 결과를 실제 드론 동작의 증거로 해석하지 않는다. 전체 EditMode와 Play Mode는 실행하지 않았다. 이전 2단계 기록은 당시 구조의 실행 근거로 보존한다.
