# 일반 운반 목표량 확보와 정산 계약

2026-10-28 사용자 확정 1A/2A/3A/4A를 따른다. 일반 반입/반출의 작업 내 수량 중재와 후속 정산 API까지 구현한다. 작업 간 자동 선택은 9.8, 실제 배정 확정은 9.9, 실제 수집/납품 연결은 9.11~9.12, 진행 중 취소/중단 정산은 9.15에 남긴다.

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
| Progress.ReservedQuantity | DroneTransportReservationSystem의 단일 Burst Job이 확보/미인계분 반환 | 후속 배차/인계 소유자는 같은 utility로 인계/부분 반환/완료를 정산 |
| Progress.DeliveredQuantity | 후속 납품 소유자가 실제 성공량으로 TryRecordDelivery 호출 | 현재 자동 Writer는 없으며 계획/승인/픽업은 증가시키지 않음 |
| DroneTransportPendingReservationElement | 후보 singleton의 별도 버퍼, Reservation만 새 승인/반환 기록 작성 | Decision은 다음 Reservation의 반환 예정량을 읽음. 후속 선택/배차가 소비하지 않은 기록은 다음 Reservation에서 반환 후 Clear |
| DroneTransportReservation | 승인 기록 안에 보관하는 Task/RemainingQuantity. 후속 배차가 Take로 인계 | 후속 9.9에서 배정과 같은 EndSimulation에 부착. 현재 제품 경로는 배정을 생성하지 않음 |

현재 작업 내 계획 순서로 승인 상한을 배분한다. 이는 공급/회수/반입/반출 간 자동 우선순위나 수행자 claim이 아니다. 같은 수행자의 서로 다른 작업 계획은 아직 대안이며 후속 9.8이 선택하지 않은 승인은 반환해야 한다. 기존 공급/회수 후보/현장 예약/Publish는 변경하지 않는다.

## 같은 틱과 다음 틱

1. Decision은 기존 미인계 승인량이 다음 Reservation에서 반환될 것을 반영해 계획한다. 진행 중 인계된 확보량과 완료량은 계속 제외한다.
2. Reservation Job은 이전 미인계분을 먼저 반환하고 버퍼를 비운 뒤 이번 계획의 상한 안에서만 새 확보량을 기록한다. Decision 이후 바뀌지 않는 재고/공간/경로/망을 반복 검증하지 않는다.
3. 후속 배차가 최종 자격/수량을 검사한 뒤 `Take(ref pending)`로 확보 기록의 소유권을 넘겨받는다. 원래 기록은 0이 되어 다음 Reservation에서 이중 반환되지 않는다. 인계 직후 공개를 거부하면 넘겨받은 기록을 Release해야 한다.
4. 실제 배정/수행자 연결/확보 기록의 공개는 후속 9.9의 같은 EndSimulation이다. 현재 예약 시스템은 ECB를 기록하지 않으며 공급/회수 Publish 버퍼에 일반 운반을 넣지 않는다.
5. 이미 삭제된 작업의 이전 미인계 기록은 반환할 Progress가 없으므로 기록만 제거한다. 과거 Task Entity와 새 Task Entity를 합산하지 않는다.

Job은 Requirement/계획 RO와 Progress/승인 버퍼 RW만 사용하고 state.Dependency에 등록한다. 공유 NativeContainer/Fence와 메인 스레드 Complete는 추가하지 않는다. ECS 조회가 해당 Writer와 동기화한다.

## 후속 소비자 API

- `TryReserve`: 남은 목표량 안에서 확보하고 Progress와 기록을 함께 갱신한다.
- `Take`: 미공개 기록에서 인계받는다. 원본을 저장해야 하며 복사본을 여러 소유자가 소비하지 않는다.
- `TryReduce`: 실제 수집량이 적으면 미수집분을 반환한다. 픽업한 분량은 진행 중 확보로 유지하고 완료량은 바꾸지 않는다.
- `TryRecordDelivery`: 실제 보관 성공량만 완료량으로 옮긴다. 기록 잔량보다 큰 값/음수는 거부하고 상태를 변경하지 않는다.
- `Release`: 남은 확보량만 반환하고 기록을 0으로 만든다. 같은 소유 기록의 재반환은 효과가 없다.

호출자는 항상 확보 기록의 Task에서 Progress를 읽고 해당 기록과 함께 저장한다. 최초 목표량은 불변이며 확보 기록은 단일 소유자만 변경한다. 이 전제를 지키면 `완료량 + 확보량 <= 목표량`이 유지된다. 행동 번호/배정 revision의 중복 거부, 실제 도착/목적지 자격과 성공량 증명은 기존 Lifecycle/후속 납품 소유자의 책임이며 utility 호출만으로 실물 인계 성공을 만들지 않는다. 목적지 변경/중단과 root 종료/삭제는 9.7/9.12/9.15에서 연결한다.

## 검증 범위

기존 DroneTaskPlanningTests를 확장한다. 여러 수행자의 3+2 확보, 반복 틱 미인계 반환/재계획, 후속 소비자 대역을 통한 인계와 부분 반환, 실제 ItemOwnership 공통 API의 부분 보관 성공분과 단일 작업 실적 귀속을 검사한다. 현재 제품 자동 배차/비행/행동 신호를 실행한 것으로 해석하지 않는다.

Unity 6000.4.11f1/CLI 1.0.0-beta.14의 원본 프로젝트에서 C# 재컴파일 completed/failed false/errors 없음/compilationFailed false, Editor ready/비컴파일/비리로드와 최신 runtime/Editor 어셈블리를 확인했다. 최종 관련 선택 EditMode는 142/142(실패/생략/불확정 0)다. Planning 9/StationLifecycle 18/Request 18/Scheduling 40/Retargeting 18/ItemTransfer 33/LifecycleIntegration 6이며 앞선 124/124나 과거 실행과 합산하지 않는다. 초기 보관 성공 테스트의 창고 전용 CountInventory를 수행자에 사용한 assertion은 화물 버퍼 검사로 고쳤다.

독립 검수는 reservation/component/guard/decision/boundary 5역할로 수행했다. 실행 결함 지적 없이 현재 연결 전이라고 적힌 코드 주석/AGENTS 시스템 지도 누락/계획 명세의 잔량 표현 P3 세 원인을 정리했다. 주석/시스템 지도/잔량 설명은 집중 재검수에서 해소를 확인했다.

검증 중 기존 DroneStationLifecycleReservationSystem.OnCreate의 관리형 ComponentType 배열 관련 Burst BC1028을 확인했다. 같은 RO/RW 구성의 QueryBuilder로 초기 쿼리만 변경한 뒤 관련 142개를 재실행해 오류 재발 없음을 확인했고 Reservation/경계 집중 재검수에서 쿼리/공개 계약 동등성을 확인했다. 최종 콘솔 오류는 0건, 경고는 213건이다. 수집 구간의 경고는 기존 독립 World 구성에서 발생한 UpdateAfter 대상 부재이며 이번 변경의 그룹 순서를 새로 추가한 것은 아니다. Play Mode/Player/Burst AOT 전체/성능 검증은 수행하지 않았다. 원본 결과는 ignored Logs/Task96에 보관한다.
