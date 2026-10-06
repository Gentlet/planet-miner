# 드론 인계의 기존 소유자 통합

이후 [시스템 파일 통합](SystemFileConsolidation-Verification.md)에서 두 보조 partial 파일을 각각 본체에 합쳤다. 아래 76개 테스트는 파일 통합 이전 소유자 통합 당시의 실행 근거로 보존한다.

## 변경과 결과

2026-10-05 사용자 승인에 따라 별도 DroneItemTransferApplySystem과 메타 파일을 제거했다. 공통 실물 반영은 기존 ItemOwnershipApplySystem, 드론 행동·상태·예약·결과 정산은 기존 DroneTaskLifecycleApplySystem에 통합했다. 새 시스템이나 ECS 타입을 추가하지 않았다.

Unity 컴파일은 `completed`, `failed:false`, `compilationFailed:false`, `errors:[]`다. 선별 EditMode **76/76 통과**(인계 32, 배정 36, 완공 8)이며 실패·건너뜀·Inconclusive는 0이다.

## 소유자와 실행 경계

- [ItemOwnershipApplySystem](../../../../Assets/Scripts/Systems/5_StateApply/ItemOwnershipApplySystem.cs)의 공통 API는 성공한 한 실물의 출발/도착 보관 버퍼, 실제 ItemOwnership, GridPosition·LocalTransform, 벨트 비활성화와 렌더 전환 ECB를 반영한다. 실물 생존·품목·Destroy·실제 소유자·출발 버퍼의 단일 참조·도착 버퍼 중복을 변경 전에 검사한다. 수량·목적지·슬롯·용량/필터 정책은 호출자가 결정한다.
- 기존 일반 TransferOwnershipRequest Job은 그대로 유지한다. 공통 API는 그 반영 이후 호출하며 새 Transfer 요청을 발행하지 않는다. 남은 활성 요청도 비활성화하여 기존 Job이 재적용하지 않게 한다. 실제 소유권을 즉시 반영하고 렌더 태그 구조 변경은 같은 EndStateApply에 기록한다.
- [DroneTaskLifecycleApplySystem](../../../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs)의 행동 처리는 당시 Actions partial에 두었다가 이후 본체로 합쳤다. 접수 순서, 재진입 방지, 이전 틱 실물 ID·수량 상한·슬롯 예산의 최종 검사, 성공분 배정/적재 출처/도착량/예약/결과 정산을 소유한다. 물리 변경은 공통 API에 위임한다.
- [Lifecycle 본체](../../../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs)는 기존 ItemOwnershipApplySystem 이후에 행동을 반영한 다음 무효화·삭제 판단용 스냅샷을 읽는다. 요청은 EndStateApply까지 남으므로 아직 공개되지 않은 결과의 배정 삭제 보류가 유지된다.
- [ConstructionLifecycleApplySystem](../../../../Assets/Scripts/Systems/5_StateApply/ConstructionLifecycleApplySystem.cs)은 DroneTaskLifecycleApplySystem 이후에 완공을 판단한다. 실물/도착량 정산과 렌더 기록이 자재·현장 삭제 기록보다 먼저 생성된다.

Decision 자격 검사와 Execution의 이전 틱 계획은 유지했다. 같은 틱 새 입고·새 적재품·출고로 생긴 공간은 다음 틱부터 사용할 수 있다. 공급원 재고·보관 공간 예약, 벨트 판단 변경, 실제 드론 이동·신호 생성은 추가하지 않았다.

## 실행 근거

Unity 6000.4.11f1의 연결 Editor에서 컴파일 후 세 클래스를 순차 실행했다. 원본 결과는 `Logs/Codex/DroneTransferOwnerConsolidation-20261005/`에 보존한다.

| 확인 | 결과 | 원본 기록 |
| --- | --- | --- |
| 컴파일 | completed, failed:false, compilationFailed:false, errors:[] | `recompile-status.json` |
| DroneItemTransferTests | 32/32, 실패·건너뜀·Inconclusive 0 | `transfer-run.json`, `transfer-status.json` |
| DroneTaskSchedulingTests | 36/36, 실패·건너뜀·Inconclusive 0 | `scheduling-run.json`, `scheduling-status.json` |
| Phase7ConstructionCompletionTests | 8/8, 실패·건너뜀·Inconclusive 0 | `completion-run.json`, `completion-status.json` |
| Editor | ready, compiling:false, domainReloadInProgress:false, playMode:stopped | `editor-status.json` |
| 어셈블리·메타 | 런타임/Editor DLL 모두 최신, 신규 partial 메타 GUID 32자리·유일성 확인 | `freshness.json` 및 해당 메타의 정적 검색 |
| 문서·차이 | 문서 12개·로컬 링크 577개 누락 0, git diff --check 종료 0, 제거 시스템/메타 부재 | `static-check.json`, `diff-check.txt` |

인계 회귀는 기존 부분·0개·중복·늦은 소유권 변경·현장 예약·결과 보호·이전 틱 계획 사례를 포함한다. 새 공통 API 회귀는 월드→보관→다른 보관→월드의 버퍼·소유권·슬롯·위치·벨트·렌더 적용, 중복 출발 요청 거부, 이전 Transfer 재적용 방지, Transform의 z·회전·scale 보존을 검증한다.

첫 CLI 호출은 `--caller`를 전역 옵션 위치에 두어 `unknown option`으로 거부되었고 Editor에 도달하지 않았다. `command` 옵션 위치로 수정한 뒤 위 컴파일과 테스트를 실행했다. Unity 소스 컴파일 실패는 없었다.

diff 검사에는 LF→CRLF 안내 15건이 있으며 공백 오류는 없다. 새 공통 API·Lifecycle partial·인계 Utility·인계 테스트에 공백/충돌 표시가 없음을 정적 확인했다. 기존 staged/unstaged 작업을 유지했으며 이번 작업에서 스테이징하거나 커밋하지 않았다.

## 검증 한계와 후속

명시적인 ECS 테스트 프리팹 DB·수행자·행동 신호로 검증했다. 실제 드론 생성·이동·충전·경로 계산·관측 위치 SoT 연결, 실제 SubScene 베이킹·화면·성능, 전체 EditMode와 Play Mode는 검증하지 않았다.

4단계 자동 재탐색·취소/완공 후 재배정·월드 변경에 따른 바닥 완공 차단 갱신은 후속이다. 이전 구조의 실행 근거는 [이전 틱 분리 기록](DronePreviousTickTransfer-Verification.md)과 [3단계 기록](DroneItemTransferStep3-Verification.md)에 보존했다.
