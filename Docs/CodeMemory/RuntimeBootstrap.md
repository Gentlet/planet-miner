# 런타임 부트스트랩과 설정

## 목적과 책임

이 영역은 GameObject 장면을 ECS 월드에 연결하고, 프리팹과 설정 데이터를 런타임 시스템이 조회할 수 있는 싱글턴 컴포넌트/버퍼로 준비한다. 초기 주 시설과 시작 아이템도 이 단계에서 만들어진다.

## 장면 구성

- `Assets/Scenes/SampleScene.unity`: 메인 장면. Main Camera, `BuildingPlacement`, `DefaultUI`, `ConstructionModeUI`, `GameUIController`, SubScene 오브젝트가 있다.
- `Assets/Scenes/SampleScene/Sub.unity`: ECS 베이킹 입력 장면. `ChunkLoadAreaAuthoring`, `ResourceMapGeneratorAuthoring`, `ResourcePrefabDatabaseAuthoring`, `ItemPrefabDatabaseAuthoring`, `BuildingPrefabDatabaseAuthoring`, `DronePrefabDatabaseAuthoring`을 포함한다.
- `Assets/Resources/Prefabs/`: 실제 엔티티 렌더 프리팹. 현재 spawn 시스템은 프리팹을 인스턴스화한 뒤 필요한 시뮬레이션 컴포넌트를 추가한다.

## 주요 스크립트와 데이터

### Baker와 프리팹 데이터베이스

- `BuildingPrefabDatabaseAuthoring`: `BuildingPrefabElement(type, prefab, size)` 버퍼를 만든다. footprint 크기의 기준 데이터다.
- `ItemPrefabDatabaseAuthoring`: `ItemPrefabElement` 버퍼를 만든다.
- `ResourcePrefabDatabaseAuthoring`: `ResourcePrefabElement` 버퍼를 만든다.
- `DronePrefabDatabaseAuthoring`: 활성 드론으로 전환할 때 사용할 `DronePrefab` 싱글턴을 만든다.
- `ChunkLoadAreaAuthoring`: 초기 `ChunkLoadArea`를 만든다.
- `ResourceMapGeneratorAuthoring`: `ResourceGenerationConfig.json`을 베이킹 시 읽어 `ResourceChunkGenerationSettings`와 `ResourceGenerationConfigElement`를 만든다.
- `ItemAuthoring`, `ResourceAuthoring`, `BeltAuthoring`: 프리팹/수동 배치 오브젝트의 최소 ECS 컴포넌트를 베이킹한다.

### 런타임 JSON 로더

- `CrafterConfigLoadSystem` + `CrafterConfigParser`: 레시피와 아이템별 저장 한도를 `CrafterConfig`, `CrafterRecipeElement`, `CrafterRecipeIngredientElement`, `ItemStorageLimitElement`로 게시한다.
- `ConstructionConfigLoadSystem` + `ConstructionConfigParser`: 건물별 공사 재료를 `ConstructionConfig`, `ConstructionMaterialConfigElement`로 게시한다.
- `PowerConfigLoadSystem` + `PowerConfigParser.*`: 전신주 범위, 발전기 출력, 석탄 연료, 소비 건물 설정을 게시한다. 전체 문서 검증이 성공한 뒤에만 ready config 엔티티를 공개한다.
- `DroneConfigLoadSystem` + `DroneConfigParser`: 정거장 저장량, 운반량, 이동·배터리·충전 수치를 `DroneConfig`로 게시한다.
- `StartingItemConfigLoadSystem` + `StartingItemConfigParser`: Main Facility의 초기 지급 목록을 `StartingItemConfigElement(itemType, quantity)` 버퍼로 게시한다.

설정 원본은 `Assets/Resources/Config/`에 있다. 자원 생성 설정만 Baker가 읽고, 공사·레시피·저장·전력·드론·시작 아이템 설정은 런타임 시스템이 `Resources.Load<TextAsset>`로 읽는다. 바닥 바이옴 설정도 `Resources`에서 읽지만 ECS 설정 엔티티가 아니라 `FloorChunkRenderer`의 프레젠테이션 설정으로 사용된다.

## 초기 실행 흐름

1. SubScene이 베이크된 프리팹 버퍼와 초기 청크/자원 설정을 ECS 월드에 넣는다.
2. config loader의 `OnCreate`가 JSON을 파싱하고 설정 엔티티를 만든다.
3. `MainFacilityBootstrapSystem`이 `PowerConfig`, `DroneConfig`, `StartingItemConfigElement`, 건물 프리팹 버퍼를 기다린다.
4. 주 시설이 없으면 `(0,0)` footprint를 `ChunkMapSystem`에 예약하고 MainFacility 프리팹을 인스턴스화한다.
5. 주 시설에 `BuildingOccupantRequest`, `Storage`, `StoredItemElement`, `StoredDroneElement`, `DroneStation`, `PowerConsumer`, `PowerGenerator`, 가상 `PowerPole`, `IndestructibleBuilding`을 추가한다.
6. `StartingItemConfigElement`의 각 항목 수량만큼 `StartingItemSpawnRequest`를 만들어 시작 자원과 Drone 아이템을 주 시설 소유 저장품으로 생성한다.
7. `ChunkMapSystem`이 주 시설의 `BuildingOccupantRequest`를 실제 셀 점유로 등록하고, 드론 네트워크와 전력 시스템이 이후 프레임에 참가자로 인식한다. 가상 전신주는 자기 셀만 공급 범위로 등록하여 주 시설 단독 전력망을 형성한다.

## 의존 관계

- `BuildingSpawnSystem`, `ChunkMapSystem`, 배치 미리보기는 `BuildingPrefabElement`의 footprint를 공동 기준으로 사용한다.
- `ItemSpawnSystem`, `WorldItemSpawnSystem`은 `ItemPrefabElement`에 의존한다.
- `ResourceSpawnSystem`은 `ResourcePrefabElement`, 자원 생성은 SubScene의 생성 설정에 의존한다.
- 공사, 드론 작업, 저장 용량, 전력 토폴로지와 생산 속도는 런타임 config singleton이 없으면 시작하거나 진행할 수 없다.
- UI의 레시피/저장 표시는 `CrafterConfig` 엔티티와 그 버퍼를 직접 조회한다.

## 수정 시 함께 확인할 영역

- 건물·아이템·자원 enum 추가: 해당 enum, 변환 extension, SubScene 데이터베이스 매핑, 프리팹, 설정 JSON, spawn 분기를 함께 확인한다.
- 건물 footprint 변경: SubScene `BuildingPrefabDatabaseAuthoring` 항목, 배치/예약, 공간 등록, 입출력 경계, 전력/정거장 중심 계산, 시각 scale을 함께 확인한다.
- 프리팹에 시뮬레이션 컴포넌트를 추가: spawn 시스템의 `AddComponent`와 중복되지 않는지 확인한다.
- 설정 스키마 변경: DTO, parser 검증 순서, loader 게시 조건, 소비 시스템 쿼리와 EditMode 테스트를 함께 확인한다.

## 구현상 주의사항

- 프리팹 데이터베이스는 SubScene singleton buffer라는 전제가 여러 시스템에 있다. 중복 데이터베이스 엔티티를 만들면 `GetSingletonBuffer` 호출이 깨진다.
- 현재 생성 프리팹은 주로 렌더 역할이며 실제 게임플레이 컴포넌트는 spawn 시스템이 붙인다.
- 설정 파싱 실패를 빈 설정과 동일하게 숨기지 않는다. 특히 전력 설정은 부분 게시가 아니라 전체 성공 후 게시하는 계약이다.
- `MainFacilityBootstrapSystem`의 주 시설은 일반 `BuildingSpawnRequest` 경로가 아니라 전용 부트스트랩 경로를 사용하지만, 마지막 공간 등록은 같은 `BuildingOccupantRequest -> ChunkMapSystem` 수명주기를 따른다.
