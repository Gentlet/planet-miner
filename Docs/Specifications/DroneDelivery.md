# 드론 자동 납품과 정상 완료 계약

## 확정 정책과 범위

2026-10-30 확정한 1A/2A를 적용한다. 1A는 목적지 도착이 EndSimulation에 공개된 다음 틱 별도 타이머 없이 수령 가능한 실물을 한 번에 인계한다는 규칙이다. 납품 자체의 추가 에너지 소비는 없다.

2A는 공개된 AwaitingDelivery와 현재 실제 목적지 위치를 모두 만족하는 경우에만 배터리 0의 납품을 허용한다. 추가 이동/신규 픽업/새 배차에는 이 예외를 적용하지 않는다. 0 배터리 인계 뒤 잔여 화물이 있으면 이전 확보를 정산하고 실물을 보존한 Retargeting/비상 귀환으로 연결한다. 전량 인계는 정상 완료 후 귀환한다. 이는 기존 5A의 귀환 우선 규칙에 명시한 납품 예외다.

현장 공급, 회수품 보관, 일반 반입/반출과 이미 적재한 화물의 유효한 재배정 공급/보관을 자동화한다. 정상 귀환/정박/수납과 수요 기반 출고는 [귀환/출고 계약](DroneReturnAndDispatch.md)에 연결한다. 실제 충전은 [충전 계약](DroneCharging.md), 사용자 진행 중 취소는 [취소/실패 계약](DroneTaskCancellation.md)에 연결한다. 자동 안전 방출과 표시 기반 회수는 [화물 복구 계약](DroneCargoRecovery.md)을 따른다. 추가 작업 유형은 해당 Producer 구현 시 연결한다.

## 요청/실물/공개 소유자

| 경계 | Writer와 소비 |
| --- | --- |
| 이전 EndSimulation | 기존 Movement Apply가 수집/납품 도착을 공개한다. |
| Preparation | 기존 Observation Preparation이 실제 위치/배터리/화물/연결로 관측을 준비한다. |
| DroneDecision | DroneActionRequestDecisionSystem의 병렬 Burst Job이 공개된 수집/납품 의도를 임시 큐에 기록한다. 같은 틱 Task Decision의 배정 무효화 의도를 재사용한다. |
| 같은 Decision의 접수 | 해당 Job만 완료한 뒤 기존 Submit이 World ReceiptSequence를 발급한다. 기존 미처리 요청과 중복하지 않으며 자동 요청에만 DroneAutomaticActionRequest를 붙인다. |
| DroneExecution | 기존 Transfer Execution이 건물 종료 상태의 실물 ID/공간 상한을 준비한다. 일반 운반은 확보 기록 잔량도 상한이다. |
| DroneStateApply | 기존 Lifecycle이 현재 식별/도착/Owner/수령 조건을 재검사하고 공통 Ownership API가 실제 옮긴 양만 정산한다. |
| EndSimulation | 기존 요청/계획 제거와 결과 공개를 확정한다. |
| 다음 DroneDecision | 자동 수행부가 자신의 결과만 소비한다. 외부 결과는 외부 호출자에게 남긴다. |

공통 요청 수행부는 픽업 전용 시스템/표식의 책임을 확장한 것이다. 새 수행자 진행 상태나 별도 결과 Writer는 추가하지 않는다. 이름 변경 때 기존 meta GUID를 유지했다. 자동 행동은 CollectFromStorage/RecoverWorldItem/SupplyConstructionSite/StoreCargo/DropCargo를 생성한다. 안전 방출은 공개 도착/실제 셀을 사용하고 확정된 1A에 따라 추가 이동 없는 0 배터리 방출을 허용한다. 자세한 범위는 화물 복구 계약을 따른다.

이번 틱 새 도착은 다음 틱부터 인계한다. 새 픽업 뒤의 목적지 이동도 다음 틱부터다. 이번 드론의 마지막 납품/바닥 회수에 따른 건물 완공은 다음 건물 틱에 반영한다. 같은 틱 Decision이나 건물 완공을 재실행하지 않는다.

## 도착과 결과 수명

정상 납품은 AwaitingDelivery, 현재 목적지의 회전 footprint 중심, LocalTransform과 관측 위치/버전 일치를 요구한다. MovingToDestination만으로 제출한 직접 요청은 인계하지 않는다. 수집은 기존 양수 배터리 자격을 유지하고, 정상 납품만 유한한 0 배터리까지 허용한다.

기존 배정/revision/행동 번호와 World 접수 순서를 유지한다. 자동 묶음은 OriginalTaskCreationSequence/수행자 Entity 순으로 접수하며 이미 접수한 외부 입력의 순서를 바꾸지 않는다. 자동 요청/결과 표식은 요청 Entity에만 있고 결과 소비와 함께 제거된다. 결과 소비가 실물이나 완료량을 다시 정산하지 않는다.

회수/기존 적재품의 유효한 보관은 원래 작업이 닫혔어도 수행한다. 자동 Producer는 납품에서 원래 작업의 종료 의도만으로 인계를 막지 않고 현재 배정의 무효화 의도를 따른다. 일반 운반의 양수 확보 기록은 해당 Task/유형/목적 방향/Progress/Requirement의 자격을 따로 검증한다.

## 일반 운반 완료량과 대기

실제 보관 성공량으로 해당 DroneTransportReservation.Task의 Progress에 TryRecordDelivery를 적용한다. DeliveredQuantity만 성공분만큼 증가하고 ReservedQuantity/RemainingQuantity는 같은 양만큼 감소한다. 최초 Quantity와 다른 작업의 완료량은 바꾸지 않는다. 같은 두 건물/품목을 가진 반입/반출도 독립 작업이다.

양수 배터리의 부분/0개 수령이 공간 부족이면 잔여 화물과 확보 기록을 유지하고 AwaitingDelivery에서 다음 틱 수령을 기다린다. 목적지의 품목/필터/슬롯 역할에 따른 수령 거부는 실제 실패로 구분한다. 수령 불가/목적지 소멸/0 배터리의 잔여 화물은 남은 확보만 반환하고 기존 화물 보존/재배정 경로를 따른다. 이미 완료한 양은 되돌리지 않는다.

공급원 재고/특정 실물/목적지 공간의 영속 예약은 추가하지 않는다. 슬롯은 건물 종료 시점 상한을 공유하고 성공한 실물만 접수 순서대로 예산을 소비한다. 인계 도중 새로 생긴 공간/실물은 기존 계획에 추가하지 않는다. 일반 운반의 계획 및 최종 인계는 각 확보 기록 잔량으로 제한해 실제 이전 후 완료 정산을 실패시키지 않는다.

화물이 비면 배정은 Completed/연결 해제다. 일반 운반 root는 완료량이 최초 목표량과 같고 확보량이 0임을 다음 Task Decision이 확인한 뒤 Closed를 반영한다. 배정/요청/결과 참조를 정리한 뒤 기존 EndSimulation에 삭제한다. 완료와 즉시 Entity 삭제를 구분한다.

현장 공급과 일반 운반 확보가 없는 기존 보관의 부분 인계는 기존 예약 정산/Retargeting을 유지한다. 이들의 정상 인계도 같은 자동 수행부가 요청하며 별도 Owner를 만들지 않는다.

## Job/검증 경계

스케줄링과 기존 Submit은 managed 경계이며 의도 판단은 병렬 Burst Job이다. 임시 큐/정렬 목록/미처리 요청 스냅샷은 같은 OnUpdate의 마지막 접근 뒤 해제한다. Native 큐를 읽고 구조 변경을 접수하려고 자신의 Job 핸들만 완료한다. 기존 구조 변경에 따른 동기화는 남으며 새 공유 Fence/전역 Complete는 추가하지 않는다.

## 검증과 한계

최종 C# 컴파일은 completed/failed=false/errors=[]/compilationFailed=false이며 runtime/Editor DLL 최신성을 확인했다. 관련 선택 EditMode는 **184/184**, 실패/생략/불확정0이다.

| fixture | 통과 |
| --- | ---: |
| DroneDeliveryTests | 8 |
| DroneMovementTests | 26 |
| DroneItemTransferTests | 40 |
| DroneLifecycleIntegrationTests | 6 |
| DroneRetargetingTests | 18 |
| DroneTaskSchedulingTests | 52 |
| DroneTaskPlanningTests | 14 |
| DroneStationLifecycleTests | 20 |

새 납품 통합 8사례는 실제 경로/이동/픽업/공개 도착/자동 납품을 실행하며 공간 대기/재개와 root 종료/삭제, 두 수행자의 제한된 수령 공간, 실제 거리 소비로 배터리가 정확히0이 된 도착의 전량/부분/0 수령, 서로 다른 작업의 실적 분리, 목적지 소멸 뒤 기존 화물의 재배정 보관, 수령 거부를 확인한다. 기존 이동 통합의 반입/반출 및 공급/회수 4사례도 실제 자동 납품까지 확장했다. 직접 납품 요청의 미공개 도착/실제 위치/관측 거절 3사례를 추가했다. 위 합계에 포함하며 중복 합산하지 않는다.

소멸/수령 거부는 테스트의 명시적 입력이고 모든 실제 철거/설정 변경 Producer를 실행한 증거는 아니다. 기존 인계/재배정/건설 fixture의 수동 도착은 공통 목표점 계산으로 준비한 격리 입력이며 실제 이동 증거와 구분한다. 마지막 납품의 다음 건물 틱 완공 경계는 기존 건설 통합으로 확인하며 같은 틱 완공을 새로 호출하지 않는다.

Decision/Execution/Reservation/Apply/컴포넌트/guard/가독성/주석/경계의 독립 정적 검수를 수행했다. 실행 경로의 결함 지적은 없었다. 낡은 Writer/Producer/결과 소비 설명과 컴포넌트 색인 링크를 고쳤고, 세 fixture의 목표점 수식은 접근 가능한 공통 DroneTargetPointUtility로 옮겨 런타임과 공유했다. 계산 본문은 유지했고 재컴파일/184개 재실행 및 국소 영향 재검수로 보완을 확인했다.

기존 검증 래퍼의 run_tests 명령이 폐기된 것은 앞선 동일 CLI 버전에서 확인했으므로, 현재 test run/test status 명령으로 선택 묶음의 최종 완료를 확인했다. 원본 결과/컴파일/해시/검수 근거는 로컬 Logs/Task912에 보존한다.

최종 Editor는 ready/비컴파일/비리로드/Play stopped이며 콘솔 오류0/경고368이다. 경고에는 기존 Lookup 생성 권고와 격리 World의 그룹 순서 경고 등이 남아 있다. 실제 장면 Play/Player/성능/전체 Burst AOT와 후속 자동 방출/정상 귀환/충전은 수행하지 않았다. Unity Editor는 종료하지 않았다.
