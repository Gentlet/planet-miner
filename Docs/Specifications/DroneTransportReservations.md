# 일반 운반 목표량 확보와 정산 계약

2026-10-28 사용자 확정 1A/2A/3A/4A를 따른다. 일반 반입/반출의 작업 내 수량 중재/후속 정산 API와 종료/무효화 연결을 구현한다. 작업 간 자동 선택은 확정한 1A 순서로 통합했고 배정 확정은 기존 Publish에 연결했고 실제 수집/납품과 완료량은 [픽업](DronePickup.md)/[납품 계약](DroneDelivery.md)에 연결했고 진행 중 취소/중단 정산은 9.15에 남긴다.

## 목표량과 실적

- `DroneTransportRequirement.Quantity`는 최초 양수 목표량이며 변경하지 않는다.
- `DroneTransportProgress.DeliveredQuantity`는 실제 목적 보관처에 보관한 성공량이다. 반입/반출 모두 픽업은 완료가 아니다.
- `ReservedQuantity`는 미공개 승인과 후속 진행 중 확보량의 합계다. 공급원 재고/특정 실물/목적지 슬롯을 잠그지 않는다.
- 신규 확보 가능량은 `목표량 - 완료량 - 확보량`이다. 한 작업에 여러 드론이 나누어 확보할 수 있다. 목표 5/적재량 3의 두 계획은 3과 2로 승인한다.
- 반입과 반출이 같은 두 건물/품목을 가리켜도 독립 작업이다. 한 실적은 해당 확보 기록의 Task 하나에만 귀속한다.

## Writer와 수명

| 자료 | 생성/Writer | Reader/소비 |
| --- | --- | --- |
| Requirement/Progress | 기존 Task Execution이 같은 EndSimulation ECB에 생성, Progress 초기값 0 | Decision의 계획 잔량과 Reservation의 최종 작업 내 수량 중재 |
| Progress.ReservedQuantity | Task Reservation의 단일 Burst Job이 확보/무효 인계/미인계 반환, Assignment Publish가 최종 감량/거부 반환, Lifecycle이 실제 픽업의 미수집분 반환 | Lifecycle이 실제 보관 성공량의 완료 정산/잔여 반환에도 같은 utility 사용 |
| Progress.DeliveredQuantity | Lifecycle이 실제 성공량으로 TryRecordDelivery 호출 | 계획/승인/픽업은 증가시키지 않으며 실제 보관 성공만 기록 |
| DroneTransportPendingReservationElement | Reservation이 새 승인/옛 기록 정리, Publish가 Take/PublicationQueued 작성 | Decision은 반환 예정량을 읽음. 미인계 기록은 다음 Reservation에서 반환, 인계 기록은 0이므로 재반환하지 않음 |
| DroneTransportReservation | 승인 기록 안의 Task/RemainingQuantity를 Assignment Publish가 Take로 인계 | 배정/수행자 연결과 같은 EndSimulation에 부착. 최초 Revision=1/MovingToSource/CollectFromStorage |

DroneTaskReservationSystem은 적재 재배정을 먼저 선택한다. 신규 공급 선두는 PlacementStamp→CreationSequence, 회수/반입/반출 선두는 CreationSequence로 고르고 두 선두를 CreationSequence로 비교한다. 수행자당 하나만 선택하며 실행 불가능하거나 남은 목표량이 0인 후보는 건너뛴다. 공급/회수/재배정은 기존 공개 대기 버퍼에, 일반 운반 선택은 아래 단일 Job의 임시 입력에 전달한다.

일반 운반의 메인 스레드 로컬 예산은 기존 미인계분/무효 인계분의 반환 예정량과 이번 틱 선택분을 반영한다. 선택하지 않은 대안에는 새 확보량을 만들지 않으므로 다른 수행자가 같은 틱에 그 잔량을 선택할 수 있다. 반환 예정량을 원본에 미리 더하거나 Decision 계획 버퍼를 수정하지 않는다. 별도의 영속 선택 컴포넌트/인덱스는 추가하지 않는다. 수행자 claim은 최종 자격 검사 뒤 기존 Publish의 일회성 승인 소비와 같은 ECB의 수행자 연결로 확정한다. 새 claim 원본 컴포넌트나 공유 캐시는 추가하지 않는다.

## 같은 틱과 다음 틱

1. Decision은 기존 미인계 승인량과 이번 무효화 의도로 반환할 인계분을 계획에 다시 포함한다. 정상 진행 중 확보량과 완료량은 계속 제외한다.
2. Reservation 메인 스레드는 현장 예약 반환/적재 재배정 뒤 모든 유형을 함께 선택한다. 일반 운반 선택은 TempJob 목록으로 단일 Job에 넘긴다. Job은 무효/고아 인계분과 이전 미인계분을 반환하고 버퍼를 비운 뒤 선택된 계획의 상한 안에서만 새 확보량을 기록한다. Decision 이후 바뀌지 않는 재고/공간/경로/망을 반복 검증하지 않는다.
3. Assignment Publish가 최종 자격/수량을 검사한 뒤 `Take(ref pending)`로 확보 기록의 소유권을 넘겨받는다. 원래 기록은 0이 되어 다음 Reservation에서 이중 반환되지 않는다. 인계 직후 공개를 거부하면 넘겨받은 기록을 Release해야 한다.
4. 실제 배정/수행자 연결/확보 기록은 기존 Publish가 같은 EndSimulation ECB에 기록한다. Take로 비워진 승인에는 PublicationQueued=true를 남겨 반복 Publish와 Playback 전 정거장 전환을 막는다. Reservation은 ECB를 기록하지 않으며 일반 운반을 기존 공급/회수 공개 대기 버퍼에 섞지 않는다.
5. 이미 삭제된 작업의 이전 미인계 기록은 반환할 Progress가 없으므로 기록만 제거한다. 과거 Task Entity와 새 Task Entity를 합산하지 않는다.

Job은 Requirement/선택된 후보 배열/무효화 의도/배정 존재 RO와 Progress/확보 기록/승인 버퍼 RW를 사용한다. 확보 기록 소유자 배열은 메인 스레드의 반환 예정량 판단에도 쓰고 같은 배열을 Job에 전달한다. 선택 목록과 소유자 배열은 마지막 Job 접근 뒤 Dispose하며 해당 핸들을 state.Dependency에 등록한다. 기존 선택의 메인 스레드 경계는 유지하며 공유 NativeContainer/Fence나 컴포넌트별 수동 Complete는 추가하지 않는다. ECS 조회가 이전 Writer와 동기화한다.

## 배차 최종 공개의 확정 정책

- 1A: 최종 수량은 승인량/현재 공통 적재량/출발 재고/목적지 공간 안에서 정한다. 양수이면 감량분만 TryReduce로 즉시 반환하고 나머지를 배정한다. 0 또는 대상/수행자/관측/경로의 최종 자격 실패이면 전량 Release하고 배정/연결을 기록하지 않는다. 최초 목표량과 완료량은 바꾸지 않는다.
- 2A: 공개 실패 뒤 같은 틱에 새 후보를 찾거나 반환량을 재배분하지 않는다. 다음 Decision부터 다시 선택한다. 9.8의 Reservation 내 대체 선택은 유지하며 최종 공개 뒤의 재선택과 구분한다.
- 순수 후보/계획은 Decision, 통합 선택/목표량 승인은 Reservation, 최종 자격/확보 인계/배정 기록은 Publish, 실제 공개는 EndSimulation이 소유한다. 유형별 승인 버퍼와 기존 수행자 연결을 재사용하며 수동 우선순위/일시 중단/재개를 추가하지 않는다.
- 일반 운반 승인 잔량이 양수이거나 PublicationQueued이면 정거장 Reservation/최종 Apply의 Busy에 포함한다. Take 후 잔량이 0이어도 같은 EndSimulation 이전에는 수납/정박으로 배정 연결을 덮어쓰지 않는다.
- 기존 CanAcceptTask와 현재 revision의 평가 완료 경로를 최종 소비한다. 배차 시 완충이나 전체 왕복 에너지의 임의 기준을 추가하지 않는다. 실제 경로/배터리/지속 관측 Producer는 9.10, 일반 운반 픽업/미수집분 반환은 [픽업 계약](DronePickup.md)에 연결했고 실제 납품/완료량은 납품 계약에 연결했고 진행 중 취소는 9.15다.

## 후속 소비자 API

- `TryReserve`: 남은 목표량 안에서 확보하고 Progress와 기록을 함께 갱신한다.
- `Take`: 미공개 기록에서 인계받는다. 원본을 저장해야 하며 복사본을 여러 소유자가 소비하지 않는다.
- `TryReduce`: 실제 수집량이 적으면 미수집분을 반환한다. 픽업한 분량은 진행 중 확보로 유지하고 완료량은 바꾸지 않는다.
- `TryRecordDelivery`: 실제 보관 성공량만 완료량으로 옮긴다. 기록 잔량보다 큰 값/음수는 거부하고 상태를 변경하지 않는다.
- `Release`: 남은 확보량만 반환하고 기록을 0으로 만든다. 같은 소유 기록의 재반환은 효과가 없다.

호출자는 항상 확보 기록의 Task에서 Progress를 읽고 해당 기록과 함께 저장한다. 최초 목표량은 불변이며 확보 기록은 단일 소유자만 변경한다. 이 전제를 지키면 `완료량 + 확보량 <= 목표량`이 유지된다. 행동 번호/배정 revision의 중복 거부, 실제 도착/목적지 자격과 성공량 증명은 기존 Lifecycle의 책임이며 utility 호출만으로 실물 인계 성공을 만들지 않는다. root 종료/무효화 정산은 아래 현재 계약을 따르고 실제 납품은 9.12에 연결했고 사용자 취소/추가 수행 중단은 9.15에 남긴다.

## 종료와 무효화

2026-10-28 사용자 확정 1A/2A를 적용한다.

- 완료량이 최초 목표량과 같고 확보량이 0이면 다음 DroneDecision이 CompleteTransportTask 의도를 작성한다. Lifecycle은 최신 수량/상태를 재검사하여 Closed를 반영한다. 납품이 Decision 뒤 반영됐다는 이유로 같은 틱에 새 종료 판단을 만들지 않는다.
- Closed root는 배정 참조와 미정산 확보량이 없어야 EndSimulation에 삭제한다. 배정의 미소비 행동 요청/결과는 기존 삭제 보류를 통해 root도 보존한다. 부분 완료만으로 작업을 닫지 않는다.
- 원래 작업을 포기한 배정은 Decision의 기존 InvalidateAssignment 의도에 연결한다. Reservation이 남은 목표 확보량만 반환하고 해당 기록을 0으로 만든다. Lifecycle은 최신 배정 revision/상태를 확인해 빈 수행자는 종료하고 적재품이 있으면 연결/실물/완료량을 보존한 Retargeting으로 전환한다.
- 기존 재배정 Publish는 0으로 정산된 이전 DroneTransportReservation을 새 배정과 같은 EndSimulation에서 제거한다. 이전 root/최초 생성 순서는 이력으로 유지할 수 있지만 그 root의 무효 상태로 새 보관/방출 이동을 취소하지 않는다. 양수 확보 기록은 이 경로에서 임의 삭제하지 않으며 최초 무효화의 Lifecycle 최신 재검사도 유지한다.
- 원래 root가 사라졌거나 다른 root로 배정이 바뀐 기록도 정산 대상이다. 배정 컴포넌트가 없는 고아 확보 기록은 Reservation에서 반환한다. 삭제된 root는 반환 대상 Progress가 없으므로 기록만 0으로 정리한다.
- 정상 경로 평가 대기/계획된 충전 경유와 공간 부족만으로 확보량을 반환하지 않는다. 실제 중단을 알리는 Producer는 9.15에서 연결한다.
- 유휴 취소의 Busy 판정은 확보 합계에서 아직 미인계인 승인량을 빼서 인계분을 구분한다. 미인계 승인만으로 Busy를 만들지 않으며 인계된 미정산분이나 기존 배정 참조가 있으면 취소를 거부한다.

일반 운반 배정 Producer는 Assignment Publish에 연결했다. 실제 픽업/미수집분 반환 Writer는 기존 Lifecycle에 연결했고 실제 납품/완료량도 기존 Lifecycle에 연결했으며 배정 공개를 실제 이동/수집/납품의 완료로 해석하지 않는다.

## 목표량 중재 검증 (Task 9.6)

기존 DroneTaskPlanningTests를 확장한다. 여러 수행자의 3+2 확보, 반복 틱 미인계 반환/재계획, 후속 소비자 대역을 통한 인계와 부분 반환, 실제 ItemOwnership 공통 API의 부분 보관 성공분과 단일 작업 실적 귀속을 검사한다. 현재 제품 자동 배차/비행/행동 신호를 실행한 것으로 해석하지 않는다.

Unity 6000.4.11f1/CLI 1.0.0-beta.14의 원본 프로젝트에서 C# 재컴파일 completed/failed false/errors 없음/compilationFailed false, Editor ready/비컴파일/비리로드와 최신 runtime/Editor 어셈블리를 확인했다. 최종 관련 선택 EditMode는 142/142(실패/생략/불확정 0)다. Planning 9/StationLifecycle 18/Request 18/Scheduling 40/Retargeting 18/ItemTransfer 33/LifecycleIntegration 6이며 앞선 124/124나 과거 실행과 합산하지 않는다. 초기 보관 성공 테스트의 창고 전용 CountInventory를 수행자에 사용한 assertion은 화물 버퍼 검사로 고쳤다.

독립 검수는 reservation/component/guard/decision/boundary 5역할로 수행했다. 실행 결함 지적 없이 현재 연결 전이라고 적힌 코드 주석/AGENTS 시스템 지도 누락/계획 명세의 잔량 표현 P3 세 원인을 정리했다. 주석/시스템 지도/잔량 설명은 집중 재검수에서 해소를 확인했다.

검증 중 기존 DroneStationLifecycleReservationSystem.OnCreate의 관리형 ComponentType 배열 관련 Burst BC1028을 확인했다. 같은 RO/RW 구성의 QueryBuilder로 초기 쿼리만 변경한 뒤 관련 142개를 재실행해 오류 재발 없음을 확인했고 Reservation/경계 집중 재검수에서 쿼리/공개 계약 동등성을 확인했다. 최종 콘솔 오류는 0건, 경고는 213건이다. 수집 구간의 경고는 기존 독립 World 구성에서 발생한 UpdateAfter 대상 부재이며 이번 변경의 그룹 순서를 새로 추가한 것은 아니다. Play Mode/Player/Burst AOT 전체/성능 검증은 수행하지 않았다. 원본 결과는 ignored Logs/Task96에 보관한다.

### 종료/무효화 연결 검증 (2026-10-28)

현재 1A/2A 구현은 Unity 6000.4.11f1/CLI 1.0.0-beta.14에서 C# 재컴파일 completed/failed false/errors 없음/compilationFailed false, Editor ready/비컴파일/비리로드, 최신 runtime/Editor 어셈블리로 확인했다. 관련 선택 EditMode는 147/147(실패/생략/불확정 0)이며 Planning 14/StationLifecycle 18/Request 18/Scheduling 40/Retargeting 18/ItemTransfer 33/LifecycleIntegration 6이다. 위 Task 9.6의 142개나 중간 실행을 합산하지 않는다.

초기 테스트 헬퍼의 내부 utility 직접 참조로 접근 수준 컴파일 오류가 발생했고, 그 상태에서 제출된 구 Editor 어셈블리 9사례는 검증에서 제외했다. 직접 참조를 제거한 뒤 컴파일 성공을 확인하고 현재 147개를 실행했다. 후속 배차는 fixture가 배정/확보 기록/수행자 연결을 같은 EndSimulation에 게시하며, 납품은 실제 Ownership API 성공량을 후속 소비자 대역이 정산한다. 실제 자동 배차/비행/도착/충전의 증거가 아니다.

Decision/Reservation/StateApply/컴포넌트/guard/경계 6역할의 같은 14파일 버전 독립 검수에서 지적은 없었다. 검수 중 게임 코드와 테스트 해시를 보존했다. 결과/문서 상태 갱신만 이후 반영했다.

콘솔에는 Pipeline GET /api/commands 연결 종료와 응답 헤더 전송 이후 변경 오류 2건이 기록됐다. 컴파일/테스트 오류와 구분하며 CLI의 후속 Editor 조회는 ready였다. 기존 독립 World 구성의 그룹 순서 경고 213건도 보존한다. 원본 결과와 제외한 실행 기록은 ignored Logs/Task97에 있다. Play Mode/Player/Burst AOT 전체/성능 검증은 수행하지 않았다.

### 커밋 전 검수 보완: 재배정 이후 기록 수명/중복 검사

커밋 전 검수에서 초기 구현 검수가 놓친 P2 1건/P3 1건을 확인했다. 원래 고정 대상 소실로 확보량을 0으로 정산한 뒤 보관처로 재배정해도 과거 DroneTransportReservation이 남아, 다음 Decision이 닫힌 원래 root를 근거로 새 이동을 Retargeting으로 되돌렸다.

기존 RemovedFixedTarget_ReleasesClaim_AndKeepsOnlyLoadedWorkerConnected의 적재 사례를 대체 Direct 경로 평가/재배정 공개/추가 틱까지 확장했다. 수정 전 선택 2사례 중 적재 사례 1건이 MovingToDestination 대신 Retargeting으로 실패해 재현됐고, 수정 후 2/2 통과를 확인했다. 새 테스트 클래스를 중복 추가하지 않았으며 사례 수는 그대로이고 기존 사례의 검증 구간을 확장했다.

기존 PublishRetarget는 정산된 0 기록만 새 배정과 같은 EndSimulation에 제거한다. 양수 확보량을 이 경로에서 반환하거나 삭제하지 않으며 원래 무효화의 Lifecycle 재검사를 유지한다. 원래 Task/최초 생성 순서/화물은 유지하면서 다음 틱 이동 상태와 revision이 바뀌지 않음을 검사한다.

AssignmentNeedsCleanup은 한 호출에서 이미 검증한 동일 Task 데이터를 지역 변수로 재사용한다. 운반 기록이 없거나 현재 Task와 다르면 기존 조회/검사를 수행한다. 저장/방출의 자격 검사나 Lifecycle의 별도 최신 재검사는 제거하지 않았다. 지속 캐시나 새 원본 상태를 추가하지 않았다.

수정 후 C# 재컴파일 completed/failed false/errors 없음/compilationFailed false, Editor ready/비컴파일/비리로드/Play stopped와 최신 runtime/Editor 어셈블리를 확인했다. 최신 관련 선택 7개 fixture는 147/147(실패/생략/불확정 0)이다. 집중 회귀 2/2는 이 묶음의 부분집합이며 합산하지 않는다. 이전 147/147은 확장 전 테스트의 실행이며 이번 보완과 구분한다. 수정 후 콘솔 오류 0건/기존 경고 213건을 기록했다. StateApply/guard/컴포넌트/경계 집중 재검수에서 P2/P3 해소를 확인했고, 최신 지침에 따른 추가 가독성/주석 독립 검수도 지적 없이 완료했다. 검증 기록의 임시 문구도 갱신했다. 원본 결과는 ignored Logs/Task97/CommitFix에 보관한다. Play Mode/Player/Burst AOT 전체/성능은 미수행이다.
일반 반입/반출 픽업은 실제 수집량만 확보로 남기고 TryReduce로 미수집분을 반환한다. 픽업은 목표량/완료량을 증가시키지 않는다. StoreCargo는 실제 인계량으로 TryRecordDelivery를 호출한다. 양수 배터리의 공간 부족은 잔여 확보를 유지하고 실제 중단은 잔량만 반환한다.
