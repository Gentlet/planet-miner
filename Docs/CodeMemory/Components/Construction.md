# 배치와 공사 컴포넌트

[전체 색인](README.md) · [건물 공통 컴포넌트](Buildings.md)

2026-10-06 건물·드론 도메인 분리 이후의 현재 소스를 설명한다. 공사 ECS 타입은 기존 5개다. 확정 규칙은 [건설·드론 공급 명세](../../Specifications/ConstructionAndDroneSupply.md), 현재 입력·완공 시점의 근거와 한계는 [도메인 분리 검증](../../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/BuildingDroneDomainSplit-Verification.md)을 따른다. [안전 방출·기록 제거 검증](../../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/DroneSafeDropAndJournalRemoval-Verification.md)은 이전 실행 구조의 당시 근거다.

DroneLifecycle 행동 정산·공통 Ownership 인계·Direct 재배정은 유지한다. 방출 목표는 현장 밖만 허용하고 현장 내부의 새 월드 Spawn도 앞단/최종 Apply에서 거부한다. Construction은 현재 월드 Owner/GridPosition과 활성 Destroy만 조회해 차단을 설정·해제한다. 예정 월드 위치 결과 버퍼는 제거했으며 실제 수행부·경로·관측 원본 Writer는 후속이다.

```text
BuildingPlacementRequest + PlacementRequestCandidateElement
  → Command: 배치 검증 → EndCommand: 현장 생성·요청 삭제
CancelConstructionRequest
  → Command: 현장 취소 → EndCommand: 실물 반환·현장/요청 삭제
남은 현장
  → BuildingStateApply 마지막: 완공 판단 → EndBuilding: 성공한 건물 생성·현장/자재 삭제
  → Synchronization: 건물/아이템 공간 상태 등록
```

취소는 Command에서 EndCommand 반환/삭제를 확정한다. Construction은 BuildingStateApply 마지막에서 현재 월드 실물·활성 Destroy와 지난 틱 도착 자재를 조회한다. 건물 종료 후 드론이 실행되므로 이번 틱 드론 납품·방해물 회수는 다음 틱 완공에 반영된다. ConstructionSiteWorldItemUtility는 현재 활성·비취소 현장의 회전 footprint 내부 새 생성/방출을 공통으로 거부한다. 같은 틱 취소/철거와 재배치는 기존 인덱스 점유로 거부한다.

ConstructionSite.Progress와 도착 비율 기획은 제거했다. [Progress 제거](../../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/ConstructionProgressRemoval-Verification.md)·[취소 Command 이동](../../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/ConstructionCancelCommand-Verification.md)은 당시 근거다. 현재 공급·회수 작업과 신호 인계·완공 차단 연결은 구현했으며 실제 드론 수행부는 후속이다.

드론 인계는 EndBuilding에서 확정된 건물 결과를 입력으로 Decision 자격 검사와 Execution 실물/수량/공간 계획을 만든다. Drone StateApply는 계획의 실물을 현재 원본으로 재검사해 수령·도착량·예약을 반영한다. 같은 틱 건물 입고품·건물 출고 공간은 사용할 수 있으나 드론 처리 중 새 수집품·새 공간은 계획에 더하지 않는다. 새 작업·배정·경로는 EndSimulation에 공개하여 다음 틱부터 사용한다. 현재 구조와 검증은 [도메인 분리 검증](../../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/BuildingDroneDomainSplit-Verification.md)을 따른다.

## BuildingPlacementRequest

- **종류·부착 대상·목적:** 일반 `IComponentData`. 별도 배치 요청 엔티티의 헤더로 `Flags`에 묶음 승인 정책, `RequestTick`에 배치 순서 기준 Tick을 담는다. `IRequestComponent`나 enableable 타입으로 선언되어 있지는 않다.
- **생성:** 현재 게임플레이 Producer는 연결되지 않았다. 테스트는 이 컴포넌트와 `PlacementRequestCandidateElement` 버퍼를 같은 엔티티에 생성한다. 실제 배치 시스템의 쿼리도 둘을 모두 요구하므로 헤더만 있는 엔티티는 이 처리 루프의 대상이 아니다.
- **읽기·승인:** Command의 `BuildingPlacementCommandSystem`이 Building/Resource/Item 공간 인덱스의 마지막 Writer 완료를 기다린 뒤 현재 맵을 읽는다. 유틸리티는 크기, 건물/현장 점유, Miner 하부 자원, 해금, 바닥 아이템을 검사한다. StrictAllOrNothing은 후보 하나라도 실패하면 묶음의 유효 후보도 취소하며, AllowPartialPlacement는 유효 후보만 남긴다.
- **처리 범위:** 임시 `claimedCells`는 요청 하나의 후보 묶음 안에서만 공유한다. 여러 요청 엔티티 전체의 영속 선점 저장소가 아니다. 바닥 아이템은 배치를 막지 않고 현장에 AwaitingItemClearance를 설정한다. 동일 Belt 위 Belt 후보는 현장 대신 기존 Belt의 Direction 갱신을 기록한다.
- **순서·반영:** 유효 후보의 현장·요구 버퍼·Stamp 생성과 원래 요청 삭제를 EndCommand ECB에 기록한다. 헤더 Tick이 0이면 시스템 내부 Tick을 사용하되 후보별 Tick이 우선한다. 배치 검증 결과는 처리 중 임시 배열이며 지속 결과 컴포넌트를 게시하지 않는다.
- **수명:** 처리가 시작된 요청은 빈 후보/성공/실패에 관계없이 EndCommand에 삭제된다. 필수 Fence·인덱스가 없거나 맵이 생성되지 않은 경우 시스템이 진행하지 않으므로 그때 요청은 남는다. 재사용·비활성화 과정은 없다.
- **근거:** [정의](../../../Assets/Scripts/Components/Construction/BuildingPlacementRequests.cs), [소비·생성](../../../Assets/Scripts/Systems/Command/BuildingPlacementCommandSystem.cs), [묶음 검증](../../../Assets/Scripts/Common/BuildingPlacementValidationUtility.cs), [테스트 생성](../../../Assets/Editor/Tests/Phase7PlacementCommandTests.cs).

## PlacementRequestCandidateElement

- **종류·부착 대상·목적:** `IBufferElementData`. 배치 요청 엔티티에서 후보별 `TargetType`, 방향 적용 전 `FootprintSize`, 좌하단 `OriginPosition`, `Direction`, 개별 `RequestTick`을 보관한다. 요청 하나에 후보 여러 개를 담을 수 있다.
- **생성:** Producer가 헤더와 함께 채워야 한다. 현재 확인한 직접 생성은 배치 테스트이며 런타임 UI/블루프린트 Producer는 없다. 후보 자체는 현장 또는 건물에 부착하는 버퍼가 아니다.
- **읽기:** Command는 이 버퍼를 임시 `PlacementCandidate` 배열로 변환해 검증한다. 이 변환에는 Tick이 포함되지 않으며, 승인 이후 원본 후보의 RequestTick을 읽어 Stamp를 정한다. 기본 크기의 유효성 및 방향 회전은 배치 검증 유틸리티가 처리한다.
- **후보 순서:** 버퍼의 앞선 유효 후보가 해당 묶음의 점유 셀을 선점한다. 승인 현장의 Stamp.Order는 후보의 원래 인덱스다. 개별 Tick이 양수이면 요청 헤더/시스템 Tick보다 우선하지만 후보 처리 순서를 Tick으로 정렬하지는 않는다.
- **소비·결과:** 유효한 후보는 현장의 BuildingType/Footprint/GridPosition/Direction/ConstructionSite/PlacementStamp 및 두 버퍼로 변환된다. 기존 같은 타입 벨트 덮어쓰기는 방향만 바꾼다. 버퍼는 개별 Remove/Clear로 소비하지 않고 요청 엔티티와 함께 EndCommand에 제거된다.
- **근거:** [정의·변환 연산자](../../../Assets/Scripts/Components/Construction/BuildingPlacementRequests.cs), [후보 해석·Stamp 발급](../../../Assets/Scripts/Systems/Command/BuildingPlacementCommandSystem.cs), [순차 검증](../../../Assets/Scripts/Common/BuildingPlacementValidationUtility.cs), [테스트 후보](../../../Assets/Editor/Tests/Phase7PlacementCommandTests.cs).

## ConstructionSite

- **종류·부착 대상·목적:** 일반 IComponentData. 현장 엔티티에 목표 완공 종류 TargetBuildingType과 AwaitingItemClearance/Cancelled 플래그를 보관한다. 진행도·자재 도착 비율 필드는 없다.
- **생성·결합:** 배치 Command가 승인 후보에 BuildingType(ConstructionSite), Footprint/GridPosition/Direction/PlacementStamp/LocalTransform, 자재 요구 버퍼와 StoredItemElement를 추가한다. 생성자는 목표 타입과 선택적인 flags를 받으며 바닥 아이템이 있으면 정리 대기 플래그로 시작한다. EndCommand에서 실체화되며 Storage는 붙이지 않는다.
- **Writer·Reader:** Cancel Job은 Cancelled로 중복 반환을 막는다. Completion Job은 남은 현장의 회전 footprint와 현재 Owner=Null/GridPosition을 비교해 AwaitingItemClearance를 매 틱 설정·해제한다. 활성 Destroy는 제외한다. 새 월드 생성/드론 방출은 현장 밖만 허용하므로 예정 위치 버퍼를 읽지 않는다.
- **완공 조건:** 취소 현장은 제외한다. 월드 아이템이 footprint에 있으면 차단 플래그를 설정하고 완공을 보류한다. 없으면 플래그를 해제하고 목표 타입과 모든 IsSatisfied를 검사한다. 요구 버퍼가 비어 있으면 자재 검사는 통과하며 진행도 조건은 없다. 지난 틱 드론이 마지막 실물을 회수했다면 이번 틱에 완공할 수 있다. 이번 드론 회수는 다음 틱에 판정하며 건물 처리 중 활성 Destroy는 현재 검사에서 제외한다.
- **완공·실패:** 공통 Spawn으로 새 건물을 만들고 non-Null을 얻은 경우에만 보관 실물과 현장 삭제를 EndBuilding에 기록한다. 생성 실패 시 현장과 자재를 보존한다. 기존 현장의 컴포넌트를 교체하는 방식은 아니다.
- **수명:** 취소 시 보관 실물을 현장 위치의 월드 아이템으로 반환하며 반환과 현장 삭제는 EndCommand에서 확정한다. 완공의 자재/현장 삭제는 EndBuilding, 공간 인덱스 반영은 Synchronization이다. 미충족 현장은 남아 다음 틱의 완공 검사를 받는다.
- **근거:** [정의](../../../Assets/Scripts/Components/Construction/ConstructionComponents.cs), [배치](../../../Assets/Scripts/Systems/Command/BuildingPlacementCommandSystem.cs), [취소](../../../Assets/Scripts/Systems/Command/ConstructionCancelCommandSystem.cs), [완공](../../../Assets/Scripts/Systems/Construction/StateApply/ConstructionLifecycleApplySystem.cs), [직접 자재 준비와 완공 테스트](../../../Assets/Editor/Tests/Phase7ConstructionCompletionTests.cs).

## ConstructionMaterialRequirementElement

- **종류·부착 대상·목적:** IBufferElementData. 현장에 품목별 RequiredQuantity, DeliveredQuantity, ReservedQuantity 데이터를 보관한다. 실물 참조는 별도 StoredItemElement 버퍼에 있다.
- **생성:** 배치 Command가 BuildingConstructionMaterialElement에서 요구량을 복사하고 도착/예약량을 0으로 초기화한다. 건물 설정이 없으면 빈 버퍼를 만든다.
- **현재 계산:** RemainingRequired/RemainingToReserve/IsSatisfied/IsFullyReserved 속성은 남아 있다. 실제 완공 Job은 IsSatisfied를 읽는다. 예약량만으로 완공하지 않는다.
- **현재 Writer 경계:** 기존 운송의 등록·수령 Writer는 제거했다. Reservation은 공통 Utility로 일반 예약 해제·확보를, StateApply Publish는 최종 공개 실패 롤백을 처리한다. DroneTaskLifecycleApplySystem은 공통 실물 API의 성공 수량으로 수집 부족분의 예약 해제와 현장 DeliveredQuantity·예약 감소를 처리한다. Decision은 ProjectedRemaining을 읽기 계산하며 이 버퍼를 변경하지 않는다. 합계에는 개별 예약과 공개 대기 기록의 CommittedQuantity가 포함된다. [드론 예약·후보·공개 대기 기록](DroneLogistics.md)를 따른다.
- **테스트·결합:** Phase7 완공/배치 통합 테스트의 일부는 보관 실물, ItemOwnership 및 도착량을 직접 준비한다. DroneLifecycleIntegrationTests의 실제 공급→다음 틱 완공 흐름은 외부 수행자·관측·경로 결과·행동 신호를 준비하고 제품 배정/인계 시스템으로 도착량을 반영한다. 직접 준비와 제품 인계 경로를 구분하며, 어느 쪽도 실제 드론 이동·행동 Producer가 자동으로 자재를 운송한다는 증거가 아니다.
- **수명:** 요구 버퍼는 유지하고 현장 삭제 때 함께 제거한다. 완공의 실물 소비는 EndBuilding에서, 취소의 실물 반환은 EndCommand에서 확정된다.
- **근거:** [정의](../../../Assets/Scripts/Components/Construction/ConstructionComponents.cs), [초기 요구량 복사](../../../Assets/Scripts/Common/BuildingConfigLookupUtility.cs), [완공 Reader](../../../Assets/Scripts/Systems/Construction/StateApply/ConstructionLifecycleApplySystem.cs), [직접 준비 사례](../../../Assets/Editor/Tests/Phase7EndToEndConstructionPipelineTests.cs).

## CancelConstructionRequest

- **종류·부착 대상·목적:** 일반 IComponentData이자 IRequestComponent. 별도 요청 엔티티의 TargetSite로 진행 중 현장 취소를 요청한다. 완공 건물 철거 요청과 구분한다.
- **생성:** 현재 게임플레이 입력/UI Producer는 없으며 테스트가 직접 요청을 만든다. Consumer는 Command의 ConstructionCancelCommandSystem이다. 소비 시스템 실행 전에 실체화된 요청은 현재 Command에서 처리하며 이후 생성된 요청은 다음 Command에서 대상 유효성을 재검증한다.
- **처리 순서:** 단일 워커 Job이 Null/현장 부재/이미 Cancelled이면 요청만 소비한다. 유효하면 Cancelled를 직접 설정해 중복 반환을 막으며 EndCommand에서 삭제된 현장은 이후 완공 대상에 포함되지 않는다. 이미 완공된 건물은 보호한다.
- **실물 반환:** 현장 Stored 버퍼를 읽고 Owner를 WorldItem, 렌더 표시와 GridPosition/LocalTransform을 현장 좌표로 되돌리는 ECB 명령을 기록한다. 활성 Destroy 대상은 반환에서 제외한다. 기존 실물 반환이며 미도착 요구량을 새로 생성해 환급하지 않는다.
- **반영·종료:** 반환과 현장·요청 삭제는 EndCommand에서 확정되고 Synchronization이 공간 인덱스를 갱신한다. 취소 시스템과 배치 시스템 사이에 조기 인덱스 갱신이나 중간 ECB 재생을 넣지 않는다. 같은 틱의 재배치는 기존 점유로 거부하며 갱신 이후 새 요청부터 변경된 점유를 검증한다. 요청은 재사용하지 않으며 거부된 배치는 자동 재시도하지 않는다.
- **기존 운송 제거와 드론 연결:** 기존 공사 운송의 Close Job과 운송 기록은 제거했다. 현장 취소로 대상이 사라지면 DroneTaskDecisionSystem이 무효화 의도를 작성하고 ConstructionSupplyReservationSystem이 남은 개별 예약을 0으로 정산한다. 현장과 요구 버퍼가 남아 있는 경우에만 현장 합계를 해제한다. DroneTaskLifecycleApplySystem은 빈 적재 배정의 종료 상태·수행자 연결을 정리하며, 해당 배정의 적재품이 남으면 배정과 수행자 연결을 유지한 채 Retargeting으로 전환한다.
- **근거:** [정의](../../../Assets/Scripts/Components/Construction/ConstructionRequests.cs), [취소 Command/Job](../../../Assets/Scripts/Systems/Command/ConstructionCancelCommandSystem.cs), [취소 회귀](../../../Assets/Editor/Tests/Phase7ConstructionCancelTests.cs), [실제 그룹의 취소·재배치](../../../Assets/Editor/Tests/Phase7EndToEndConstructionPipelineTests.cs).
