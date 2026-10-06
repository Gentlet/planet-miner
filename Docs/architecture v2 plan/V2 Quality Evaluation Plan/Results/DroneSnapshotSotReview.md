# 드론 스냅샷과 Source of Truth 검토

이 기록은 실물 인계 3단계 구현 전의 검토다. 이후 행동 신호 접수·실제 인계·결과 생성/소비를 연결했으며 현재 구현은 [3단계 검증](DroneItemTransferStep3-Verification.md)을 따른다. 아래 미구현 소비자에 대한 문장은 당시 상태로 보존하며, 실제 드론 위치/관측의 SoT 연결 TODO는 현재도 남아 있다.

## 범위와 판단 기준

2026-10-05 드론 ECS 타입 18개와 직접 작성/소비 경로를 확인했다. 사용자 승인으로 `DroneTaskCandidateElement`를 [DroneTaskCandidateDecisionElement](../../../../Assets/Scripts/Components/DroneLogistics/DroneTaskCandidateDecisionElement.cs)로 이름 변경했고 필드·선택·예약·공개 동작은 유지했다. `.meta` GUID `bf528a1134f84592be2b59509c9c972e`도 보존했다.

검토는 [AGENTS.md](../../../../AGENTS.md)의 현재 원본/단계 계약을 기준으로 한다. [Architecture V2 Plan_0.2](../../Architecture%20V2%20Plan_0.2.md)의 470행 원칙도 같은 정보가 여러 곳에 있어도 원본은 하나여야 한다고 명시한다. 해당 문서의 SoT 후보 표는 확정 설계가 아니므로 현재 구현보다 우선하지 않는다.

판단 시점의 입력·revision·결과를 복사하는 것 자체는 위반이 아니다. 복사 값이 원본으로 재검증되는지, 현재 상태를 대신 갱신하는지, 누가 쓰고 언제 소비하는지를 확인했다. 현재 위치 원본과 관측 값의 연결이 빠진 부분은 아래의 **원본 연결 공백**, 아직 없는 후속 소비자의 검사 계약은 **미구현 검증**으로 구분한다. 실제 드론 이동의 실행 오류를 재현한 것은 아니다.

## 스냅샷·파생 결과·판단 문맥을 담는 타입

| 타입 | 복사하거나 보관하는 값 | 현재 사용과 SoT 판단 |
| --- | --- | --- |
| DroneWorkerObservation | Position, Revision, CanAcceptTask | 후속 수행부가 게시할 관측 값. 현재 관리 판단은 Position을 직접 사용하지만 실제 위치 원본과 동기화/대조하는 Producer·검사가 없다. 원본 연결 공백. |
| DroneRouteEvaluationRequest | OriginPosition, SourcePosition, DestinationPosition, 관측/평가 revision | 공급원·목적지 위치는 현재 GridPosition과 재검사한다. 출발점은 WorkerObservation과만 비교하므로 그 원본 연결 공백을 이어받는다. 요청의 불변 입력을 위치 원본에 다시 쓰지는 않는다. |
| DroneRouteEvaluationResult | EvaluationRevision, Status, TotalDistance | 경로 계산의 파생 결과 계약. 요청 revision과 유효성을 확인한 뒤 후보 비교에만 사용한다. 실제 평가 Producer는 아직 없다. |
| DroneRouteDecisionElement | 경로 요청 Request 전체와 ExistingRequest | Decision→Execution 명령 전달용 복사다. 생성 명령은 RouteIsCurrent를 검사하고 요청을 ECB에 게시한다. 현재 위치를 변경하지 않는다. |
| DroneTaskCandidateDecisionElement | Worker/Task/Source/Destination, 품목·수량, WorkerObservationRevision | 배정 후보와 공개 대기 예약 기록. Reservation/Publish가 현재 작업·경로·공통 적재량·재고·현장 잔량을 다시 읽는다. 독립적인 재고/적재 원본으로 사용하지 않는다. |
| DroneTaskInvalidationDecisionElement | Target, AssignmentRevision, Kind | 무효화 판단 당시 배정 버전을 보관한다. Lifecycle은 현재 revision·무효 여부를 다시 검사한다. 스냅샷이 원본 종료 상태를 직접 소유하지 않는다. |
| DroneActionReadyRequest | Action의 배정 revision/sequence/Worker/Target, 관측 revision, WorldPosition | 후속 행동 시도 입력 계약. 위치·식별자·순서의 실제 소비/대조는 아직 구현되지 않았다. 현재는 Lifecycle이 배정 참조를 읽어 삭제를 보류한다. |
| DroneItemTransferResult | Action, Status, MovedQuantity | 내부 인계가 실제 이동량으로 작성할 결과 계약. 현재 생성/인계/소비 기능이 없다. 실제 적재량 원본을 대체하는 코드가 있는 것으로 판단하지 않는다. |

## 현재 코드에서 확인한 원본 연결 공백

핵심은 [DroneWorkerObservation](../../../../Assets/Scripts/Components/DroneLogistics/DroneWorkerComponents.cs)의 Position이다. 주석은 격자/렌더 위치 원본을 대체하지 않는 관측 스냅샷이라고 정의한다. 그러나 [수행자 검사](../../../../Assets/Scripts/Common/DroneSchedulingUtility.cs)의 IsIdleWorker는 Worker의 GridPosition이나 실제 이동 위치 원본을 요구하지 않는다. 현재 테스트 수행자도 위치 데이터는 Observation만 준비하며 실제 위치 원본 없이 작업 후보를 만들 수 있다.

[요청 작성](../../../../Assets/Scripts/Systems/2_Decision/DroneTaskDecisionSystem.cs)은 `OriginPosition = observation.Position`을 기록한다. [RouteIsCurrent](../../../../Assets/Scripts/Common/DroneSchedulingUtility.cs)는 이 값과 현재 Observation.Position/Revision을 비교한다. 이것은 **관측과 관측에서 만든 요청의 일치**를 보장하며, 실제 드론 위치와 관측의 일치를 보장하지 않는다. 공급원·목적지는 같은 메서드에서 실제 GridPosition과 대조하므로 출발점과 검증 경계가 다르다.

따라서 실제 위치만 바뀌고 Observation이 갱신되지 않은 상태를 가정하면, 현재 검사는 그 변화 자체를 알아낼 수 없다. 이는 소스에서 확인한 검증 공백이다. 이동/관측 Producer가 없으므로 현재 정상 게임 실행에서 발생한 SoT 분기를 확인한 것은 아니다.

후속 연결에서는 드론의 실제 위치를 소유하는 ECS 상태와 그 Writer를 먼저 확정하고, 관리 측이 원본을 직접 읽거나 관측이 원본에서 갱신되었음을 보장해야 한다. 스냅샷끼리의 revision 비교만으로는 이 연결이 만들어지지 않는다. CanAcceptTask도 현재 원본 정의·Producer가 없는 외부 준비 상태 계약이므로, 배터리/이동 상태 등이 구현될 때 어떤 원본에서 계산하는지 정해야 한다. 현재 후보 검사는 별도로 배정 연결과 실제 적재 버퍼도 확인한다.

이번 요청은 확인 작업이므로 이 문제의 위치 소유자나 이동/관측 시스템을 임의로 구현하지 않았다.

이후 사용자 요청으로 DroneWorkerObservation의 정의와 RouteIsCurrent의 출발점 검사에 `TODO(SoT 연결)` 주석을 추가했다. 실제 실행 위반의 재현이 아니라 위치 원본/Writer 및 관측 갱신 경계가 미완성이라는 점을 표시했다. 주석만 추가한 뒤 컴파일 completed·failed:false·errors:[], 편집기 ready·비컴파일·비리로드 및 최신 런타임 DLL을 확인했다. 이 후속 원본은 `Logs/Codex/DroneSotComments-20261005/`에 있으며 테스트를 다시 실행한 기록은 아니다.

## 복사처럼 보이지만 다른 상태를 표현하는 값

- DroneLogisticsTask의 품목·대상은 상위 작업의 계약이다. DroneTaskAssignment의 AssignedQuantity는 승인된 운반 수량이며 현재 실물 적재 수량의 복사본이 아니다. 현재 Lifecycle은 실제 StoredItemElement를 읽어 적재 유무를 판단한다.
- ConstructionSupplyReservation.RemainingQuantity는 남은 개별 예약의 원본이다. 현장 ReservedQuantity는 활성 개별 예약과 미공개 CommittedQuantity의 합계다. Reservation의 확보/해제, Publish의 인계/축소 롤백이 함께 유지하는 집계 불변식이며 별도 정책값 두 개로 사용하지 않는다.
- DroneWorkerAssignment는 배정 연결의 역방향 참조다. Publish와 Lifecycle이 배정·연결의 작성/해제를 소유한다. 현재 단독으로 작업·적재 상태를 덮어쓰는 스냅샷은 아니다.
- DroneCapacityState, DroneTaskSequence, DroneCargoState는 각 도메인의 원본 상태다. 시스템의 지역 변수는 업데이트 중 읽은 값이며 추가 원본 컴포넌트가 아니다.
- DroneWorker와 DroneRecoveryPending은 태그다. DroneTaskCreationDecisionElement는 생성 명령을 전달하며 현재 실물 상태를 복제한 상태 소유자로 사용하지 않는다.

## 후속 소비자에서 완성해야 할 검사

ConstructionSupplyReservation.AssignmentRevision은 배정 revision과 일치해야 한다는 계약이다. 현재 무효 예약 해제와 ProjectedRemaining에서는 두 revision을 비교하지 않는다. 현 Producer는 배정과 예약을 모두 revision 1로 만들고 revision을 바꾸는 재배정 Producer도 아직 없으므로, 현재 정상 경로의 불일치를 확인한 것은 아니다. 후속 재배정에서는 이전 예약 정산과 버전 연결 검사를 구현해야 한다.

DroneActionReadyRequest/DroneItemTransferResult의 행동 revision·sequence·WorldPosition·MovedQuantity 검사도 정의된 계약이며 실제 소비자는 없다. 문서와 타입이 존재한다는 사실을 인계 SoT 정합성의 구현 증거로 취급하지 않는다.

## 이름 변경과 선별 실행 근거

이름 변경 후 Unity 6000.4.11f1 연결 편집기에서 컴파일 `completed`, `failed:false`, `compilationFailed:false`, `errors:[]`를 확인했다. 기존 `StaleRouteSnapshot_IsNotUsedForAssignment`의 결과 revision·수행자 관측·목적지 위치 변경 3개 사례가 모두 통과했다(실패/건너뜀/Inconclusive 0). 이는 관측 갱신과 목적지 원본 변경을 거절하는 기존 경계의 실행 근거이며, 실제 드론 위치 원본과 관측의 연결을 검증한 것은 아니다. 원본 출력은 프로젝트의 `Logs/Codex/DroneCandidateDecisionRename-20261005/`에 보관한다.

| 확인 | 최종 결과 | 기록 |
| --- | --- | --- |
| 컴파일 | completed, failed:false, compilationFailed:false, errors:[] | `recompile.json`, `recompile-status.json` |
| 선별 EditMode | total 3, passed 3, failed/skipped/inconclusive 0 | `stale-route-run.json`, `stale-route-status.json` |
| 편집기 | ready, compiling:false, domainReloadInProgress:false, playMode:stopped | `editor-status.json` |
| 어셈블리·메타 | 최신 런타임 소스보다 DLL 최신, 후보 GUID 보존·중복 0, 구 C# 이름 잔여 0 | `final-static-check.json` |
| 차이 검사 | git diff --check 종료 0, LF→CRLF 안내 9건 | `diff-check.txt` |

SoT 공백의 판단은 소스 검토 결과다. 전체 EditMode, Play Mode, 실제 드론 이동·연구·경로 계산·인계는 실행하지 않았다. 기존 실행 기록은 [공통 적재량·무효화 검증](DroneCapacityAndInvalidation-Verification.md)에 보존한다.
