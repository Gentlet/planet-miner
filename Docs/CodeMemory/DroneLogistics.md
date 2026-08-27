# 드론 물류

## 목적과 책임

드론 시스템은 정거장 범위를 네트워크로 묶고, 건설·철거·건물 아이템 이동·월드 아이템 회수 작업을 우선순위로 관리한다. 아이템과 목적지 용량을 예약하고, 배차된 드론을 이동·픽업·전달·귀환·충전·보관하며, 실패 시 예약과 화물을 회수한다.

## 주요 데이터

### 정거장과 활성 드론

- `DroneStation`, `DroneStationNetwork`: 정거장 활동 범위와 계산된 network ID.
- `StoredDrone`, `StoredDroneElement`: 정거장에 보관된 활성 drone entity 관계.
- `ActiveDrone`, `DroneBattery`, `DroneState`: 운반량, 속도, 배터리와 상태 머신.
- `DroneAssignment`: task/reservation, source/destination, return station, network.
- `DroneCargo`: 현재 운반 item type과 수량.

### 작업과 예약

- `DroneTask`: Construction, Demolition, RemoveBuildingItem, InsertBuildingItem, RecoverWorldItem.
- `DroneTaskStatus`: Pending, InProgress, Suspended, Completed, Cancelled.
- `DroneTaskPriority`: Emergency 또는 normal 1~10. 숫자가 작을수록 높다.
- `DroneTaskQuantity`: total/delivered/reserved와 계산된 unreserved quantity.
- 타입별 task data: target building, world item, demolition target.
- `DroneTaskReservation`: task와 destination, item type, quantity.
- `DroneTaskReservedItemElement`: 실제 예약 item entity와 source owner/buffer 종류.
- `DroneItemReservation`: item entity에 붙는 예약 marker.
- `DroneReservedStorageCapacityElement`: destination owner별 예약된 수용량.

## 정거장 네트워크

`DroneStationNetworkSystem`은 매 프레임 `BuildingOccupant`가 된 정거장을 동기화한다.

1. `DroneStationRangeUtility`가 정거장 셀의 chunk와 `activityRangeInChunks`로 inclusive activity bounds를 만든다.
2. 범위를 `ChunkMapSystem` 셀 coverage에 등록하고 사라진/이동한 정거장의 이전 범위를 해제한다.
3. bounds가 겹치는 정거장을 union-find로 병합한다.
4. component 안에서 가장 작은 entity index 기반 값을 network ID로 게시한다.

네트워크는 전력망과 별개다. 작업 source/destination/월드 item이 어느 drone network에 포함되는지는 정거장 coverage와 network ID로 판단한다.

## 작업 생성과 계획

- UI 또는 공사/철거/파괴 시스템이 `DroneTaskCreateRequest`와 타입별 data를 만든다.
- `DroneTaskCommandSystem`이 요청을 실제 task entity로 변환하고 priority, creation order, quantity를 붙인다. 우선순위 변경, 수동 suspend/resume, cancel 요청도 여기서 처리한다.
- `DroneBuildingItemTaskPlanningSystem`은 Construction/Insert/Remove 작업의 target과 network를 검증하고 source/destination을 찾는다.
- insertion/construction은 같은 network의 storage/producer에서 item을 찾고 target capacity를 확인한다.
- removal은 target building의 item을 source로 삼고 같은 network의 수용 가능한 storage를 찾는다.
- source 부족, destination capacity 부족, network 밖 상태는 자동 suspension reason으로 기록하며 조건이 회복되면 재개할 수 있다. 이미 예약된 수량이 있으면 그 운송을 유지하고, 예약이 전혀 없는 작업만 자동 중단한다.
- `DroneDirectTaskPlanningSystem`은 demolition과 world item recovery 대상의 유효성과 목적지를 준비한다.

## 예약

`DroneTaskReservationSystem`과 partial 파일이 reservation의 단일 소유자다.

1. planning system이 `DroneTaskReservationRequest`를 만든다.
2. task의 남은 수량, target, item acceptance, destination capacity를 검증한다.
3. source owner의 `StoredItemElement` 또는 `ProducedItemElement`에서 실제 item entity를 선택한다.
4. item마다 `DroneItemReservation`을 붙이고 destination에는 reserved capacity를 더한다.
5. task의 `reservedQuantity`를 증가시키고 reservation entity를 게시한다.
6. task/owner/item/destination이 무효해지거나 cancel되면 item marker, capacity, quantity를 모두 되돌린다.

다중 드론은 한 task의 서로 다른 reservation을 병렬로 처리할 수 있다. 예약은 수량 숫자만이 아니라 실제 item entity와 목적지 용량을 함께 잠근다.

## 배차, 이동, 전달

1. `DroneDispatchSystem`이 `Stored` 또는 `AwaitingCharge` 드론의 network를 확인하고 예약 작업과 direct 작업을 우선순위/creation order로 비교한다.
2. 배터리가 source -> destination -> 귀환 정거장 경로를 완료할 수 있는지 `DroneDispatchBatteryUtility`로 확인한다. 완충 여부 자체가 아니라 이 전체 경로의 필요량이 배차 기준이며, 부족한 `AwaitingCharge` 드론은 충전을 계속한다.
3. 예약을 claim하고 `DroneAssignment`, `DroneState`를 이동 상태로 갱신한다.
4. `DroneMovementSystem`이 상태별 목표 셀을 향해 직선 이동하며 이동 거리만큼 배터리를 소비한다. 도착 시 PickingUp/Delivering/Demolishing 등의 실행 상태로 전환한다.
5. `DroneCargoTransferSystem`은 reserved item을 `ItemStorageSystem`으로 drone owner에 옮기고, 목적지에 전달한 뒤 reservation/task quantity를 완료한다.
6. `DroneDirectTaskSystem`은 demolition과 world item pickup/delivery를 실행한다.
7. 작업이 끝나면 `DroneReturnRouteUtility`가 적절한 return station을 선택하고 귀환 상태로 전환한다.

## 귀환, 충전, 보관, identity 전환

- `DroneStationStorageSystem`: 귀환 드론을 정거장에 넣는다. 현재 정거장에 공간이 없으면 우선 같은 network, 이후 다른 사용 가능한 정거장으로 reroute하며, 없으면 `AwaitingStorage`가 된다.
- `DroneChargingDemandSystem`: 충전이 필요한 stored drone을 기준으로 정거장 `PowerConsumer.maximumConsumption`을 갱신한다.
- `DroneChargingSystem`: 정거장의 실제 supply ratio와 충전 설정으로 stored drone battery를 채운다. 완충 후 `Stored` 상태가 된다.
- `DroneIdentityConversionSystem`: 정거장에 저장된 `ItemTypeEnum.Drone` item을 active drone identity로 전환한다. 정거장 파괴 시 반대로 active/stored drone을 월드 Drone item으로 복원한다.

정거장의 일반 item 저장과 stored drone은 같은 `Storage.capacity` slot 계산을 공유한다. Drone item의 stack limit과 `StoredDroneElement` 개수도 용량 계산에 포함된다.

정거장 안에서도 `AwaitingCharge` 드론은 화면에 남고 완충되어 `Stored`가 된 드론만 `DisableRendering`으로 숨긴다. 저장된 드론을 배차할 때는 렌더링을 다시 활성화한다.

## 실패와 회수

- 목표 소멸, 배터리 부족, 전달 실패는 `DroneRecoveryRequest`로 전환될 수 있다.
- `DroneRecoverySystem`은 assignment reservation을 해제하고, 실은 화물을 주변 storage에 넣거나 가능한 월드 셀에 떨어뜨린 뒤 귀환시킨다.
- 월드에 떨어진 item은 `DroneWorldItemRecoveryRequestSystem`과 task utility를 통해 별도 RecoverWorldItem 작업이 된다.
- 작업 범위 밖 world item은 자동 suspended 상태가 될 수 있으며 coverage가 생기면 다시 계획된다.
- 정거장 파괴 시 stored drone 복원이 실패하면 `BuildingDestroySystem`이 파괴를 보류한다.

## 다른 시스템과의 의존 관계

- 공간 coverage/건물·item 위치: `ChunkMapSystem`, `ItemTrackingSystem`
- item 소유권: `ItemStorageSystem`
- storage slot과 item acceptance: `StorageCapacityUtility`, crafter recipe buffers
- 공사/철거: construction site와 building destroy request
- 전력: 정거장 charging demand와 공급 비율
- UI: BuildingUI의 insert/remove 요청, ConstructionModeUI의 normal priority

## 수정 시 함께 확인할 영역

- 작업 상태/우선순위: command, planning, scheduling/dispatch, reservation release, automatic suspension을 함께 확인한다.
- reservation 구조: 실제 item marker, task reserved quantity, destination capacity, drone assignment의 네 방향 롤백을 확인한다.
- 이동/배터리: dispatch route feasibility, emergency return, charging demand, station selection, `ActiveDroneTransportTests`와 `DroneChargingPowerTests`를 확인한다.
- 정거장 범위: `ChunkMapSystem.DroneStations`, network topology, task coverage, station destruction을 확인한다.
- Drone item identity: crafter output, station storage, active conversion, 파괴 복원, `DroneIdentityLifecycleTests`를 확인한다.

## 구현상 주의사항

- UI가 task 상태나 reservation buffer를 직접 수정하지 않는다. 요청 엔티티를 만든다.
- `DroneTaskReservationSystem` 밖에서 `DroneItemReservation`이나 reserved capacity를 임의로 조정하지 않는다.
- 드론은 한 item type만 운반하며 capacity 이내에서 여러 실제 item entity를 운반한다.
- 정거장 network와 power grid를 같은 ID나 같은 연결 규칙으로 취급하지 않는다.
- partial 파일은 planning, reservation, direct task, network, identity의 동일 상태 소유자를 기능별로 나눈 것이다.
- 자동 suspension과 사용자 suspension은 구분된다. 자동 사유가 해소될 때만 자동 재개하며 사용자가 멈춘 작업을 임의로 재개하지 않는다.
