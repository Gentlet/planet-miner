# 드론 상태 전이와 Lifecycle Owner 계약

## 목적과 완료 범위

2026-10-15 사용자 확정 1A·2A·3A·4A·5A·6A를 반영한 Task 9.1의 계약이다. 상태 전이표, 상태/부수 효과의 소유자, 이벤트와 공개 경계, 후속 검증 기준을 확정한다. 이번 작업은 문서 계약의 완료이며 실제 정거장·수행자 생성·이동·배터리·충전·수납 시스템을 구현한 것은 아니다. 새로운 C# enum·컴포넌트·시스템도 추가하지 않는다.

현재 구현의 필드·호출 관계는 [드론 컴포넌트 계약](../CodeMemory/Components/DroneLogistics.md), 공사/회수 규칙은 [건설·공급 명세](ConstructionAndDroneSupply.md), 전력의 배정량 확정 소비는 [전력 명세](PowerGrid.md)를 따른다. 아래 후속 소유자는 구현할 책임을 뜻하며 같은 이름의 시스템이 이미 존재한다는 뜻이 아니다.

## 상태를 구분하는 기준

| 구분 | 원본과 현재 상태 | 책임 |
| --- | --- | --- |
| 상위 작업 | `DroneLogisticsTask.State`의 Open/Closed/Cancelled. Cancelled는 명시 취소를 보존하고 재취소를 거부한다. 현장·품목별 공급 또는 실물별 회수 요구다. | 작업이 필요한지와 종료를 관리한다. 한 작업에 여러 배정이 연결될 수 있다. |
| 개별 배정 | `DroneTaskAssignment.State/NextAction`, 대상·수량·Revision. | 한 수행자의 수집/인계·완료/취소·적재품 재배정을 관리한다. |
| 수행자 연결 | `DroneWorkerAssignment.Assignment`. | 수행자가 현재 수행하는 배정을 가리킨다. 배정 종료와 배정 Entity 삭제는 별개다. |
| 화물 | `StoredItemElement`와 `ItemOwnership`, 출처는 `DroneCargoState`. | 실제 실물의 종류·수량·Owner가 원본이다. 배정 수량으로 실물을 대체하지 않는다. |
| 예약 | 개별 `ConstructionSupplyReservation`과 현장 `ReservedQuantity`, 미공개 예약 기록. | 공급 예정량을 관리한다. 공급원 재고·일반 보관 공간의 영속 예약을 새로 만들지 않는다. |
| 드론 자체 상태 | 활동 원본은 DroneActivityState이며 비정박 이동/충전 경유 대기/최소 비상 귀환은 Movement가, 정박 충전/대기는 Charging Execution이 작성한다. 정박은 DroneDocked, 수납은 Item 역할 전환이다. 정상 귀환/정거장 전환과 수요 기반 출고는 [귀환/출고 계약](DroneReturnAndDispatch.md)을 따르며 실제 충전은 [충전 계약](DroneCharging.md)을 따른다. | 작업 상태를 복제하지 않고 수행자의 활동·보관 전환을 관리한다. |
| 배터리/관측 | 현재량 원본은 `DroneBatteryState`의 가용 `CurrentCharge`와 충전된 미세 잔량 `FractionalCharge`다. 경로/배차는 가용값을 읽고 충전 정산은 잔량을 보존한다. DroneSpawnUtility가 호출자의 EndSimulation ECB에 최초 현재량과 입력 위치/Revision 1/CanAcceptTask=false 관측을 기록하고 기존 배터리는 보존한다. | 지속 이동 소비는 Movement Execution, 관측은 다음 Preparation이 소유한다. 정박 배터리 증가는 DroneChargingExecutionSystem이 소유하고 다음 틱 판단에 반영한다. 실제 결과를 관측과 배차 자격에 연결하며 관측값만으로 배터리를 만들지 않는다. |

`Pending → Reserved → Assigned → PickingUp → Delivering → Completed`는 후보·예약·배정·행동의 흐름을 설명하는 말이다. 이를 `DroneLogisticsTask.State` 하나에 합치지 않는다. Movement가 공개 배정으로 비행하고 도착 Apply가 `AwaitingCollection/AwaitingDelivery`를 EndSimulation에 기록한다. 자동 픽업은 [픽업 계약](DronePickup.md)에 연결했고 자동 납품은 [납품 계약](DroneDelivery.md)에 연결했다. 수납은 화물의 `StoreCargo`와 구분한다.

## 확정한 게임 정책

| 정책 | 확정 규칙 |
| --- | --- |
| 1A 수납 | 완충, 활성 배정 없음, 이동·충전·충전 대기 등 진행 중 동작 없음, 적재품 없음, 정거장 보관 공간 있음의 모든 조건을 만족할 때만 수납한다. 정거장 도착이나 충전 접속만으로 숨기거나 보관 아이템으로 전환하지 않는다. |
| 2A 완충 전 배차 | 완충 전에도 실행 가능한 작업은 배차한다. 경로는 충전소 경유를 포함하며 다음 이동 구간에 필요한 에너지를 확보해야 한다. 현재 배터리로 전체 작업과 귀환을 한 번에 마쳐야 한다는 제한은 추가하지 않는다. |
| 3A 연속 배차 | 작업 종료 후 현재 위치에서 수행 가능한 다음 작업을 우선한다. 없거나 충전이 필요하면 귀환한다. 작업마다 정거장 수납/재출고를 강제하지 않는다. |
| 4A 정상 귀환 | 같은 드론 네트워크의 수용 가능하고 실제 경로가 가까운 정거장을 우선하고, 없으면 다른 사용 가능한 정거장을 찾는다. 거리 동률은 기존 PlacementStamp 비교를 재사용한다. 모두 불가능하면 활성 상태로 대기한다. 전력망과 드론 네트워크를 같은 것으로 취급하지 않는다. |
| 5A 배터리 0 | 신규 작업을 차단하고 설정의 비상 속도로 정거장 귀환만 허용한다. 공개 도착과 실제 목적지 위치를 만족하는 추가 이동 없는 납품만 2A 예외로 허용한다. 배터리를 음수로 차감하거나 비상 이동을 충전으로 간주하지 않는다. 적재품을 보존하며 귀환 가능한 정거장이 없으면 활성 상태로 대기한다. |
| 6A 실제 작업 중단 | 배터리·경로 문제로 기존 작업 수행 불가가 확정되면 이전 현장 예약을 해제한다. 빈 수행자는 배정을 취소하고, 적재품이 있으면 연결과 실물을 보존한 Retargeting으로 전환한다. 경로 평가 대기와 계획된 충전 경유는 중단으로 취급하지 않는다. |

정상 귀환 중 재배차와 보관 아이템의 작업 수요 기반 자동 출고는 2026-10-30 확정한 [귀환/출고 계약](DroneReturnAndDispatch.md)의 1A/2A를 추가로 따른다. 아래 Task 9.1의 정책 번호와 구분한다.

정거장의 수용 가능성은 [Task 9.3의 보관 소유자](DroneIdentityAndStationStorage.md)의 전용/일반 슬롯과 실제 보관/정박, 이번 틱 승인분으로 판단한다. 사용자 확정 전용 5슬롯/일반 공간 공유를 적용한다. DroneConfig는 적재량/최대 배터리/이동과 비상 속도/거리 소비/충전 속도와 드론당 전력을 게시하며 충전 수치/분담/틱 경계는 [충전 계약](DroneCharging.md)을 따른다.

## 기존 작업·배정의 전이

| 현재 상태/상황 | 조건·입력 | 다음 상태와 부수 효과 | 소유자·공개 | 구현 구분 |
| --- | --- | --- | --- | --- |
| 상위 작업 없음 | Decision이 공급/회수 필요를 판단 | 생성 의도를 기록하고 양수 생성 순번의 Open 작업을 생성 | TaskExecution이 EndSimulation 기록. 다음 틱부터 후보화 | 현재 구현 |
| Open | Decision의 종료 의도와 현재 필요/대상 무효가 일치 | Closed. 연결 배정과 화물의 정리를 별도로 진행 | TaskLifecycle이 상태 반영 | 현재 구현 |
| 일반 운반 Open | 완료량==최초 목표량이고 확보량0을 다음 Decision에서 확인 | CompleteTransportTask 의도 후 Lifecycle이 최신 수량을 재검사해 Closed. 배정/확보/행동 참조 정리 뒤 EndSimulation 삭제 | Decision/TaskLifecycle/기존 EndSimulation | 실제 납품 후 다음 Decision의 종료 연결까지 구현 |
| 배정 없음 | 후보 선택·수량 예약 후 최종 공개 검사 성공 | MovingToSource, Revision=1, 개별 예약과 수행자 연결 | AssignmentPublish가 같은 EndSimulation ECB에 기록 | 현재 구현 |
| MovingToSource / AwaitingCollection | 현재 버전의 수집 요청으로 실제 양수 수집 | 실제 수집량으로 AssignedQuantity/화물 출처를 갱신하고 MovingToDestination. 미수집분 현장 예약 해제 | TaskLifecycle의 행동 반영 | 현재 구현 |
| MovingToSource / AwaitingCollection | 실제 수집량 0 | Cancelled, 현장 예약 정산, 수행자 연결 해제 | TaskLifecycle의 행동 반영 | 현재 구현 |
| MovingToDestination / AwaitingDelivery | 공급·보관·안전 방출의 실제 인계 후 적재품 없음 | Completed, 기존 목적지 예약 정산, 화물 출처/수행자 연결 해제 | TaskLifecycle의 행동 반영 | 현재 구현 |
| MovingToDestination / AwaitingDelivery | 실제 인계 후 잔여 적재품 있음 | Retargeting, 기존 목적지 예약 정산. 남은 실물·출처·수행자 연결 유지 | TaskLifecycle의 행동 반영 | 현재 구현 |
| Open / 실행 중 배정이 남은 Closed root | 사용자 취소 승인 | root Cancelled. 빈 배정은 Cancelled/연결 해제, 적재 배정은 Retargeting/화물 보존 | RequestDecision 승인 → Reservation 잔량 반환 → TaskLifecycle 내부 상태/EndSimulation 결과와 차단 | 현재 취소/실패 계약. Cancelled root의 재취소는 거부 |
| 유효하지 않은 배정 | 대상/작업 무효, 적재품 없음 | Cancelled 및 연결 해제. 예약·요청·결과 정리 전 배정 Entity 보존 | Reservation/TaskLifecycle의 각 정산 책임 | 현재 구현 |
| 유효하지 않은 배정 | 대상/작업 무효, 적재품 있음 | Retargeting과 연결 유지. 기존 예약 해제 후 새 목적지 계획 | Reservation/TaskLifecycle | 현재 구현 |
| Retargeting | 현재 정책에 맞는 목적지·경로·수량 확정 | 같은 배정 Entity의 Revision 증가, 최초 작업 순서와 화물 보존, MovingToDestination | AssignmentPublish가 EndSimulation 기록 | 현재 구현 |
| Completed / Cancelled | 화물·잔여 예약·미처리 요청/결과 참조 없음 | 연결 해제 확인 후 배정 삭제 | TaskLifecycle이 EndSimulation 기록 | 현재 구현 |
| 배정 실행 중 | 배터리·수행 경로의 실제 중단이 확정 | 6A에 따라 Cancelled 또는 Retargeting. 이전 예약 정산 후 귀환/회복 흐름 | 후속 수행부 입력 → Decision 종료 의도 → 기존 Reservation/TaskLifecycle에 연결 | 현재 이동 실패/명시 취소는 [취소/실패 계약](DroneTaskCancellation.md). 추가 유형은 해당 Producer 후속 |
| 추가 직접 작업 | 현재 대상에 실제 도착하고 작업 행동 성공/실패가 확인됨 | 성공 시 완료, 실패/취소 시 정상 정리. 화물이 있으면 보존 경로 적용 | 추가 작업의 Decision/Execution과 기존 Lifecycle을 연결. 건물 변경은 건물 소유자에 요청 | Task 9.17 등 후속 |

작업 Closed/Cancelled나 배정 Completed만으로 Entity를 즉시 삭제하지 않는다. 실물 인계 성공량과 예약 정산, 연결 해제, 미소비 요청/결과 참조를 별도로 확인한다. 재배정 후 과거 Revision의 종료·행동 이벤트를 새 배정에 적용하지 않는다.

## 후속 드론 자체 상태 전이

아래 상태명은 후속 상태표의 개념 이름이며 현재 C# enum이 아니다. `작업 수행`은 기존 배정 상태로 판별하고 같은 이동/납품 상태를 드론 자체 상태에 중복 저장하지 않는다.

| 현재 상태 | 전이 조건 | 다음 상태·행동 | 제한/효과 |
| --- | --- | --- | --- |
| 수납 | 유효한 출고·배차 요청과 정거장 수용/Identity 조건 충족 | 활성 대기 또는 공개된 배정의 수행 | 같은 Identity를 유지한 전환. 이중 활성화·보관 중 배차 금지 |
| 활성 대기 / 작업 종료 | 현재 위치에서 실행 가능한 다음 작업과 배차 자격 있음 | 배정 공개 후 작업 수행 | 완충을 요구하지 않으며 기존 후보 우선순위 유지 |
| 활성 대기 / 작업 종료 | 다음 작업이 없거나 충전 필요, 수용 가능한 정거장 있음 | 정상 귀환 | 4A 선택을 적용하고 적재품을 임의 삭제하지 않음 |
| 정상 귀환 | 현재 위치에서 실행 가능한 다음 작업 또는 적재품 재배정이 선택됨 | 귀환 중단 후 기존 배정 공개/작업 수행 | 귀환/출고 계약 1A. 비상 귀환과 기존 작업의 충전 경유에는 적용하지 않음 |
| 정상 귀환 | 목적 정거장 만석·소멸·도달 불가 | 대체 정거장으로 귀환 또는 정거장 대기 | 같은 네트워크 우선, 이후 다른 정거장 |
| 정상 귀환 / 비상 귀환 | 정거장에 실제 도착, 배터리 부족 | 정박 충전 대기 | 도착과 수납을 구분하고 활성 상태 유지 |
| 정박 충전 대기 | 충전 후보로 준비됐고 Power의 양수 공급 확보 | 충전 | 확정된 배정 에너지 한도만 반영 |
| 충전 | 전력 공급 0, 아직 미완충 | 정박 충전 대기 | 무전력은 정상 대기이며 자동 완충·수납하지 않음 |
| 정박 충전 대기 / 충전 | 다음 작업 경로가 실행 가능하고 다음 구간 에너지 확보 | 현재 위치에서 재배차 | 완충 전 가능. 기존 배정이 있으면 계획된 경유의 재개로 구분 |
| 충전 | 최대 배터리 도달 | 충전 의도 종료 후 활성 대기·재배차·수납 판단 | 상한 초과 충전 금지. 완충만으로 수납하지 않음 |
| 정거장에 있는 활성 대기 | 1A의 수납 조건 전부 충족 | 수납 | 정거장 소유자와 Identity/Owner/버퍼를 같은 공개 경계에서 반영 |
| 정거장에 있는 활성 대기 | 수납 조건 일부 미충족 | 활성 상태 유지 | 미완충·활성 배정·진행 중 동작·적재·보관 공간 부족 중 하나라도 있으면 수납 금지 |
| 작업 수행 / 정상 이동 | 배터리 0 | 중단 정산 후 비상 귀환 또는 정거장 대기 | 5A·6A 적용. 신규 작업·기존 적재품의 새 작업 재배정 차단 |
| 비상 귀환 | 경로상의 정거장 소멸·수용 불가 | 가능한 정거장 재선택 또는 정거장 대기 | 비상 귀환용 이동만 허용하고 화물 유지 |
| 정거장 대기 | 정거장/경로/수용 가능성 회복 | 정상 또는 비상 귀환 | 대기 중 자동 파괴·수납·화물 폐기 금지 |
| 계획된 충전 경유 / 경로 평가 대기 | 실패가 확정되지 않음 | 기존 상태/배정의 대기·재개 | 6A 중단으로 오인해 예약을 반복 해제하지 않음 |

`CanAcceptTask`는 현재 빈 수행자의 신규 배정 자격이다. 이것만 false로 바꿔도 기존 배정이나 적재품 재배정이 중단되지는 않는다. 현재 Observation Preparation은 귀환/충전 이동 상태를 신규 배정 자격에 반영하고, IsRetargetingWorker는 이동 상태의 충전/귀환 목적지와 배터리를 별도로 검사해 적재 재배정 후보에서도 제외한다. 수납 전환 자격은 정거장 Lifecycle이 검사한다. 비상 귀환/정거장 대기 중에는 에너지 고갈을 이유로 기존 일반 안전 방출을 자동 시작하지 않는다. 회복 후에는 기존 적재품 재배정·안전 방출 규칙을 재사용한다.

## Lifecycle Owner와 상태 변경 책임

| 원본/부수 효과 | 소유자와 책임 | 페이즈·공개 경계 | 상태 |
| --- | --- | --- | --- |
| 작업 생성/무효화/목표 달성 필요, 경로/배정 후보 | DroneTaskDecisionSystem. 판단 의도만 작성 | DroneDecision | 구현 |
| 현장 예약과 공개 대기 예약 | DroneTaskReservationSystem의 메인 스레드. 통합 후보 선택/현장 미공개 정산/예약 해제 | DroneReservation. 최종 Publish 축소/실패와 실제 인계 정산은 기존 공통 예약 API 사용 | 구현 |
| 일반 운반 목표 확보/미인계 승인/무효 인계 정산 | DroneTaskReservationSystem의 단일 Burst Job. Decision 의도/고아 확보분/이전 미인계분 반환 후 통합 선택된 일반 운반만 승인 | DroneReservation의 단일 Burst Job. 최초 Progress는 Task Execution과 같은 EndSimulation에 생성 | 구현. 배정 Publish의 인계와 Lifecycle의 실제 납품/완료량 정산이 연결됨 |
| 작업·경로 요청 생성 순번/ECB 기록 | DroneTaskExecutionSystem. 전달 의도를 적용하고 필요를 다시 탐색하지 않음 | DroneExecution → EndSimulation | 구현 |
| 실제 인계할 실물/공간 계획 | DroneItemTransferDecisionSystem / DroneItemTransferExecutionSystem. 원본 소유권·수량을 변경하지 않음 | DroneDecision / DroneExecution OrderFirst | 구현 |
| 상위 작업 종료, 배정 행동 결과·취소·완료·재배정 필요 | DroneTaskLifecycleApplySystem. 최신 의도/버전과 실제 성공분 반영 | DroneStateApply 직접 반영. 요청 제거·결과 추가·종료 삭제는 EndSimulation | 구현 |
| 배정 생성/재배정 공개, 수행자 연결 설정 | DroneTaskAssignmentPublishSystem. 최종 수량 검사/감량 및 거부 반환/일반 운반 Take/Revision 갱신과 정산된 이전 운반 확보 기록 분리 | DroneStateApply OrderLast → 새 배정과 같은 EndSimulation | 구현 |
| 수행자 연결 해제, 잔여 예약/요청/결과에 따른 삭제 보류 | DroneTaskLifecycleApplySystem. 해제와 배정 삭제를 분리 | DroneStateApply / EndSimulation | 구현 |
| 실물 Owner·출발/도착 버퍼·위치·렌더 | ItemOwnershipApplySystem의 공통 TryTransferItem. Drone Lifecycle이 실제 성공분 사용 | 일반 건물 반영 이후 DroneStateApply, 필요한 렌더 기록은 EndSimulation | 구현 |
| 외부 행동 결과 소비 | DroneActionRequestUtility.TryConsumeResult의 외부 Consumer | EndSimulation 공개 이후. 예약·실물 성공량 재정산 금지 | 자동 픽업/납품은 자기 요청 결과만 소비하며 외부 Consumer API는 유지 |
| 수행자 등록/정박/수납과 Identity 전환 | DroneStationLifecycle D/R/Apply. 같은 Entity와 보관 관계를 반영하며 작업/예약을 직접 중복 정산하지 않음 | DroneDecision/Reservation → DroneStateApply의 작업 Publish 기록 뒤 → EndSimulation | Task 9.3의 전환을 귀환/출고 자동 요청에 연결. 철거 재배치는 9.19 후속 |
| 실제 이동 위치·도착/실패 근거 | Movement Execution이 실제 거리/도착/중단을 제공하고 Movement Apply가 같은 배정의 도착 상태만 기록 | DroneExecution. 인계 신호는 기존 외부 접수/공개 계약에 연결 | Task 9.10 |
| 배터리 원본 | Movement Execution은 비정박 이동 소비를, 그 뒤 Charging Execution은 정박 증가량을 반영한다. 정박 관계로 쓰기 대상을 분리하며 같은 수행자에 두 처리를 중복하지 않는다 | DroneExecution. 후속 배차 자격 관측은 확정된 값을 사용 | Task 9.10·9.14 |
| 실제 위치 기반 관측·CanAcceptTask | DroneWorkerObservationPreparationSystem이 마지막 확정 이동/배터리/수행자 상태로 관측을 준비 | 기존 Observation Revision/외부 입력 계약 준수. 다음 Decision에 최신 입력 제공 | Task 9.3·9.10·9.14 |
| 충전 자격/수요 근거 | DroneChargingPreparationSystem이 정거장 준비 버퍼를 작성하고 기존 BuildingPowerDemandPreparationSystem만 합산 수요를 작성 | Preparation. 충전 수요는 Power 이전에 준비 | Task 9.14 |
| 전력 배분·연료·확정 소비 | 기존 Power 시스템. 배정량은 이번 틱 소비이며 환급/재배분 없음 | Power → EndPower. Drone은 공개 공급만 사용 | 구현 |
| 드론 직접 철거 등 건물 변경 | 후속 드론 작업은 실제 행동 결과만 전달. 승인/반환/환급/삭제는 기존 건물 소유자가 담당 | 기존 Command/EndBuilding 경계 유지. Drone이 건물 원본을 직접 삭제하지 않음 | Task 9.17 |

기존 Lifecycle과 Publish의 역할 차이는 의도된 분리다. 하나의 새로운 통합 상태 소유자가 작업·배정·예약·화물·드론·배터리를 전부 중복 소유하지 않는다. 후속 역할의 구체적 시스템/컴포넌트 이름은 각 구현 Task에서 정하되 위 Writer와 공개 경계를 지킨다.

## 이벤트·전이 순서와 금지 사항

1. 외부 수행부는 현재 배정과 관측을 읽고 기존 ActionIdentity/Revision/ActionSequence로 행동을 제출한다. 실물 인계는 World 접수 순서이며 실제 성공을 외부가 미리 기록하지 않는다.
2. DroneDecision은 후보·종료·경로 의도를 작성한다. Reservation이 먼저 기존 미공개/무효 예약을 정산하고 후보 수량을 예약한다. Lifecycle은 행동 반영 뒤 최신 상태로 종료 의도를 재검사한다.
3. 실제 작업 중단의 배터리/경로 사유는 후속 수행부가 제공하고 Decision의 종료 의도·무효화 검사에 연결한다. 현재 코드에 없는 배터리 실패 이벤트를 이미 처리한다고 설명하지 않는다.
4. 중단 시 기존 목적지 예약을 정산하고, 빈 수행자는 연결을 해제한다. 적재품이 있으면 같은 배정·최초 작업 순서·출처를 보존해 Retargeting으로 둔다. 새 목적지 예약은 다음 선택/Publish가 소유한다.
5. 화물/잔여 예약/행동 요청/미소비 결과가 배정을 참조하면 삭제를 보류한다. 과거 배정의 지연 결과가 새 작업의 적재품이나 예약을 정산하지 않도록 Revision과 행동 번호를 검사한다.
6. 실제 인계·중단·충전 결과를 이유로 같은 틱 Decision을 다시 돌리지 않는다. 새 작업·배정·경로는 EndSimulation에 공개되고 다음 틱부터 이용한다. 다음 작업을 현재 위치에서 선택한다는 3A도 이 경계를 따른다.
7. 충전 수요는 Preparation에서 준비하고 Power가 소비를 확정한 뒤 배터리에 반영한다. 이번 틱 새로 귀환/접속한 드론은 다음 틱 충전 수요부터 참여한다. Drone 처리 중 새 배차/귀환이 생겨 배정 전력을 쓰지 못해도 환급하지 않는다. 같은 틱 후반에 생긴 수요를 앞선 Power에 소급 반영하지 않는다.
8. 전력 없음·미평가 경로·정거장 만석·배터리 부족은 정상 대기/귀환/중단 정책의 입력이다. 그 자체를 공통 Fatal로 처리하지 않는다. 공통 Fatal의 NextTick/RemainingDomains 정책과 현재 틱 rollback 부재는 기존 계약을 유지한다.

경로/충전 경유 거리와 에너지 계획, 실제 이동/관측/도착과 최소 비상 귀환은 [이동 명세](DroneMovement.md)에 연결했다. 자동 픽업은 [픽업 계약](DronePickup.md)을 따르며 자동 납품은 [납품 계약](DroneDelivery.md)을 따르며 실제 충전은 [충전 계약](DroneCharging.md)을 따른다. 빈 수행자 배차와 적재 재배정 모두 실행 가능한 경로를 사용해야 하며, 단순 직선 거리·임의 배터리 기준으로 성공을 만들지 않는다. 정거장 도착, 충전 접속, 충전 성공, 수납 성공, 작업 성공은 서로 다른 이벤트/조건이다.

## 전이 검증 기준과 근거

| 검사 상황 | 기대 결과 | 근거/실행 범위 |
| --- | --- | --- |
| 수집/납품 부분 성공 또는 0개 성공 | 실제 성공량만 정산. 남은 실물 보존, 기존 목적지 예약 해제/축소, Completed/Cancelled/Retargeting 구분 | 현재 DroneItemTransferTests의 부분 수집·부분 보관·초과 공급 사례와 소스 대조 |
| 중복·과거 Revision 행동, 미소비 결과 | 이중 인계/예약 정산 금지. 필요 참조가 있는 배정 삭제 보류 | 현재 DroneItemTransferTests / DroneRetargetingTests와 소스 대조 |
| 미평가 경로·도달 불가·외부 방출 셀 미확정 | None은 대기. 기존 안전 방출 조건 미충족 시 화물 보존 | 현재 DroneRetargetingTests와 소스 대조 |
| 미완충·충전 중·충전 대기·활성 배정·적재품·공간 부족 중 하나라도 있음 | 수납 금지. 충전 접속/도착을 수납으로 취급하지 않음 | 후속 9.3·9.13·9.14 테스트 필요 |
| 완충, 빈 적재, 무배정, 무동작, 보관 공간 있음 | 한 번만 Identity/보관 전환. 완충 후 충전 대기 잔존 금지 | 후속 9.3·9.13·9.14 테스트 필요 |
| 완충 전이나 충전 경유 경로의 다음 구간 실행 가능 | 현재 위치 재배차 허용. 전체 경로 에너지를 현재 배터리에 한 번에 요구하지 않음 | 후속 9.10·9.13·9.14 테스트 필요 |
| 정상 귀환 정거장 만석·소멸·전부 수용 불가 | 같은 네트워크→다른 정거장→활성 대기. 이중 수납·Identity 손실 금지 | 후속 9.2·9.3·9.13·9.19 테스트 필요 |
| 배터리 0, 적재 있음/없음, 귀환 정거장 있음/없음 | 비상 귀환 또는 활성 대기. 배터리 음수·신규/적재 재배정·에너지 고갈에 따른 자동 화물 폐기 금지 | 후속 9.10·9.13·9.15 테스트 필요 |
| 작업 중단 확정과 단순 평가/충전 대기 비교 | 확정 중단만 6A 정산. 대기를 취소로 오인하지 않음 | 후속 9.10·9.14·9.15 테스트 필요 |
| 부분 전력·무전력·충전 중 배차 | 공급/배터리 상한 준수, 무전력 자동 충전 금지, 미사용 전력 환급 금지 | 후속 9.14 테스트 필요 |
| 종료·수납·충전·직접 작업 결과의 중복/지연 도착 | 같은 전이를 한 번만 반영하고 과거 결과가 현재 배정/소유권을 변경하지 않음 | 해당 후속 Task의 버전/접수 순서 테스트 필요 |

이번 Task 9.1에서는 현재 타입·쓰기 경로·기존 테스트 정의와 표의 정합성을 정적으로 대조했다. 위 후속 기대 결과는 새 실행 성공 기록이 아니다. 코드·에셋을 변경하지 않았고 Unity 컴파일·EditMode·Play Mode·베이킹·Player 빌드·성능 검증을 실행하지 않았다. 기존 드론/전력 테스트 수치를 이번 상태표의 실행 증거로 합산하지 않는다.

## 후속 Task 연결

- 9.3.1: [드론 Authoring·DB·생성 초기화](DroneCreation.md)는 구현했다. 실제 관측/이동·제품 배차·Identity 전환·수용량의 완료와 구분한다.
- 9.2의 [정거장 등록/범위/네트워크](DroneStationNetwork.md)와 9.3의 [동일 Entity 전환/정박/수납/수용량](DroneIdentityAndStationStorage.md), [6A 초기화](DroneCapacityInitializationPlan.md)를 구현했다. 실제 경로/이동/도착과 배터리 0의 최소 비상 귀환 목적지 선택은 Movement가 제공한다. 정상 귀환/정박/수납과 수요 기반 출고는 [귀환/출고 계약](DroneReturnAndDispatch.md)을 따른다. 실제 충전은 [충전 계약](DroneCharging.md)을 따른다.
- 9.10/9.13: 실제 이동/거리 소비/관측과 정상/비상 귀환, 재배차/정박/수납/수요 기반 출고를 연결한다. 9.14는 실제 Power 기반 충전과 충전 완료 자격을 이 기존 흐름에 연결한다.
- 9.15는 현재 네 종류의 명시 취소/기존 실패 정산을 [취소/실패 계약](DroneTaskCancellation.md)에 연결했다. Open뿐 아니라 비종료 배정이 남은 Closed root를 취소하고, 같은 틱의 미수행 이동/인계를 막는다. 9.16의 실제 안전 방출/추가 복구는 후속이며 복구를 이유로 실물을 재생성하지 않는다.
- 9.17·9.19·9.20: 추가 작업·정거장 철거의 재배치와 전체 흐름 검증. 기존 정거장 철거의 전체 수용량 선검사/불충족 시 철거 보류 규칙은 유지한다.
- 10B.3·10B.4: 충전 대기·충전·수납·배정/작업 상태를 구분해 표시한다. 화면 비표시를 상태 전환 원본으로 사용하지 않는다.

자동 픽업은 공개 도착 다음 틱 현재 위치/관측/양수 배터리로 시작한다. 수집 직전 배터리 0이나 수집점 변경은 5A/6A의 실제 중단 경로로 정산한다. 일반 운반 픽업은 미수집 확보량을 반환하고 실제 자동 납품/완료량은 [납품 계약](DroneDelivery.md)에 연결했다.

일반 운반은 양수 배터리에서 공간 부족의 잔여 화물/확보량을 유지하고 같은 목적지에서 수령을 기다린다. 품목/필터/슬롯 역할의 수령 거부, 목적지 소멸, 0 배터리의 잔여 화물은 실제 중단으로 정산한다. 실제 보관 성공량은 확보 기록의 Task 하나에만 귀속하고 원래 목표량은 바꾸지 않는다.
