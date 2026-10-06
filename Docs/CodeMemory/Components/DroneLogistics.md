# 드론 작업 관리

[전체 색인](README.md) · [배치와 공사](Construction.md) · [아이템과 저장](ItemsAndStorage.md)

2026-10-06 현재 드론 ECS 타입 22개를 설명한다. 관리 계층의 1~4단계 작업·예약·인계·재배정·안전 방출·완공 차단과 5단계 선별 통합 회귀가 완료 범위다. 세부 역할과 처리 단계는 아래 컴포넌트 계약을 따른다.

[기존 142사례](../../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/DroneSafeDropAndJournalRemoval-Verification.md)와 [신규 통합 4사례](../../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/DroneLifecycleIntegration-Verification.md)는 별도 실행이다. 신규는 정상 취소/철거 3흐름과 실제 완공 후 초기 무예약 적재 배정의 방어 재배정 1흐름이다. 방어 fixture 외 Closed/Retargeting 전이는 시스템이 만들며 정상 복수 예약을 남긴 선완공 사례로 설명하지 않는다.

실제 수행부의 생성·이동·경로 계산·관측 게시·행동 신호와 공통 능력/연구 Writer는 후속이다. DropPosition은 선택 목표이고 실제 위치 원본이 아니다. 경로 결과·관측·행동 신호를 테스트가 준비한 검증은 실제 비행의 근거가 아니다. 다음 추천인 [공통 적재량 초기화 검토안](../../Specifications/DroneCapacityInitializationPlan.md)은 설정 출처·오류·기존 singleton 처리 결정 전의 제안이다.

```text
Command / EndCommand: 외부 배치 요청에 따라 현장과 자재 요구 공개
Decision: 작업 생성·종료 의도 / 경로 생성·유지·삭제 의도 / 유효 결과로 후보 작성
          행동 요청의 식별·배정 자격 판단
Reservation: 미공개·무효 배정 예약 해제 → 작업 선택 → 현장 공급량 예약
Execution OrderFirst: 이번 물류 쓰기 전 실물 ID·수량·슬롯 계획 확정
Execution: 작업 생성 명령에 순번 발급 / 경로 명령 실행 → EndStateApply ECB 기록
StateApply: 저장·라우팅 → 일반 Ownership 요청 적용 → DroneLifecycle 행동 정산
            행동 실물 반영: 동일 ItemOwnership 소유자의 TryTransferItem API
            무효화 의도 재검사·상태/연결 반영 / 배정 최종 재검사·공개 기록
Construction: DroneLifecycle 이후 현재 월드 Owner/GridPosition·Destroy로 차단 갱신·완공
EndStateApply: 상위 작업 / 경로 요청 / 배정 / 개별 예약 / 인계 결과 실체화, 요청 제거·종료 엔티티 삭제
다음 Decision: 이전 EndStateApply에 생성한 상위 작업으로 배정 후보 계산
```

StateApply의 위 흐름은 필요한 부분 순서다. Item Lifecycle과 Building Lifecycle 전체를 하나의 체인으로 강제하지 않는다. 외부 입력은 시뮬레이션 전에 DroneActionRequestUtility.Submit으로 요청을 게시하고, 결과는 EndStateApply 이후 시뮬레이션 외부 또는 다음 틱의 외부 경계에서 TryConsumeResult로 읽고 삭제한다.

BuildingPlacementCommandSystem은 현장·자재 요구 생성까지만 소유하며 드론 작업이나 생성 순번을 참조하지 않는다. Decision은 생성·종료·경로 의도와 배정 후보만 작성하고 예약량·작업 상태·배정 상태·수행자 연결을 변경하거나 ECB에 생성·삭제를 기록하지 않는다. 무효 여부는 상태 반영 전에 읽기 검사로 후보에서 제외한다. Reservation이 해제할 미공개·무효 예약량을 반영한 예상 부족량은 ProjectedRemaining으로 계산하며 실제 예약량 변경은 Reservation에서 수행한다.

DroneTaskExecutionSystem은 의도의 대상 생존·타입·중복 또는 경로 스냅샷을 적용 guard로 확인한다. 작업 필요 여부를 다시 탐색하거나 공급 후보를 새로 고르지는 않는다. Execution 이후 StateApply에서 입출고·소유권·현장 상태가 바뀔 수 있으므로, EndStateApply에 공개된 작업·경로라도 다음 Decision에서 유효성을 다시 확인한다. 그 사이 무효가 된 작업·경로는 배정 후보에서 제외하고 정리 의도를 만든다. Lifecycle Apply는 전달받은 종료 의도를 최종 재검사하며 이미 Closed인 작업의 참조 해제 후 삭제도 담당한다. Assignment Publish의 최종 재검사·예약 롤백·배정 공개 책임은 유지한다.

공급원 재고와 보관 공간은 예약하지 않는다. DroneItemTransferExecutionSystem은 이번 물류 실행 전의 전체 사용 가능 실물 ID와 수량 상한·이전 슬롯 품목/개수를 기록한다. 이 기록이 재고나 공간을 잠그지는 않는다. 일반 Ownership 요청 적용 이후 DroneTaskLifecycleApplySystem의 행동 반영 메서드가 접수 순서로 계획 자격·실물·슬롯을 재검사하고 공통 ItemOwnership API의 실제 성공분만 정산한다. 이번 틱 새 입고품·출고 공간을 계획에 더하지 않는다. 현재 재고·공간이 줄면 실제 인계량도 줄어든다. 기존 벨트 판단·예약·FIFO와 Transfer 요청 경로는 유지한다. 실제 이동·도착 검증과 관측 원본 Writer는 후속이다.

DroneLifecycle은 일반 Ownership 이후, Publish는 OrderLast/EndStateApply 이전이다. Construction은 DroneLifecycle 이후 현재 Owner=Null/GridPosition과 활성 Destroy로만 차단을 판단한다. Item/Building Lifecycle 선행 제약과 예정 위치 기록을 제거했다. 현재 현장 내부의 새 월드 생성은 Decision/최종 Apply가 거부하고 드론 방출도 현장 밖에서만 반영한다. 단일 시스템 파일과 이전 틱 인계 계획은 유지한다.

인계 Decision/Execution은 파생 계획만 쓰며 원본 실물·Owner·예약·배정·행동 번호를 변경하지 않는다. 일반 Ownership 이후 Lifecycle이 계획 안의 행동을 반영한다. Construction은 현재 월드 실물로 AwaitingItemClearance를 설정·해제하고 요구량 충족/공통 Spawn 성공 뒤에만 완공을 확정한다. 기존 월드 실물 유입은 계속 차단하며 마지막 실물 수집/활성 Destroy 후 해제할 수 있다.

공통 실물 API는 ItemOwnershipApplySystem.cs 본체에 함께 구현한다. EffectiveOwner/CanTransferItem은 조회·검사이며 TryTransferItem은 일반 요청 적용 뒤 실제 Owner가 예상 출발지와 일치하는지까지 확인한다. 호출자가 품목·수량·대상·슬롯을 고르고, API가 성공한 실물 하나의 출발 버퍼 제거·도착 버퍼 추가·Owner·격자/시각 위치·벨트 정지와 렌더 ECB를 함께 반영한다. 새 TransferOwnershipRequest를 발행하지 않는다. Lifecycle의 행동 반영은 실물을 직접 쓰지 않고 성공 개수로 배정·적재 출처·현장 도착량/예약과 요청 결과를 정산한다. 각 시스템의 메서드는 해당 본체 파일에 함께 있으며 별도 시스템이나 실행 단계를 만들지 않는다. partial 선언은 Unity Entities 소스 생성에 필요한 문법으로 유지한다.

최초 배정은 수행 가능한 후보에서 **수행자별 공급 대표를 현장 PlacementStamp → 작업 CreationSequence 순서로**, **회수 대표를 CreationSequence 순서로** 고른 뒤, **두 대표의 CreationSequence를 비교**하는 사용자 확정 규칙이다. ConstructionSupplyReservationSystem이 이 순서로 작업을 고른다. 공급원은 드론→공급원→현장, 회수 보관처는 드론→회수품→보관처의 전체 실제 이동거리로 비교하며 거리 동률은 대상 건물 PlacementStamp를 사용한다. 충전 경유를 포함한 거리는 외부 평가자가 제공할 계약이며 이번 시스템에서 계산하지 않는다.

적재품 재배정은 빈 수행자 신규 배정보다 먼저 처리한다. Supply는 필요한 현장→보관처, Recovery는 보관처만 Direct 실제 거리/동률 PlacementStamp로 찾는다. 관련 결과 None은 대기하고 모두 평가해 목적지가 없으면 안전한 방출 위치 선택으로 넘어간다. 현재 관측 셀이 현장 밖이면 그 셀을, 안이면 외부 평가자의 가장 가까운 실제 도달 가능한 현장 외부 셀을 기다린다. 방출 위치 검색의 None/Unreachable은 적재 대기이며 임의의 가까운 셀이나 거리를 만들지 않는다.

Reservation은 기존 예약을 정산한 뒤 적재 그룹을 최초 작업 순서로 선택하며 공급 현장만 예약한다. Publish는 같은 배정 revision을 증가시키고 행동 번호를 0으로 초기화해 새 행동/목적지/DropPosition을 기록한다. 실물·출처·최초 순서·기존 결과 참조는 보존한다. 재배정은 MovingToDestination으로 공개하고 외부 수행부가 목표에 도착한 뒤 행동 신호를 보낸다. 기존 적재 수량에 공통 용량을 소급 적용하지 않는다.

DropCargo의 최종 자격은 배정 DropPosition == 요청 WorldPosition == 현재 관측 위치의 격자 셀이며 현재 현장 외부여야 한다. 선택 목표에 새 현장이 생기면 실물을 방출하지 않고 동일 배정/수행자 연결·revision을 확인해 Retargeting으로 돌린다. 다시 목적지를 선택하며 기존 실물은 유지한다. 실제 관측 원본 Writer와 이동은 후속이므로 이 검사는 선택 목표·신호·관측의 일치 계약을 확인하는 범위다.

관련 세 시스템은 생성 판단·배정/행동 정산·공통 실물 API를 각각 시스템 본체 파일 한 곳에 구현한다. Unity Entities 소스 생성에 필요한 partial 선언은 유지하며 보조 partial 소스 파일로 나누지 않는다. 동작 계약과 ECS 22개 타입은 유지한다. 파일 구조 변경의 실행 근거는 [시스템 파일 통합 검증](../../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/SystemFileConsolidation-Verification.md)을 따른다.

## DroneLogisticsTask

- **종류·부착 대상·목적:** 일반 IComponentData. 독립 상위 작업 엔티티의 헤더다. 공급은 (현장, 품목), 회수는 월드 실물별 작업이다. 여러 하위 배정이 같은 공급 작업을 참조할 수 있다.
- **필드:** Kind는 ConstructionSupply/WorldItemRecovery, State는 Open/Closed다. Target은 현장 또는 회수 실물, ItemType은 품목이다. 양수 CreationSequence는 작업 생성 순서이며 현장의 PlacementStamp와 별개다. RecoveryReason은 SiteClearance/DroneDrop을 구분한다.
- **공급 판단·Producer:** DroneTaskDecisionSystem의 생성 판단 메서드가 유효 현장을 PlacementStamp 순서로 순회하여 RequiredQuantity > DeliveredQuantity인 품목의 생성 의도를 만든다. 같은 품목의 요구 행이 여러 개여도 의도는 하나다. 동일 현장·품목의 살아 있는 Open/Closed 작업이 있으면 생성하지 않는다. DroneTaskExecutionSystem이 의도에 순번을 부여하고 작업 생성을 기록한다. 배치 경로와 무관하게 현재 현장 데이터를 기준으로 판단한다.
- **회수 판단·Producer:** 같은 Decision 시스템은 현재 ECS의 현장 footprint 아래 실물 또는 DroneRecoveryPending이 있는 실물의 생성 의도를 만든다. 현재 GridPosition·월드 소유권·회전 footprint를 사용하고 활성 Destroy를 제외한다. 일반 월드 실물 전체를 자동 회수하지 않는다. Open 회수 작업이 있는 실물에는 중복 생성하지 않는다. Execution이 Open 작업 생성을 기록하고 EndStateApply에 공개하여 다음 시뮬레이션의 Decision부터 후보로 조회한다.
- **Reader·Writer·종료:** DroneTaskDecisionSystem은 소실·취소 현장, 수납·소멸 실물, 현장 밖으로 나간 SiteClearance 대상 등 무효 작업을 읽기 검사로 제외하고 CloseTask 의도를 남긴다. 유효 작업은 후보 생성과 Reservation 우선순위 비교에 사용한다. DroneTaskLifecycleApplySystem이 종료 의도를 최종 재검사하여 Closed로 바꾸고 연결된 배정이 없으면 EndStateApply에 삭제한다. 이전 틱부터 Closed인 작업도 연결 배정이 사라지면 삭제한다. 상위 작업 자체가 공급원 재고나 공간을 잠그지는 않는다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneTaskComponents.cs), [생성 판단](../../../Assets/Scripts/Systems/2_Decision/DroneTaskDecisionSystem.cs), [작업 생성 실행](../../../Assets/Scripts/Systems/4_Execution/DroneTaskExecutionSystem.cs), [수명주기 반영](../../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs), [Decision](../../../Assets/Scripts/Systems/2_Decision/DroneTaskDecisionSystem.cs).

## DroneTaskSequence

- **종류·부착 대상·목적:** 일반 IComponentData. World의 단일 엔티티에서 공급·회수가 공유하는 작업 생성 순서의 원본이다. NextValue는 다음에 발급할 양수 번호다.
- **생성·Writer:** DroneTaskExecutionSystem만 OnCreate에서 DroneTaskCreationUtility.GetOrCreateSequence로 singleton을 확보한다. 새 상태는 1부터 시작한다. OnUpdate에서 값을 읽고 생성 의도를 처리하여 실제 생성 기록을 남긴 공급·회수 작업마다 증가시킨 뒤 저장한다. Decision과 배치 시스템은 이 데이터에 접근하지 않는다.
- **수명:** World 수명 동안 유지하고 종료된 작업의 번호를 재사용하지 않는다. 0은 발급 전에 1로 보정하며 ulong.MaxValue에서는 예외로 중단하여 순환 발급하지 않는다. 작업 엔티티 참조는 EndStateApply에 확정된다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneTaskSequence.cs), [공통 발급·생성](../../../Assets/Scripts/Common/DroneTaskCreationUtility.cs).

## DroneRecoveryPending

- **종류·부착 대상·목적:** 빈 일반 IComponentData. 드론이 보관처를 찾지 못해 월드에 내려놓은 실물의 회수 필요 표시다. 위치·품목·수량을 복제하지 않는다.
- **현재 Reader:** DroneTaskDecisionSystem의 생성 판단 메서드는 월드 실물에 이 표시가 있으면 현장 밖에서도 DroneDrop 회수 생성 의도를 만든다. Execution이 작업을 생성하면서 표시를 제거하지 않으며 Open 작업으로 중복 생성을 막는다.
- **Producer·제거:** DroneTaskLifecycleApplySystem은 DropCargo로 월드에 반환한 기존 실물에 표시 추가를 EndStateApply에 기록한다. RecoverWorldItem이 해당 실물을 실제로 수집하면 표시 제거를 같은 ECB에 기록한다. 실제 드론의 방출 행동 신호 생성은 후속이며 인계 시스템이 임의로 방출 신호를 만들지는 않는다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneRecoveryPending.cs), [회수 판단](../../../Assets/Scripts/Systems/2_Decision/DroneTaskDecisionSystem.cs), [회수 생성 실행](../../../Assets/Scripts/Systems/4_Execution/DroneTaskExecutionSystem.cs), [방출·회수 인계](../../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs).

## DroneTaskCreationDecisionElement

- **종류·부착 대상·목적:** IBufferElementData. DroneTaskCandidateDecisionElement와 같은 공유 singleton에 있는 공급·회수 작업 생성 의도 버퍼다. 작업 필요 여부의 판단과 실제 생성 명령 실행을 분리한다.
- **필드:** Kind는 ConstructionSupply/WorldItemRecovery, Target은 현장/회수 실물, ItemType은 품목, RecoveryReason은 None/SiteClearance/DroneDrop이다. 의도에는 작업 생성 번호나 실제 작업 엔티티가 없다.
- **Producer:** DroneSchedulingUtility.GetOrCreateCandidates가 초기화한다. DroneTaskDecisionSystem의 생성 판단 메서드가 매 틱 Clear한 뒤 공급 필요량·현장 범위·월드 소유권·활성 Destroy·기존 작업을 확인하여 중복 없는 의도를 작성한다. 공급 현장은 PlacementStamp 순서이며 공급 의도 뒤에 회수 의도를 기록한다.
- **Consumer·수명:** DroneTaskExecutionSystem이 순서대로 소비하며 대상 생존·타입과 기존/같은 틱 생성 명령의 중복을 다시 확인한다. 유효 의도에만 생성 번호를 발급하고 EndStateApply ECB에 작업 생성을 기록한 뒤 버퍼를 Clear한다. 의도의 수명은 Decision부터 Execution까지이며 작업 실체화는 EndStateApply다.
- **한계:** Execution 이후 상태 변경까지 의도가 보장하지 않는다. 이후 무효가 된 작업은 다음 Decision에서 배정 대상에서 제외한다. 실제 드론 생성·이동·자재 인계 명령도 아니다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneTaskCreationDecisionElement.cs), [작성](../../../Assets/Scripts/Systems/2_Decision/DroneTaskDecisionSystem.cs), [소비](../../../Assets/Scripts/Systems/4_Execution/DroneTaskExecutionSystem.cs), [초기화](../../../Assets/Scripts/Common/DroneSchedulingUtility.cs).

## DroneTaskInvalidationDecisionElement

- **종류·부착 대상·목적:** IBufferElementData. 후보와 같은 공유 singleton에서 작업·배정이 더 이상 유효하지 않다는 판단 결과를 StateApply로 전달한다. 판단은 Decision 시스템이 수행하며 이 버퍼가 판단 결과를 보관한다. 생성 시 DroneSchedulingUtility.GetOrCreateCandidates가 부착한다. 기존 DroneTaskLifecycleDecisionElement를 이 이름으로 변경했다.
- **필드:** Kind는 DroneTaskInvalidationDecisionKindEnum의 CloseTask/InvalidateAssignment, Target은 상위 작업 또는 하위 배정, AssignmentRevision은 판단한 배정 버전이다. CloseTask는 배정 버전을 사용하지 않는다. 무효 배정은 적재품이 있으면 재배정 상태로 보존하므로 모든 행이 즉시 종료를 뜻하지는 않는다.
- **Producer:** DroneTaskDecisionSystem이 매 틱 비운 뒤 Open 작업의 유효성과 배정의 정리 필요성을 읽기 검사하여 의도를 작성한다. 무효 대상은 같은 Decision의 후보 계산에서도 제외한다. 이때 작업·배정 상태나 수행자 연결은 변경하지 않는다.
- **Consumer·수명:** DroneTaskLifecycleApplySystem이 Target의 생존·컴포넌트, 배정 버전과 현재 무효 여부를 재검사한다. 종료 작업은 Closed로, 무효 배정은 적재가 있으면 Retargeting/없으면 Cancelled로 반영한다. 빈 적재의 수행자 연결은 같은 배정을 가리킬 때만 해제한다. 이후 의도 버퍼를 Clear한다.
- **삭제 보류:** Apply는 미정산 예약과 행동 요청·결과 참조가 남은 배정을 삭제하지 않는다. 보류된 배정은 다음 Decision이 다시 검사한다. 이미 Closed인 상위 작업은 의도 존재 여부와 별도로 연결 배정이 사라진 후 EndStateApply에 삭제한다. 의도 기록만으로 예약 해제나 실물 반환이 일어나지는 않는다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneTaskInvalidationDecisionElement.cs), [작성](../../../Assets/Scripts/Systems/2_Decision/DroneTaskDecisionSystem.cs), [소비·보류](../../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs), [초기화](../../../Assets/Scripts/Common/DroneSchedulingUtility.cs).

## DroneTaskCandidateDecisionElement

- **종류·부착 대상·목적:** IBufferElementData. DroneSchedulingUtility.GetOrCreateCandidates가 공유 singleton에 만드는 배정 후보 및 공개 대기 예약 버퍼다. 후보 부분은 Decision이 작성하는 판단 결과다. 한 행은 특정 수행자가 특정 작업·공급원·목적지·품목·수량 조합을 수행할 수 있다는 제안이며 수행자별로 여러 행이 생길 수 있다. 이후 Reservation과 Publish의 처리 상태도 같은 행에 기록하여 하위 배정 엔티티가 실체화되기 전의 예약 근거를 유지한다.
- **후보 필드:** Worker/Task·Source/Destination·ItemType·RouteRequest는 작업/운반 조합이다. Assignment=Null은 신규, 그 외는 같은 배정의 적재 재배정이다. AssignmentRevision은 이전 버전, NextAction은 공급/보관/방출이다. DropPosition은 방출 후보가 선택한 현장 외부 목표 셀로 실제 위치 원본이 아니다. WorkerObservationRevision은 후보 관측 버전이고 Quantity는 후보/승인 수량이다.
- **후속 단계 필드:** Selected는 Reservation 승인, CommittedQuantity는 이미 현장 합계에 더했지만 개별 배정 예약에는 아직 인계하지 않은 수량이다. Published는 Publish가 배정 생성을 ECB에 기록했음을 뜻하며 실제 엔티티 생성 완료를 뜻하지 않는다. Decision 결과와 공유되는 승인·공개 상태를 구분하여 읽는다.
- **Writer·순서:** Decision이 적재 재배정과 빈 수행자 후보를 작성한다. Reservation은 적재 그룹을 먼저 최초 작업 순서로 선택하고 이후 기존 신규 배정 우선순위를 적용한다. 수행자당 하나만 승인하며 현장 공급 후보에만 잔여량을 예약한다. 회수 실물의 중복 배정도 막는다. 공급원 재고·보관 공간은 예약하지 않는다.
- **공개·소비:** Publish는 최신 수행자·배정 revision·경로·대상·수량을 재검사하고 무효/축소 공급량을 롤백한다. 신규는 배정 생성, 적재 재배정은 기존 배정 revision 증가·행동/목적지 변경을 EndStateApply에 기록한다. 수행자 연결·개별 예약도 함께 공개한다. 다음 Decision은 공개 완료/예약 없는 후보만 제거하며 미공개 CommittedQuantity는 Reservation 정산까지 보존한다. 개별 예약으로 인계한 공개 후보의 수량은 다시 해제하지 않는다.
- **수량 예:** 현장 부족량이 8개이고 공통 적재량이 5개이면 두 수행자에게 각각 최대 5개 후보가 생길 수 있다. Reservation이 첫 후보에 5개를 예약하면 두 번째는 남은 3개만 승인한다. 미공개 예약을 가진 후보를 임의로 Clear하면 예약 해제 근거를 잃으므로 단순한 매 틱 임시 Decision 버퍼로 소비하지 않는다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneTaskCandidateDecisionElement.cs), [Decision](../../../Assets/Scripts/Systems/2_Decision/DroneTaskDecisionSystem.cs), [선택·예약](../../../Assets/Scripts/Systems/3_Reservation/ConstructionSupplyReservationSystem.cs), [공개](../../../Assets/Scripts/Systems/5_StateApply/DroneTaskAssignmentPublishSystem.cs), [검사 유틸리티](../../../Assets/Scripts/Common/DroneSchedulingUtility.cs).

## DroneTaskAssignment

- **종류·부착 대상·목적:** 일반 IComponentData. 상위 작업·현장·수행자와 분리된 하위 작업 엔티티다. 현장 취소 이후에도 적재 실물을 처리할 수 있도록 독립시킨다.
- **필드:** Worker/Task·Source/Destination은 배정 대상이다. OriginalTaskCreationSequence는 최초 순서, Revision/LastAppliedActionSequence는 버전/소비 번호, State/NextAction은 진행/행동이다. ItemType/AssignedQuantity는 배정량이며 실제 적재 원본은 StoredItemElement다. DropPosition은 선택한 방출 도착 목표이고 GridPosition/관측 위치를 대신하지 않는다.
- **Producer·공개:** DroneTaskAssignmentPublishSystem이 유효 후보를 Revision=1, LastAppliedActionSequence=0, MovingToSource로 기록한다. 공급은 CollectFromStorage, 회수는 RecoverWorldItem을 다음 행동으로 둔다. EndStateApply에 수행자 연결과 함께 공개하며 생성만으로 드론이 움직이지 않는다.
- **재배정 공개:** 적재품은 새 배정 엔티티를 만들지 않고 기존 엔티티의 Revision을 증가시킨다. 최초 순서·적재 실물·출처·기존 행동 결과 참조를 보존하고 LastAppliedActionSequence=0, Source=Null, 새 Task/Destination/NextAction·예약 revision을 기록한다. revision이 uint.MaxValue이면 증가하지 않는다. 이전 revision 신호는 공통 인계 검사가 거절한다.
- **공통 적재량 변경:** 빈 수행자의 새 배정 수량은 현재 공통 한도를 넘지 않는다. 이미 공개한 수량·예약·적재와 적재품 재배정량을 공통 한도 변경만으로 줄이지 않는다. 기존 적재 재배정은 현재 실물 수량과 현장 필요량을 사용한다.
- **인계 후 진행:** 수집 양수는 Origin=Supply/Recovery와 다음 공급/보관 행동을 설정하고 MovingToDestination이 된다. 0개는 예약을 해제하고 Cancelled/연결 해제로 끝난다. 공급·보관·방출 후 빈 적재는 Completed/연결 해제, 잔여품은 Retargeting/NextAction=None이다. 다음 Decision이 Direct 경로로 새 목적지를 선택하고 Reservation/Publish가 같은 배정을 갱신한다. 실제 이동·행동 신호 생성은 후속이다.
- **무효화·수명:** Reservation은 무효 대상·종료 상태의 개별 현장 예약을 해제한다. Decision은 무효 배정을 후보 경합에서 제외하고 InvalidateAssignment 의도를 기록한다. DroneTaskLifecycleApplySystem은 의도의 배정 버전과 현재 무효 여부를 재검사하여 적재가 없으면 Cancelled로 바꾸고 같은 배정을 가리키는 수행자 연결을 해제한다. 행동 요청·결과의 참조와 잔여 예약이 없을 때 EndStateApply에 삭제한다. 적재가 있으면 Retargeting으로 보존한다.
- **수집한 회수품의 보관 예외:** 회수 실물이 수행자에 들어오면 원래 월드 회수 작업은 무효·Closed가 될 수 있다. 원래 실물이 수행자 소유이고 Origin=Recovery, NextAction=StoreCargo이며 보관 목적지가 유효하면 보관 이동 배정은 계속 유효하다. 상위 회수 작업이 Closed라는 이유만으로 수집한 실물의 보관을 막지 않는다.
- **식별 검사:** Decision·Execution·StateApply가 DroneItemTransferValidationUtility.TryValidate의 읽기 검사를 공유한다. 배정 Revision과 양수 Action.Sequence, LastAppliedActionSequence·수행자 연결·행동·대상·관측 버전을 대조하며 StateApply에서만 유효 요청의 행동 번호를 반영한다. 수량이 0인 유효 요청도 번호를 소비하므로 같은 번호를 다시 인계하지 않는다. 이 배정 내부 번호는 World ReceiptSequence와 목적이 다르다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneTaskComponents.cs), [공개](../../../Assets/Scripts/Systems/5_StateApply/DroneTaskAssignmentPublishSystem.cs), [인계·진행](../../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs), [회수 보관 유효성](../../../Assets/Scripts/Common/DroneSchedulingUtility.cs), [무효 배정 정리](../../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs).

## ConstructionSupplyReservation

- **종류·부착 대상·목적:** 일반 IComponentData. 하위 배정과 같은 엔티티의 현장 공급량 예약이다. 공급원 재고·보관 슬롯 잠금이 아니다.
- **필드:** Site, ItemType, AssignmentRevision, RemainingQuantity다. 배정 Revision과 예약 Revision은 일치해야 하며 0은 예약 없음/정산 완료다. 회수 배정에도 부착하지만 Site=Null, RemainingQuantity=0이다.
- **생성·합계:** ConstructionSupplyReservationSystem이 ConstructionSupplyReservationUtility.Reserve로 요구 버퍼 ReservedQuantity를 증가시키고 공개 전까지 후보의 CommittedQuantity에 근거를 보관한다. Publish가 EndStateApply에 배정과 개별 기록을 함께 생성한다. 현장 합계에는 공개된 개별 기록과 미공개 승인 후보가 모두 포함된다. Publish의 잔여량 검사에서는 해당 후보 자신의 CommittedQuantity를 다시 더해 자기 예약 때문에 승인량이 줄지 않도록 한다.
- **현재 해제:** Reservation이 신규 예약 전에 미공개 후보와 무효/종료 배정의 잔여량을 해제한다. Publish는 이후 최종 축소·무효 수량만 되돌린다. Decision은 예약 해제 후 예상 부족량을 계산할 뿐 현장 합계와 개별 예약을 수정하지 않는다. 현장이 사라졌으면 버퍼에 접근하지 않고 개별 RemainingQuantity는 0으로 정리한다. 같은 품목의 여러 요구 행은 유틸리티가 순서대로 나누어 예약·해제한다.
- **실물 인계·재배정 정산:** Lifecycle은 부분 수집량만 예약에 남기고 부족분을 해제하며 0개는 모두 해제한다. 현장 공급은 실제 인계량만 도착량에 더하고 해당 배정 잔여 예약을 정리한다. 재배정 전 Reservation이 기존 예약을 0으로 정산하고 새 공급 현장에만 예약한다. Publish는 증가한 배정 revision과 새 예약을 함께 공개한다. 초과 실물은 보존하며 다른 배정의 예약을 해제하지 않는다.
- **근거:** [정의](../../../Assets/Scripts/Components/Construction/ConstructionSupplyReservation.cs), [선택·예약](../../../Assets/Scripts/Systems/3_Reservation/ConstructionSupplyReservationSystem.cs), [예약 유틸리티](../../../Assets/Scripts/Common/ConstructionSupplyReservationUtility.cs), [실물 정산](../../../Assets/Scripts/Common/DroneItemTransferUtility.cs), [인계](../../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs), [공개](../../../Assets/Scripts/Systems/5_StateApply/DroneTaskAssignmentPublishSystem.cs), [현장 요구](../../../Assets/Scripts/Components/Construction/ConstructionComponents.cs).

## DroneWorker

- **종류·부착 대상·목적:** 빈 일반 IComponentData. 드론 작업을 수행할 수 있는 엔티티의 표시다. 수행자별 적재량을 보관하지 않으며 최대 적재량의 원본은 World의 DroneCapacityState다.
- **Producer·Reader:** 실제 생성·등록 Producer는 없다. 등록 데이터가 준비되면 Decision·Reservation·Publish와 Execution의 경로 적용 검사가 이 표시와 관측·활성 배정 연결·적재 출처·StoredItemElement를 확인한다. 공통 적재량이 없거나 0 이하이면 신규 배정을 진행하지 않는다.
- **한계·수명:** 현재 신규 배정과 수집은 빈 적재 수행자만 대상이다. 실제 인계는 한 품목과 이미 배정한 수량 상한을 확인한다. 수행자 자체의 종료 시 적재 반환은 후속이다. 등록된 수행자와 함께 유지하며 타입만 추가한다고 드론이나 버퍼가 생성되지 않는다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneWorkerComponents.cs), [수행자 검사](../../../Assets/Scripts/Common/DroneSchedulingUtility.cs), [보관 버퍼](../../../Assets/Scripts/Components/Storage/StorageComponents.cs).

## DroneCapacityState

- **종류·부착 대상·목적:** 일반 IComponentData. World의 단일 엔티티에 붙는 모든 드론의 공통 최대 적재량 상태다. CarryingCapacity는 한 품목의 최대 실물 개수이며 Storage.SlotCount·품목 MaxStack과 독립적이다. 수행자마다 같은 값을 복제하지 않는다.
- **현재 Reader:** 빈 수행자 신규 후보·Reservation 승인·ViaSource 경로 검사·Publish 공개는 공통 값으로 제한한다. 부재/0 이하면 신규 배정은 대기하고 singleton을 자동 생성하거나 기본값으로 대체하지 않는다. 이미 적재한 수행자의 Direct 경로·재배정 수량에는 이 제한을 소급 적용하지 않는다.
- **후속 Producer·Writer:** 런타임 초기화와 연구 갱신 시스템은 아직 없다. 후속 초기화가 기본 적재량을 게시하고 연구 처리가 공통 값을 갱신할 계약이다. 테스트가 명시적으로 준비하는 singleton은 제품 초기화 구현의 증거가 아니다.
- **변경 적용·수명:** World 수명 동안 공통 값을 유지한다. 연구로 한도가 증가하면 이후 새로 배정하는 작업부터 적용하며 기존 AssignedQuantity·ConstructionSupplyReservation·StoredItemElement를 다시 늘리거나 정산하지 않는다. 후보는 공개 전까지 현재 값을 재검사한다. 실제 수집은 이미 공개한 AssignedQuantity와 해당 예약·현재 현장 잔여량을 상한으로 삼아 공통 값 변경을 소급 적용하지 않는다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneCapacityState.cs), [공통 값·후보 검사](../../../Assets/Scripts/Common/DroneSchedulingUtility.cs), [예약](../../../Assets/Scripts/Systems/3_Reservation/ConstructionSupplyReservationSystem.cs), [경로 적용 검사](../../../Assets/Scripts/Systems/4_Execution/DroneTaskExecutionSystem.cs), [최종 공개](../../../Assets/Scripts/Systems/5_StateApply/DroneTaskAssignmentPublishSystem.cs).

## DroneWorkerObservation

- **종류·부착 대상·목적:** 일반 IComponentData. 외부 수행부가 수행자에 게시할 위치·신규 작업 가능 상태의 스냅샷이다. 관리 측의 배정 상태와 작성 책임을 분리한다.
- **필드·검사:** Position은 출발 월드 좌표, Revision은 양수 관측 버전, CanAcceptTask는 신규 작업 가능 여부다. 후보에는 유한한 위치·양수 버전·작업 가능 상태가 필요하며 경로 결과와 후보 공개 시 요청의 버전·위치와 대조한다.
- **현재 Reader·후속 Writer:** Decision과 예약·공개 검사에서 읽는다. 실제 관측을 게시·갱신하거나 드론을 이동시키는 시스템은 없다. 위치·경로 조건이 바뀌면 수행부가 Revision을 갱신해야 한다.
- **수명:** 수행자 수명 동안 최신 스냅샷을 유지하는 계약이다. GridPosition이나 렌더 위치 원본을 대체하지 않는다.
- **현재 원본 연결 한계:** 관리 측은 Position/Revision을 서로 비교하지만 실제 드론 위치 원본을 요구하거나 관측이 원본에서 갱신되었는지 검사하지 않는다. 위치/관측 Producer가 아직 없어 실제 이동과 관측의 정합성은 구현된 계약이 아니다. 스냅샷과 다른 드론 값의 SoT 구분은 [검토 기록](../../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/DroneSnapshotSotReview.md)을 따른다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneWorkerComponents.cs), [경로·후보 검사](../../../Assets/Scripts/Common/DroneSchedulingUtility.cs).

## DroneWorkerAssignment

- **종류·부착 대상·목적:** 일반 IComponentData. 수행자에서 관리 측이 소유하는 활성 배정 연결이다. Assignment=Null은 배정 없음이다.
- **Writer·순서:** 초기 구성은 후속 등록 측 책임이다. Reservation의 동일 틱 중복 선택 방지와 기존 연결 검사로 수행자당 하나를 고른다. Publish가 EndStateApply에 새 배정과 연결을 함께 기록한다. DroneTaskLifecycleApplySystem은 검증한 배정의 0개 수집 또는 인계 후 빈 적재 완료 시 연결을 해제한다. DroneTaskLifecycleApplySystem은 무효/종료된 빈 적재 배정을 정리할 때 같은 배정을 가리키는 연결만 해제한다.
- **Reader·수명:** Decision·Reservation·Publish는 신규 배정 가능 여부를 읽고 후속 수행부는 작업 조회 시작점으로 쓴다. 수행자에 유지하며 Null 변경만으로 적재품이나 회수 출처를 지우지 않는다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneWorkerComponents.cs), [공개](../../../Assets/Scripts/Systems/5_StateApply/DroneTaskAssignmentPublishSystem.cs), [인계 후 해제](../../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs), [무효화](../../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs).

## DroneCargoState

- **종류·부착 대상·목적:** 일반 IComponentData. 수행자의 적재 출처 제약이다. Origin은 None/Supply/Recovery이며 실제 목록·품목·수량은 StoredItemElement가 원본이다.
- **현재 Reader:** 신규 배정 검사에서 Origin=None 및 빈 보관 버퍼를 요구한다. 적재가 남은 수행자에게 신규 작업을 중복 배정하지 않는다.
- **Writer·제약:** DroneTaskLifecycleApplySystem이 수집 성공 시 Supply 또는 Recovery로 설정하고 인계 후 빈 적재이면 None으로 비운다. Recovery는 보관 건물에 넣기 전 현장에 직접 공급할 수 없도록 SupplyConstructionSite에서 Origin=Supply를 요구한다. 상위 작업 종료나 Retargeting만으로 출처를 지우지 않는다. 적재품 목적지 재탐색은 Direct 경로로 연결했고 실제 이동은 후속이다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneWorkerComponents.cs), [빈 수행자 검사](../../../Assets/Scripts/Common/DroneSchedulingUtility.cs), [출처 반영·공급 검사](../../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs).

## DroneRouteEvaluationRequest

- **종류·부착 대상·목적:** 일반 IRequestComponent. 독립 요청 엔티티의 경로 평가 입력이다. 실제 비행·경로 계산을 실행하는 데이터가 아니다.
- **필드:** Worker/관측 revision·Assignment/배정 revision·EvaluationRevision과 Kind/출발·공급원·목적지 위치를 가진다. Direct의 IsDropPositionSearch=true는 건물 목적지 평가가 아니라 현장 외부 방출 셀 검색이다. 이때 Source/Destination은 Null이고 원점 관측과 배정 버전을 고정한다.
- **현재 Producer:** Decision이 배정 전 ViaSource 입력을 DroneRouteDecisionElement의 Create 명령에 기록하고, DroneTaskExecutionSystem이 현재 대상·관측 스냅샷을 적용 guard로 확인한 뒤 EndStateApply에 요청을 공개한다. Assignment=Null, AssignmentRevision=0이며 공급은 수행자→공급원→현장, 회수는 수행자→실물→보관처다. 같은 수행자·공급원·목적지의 현재 유효 요청을 재사용하고 같은 틱 중복 의도도 막는다.
- **대기·Consumer·삭제:** 외부 평가자는 아직 없다. 필요한 경로의 결과 대기 중 요청을 유지한다. Decision과 후보 재검사는 관측·대상 위치·생존·철거 예정 상태를 대조한다. Decision은 무효 스냅샷·잘못된 결과를 제외하고 기존 요청마다 Retain 또는 Remove 명령을 기록한다. Execution은 명시된 Remove만 EndStateApply에 엔티티째 삭제하고 Retain에는 ECS/ECB 변경을 하지 않는다. 의도 버퍼에 없는 요청을 임의로 삭제하지 않는다. 유효 결과는 현재 관측에서 재사용되며 배정 후 수행자가 더 이상 빈 수행자가 아니면 다음 틱의 Decision·Execution을 거쳐 정리된다.
- **적재 재배정 Direct 경로:** Decision이 Assignment/AssignmentRevision과 현재 관측→목적지 Direct 요청을 작성한다. Source=Null이며 현재 관측 버전/위치·배정 revision·대상 생존/위치·철거 예정 상태로 재검사한다. 관련 결과 None은 대기하고 Reachable의 실제 거리/동률 PlacementStamp를 비교한다. 실제 경로 계산·충전 경유 Writer는 후속이다.
- **방출 셀 검색:** 현재 셀이 현장 내부일 때 Direct/IsDropPositionSearch 요청으로 외부 평가자에게 가장 가까운 실제 도달 가능한 현장 외부 셀을 요청한다. 관리층은 검색 범위·기본 거리·대체 좌표를 만들지 않는다. None/Unreachable이면 적재 대기하고 Reachable/HasDropPosition 결과의 현재 현장 외부 조건을 다시 확인한다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneRouteContracts.cs), [생성·유지 판단](../../../Assets/Scripts/Systems/2_Decision/DroneTaskDecisionSystem.cs), [요청 생성·정리](../../../Assets/Scripts/Systems/4_Execution/DroneTaskExecutionSystem.cs), [스냅샷 검사](../../../Assets/Scripts/Common/DroneSchedulingUtility.cs).

## DroneRouteDecisionElement

- **종류·부착 대상·목적:** IBufferElementData. DroneTaskCandidateDecisionElement와 같은 singleton에 있는 한 틱의 경로 생성·유지·삭제 명령 버퍼다. 경로 요청 엔티티의 실제 생성·삭제와 Decision의 판단을 분리한다.
- **필드:** Kind는 Create/Retain/Remove다. Create는 Request에 새 DroneRouteEvaluationRequest 입력을 담고, Retain/Remove는 ExistingRequest로 기존 요청을 지정한다. Kind 없이 ExistingRequest의 Null 여부만으로 동작을 추론하지 않는다.
- **Producer·Reader·소비:** DroneSchedulingUtility.GetOrCreateCandidates가 시스템 초기화 시 버퍼를 구성한다. Decision은 매 틱 비운 뒤 경로 생성 명령과 기존 요청별 유지/삭제 명령을 작성한다. DroneTaskExecutionSystem은 Create의 최신 관측·대상 스냅샷을 확인하여 생성하고 Remove의 기존 요청을 삭제하도록 EndStateApply ECB에 기록한다. Retain은 변경 없이 유지하고 소비 후 버퍼를 Clear한다. 의도에 없는 기존 요청을 찾아 삭제하는 추가 판단은 하지 않는다.
- **수명·한계:** Decision에서 Execution까지 유지하는 임시 판단 데이터다. 의도를 기록한 순간 요청 엔티티가 생기는 것은 아니며 외부 평가자는 EndStateApply 이후에 공개된 요청을 읽는다. Execution 뒤에 변경된 대상은 다음 Decision에서 재검사한다. 거리 계산·이동 실행 결과도 아니다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneRouteDecisionElement.cs), [작성](../../../Assets/Scripts/Systems/2_Decision/DroneTaskDecisionSystem.cs), [실행·소비](../../../Assets/Scripts/Systems/4_Execution/DroneTaskExecutionSystem.cs), [초기화](../../../Assets/Scripts/Common/DroneSchedulingUtility.cs).

## DroneRouteEvaluationResult

- **종류·부착 대상·목적:** 일반 IComponentData. 경로 요청과 같은 엔티티에 외부 평가자가 게시할 결과다. 배정 승인·실물 인계 성공이 아니다.
- **필드·유효성:** EvaluationRevision은 요청 버전과 같고 Status는 None/Reachable/Unreachable이다. Reachable의 TotalDistance는 유한한 실제 거리다. 방출 검색 성공은 HasDropPosition=true와 DropPosition을 명시해야 하며 기본 0 좌표를 성공으로 해석하지 않는다. 목표 좌표는 실제 드론 위치가 아니고 None/Unreachable은 방출 대기다.
- **현재 Consumer:** Decision이 유효 결과를 후보 비교에 사용하고 Reservation·Publish도 재검사한다. 현재 재고·공간 조건을 만족한 관련 공급원/보관처 중 평가를 기다리는 것이 있으면 해당 작업 후보를 대기시킨다. Unreachable은 후보에서 제외한다.
- **Producer·수명:** 실제 거리 계산과 결과 게시 Producer는 아직 없다. 요청 유효기간 동안 재사용하며 무효화되면 같은 엔티티로 삭제한다. 테스트용 결과 입력과 실제 거리 계산 구현은 구분해야 한다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneRouteContracts.cs), [결과 검사](../../../Assets/Scripts/Common/DroneSchedulingUtility.cs), [후보 생성](../../../Assets/Scripts/Systems/2_Decision/DroneTaskDecisionSystem.cs).

## DroneActionReadyRequest

- **종류·부착 대상·목적:** 일반 IRequestComponent. 외부 수행부가 도착·행동 완료 때 독립 엔티티에 게시할 실물 인계 시도 신호다. 외부에서 성공 수량을 확정하지 않는다. 실제 수행부의 행동 신호 생성은 후속이며 제출 API와 내부 Consumer는 연결했다.
- **필드·계약:** ReceiptSequence는 World의 접수 순서다. Action은 배정·버전·행동 번호·수행자·행동·대상의 DroneActionIdentity다. WorkerObservationRevision은 관측 버전, WorldPosition은 DropCargo 격자 위치다. CollectFromStorage, RecoverWorldItem, SupplyConstructionSite, StoreCargo, DropCargo를 구분한다.
- **Producer·접수:** 시뮬레이션 전 주 스레드의 외부 입력 경계에서 DroneActionRequestUtility.Submit이 World 접수 순번을 발급하고 새 요청 엔티티를 즉시 게시한다. 전달된 ReceiptSequence는 발급값으로 덮어쓰며 DroneItemTransferDecision과 실물·슬롯 계획 버퍼도 부착한다. 실물·예약을 바꾸거나 행동 성공을 만들지는 않는다.
- **Decision·Execution:** DroneItemTransferDecisionSystem은 요청의 초기 자격만 기록한다. Execution의 OrderFirst 인계 계획 시스템은 이번 물류 쓰기 전의 실물 전체 ID·요구량/수량 상한·기존 슬롯 품목/개수를 확정한다. 이번 틱 수집 이후에만 가능한 Supply 신호를 미리 함께 넣으면 Decision 시점의 NextAction 불일치로 거절한다. 공급하려면 다음 틱에 새 행동 신호를 제출해야 한다.
- **StateApply 검사:** DroneTaskLifecycleApplySystem은 접수 순서로 초기 자격·계획 준비 여부와 공통 TryValidate 검사를 다시 확인한다. 계획 안의 실물만 현재 버퍼·생존·품목·유효 Transfer 소유권·Destroy·현장 footprint로 검사한다. 이번 틱 새 입고품·새 수집 적재품과 출고로 생긴 새 여유는 사용할 수 없다. 일반 행동의 실제 이동·도착 검증과 관측 원본 Writer는 후속이다.
- **수명·결과 공개:** 처리한 요청과 세 판단/계획 컴포넌트 제거, 같은 엔티티의 DroneItemTransferResult 추가를 EndStateApply에 기록한다. 내부 pending 집합으로 ECB 재생 전 시스템 반복 호출의 중복 적용을 막는다. Lifecycle은 남은 요청 참조가 있는 배정의 삭제를 보류한다. 미소비 결과가 있는 엔티티에 새 요청을 부착해도 기존 결과를 덮어쓰거나 다시 인계하지 않는다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneActionContracts.cs), [접수·외부 소비 API](../../../Assets/Scripts/Common/DroneActionRequestUtility.cs), [초기 자격](../../../Assets/Scripts/Systems/2_Decision/DroneItemTransferDecisionSystem.cs), [계획 준비](../../../Assets/Scripts/Systems/4_Execution/DroneItemTransferExecutionSystem.cs), [공통 자격 검사](../../../Assets/Scripts/Common/DroneItemTransferValidationUtility.cs), [인계·결과](../../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs), [유효 소유권](../../../Assets/Scripts/Common/DroneItemTransferUtility.cs), [삭제 보류](../../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs).

| 행동 | 실제 실물·수량 처리 |
| --- | --- |
| CollectFromStorage | 계획에 포함된 기존 공급품만 현재 버퍼에서 수행자 버퍼로 옮긴다. 계획 상한·자기 예약·현재 현장 잔여량을 넘지 않으며 이번 틱 새 입고품은 제외한다. 부족분 예약은 해제한다. |
| RecoverWorldItem | 계획에 포함된 원실물 하나만 현재 월드 소유권과 SiteClearance의 최신 현장 footprint로 재검사한다. 성공하면 수행자에 넣고 DroneRecoveryPending 제거를 기록한다. 출처는 Recovery다. |
| SupplyConstructionSite | 물류 쓰기 전부터 적재되어 계획에 포함된 Origin=Supply 실물만 현장에 넣는다. 실제 도착량을 더하고 해당 예약을 정리한다. 이번 틱 새 수집품은 다음 틱 공급하며 잔여품은 Retargeting으로 보존한다. |
| StoreCargo | 계획 실물·기존 슬롯 예산 안에서 DroneSchedulingUtility.CanStoreInSlot의 현재 필터·슬롯·MaxStack 조건까지 만족하는 실물만 보관한다. 실제 성공분만 같은 목적지의 공유 예산에 더하고 이번 틱 출고로 열린 새 공간은 쓰지 않는다. 사전·영속 공간 예약은 없다. |
| DropCargo | 계획 실물만 선택한 DropPosition·요청 위치·현재 관측 셀의 일치와 현장 외부 조건을 확인해 반환한다. 새 현장이 목표를 막으면 Retargeting으로 돌리고 실물을 유지한다. 성공분의 회수 표시/렌더 변경은 EndStateApply에 기록하며 새 아이템/Transfer 요청은 만들지 않는다. |

## DroneItemTransferDecision

- **종류·부착 대상·목적:** 일반 IComponentData. 행동 요청 엔티티의 한 틱 인계 자격과 수량 계획이다. 실물·예약·배정 원본을 복제한 상태가 아니라 현재 입력에서 계산한 파생 결과다. Submit이 기본값으로 부착한다.
- **필드·Writer:** CanExecute는 Decision의 공통 자격 검사 결과, Prepared는 Execution의 계획 확정 여부다. WantedQuantity는 행동이 처리하려던 원래 수량이고 MaximumQuantity는 이번 물류 쓰기 전 입력으로 허용한 상한이다. Decision은 CanExecute만 작성하고 나머지는 초기화하며 Execution이 계획값을 채운다.
- **Consumer·수명:** StateApply는 CanExecute/Prepared와 현재 자격을 다시 검사하고 MaximumQuantity를 넘지 않게 계획 실물을 처리한다. 자격·상태가 잘못되면 Rejected다. 자격은 유효하지만 기존 재고/공간이 0이면 CanExecute=true/MaximumQuantity=0으로 유지하여 Unavailable 결과와 정상 0개 정산을 적용한다. EndStateApply에 제거한다.
- **재진입·쓰기 경계:** Prepared가 true이면 같은 Execution 재호출에서 새 재고·공간을 계획에 더하지 않는다. Decision·Execution은 아이템·Owner·원본 보관 버퍼·현장 예약·배정·행동 번호를 쓰지 않는다. 실제 쓰기는 StateApply만 수행한다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneItemTransferDecisions.cs), [입력 부착](../../../Assets/Scripts/Common/DroneActionRequestUtility.cs), [Decision](../../../Assets/Scripts/Systems/2_Decision/DroneItemTransferDecisionSystem.cs), [Execution](../../../Assets/Scripts/Systems/4_Execution/DroneItemTransferExecutionSystem.cs), [반영·제거](../../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs).

## DroneItemTransferItemDecisionElement

- **종류·부착 대상·목적:** IBufferElementData. 행동 요청에 붙는 물류 실행 전 사용 가능 실물 ID 목록이다. ItemEntity만 저장하고 소유권·품목·위치의 원본을 대신하지 않는다. Submit이 빈 버퍼를 부착한다.
- **Producer·Consumer:** Execution이 수집·공급·보관·방출의 기존 실물 전체를 중복 없이 기록하고 회수는 원실물 하나를 기록한다. 후보를 미리 수량 상한만큼 잘라내지 않으므로 선행 드론이 일부를 가져가도 다른 기존 실물을 선택할 수 있다. StateApply는 목록 안의 ID만 현재 버퍼·품목·생존·유효 소유권·Destroy로 재검사한다.
- **수명·제약:** 이번 틱 입고·새 수집으로 생긴 적재품을 나중에 추가하지 않는다. 여러 요청이 같은 ID를 후보로 가질 수 있지만 접수 순서의 실제 인계 뒤 현재 원본을 재검사하므로 같은 실물을 중복 사용하지 않는다. 공급원 재고 예약이나 영속 캐시는 아니며 EndStateApply에 제거한다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneItemTransferDecisions.cs), [전체 후보 수집](../../../Assets/Scripts/Systems/4_Execution/DroneItemTransferExecutionSystem.cs), [ID·원본 재검사](../../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs), [실물 검사](../../../Assets/Scripts/Common/DroneItemTransferUtility.cs).

## DroneItemTransferSlotDecisionElement

- **종류·부착 대상·목적:** IBufferElementData. 보관 행동 요청이 물류 실행 전에 읽은 슬롯의 파생 입력이다. SlotIndex·ItemType·ItemCount를 저장하며 원본 Storage/StoredItemElement를 대신하거나 보관 공간을 잠그지 않는다. Submit이 빈 버퍼를 부착한다.
- **Producer·예산:** Execution이 기존 슬롯의 품목·개수와 현재 필터·입력 슬롯·MaxStack에서 가능한 수량을 계산한다. StateApply는 유효한 양수 보관 계획의 목록으로 목적지별 한 틱 공유 예산을 만들고 실제 성공분만 ItemType/ItemCount에 반영한다. 실패한 요청이 공간을 차감하거나 0개 계획이 빈 예산으로 다른 요청을 가로막지 않는다.
- **최종 검사·수명:** 현재 CanStoreInSlot 검사와 기존 품목/개수 예산을 모두 만족하는 슬롯만 사용한다. 이번 틱 출고로 늘어난 여유는 기존 예산에 더하지 않으며 선행 입고로 줄어든 공간은 현재 검사에서 제한한다. 영속 공간 예약은 없고 공유 예산은 한 OnUpdate 안에서만 사용한다. 요청 버퍼는 EndStateApply에 제거한다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneItemTransferDecisions.cs), [슬롯 입력 계산](../../../Assets/Scripts/Systems/4_Execution/DroneItemTransferExecutionSystem.cs), [성공분 공유·반영](../../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs), [공통 현재 슬롯 검사](../../../Assets/Scripts/Common/DroneSchedulingUtility.cs).

## DroneActionReceiptSequence

- **종류·부착 대상·목적:** 일반 IComponentData. World의 단일 엔티티가 모든 드론 행동 신호의 접수 순서 원본을 소유한다. 먼저 접수된 유효 신호부터 이전 틱 입력의 계획을 현재 원본으로 재검사해 인계하는 기준이다. 작업 생성 순번이나 배정 내부 Action.Sequence와 별개다.
- **필드·Producer:** NextValue는 다음 양수 접수 번호다. DroneActionRequestUtility.Submit의 입력 경계만 singleton을 생성·초기화하고 번호를 발급한다. 처음 값은 1이며 제출마다 증가한다. 0 또는 ulong.MaxValue이면 예외로 거부하여 순환하거나 번호를 재사용하지 않는다.
- **Reader·수명:** DroneTaskLifecycleApplySystem은 요청에 복사된 ReceiptSequence를 정렬 기준으로 사용한다. 같은 값이면 요청 엔티티 Index·Version으로 순서를 정하지만 정상 제출 API는 서로 다른 번호를 발급한다. singleton은 World 수명 동안 유지하며 요청·결과 소비 후에도 과거 번호를 다시 발급하지 않는다.
- **입력 계약:** 실제 수행부는 Submit으로 요청을 게시해야 한다. 엔티티를 직접 만들고 ReceiptSequence=0으로 두면 인계 검사가 거절한다. 접수 순번만 유효하다고 행동이나 대상이 유효해지는 것은 아니다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneActionReceiptSequence.cs), [유일한 접수 Writer](../../../Assets/Scripts/Common/DroneActionRequestUtility.cs), [순서 적용](../../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs).

## DroneItemTransferResult

- **종류·부착 대상·목적:** 일반 IComponentData. 내부 인계 Apply가 실제 옮긴 수량을 기록하는 결과다. 수행부의 행동 완료와 구분하며 처리한 행동 요청과 같은 엔티티에 남긴다.
- **필드:** Action은 원래 식별자, Status는 None/Completed/Partial/Unavailable/Rejected, MovedQuantity는 인계한 아이템 엔티티 수다. Completed/Partial은 양수, Unavailable/Rejected는 0, None은 미결정이다.
- **Producer·성공 수량:** DroneTaskLifecycleApplySystem이 초기 자격·계획 준비 여부와 현재 행동 식별자를 검사하고 ItemOwnershipApplySystem.TryTransferItem의 성공 개수로 작성한다. 자격·상태·계획 검사가 실패하면 Rejected, 유효한 기존 입력이 0이거나 최종 검사에서 옮기지 못하면 Unavailable, WantedQuantity 전량이면 Completed, 일부이면 Partial이다. 공통 API가 버퍼·Owner·위치·벨트·렌더를 반영하고 Lifecycle은 배정·도착량/예약을 정산한다. 새 Transfer 요청을 만들지 않는다.
- **Consumer·수명:** EndStateApply 이후 시뮬레이션 외부 또는 다음 틱의 외부 경계에서 DroneActionRequestUtility.TryConsumeResult가 완료 상태를 읽고 결과 엔티티를 즉시 삭제한다. 요청이 아직 있거나 결과가 없거나 None이면 소비하지 않는다. Lifecycle은 결과의 배정 참조가 남아 있으면 삭제를 보류한다. 외부 Consumer를 자동 호출하는 수행부는 아직 없다.
- **정산 경계·한계:** 외부 소비는 이미 반영한 수량/예약을 다시 정산하지 않는다. 결과가 남아 있어도 적재 재배정은 같은 엔티티와 원래 결과 참조를 보존한 채 새 revision으로 진행할 수 있다. 실제 비행·충전·도착은 외부 수행부 연결 전까지 검증된 것으로 설명하지 않는다.
- **근거:** [정의](../../../Assets/Scripts/Components/DroneLogistics/DroneActionContracts.cs), [행동 정산·결과 Writer](../../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs), [실물 반영 API](../../../Assets/Scripts/Systems/5_StateApply/ItemOwnershipApplySystem.cs), [외부 소비](../../../Assets/Scripts/Common/DroneActionRequestUtility.cs), [배정 참조 검사](../../../Assets/Scripts/Systems/5_StateApply/DroneTaskLifecycleApplySystem.cs).

DroneActionIdentity는 요청/결과 안의 일반 구조체다. 인계가 revision/행동 번호·대상 일치를 검사하며 재배정은 revision 증가와 번호 초기화로 이전 신호를 구분한다. World ReceiptSequence는 요청 간 접수 순서다. 완공은 현재 월드 실물과 활성 Destroy로 차단을 갱신하며 회수 작업 존재 자체로 차단하지 않는다.
