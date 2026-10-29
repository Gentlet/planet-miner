# 드론 픽업과 수집 실물 인계

## 확정 정책과 범위

확정 1A는 수집 도착이 EndSimulation에 공개된 다음 틱에 별도 대기 시간 없이 한 번 픽업하는 규칙이다. 픽업 타이머/추가 에너지 소비는 만들지 않는다. 공사 공급/월드 회수/일반 반입/일반 반출을 지원하며 실제 자동 납품/완료량 정산은 후속이다.

수집 성공은 요청 수량이 아니라 공통 Ownership API가 옮긴 실제 실물 수량이다. 공급원 재고와 보관 공간의 영속 예약을 추가하지 않는다. 부분 수집은 실제 적재량으로 진행하고 미수집분을 반환한다. 0개 수집은 배정을 취소하고 확보량/수행자 연결을 정리한다.

## Producer와 같은 틱 경계

| 시점 | 소유자와 입력/출력 |
| --- | --- |
| 이전 EndSimulation | Movement Apply가 수집점 도착을 AwaitingCollection으로 공개한다. |
| Preparation | 기존 Observation Preparation이 실제 위치/배터리/연결로 관측을 준비한다. |
| DroneDecision | Flight/Task Decision 뒤 DronePickupDecisionSystem이 공개 도착/양수 배터리/빈 화물/이번 무효화 의도로 픽업 의도를 판단한다. 정박 수행자는 제외한다. |
| 같은 Decision의 요청 접수 | 판단 Job이 끝난 뒤 기존 DroneActionRequestUtility.Submit으로 요청을 생성하고 DroneAutomaticPickupRequest를 붙인다. 기존 Transfer Decision이 이 요청을 읽는다. |
| DroneExecution 시작 | 기존 Transfer Execution이 건물 종료 상태의 실물 ID/수량 상한을 준비한다. |
| DroneStateApply | 기존 Lifecycle이 최신 식별/도착/소유권을 검사하고 공통 ItemOwnershipApplySystem.TryTransferItem으로 실물을 옮긴 실제 수량만 정산한다. |
| EndSimulation | 기존 요청/계획 제거와 DroneItemTransferResult 공개를 확정한다. |
| 다음 DroneDecision | 자동 Producer가 자신이 만든 결과만 TryConsumeResult로 소비한다. 외부 요청 결과는 외부 호출자가 소비한다. |

Movement Execution보다 픽업 판단이 먼저이므로 이번 틱에 새로 도착한 드론은 같은 틱에 픽업하지 않는다. 픽업으로 MovingToDestination이 된 뒤의 이동도 다음 틱부터다. 이후 AwaitingDelivery에 도착해도 자동 납품 신호를 만들지 않는다.

## 요청/결과 수명과 중복 방지

DroneAutomaticPickupRequest는 요청 Entity에만 있는 수명 표식이다. 수행자의 작업/화물/배정 상태를 복제하지 않고 Spawn/Store의 역할 구성에 추가하지 않는다. 요청이 결과로 전환된 뒤에도 유지하며 자동 Producer가 결과 Entity를 소비할 때 함께 사라진다.

자동 생성 묶음은 기존 OriginalTaskCreationSequence, 수행자 Entity 순서로 접수한다. 이미 접수된 외부 요청 순서를 재정렬하지 않는다. 실제 경합은 기존 World ReceiptSequence를 따른다. 같은 배정/revision/수행자/수집 행동의 미처리 요청이 있으면 자동 요청을 추가하지 않는다. 행동 번호는 현재 LastAppliedActionSequence 다음 값이며 기존 식별 검사가 중복/과거 신호를 거절한다.

외부 결과는 자동 소비하지 않는다. 자동 결과를 방치해 배정 삭제를 영구 보류하지 않으며 결과 소비로 실물/확보량을 다시 정산하지 않는다.

## 수집 도착과 실패

수집은 AwaitingCollection과 실제 LocalTransform의 현재 수집점 도착을 요구한다. 건물은 회전 footprint 중심, 월드 실물은 격자점이며 Flight와 같은 목표점 함수를 사용한다. 관측 위치는 실제 위치와 일치해야 하고 관측/배정 revision/행동 식별도 기존 공통 검사로 확인한다. MovingToSource만으로 제출한 외부 요청은 수집하지 않는다.

배터리 0은 5A/6A에 따라 새 픽업을 시작하지 않는다. Flight가 수집 시작 불가를 이동 결과에 남기고 기존 다음 Decision/Reservation/Lifecycle이 확보량 반환/취소/비상 귀환을 정산한다. 수집 전에 목표점이 바뀌면 확정한 과거 도착을 현재 수집 자격으로 재사용하지 않고 같은 중단 경로에 연결한다. 이미 공개된 납품 대기는 자동 납품 후속 범위와 구분해 유지한다.

## 일반 운반 확보량

일반 반입/반출의 수집 요청은 상위 Open 작업의 유형/대상 방향과 DroneTransportReservation.Task 연결, Requirement/Progress, 출발/도착 보관처를 검사한다. 수집 상한은 기존 AssignedQuantity와 해당 확보 기록의 RemainingQuantity 안이다.

실제 수집 후 TryReduce로 미수집분만 Progress.ReservedQuantity에서 반환하고 같은 확보 기록을 저장한다. 원래 목표 Quantity와 DeliveredQuantity는 변경하지 않는다. 수집분은 진행 중 확보로 남기고 Supply 출처/StoreCargo 다음 행동으로 이동한다. 보관처에서 수집한 출처이므로 기존 Supply 재배정 규칙을 재사용한다. 월드 회수는 기존 Recovery 출처와 현장 직접 공급 금지를 유지한다.

일반 운반의 납품/완료량 정산이 아직 연결되지 않았으므로 운반 확보 기록이 붙은 StoreCargo 요청은 거절한다. 기존 보관 경로가 확보량을 남긴 채 배정을 완료시키지 않도록 하는 범위 제한이며, 후속 납품 구현에서 실제 성공량 정산과 함께 교체한다. 기존 공급/회수와 확보 기록을 정산/제거한 적재 재배정의 인계 경로는 유지한다.

## Job과 자료 수명

병렬 Burst PreparePickupsJob은 임시 NativeQueue에 의도만 쓴다. 스케줄링 OnUpdate는 managed다. 기존 접수 API의 번호 발급/구조 변경을 위해 이 Job 핸들만 완료하고 임시 의도를 정렬해 접수한다. 명시적인 전역 Complete나 새 공유 인덱스/Fence는 추가하지 않는다. 기존 Submit의 EntityManager 구조 변경에 따른 동기화는 남으므로 전체 파이프라인 비동기화나 성능 개선의 증거로 해석하지 않는다. 큐/정렬 목록/기존 요청 스냅샷은 마지막 접근 뒤 같은 OnUpdate에서 해제한다.

## 검증과 한계

최종 C# 컴파일은 completed/failed=false/errors=[]/compilationFailed=false이며 runtime/Editor DLL 최신성을 확인했다. 관련 선택 EditMode는 **173/173**, 실패/생략/불확정 0이다.

| fixture | 통과 |
| --- | ---: |
| DroneMovementTests | 26 |
| DroneItemTransferTests | 37 |
| DroneLifecycleIntegrationTests | 6 |
| DroneRetargetingTests | 18 |
| DroneTaskSchedulingTests | 52 |
| DroneTaskPlanningTests | 14 |
| DroneStationLifecycleTests | 20 |

기존 이동 통합 fixture에 실제 경로/배정/이동/관측/공개 도착과 자동 픽업을 연결했다. 새 통합 11사례는 네 작업 유형, 도착 틱의 수집 금지, 두 수행자의 일반 반입/반출 3+2 확보 뒤 실제 1+0/0+0 수집과 반환, 외부 요청/결과 소유, 수집 전 배터리0, 실제 벨트 이동 뒤의 낡은 도착 거절/다음 틱 정산을 확인한다. 기존 인계 fixture의 도착 상태/실제 위치/관측/배터리 자격 4사례도 추가했다. 이 15사례는 위 합계에 포함하며 별도로 합산하지 않는다.

기존 실물 인계/건설 통합 fixture는 명시적인 실제 위치/양수 배터리/도착 입력을 준비한다. 실제 이동 실행 증거는 이동 통합 fixture와 구분한다. 목표 이동 회귀는 실제 BeltSpatialSyncSystem이 소유한 맵과 BeltMovementExecutionSystem Writer를 사용하며 전체 Belt Decision이나 GameSimulationGroup의 실행 증거는 아니다.

초기 검사에서는 DynamicBuffer를 NUnit IsEmpty로 검사한 테스트 5건이 실패해 길이 단언으로 고쳤다. 기존 목표 이동 소비자 fixture 1건의 Unavailable 기대는 실제 도착 자격이 먼저 거절되는 현재 Rejected 계약에 맞춰 바꿨고, 전체 이동/작업 흐름에서 후속 배정 정산을 별도로 확인했다. 테스트를 제외하지 않고 최종 전체 관련 묶음을 재실행했다.

Decision/Execution/Reservation/Apply/컴포넌트/guard/가독성/주석/경계의 독립 정적 검수를 수행했다. 낡은 픽업 후속 설명 3곳을 고쳤고 테스트 이름, 두 수행자 경합, 실제 목표 이동 Writer 증거를 보완해 영향 재검수에서 지적 해소를 확인했다. 게임 동작 코드는 이 보완 전후 동일하다.

기존 검증 래퍼는 폐기된 run_tests 명령에서 실패했다. 현재 CLI의 test run/test status 명령으로 위 선택 묶음의 최종 완료를 확인했다. 원본 결과/컴파일/실패/해시/검수 자료는 로컬 Logs/Task911에 보존한다.

최종 Editor는 ready/비컴파일/비리로드/Play stopped이며 콘솔 오류0/경고344다. 경고에는 OnUpdate의 Lookup 생성에 대한 성능 권고와 격리 World의 그룹 순서 경고 등이 남아 있다. 자동 납품/정상 귀환/실제 충전/사용자 진행 중 취소/Play Mode/Player/전체 Burst AOT/성능은 이 완료 범위 밖이다. Unity Editor는 종료하지 않았다.
