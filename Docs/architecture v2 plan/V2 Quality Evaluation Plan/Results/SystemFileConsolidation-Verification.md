# 시스템 소스 파일 통합

## 변경 범위

2026-10-05 사용자 지시에 따라 별도 요청 없이 하나의 시스템을 여러 partial 소스 파일로 분리하지 않는 관례를 AGENTS.md에 기록했다. Unity Entities 소스 생성에 필요한 본체의 `partial` 선언은 유지한다.

Assets/Scripts의 시스템 선언과 파일 경로를 확인해 분리된 세 시스템을 각각 본체 파일에 통합했다.

| 본체 | 통합 후 삭제한 보조 소스와 메타 |
| --- | --- |
| [DroneTaskDecisionSystem](../../../../Assets/Scripts/Systems/2_Decision/DroneTaskDecisionSystem.cs) | DroneTaskDecisionSystem.Creation.cs / .meta |
| [DroneTaskLifecycleApplySystem](../../../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs) | DroneTaskLifecycleApplySystem.Actions.cs / .meta |
| [ItemOwnershipApplySystem](../../../../Assets/Scripts/Systems/5_StateApply/ItemOwnershipApplySystem.cs) | ItemOwnershipApplySystem.Transfer.cs / .meta |

using 선언을 병합하고 기존 필드·메서드를 본체에 옮겼다. 시스템 책임, 행동 반영→무효화→종료 정리의 OnUpdate 호출 순서, UpdateBefore/UpdateAfter, 두 ECB 반영 경계는 유지한다. 공통 TryTransferItem API도 같은 기존 ItemOwnership 소유자에 속한다. 기존 스테이징 상태를 유지했으며 추가 스테이징·커밋은 하지 않았다.

## 검증 결과

Unity 6000.4.11f1 연결 Editor에서 `recompile --focus false`를 실행했다. 최종 결과는 `completed`, `failed:false`, `compilationFailed:false`, `errors:[]`다. Editor는 `ready`, `compiling:false`, `domainReloadInProgress:false`, `playMode:stopped`다.

원본 실행 결과는 `Logs/Codex/SystemFileConsolidation-20261005/`에 보존한다.

- `recompile.json`, `recompile-status.json`: 컴파일 요청과 완료 결과.
- `editor-status.json`: 검증 후 Editor 상태.
- `source-preservation.json`: 공통 Owner 본체/이관 본문·메타 보존 및 Decision 원본/생성 본문 비교. Decision 원본 비교는 병합 경계의 빈 줄을 제외한다.
- `source-meta-freshness.json`: 본체 메타 3개가 기존 Git 인덱스와 동일하고 보조 소스/메타 6개가 제거됐음을 확인했다. 여러 파일로 분리된 시스템은 0개이며 런타임 DLL이 최신 C# 소스보다 새롭다.
- `document-check.json`, `diff-check.txt`: 관련 문서 14개·로컬 링크 600개 누락 0, git diff --check 종료 0. 줄바꿈 정규화 안내는 있으며 공백 오류는 없다.

이번 변경은 소스 파일 배치 정리이며 새 테스트는 작성하거나 실행하지 않았다. 이전 소유자 통합의 선별 EditMode 76/76은 [그 당시 검증](DroneTransferOwnerConsolidation-Verification.md)으로 보존하며 이번 컴파일 결과와 구분한다. Play Mode·실제 드론 이동·입력·화면·성능 검증은 수행하지 않았다.
