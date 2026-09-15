# 건물 배치와 수명주기

## 목적과 책임

이 영역은 사용자의 건설 모드 입력을 배치 후보와 미리보기로 표현하고, 공간 예약, 공사 현장, 드론 자재 운송, 건물 생성, 공사 취소, 드론 철거, 파괴 후 자원 반환까지 연결한다.

## 배치 계층

- `BuildingPlacementController`: 건설 모드 입력의 진입점. 현재 작업, drag 모드, 작업 우선순위를 보유하고 공사/취소/철거 요청을 만든다.
- `BuildingPlacementOperation`: 후보 묶음, 전체 회전, world 위치 준비, footprint 충돌 평가를 담당한다.
- `BuildingPlacementCandidate`: blueprint-local offset, 실제 anchor, 크기, 방향, 선택 레시피를 전달한다.
- `BuildingPlacementReservationUtility`: 여러 후보의 모든 footprint 셀을 원자적으로 예약하고 실패 시 전체 롤백한다.
- `BuildingPlacementPreview`, `.Power`, `.Construction`: 배치 후보와 공사 현장의 sprite/크기/색, 전신주 공급·연결 범위, 연결 가능 전신주 표시를 담당한다. `.Construction`은 `ConstructionSite`를 읽어 청색 반투명 고스트만 관리하며 시뮬레이션 컴포넌트를 만들지 않는다.
- `BuildingCopyInteraction`, `BuildingCopyBlueprintBuilder`, `BuildingCopySelectionPreview`: 범위 선택과 등록 건물의 배치 후보 변환을 담당한다.
- `BuildingDemolitionSelectionInteraction`, `BuildingDemolitionSelectionPreview`, `DemolitionAreaRequestUtility`: Delete로 시작하는 철거 범위 선택, 붉은 경계 표시, 범위 안의 건물 철거·월드 아이템 회수·공사 취소 요청 생성을 담당한다.
- `BuildingSettingsTransfer`: 같은 종류 건물 사이 설정 복사/붙여넣기를 담당하며 현재는 crafter 레시피 변경 요청을 만든다.

복사 blueprint는 선택 사각형의 lower-left를 `(0,0)` pivot으로 사용한다. `candidate.gridPosition`은 회전 전 blueprint-local offset이고, `candidate.position`만 현재 pivot 기준 world anchor로 갱신된다.

## 공사 생성 흐름

1. `ConstructionModeUI`가 건물 버튼을 `BuildingPlacementOperation`으로 변환하고 작업 우선순위를 설정한다.
2. `BuildingPlacementController`가 pointer 셀에 후보 위치를 준비하고 `ChunkMapSystem`으로 점유/예약과 광부 자원 조건을 평가한다.
3. 좌클릭/drag 시 모든 후보 footprint를 먼저 예약한다.
4. 후보별 `ConstructionSiteCreateRequest`와 `ConstructionSiteReservedCellElement` 버퍼를 만든다.
5. `ConstructionSiteCreationSystem`이 공사 설정과 프리팹 정의를 확인하여 요청 엔티티 자체를 `ConstructionSite`로 전환하고, 정규화한 `BuildingFootprint`를 붙인다. 정의가 없으면 예약을 해제하고 거부한다. `ChunkMapSystem`에 reserved cells를 공사 현장으로 등록한다.
   `BuildingPlacementPreview.Construction`은 이 현장을 관찰해 기존 footprint·회전·렌더 규칙을 따르는 고스트를 표시한다.
6. 필요한 재료별 `ConstructionMaterialRequirementElement`와 `DroneTaskCreateRequest(Construction)`를 만든다.
   요청에 기록된 normal priority는 재료별 Construction 작업에 그대로 전달되며, 값이 유효하지 않을 때만 `DroneConfig.defaultTaskPriority`를 사용한다.
7. 드론 시스템이 재료를 현장 `StoredItemElement`로 전달한다.
8. `ConstructionCompletionSystem`이 모든 요구량 도착과 item reservation 해제를 확인하고, 재료 엔티티를 `ItemStorageSystem`으로 소비한다.
9. 현장 공간 인덱스는 제거하지만 footprint 예약은 유지한 채 `BuildingSpawnRequest`를 만든다.
10. `BuildingSpawnSystem`이 프리팹과 타입별 시뮬레이션 컴포넌트를 만들고 `BuildingOccupantRequest`를 붙인다.
11. `ChunkMapSystem`이 최종 셀 점유를 등록하고 예약을 해제한 뒤 `BuildingOccupant`로 전환한다.
    공사 고스트는 `ConstructionSite`가 사라지면 제거된다.

## 타입별 생성 책임

`BuildingSpawnSystem`은 공통 transform, `PostTransformMatrix`, `BuildingType`, `GridPosition`, `Direction`, `BuildingFootprint`를 설정하고 `BuildingOccupantRequest`를 붙인 뒤 타입별 컴포넌트를 추가한다. 같은 ECB의 인스턴스 생성 뒤 공간 컴포넌트를 기록하므로 등록 시스템이 미완성 인스턴스를 관찰하지 않는다.

주 시설 bootstrap도 점유 요청 전에 footprint를 붙인다. 수동 `BeltAuthoring.Baker`는 기존 단일 셀 벨트 크기 `(1,1)`을 붙인다. 크기·방향의 공통 계약은 [월드 공간 문서](WorldSpatialAndResources.md#크기-데이터-계약)를 따른다.

- Belt: `BuildingRuntimeConfigElement.speed`를 적용한 `Belt`
- Miner: `BuildingRuntimeConfigElement.speed`를 적용한 `Miner`, `BuildingOutputCursor`, `ProducedItemElement`
- Crafter: `BuildingRuntimeConfigElement.speed`를 적용한 `Crafter`, `BuildingOutputCursor`, `StoredItemElement`, `ProducedItemElement`
- Splitter/Merger: 각 방향 cursor 컴포넌트
- Storage: `BuildingRuntimeConfigElement.storageCapacity`를 적용한 `Storage`, `BuildingOutputCursor`, `StoredItemElement`
- PowerPole: `PowerPole`
- CoalGenerator: `CoalGenerator`, `StoredItemElement`, 설정 기반 `PowerGenerator`
- DroneStation: `Storage`, item/drone 버퍼, `PowerConsumer`, `DroneStation`
- ResearchBuilding: 설정 속도를 적용한 `ResearchBuilding`, `StoredItemElement`, 설정 기반 `PowerConsumer`

전력 소비 설정에 등록된 건물은 `PowerConsumer`도 받는다. 일반 Belt/Miner/Crafter/Storage의 조정값은 `BuildingRuntimeConfig.json`, 발전·소비는 `PowerConfig.json`, DroneStation은 `DroneConfig.json`이 소유한다. `progress`, `timer`, output cursor, 방향 cursor 같은 순수 런타임 초기값은 spawn 코드가 설정한다.

`ConstructionSiteCreationSystem`은 `BuildingUnlockElement`도 권위 있게 확인한다. 잠긴 건물은 UI에서 숨기지만 복사나 직접 요청으로 우회해도 공사 현장으로 전환되지 않는다.

## 취소와 철거

### 공사 취소

우클릭 입력은 `ConstructionCancelRequest`도 함께 만든다. 셀에 공사 현장이 있으면 `ConstructionCancelSystem`이 관련 Construction 작업을 취소하고, 도착한 자재를 월드 아이템으로 복원하며 회수 작업을 만든다. 마지막에 공사 현장 인덱스와 footprint 예약을 해제하고 현장을 제거한다.

취소는 `DroneTaskCommandSystem`, `DroneTaskReservationSystem`, `DroneCargoTransferSystem`, `ConstructionCompletionSystem`보다 먼저 실행되어 같은 프레임에 완성되는 것을 막는다.

### 건물 철거

`DroneDemolitionRequestSystem`이 우클릭 위치의 실제 건물을 찾아 `DroneTaskTypeEnum.Demolition` 작업을 만든다. 건설 모드에서 선택한 normal priority도 요청을 거쳐 이 작업에 전달된다. 드론이 대상에 도착해 철거를 완료하면 `BuildingDestroyRequest(UserDemolition)`으로 전환된다.

Delete 철거 선택은 배치/복사 상태를 종료한 뒤 좌클릭 drag의 inclusive `GridBounds`를 만든다. `DemolitionAreaRequestUtility`는 그 범위의 등록 건물마다 철거 요청을, 각 셀의 월드 아이템마다 회수 요청을 만들고, 여러 셀에 걸친 동일 공사 현장은 한 번만 취소 요청으로 만든다. 이 경로의 철거·회수 요청도 현재 normal priority를 사용한다.

`WorldTaskMarkerPresentationSystem`은 활성 철거 및 월드 아이템 회수 작업마다 별도의 프레젠테이션 Entity를 만들고 공용 X Mesh와 URP 머티리얼로 붉은 철거 표시를 렌더링한다. 건물 표시는 footprint 크기·방향을 따르고, 회수 표시는 월드 아이템의 `LocalTransform`을 고정 크기로 따라간다. 표시 Entity는 작업과 대상만 참조하며 공간·시뮬레이션 상태를 소유하지 않는다. 작업이 완료·취소되거나 대상이 사라지거나 아이템이 저장 소유권으로 전환되면 자동으로 제거된다.

`BuildingDestroySystem`은 생산/저장/연료 처리 뒤에 실행되며 다음을 수행한다.

1. 대상 존재, `BuildingOccupant`, 파괴 가능 여부를 검증한다.
2. 정거장이면 stored drone 전체를 수용 가능한 다른 정거장에 선배정하고 `Returning` 상태로 해제한다. 같은 network를 우선하며, 전체 수용 공간이 없으면 파괴를 보류한다.
3. `StoredItemElement`와 `ProducedItemElement`를 `ItemStorageSystem`으로 월드에 복원한다.
4. 공사 설정의 건설 재료 100%를 `WorldItemSpawnRequest`로 반환한다.
5. 사용자 철거인 경우 복원된 아이템에 회수 작업을 만든다.
6. 전신주 범위/토폴로지 등록과 `ChunkMapSystem` 건물 점유를 해제한다.
7. 건물 엔티티를 제거한다.

## 다른 시스템과의 의존 관계

- 공간 예약/점유: `ChunkMapSystem`
- 공사 비용: `ConstructionConfigLoadSystem`
- 자재 운반과 철거 실행: 드론 작업/예약/이동 시스템
- 소유 아이템 소비·복원: `ItemStorageSystem`
- 생성 전 렌더 프리팹과 크기 정의: `BuildingPrefabElement`; 생성 후 크기: `BuildingFootprint`
- 일반 건물 속도와 Storage 용량: `BuildingRuntimeConfig`, `BuildingRuntimeConfigElement`
- 전력/정거장 타입 초기화: `PowerConfig`, `DroneConfig`

## 수정 시 함께 확인할 영역

- 새 건물 타입: enum, SubScene prefab DB, 공사 JSON, `BuildingSpawnSystem`, `BuildingRuntimeConfig.json`, UI 선택 버튼, 전력 설정, 생산/물류 시스템, 파괴 반환을 함께 확인한다.
- footprint 또는 anchor 변경: 복사 pivot, 미리보기, 예약, 현장 reserved buffer, spawn transform, 최종 점유, 입출력 경계를 함께 확인한다.
- 공사 완료/취소 순서 변경: `DroneConstructionTests`, `DroneDemolitionRecoveryTests`, 예약 해제와 도착 자재 복원을 함께 확인한다.
- 파괴 경로 또는 범위 철거 변경: stored/produced item, stored drone, 전신주 등록, 벨트 활성 인덱스, 회수 작업, `DroneDemolitionRecoveryTests`와 `WorldTaskMarkerPresentationTests`를 함께 확인한다.

## 구현상 주의사항

- 배치 요청을 만들기 전에 예약이 끝나야 한다. 요청별로 따로 예약하면 복사 배치의 원자성이 깨진다.
- 현장 완성 시 예약을 해제하지 않는 이유는 다음 `BuildingSpawnRequest`가 같은 footprint를 이어받기 때문이다.
- 실제 건물 등록과 예약 해제는 `BuildingSpawnSystem`이 아니라 `ChunkMapSystem`이 담당한다.
- 공사 재료가 아직 `DroneItemReservation` 상태면 완성을 보류한다.
- `BuildingPlacementPreview` 컴포넌트는 건설 모드를 나가도 공사 현장 고스트를 갱신할 수 있어야 한다. 모드 종료 시에는 배치 후보만 숨기고 공사 현장 프레젠테이션의 수명주기를 끊지 않는다.
- 주 시설은 일반 생성/철거 경로의 예외다. 전용 부트스트랩으로 생성되고 `IndestructibleBuilding` 때문에 철거되지 않는다.
