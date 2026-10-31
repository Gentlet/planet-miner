# 드론 화물 복구와 자동 안전 방출

## 확정 범위

2026-10-31 확정한 1A를 적용한다. 현재 ConstructionSupply/WorldItemRecovery/StorageImport/StorageExport의 적재품 재배정과 안전 방출을 기존 행동 요청/실물 정산 경로에 연결한다. 추가 직접 작업의 복구는 해당 Producer 구현 시 연결한다. 새 아이템 생성/별도 Ownership Writer/영속 복구 캐시는 추가하지 않는다.

공급 출처 화물은 필요한 현장, 보관처 순서로 찾고 회수 출처 화물은 보관처만 찾는다. 모든 대상 평가 후 도달 가능한 목적지가 없으면 안전 방출 목표를 선택한다. 평가 대기와 에너지 회복 대기는 방출 사유로 바꾸지 않는다. 정상 일반 운반의 유효한 고정 목적지 공간 부족은 기존 화물/확보 유지 대기다. 실패/취소 후 Retargeting의 복구와 구분한다.

## 도착 후 방출

DroneActionRequestDecisionSystem의 기존 병렬 Burst Job이 공개된 AwaitingDelivery와 DropCargo를 읽어 요청 의도를 만든다. 같은 틱 Task Decision의 무효화/취소 의도가 있으면 만들지 않는다. 자동 방출은 실제 이동 도착이 EndSimulation에 공개된 다음 Decision부터 접수하며 새 목표 선택/이동/방출을 한 틱에 연쇄 실행하지 않는다.

1A에 따라 유효한 방출 지점의 도착이 공개되었다면 배터리 0이어도 추가 이동 없이 방출한다. 배터리를 환급하거나 음수로 차감하지 않는다. 배터리 0만으로 새 방출 목표를 선택하지 않으며 기존 비상 귀환/정거장 대기와 적재품 보존을 유지한다. 일반 재배정/비행의 양수 배터리 자격도 유지한다.

DropCargo의 Target은 Entity.Null이며 건물 수령점 검사 대신 의도에 담긴 실제 격자 셀과 선택 DropPosition을 비교한다. Lifecycle은 기존 관측/revision/행동 번호/실물 Owner와 목표==요청 위치==관측 셀, 현재 활성 비취소 현장 footprint 외부를 최종 검사한다. 새 현장이 막으면 실물을 보존한 Retargeting으로 돌아가 외부 안전 셀을 다시 찾는다. 외부 셀 평가의 None/Unreachable은 적재 대기다.

## 소유자와 공개 시점

| 단계 | 책임 |
| --- | --- |
| Decision/Reservation/Publish | 기존 목적지 재탐색, 안전 셀 평가, 예약 반환/새 예약, 같은 배정의 revision 증가를 소유한다. 새 배정은 EndSimulation 뒤 다음 틱부터 사용한다. |
| 자동 행동 Decision | 기존 ReceiptSequence 접수 API를 사용하고 자기 요청에만 DroneAutomaticActionRequest를 붙인다. 같은 배정/revision/행동의 외부 또는 자동 미처리 요청이 있으면 중복 접수하지 않는다. |
| Transfer Execution | 기존 건물 종료 입력에서 준비한 실물/최대 수량 계획을 사용한다. |
| Task Lifecycle | 현재 실물을 재검사한 뒤 공통 ItemOwnership API로 같은 Entity의 Owner/출발 버퍼/격자를 직접 변경한다. 실제 옮긴 양만 행동/예약/화물 상태에 반영한다. 빈 화물은 Completed/연결 해제, 잔여품은 Retargeting이다. |
| EndSimulation | 렌더 구조/DroneRecoveryPending 표시, 요청 제거/결과, 재배정/종료 삭제를 공개한다. 실제 Playback과 기록 완료 표시는 구분한다. |
| 다음 Decision | 자동 수행부는 자기 결과만 소비한다. 외부 결과는 외부 TryConsumeResult에 남긴다. 표시된 월드 실물은 기존 한정 회수 생성의 입력이 된다. |

이전 작업의 이미 확정한 완료량은 유지한다. 안전 방출/후속 회수 보관을 취소된 일반 운반 목표에 추가 귀속하지 않는다. 명시 취소가 같은 틱의 아직 수행하지 않은 방출보다 우선하며 과거 revision/행동 번호의 신호는 새 복구에 적용하지 않는다.

## 표시 기반 회수와 후속 범위

안전 방출 성공 Entity에는 DroneRecoveryPending을 기록한다. [철거 계약](DroneDemolition.md)은 기존 반환/환급 Entity에 BuildingDemolition 출처의 같은 표시를 EndBuilding에 기록한다. 다음 Task Decision은 기존 한정 WorldItemRecovery 작업을 만들고 기존 이동/픽업/보관으로 회수한다. 보관처가 모두 가득 차면 월드 실물/표시와 미배정 회수 작업을 보존하고, 공간이 생기면 다시 배차한다. 픽업 성공 뒤 기존 Lifecycle이 표시를 제거한다. 일반 바닥/벨트 실물 전체를 자동 회수하지 않는다.

기존 사용자 취소의 동일 키 자동 생성 차단은 유지한다. 취소한 월드 회수의 같은 아이템 Entity가 다시 방출되어도 기존 차단을 임의 해제하지 않으며 유효한 명시 생성이 해제한다.

일반 건물 직접 철거는 철거 계약을 따른다. 일반 바닥/사용자 범위 회수 확장은 Task 9.18, 정거장 철거의 추가 Producer는 해당 후속 Task, 공사 취소 통합은 Task 9.20에 남긴다.

## 검증

관련 선택 EditMode 12개 fixture 262/262(실패/생략/불확정 0)를 확인했다. DroneDeliveryTests의 기존 8개와 신규 9개가 실제 경로/이동/공개 도착/자동 요청/실물 인계를 실행한다. 신규 사례는 양수/실제 마지막 이동으로 0 배터리 도착, 회수 작업 중복 방지와 공간 복구 후 회수, 취소와 이미 접수된 외부 방출, 회전 현장에 막힌 목표의 실제 외부 셀 재탐색, 0 배터리의 새 방출 금지, 외부 중복/지연 요청을 포함한다. 초기 자동 방출 두 회귀는 수정 전 실제 도착 후에도 화물 3개가 남아 실패했다.

정거장 네트워크 범위는 fixture 입력이며 전체 장면/Power 충전의 통합 증거로 확대하지 않는다. 별도 관련 Movement/Charging/Return/Reservation/인계/취소 fixture를 포함한다. Play Mode/Player/성능/전체 Burst AOT는 미수행이다. Unity C# 컴파일은 completed/failed false/errors 없음/compilationFailed false이며 최신 runtime/Editor DLL을 확인했다.
