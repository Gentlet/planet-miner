# 드론 작업 관리

[전체 색인](README.md) · [배치와 공사](Construction.md) · [아이템과 저장](ItemsAndStorage.md)

드론 작업/예약/인계/재배정과 정거장 연결/보관/Identity/요청/계획의 데이터 계약을 설명한다. 일반 운반 계획은 별도 버퍼로 구분하며 배정 공개는 기존 Publish에 연결했고 실제 경로/이동/관측/도착은 [이동 명세](../../Specifications/DroneMovement.md)를 따르며 자동 픽업은 [픽업 명세](../../Specifications/DronePickup.md)에 연결했고 자동 납품은 [납품 명세](../../Specifications/DroneDelivery.md)에 연결했다. 정상 귀환/정거장 전환과 수요 기반 출고는 [귀환/출고 명세](../../Specifications/DroneReturnAndDispatch.md)를 따르며 실제 충전은 [충전 계약](../../Specifications/DroneCharging.md)을 따른다.

2026-10-15 Task 9.1은 [상태 전이·Lifecycle Owner 계약](../../Specifications/DroneLifecycle.md)을 확정했다. 1A~6A의 수납·완충 전/연속 배차·귀환·비상 이동·중단 정산은 후속 수행부가 따라야 할 정책이다. Task 9.1 당시 23개 타입과 관리 계층을 유지했으며 실제 정거장·이동·배터리·충전·수납 Writer나 새 상태 enum을 추가하지 않았다. 이후 Task 9.2의 정거장 기반은 아래 별도 타입/명세로 구분한다. 작업/배정의 기존 상태와 수행자 자체의 후속 개념 상태를 구분한다.

기존 142사례와 기존 통합 4사례는 이전 구조의 별도 실행이며 합산하지 않는다. Task 9.3.1은 생성 초기화, 9.3은 [명시적 출고/정박/수납과 용량](../../Specifications/DroneIdentityAndStationStorage.md), [공통 설정 초기화](../../Specifications/DroneCapacityInitializationPlan.md)를 연결한다. 경로/이동/관측/도착과 최소 비상 귀환은 구현되어 있다. 자동 픽업은 공개 도착 다음 틱 기존 인계 경로를 사용한다. 자동 납품/완료량은 [납품 명세](../../Specifications/DroneDelivery.md)를 따른다. 정상 귀환/정거장 전환과 수요 기반 출고는 [귀환/출고 명세](../../Specifications/DroneReturnAndDispatch.md)를 따른다. 실제 충전은 [충전 계약](../../Specifications/DroneCharging.md)을 따르며 연구 Writer는 후속이다. DropPosition은 실제 위치 원본이 아니다.

```text
Command / EndCommand: 현장·자재 요구와 취소 반환·삭제 공개
BuildingSimulation: 건물 입출고·생산·수명주기 → 마지막 완공 판정
EndBuilding: 건물·아이템 생성/삭제·반환/환급 확정
DroneDecision: 작업 생성·종료·경로 의도 / 순수 배정 후보 / 행동 자격
DroneReservation: 이전 공개 대기·무효 예약 정산 → 후보 선택 → 현장 공급량 예약
DroneExecution OrderFirst: 건물 처리 후 실물 ID·수량·슬롯 계획 확정
DroneExecution: 작업 순번 발급 / 생성·경로 명령을 최종 ECB에 기록
DroneStateApply: 계획 현재 원본 재검사 → 공통 Ownership API로 실물 인계·성공분 정산
                무효화 상태/연결/삭제 보류 → Publish 최종 검사·롤백·공개 기록
SimulationCommit / EndSimulation: 작업·경로·배정·개별 예약·결과 공개와 종료 삭제
Synchronization: 원본 ECS로 공간 인덱스 갱신
다음 틱: 새 작업·배정·경로 이용 / 지난 틱 드론 납품·회수로 건물 완공 판정
```

BuildingSimulationGroup과 DroneSimulationGroup은 각각 한 번 실행한다. 드론은 같은 틱 건물 종료의 새 입고품·생산품·철거 반환/환급과 새 보관 공간을 읽는다. 기존 공급원 종류와 품목 제한은 유지한다. 드론 실행 전 모든 계획을 준비하며 드론 적용 중 새 수집품이나 비워진 공간을 기존 계획에 추가하지 않는다. 건물도 이번 틱 입고 재료로 다시 생산 판단하지 않는다.

외부 입력은 시뮬레이션 전 DroneActionRequestUtility.Submit으로 게시한다. 결과는 EndSimulation 이후 외부 또는 다음 틱 외부 경계에서 TryConsumeResult로 읽고 삭제한다. GameSimulationGroup은 틱 시작에 Ready/Fatal을 검사한다. EndBuilding의 NextTick Fatal은 현재 틱을 마치고 다음 틱부터 차단한다. Power의 RemainingDomains Fatal은 EndPower에서 공개하여 같은 틱 Building/Drone을 건너뛴다.

Placement는 현장·자재 요구만 생성한다. Decision은 생성·무효화·경로 의도와 후보만 쓰며 원본 예약·배정·연결을 바꾸지 않는다. ProjectedRemaining은 Reservation이 해제할 미공개·무효 예약을 읽기 계산한다. Execution은 대상 생존·타입·중복·경로 스냅샷만 적용 검사하며 필요 작업을 다시 탐색하지 않는다. 이후 드론 Apply로 낡은 작업·경로는 다음 Decision이 후보에서 제외하고 정리 의도를 작성한다.

순수 후보 DroneTaskCandidateDecisionElement는 매 Decision에서 Clear한다. Reservation 선택·수량·미공개 예약·PublicationQueued는 DroneTaskPendingPublicationElement에 보관한다. Publish는 최신 상태를 재검사하고 무효/축소 공급량을 롤백하며 배정과 개별 예약·수행자 연결을 최종 ECB에 함께 기록한다. PublicationQueued는 기록 완료이며 실제 공개는 EndSimulation이다. 미공개 예약은 다음 Reservation이 해제할 때까지 남고 후보 Clear와 수명이 다르다.

일반 Storage/Routing·Ownership은 건물 그룹에서 끝난다. DroneLifecycle은 접수 순서로 준비된 실물·슬롯을 재검사해 ItemOwnershipApplySystem.TryTransferItem의 실제 성공분만 정산한다. API는 실물의 출발/도착 버퍼·Owner·격자/시각 위치·벨트 정지·렌더 ECB를 함께 반영한다. 새 TransferOwnershipRequest를 발행하지 않는다. 공급원 재고·보관 공간은 영속 예약하지 않으며 목적지 공유 슬롯 예산은 성공분만 소비한다.

Construction은 BuildingStateApply 마지막에서 현재 Owner=Null/GridPosition·활성 Destroy와 기존 도착량으로 완공을 판단한다. 이번 틱 드론 납품·마지막 방해물 회수는 다음 틱에 반영된다. 예정 월드 위치 기록은 없고 활성·비취소 현장 내부 World Spawn/드론 방출은 공통 helper로 금지한다.

최초 배정은 수행자별 공급 대표 PlacementStamp→CreationSequence, 회수 대표 CreationSequence를 선택한 뒤 두 대표 CreationSequence를 비교한다. 적재품 재배정은 신규보다 먼저 최초 OriginalTaskCreationSequence 순서다. Supply는 필요 현장→보관처, Recovery는 보관처만 Direct 실제 거리/동률 PlacementStamp로 고른다. 관련 결과 None은 대기하며 목적지가 없으면 안전 방출 위치를 평가한다. 현재 셀이 현장 내부이면 Flight Decision의 가장 가까운 실제 도달 가능한 외부 셀 결과를 기다리고 None/Unreachable은 적재 대기다.

재배정은 같은 배정 revision을 증가시키고 행동 번호를 0으로 초기화하며 실물·출처·최초 순서·기존 결과 참조를 보존한다. 기존 적재량에 공통 용량을 소급 적용하지 않는다. 최종 Drop은 배정 목표==요청 위치==관측 셀과 현장 외부를 검사하고 새 현장이 막으면 Retargeting으로 돌려 실물을 보존한다. 이동 Execution이 위치 원본을 쓰고 다음 Observation Preparation이 관측을 갱신한다.

생성 판단·배정/행동 정산·공통 실물 API는 각 시스템 본체 한 파일에서 유지하며 Unity Entities 소스 생성용 partial 선언을 파일 분리 근거로 사용하지 않는다.
2026-10-22 Task 9.2의 Unity 컴파일 오류 없음과 관련 선택 EditMode 99/99(신규 정거장 14/14 포함)를 확인했다. 기존 건설 통합 fixture의 그룹/준비·격리 공급 누락 보완과 검수 결과는 [정거장 명세](../../Specifications/DroneStationNetwork.md#검증)에 기록했다. 실제 드론 수행자·수용량·귀환·충전과 작업 후보의 망 필터는 후속이다.

## DroneBatteryState

가용 배터리 CurrentCharge(float)와 이미 충전됐지만 표현 단위 미만인 FractionalCharge(double)를 같은 배터리 원본으로 소유한다. 충전 정산이 잔량을 누적/게시하고 이동과 역할 전환은 잔량을 보존한다. 최대 배터리와 전력 예산은 복제하지 않는다. DroneSpawnUtility가 호출자의 명시적 최대치로 새 개체를 초기화하고 기존 값은 역할 초기화로 충전하지 않는다. 최대치 설정과 Identity 전환은 Task 9.3에서 연결했고 이동 소비는 9.10에 연결했고 정박 증가량은 DroneChargingExecutionSystem이 반영한다. 생성 초기 구성과 EndSimulation 공개·Ready 전제는 [드론 생성 명세](../../Specifications/DroneCreation.md)를 따른다.

## DroneStationStorage

공통 건물 생성이 Config의 전용 수를 부착한다. Storage.SlotCount의 뒤쪽 전용 구간을 뜻하며 이후 불변이다. StationStorageUtility, 벨트 슬롯 Reservation과 드론 보관 계획/인계가 읽는다. 일반 구간은 총 슬롯에서 전용 수를 뺀 앞 구간이다. [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneStationStorageComponents.cs).

## DockedDroneElement

정거장의 활성 정박 드론/슬롯 역참조 버퍼다. 공통 건물 생성은 빈 버퍼, DroneStationLifecycleApplySystem은 Docked 관계와 함께 EndSimulation에 양쪽을 기록한다. 활성 드론 하나가 슬롯 전체를 점유하며 아이템 스택과 혼합하지 않는다. 보관 계획/철거 Command/최종 불변식 검사가 읽고 출고/수납 때 제거한다.

## DroneDocked

활성 수행자의 Station/SlotIndex 관계 원본이다. 수납 아이템의 ItemOwnership과 상호 배타적이다. Lifecycle만 EndSimulation에 양쪽 관계를 기록한다. 이동 도착을 읽은 자동 Producer가 기존 Dock/Activate 요청을 제출한다. IsIdleWorker는 정박 중 새 후보를 만들지 않는다. 목적지 무효화 뒤 화물과 연결이 Retargeting으로 보존된 경우에도 자동 재개 판단은 실행 가능한 대체 경로를 확인하고 Activate로 정박을 해제한다. 반복 무효화 의도만으로 이 재개 판단을 차단하지 않는다.

## DroneActivityState

수행자 자체의 Idle/Moving/Acting/Charging/WaitingForCharge 활동이다. 생성이 Idle을 기록하고 Store/Dock 자격 검사가 읽는다. 작업/배정 상태를 복제하지 않는다. 비정박 이동/대기는 Movement Execution, 정박 충전/대기는 Charging Execution이 작성한다. 자동 정박 Activate는 이동 상태 초기값/Idle을 기존 정박 관계 해제와 같은 EndSimulation에 기록하며 기존 배터리/배정/화물을 보존한다. 이번 틱 충전량은 다음 틱 판단이 읽으며 수납 전환에서 활동을 제거한다.

## DroneBatteryConfig

World의 읽기 전용 최대 배터리 singleton이다. DroneConfigInitializationSystem이 maximumBattery를 검증/게시하며 기존 유효 singleton을 보존한다. 개체 현재량과 적재량을 복제하지 않는다. Station Lifecycle이 최초 생성 최대치와 완충 수납 자격에 사용한다.

## DroneStationRequest

외부 입력 또는 자동 Station Decision의 메인 스레드 Submit이 Drone/Station/Operation/양수 Sequence를 접수하고 Decision과 함께 생성한다. Activate/Dock/Store 요청이며 단순 입고가 자동 생성하지 않는다. 처리 뒤 Request/Decision 제거와 결과 추가는 같은 EndSimulation이다. [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneStationRequests.cs).

## DroneStationDecision

병렬 Burst Station Lifecycle Decision Job이 요청별 자격/거부 상태를 초기화한다. 자동 수요 기반 출고는 Task Reservation 뒤 Station Activation Reservation이 잔여 수요 상한을 중재하며, 그 뒤 기존 단일 Burst Station Lifecycle Reservation Job이 Sequence 순으로 승인/SlotIndex/경합 거부 Status를 작성한다. 단일 Burst Apply 데이터 Job은 승인 슬롯과 최신 원본을 검사하며 다른 슬롯으로 확장하지 않는다. 실물/영속 예약/공개 결과가 아니며 EndSimulation에 소비한다.

입력 Entity 목록은 비동기 수집의 deferred array로 전달하고 state.Dependency를 연결한다. 요청 정렬/Busy 수행자 집합/슬롯 예산은 각 Job 내부 Native 임시 자료다. 공통 자격 규칙은 QueryMask와 값별 RO Lookup을 읽으며 ECS 원본을 별도 캐시하지 않는다. Apply만 Actor/Prefab 표시 목록을 TempJob으로 전달한다. 데이터 ECB Producer를 등록하고 목록 완료를 기다린 뒤 관리형 RenderMeshArray를 메인 스레드에서 별도 EndSimulation ECB에 기록한다. 두 ECB는 순서대로 같은 경계에서 공개하며 임시 자료는 마지막 접근 뒤 해제한다. 상세 계약과 현재 검증은 [Identity/보관 명세](../../Specifications/DroneIdentityAndStationStorage.md)를 따른다.

## DroneStationResult

Station Lifecycle Apply가 전환 결과를 기존 요청 Entity에 EndSimulation으로 게시한다. 외부 요청의 결과는 외부 호출자가 TryConsumeResult로 한 번 소비한다. 자동 요청은 DroneAutomaticStationRequest 표식으로 구분하고 다음 DroneStationAutomationDecisionSystem이 자기 결과만 소비한다. 결과 소비는 역할/소유권 전환을 반복하지 않는다.

## DroneStationRequestSequence

Submit이 단독 생성/증가하는 World 접수 순번이다. 0/순환을 허용하지 않는다. Reservation/Apply는 이 순서로 요청을 처리한다. 작업/행동 순번과 별개다.

## DroneStation

주 시설·완공 정거장의 역할 마커다. 공통 건물 생성이 EndBuilding에 부착하고 건물 삭제와 함께 제거한다. 전력 공급과 독립적으로 참가하며 위치·방향·footprint·설정은 기존 원본을 사용한다. [정거장 명세](../../Specifications/DroneStationNetwork.md)를 따른다.

## DroneStationNetworkDecision

DroneStationNetworkDecisionSystem이 DroneDecision 앞단에서 매 틱 전체 결과를 초기화하고 활성 정거장에 대표 Entity/ActivityBounds와 enable을 작성한다. enable은 이번 틱 참가/연결 판단 완료다. 비활성·철거 승인 대상은 미준비로 남는다. 같은 틱 소비자는 이 값과 건물 원본을 읽으며 이전 공개 인덱스로 대체하지 않는다.

## DroneStationNetwork

DroneStationNetworkApplySystem이 Decision의 대표 Entity를 EndSimulation에 공개한다. 미참가 정거장은 Null이며 삭제될 때 함께 사라진다. 대표 변경은 정상 망 재구성으로 영속 식별을 보장하지 않는다. 작업·드론 배정/예약 상태를 소유하지 않는다.

## DroneStationSpatialIndex

DroneStationSpatialSyncSystem이 같은 singleton의 Fence와 함께 생성한다. Synchronization에서 공개 연결과 원본 건물로 정거장→대표/정사각형 영역을 Clear/Populate하며 0개일 때도 비운다. Contains는 영역의 합집합만 포함한다. 셀별 점유·망 외곽 사각형·위치의 별도 원본은 없다. 같은 틱 작업 필터는 이 이전 공개 맵 대신 준비된 NetworkDecision 대표/범위를 사용한다.

## DroneStationSpatialIndexFence

소유자는 위 Sync 시스템이다. Reader/Writer Job은 GetReaderDependency/AddReader와 GetWriterDependency/SetWriter를 사용한다. 직접 재할당·해제는 Complete 뒤 수행하며 ECS 의존성과 ECB Producer를 대신하지 않는다. World 종료에 최종 접근 완료 후 소유자가 맵을 Dispose한다. 공통 Fence 계약은 루트 AGENTS.md를 따른다.

## DroneLogisticsTask

- **종류·부착 대상·목적:** 일반 IComponentData. 독립 상위 작업 엔티티의 헤더다. 공급은 (현장, 품목), 회수는 월드 실물별 작업이다. 여러 하위 배정이 같은 공급 작업을 참조할 수 있다.
- **필드:** Kind는 ConstructionSupply/WorldItemRecovery/StorageImport/StorageExport/BuildingDemolition, State는 Open/Closed/Cancelled/ExecutionConfirmed다. 철거의 ItemType은 None이며 확인 결과 대기 상태에서는 취소/새 배차를 하지 않는다. Target은 현장/회수 실물/일반 운반의 고정 출발 또는 도착 건물, ItemType은 품목이다. 양수 CreationSequence는 작업 생성 순서이며 현장의 PlacementStamp와 별개다. RecoveryReason은 SiteClearance/DroneDrop을 구분한다.
- **공급 판단·Producer:** DroneTaskDecisionSystem의 생성 판단 메서드가 유효 현장을 PlacementStamp 순서로 순회하여 RequiredQuantity > DeliveredQuantity인 품목의 생성 의도를 만든다. 같은 품목의 요구 행이 여러 개여도 의도는 하나다. 동일 현장·품목의 살아 있는 Open/Closed/Cancelled 작업이 있으면 자동 생성하지 않는다. 참조 정리 전 취소 root도 이 중복 억제에 참여한다. 유효한 명시 생성은 취소/요청 계약에 따라 새 root를 만들고 차단을 해제한다. DroneTaskExecutionSystem이 의도에 순번을 부여하고 작업 생성을 기록한다. 배치 경로와 무관하게 현재 현장 데이터를 기준으로 판단한다.
- **회수 판단·Producer:** 같은 Decision 시스템은 현재 ECS의 현장 footprint 아래 실물 또는 DroneRecoveryPending이 있는 실물의 생성 의도를 만든다. 현재 GridPosition·월드 소유권·회전 footprint를 사용하고 활성 Destroy를 제외한다. 일반 월드 실물 전체를 자동 회수하지 않는다. Open 회수 작업이 있는 실물에는 중복 생성하지 않는다. Execution이 Open 작업 생성을 기록하고 EndSimulation에 공개하여 다음 시뮬레이션의 Decision부터 후보로 조회한다.
- **Reader·Writer·종료:** DroneTaskDecisionSystem은 소실·취소 현장, 수납·소멸 실물, 현장 밖으로 나간 SiteClearance 대상 등 무효 작업을 읽기 검사로 제외하고 CloseTask 의도를 남긴다. 유효 작업은 후보 생성과 Reservation 우선순위 비교에 사용한다. DroneTaskLifecycleApplySystem이 종료 의도를 최종 재검사하여 Closed로 바꾸고 연결된 배정이 없으면 EndSimulation에 삭제한다. 명시 취소는 Cancelled로 구분한다. 이전 틱부터 Closed/Cancelled인 작업도 연결 배정과 미정산 확보량이 사라지면 삭제한다. 상위 작업 자체가 공급원 재고나 공간을 잠그지는 않는다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneTaskComponents.cs), [생성 판단](../../../Assets/Scripts/Systems/Drones/Decision/DroneTaskDecisionSystem.cs), [작업 생성 실행](../../../Assets/Scripts/Systems/Drones/Execution/DroneTaskExecutionSystem.cs), [수명주기 반영](../../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskLifecycleApplySystem.cs), [Decision](../../../Assets/Scripts/Systems/Drones/Decision/DroneTaskDecisionSystem.cs).

## DroneTransportRequirement / DroneTransportPlanDecisionElement

[작업 계획 명세](../../Specifications/DroneTaskPlanning.md)를 따른다. Requirement.Quantity는 일반 반입/반출의 불변 일회성 목표량으로 Execution이 root와 같은 EndSimulation에 최초 게시한다. PlanDecisionElement는 Decision이 후보 singleton에 매 틱 Clear/작성하는 계획 버퍼이며 기존 Candidate 형식을 재사용한다. 통합 DroneTaskReservationSystem이 기존 후보와 이 계획을 함께 읽어 수행자당 하나 선택한다. 선택된 일반 운반만 단일 Burst Job이 별도 목표량 확보로 중재한다. 배정 공개는 기존 Publish가 담당하고 실제 보관 성공량은 기존 Lifecycle이 해당 확보 Task에 자동 정산하며 계획으로 재고/공간/Owner를 변경하지 않는다.

## DroneTransportProgress / DroneTransportReservation / DroneTransportPendingReservationElement

[일반 운반 확보/정산 명세](../../Specifications/DroneTransportReservations.md)를 따른다. Progress는 root에 부착하며 DeliveredQuantity는 실제 보관 완료량, ReservedQuantity는 미공개/진행 중 확보량 합계다. 기존 Execution이 같은 EndSimulation에 0으로 초기화한다. DroneTaskReservationSystem은 메인 스레드에서 적재품 우선/공급 선두와 회수/반입/반출 선두 비교로 수행자당 하나 선택한다. 일반 운반은 로컬 반환 예정량/선택 예산으로 상한을 정하고 선택분만 단일 Burst Job에 넘긴다. Job은 무효/고아 인계분과 이전 미인계분을 반환한 뒤 목표 잔량 안에서 승인한다. 미선택 계획은 원본 확보량을 증가시키지 않으며 다른 드론의 같은 틱 선택을 막지 않는다. 별도 Pending 버퍼의 Candidate/Reservation은 반환 근거이며 Decision이 Clear하지 않는다. Publish는 Take 후 원본 기록을 비우고 PublicationQueued를 작성한다. 잔량이 0이어도 이 표시가 있으면 같은 EndSimulation 전 정거장 Busy를 유지한다. 다음 Reservation은 인계 기록을 재반환하지 않고 Clear한다.

Reservation은 Task/RemainingQuantity의 단일 소유 기록이다. Assignment Publish가 최종 자격을 검사한 뒤 Take로 인계받아 배정과 같은 EndSimulation에 부착하고, 기존 원본 기록은 0으로 만들어 이중 반환을 막는다. 현재 Reservation은 무효화 의도/고아 소유자의 인계분을 반환하고 0으로 정산한다. 기존 재배정 Publish는 0 기록을 새 배정과 같은 EndSimulation에 제거한다. 원래 Task/생성 순번 이력은 유지하되 정산된 기록의 유효성 검사를 새 보관/방출에 소급하지 않는다. Publish가 현재 재고/공간에 맞춰 감량/거부분을 즉시 반환하고 PublicationQueued를 기록한다. 소비된 승인 원본은 0이며 정거장 Busy는 이 기록 완료 표시도 읽는다. 실패 뒤 재선택은 다음 Decision이다. 실제 납품 Writer는 기존 Lifecycle이며 TryReduce/Release/TryRecordDelivery로 같은 기록을 정산한다. 실제 성공량은 해당 Task 하나에만 반영하며 재고/공간/Owner는 이 데이터가 소유하지 않는다. 후보/망 재검사와 행동 번호 중복 거부를 여기서 반복하지 않는다.

## DroneTaskSequence

- **종류·부착 대상·목적:** 일반 IComponentData. World의 단일 엔티티에서 공급·회수가 공유하는 작업 생성 순서의 원본이다. NextValue는 다음에 발급할 양수 번호다.
- **생성·Writer:** DroneTaskExecutionSystem만 OnCreate에서 DroneTaskCreationUtility.GetOrCreateSequence로 singleton을 확보한다. 새 상태는 1부터 시작한다. OnUpdate에서 값을 읽고 생성 의도를 처리하여 실제 생성 기록을 남긴 공급·회수 작업마다 증가시킨 뒤 저장한다. Decision과 배치 시스템은 이 데이터에 접근하지 않는다.
- **수명:** World 수명 동안 유지하고 종료된 작업의 번호를 재사용하지 않는다. 0은 발급 전에 1로 보정하며 ulong.MaxValue에서는 예외로 중단하여 순환 발급하지 않는다. 작업 엔티티 참조는 EndSimulation에 확정된다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneTaskSequence.cs), [공통 발급·생성](../../../Assets/Scripts/Common/DroneTaskCreationUtility.cs).

## DroneRecoveryPending

- **종류·부착 대상·목적:** 일반 IComponentData. 안전 방출/철거 반환과 환급 실물의 한정 회수 표시다. Reason은 DroneDrop/BuildingDemolition 출처이며 기존 None 입력은 DroneDrop으로 해석한다. 위치/품목/수량은 복제하지 않는다.
- **현재 Reader:** DroneTaskDecisionSystem의 생성 판단 메서드는 월드 실물에 이 표시가 있으면 현장 밖에서도 DroneDrop 회수 생성 의도를 만든다. Execution이 작업을 생성하면서 표시를 제거하지 않으며 Open 작업으로 중복 생성을 막는다.
- **Producer·제거:** DroneTaskLifecycleApplySystem은 DropCargo 성공분을 EndSimulation에, Building Lifecycle은 철거 반환/환급과 철거 벨트 정지 실물을 EndBuilding에 기록한다. RecoverWorldItem이 해당 실물을 실제로 수집하면 표시 제거를 같은 ECB에 기록한다. 방출 행동 요청은 DroneActionRequestDecisionSystem이 공개된 도착으로 접수하며 인계 시스템은 기존 요청/계획을 소비한다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneRecoveryPending.cs), [회수 판단](../../../Assets/Scripts/Systems/Drones/Decision/DroneTaskDecisionSystem.cs), [회수 생성 실행](../../../Assets/Scripts/Systems/Drones/Execution/DroneTaskExecutionSystem.cs), [방출·회수 인계](../../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskLifecycleApplySystem.cs).

## DroneTaskRequest / DroneTaskRequestDecision / DroneTaskRequestExecution / DroneTaskRequestResult

Task 9.4의 외부 생성/취소 요청이다. Submit은 시뮬레이션 전 메인 스레드에서 별도의 World 접수 순번을 부여한다. Decision은 자격/기존 작업/앞선 생성 요청을 판단하고 Execution만 생성 결과의 CreationRecorded를 작성한다. 생성 결과의 deferred Task는 생성과 같은 ECB에만 기록한다. Decision은 Open/비종료 배정이 남은 Closed root의 취소를 승인한다. Lifecycle은 자체 정산 뒤 자격을 재판정하지 않고 root Cancelled를 내부 반영하며 마지막 참조가 정리되면 삭제와 차단/결과를 EndSimulation에 기록한다. 요청/두 scratch 제거와 결과 추가도 같은 경계이며 TryConsumeResult가 결과 Entity를 한 번 소비한다. 수동 우선순위/일시 중단/재개는 없다. 활성 취소/예약 반환/화물 보존은 [취소/실패 계약](../../Specifications/DroneTaskCancellation.md)을 따른다. [요청 명세](../../Specifications/DroneTaskRequests.md).

## DroneTaskGenerationBlockElement / DroneTaskGenerationDecisionElement

기존 후보 singleton에서 Block은 사용자 취소의 영속 원본, Decision은 매 틱 재작성하는 제어 의도다. 공급 키는 현장/품목, 회수 키는 실물 Entity다. Decision은 취소 예정 root를 이번 틱 생성/신규 후보/재배정 목적지에서 제외한다. Lifecycle만 성공한 생성/취소 순서로 Block을 EndSimulation에 게시하며 거부는 원본을 바꾸지 않는다. 유효한 명시적 생성이 차단을 해제하고 대상 소멸 시 회수한다. 별도의 재고/공간 예약이나 인덱스 캐시가 아니다.

## DroneTaskCreationDecisionElement

- **종류·부착 대상·목적:** IBufferElementData. DroneTaskCandidateDecisionElement와 같은 공유 singleton에 있는 공급·회수 작업 생성 의도 버퍼다. 작업 필요 여부의 판단과 실제 생성 명령 실행을 분리한다.
- **필드:** Kind는 ConstructionSupply/WorldItemRecovery, Target은 현장/회수 실물, ItemType은 품목, RecoveryReason은 None/SiteClearance/DroneDrop이다. 의도에는 작업 생성 번호나 실제 작업 엔티티가 없다.
- **Producer:** DroneSchedulingUtility.GetOrCreateCandidates가 초기화한다. DroneTaskDecisionSystem의 생성 판단 메서드가 매 틱 Clear한 뒤 공급 필요량·현장 범위·월드 소유권·활성 Destroy·기존 작업을 확인하여 중복 없는 의도를 작성한다. 공급 현장은 PlacementStamp 순서이며 공급 의도 뒤에 회수 의도를 기록한다.
- **Consumer·수명:** DroneTaskExecutionSystem이 순서대로 소비하며 대상 생존/타입과 같은 ECB에 기록한 생성 명령 중복을 확인한다. 기존 root 중복은 선행 Decision이 보장하며 중간 Reservation은 root를 생성/삭제하지 않는다. 유효 의도에만 생성 번호를 발급하고 EndSimulation ECB에 작업 생성을 기록한 뒤 버퍼를 Clear한다. 의도의 수명은 Decision부터 Execution까지이며 작업 실체화는 EndSimulation다.
- **한계:** Execution 이후 상태 변경까지 의도가 보장하지 않는다. 이후 무효가 된 작업은 다음 Decision에서 배정 대상에서 제외한다. 실제 드론 생성·이동·자재 인계 명령도 아니다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneTaskCreationDecisionElement.cs), [작성](../../../Assets/Scripts/Systems/Drones/Decision/DroneTaskDecisionSystem.cs), [소비](../../../Assets/Scripts/Systems/Drones/Execution/DroneTaskExecutionSystem.cs), [초기화](../../../Assets/Scripts/Common/DroneSchedulingUtility.cs).

## DroneTaskInvalidationDecisionElement

- **종류·부착 대상·목적:** IBufferElementData. 후보와 같은 공유 singleton에서 작업·배정이 더 이상 유효하지 않다는 판단 결과를 StateApply로 전달한다. 판단은 Decision 시스템이 수행하며 이 버퍼가 판단 결과를 보관한다. 생성 시 DroneSchedulingUtility.GetOrCreateCandidates가 부착한다. 기존 DroneTaskLifecycleDecisionElement를 이 이름으로 변경했다.
- **필드:** Kind는 DroneTaskInvalidationDecisionKindEnum의 CloseTask/InvalidateAssignment/CompleteTransportTask/CancelAssignment/FinishDemolitionTask, Target은 상위 작업 또는 하위 배정, AssignmentRevision은 판단한 배정 버전이다. 상위 작업 닫기는 배정 버전을 사용하지 않는다. CompleteTransportTask는 완료량==목표량/확보량0의 다음 Decision 의도이며 Lifecycle이 최신 값을 재검사한다. CancelAssignment는 유효한 작업도 사용자 승인으로 중단하는 의도다. 무효/취소 배정은 적재품이 있으면 재배정 상태로 보존하므로 모든 행이 즉시 종료를 뜻하지는 않는다.
- **Producer:** DroneTaskDecisionSystem이 매 틱 비운 뒤 Open 작업의 유효성과 배정의 정리 필요성을 읽기 검사하여 의도를 작성한다. 무효/취소 대상은 같은 Decision의 후보 계산에서도 제외한다. 이때 작업·배정 상태나 수행자 연결은 변경하지 않는다.
- **Consumer·수명:** Reservation은 같은 의도의 남은 예약/확보량을 반환한다. Movement/자동 출고/행동 Producer는 승인된 취소의 이번 틱 실행을 차단하고 외부 행동은 최종 인계에서 거부한다. DroneTaskLifecycleApplySystem이 Target의 생존/컴포넌트와 배정 버전을 확인한다. 일반 무효화는 최신 무효 상태를 재검사하며 명시 취소는 Decision 승인 자체를 종료 근거로 사용한다. 종료 작업은 Closed로, 무효 배정은 적재가 있으면 Retargeting/없으면 Cancelled로 반영한다. 빈 적재의 수행자 연결은 같은 배정을 가리킬 때만 해제한다. 이후 의도 버퍼를 Clear한다.
- **삭제 보류:** Apply는 미정산 예약과 행동 요청·결과 참조가 남은 배정을 삭제하지 않는다. 보류된 배정은 다음 Decision이 다시 검사한다. 이미 Closed/Cancelled인 상위 작업은 의도 존재 여부와 별도로 연결 배정이 사라진 후 EndSimulation에 삭제한다. 의도 기록만으로 예약 해제나 실물 반환이 일어나지는 않는다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneTaskInvalidationDecisionElement.cs), [작성](../../../Assets/Scripts/Systems/Drones/Decision/DroneTaskDecisionSystem.cs), [소비·보류](../../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskLifecycleApplySystem.cs), [초기화](../../../Assets/Scripts/Common/DroneSchedulingUtility.cs).

## DroneTaskCandidateDecisionElement

- **종류·부착 대상·목적:** IBufferElementData. World의 단일 후보 관리 엔티티에서 이번 틱 Decision 후보를 보관한다. 수행자별로 작업·공급원·목적지 조합의 여러 제안이 생길 수 있다. 선택·예약·공개 상태는 이 버퍼에 쓰지 않는다.
- **필드:** Worker/Task·Source/Destination·ItemType·RouteRequest는 작업 조합이다. Assignment=Null은 신규, 그 외는 적재 재배정이고 AssignmentRevision은 기존 버전이다. NextAction/DropPosition은 다음 행동/선택 목표이며 WorkerObservationRevision은 관측 버전이다. Quantity는 판단 당시 수량 상한이다.
- **Writer·Consumer·수명:** Decision이 시작할 때 Clear하고 유효한 신규·적재 재배정 후보를 작성한다. Reservation은 읽기 검사 후 수행자당 한 후보를 선택해 DroneTaskPendingPublicationElement에 복사한다. 후보 제거가 실제 예약량을 변경하지 않는다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneTaskCandidateDecisionElement.cs), [Decision](../../../Assets/Scripts/Systems/Drones/Decision/DroneTaskDecisionSystem.cs), [선택·예약](../../../Assets/Scripts/Systems/Drones/Reservation/DroneTaskReservationSystem.cs).

## DroneTaskPendingPublicationElement

- **종류·부착 대상·목적:** IBufferElementData. 순수 후보와 같은 World 관리 엔티티에서 선택된 배정과 공개 전 예약을 보관한다. Decision 후보와 수명을 분리한다.
- **추가 이유:** 기존 후보 버퍼에 섞여 있던 한 틱 판단 결과와 공개 대기 상태의 수명을 분리했다. Reservation에서 현장 합계를 먼저 올리고 배정은 나중에 ECB로 공개하므로, 그 사이에는 최종 공개 입력과 실패 시 되돌릴 확보량이 필요하다. 별도 ECS 타입 자체가 필수인 것은 아니지만, 현재 구현은 후보를 매 틱 비워도 정산 근거가 사라지지 않도록 별도 버퍼를 사용한다. 공급 외의 회수·보관·방출 선택도 이 버퍼를 Publish 입력으로 사용한다.
- **필드:** Candidate는 선택 당시 후보 스냅샷이며 Candidate.Quantity는 Decision의 상한이다. Quantity는 Reservation 승인량으로 시작해 Publish의 재검사 상한으로 쓰인다. Publish 성공 시 최종 양수 수량으로 갱신하고 공개 불가/실패 시 0으로 만든다. 실제 배정의 AssignedQuantity는 그 최종 수량을 ECB에 기록해 EndSimulation에서 공개한다. CommittedQuantity는 현장 합계에 먼저 더한 공급 예약의 인계/롤백 근거이며 Publish가 축소분을 해제하면 함께 줄인다. 회수·보관·방출은 Quantity가 양수여도 CommittedQuantity=0이다. PublicationQueued는 Publish의 최종 ECB 기록 완료이며 실제 공개 완료를 뜻하지 않는다.
- **Producer·선택:** Reservation은 기존 미공개·무효 예약을 정산하고 적재 그룹을 최초 작업 순서로 먼저 선택한 뒤 신규 우선순위를 적용한다. 일반 운반 선택과도 수행자 중복을 막되 이 버퍼에는 기존 공급/회수/적재 재배정만 기록하며 공급 현장만 예약한다. 공급원 재고·보관 공간은 예약하지 않는다.
- **Publish·인계:** 최신 수행자·배정 revision·경로·대상·수량을 재검사해 무효/축소 예약을 롤백한다. 신규 배정 또는 기존 배정 revision 변경과 수행자 연결·개별 예약을 EndSimulation에 함께 기록하고 PublicationQueued를 설정한다.
- **소비·수명:** 다음 Reservation에서 공개 기록은 제거한다. 미공개 CommittedQuantity는 먼저 현장 합계에서 해제하고 기록을 제거한다. Decision은 Clear하지 않고 ProjectedRemaining에서 해제 예정량만 읽는다. 공개되어 개별 예약으로 인계한 수량은 다시 해제하지 않는다.
- **공개 후 합계:** PublicationQueued=true가 되어도 이 행의 CommittedQuantity 숫자는 다음 Reservation까지 남을 수 있다. EndSimulation 이후에는 같은 수량을 개별 ConstructionSupplyReservation이 담당하므로, 현장 합계를 계산할 때 공개 기록의 숫자를 다시 더하지 않는다. 이 표시는 ECB 재생의 성공을 별도로 확인하는 상태가 아니며, 최종 ECB를 재생한 뒤 다음 틱을 시작하는 실행 경계를 전제로 한다.
- **수량 예:** 부족량 8·공통 용량 5에서 후보 두 개의 상한이 각각 5여도 첫 pending은 5, 두 번째는 3만 승인한다. 순수 후보를 Clear해도 공개 대기 예약의 정산 근거는 남는다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneTaskPendingPublicationElement.cs), [선택·정산](../../../Assets/Scripts/Systems/Drones/Reservation/DroneTaskReservationSystem.cs), [공개](../../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskAssignmentPublishSystem.cs), [예상 부족량](../../../Assets/Scripts/Common/DroneSchedulingUtility.cs).

## DroneTaskAssignment

- **종류·부착 대상·목적:** 일반 IComponentData. 상위 작업·현장·수행자와 분리된 하위 작업 엔티티다. 현장 취소 이후에도 적재 실물을 처리할 수 있도록 독립시킨다.
- **필드:** Worker/Task·Source/Destination은 배정 대상이다. OriginalTaskCreationSequence는 최초 순서, Revision/LastAppliedActionSequence는 버전/소비 번호, State/NextAction은 진행/행동이다. ItemType/AssignedQuantity는 배정량이며 실제 적재 원본은 StoredItemElement다. DropPosition은 선택한 방출 도착 목표이고 GridPosition/관측 위치를 대신하지 않는다.
- **Producer·공개:** DroneTaskAssignmentPublishSystem이 유효 후보를 Revision=1, LastAppliedActionSequence=0, MovingToSource로 기록한다. 현장 공급과 일반 반입/반출은 CollectFromStorage, 월드 회수는 RecoverWorldItem을 다음 행동으로 둔다. Source/Destination은 항상 출발/도착 대상이며 일반 반입 root의 Target은 도착, 반출 root의 Target은 출발 건물이다. EndSimulation에 유형별 확보 기록/수행자 연결과 함께 공개하며 생성만으로 드론이 움직이지 않는다. 공개된 배정은 다음 틱부터 Movement가 비행하며 일반 운반의 실제 픽업/미수집분 반환은 기존 Lifecycle에 연결했고 실제 납품/완료량 정산은 [납품 명세](../../Specifications/DroneDelivery.md)에 연결했다.
- **재배정 공개:** 적재품은 새 배정 엔티티를 만들지 않고 기존 엔티티의 Revision을 증가시킨다. 최초 순서·적재 실물·출처·기존 행동 결과 참조를 보존하고 LastAppliedActionSequence=0, Source=Null, 새 Task/Destination/NextAction·예약 revision을 기록한다. revision이 uint.MaxValue이면 증가하지 않는다. 이전 revision 신호는 공통 인계 검사가 거절한다.
- **공통 적재량 변경:** 빈 수행자의 새 배정 수량은 현재 공통 한도를 넘지 않는다. 이미 공개한 수량·예약·적재와 적재품 재배정량을 공통 한도 변경만으로 줄이지 않는다. 기존 적재 재배정은 현재 실물 수량과 현장 필요량을 사용한다.
- **기존 현장 공급/회수의 인계 후 진행:** 수집 양수는 Origin=Supply/Recovery와 다음 공급/보관 행동을 설정하고 MovingToDestination이 된다. 0개는 예약을 해제하고 Cancelled/연결 해제로 끝난다. 공급·보관·방출 후 빈 적재는 Completed/연결 해제, 잔여품은 Retargeting/NextAction=None이다. 다음 Decision이 Direct 경로로 새 목적지를 선택하고 Reservation/Publish가 같은 배정을 갱신한다. 실제 이동/도착은 Movement가 처리하며 자동 픽업/납품/안전 방출 신호는 공통 DroneActionRequestDecisionSystem이 만든다. 공개 도착 뒤 기존 접수/계획/최종 인계를 사용하며 [화물 복구 계약](../../Specifications/DroneCargoRecovery.md)을 따른다.
- **무효화·수명:** Reservation은 무효 대상·종료 상태의 개별 현장 예약을 해제한다. Decision은 무효 배정을 후보 경합에서 제외하고 InvalidateAssignment 의도를 기록한다. DroneTaskLifecycleApplySystem은 의도의 배정 버전과 현재 무효 여부를 재검사하여 적재가 없으면 Cancelled로 바꾸고 같은 배정을 가리키는 수행자 연결을 해제한다. 행동 요청·결과의 참조와 잔여 예약이 없을 때 EndSimulation에 삭제한다. 적재가 있으면 Retargeting으로 보존한다.
- **수집한 회수품의 보관 예외:** 회수 실물이 수행자에 들어오면 원래 월드 회수 작업은 무효·Closed가 될 수 있다. 원래 실물이 수행자 소유이고 Origin=Recovery, NextAction=StoreCargo이며 보관 목적지가 유효하면 보관 이동 배정은 계속 유효하다. 상위 회수 작업이 Closed라는 이유만으로 수집한 실물의 보관을 막지 않는다.
- **식별 검사:** Decision·Execution·StateApply가 DroneItemTransferValidationUtility.TryValidate의 읽기 검사를 공유한다. 배정 Revision과 양수 Action.Sequence, LastAppliedActionSequence·수행자 연결·행동·대상·관측 버전을 대조하며 StateApply에서만 유효 요청의 행동 번호를 반영한다. 수량이 0인 유효 요청도 번호를 소비하므로 같은 번호를 다시 인계하지 않는다. 이 배정 내부 번호는 World ReceiptSequence와 목적이 다르다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneTaskComponents.cs), [공개](../../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskAssignmentPublishSystem.cs), [인계·진행](../../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskLifecycleApplySystem.cs), [회수 보관 유효성](../../../Assets/Scripts/Common/DroneSchedulingUtility.cs), [무효 배정 정리](../../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskLifecycleApplySystem.cs).

## ConstructionSupplyReservation

- **종류·부착 대상·목적:** 일반 IComponentData. 하위 배정과 같은 엔티티의 현장 공급량 예약이다. 공급원 재고·보관 슬롯 잠금이 아니다.
- **필드:** Site, ItemType, AssignmentRevision, RemainingQuantity다. 배정 Revision과 예약 Revision은 일치해야 하며 0은 예약 없음/정산 완료다. 회수 배정에도 부착하지만 Site=Null, RemainingQuantity=0이다.
- **생성·합계:** DroneTaskReservationSystem이 ConstructionSupplyReservationUtility.Reserve로 요구 버퍼 ReservedQuantity를 증가시키고 공개 전까지 DroneTaskPendingPublicationElement의 CommittedQuantity에 근거를 보관한다. Publish가 EndSimulation에 배정과 개별 기록을 함께 생성한다. 현장 합계에는 공개된 개별 기록과 미공개 공개 대기 기록이 모두 포함된다. Publish의 잔여량 검사에서는 해당 공개 대기 기록 자신의 CommittedQuantity를 다시 더해 자기 예약 때문에 승인량이 줄지 않도록 한다.
- **현재 해제:** Reservation이 신규 예약 전에 미공개 공개 대기 기록과 무효/종료 배정의 잔여량을 해제한다. Publish는 이후 최종 축소·무효 수량만 되돌린다. Decision은 해제 예정량을 읽어 예상 부족량을 계산할 뿐 현장 합계와 개별 예약을 수정하지 않는다. 현장이 사라졌으면 버퍼에 접근하지 않고 개별 RemainingQuantity는 0으로 정리한다. 같은 품목의 여러 요구 행은 유틸리티가 순서대로 나누어 예약·해제한다.
- **실물 인계·재배정 정산:** Lifecycle은 부분 수집량만 예약에 남기고 부족분을 해제하며 0개는 모두 해제한다. 현장 공급은 실제 인계량만 도착량에 더하고 해당 배정 잔여 예약을 정리한다. 재배정 전 Reservation이 기존 예약을 0으로 정산하고 새 공급 현장에만 예약한다. Publish는 증가한 배정 revision과 새 예약을 함께 공개한다. 초과 실물은 보존하며 다른 배정의 예약을 해제하지 않는다.
- **근거:** [정의](../../../Assets/Scripts/Components/Construction/ConstructionSupplyReservation.cs), [선택·예약](../../../Assets/Scripts/Systems/Drones/Reservation/DroneTaskReservationSystem.cs), [예약 유틸리티](../../../Assets/Scripts/Common/ConstructionSupplyReservationUtility.cs), [실물 정산](../../../Assets/Scripts/Common/DroneItemTransferUtility.cs), [인계](../../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskLifecycleApplySystem.cs), [공개](../../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskAssignmentPublishSystem.cs), [현장 요구](../../../Assets/Scripts/Components/Construction/ConstructionComponents.cs).

## DroneWorker

- **종류·부착 대상·목적:** 빈 일반 IComponentData. 드론 작업을 수행할 수 있는 엔티티의 표시다. 수행자별 적재량을 보관하지 않으며 최대 적재량의 원본은 World의 DroneCapacityState다.
- **Producer/Reader:** DroneSpawnUtility가 EndSimulation에 초기 구성을 기록한다. 명시적 출고/수납 요청은 DroneStationLifecycleApplySystem이 EndSimulation에 반영한다. Decision/Reservation/Publish와 Execution의 경로 적용 검사가 이 표시와 관측/활성 배정 연결/적재 출처/StoredItemElement를 확인한다. 공통 적재량이 없거나 0 이하이면 신규 배정을 진행하지 않는다.
- **한계·수명:** 현재 신규 배정과 수집은 빈 적재 수행자만 대상이다. 실제 인계는 한 품목과 이미 배정한 수량 상한을 확인한다. 수행자 자체의 종료 시 적재 반환은 후속이다. 등록된 수행자와 함께 유지하며 타입만 추가한다고 드론이나 버퍼가 생성되지 않는다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneWorkerComponents.cs), [수행자 검사](../../../Assets/Scripts/Common/DroneSchedulingUtility.cs), [보관 버퍼](../../../Assets/Scripts/Components/Storage/StorageComponents.cs).

## DroneCapacityState

- **종류·부착 대상·목적:** 일반 IComponentData. World의 단일 엔티티에 붙는 모든 드론의 공통 최대 적재량 상태다. CarryingCapacity는 한 품목의 최대 실물 개수이며 Storage.SlotCount·품목 MaxStack과 독립적이다. 수행자마다 같은 값을 복제하지 않는다.
- **현재 Reader:** 빈 수행자 신규 후보·Reservation 승인·ViaSource 경로 검사·Publish 공개는 공통 값으로 제한한다. 부재/0 이하면 신규 배정은 대기하고 singleton을 자동 생성하거나 기본값으로 대체하지 않는다. 이미 적재한 수행자의 Direct 경로·재배정 수량에는 이 제한을 소급 적용하지 않는다.
- **Producer/Writer:** DroneConfigInitializationSystem이 Resources의 명시적 carryingCapacity를 한 번 게시하고 유효한 기존 값을 보존한다. 중복/무효/설정 실패는 공통 Fatal이며 기본값 대체/자동 재시도는 없다. 연구 갱신 Writer는 후속이다.
- **변경 적용·수명:** World 수명 동안 공통 값을 유지한다. 연구로 한도가 증가하면 이후 새로 배정하는 작업부터 적용하며 기존 AssignedQuantity·ConstructionSupplyReservation·StoredItemElement를 다시 늘리거나 정산하지 않는다. 후보는 공개 전까지 현재 값을 재검사한다. 실제 수집은 이미 공개한 AssignedQuantity와 해당 예약·현재 현장 잔여량을 상한으로 삼아 공통 값 변경을 소급 적용하지 않는다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneCapacityState.cs), [공통 값·후보 검사](../../../Assets/Scripts/Common/DroneSchedulingUtility.cs), [예약](../../../Assets/Scripts/Systems/Drones/Reservation/DroneTaskReservationSystem.cs), [경로 적용 검사](../../../Assets/Scripts/Systems/Drones/Execution/DroneTaskExecutionSystem.cs), [최종 공개](../../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskAssignmentPublishSystem.cs).

## DroneWorkerObservation

- **종류·부착 대상·목적:** 일반 IComponentData. 외부 수행부가 수행자에 게시할 위치·신규 작업 가능 상태의 스냅샷이다. 관리 측의 배정 상태와 작성 책임을 분리한다.
- **필드·검사:** Position은 출발 월드 좌표, Revision은 양수 관측 버전, CanAcceptTask는 신규 작업 가능 여부다. 후보에는 유한한 위치·양수 버전·작업 가능 상태가 필요하며 경로 결과와 후보 공개 시 요청의 버전·위치와 대조한다.
- **Writer/Reader:** DroneSpawnUtility가 입력 위치/Revision 1/CanAcceptTask=false로 최초 관측을 기록한다. DroneWorkerObservationPreparationSystem은 마지막 확정 위치/배터리/연결/화물/활동으로 관측을 준비하며 값이 바뀔 때만 Revision을 증가시킨다. Decision과 예약/공개 검사에서 읽는다.
- **수명:** 수행자 수명 동안 최신 스냅샷을 유지하는 계약이다. GridPosition이나 렌더 위치 원본을 대체하지 않는다.
- **원본 연결:** Movement Execution은 LocalTransform/GridPosition/렌더와 실제 배터리를 함께 갱신한다. 다음 Preparation이 확정 위치에서 관측을 준비하며 관측만으로 원본 위치나 배터리를 만들지 않는다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneWorkerComponents.cs), [경로·후보 검사](../../../Assets/Scripts/Common/DroneSchedulingUtility.cs).

## DroneWorkerAssignment

- **종류·부착 대상·목적:** 일반 IComponentData. 수행자에서 관리 측이 소유하는 활성 배정 연결이다. Assignment=Null은 배정 없음이다.
- **Writer/순서:** DroneSpawnUtility가 Assignment=Null로 초기화한다. Reservation의 동일 틱 중복 선택 방지와 기존 연결 검사로 수행자당 하나를 고른다. Publish가 EndSimulation에 새 배정과 연결을 함께 기록한다. DroneTaskLifecycleApplySystem은 검증한 배정의 0개 수집 또는 인계 후 빈 적재 완료 시 연결을 해제한다. DroneTaskLifecycleApplySystem은 무효/종료된 빈 적재 배정을 정리할 때 같은 배정을 가리키는 연결만 해제한다.
- **Reader·수명:** Decision·Reservation·Publish는 신규 배정 가능 여부를 읽고 후속 수행부는 작업 조회 시작점으로 쓴다. 수행자에 유지하며 Null 변경만으로 적재품이나 회수 출처를 지우지 않는다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneWorkerComponents.cs), [공개](../../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskAssignmentPublishSystem.cs), [인계 후 해제](../../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskLifecycleApplySystem.cs), [무효화](../../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskLifecycleApplySystem.cs).

## DroneCargoState

- **종류·부착 대상·목적:** 일반 IComponentData. 수행자의 적재 출처 제약이다. Origin은 None/Supply/Recovery이며 실제 목록·품목·수량은 StoredItemElement가 원본이다.
- **현재 Reader:** 신규 배정 검사에서 Origin=None 및 빈 보관 버퍼를 요구한다. 적재가 남은 수행자에게 신규 작업을 중복 배정하지 않는다.
- **Writer/제약:** DroneSpawnUtility가 Origin=None과 빈 화물 버퍼로 초기화한다. DroneTaskLifecycleApplySystem이 수집 성공 시 Supply 또는 Recovery로 설정하고 인계 후 빈 적재이면 None으로 비운다. Recovery는 보관 건물에 넣기 전 현장에 직접 공급할 수 없도록 SupplyConstructionSite에서 Origin=Supply를 요구한다. 상위 작업 종료나 Retargeting만으로 출처를 지우지 않는다. 적재품 목적지 재탐색은 Direct 요청을 사용하고 Flight Decision의 실제 경로/충전 경유 평가 뒤 Movement가 비행한다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneWorkerComponents.cs), [빈 수행자 검사](../../../Assets/Scripts/Common/DroneSchedulingUtility.cs), [출처 반영·공급 검사](../../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskLifecycleApplySystem.cs).

## DroneRouteEvaluationRequest

- **종류·부착 대상·목적:** 일반 IRequestComponent. 독립 요청 엔티티의 경로 평가 입력이다. 실제 비행·경로 계산을 실행하는 데이터가 아니다.
- **필드:** Worker/관측 revision·Assignment/배정 revision·EvaluationRevision과 Kind/출발·공급원·목적지 위치를 가진다. Direct의 IsDropPositionSearch=true는 건물 목적지 평가가 아니라 현장 외부 방출 셀 검색이다. 이때 Source/Destination은 Null이고 원점 관측과 배정 버전을 고정한다.
- **현재 Producer:** Decision이 배정 전 ViaSource 입력을 DroneRouteDecisionElement의 Create 명령에 기록하고, DroneTaskExecutionSystem이 현재 대상·관측 스냅샷을 적용 guard로 확인한 뒤 EndSimulation에 요청을 공개한다. Assignment=Null, AssignmentRevision=0이며 공급은 수행자→공급원→현장, 회수는 수행자→실물→보관처다. 같은 수행자·공급원·목적지의 현재 유효 요청을 재사용하고 같은 틱 중복 의도도 막는다.
- **대기·Consumer·삭제:** 외부 평가자는 아직 없다. 필요한 경로의 결과 대기 중 요청을 유지한다. Decision과 후보 재검사는 관측·대상 위치·생존·철거 예정 상태를 대조한다. Decision은 무효 스냅샷·잘못된 결과를 제외하고 기존 요청마다 Retain 또는 Remove 명령을 기록한다. Execution은 명시된 Remove만 EndSimulation에 엔티티째 삭제하고 Retain에는 ECS/ECB 변경을 하지 않는다. 의도 버퍼에 없는 요청을 임의로 삭제하지 않는다. 유효 결과는 현재 관측에서 재사용되며 배정 후 수행자가 더 이상 빈 수행자가 아니면 다음 틱의 Decision·Execution을 거쳐 정리된다.
- **적재 재배정 Direct 경로:** Decision이 Assignment/AssignmentRevision과 현재 관측→목적지 Direct 요청을 작성한다. Source=Null이며 현재 관측 버전/위치·배정 revision·대상 생존/위치·철거 예정 상태로 재검사한다. 관련 결과 None은 대기하고 Reachable의 실제 거리/동률 PlacementStamp를 비교한다. Flight Decision이 실제 경로 거리/충전 경유를 평가한다. 에너지 대기는 도달 불가나 안전 방출 허가로 해석하지 않는다.
- **방출 셀 검색:** 현재 셀이 현장 내부일 때 Direct/IsDropPositionSearch 요청으로 Flight Decision에 가장 가까운 실제 도달 가능한 현장 외부 셀을 요청한다. 관리층은 검색 범위·기본 거리·대체 좌표를 만들지 않는다. None/Unreachable이면 적재 대기하고 Reachable/HasDropPosition 결과의 현재 현장 외부 조건을 다시 확인한다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneRouteContracts.cs), [생성·유지 판단](../../../Assets/Scripts/Systems/Drones/Decision/DroneTaskDecisionSystem.cs), [요청 생성·정리](../../../Assets/Scripts/Systems/Drones/Execution/DroneTaskExecutionSystem.cs), [스냅샷 검사](../../../Assets/Scripts/Common/DroneSchedulingUtility.cs).

## DroneRouteDecisionElement

- **종류·부착 대상·목적:** IBufferElementData. DroneTaskCandidateDecisionElement와 같은 singleton에 있는 한 틱의 경로 생성·유지·삭제 명령 버퍼다. 경로 요청 엔티티의 실제 생성·삭제와 Decision의 판단을 분리한다.
- **필드:** Kind는 Create/Retain/Remove다. Create는 Request에 새 DroneRouteEvaluationRequest 입력을 담고, Retain/Remove는 ExistingRequest로 기존 요청을 지정한다. Kind 없이 ExistingRequest의 Null 여부만으로 동작을 추론하지 않는다.
- **Producer·Reader·소비:** DroneSchedulingUtility.GetOrCreateCandidates가 시스템 초기화 시 버퍼를 구성한다. Decision은 매 틱 비운 뒤 경로 생성 명령과 기존 요청별 유지/삭제 명령을 작성한다. DroneTaskExecutionSystem은 Create의 최신 관측·대상 스냅샷을 확인하여 생성하고 Remove의 기존 요청을 삭제하도록 EndSimulation ECB에 기록한다. Retain은 준비된 내부 평가 결과를 EndSimulation에 공개하고 요청을 유지한다. 소비 후 의도 버퍼를 Clear한다. 의도에 없는 기존 요청을 찾아 삭제하는 추가 판단은 하지 않는다.
- **수명·한계:** Decision에서 Execution까지 유지하는 임시 판단 데이터다. 의도를 기록한 순간 요청 엔티티가 생기는 것은 아니며 외부 평가자는 EndSimulation 이후에 공개된 요청을 읽는다. Execution 뒤에 변경된 대상은 다음 Decision에서 재검사한다. 거리 계산·이동 실행 결과도 아니다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneRouteDecisionElement.cs), [작성](../../../Assets/Scripts/Systems/Drones/Decision/DroneTaskDecisionSystem.cs), [실행·소비](../../../Assets/Scripts/Systems/Drones/Execution/DroneTaskExecutionSystem.cs), [초기화](../../../Assets/Scripts/Common/DroneSchedulingUtility.cs).

## DroneRouteEvaluationResult

- **종류·부착 대상·목적:** 일반 IComponentData. 경로 요청과 같은 엔티티에 외부 평가자가 게시할 결과다. 배정 승인·실물 인계 성공이 아니다.
- **필드·유효성:** EvaluationRevision은 요청 버전과 같고 Status는 None/Reachable/Unreachable이다. Reachable의 TotalDistance는 유한한 실제 거리다. 방출 검색 성공은 HasDropPosition=true와 DropPosition을 명시해야 하며 기본 0 좌표를 성공으로 해석하지 않는다. 목표 좌표는 실제 드론 위치가 아니고 None/Unreachable은 방출 대기다.
- **현재 Consumer:** Decision이 유효 결과를 후보 비교에 사용하고 Reservation·Publish도 재검사한다. 현재 재고·공간 조건을 만족한 관련 공급원/보관처 중 평가를 기다리는 것이 있으면 해당 작업 후보를 대기시킨다. Unreachable은 후보에서 제외한다.
- **Producer/수명:** Flight Decision이 이번 틱 DroneRouteEvaluationDecision을 만들고 Task Decision은 이를 우선 읽는다. Task Execution은 유지되는 결과만 EndSimulation에 공개한다. 요청 유효기간 동안 재사용하며 무효화되면 요청/결과를 같은 엔티티로 삭제한다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneRouteContracts.cs), [결과 검사](../../../Assets/Scripts/Common/DroneSchedulingUtility.cs), [후보 생성](../../../Assets/Scripts/Systems/Drones/Decision/DroneTaskDecisionSystem.cs).

## DroneActionReadyRequest

- **종류·부착 대상·목적:** 일반 IRequestComponent. 외부 수행부 또는 픽업/납품 공통 자동 Producer가 독립 Entity에 게시하는 실물 인계 시도다. 성공 수량은 입력하지 않는다. 자동 납품 신호도 현재 연결되어 있다.
- **필드·계약:** ReceiptSequence는 World의 접수 순서다. Action은 배정·버전·행동 번호·수행자·행동·대상의 DroneActionIdentity다. WorkerObservationRevision은 관측 버전, WorldPosition은 DropCargo 격자 위치다. CollectFromStorage, RecoverWorldItem, SupplyConstructionSite, StoreCargo, DropCargo를 구분한다.
- **Producer·접수:** 외부 입력은 시뮬레이션 전, 자동 픽업/납품은 관측 준비/Task Decision 뒤 같은 DroneDecision에서 DroneActionRequestUtility.Submit으로 World 접수 번호와 요청 Entity를 게시한다. 전달된 ReceiptSequence는 발급값으로 덮어쓰며 DroneItemTransferDecision과 실물·슬롯 계획 버퍼도 부착한다. 실물·예약을 바꾸거나 행동 성공을 만들지는 않는다.
- **Decision·Execution:** DroneItemTransferDecisionSystem은 요청의 초기 자격만 기록한다. Execution의 OrderFirst 인계 계획 시스템은 이번 드론 물류 쓰기 전의 실물 전체 ID·요구량/수량 상한·기존 슬롯 품목/개수를 확정한다. 이번 틱 수집 이후에만 가능한 Supply 신호를 미리 함께 넣으면 Decision 시점의 NextAction 불일치로 거절한다. 공급하려면 다음 틱에 새 행동 신호를 제출해야 한다.
- **StateApply 검사:** DroneTaskLifecycleApplySystem은 접수 순서로 초기 자격·계획 준비 여부와 공통 TryValidate 검사를 다시 확인한다. 계획 안의 실물만 현재 버퍼·생존·품목·유효 Transfer 소유권·Destroy·현장 footprint로 검사한다. 같은 틱 건물 종료의 입고품·출고 여유는 계획에 포함할 수 있다. 드론 적용 중 새 수집품·새 공간은 추가하지 않는다. 이동/도착과 관측 원본 Writer는 연결되어 있다. 자동 픽업은 도착 공개 다음 틱 실제 도착/배터리 자격을 거쳐 요청한다. 자동 납품도 현재 실제 도착으로 요청하며 도착만으로 인계 성공을 만들지 않는다.
- **수명·결과 공개:** 처리한 요청과 세 판단/계획 컴포넌트 제거, 같은 엔티티의 DroneItemTransferResult 추가를 EndSimulation에 기록한다. 내부 pending 집합으로 ECB 재생 전 시스템 반복 호출의 중복 적용을 막는다. Lifecycle은 남은 요청 참조가 있는 배정의 삭제를 보류한다. 미소비 결과가 있는 엔티티에 새 요청을 부착해도 기존 결과를 덮어쓰거나 다시 인계하지 않는다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneActionContracts.cs), [접수·외부 소비 API](../../../Assets/Scripts/Common/DroneActionRequestUtility.cs), [초기 자격](../../../Assets/Scripts/Systems/Drones/Decision/DroneItemTransferDecisionSystem.cs), [계획 준비](../../../Assets/Scripts/Systems/Drones/Execution/DroneItemTransferExecutionSystem.cs), [공통 자격 검사](../../../Assets/Scripts/Common/DroneItemTransferValidationUtility.cs), [인계·결과](../../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskLifecycleApplySystem.cs), [유효 소유권](../../../Assets/Scripts/Common/DroneItemTransferUtility.cs), [삭제 보류](../../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskLifecycleApplySystem.cs).

| 행동 | 실제 실물·수량 처리 |
| --- | --- |
| CollectFromStorage | 계획에 포함된 기존 공급품만 현재 버퍼에서 수행자 버퍼로 옮긴다. 계획 상한·자기 예약·현재 현장 잔여량을 넘지 않으며 건물 종료까지 입고된 공급품은 포함하고 드론 처리 중 추가 실물은 제외한다. 부족분 예약은 해제한다. |
| RecoverWorldItem | 계획에 포함된 원실물 하나만 현재 월드 소유권과 SiteClearance의 최신 현장 footprint로 재검사한다. 성공하면 수행자에 넣고 DroneRecoveryPending 제거를 기록한다. 출처는 Recovery다. |
| SupplyConstructionSite | 물류 쓰기 전부터 적재되어 계획에 포함된 Origin=Supply 실물만 현장에 넣는다. 실제 도착량을 더하고 해당 예약을 정리한다. 이번 틱 새 수집품은 다음 틱 공급하며 잔여품은 Retargeting으로 보존한다. |
| StoreCargo | 계획 실물·기존 슬롯 예산 안에서 DroneSchedulingUtility.CanStoreInSlot의 현재 필터·슬롯·MaxStack 조건까지 만족하는 실물만 보관한다. 실제 성공분만 같은 목적지의 공유 예산에 더하고 건물 출고로 열린 공간은 계획에 포함하고 드론 처리 중 새 공간은 추가하지 않는다. 사전·영속 공간 예약은 없다. |
| DropCargo | 계획 실물만 선택한 DropPosition·요청 위치·현재 관측 셀의 일치와 현장 외부 조건을 확인해 반환한다. 새 현장이 목표를 막으면 Retargeting으로 돌리고 실물을 유지한다. 성공분의 회수 표시/렌더 변경은 EndSimulation에 기록하며 새 아이템/Transfer 요청은 만들지 않는다. |

## DroneItemTransferDecision

- **종류·부착 대상·목적:** 일반 IComponentData. 행동 요청 엔티티의 한 틱 인계 자격과 수량 계획이다. 실물·예약·배정 원본을 복제한 상태가 아니라 현재 입력에서 계산한 파생 결과다. Submit이 기본값으로 부착한다.
- **필드·Writer:** CanExecute는 Decision의 공통 자격 검사 결과, Prepared는 Execution의 계획 확정 여부다. WantedQuantity는 행동이 처리하려던 원래 수량이고 MaximumQuantity는 이번 드론 물류 쓰기 전 입력으로 허용한 상한이다. Decision은 CanExecute만 작성하고 나머지는 초기화하며 Execution이 계획값을 채운다.
- **Consumer·수명:** StateApply는 CanExecute/Prepared와 현재 자격을 다시 검사하고 MaximumQuantity를 넘지 않게 계획 실물을 처리한다. 자격·상태가 잘못되면 Rejected다. 자격은 유효하지만 기존 재고/공간이 0이면 CanExecute=true/MaximumQuantity=0으로 유지하여 Unavailable 결과와 정상 0개 정산을 적용한다. EndSimulation에 제거한다.
- **재진입·쓰기 경계:** Prepared가 true이면 같은 Execution 재호출에서 새 재고·공간을 계획에 더하지 않는다. Decision·Execution은 아이템·Owner·원본 보관 버퍼·현장 예약·배정·행동 번호를 쓰지 않는다. 실제 쓰기는 StateApply만 수행한다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneItemTransferDecisions.cs), [입력 부착](../../../Assets/Scripts/Common/DroneActionRequestUtility.cs), [Decision](../../../Assets/Scripts/Systems/Drones/Decision/DroneItemTransferDecisionSystem.cs), [Execution](../../../Assets/Scripts/Systems/Drones/Execution/DroneItemTransferExecutionSystem.cs), [반영·제거](../../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskLifecycleApplySystem.cs).

## DroneItemTransferItemDecisionElement

- **종류·부착 대상·목적:** IBufferElementData. 행동 요청에 붙는 물류 실행 전 사용 가능 실물 ID 목록이다. ItemEntity만 저장하고 소유권·품목·위치의 원본을 대신하지 않는다. Submit이 빈 버퍼를 부착한다.
- **Producer·Consumer:** Execution이 수집·공급·보관·방출의 기존 실물 전체를 중복 없이 기록하고 회수는 원실물 하나를 기록한다. 후보를 미리 수량 상한만큼 잘라내지 않으므로 선행 드론이 일부를 가져가도 다른 기존 실물을 선택할 수 있다. StateApply는 목록 안의 ID만 현재 버퍼·품목·생존·유효 소유권·Destroy로 재검사한다.
- **수명·제약:** 같은 틱 건물 종료까지 입고된 실물은 계획에 포함할 수 있다. 계획 준비 후 드론 처리 중 입고·새 수집으로 생긴 실물은 나중에 추가하지 않는다. 여러 요청이 같은 ID를 후보로 가질 수 있지만 접수 순서의 실제 인계 뒤 현재 원본을 재검사하므로 같은 실물을 중복 사용하지 않는다. 공급원 재고 예약이나 영속 캐시는 아니며 EndSimulation에 제거한다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneItemTransferDecisions.cs), [전체 후보 수집](../../../Assets/Scripts/Systems/Drones/Execution/DroneItemTransferExecutionSystem.cs), [ID·원본 재검사](../../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskLifecycleApplySystem.cs), [실물 검사](../../../Assets/Scripts/Common/DroneItemTransferUtility.cs).

## DroneItemTransferSlotDecisionElement

- **종류·부착 대상·목적:** IBufferElementData. 보관 행동 요청이 물류 실행 전에 읽은 슬롯의 파생 입력이다. SlotIndex·ItemType·ItemCount를 저장하며 원본 Storage/StoredItemElement를 대신하거나 보관 공간을 잠그지 않는다. Submit이 빈 버퍼를 부착한다.
- **Producer·예산:** Execution이 기존 슬롯의 품목·개수와 현재 필터·입력 슬롯·MaxStack에서 가능한 수량을 계산한다. StateApply는 유효한 양수 보관 계획의 목록으로 목적지별 한 틱 공유 예산을 만들고 실제 성공분만 ItemType/ItemCount에 반영한다. 실패한 요청이 공간을 차감하거나 0개 계획이 빈 예산으로 다른 요청을 가로막지 않는다.
- **최종 검사·수명:** 현재 CanStoreInSlot 검사와 기존 품목/개수 예산을 모두 만족하는 슬롯만 사용한다. 건물 출고로 늘어난 여유는 계획에 포함하되 드론 처리 중 늘어난 여유는 기존 예산에 더하지 않으며 선행 입고로 줄어든 공간은 현재 검사에서 제한한다. 영속 공간 예약은 없고 공유 예산은 한 OnUpdate 안에서만 사용한다. 요청 버퍼는 EndSimulation에 제거한다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneItemTransferDecisions.cs), [슬롯 입력 계산](../../../Assets/Scripts/Systems/Drones/Execution/DroneItemTransferExecutionSystem.cs), [성공분 공유·반영](../../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskLifecycleApplySystem.cs), [공통 현재 슬롯 검사](../../../Assets/Scripts/Common/DroneSchedulingUtility.cs).

## DroneActionReceiptSequence

- **종류·부착 대상·목적:** 일반 IComponentData. World의 단일 엔티티가 모든 드론 행동 신호의 접수 순서 원본을 소유한다. 먼저 접수된 유효 신호부터 건물 종료 상태 기준의 계획을 현재 원본으로 재검사해 인계하는 기준이다. 작업 생성 순번이나 배정 내부 Action.Sequence와 별개다.
- **필드·Producer:** NextValue는 다음 양수 접수 번호다. DroneActionRequestUtility.Submit의 입력 경계만 singleton을 생성·초기화하고 번호를 발급한다. 처음 값은 1이며 제출마다 증가한다. 0 또는 ulong.MaxValue이면 예외로 거부하여 순환하거나 번호를 재사용하지 않는다.
- **Reader·수명:** DroneTaskLifecycleApplySystem은 요청에 복사된 ReceiptSequence를 정렬 기준으로 사용한다. 같은 값이면 요청 엔티티 Index·Version으로 순서를 정하지만 정상 제출 API는 서로 다른 번호를 발급한다. singleton은 World 수명 동안 유지하며 요청·결과 소비 후에도 과거 번호를 다시 발급하지 않는다.
- **입력 계약:** 실제 수행부는 Submit으로 요청을 게시해야 한다. 엔티티를 직접 만들고 ReceiptSequence=0으로 두면 인계 검사가 거절한다. 접수 순번만 유효하다고 행동이나 대상이 유효해지는 것은 아니다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneActionReceiptSequence.cs), [유일한 접수 Writer](../../../Assets/Scripts/Common/DroneActionRequestUtility.cs), [순서 적용](../../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskLifecycleApplySystem.cs).

## DroneItemTransferResult

- **종류·부착 대상·목적:** 일반 IComponentData. 내부 인계 Apply가 실제 옮긴 수량을 기록하는 결과다. 수행부의 행동 완료와 구분하며 처리한 행동 요청과 같은 엔티티에 남긴다.
- **필드:** Action은 원래 식별자, Status는 None/Completed/Partial/Unavailable/Rejected, MovedQuantity는 인계한 아이템 엔티티 수다. Completed/Partial은 양수, Unavailable/Rejected는 0, None은 미결정이다.
- **Producer·성공 수량:** DroneTaskLifecycleApplySystem이 초기 자격·계획 준비 여부와 현재 행동 식별자를 검사하고 ItemOwnershipApplySystem.TryTransferItem의 성공 개수로 작성한다. 자격·상태·계획 검사가 실패하면 Rejected, 유효한 기존 입력이 0이거나 최종 검사에서 옮기지 못하면 Unavailable, WantedQuantity 전량이면 Completed, 일부이면 Partial이다. 공통 API가 버퍼·Owner·위치·벨트·렌더를 반영하고 Lifecycle은 배정·도착량/예약을 정산한다. 새 Transfer 요청을 만들지 않는다.
- **Consumer/수명:** 자동 표식의 결과는 공통 DroneActionRequestDecisionSystem이 다음 DroneDecision에서 TryConsumeResult로 소비한다. 표식 없는 외부 요청 결과는 해당 외부 호출자가 EndSimulation 이후 소비한다. 요청이 남았거나 결과가 없거나 None이면 소비하지 않는다. 실제 소비는 결과 Entity 삭제이며 그 전까지 배정 참조를 보존한다.
- **정산 경계/한계:** 결과 소비는 이미 반영한 수량/예약을 다시 정산하지 않는다. 결과가 남아 있어도 적재 재배정은 같은 Entity와 원래 결과 참조를 보존하며 revision을 바꿀 수 있다. 실제 이동/도착/자동 인계의 검증은 각 명세의 범위이며 실제 충전/Play/Player/성능과 구분한다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneActionContracts.cs), [행동 정산·결과 Writer](../../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskLifecycleApplySystem.cs), [실물 반영 API](../../../Assets/Scripts/Systems/Items/StateApply/ItemOwnershipApplySystem.cs), [외부 소비](../../../Assets/Scripts/Common/DroneActionRequestUtility.cs), [배정 참조 검사](../../../Assets/Scripts/Systems/Drones/StateApply/DroneTaskLifecycleApplySystem.cs).

DroneActionIdentity는 요청/결과 안의 일반 구조체다. 인계가 revision/행동 번호·대상 일치를 검사하며 재배정은 revision 증가와 번호 초기화로 이전 신호를 구분한다. World ReceiptSequence는 요청 간 접수 순서다. 완공은 현재 월드 실물과 활성 Destroy로 차단을 갱신하며 회수 작업 존재 자체로 차단하지 않는다.
## DroneMovementConfig

- **종류/부착 대상/목적:** 읽기 전용 singleton IComponentData다. Speed/EmergencySpeed/EnergyPerDistance는 정상 속도/비상 속도/실제 거리당 소비다.
- **Writer/수명:** DroneConfigInitializationSystem이 유한 양수를 다른 공통 드론 설정과 함께 원자적으로 검증/게시한다. 유효한 기존 singleton은 보존하며 World 수명을 따른다.
- **Reader:** Flight Decision의 구간 에너지 계획과 Movement Execution의 실제 이동/소비가 읽는다. [이동 명세](../../Specifications/DroneMovement.md)를 따른다.

## DroneMovementState

- **종류/부착 대상/목적:** 수행자의 IComponentData다. ChargingStation/ReturnStation은 진행 중인 충전 경유/정상 또는 비상 귀환 목적지를 유지한다. IsNormalReturn은 정상 귀환 중 신규 배차/적재 재배정 허용과 비상 귀환/작업 충전 경유 차단을 구분한다. 위치/배터리/작업 상태를 복제하지 않는다.
- **Writer/수명:** Spawn/Activate가 같은 EndSimulation에 초기화하고 Movement Execution이 도착/대기를 갱신한다. Store는 Worker 역할과 함께 제거한다.

## DroneMovementDecision

- **종류/부착 대상/목적:** 수행자의 이번 틱 IComponentData다. Ready/Purpose/Target/Station과 Assignment/AssignmentRevision/Action은 실행할 구간과 판단 버전이다. Failure/ClearFailure는 중단 결과의 생성/정리 의도다.
- **Writer/Reader/수명:** Flight Decision의 병렬 Job만 작성하고 Movement Execution이 소비한다. Return Reservation의 차단은 별도 DroneReturnReservationElement로 전달하며 이 결정을 덮어쓰지 않는다. 원본 이동이나 공개 배정 상태를 대신하지 않으며 다음 Decision이 다시 작성한다.

## DroneReturnReservationElement

- **종류/부착 대상/목적:** 기존 task candidates singleton의 IBufferElementData scratch다. Worker와 PreserveArrival은 이번 틱 정상 귀환이 작업/미평가 경로/접수 요청에 의해 차단됐음을 전달하며 영속 예약이나 작업 상태가 아니다.
- **Writer/Reader/수명:** DroneSchedulingUtility.GetOrCreateCandidates가 최초 부착한다. DroneReturnReservationSystem의 단일 Burst Job이 Task Reservation 뒤 매 틱 Clear/작성하며 Movement Execution이 읽는다. Flight Decision을 덮어쓰지 않고 EndSimulation에 공개할 별도 원본을 만들지 않는다.
- **소비:** PreserveArrival이면 접수된 정거장/행동 요청의 최종 적용까지 현재 이동 상태와 위치를 보존하며 멈춘다. 그 외 차단이면 이동 목적지를 정리하고 Idle로 현재 위치를 유지해 다음 경로 평가/배정에 넘긴다.

## DroneMovementResult

- **종류/부착 대상/목적:** 수행자의 IComponentData다. Assignment/AssignmentRevision/Action에 대한 실제 Arrived/Failure 결과를 유지한다.
- **Writer/Reader/수명:** Movement Execution이 실제 이동 후 작성한다. Movement Apply는 현재 연결/버전/행동과 일치하는 도착 상태만 EndSimulation에 기록한다. 중단 결과는 Task Decision/Reservation/Lifecycle이 기존 예약/화물을 정산하기 전 지우지 않는다. Spawn/Activate에서 초기화하고 Store에서 제거한다.

## DroneRouteEvaluationDecision

- **종류/부착 대상/목적:** 경로 요청 Entity의 이번 틱 내부 IComponentData다. Ready/Result로 공개 DroneRouteEvaluationResult와 수명을 구분한다.
- **Writer/Reader/공개:** Flight Decision이 작성하고 Task Decision이 같은 틱에 우선 읽는다. Task Execution은 유지되는 결과만 EndSimulation에 게시하며 요청 삭제 때 함께 제거한다.
- **에너지 계약:** WaitingForEnergy는 기하 도달 불가와 구분하고 적재품 안전 방출 근거로 쓰지 않는다. 충전 경유 거리와 계획 최대 배터리는 경로 계산에만 쓰며 실제 배터리를 채우지 않는다.

## DroneAutomaticActionRequest

- **종류/부착 대상/목적:** 자동 Producer가 소유하는 요청 Entity의 IComponentData 표식이다. 수행자 역할이나 작업 진행 상태를 복제하지 않는다.
- **Writer/수명:** DroneActionRequestDecisionSystem이 Submit으로 만든 요청에 붙이고 EndSimulation의 결과 공개 뒤 다음 Decision에서 TryConsumeResult로 해당 Entity를 제거한다. 외부 요청에는 붙이지 않고 외부 결과를 소비하지 않는다.
- **흐름:** 공개된 AwaitingCollection/빈 화물/양수 배터리/현재 실제 수집점과 관측을 요구한다. 정상 납품은 공개된 AwaitingDelivery/실제 목적지/관측을 요구하며 0 배터리 인계 예외를 허용한다. 안전 방출도 공개된 AwaitingDelivery/DropCargo에서 실제 셀==DropPosition으로 접수하고 Target은 Entity.Null이다. 이미 도착한 0 배터리 방출을 허용하되 새 목표 선택 자격은 바꾸지 않는다. 같은 배정/revision/행동의 미처리 요청이 있으면 새 자동 요청을 만들지 않는다. 번호 발급과 결과 정산은 기존 소유자를 재사용한다.
- **일반 운반:** 실제 수집량으로 확보 기록을 줄여 미수집분을 반환하고 완료량은 유지한다. 일반 운반 StoreCargo는 실제 보관 성공량만 해당 확보 기록의 Task 완료량에 반영한다. 공간 부족은 양수 배터리의 화물/확보를 유지하며 기다린다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneAutomaticActionRequest.cs), [Producer](../../../Assets/Scripts/Systems/Drones/Decision/DroneActionRequestDecisionSystem.cs), [픽업 계약](../../Specifications/DronePickup.md), [화물 복구 계약](../../Specifications/DroneCargoRecovery.md).

현재 일반 운반 납품은 TryRecordDelivery로 실제 성공량만 DeliveredQuantity로 옮긴다. 잔여 화물은 공간 부족/수령 거부/0 배터리를 구분해 대기하거나 정산/재배정한다. 요청/결과 표식은 픽업/납품이 같은 자동 수행부를 사용하며 역할 상태를 복제하지 않는다. [납품 계약](../../Specifications/DroneDelivery.md)을 따른다.

자동 표식 결과는 공통 DroneActionRequestDecisionSystem이 다음 DroneDecision에 소비한다. 표식 없는 외부 결과는 별도 호출자에게 남기며 결과 소비로 화물/완료량을 다시 정산하지 않는다.

## DroneAutomaticStationRequest

- **종류/부착 대상/목적:** 자동 정거장 요청 Entity의 IComponentData다. 자동 결과 소비의 소유권을 표시하고 DemandTask/Quantity로 해당 호출의 수요 기반 Activate 중재 입력을 전달한다. 수행자 상태나 영속 작업 확보를 복제하지 않는다.
- **Writer/Reader:** DroneStationAutomationDecisionSystem이 기존 Submit으로 만든 요청에 부착한다. DroneStationActivationReservationSystem은 Task Reservation 뒤 남은 작업 수요만 출고 대상으로 중재하고 기존 Station Lifecycle이 전환/결과를 기록한다.
- **수명/공개:** 기존 요청/결정 제거와 결과 공개는 EndSimulation이다. 다음 자동 Decision이 자기 표식의 결과만 TryConsumeResult로 소비한다. 외부 요청/결과에는 이 소유권을 적용하지 않는다.


## DroneChargingConfig

초기화가 한 번 검증/게시하는 ChargePerSecond/PowerPerDrone이다. 유효한 기존 singleton은 보존하고 무효/중복은 공통 Fatal이다. Preparation은 필요량을, Execution은 에너지당 증가량을 계산한다. 위치/현재 배터리는 포함하지 않는다.

## DroneChargingPreparationElement

공통 건물 생성이 DroneStation/MainFacility에 부착하는 이번 틱 버퍼다. DroneChargingPreparationSystem만 Clear/작성하며 Drone/DesiredCharge/RequestedEnergy를 준비한다. BuildingPowerDemandPreparationSystem은 합산 수요를 작성하고 DroneChargingExecutionSystem은 확정 공급으로 정박 배터리/활동을 갱신한다. 원본 정박 관계나 영속 예약을 복제하지 않으며 다음 Preparation이 다시 만든다.

## DroneDemolitionDecisionElement

후보 singleton의 이번 틱 IBufferElementData다. Task Decision만 Clear/작성하고 Lifecycle이 소비한다. Assignment/AssignmentRevision/WorkerObservationRevision을 전달하며 공개된 AwaitingDelivery/DemolishBuilding의 실제 확인 자격이다. 새 건물 삭제 Writer나 행동 진행 상태가 아니다.

## BuildingDemolitionExecutionRequest

Lifecycle이 확인한 root에 EndSimulation으로 붙이는 일회성 건물 실행 요청이다. 대상은 root.Target이고 건물의 PendingBuildingDemolition.Task가 역연결이다. Building Lifecycle만 다음 EndBuilding에 반환/환급/삭제/결과를 기록하고 요청을 제거한다. root의 ExecutionConfirmed는 결과 전 수명을 보존한다.

## BuildingDemolitionAutomaticTaskRequest

기존 DemolishBuildingRequest를 Task 요청으로 변환한 Command의 요청 수명 표식이다. Command는 기존 접수 API로 Sequence를 부여하고 자기 결과만 다음 Command에 소비한다. 외부 요청/결과에는 이 표식을 붙이지 않는다.

## BuildingDemolitionResult

건물 소유자가 실제 처리 뒤 root에 EndBuilding으로 게시하는 Completed/Failed 결과다. Task Decision의 FinishDemolitionTask를 Lifecycle이 읽어 Closed를 반영한다. 확인만 보냈다는 사실이나 대상 소실을 성공으로 만들지 않는다. 결과/참조가 정리된 root와 함께 삭제한다.

철거 입력/중단/단일 배차/DirectToBuilding 경로/확인/취소/반환 회수/결과의 상세 계약은 [철거 명세](../../Specifications/DroneDemolition.md)를 따른다. 이 네 타입은 기존 60개에 추가되어 현재 64개다.
