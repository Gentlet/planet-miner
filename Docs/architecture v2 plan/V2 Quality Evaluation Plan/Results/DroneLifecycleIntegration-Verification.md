# 드론 작업 종료의 실제 명령·완공 연결 검증

## 결과와 범위

2026-10-06 5단계의 연결 사례를 [DroneLifecycleIntegrationTests](../../../../Assets/Editor/Tests/DroneLifecycleIntegrationTests.cs)에 추가했다. 실제 Command→Decision→Reservation→Execution→StateApply→Synchronization 및 EndCommand/EndStateApply를 등록하고 정렬한 fixture에서 실행했다. 초기 현장·재고·수행자와 외부 거리 결과·관측·행동 신호는 테스트가 제공한다.

Unity 컴파일은 `completed`, `failed:false`, `compilationFailed:false`, `errors:[]`다. 신규 집중 EditMode **4/4 통과**이며 실패·건너뜀·Inconclusive는 0이다. 이번 단계에서는 제품 코드를 변경하지 않았다.

앞선 안전 배출 단계의 [142개 검증](DroneSafeDropAndJournalRemoval-Verification.md)은 당시 실행 근거로 유지한다. 이번 신규 4개와 동일 실행인 것처럼 합산하지 않는다.

## 검증한 연결

| 사례 | 실제 처리와 확인 |
| --- | --- |
| 현장 취소 | 실제 최초 배정·Collect 후 CancelConstructionRequest를 처리한다. EndCommand에서 도착 자재가 원래 현장 위치의 월드 아이템으로 반환되고 현장/요청이 삭제된다. 예약 0·Retargeting을 거쳐 다른 현장으로 재배정하고 Supply로 실제 실물·도착량·예약을 정산한다. 이전 Collect 결과와 기존 배정 엔티티가 유지되는 것도 확인한다. |
| 실제 완공 후 방어적 재배정 | 정상 공급 수행자는 D/R/Publish→Collect→Supply→Construction/EndStateApply로 완공한다. 같은 현장을 향한 별도 기존 운반 배정의 예약 0 상태만 초기 fixture로 준비하고, 현장 소실 후 무효화·Retargeting·다른 현장 공급은 실제 시스템으로 진행한다. |
| 수집 전 공급원 철거 | 최초 배정과 예약 후 실제 DemolishBuildingRequest를 처리한다. Command 승인 상태부터 공급원이 제외되고 EndStateApply에 철거된다. 빈 수행자의 이전 예약 해제·연결 정리 후 다른 공급원에서 새 배정·Collect가 이루어지며 실물과 개별 예약을 확인한다. |
| 회수품 운송 중 보관처 철거 | 실제 회수 작업 배정·Collect로 받은 Recovery 적재품을 유지한 채 목적 보관처를 철거한다. 대체 보관처로 Direct 재배정하고 Store로 저장한다. 회수품 출처·동일 실물·예약 0·배정 revision을 확인한다. |

## 완공 사례의 불변식과 한계

정상 예약은 현장의 필요량−도착량−다른 예약량을 넘지 않고 공급 인계도 자신의 남은 예약량을 넘지 않는다. 따라서 다른 드론에 유효한 양수 예약과 운반품이 남아 있는 상태에서 한 수행자가 먼저 모든 자재를 채워 완공하는 정상 흐름은 만들 수 없다.

완공 사례는 **예약이 이미 0인 기존 운반 배정**의 방어 연결로 명시했다. 그 배정의 초기 등록만 수동이며 Closed/Cancelled/Retargeting이나 실제 현장 삭제를 미리 설정하지 않았다. 정상 완료 흐름의 불변식을 깨뜨려 유효 예약 경합 사례처럼 취급하지 않는다.

## 실행 근거

Unity 6000.4.11f1 연결 Editor에서 컴파일 후 신규 클래스만 실행했다. 원본은 `Logs/Codex/DroneLifecycleIntegration-20261006/`에 보존한다.

| 확인 | 결과 | 기록 |
| --- | --- | --- |
| 컴파일 | completed, failed:false, compilationFailed:false, errors:[] | `recompile.json`, `recompile-status.json` |
| DroneLifecycleIntegrationTests | 4/4, 실패·건너뜀·Inconclusive 0 | `integration-run.json`, `integration-status.json` |
| Editor | ready, compiling:false, domainReloadInProgress:false, playMode:stopped | `editor-status.json` |
| 어셈블리·메타 | 런타임/Editor DLL 최신, 새 테스트 GUID 정상·유일, 결과 클래스 일치·새 파일 공백/충돌 0 | `freshness-and-meta.json` |
| 문서·차이 | 문서 6개·로컬 링크 417개 누락 0, git diff --check 종료 0 | `static-check.json`, `diff-check.txt` |

취소는 EndCommand에서 삭제되어 같은 틱의 예약 정산/무효화가 가능하다. 완공은 EndStateApply에서 현장이 소실되므로 다른 운반 배정의 정산·재배정은 다음 Decision부터 확인한다. 철거는 Command의 승인 상태부터 수집·보관을 막고 물리적 건물 삭제는 EndStateApply에 확정한다. 이 가시성 차이를 테스트에서 각 경계로 확인한다.

실제 드론 생성·이동·충전·최단 경로 계산·관측 위치 SoT 연결·초기 공통 능력/연구 Writer는 후속이다. 실제 SubScene 베이킹·화면·성능, 전체 EditMode와 Play Mode는 실행하지 않았다. 명시적 경로 결과/행동 신호를 실제 이동의 실행 근거로 보고하지 않는다. 기존 staged/unstaged 작업을 유지했고 추가 스테이징·커밋은 하지 않았다.
