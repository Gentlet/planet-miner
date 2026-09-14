# 연구와 해금 시스템

## 목적과 책임

이 영역은 설정 기반 연구 트리, 전역 연구 선택·진척도·완료, 연구건물의 병렬 주기, 건물·레시피 해금과 영구 속도 보너스를 소유한다. 개별 연구건물은 입력 아이템과 현재 로컬 주기만 보유하며, 연구 선택과 확정 진척도는 하나의 `ResearchConfig` 엔티티에 모인다.

## 주요 데이터와 소유권

- `ResearchConfig`, `ResearchState`: 연구 설정 singleton 표식과 현재 `activeResearchId`.
- `ResearchDefinitionElement`, `ResearchPrerequisiteElement`, `ResearchIngredientElement`, `ResearchRewardElement`: 설정에서 게시된 연구 정의와 연결 데이터.
- `ResearchProgressElement`: `stableId`별 확정 진척도와 완료 상태. 활성 연구를 바꿔도 유지된다.
- `BuildingUnlockElement`, `RecipeUnlockElement`: 현재 사용할 수 있는 건물과 제작 레시피의 권위 있는 목록.
- `ResearchStatModifierElement`: 완료 보상으로 누적된 채굴·제작·벨트·연구 속도 보너스.
- `ResearchBuilding`: 한 연구건물의 기본 속도, 진행 중인 연구 ID, 로컬 주기 진행도와 표시 상태.
- `ResearchSelectionRequest`: UI가 만드는 연구 선택 명령. UI는 연구 상태를 직접 수정하지 않는다.

전역 상태와 모든 버퍼는 `ResearchConfigLoadSystem`이 만든 같은 엔티티에 있다. 개별 연구건물이나 UI에 별도 완료·해금 캐시를 만들지 않는다.

## 설정 로드와 검증

`ResearchConfigLoadSystem`은 `CrafterConfig`가 준비된 뒤 `Assets/Resources/Config/ResearchConfig.json`을 `ConfigResourceLoader`로 읽는다. 제작 레시피 ID까지 검증한 뒤 다음 데이터를 한 번에 게시하고 비활성화된다.

- 초기 건물·레시피 해금
- `stableId` 기반 연구 정의 (UI 좌표는 저장하지 않음)
- 선행 연구, 주기 재료, 진행 수치와 보상
- 연구별 초기 진척도와 빈 전역 활성 연구

`ResearchConfigParser`는 ID 중복과 길이, 존재하지 않거나 순환하는 선행 관계, 재료 타입·수량, 양수 진행 수치, 지원하지 않는 보상/능력치, 잘못된 건물·레시피 대상과 중복 해금을 거부한다. `ResearchBuilding`은 초기 해금 목록에 반드시 있어야 하며, 하나라도 잘못되면 `ResearchConfig`를 부분 게시하지 않는다.

`BeltMoveSystem`, `MiningSystem`, `CrafterSystem`, `CrafterRecipeChangeSystem`, `ConstructionSiteCreationSystem`, `ResearchSelectionSystem`, `ResearchSystem`은 `ResearchConfig`를 요구한다. 연구 설정 로드 실패는 연구 UI만의 문제가 아니라 해금과 보너스를 소비하는 이 시스템들의 실행도 막는다.

## 연구 선택 흐름

1. `DefaultUI.Research`의 목록/노드 클릭은 조회할 연구만 변경한다. 별도의 연구 시작 버튼이 `ResearchSelectionRequest`를 만든다.
2. `ResearchSelectionSystem`이 정의 존재, 미완료 상태, 모든 선행 연구 완료를 확인한다.
3. 유효한 다른 연구이면 `ResearchState.activeResearchId`를 즉시 바꾼다.
4. 모든 연구건물의 진행 중 로컬 주기를 0으로 초기화한다. 이미 소비한 재료는 반환하지 않고, 실제 주기를 잃은 건물에는 짧은 `ResearchChanged` 표시 상태를 남긴다.
5. 새 연구에서 사용하지 않는 보관 아이템은 삭제하지 않고 `DroneBuildingItemRequestUtility`의 기존 제거 요청 경로로 보낸다.

연구별 `ResearchProgressElement`는 선택 변경으로 초기화되지 않는다. 완료된 연구나 선행 조건을 만족하지 않은 연구는 요청으로 우회해도 선택되지 않는다.

## 연구건물 실행 흐름

`ResearchSystem`은 `ItemTrackingSystem`, `ResearchSelectionSystem`, `CrafterSystem` 뒤에서 실행한다.

1. pending 월드 아이템 위치 변경을 적용하고 현재 전역 활성 연구와 연구건물 목록을 읽는다.
2. 각 연구건물의 회전 footprint 전체에서 현재 연구 재료만 수집한다. 품목별 보관 한도는 주기당 필요량의 2배이며 저장 전환은 `ItemStorageSystem.TryStoreItemImmediate`를 사용한다.
3. 예약되지 않은 모든 1회분 재료와 양수 전력 공급을 확인한다. 조건을 모두 만족할 때만 재료 묶음을 소비하고 로컬 주기를 시작한다.
4. 로컬 진행도는 전력 공급 비율, `ResearchBuilding.speed`, 누적 `ResearchSpeed` 보너스를 곱해 증가한다. 0 전력에서는 진행도를 보존하고 대기한다.
5. 주기가 끝나면 해당 연구의 확정 진척도에 `progressPerCycle`을 더한다.
6. 요구 진척도에 도달하면 완료를 한 번 기록하고 보상을 적용한 뒤 전역 활성 연구를 비운다.
7. 다른 연구건물에서 진행 중이던 같은 연구 주기는 `ResearchCompleted` 사유로 초기화한다. 이미 소비한 재료는 반환하지 않는다.

모든 연구건물은 같은 전역 연구에 독립적으로 기여하며 남은 진척도를 예약하지 않는다. 한 건물의 완료가 먼저 확정되면 그 프레임의 뒤쪽 건물은 더 진행하지 않는다.

## 해금과 보너스 연결

- `BuildingUnlock`: 건설 UI가 잠긴 버튼을 숨기고 `ConstructionSiteCreationSystem`도 직접·복사 요청을 다시 검증한다.
- `RecipeUnlock`: 제작기 UI가 잠긴 레시피를 숨기고 `CrafterRecipeChangeSystem`이 변경 요청을 거부한다. `CrafterSystem`도 이미 선택된 레시피가 잠겨 있으면 선택과 진행도를 초기화한다.
- `StatModifier`: `ResearchRuntimeUtility.GetStatMultiplier`가 같은 타입의 퍼센트 보너스를 합산하여 `max(0, 1 + 합계)`를 반환한다. `MiningSystem`, `CrafterSystem`, `BeltMoveSystem`, `ResearchSystem`이 매 갱신 시 해당 배율을 적용하므로 기존 건물과 이후 생성 건물에 같은 방식으로 반영된다.

현재 지원 능력치 키는 `MiningSpeed`, `CraftingSpeed`, `BeltSpeed`, `ResearchSpeed`다. 새 키를 설정에 쓰려면 enum, parser 검증, 실제 소비 시스템과 UI 표시를 함께 구현해야 한다.

## 건물과 UI 연결

- `BuildingTypeEnum.ResearchBuilding`은 SubScene 프리팹 데이터베이스에서 3×3 footprint를 사용한다.
- `BuildingRuntimeConfig.json`이 기본 연구 속도, `ConstructionConfig.json`이 건설 재료, `PowerConfig.json`이 최대 소비 전력을 제공한다.
- `BuildingSpawnSystem`은 `ResearchBuilding`, `StoredItemElement`, 설정 기반 `PowerConsumer`를 붙인다. 별도 생산품이나 벨트 출력 버퍼는 없다.
- `DefaultUI.Research`는 왼쪽에 미완료이면서 모든 선행 조건을 충족한 연구를 표시한다. 진행 중 연구도 목록에 남으며 강조된다. 오른쪽은 목록에서 선택한 연구를 루트로 후속 연구를 위에서 아래로 표시한다. 그래프 노드 클릭은 상세 조회만 바꾸며 루트·배치·스크롤을 유지한다.
- `ResearchTreeLayout`은 선행 관계의 역방향 인접 목록으로 도달 가능한 하위 연구를 찾고, 표시되는 부모 중 최대 깊이 + 1에 자식을 배치한다. 같은 깊이는 JSON 정의 순서로 정렬하고 가운데 정렬한다. 공통 자식은 한 번만 배치하며 선행 관계마다 연결선을 그린다.
- 트리 밖의 다른 선행 연구는 자동 완료로 취급하지 않는다. 잠긴 노드와 상세 설명에는 실제 미완료 선행 조건을 표시한다. 잠긴 노드도 조회할 수 있지만 연구 시작은 비활성화되며 요청 생성 직전 다시 검증한다.
- 조회 ID와 그래프 루트 ID는 UI 로컬 상태이며 `ResearchState.activeResearchId`와 구분한다. 목록에서 루트 변경 때만 트리를 다시 만들고, 0.1초 갱신에서는 버튼/목록 요소와 `FixedString64Bytes` 키 딕셔너리 및 캐싱된 `EntityQuery`를 재사용한다.
- 상단 활성 연구는 실제 진행 연구의 이름·진척도·설명·1주기 소모 재료를 표시한다. 상세 조회 영역에는 처음부터 완료 기준 총 재료량을 표시한다: `ceil(requiredProgress / progressPerCycle) × ingredient.amount`. 이는 잔여량이나 실제 누적 소모량이 아니며 주기 초기화/병렬 완료에 따른 낭비는 포함하지 않는다. 아이템 이름은 `ItemTypeExtension.GetDisplayName`을 사용한다.
- `ConstructionModeUI`는 캐싱된 `_buildingUnlockQuery`를 통해 잠긴 건물 버튼을 숨긴다.
- `BuildingUI`의 연구건물 분기는 전역/로컬 진척도, 입력 보관량, 회수 대기 아이템, 전력과 유효 속도, 로컬 주기 초기화 결과를 표시하며 연구를 선택하지 않는다.

## 다른 시스템과의 의존 관계

- 설정과 레시피 ID: `ConfigResourceLoader`, `CrafterConfigLoadSystem`
- footprint 입력과 아이템 소유권: `ChunkMapSystem`, `BuildingInputCollectionUtility`, `ItemTrackingSystem`, `ItemStorageSystem`
- 부적합 보관품 회수: 드론 건물 아이템 제거 요청과 예약 수명주기
- 건설과 파괴: 일반 `ConstructionSite`/`BuildingSpawnSystem`/`BuildingDestroySystem` 흐름
- 전력 진행 속도: `PowerProductionUtility`, `PowerGridSystem`
- 표시와 명령 생성: `DefaultUI.Research`, `BuildingUI`, `ConstructionModeUI`

## 수정 시 함께 확인할 영역

- 연구 JSON 스키마·보상 변경: DTO, parser 전체 검증, load system 게시 버퍼, `ConfigLoadSystemTests`를 함께 확인한다.
- 선택·완료 규칙 변경: 전역 진척도 보존, 모든 로컬 주기 초기화, 소비 재료 비반환, 선행 조건과 `ResearchSelectionPreservesGlobalProgressAndResetsLocalCycle` 테스트를 확인한다.
- 연구건물 입력/진행 변경: footprint 수집, 2주기 용량, 예약 아이템 제외, 원자적 소비, 전력 0 일시 정지, `PowerProductionProgressTests`의 병렬 연구와 첫 입력 테스트를 확인한다.
- 건물·레시피 해금 변경: UI 숨김, 공사 생성 검증, 레시피 변경 검증, 이미 선택된 레시피 정리까지 함께 확인한다.
- 연구 목록·후속 트리·상세 조회 변경: `ResearchTreeLayout`, 조회와 실제 연구 시작의 분리, 잠금·완료 상태, UI 요소 재사용을 `ResearchUITests`에서 확인한다.
- 속도 보너스 추가: parser가 허용하는 키, modifier 합산, 해당 런타임 계산과 BuildingUI 표시를 함께 확인한다.

## 구현상 주의사항

- `ResearchConfig` 엔티티가 전역 연구 상태, 확정 진척도, 해금, 보너스의 단일 소유자다.
- 연구 선택/완료 시 로컬 주기에서 이미 소비한 재료를 복원하지 않는다. 아직 `StoredItemElement`에 남은 아이템만 일반 소유권·회수 흐름을 따른다.
- 연구 재료를 직접 buffer에서 제거하거나 월드 아이템을 직접 비활성화하지 않는다. `ItemStorageSystem` 경계를 사용한다.
- 해금은 UI 표시만으로 강제하지 않는다. 공사와 제작 시스템의 권위 검증을 유지한다.
- `ResearchConfig`가 없을 때 핵심 생산·해금 소비 시스템이 실행되지 않는 현재 의존성을 설정 로더 순서 변경 시 함께 고려한다.
- 연구 UI 및 건설 해금 UI 갱신 시 `CreateEntityQuery`를 반복 호출하지 않고, 딕셔너리 키 조회 시 `ToString()`을 피하고 `FixedString64Bytes`를 직접 사용한다.
