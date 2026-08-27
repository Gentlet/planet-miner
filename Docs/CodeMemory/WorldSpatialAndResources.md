# 월드 공간, 청크, 자원

## 목적과 책임

이 영역은 무한 격자 좌표를 청크와 셀로 나누고, 각 셀의 건물·자원·아이템·범위 소유권을 추적한다. 자원 청크 생성도 이 공간 모델을 기준으로 동작한다.

## 핵심 상태 소유자

`ChunkMapSystem`과 그 partial 파일들이 하나의 중앙 공간 인덱스를 구성한다.

- `ChunkMapSystem.cs`: 청크 dictionary, 아이템 역방향 인덱스, 공사 현장 셀 인덱스, 활성 벨트 셀, 벨트 Native map, 건물 예약 set의 수명주기.
- `ChunkMapSystem.Buildings.cs`: footprint 예약, 공사 현장 등록, 실제 건물 점유 등록/해제, 범위 내 건물 조회.
- `ChunkMapSystem.Items.cs`: 셀별 월드 아이템과 `Entity -> cell` 역방향 소유권.
- `ChunkMapSystem.Belts.cs`: `cell -> belt`, 활성 벨트 셀, 진행 방향에 따른 셀 내 아이템 정렬.
- `ChunkMapSystem.Resources.cs`: 자원 점유 요청 등록과 제거.
- `ChunkMapSystem.PowerPoles.cs`: 각 셀을 덮는 전신주 목록의 원자적 등록/해제.
- `ChunkMapSystem.DroneStations.cs`: 각 셀을 덮는 드론 정거장 목록의 원자적 등록/해제.

`Chunk`, `ChunkCell`은 managed 데이터다. 셀에는 한 건물, 한 자원, 여러 월드 아이템, 여러 전신주 범위, 여러 정거장 범위를 저장한다. `ChunkMapSystem` 외부에서 `ChunkCell`을 직접 변경하지 않고 시스템 API를 사용한다.

## 좌표와 footprint

- `ChunkUtility`: 음수 좌표에서도 올바른 floor division으로 청크와 로컬 셀을 변환한다.
- `GridBounds`: inclusive 사각형의 정규화, 포함, 중첩, 셀 열거.
- `BuildingFootprintUtility`: anchor와 크기, 방향으로 회전된 점유 셀과 시각 중심을 계산한다.
- `VectorExtension.ToGridCell`, `Float3Extension.ToGridCell`: 셀 중심이 정수 좌표인 규칙으로 월드 좌표를 셀로 바꾼다.

## 공간 등록 흐름

### 건물

1. 배치 계층이 모든 footprint 셀을 `TryReserveBuilding`으로 예약한다.
2. 공사 현장은 예약 셀을 `TryRegisterConstructionSite`에 등록해 취소/완성 전까지 소유한다.
3. 완성 후 생성된 건물은 `BuildingOccupantRequest`를 가진다.
4. `ChunkMapSystem.OnUpdate`가 필수 공간 컴포넌트와 footprint 충돌을 검사하고 모든 셀에 같은 건물 엔티티를 기록한다.
5. 벨트면 anchor 셀 벨트 인덱스도 등록한다.
6. 등록 성공 시 예약을 해제하고 `BuildingOccupant`로 전환한다. 중간 실패 시 이미 쓴 셀과 예약을 롤백한다.

### 월드 아이템

`ChunkMapSystem`은 아이템이 가진 `GridPosition`만 믿지 않고 역방향 인덱스를 함께 유지한다. `ItemTrackingSystem`이 `ItemCellChanged`를 소비해 이전 셀에서 제거하고 새 셀에 등록하며, 벨트 셀의 순서를 다시 정렬한다.

### 자원

`ResourceSpawnSystem`이 `ResourceOccupantRequest`를 붙인 자원 엔티티를 만들고, `ChunkMapSystem`이 셀 점유와 floor를 등록한 뒤 `ResourceOccupant`로 전환한다.

### 전력과 드론 범위

`PowerGridSystem`과 `DroneStationNetworkSystem`이 계산한 inclusive bounds를 `ChunkMapSystem`에 등록한다. 셀은 단일 망 ID가 아니라 해당 셀을 덮는 전신주/정거장 엔티티 목록을 보관한다. 실제 망 선택과 병합 규칙은 각 도메인 시스템이 결정한다.

## 청크와 자원 생성 흐름

1. SubScene의 `ChunkLoadAreaAuthoring`이 초기 `ChunkLoadArea`를 베이킹한다. 카메라 이동 중에는 `CameraChunkLoader`도 아직 요청하지 않은 주변 청크의 `ChunkLoadRequest`를 만든다.
2. `ChunkLoadAreaSystem`이 영역을 개별 `ChunkLoadRequest`로 펼친다.
3. `ResourceMapGenerationSystem`이 `worldSeed`, 청크 좌표, 자원 종류를 해시하여 결정론적 patch 후보를 만든다.
4. 대상 청크에 아직 자원이 생성되지 않았을 때만 `ResourceSpawnRequest`를 만들고 청크를 generated 상태로 표시한다.
5. `ResourceSpawnSystem`이 프리팹을 인스턴스화하고 `GridPosition`, `ResourceDeposit`, `ResourceOccupantRequest`를 설정한다.
6. `ChunkMapSystem`이 실제 셀 자원 점유를 등록한다.

## 바닥 바이옴 프레젠테이션

`FloorChunkRenderer`는 장면 로드 뒤 `RuntimeInitializeOnLoadMethod`에서 단일 persistent GameObject로 만들어지고, `Resources/Config/FloorGenerationConfig.json`을 읽어 자원 생성이 완료된 청크마다 바닥 메시를 하나 만든다. 이 계층은 `ChunkMapSystem`의 청크 목록을 읽기만 하며, 셀 floor 값, 건물·자원 점유, 예약, 자원 생성에는 관여하지 않는 순수한 배경 렌더링이다.

- 바이옴 영역은 설정된 `biomeRegionSizeInChunks`(기본 3×3 청크) 단위로 고정 시드 해시에서 선택한다.
- `transitionWidthInChunks`(기본 총 2청크) 안에서는 저주파 도메인 워프와 좌표 해시로 인접 바이옴 바닥을 결정론적으로 섞는다. `nearBiomePreferenceExponent`(기본 0.5)는 완충지대에서 가까운 쪽 바이옴의 우세가 증가하는 속도를 정하며, 1보다 작을수록 더 빨리 우세해진다.
- 바닥 이미지와 가중치는 `biomes[].floorVariants[]` 설정 목록에서 관리한다. 이미지는 `Resources` 경로를 사용하며, 추가 바이옴·변형은 코드를 분기하지 않고 설정으로 확장한다.
- 각 셀 결과는 월드 시드와 월드 좌표만으로 계산하므로 청크·카메라 로드 순서와 무관하다.
- 하나의 청크 메시 안에서 variant별 submesh/material을 사용하고, 생성한 청크 GameObject를 내부 dictionary에 보관한다.
- 청크 unload가 구현될 때에는 해당 청크의 바닥 메시 GameObject도 함께 해제해야 한다.

## 다른 시스템과의 의존 관계

- 건물 배치, UI 선택, 광부, 저장/제작 입출력, 벨트, 전력, 드론이 모두 `ChunkMapSystem` 조회 API에 의존한다.
- `ItemTrackingSystem`은 공간 인덱스와 `GridPosition`의 일치를 유지하는 전담 동기화 계층이다.
- `PowerGridSystem`은 전신주 후보를 셀 인덱스에서 얻지만 토폴로지와 grid entity는 자체 소유한다.
- `DroneStationNetworkSystem`은 정거장 범위를 셀 인덱스에 등록하지만 network ID는 자체 소유한다.

## 수정 시 함께 확인할 영역

- 셀 소유권 변경: 등록, 역방향 인덱스, 활성 벨트 set, 롤백, 파괴 경로를 함께 확인한다.
- footprint/회전 변경: 배치 후보, 예약, 공사 현장, 건물 등록/해제, 입출력 경계, 미리보기를 함께 확인한다.
- 청크 크기/좌표 변경: `GameConstants`, `ChunkUtility`, 정거장 chunk 범위, 자원 seed, 카메라 로더를 함께 확인한다.
- 자원 생성 변경: authoring JSON 로드, 결정론적 seed, 인접 청크 patch 경계, 중복 방지, `Chunk.HasGeneratedResources`를 확인한다.
- 바닥 바이옴 변경: config parser 검증, 월드 좌표 기반 sampler 결정론, `Resources` sprite 경로, 청크 메시 cache를 함께 확인한다.

## 구현상 주의사항

- 현재 청크 unload 경로가 없다. 생성된 청크와 카메라의 requested set은 월드 수명 동안 증가한다.
- `BuildingOccupantRequest`와 `ResourceOccupantRequest`는 공간 등록 대기 마커다. 실제 등록 전에 `Occupant`로 간주하지 않는다.
- 예약과 등록은 다중 셀 중간 실패가 가능한 작업이므로 롤백을 제거하면 안 된다.
- 전신주/정거장 범위 등록은 아직 만들어지지 않은 셀도 생성할 수 있다. 이 동작은 범위 조회의 일관성을 위한 현재 구조다.
- `Clear`는 내부 상태를 모두 비우는 private 경로이며 일반 월드 unload 기능이 아니다.
