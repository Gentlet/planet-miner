# 이전 틱 입력 기반 드론 인계 단계 분리

## 변경과 결과

2026-10-05 사용자 승인에 따라 드론 인계 판단·실행 대상을 이전 틱에 확정된 물류 상태로 제한했다. 이번 틱 새 입고·새 적재품·출고로 생긴 여유를 계획에 추가하지 않는다. 실제 반영에서는 현재 실물 원본을 재검사해 같은 틱 중복 소비를 막고, Command의 취소·철거 차단도 유지한다.

최종 컴파일 `completed`, `failed:false`, `compilationFailed:false`, `errors:[]`를 확인했다. 이번 최종 소스에서 선별 EditMode **75/75 통과**(인계 31, 기존 배정 36, 완공 8)이며 실패·건너뜀·Inconclusive는 0이다.

## 단계별 책임

| 단계 | 현재 소유자와 처리 |
| --- | --- |
| Decision | [DroneItemTransferDecisionSystem](../../../../Assets/Scripts/Systems/2_Decision/DroneItemTransferDecisionSystem.cs)이 신호의 행동 자격·초기 배정 상태를 읽기 검사한다. 실제 버퍼·Owner·예약·배정·행동 번호를 변경하지 않는다. |
| Execution | [DroneItemTransferExecutionSystem](../../../../Assets/Scripts/Systems/4_Execution/DroneItemTransferExecutionSystem.cs)이 OrderFirst에서 이번 물류 변경 전의 실물 후보 전체 ID, 수량 상한, 보관 슬롯의 기존 품목/수량을 준비한다. 실제 인계나 재고/공간의 영속 예약을 하지 않는다. |
| StateApply | 당시 DroneItemTransferApplySystem이 준비한 ID에 포함된 실물만 현재 소유권·생존·Destroy·대상·버전으로 재검사하여 반영했다. 실제 성공 수량으로 예약·도착량·배정과 결과를 정산했다. 현재 코드는 [DroneTaskLifecycleApplySystem](../../../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs)과 공통 ItemOwnership API로 이관했으며 [소유자 통합 기록](DroneTransferOwnerConsolidation-Verification.md)을 따른다. 이 보고서의 75개 검증은 이관 이전의 실행 근거다. |
| Ownership/EndStateApply | 기존 ItemOwnershipApplySystem이 최종 Owner·렌더를 적용한다. 요청·임시 계획을 제거하고 결과를 공개하는 구조 변경은 EndStateApply다. |

물류 입력은 이전 틱 StateApply/Synchronization에서 확정된 상태이며 현재 Command의 취소·철거 검사는 즉시 유효하다. Execution 첫 순서에서 입력을 읽으므로 현재 틱의 벨트 이동·Storage/Routing 적용 결과를 미리 추론하지 않는다. 계획 ID·슬롯 입력은 현재 원본을 대체하는 재고 상태가 아니다.

## 임시 계획과 인계 조건

[DroneItemTransferDecisions](../../../../Assets/Scripts/Components/DroneLogistics/DroneItemTransferDecisions.cs)에 세 타입을 추가했다.

- DroneItemTransferDecision: 행동 자격, 준비 여부, 요청 수량과 인계 상한. 자격이 유효하고 재고/공간이 0이면 MaximumQuantity=0인 정상 계획으로 기록한다.
- DroneItemTransferItemDecisionElement: 이전 입력에서 사용할 수 있던 전체 실물 ID. 상한만큼 ID를 잘라 두지 않아 앞선 요청이 소비한 뒤에도 남은 기존 실물을 후속 요청이 선택할 수 있다.
- DroneItemTransferSlotDecisionElement: 물류 변경 전 슬롯의 품목과 점유 수량. 실제 성공 입고량만 목적지별 공유 예산에 더한다.

Submit이 입력 경계에서 계획 컴포넌트·버퍼를 미리 붙인다. D/E에서 구조 변경을 추가하지 않는다. 준비한 계획은 실행 재진입 때 확장하지 않으며 EndStateApply에 제거한다. 계획 데이터가 없는 직접 생성 요청도 Apply에서 Rejected 결과로 소비한다.

[DroneItemTransferValidationUtility](../../../../Assets/Scripts/Common/DroneItemTransferValidationUtility.cs)는 기존 검사를 읽기 전용으로 공유한다. 초기 Decision과 최종 반영의 검증이 별도 정책으로 갈라지지 않게 했다. 실제 아이템 원본에 대한 CanMove·범위·슬롯 검사는 여전히 최종 반영에 필요하다.

같은 틱 벨트 입고가 추가된 아이템은 기존 ID 목록에 없으므로 수집하지 않는다. 기존 계획의 실물이 출고됐다면 새 입고품으로 대체하지 않는다. 이번 틱 수집으로 NextAction과 적재품이 바뀌어도 같은 틱 공급 요청은 초기 Decision에서 거절되고 다음 틱의 새 요청으로 공급한다.

보관은 기존 슬롯 예산을 공유하여 같은 틱 출고로 생긴 새 공간을 이용하지 않는다. 현재 슬롯도 [CanStoreInSlot](../../../../Assets/Scripts/Common/DroneSchedulingUtility.cs)로 다시 검사하므로 현재 입고가 기존 여유를 차지했으면 실패/부분 성공으로 남긴다. 이전 공간을 사용할 수 없는 MaximumQuantity=0 요청은 빈 공유 예산을 만들지 않아 뒤의 다른 품목 요청을 방해하지 않는다.

## 실행 근거

Unity 6000.4.11f1 연결 편집기에서 컴파일 후 세 클래스를 순차 실행했다. 원본은 프로젝트의 `Logs/Codex/DronePreviousTickTransfer-20261005/`에 보관한다.

| 확인 | 최종 결과 | 기록 |
| --- | --- | --- |
| 컴파일 | completed, failed:false, compilationFailed:false, errors:[] | `recompile-final.json`, `recompile-status-final.json` |
| DroneItemTransferTests | 31/31, 실패/건너뜀/Inconclusive 0 | `transfer-run.json`, `transfer-status.json` |
| DroneTaskSchedulingTests | 36/36, 실패/건너뜀/Inconclusive 0 | `scheduling-run.json`, `scheduling-status.json` |
| Phase7ConstructionCompletionTests | 8/8, 실패/건너뜀/Inconclusive 0 | `completion-run.json`, `completion-status.json` |
| 편집기 | ready, compiling:false, domainReloadInProgress:false, playMode:stopped | `editor-status.json` |
| 어셈블리·메타 | 런타임/Editor DLL 최신, ECS 102개·색인 누락 0, 신규 GUID 형식·유일성 정상 | `freshness-and-meta.json` |
| 문서·차이 검사 | 문서 11개·로컬 링크 564개 누락 0, git diff --check 종료 0, 대상 소스 공백/충돌 0 | `final-static-check.json`, `diff-check.txt` |

LF→CRLF 안내 13건은 있으며 공백 오류는 없다. 기존 staged/unstaged 작업은 유지했고 이번 작업에서 스테이징하거나 커밋하지 않았다.

초기 CS0246은 Execution 소스의 33자리 메타 GUID 때문에 편집기에 등록되지 않아 발생했다. 유효한 32자리 GUID로 수정하여 해결했고 최초 오류와 최종 성공을 구분한다.

인계 테스트는 기존 부분·0개 처리/중복/소유권/예약/결과 보호를 유지하면서 같은 틱 입고 지연, 새 적재품의 다음 틱 공급, 기존 실물 소실 후 신규 재고 대체 금지, 출고로 생긴 공간의 다음 틱 사용, 동시 입고의 공유 이전 공간 한도, D/E 원본 불변을 검증한다. 첫 불가 품목 요청이 뒤의 정상 품목 입고를 방해하지 않는 회귀도 확인했다.

## 검증 한계와 후속

명시적인 ECS 테스트 프리팹 DB·수행자·행동 신호로 실행했다. 실제 드론 생성·이동·충전·경로 계산·관측 위치 SoT 연결, 실제 SubScene 베이킹·화면·성능, 전체 EditMode와 Play Mode는 실행하지 않았다.

4단계의 자동 재탐색·취소/완공 후 재배정·현재 틱 월드 변경에 따른 바닥 완공 차단 갱신은 후속이다. 현재 원본 반영은 기존 Ownership 경계를 유지하며 인계 계획이 원본의 추가 소유자가 되지 않는다. 앞선 동일 틱 즉시 인계 당시 실행은 [3단계 기록](DroneItemTransferStep3-Verification.md)에 보존한다.
