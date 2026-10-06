# 드론 작업 생성과 배정 2단계 변경 및 검증

## 범위와 결과

이 문서는 최초 2단계 구현 당시 기록이다. 이후 사용자 피드백으로 Placement 의존과 Command 작업 생성을 제거하고 Decision의 상태 반영을 분리했다. 이어 생성·경로 명령을 Execution으로 옮겼다. 현재 구조는 [Execution 분리 검증](DroneExecutionSplit-Verification.md)을 따르며 중간 변경은 [phase 분리 검증](DronePhaseSeparation-Verification.md)에 남긴다. 아래의 당시 테스트 결과와 처리 시점은 과거 실행 근거로 보존한다.

2026-10-04 승인된 당시 구현 계획의 2단계를 구현했다. 공급·회수 작업 생성, 외부 경로 평가 요청·결과 조회, 후보 선택, 현장 공급량 예약과 하위 배정 공개를 연결했다.

최종 Unity 컴파일은 `completed`, `failed=false`, `errors=[]`이다. 선별 EditMode는 새 작업 배정 27개와 기존 배치 7개, **총 34/34 통과**이며 실패·건너뜀·Inconclusive는 0이다. 실제 드론 생성·등록·이동·경로 계산·충전·행동 실행·실물 인계·바닥 완공 차단 갱신은 구현하지 않았다.

## 구현 내용

- [BuildingPlacementCommandSystem](../../../../Assets/Scripts/Systems/1_Command/BuildingPlacementCommandSystem.cs)은 현장과 같은 EndCommand에 필요한 품목별 공급 작업을 생성한다. [DroneTaskCreationUtility](../../../../Assets/Scripts/Common/DroneTaskCreationUtility.cs)와 DroneTaskSequence가 공급·회수의 생성 순번을 공유한다.
- 당시 `DroneTaskCreationCommandSystem`은 현장 footprint 아래 월드 실물과 DroneRecoveryPending 표시 실물에 회수 작업을 만들었다. 새 현장은 다음 Command부터 검사했다. 이후 `DroneTaskCreationApplySystem`을 거쳐 생성 판단은 Decision으로, 명령 소비는 [DroneTaskExecutionSystem](../../../../Assets/Scripts/Systems/4_Execution/DroneTaskExecutionSystem.cs)으로 분리했으며 현재 동작/시점은 위 최신 검증 기록을 따른다.
- [DroneTaskDecisionSystem](../../../../Assets/Scripts/Systems/2_Decision/DroneTaskDecisionSystem.cs)은 입력이 갖춰진 수행자의 경로 요청과 후보를 만든다. 같은 작업의 유효 공급원/보관 후보 경로 평가가 모두 끝난 뒤 전체 실제 거리로 선택한다. 관측·목적지·평가 revision이 오래된 결과, 도달 불가·무효 거리, 사용 중인 작업이 없는 요청을 배정에 사용하지 않는다.
- [ConstructionSupplyReservationSystem](../../../../Assets/Scripts/Systems/3_Reservation/ConstructionSupplyReservationSystem.cs)은 현장 잔여량만 확보한다. 기존 및 같은 틱의 수행자·회수 실물 배정을 함께 확인한다. 다른 수행자의 앞선 예약으로 후보가 막히면 다음 가능한 후보도 검토한다.
- [DroneTaskAssignmentPublishSystem](../../../../Assets/Scripts/Systems/5_StateApply/DroneTaskAssignmentPublishSystem.cs)은 최종 재검사 후 EndStateApply에 배정·개별 예약·수행자 연결을 공개한다. 실물이나 수행자의 관측 위치는 바꾸지 않는다.

신규 후보 버퍼의 CommittedQuantity는 아직 Assignment로 공개되지 않은 예약의 근거다. 현장 합계는 개별 예약과 미공개 예약을 포함한다. Publish는 `max(0, Required - Delivered - max(0, Reserved - 자기 예약))`으로 부족량을 다시 확인하고, 충족된 현장이나 무효/축소된 배정의 예약을 되돌린다. 다음 Decision은 공개되지 않은 이전 후보도 정리한다.

공급원 재고·보관 공간 예약은 만들지 않았다. 벨트의 Decision·Reservation·입출고·FIFO 구현은 수정하지 않았다. 등록된 최소 수행자 및 외부 경로 결과가 없으면 대기한다. 적재가 없는 무효 배정은 예약·수행자 연결을 해제하고, 적재가 있으면 Retargeting 상태로 보존한다. 실제 적재품 재배정과 인계는 후속이다.

## 추가 확인한 우선순위

구현 중 배치 요청의 PlacementStamp와 실제 작업 생성 순서가 어긋날 수 있음을 확인했고 사용자가 다음 규칙을 선택했다.

1. 공급 후보의 선두: 현장 PlacementStamp, 이어 CreationSequence.
2. 회수 후보의 선두: CreationSequence.
3. 두 선두 작업의 경쟁: CreationSequence.

공급 A가 생성 1/Stamp 20, 회수가 생성 2, 공급 B가 생성 3/Stamp 10인 경우 공급 선두는 B이고 회수가 B보다 먼저 배정된다. 이 혼합 사례를 회귀 테스트로 고정했다. 전역 작업을 생성 순서 하나로 정렬하는 계약으로 설명하지 않는다.

## 선별 테스트

| 필터 | Total | Passed | Failed | Skipped | Inconclusive |
| --- | ---: | ---: | ---: | ---: | ---: |
| DroneTaskSchedulingTests | 27 | 27 | 0 | 0 | 0 |
| Phase7PlacementCommandTests | 7 | 7 | 0 | 0 | 0 |

[새 테스트](../../../../Assets/Editor/Tests/DroneTaskSchedulingTests.cs)는 실제 여섯 단계 그룹과 두 ECB 경계에 필요한 시스템을 등록한다. 테스트가 최소 수행자와 경로 결과를 명시적으로 제공한다. 전체 제품 시스템이나 실제 수행부를 실행한 테스트는 아니다.

확인한 주요 사례는 다음과 같다.

- 배치 시 전체 품목 작업의 EndCommand 공개, 회수 작업 중복 및 일반 월드/벨트 실물의 전역 자동 회수 방지.
- 경로 미응답·기본 결과·도달 불가·무효 거리·오래된 관측/목적지/revision에 대한 대기.
- 수행 불가인 오래된 작업 건너뛰기, 확정한 혼합 우선순위, 전체 경로 거리 및 동률 PlacementStamp.
- 두 수행자가 같은 공급원의 실물 3개를 참고하여 현장 필요량 5개를 3+2로 배정할 수 있고 공급원 실물은 그대로 남는 무예약 계약.
- 현장 과예약 방지, EndStateApply 전후의 배정 가시성, 회수 실물의 단일 수행자 배정.
- 공개 직전 현장이 충족되었을 때 배정 거부와 예약 전량 반환.
- 공급원 소실·현장 취소에 따른 기존 배정 예약/수행자 연결 해제.
- 현장이 없어져 회수 작업이 종료되면 실물·보관 건물이 살아 있어도 경로 요청이 남지 않는 회귀.

## 실행 근거와 수정 과정

설치된 Unity CLI의 연결된 Editor를 사용했고 호출은 순차 실행했다. 최종 Editor는 `ready`, `compiling=false`, `domainReloadInProgress=false`, `playMode=stopped`였다. Assembly-CSharp.dll과 Assembly-CSharp-Editor.dll은 각각 해당 최신 소스보다 이후에 생성된 것을 확인했다.

추가 테스트에서 CS1612 버퍼 반환값 수정 오류가 발생하여 로컬 버퍼 변수로 수정했다. 이 컴파일 실패 뒤 이전 테스트 어셈블리로 실행된 22개 결과는 최종 검증에서 제외했다. 수정 후 최신 어셈블리의 26개가 통과했고, 고아 경로 요청 회귀를 보강한 최종 27개와 기존 배치 7개를 다시 확인했다. 중간 결과를 최종 통과 수에 합산하지 않는다.

원본 JSON은 프로젝트 로컬 `Logs/Codex/DroneSchedulingStep2-20261004/`에 보관했다.

- `recompile-status-4.json`: 최종 컴파일.
- `scheduling-status-final.json`: 최종 27개 작업 배정 결과.
- `placement-status.json`: 기존 배치 7개 결과.
- `editor-status-final.json`, `assembly-freshness-final.json`: Editor 상태와 런타임/테스트 어셈블리 최신성.
- `scheduling-status-invalid-build.json`: 제외한 이전 어셈블리 실행 기록.

기존 작업 트리와 스테이징 변경을 보존했다. 이번 작업은 커밋하거나 스테이징하지 않았다. 데이터 정의는 94개이며 [컴포넌트 색인](../../../CodeMemory/Components/README.md)에 누락이 없다.

변경 관련 문서의 로컬 링크 383개가 모두 유효했고 새 C# 11개(런타임 10개, 테스트 1개)의 meta 존재·GUID 유일성을 확인했다. `git diff --check`의 공백 오류는 없었다. Git의 LF→CRLF 정규화 안내는 있었으며 컴파일·테스트 오류와 구분한다.

## 실행 한계

이 검증은 작업 생성·선택·현장 예약·배정 공개의 범위다. 벨트 코드가 그대로라는 소스 확인과 공급원 실물이 변하지 않는 테스트를 실제 벨트와 드론의 물리적 인계 동작 검증으로 확대하지 않는다.

실제 드론 이동·배터리·충전·경로 계산·행동 실행, 자재 수령/소비, 부분 수집·부분 보관의 실물 처리, 바닥 아이템 회수 후 완공 차단 해제, 장면 입력·렌더링·프리팹 베이킹·성능은 이번에 검증하지 않았다. 전체 EditMode와 Play Mode도 실행하지 않았다. 다음 단계는 실제 실물 인계와 결과 처리다.
