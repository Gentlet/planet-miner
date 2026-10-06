# 건설 현장과 드론 자재 공급 명세

## 문서 목적과 상태

2026-10-06 안전 방출 정책은 현장 내부의 새 월드 생성/드론 방출을 금지하고 예정 위치 기록을 제거했다. Construction은 현재 월드 Owner/GridPosition과 활성 Destroy만 읽는다. 방출은 현재 외부 셀 또는 외부 평가자의 가장 가까운 실제 도달 가능한 현장 외부 셀을 선택한다. None/Unreachable이면 적재 대기하며 선택 DropPosition은 실제 위치 원본이 아니다. 현재 근거는 [안전 방출·기록 제거 검증](../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/DroneSafeDropAndJournalRemoval-Verification.md), 앞선 기록은 당시 근거다. 실제 수행부·관측/경로/초기 능력/연구 Writer는 후속이다.

이 문서는 2026-10-04까지의 대화에서 사용자가 확정한 건설 현장·드론 자재 공급·회수 규칙과 2026-10-05 phase·공통 적재량·행동 신호 접수 순서 계약을 기록한다. 배치 승인부터 자재 확보, 운반, 수령, 완공 또는 취소까지의 게임 동작을 설계 기준으로 삼고 현재 구현 단계와 구분한다.

- **상태:** 드론 관리 1~3단계와 2026-10-05 이전 틱 입력의 인계 판단·실행 계획 분리를 반영했다. 요청 자격은 Decision, 이번 물류 쓰기 전 실물 ID·수량·기존 슬롯 계획은 Execution, 계획의 현재 원본 재검사·실제 인계/정산은 StateApply가 담당한다. 이번 틱 새 입고·새 수집 적재품·출고 공간은 다음 틱부터 사용한다. 적재품 재탐색·바닥 차단 갱신은 4단계에서 연결했으며 실제 수행자·관측 원본·이동·경로 계산은 후속이다.
- **현재 구현:** [ECS 컴포넌트 색인](../CodeMemory/Components/README.md)과 [현재 공사 컴포넌트](../CodeMemory/Components/Construction.md)는 현재 소스를 설명한다. [기존 운송 제거 검증](../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/ConstructionTransportRemoval-Verification.md)은 당시 실행 근거이며 이번 3단계는 별도 검증 기록을 따른다. 아래 게임 규칙 전체가 구현되었다는 뜻이 아니다.
- **관리 계층의 완료 범위와 근거:** 1~4단계 작업·예약·인계·재배정·안전 방출·완공 차단과 5단계 선별 통합 회귀를 마쳤다. 현재 드론 ECS 타입 22개의 역할과 처리 순서는 [컴포넌트 계약](../CodeMemory/Components/DroneLogistics.md)을 따른다. [기존 142사례](../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/DroneSafeDropAndJournalRemoval-Verification.md)와 [신규 통합 4사례](../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/DroneLifecycleIntegration-Verification.md)는 별도 실행 근거다. [이전 틱 인계 검증](../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/DronePreviousTickTransfer-Verification.md)과 [3단계 최초 검증](../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/DroneItemTransferStep3-Verification.md)은 당시 구현의 근거로 보존한다.

## 이번 구현 범위

2026-10-04 추가 확정: 배치 시스템은 드론 순번·작업 생성을 관리하지 않는다. Command는 사용자 조작이나 Mono 등 외부 요청을 처리하는 경계로 둔다. 자동 공급·회수 작업은 EndStateApply에 공개되며 다음 시뮬레이션의 Decision부터 배정 판단할 수 있다. 같은 시뮬레이션에서 작업 생성과 배정을 모두 끝낼 필요는 없다.

2026-10-05 추가 확정: Decision은 작업 생성·종료와 경로 생성/유지/삭제 의도, 배정 후보를 판단한다. Reservation은 현장 수량 예약·해제를 담당한다. Execution은 확정된 작업 생성·경로 명령을 소비하고 생성 순번과 EndStateApply 기록을 소유한다. StateApply의 Lifecycle은 종료 의도를 최종 재검사하여 작업/배정 상태·수행자 연결·삭제 보류를 반영한다. 최종 배정 재검사·공개와 실패 시 예약 롤백은 기존 Publish에 유지한다. 앞선 StateApply 생성 구조는 [phase 분리 검증](../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/DronePhaseSeparation-Verification.md), 현재 책임 분리는 [Execution 분리 검증](../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/DroneExecutionSplit-Verification.md)을 따른다.

Execution 이후 상태가 달라진 작업·경로는 다음 Decision에서 재검사하여 무효 후보의 배정을 막는다. 작업 생성이나 경로 요청 공개만으로 실제 드론이 이동하거나 자재를 인계한 것으로 처리하지 않는다.

2026-10-05 추가 확정: 작업·배정 무효화 판단 결과는 DroneTaskInvalidationDecisionElement로 전달한다. DroneTaskCandidateDecisionElement의 후보 부분은 Decision 결과이며 Reservation의 승인·공개 대기 예약과 Publish의 공개 기록도 같은 행에 유지한다. DroneWorker는 수행자 표시만 맡고 모든 드론의 최대 적재량은 World singleton DroneCapacityState가 소유한다. 초기화·연구 갱신 Producer는 아직 없으며 값이 없거나 0 이하이면 신규 배정은 대기한다.

3단계 추가 확정: 같은 틱 재고·공간을 사용하는 행동 신호는 먼저 접수된 순서로 적용한다. 시뮬레이션 전 외부 입력 경계의 DroneActionRequestUtility.Submit이 World 공통 ReceiptSequence를 발급하며 배정 내부 Action.Sequence는 중복 거절에 별도로 사용한다. StateApply의 인계 결과는 EndStateApply 이후 시뮬레이션 외부 또는 다음 틱 외부 경계에서 TryConsumeResult로 읽고 삭제한다. 미소비 요청·결과는 배정 삭제를 보류한다. 실제 수행부의 신호 생성과 API 호출 연결은 후속이다.

이전 틱 입력 추가 확정: 행동 신호의 판단과 실행 계획은 이번 물류 쓰기 전에 확정된 입력만 사용한다. Submit은 요청에 DroneItemTransferDecision과 실물 ID·슬롯 계획 버퍼를 부착한다. Decision은 초기 자격, Execution OrderFirst는 전체 기존 Item ID·수량 상한·기존 슬롯 품목/개수를 작성한다. 두 단계는 원본 아이템·Owner·재고·예약·배정·행동 번호를 수정하지 않는다. StateApply는 계획 안의 실물만 현재 원본으로 재검사해 실제 인계·정산한다.

- 이번 틱 새로 입고한 공급품, 새로 수집한 적재품, 출고로 생긴 새 보관 여유는 다음 틱부터 사용할 수 있다.
- 같은 틱 수집과 그 뒤 공급 신호를 미리 제출하면 공급은 초기 NextAction 불일치로 거절한다. 수집 완료 후 다음 틱에 새 공급 신호를 제출한다.
- 여러 요청의 기존 실물·공간 경합은 접수 순서로 실제 성공분만 반영한다. 슬롯 예산은 현재 조건과 함께 검사하고 실패나 0개 계획이 공간을 미리 선점하지 않는다. 공급원 재고·영속 공간 예약은 추가하지 않는다.
- 이전 재고·공간이 0인 유효 신호는 수량 상한 0과 Unavailable로 처리해 실제 0개 정산을 수행한다. 초기 자격이 잘못되거나 계획이 준비되지 않은 Rejected와 구분한다.

이번 범위에는 건설 공급·회수와 드론 작업 관리 시스템을 포함한다. 공급·회수 작업 생성, 작업 선택과 배정, 현장 공급량 예약, 자재 인계, 취소·완공에 따른 종료와 재배정, 최소 드론 데이터 및 작업 요청·결과 규격을 구현 대상으로 삼는다.

실제 드론의 생성·이동·경로 탐색·배터리·충전·행동 실행은 후속 범위다. 이번 관리 시스템은 등록된 수행자의 상태와 도달 가능성·실제 이동거리, 도착·행동 완료 정보를 입력받는 연결 규격을 제공한다. 실제 수행자가 없거나 필요한 정보가 없으면 작업을 대기하며, 자동으로 이동·수집·공급이 성공한 것으로 처리하지 않는다.

공급원 재고와 보관 공간은 예약하지 않는다. 드론은 도착한 뒤 현재 재고 또는 빈 용량을 확인한다. 현장 공급량만 예약하여 여러 드론의 중복 배정을 막는다. 드론 예약을 이유로 기존 벨트 입출고 판단·예약·FIFO 규칙을 변경하지 않는다. 실제 드론 수집·보관으로 변한 재고와 공간은 이후 일반 물류가 현재 상태로 읽는다.

## 기존 기능의 변경 방향

- 기존 공사 자재 운송 기능은 전부 제거 대상으로 두고 새 규칙으로 다시 설계한다. 기존 운송 기록·등록·수령·예약·결과 처리 구조를 유지해야 한다는 전제는 두지 않는다.
- `ConstructionSite.Progress`와 생성자 인자·초기화·테스트 참조를 제거했다. 자재 도착 비율의 계산·표시 기획도 제거한다. 변경의 실행 근거는 [Progress 제거 검증](../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/ConstructionProgressRemoval-Verification.md)에 기록한다.
- `CancelConstructionRequest`는 Command의 ConstructionCancelCommandSystem이 처리하며, 기존 실물 반환과 현장/요청 삭제는 EndCommand에서 확정한다. 실행 근거는 [취소 Command 이동 검증](../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/ConstructionCancelCommand-Verification.md)을 따른다.
- DemolishBuildingRequest 이름 변경 제안은 철회하여 기존 이름을 유지하고 CancelConstructionRequest와 분리한다. Command 승인 대상에 PendingBuildingDemolition 상태를 기록하고 모든 철거 요청을 EndCommand에 삭제한다. 이후 건물의 입고·생산·출고·벨트 이동/진입·분배·합류 운송을 앞단에서 중단한다. 실제 반환/환급/철거는 StateApply/EndStateApply에 유지한다. 실행 근거는 [철거 예정 상태·동작 중단 검증](../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/BuildingDemolitionStop-Verification.md)을 따른다.

## 현장 생성과 점유

- 배치가 승인되면 즉시 건설 현장을 생성한다.
- 현장은 건물 완공에 필요한 품목과 수량을 관리한다.
- 현장 생성 시부터 건물 크기만큼 공간을 점유한다.
- 현장이 생성되면 드론 관리의 Decision이 필요한 모든 품목의 공급 작업 생성 의도를 만들고, Execution이 생성 명령을 기록하여 EndStateApply에 공개한다. 배치 Command가 직접 작업을 만들지 않는다.
- 현장 점유 범위 아래 월드 아이템이 있으면 자동으로 회수 작업을 생성하고 완공을 막는다.
- 현장 생성 이후 새로 들어온 월드 아이템에도 회수 작업 생성과 완공 차단을 적용한다.

## 자재의 표현과 보관

- 공급 작업의 수량은 한 번에 수집·운반·공급할 아이템 개수를 뜻한다. 수량 단위의 작업과 실물 엔티티 유지는 함께 적용한다.
- 드론의 수집·운반·공급 사이에도 기존 아이템 엔티티를 유지한다.
- 현장과 드론의 내부 자재는 `StoredItemElement`를 사용한다. 이는 실물 아이템 엔티티를 참조하는 현재 버퍼 구조와 양립한다. 드론의 최대 적재량은 수행자별 필드로 복제하지 않고 World singleton `DroneCapacityState.CarryingCapacity`로 관리한다.
- 드론은 한 번에 한 품목만 적재할 수 있다.
- 같은 품목은 모든 드론에 공통인 최대 적재량까지 적재할 수 있다. `StoredItemElement` 자체가 한 품목 제한과 드론 한도를 강제하지 않으므로 신규 배정에 공통 한도를 적용하고 실제 수집은 그 배정 수량을 상한으로 검사한다. 이미 배정한 작업에 연구로 늘어난 한도를 소급 적용하지 않는다.
- 연구로 공통 최대 적재량이 증가하면 새로 배정하는 작업부터 적용한다. 기존 `AssignedQuantity`, 개별 현장 예약과 실물 적재량을 변경된 한도에 맞춰 자동 증량하지 않는다. 공개 전 후보는 현재 공통 한도로 다시 검사한다.
- 현장은 받은 실물을 보관한다. 완공 처리 전까지 자재를 소비하지 않으며, 완공할 때 소비한다.
- 현장 취소 또는 드론의 월드 방출은 운반·보관 중인 기존 실물을 월드 아이템으로 전환하는 동작이다.

현재 데이터 구조 참고: [StoredItemElement](../../Assets/Scripts/Components/Storage/StorageComponents.cs), [ItemOwnership](../../Assets/Scripts/Components/Items/ItemComponents.cs). 일반 Ownership 요청 적용 이후 [같은 Lifecycle의 Actions](../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs)가 행동·이전 계획을 재검사하고 [같은 Ownership의 공통 API](../../Assets/Scripts/Systems/5_StateApply/ItemOwnershipApplySystem.cs)로 실물을 반영한다. API가 버퍼·Owner·위치·벨트·렌더를 함께 쓰며 새 Transfer 요청은 만들지 않는다. Lifecycle은 성공 개수로 배정·수량/결과를 정산한다. 활성 Destroy 실물은 옮기지 않는다.

현재 인계 계획은 [DroneItemTransferDecisions](../../Assets/Scripts/Components/DroneLogistics/DroneItemTransferDecisions.cs)에 보관한다. 실물·재고 원본을 대신하는 데이터가 아니며 계획에 포함된 기존 ID와 이전 슬롯 예산 안에서만 최종 반영하고 EndStateApply에 제거한다.

실물 반영과 행동 정산은 기존 소유자로 통합했다. 일반 Ownership Job → 같은 DroneLifecycle의 Actions → Construction 완공 순서이며, 드론 인계는 공통 ItemOwnership API로 실제 버퍼·Owner·위치·벨트·렌더를 반영하고 새 Transfer 요청을 만들지 않는다. 접수 순서·이전 틱 입력·실제 성공량 정산과 외부 결과 소비 계약은 유지한다. 현재 근거는 [소유자 통합 검증](../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/DroneTransferOwnerConsolidation-Verification.md), 이전 검증은 당시 구현 기록으로 구분한다.

## 공급원과 보관 장소

- 건설 자재를 가져올 수 있는 건물은 `Storage`, `MainFacility`, `DroneStation`이다.
- 초과 자재와 회수품을 맡길 수 있는 건물도 위 세 종류다.
- 드론은 공급원에 도착하여 아이템 수집 작업을 수행한다.
- 수집 후 현장으로 이동하고, 현장에서 자재 공급 작업을 수행한다. 이 공급 작업으로 현장이 자재를 받은 것으로 인정한다.

정상 흐름은 다음과 같다.

```text
현장 생성 및 전체 필요 품목의 공급 작업 생성
  → 수행 가능한 작업 선택
  → 현장 대상과 공급원 선택
  → 수집하러 출발할 때 현장 공급량 예약
  → 공급원 도착 및 현재 재고 확인
  → 가능한 실물 수집 및 부족분의 현장 예약 해제
  → 현장 이동 및 공급 작업
  → 필요한 실물을 현장에 보관하고 공급 예약 정산
  → 전체 자재 충족 및 바닥 정리 확인
  → 즉시 완공 및 보관 자재 소비
```

위 흐름은 게임 동작의 순서다. 실제 ECS 시스템 그룹과 Job 순서를 새로 확정한 표는 아니다.

## 작업 생성과 배정 우선순위

- 현장 생성 시 모든 필요 품목의 공급 작업을 생성한다. 한 품목의 공급이 끝나야 다음 품목의 작업을 생성하는 순차 규칙은 없다.
- 공급 작업에서 여러 개별 드론에 하위 작업을 배정하고 병렬로 수행한다.
- 개별 드론의 하위 작업은 한 품목과 배정 수량을 운반하며, 드론의 적재 한도를 지킨다.
- 현재 수행 가능한 공급 후보의 선두는 **현장 PlacementStamp → 작업 생성 순서**, 회수 후보의 선두는 **작업 생성 순서**로 고른다. 공급·회수의 두 선두 작업끼리는 생성 순서로 비교한다.
- 현재 수행할 수 없는 오래된 작업 때문에 다른 수행 가능한 작업을 막지 않는다.
- 품목 간 별도의 고정 우선순위는 요구하지 않는다. 동시에 생성된 작업의 동률 처리와 구체적인 하위 작업 데이터는 [드론 컴포넌트 계약](../CodeMemory/Components/DroneLogistics.md)을 따른다.

작업 생성 순서와 현장 대상의 `PlacementStamp`는 서로 다른 선택 기준이다. 2단계 구현 중 늦게 들어온 배치 요청의 Stamp가 더 이를 수 있는 경우를 확인했고, 사용자가 위의 두 선두 비교를 선택했다. 예를 들어 공급 A가 생성 1/Stamp 20, 회수가 생성 2, 공급 B가 생성 3/Stamp 10이면 공급 선두는 B이고, 회수가 B보다 먼저 선택된다. 전체 작업을 생성 순서 하나로 정렬하는 방식으로 설명하지 않는다.

## 대상 선택과 실제 이동거리

| 선택 상황 | 확정된 기준 |
| --- | --- |
| 최초 공급 현장 선택 | `PlacementStamp`가 앞선 현장 우선 |
| 선택한 현장에 공급할 공급원 선택 | `드론 → 공급원 → 현장`의 전체 실제 이동거리 비교 |
| 초과량 또는 현장 취소·완공 후 다른 현장으로 재배정 | 드론이 이동 가능한 범위에서 실제 이동거리가 가까운 필요지 우선 |
| 주변 보관 장소 탐색 | 드론이 이동 가능한 범위에서 실제 이동거리가 가까운 보관 장소 탐색 |
| 거리가 같은 대상 건물이 여러 개 | `PlacementStamp`가 앞선 대상 우선 |

- 실제 이동거리는 필요한 배터리 충전을 위한 스테이션 경유도 포함한다.
- 이동 가능 범위는 지금 남은 배터리만으로 직접 도달하는 범위에 한정하지 않는다. 스테이션에서 충전하고 다시 이동하여 도달할 수 있는 대상도 포함한다.
- 대화에서 말한 현장의 생성 순서는 `PlacementStamp`를 뜻한다. 현재 타입은 Tick, 이어 Order를 비교한다. [정의](../../Assets/Scripts/Components/Buildings/PlacementStamp.cs)
- 관리 시스템은 후속 드론 수행부가 제공하는 도달 가능성 및 충전 경유를 포함한 실제 이동거리를 사용한다. 실제 경로 탐색·배터리·충전 계산은 이번 구현 범위에 포함하지 않는다. 해당 입력이 없으면 작업 가능 여부와 거리를 임의로 만들어 배정하지 않는다.

## 현장 예약과 도착 시 재고 확인

- 현장의 신규 공급 가능 수량은 `필요량 − 도착량 − 다른 드론의 예약량`으로 계산한다.
- 드론이 **수집하러 출발할 때** 현장의 자재 공급량만 예약한다. 수집을 마친 뒤 현장으로 출발하는 시점까지 현장 예약을 미루지 않는다.
- 공급원 선택 시 현재 재고를 참고하되 재고를 예약하거나 실물을 선점하지 않는다. 도착 시 벨트 입출고 등으로 재고가 달라질 수 있으므로 실제 수집 시점에 다시 확인한다.
- 운송 시작 시 특정 현장 몫으로 배정한다. 취소·완공 또는 초과량에 따른 변경은 재배정 규칙을 따른다.
- 여러 드론의 동시 하위 작업은 현장에 남은 수량과 드론의 적재 한도를 지킨다. 같은 공급원을 선택한 다른 드론의 예정 수집량을 공급원 재고 예약으로 차감하지 않는다.

| 수집 결과 | 현장 공급 예약과 다음 동작 |
| --- | --- |
| 배정한 수량 전부 수집 | 실제 운반량의 예약을 유지하며 현장으로 이동 |
| 배정량보다 적게 수집 | 부족분 예약을 해제하고 수집한 양만 공급 |
| 하나도 수집하지 못함 | 현장 예약을 해제하고 다시 탐색 |

현장 공급 예약은 해당 현장에 배정한 수량이며, 수집 후에는 실제 운반하는 수량으로 줄인다. 수령으로 처리하거나 대상 변경에 맞춰 정산한다. 예약량은 현장이 이미 받은 실물 수량과 구분한다. 공급원 재고 예약 및 그 해제·정산은 구현하지 않는다.

## 현장 수령과 초과 자재

- 공급 작업을 수행할 때 현장에 필요한 양만 전달한다.
- 초과량은 해당 현장에 넣지 않고 드론이 보유한다.
- 드론은 초과량을 필요로 하는 주변 다른 현장을 먼저 찾고 공급 예약을 한 뒤 이동·공급한다. 공급 가능한 다른 현장이 없으면 주변 보관 장소를 찾는다.
- 보관 공간은 예약하지 않는다. 보관 장소 도착 시 현재 빈 용량을 확인하여 가능한 만큼 보관하고, 남은 자재는 재탐색한다.
- 재배정 대상과 보관 장소는 실제 이동거리 및 거리 동률 시 PlacementStamp 규칙을 따른다.
- 다른 필요지와 보관 공간이 모두 없으면 드론은 적재 실물을 월드에 내려놓고 회수 작업을 생성한다. 보관 장소가 생기면 회수 작업을 수행한다.
- 보관 장소에 일부 수량만 들어가는 경우에도 남은 수량에 같은 재탐색·월드 방출·회수 작업 규칙을 적용한다.

다른 필요지와 보관 장소가 동시에 가능하면 다른 현장 공급을 우선한다. 현장 취소·완공 후 재배정에도 같은 목적지 종류 우선순위를 적용한다.

## 완공과 현장 취소

- 모든 필요 자재가 충족되고 바닥 월드 아이템에 의한 차단이 해소되면 즉시 완공한다.
- 별도의 건설 작업 시간을 요구하지 않는다. 자재 부족 시 현장을 유지하며 공급을 기다린다.
- 완공 시 현장에 보관한 건설 자재를 소비한다. 자재 도착 비율이나 시간 진행도로 완공을 판정하지 않는다.
- 현장 취소는 Command에서 처리한다.
- 같은 시뮬레이션 틱에 철거/현장 취소와 같은 위치의 재배치 명령이 생기면 건설 요청은 갱신 전 공간 인덱스의 점유를 기준으로 거부한다. Synchronization 이후 새 건설 요청부터 변경된 점유 상태를 검증하며, 거부된 요청을 자동 재시도하지 않는다.
- 취소 시 이미 받은 자재는 기존 실물 엔티티를 월드 아이템으로 전환하여 현장 위치에 남긴다. 완공 전 소비가 없으므로 도착 자재를 이미 소모한 것으로 처리하지 않는다.
- 취소 또는 먼저 완공된 현장으로 운송 중인 자재는 다른 공급 필요지를 탐색하고, 필요지가 없으면 보관 장소를 탐색한다.
- 이 재탐색에도 도달 가능성·실제 이동거리·동률 PlacementStamp 기준을 적용한다. 모두 없으면 월드 방출과 회수 작업 생성 규칙을 따른다.
- 취소·완공 후 기존 현장에 공급을 계속하지 않는다. 예약과 하위 작업의 종료·이전은 [드론 컴포넌트 계약](../CodeMemory/Components/DroneLogistics.md)을 따른다.

## 월드 아이템 회수

- 회수 작업도 드론이 수행한다.
- 회수하러 출발할 때 목적 보관 장소를 탐색하되 빈 용량을 예약하지 않는다. 도착 시 현재 빈 용량을 다시 확인하여 가능한 만큼 보관한다.
- 회수한 실물은 현장에 바로 공급할 수 없다. 먼저 Storage/MainFacility/DroneStation 중 보관 장소에 넣는다.
- 보관 건물에 들어간 회수품은 이후 일반 건설 자재로 다시 공급할 수 있다.
- 보관 시 목적지가 없어졌거나 공간이 부족하면 다시 보관 장소를 탐색한다. 회수품은 보관되기 전까지 현장에 직접 공급하지 않으며, 보관하지 못한 수량은 보관 장소 재탐색 후 모두 없으면 월드 방출·회수 작업 생성 규칙을 따른다.
- 현장 아래 월드 아이템은 현장 생성 때뿐 아니라 현장이 존재하는 동안 새로 유입된 경우에도 회수 대상이며 완공을 막는다.
- 작업 간 우선순위는 공급과 같은 기준인 수행 가능한 작업의 생성 순서다.

## 구현 상태와 후속 수행부

현재 소유자·데이터·처리 순서는 [드론 컴포넌트 계약](../CodeMemory/Components/DroneLogistics.md)을 따른다. 현재 1~4단계는 작업/인계/예약 정산과 적재품 재배정·완공 차단을 구현했다. Supply는 현장→보관처, Recovery는 보관처만 Direct 경로로 찾고 None은 대기하며 모두 도달 불가이면 방출을 배정한다. 적재 그룹은 최초 작업 순서로 신규보다 우선하며 같은 배정의 revision을 증가시켜 실물·출처·결과 참조를 보존한다. 실물 행동과 이동은 외부 수행부의 신호를 기다린다.

후속 실제 드론 수행부는 위치·작업 가능 상태, 도달 가능성·충전 경유를 포함한 실제 거리, 도착·행동 완료 정보를 제공해야 한다. 실제 드론의 생성과 실행, 이동·배터리·충전 수치 및 실제 경로 계산은 이번 범위에서 임의로 정하지 않는다.

공급원 재고 예약과 보관 공간 예약을 삭제한 최신 합의가 이전 예약 합의를 대체한다. 기존 벨트와 신규 드론 예약 사이의 우선순위를 정하기 위해 벨트 입출고를 변경하는 안도 이번 계획에서 사용하지 않는다.

최초 문서화 이후 기존 공사 운송·Progress 제거, 현장 취소의 Command 이동, 철거 승인 상태의 동작 중단을 구현했다. [1단계](../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/DroneContractsStep1-Verification.md)의 데이터 정의와 [2단계](../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/DroneSchedulingStep2-Verification.md)의 작업 생성·배정 검증을 구분한다. 2단계 테스트의 수행자·경로 결과는 명시적인 테스트 입력이며 실제 비행·자재 인계의 실행 증거가 아니다.

[3단계 검증](../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/DroneItemTransferStep3-Verification.md)은 명시적으로 준비한 수행자·배정·행동 신호에 따른 내부 실물 인계 범위를 기록한다. 실제 비행·도착·충전과 4단계 완공 차단·자동 재배정의 실행 증거로 확대하지 않는다.

[현재 이전 틱 인계 검증](../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/DronePreviousTickTransfer-Verification.md)은 초기 자격·물류 쓰기 전 계획·최종 원본 재검사의 책임과 같은 틱 신규 입력을 배제하는 정책을 기록한다. 앞선 검증의 성공 범위를 현재 정책으로 소급 해석하지 않는다.

Construction은 DroneLifecycle 이후 현재 월드 Owner/GridPosition과 활성 Destroy만 조회하며 Item/Building 선행 속성과 예정 위치 기록은 제거했다. 공통 helper가 활성·비취소 현장의 회전 footprint 내부 World Spawn을 Decision/최종 Apply에서 거부한다. Drop 최종 신호는 배정 목표==요청 WorldPosition==관측 격자 셀과 현장 외부 조건을 충족해야 한다. 선택 후 새 현장이 목표를 막으면 실물을 유지하고 Retargeting으로 재탐색한다. 기존 공간 인덱스·이전 틱 인계·단일 시스템 파일은 유지한다.
