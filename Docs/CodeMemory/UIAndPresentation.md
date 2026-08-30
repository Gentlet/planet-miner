# UI, 입력, 프레젠테이션

## 목적과 책임

이 영역은 카메라 이동과 청크 요청, 기본/건설 UI 전환, 건물 배치 입력, 건물 정보 조회 및 ECS 요청 생성을 담당한다. 실제 게임 상태를 소유하지 않고 ECS 컴포넌트를 읽거나 요청 엔티티를 만든다.

## 장면의 MonoBehaviour 연결

`Assets/Scenes/SampleScene.unity`에는 다음 주요 GameObject가 있다.

- Main Camera: `CameraMovementController`, `CameraChunkLoader`와 렌더/카메라 컴포넌트.
- BuildingPlacement: `BuildingPlacementController`, `BuildingPlacementPreview`, `GameUIController`.
- DefaultUI: `UIDocument`, `DefaultUI`, `BuildingUI`.
- ConstructionModeUI: 별도 `UIDocument`, `ConstructionModeUI`.
- Sub: `Assets/Scenes/SampleScene/Sub.unity`를 로드하는 `SubScene`.

## UI 모드

- `GameUIController`가 `DefaultUI`와 `ConstructionModeUI` root의 `DisplayStyle`과 컴포넌트 enabled 상태를 전환한다.
- 건설 모드 진입 시 `BuildingUI` 선택을 닫고 비활성화하며 `BuildingPlacementController`를 활성화한다.
- 기본 모드 복귀 시 배치 입력을 비활성화하고 건물 선택을 다시 허용한다.
- UIDocument GameObject 자체를 껐다 켜는 대신 root display와 controller enabled를 사용한다.

`DefaultUI`는 건설 모드 버튼 callback을 `OnEnable/OnDisable`에서 관리한다. `ConstructionModeUI`도 건물·우선순위 버튼과 placement event를 같은 lifecycle에서 연결/해제한다.

## 건설 모드 입력

`ConstructionModeUI`는 건물 버튼과 키보드 단축키를 `BuildingPlacementOperation`으로 변환한다. 동일 버튼을 다시 누르면 선택을 해제한다. normal task priority 1~10 선택은 `BuildingPlacementController`에 전달되어 새 공사/철거 요청에 기록되며, 1이 가장 높고 10이 가장 낮고 기본 선택은 5다.

`BuildingPlacementController` 입력은 다음 역할로 분리된다.

- 좌클릭/drag: 현재 후보의 공사 요청 생성.
- 우클릭/drag: 해당 셀의 `DroneDemolitionRequest`와 `ConstructionCancelRequest` 생성.
- `R`: 후보 묶음 회전.
- `Ctrl+C`: 등록된 건물 범위 선택과 blueprint 복사 모드.
- Shift+우클릭/좌클릭: 설정 복사/붙여넣기. 현재 crafter 레시피는 `CrafterRecipeChangeRequest`로 전달한다.

`PointerUtility.IsPointerOverUi`는 모든 활성 UIDocument에서 `blocking-ui` class를 가진 element를 검사한다. UI 위 입력은 배치, 파괴, 건물 선택으로 통과하지 않는다.

## 건물 정보 UI

`BuildingUI`는 DefaultUI 안의 `building-panel`만 제어하는 partial MonoBehaviour다.

- `BuildingUI.cs`: pointer 선택, 선택 수명주기, 0.05초 refresh, 대상 타입 분기. 화면상의 활성 드론은 반경 기반으로 건물보다 우선 선택한다.
- `.View`: VisualElement 바인딩, layout 전환, 공통 inventory/status 렌더링.
- `.Crafter`: 레시피, 입력/출력, 진행도, recipe change 요청.
- `.Miner`: 자원 생산 상태와 진행도.
- `.Storage`: stack slot 단위 저장 상태. 드론 정거장과 메인 스테이션은 일반 보관공간과 드론 전용 5칸을 별도 grid로 표시한다.
- `.Power`: consumer/generator/main facility/pole의 grid와 전력 상태.
- `.DroneItems`: 선택 건물의 insert/remove drone task 요청.
- `.Drone`: 선택 드론의 상태, 배터리, 화물, 성능, 작업 할당 표시.
- `.Belt`: 선택 벨트 셀의 월드 아이템 종류별 개수와 `Belt.speed` 최대 이동 속도 표시.
- `.Construction`: `ConstructionSite`의 필요/도착 자재, 납품률, 집계 운송 상태를 표시하고, 현장이 실제 건물로 전환되면 같은 셀의 건물 UI로 선택을 넘긴다.

건물 선택은 먼저 화면상의 활성 드론을 반경 기반으로 찾고, 그 다음 `ChunkMapSystem.TryGetConstructionSite`, `TryGetBuilding` 순으로 셀의 엔티티를 찾는다. 공사 현장은 기본 모드에서만 선택 가능하며, 실제 건물이 생성될 때까지 footprint 예약이 남아 있으면 UI는 완료 전환 상태를 표시한다. 벨트 UI는 선택 엔티티의 `GridPosition`으로 `ChunkMapSystem.GetItems`를 조회한다. UI는 ECS 컴포넌트와 config buffer를 읽어 표시한다.

레시피 변경과 드론 아이템 이동은 직접 buffer를 수정하지 않고 각각 `CrafterRecipeChangeRequest`, `DroneBuildingItemRequestUtility` 경로를 사용한다.

## 카메라와 청크 로드

- `CameraMovementController`: Input System의 `Player/Move`, `ScrollWheel`, `Sprint` action을 찾아 이동·줌·가속을 처리한다. 자신이 enable한 action만 `OnDisable`에서 disable한다.
- `CameraChunkLoader`: 카메라 중심 청크가 바뀔 때 반경 내 아직 요청하지 않은 청크에 `ChunkLoadRequest`를 만든다. 이미 자원 생성이 끝난 청크는 건너뛴다.

## 다른 시스템과의 의존 관계

- 건물 선택과 배치 가능성: `ChunkMapSystem`
- 공사/철거: building lifecycle request components
- 레시피/저장/전력 표시: 각 ECS component와 config singleton buffers
- 드론 요청: task request utility 및 normal priority 규칙
- 전신주 미리보기: `PowerGridSystem` query와 power config

## 수정 시 함께 확인할 영역

- UI 모드 전환: 두 UIDocument의 display, component enabled, BuildingUI selection, placement enable을 함께 확인한다.
- UXML element 이름 변경: 해당 controller의 `Q<T>` 바인딩과 callback 해제를 함께 수정한다.
- 새 정보 패널 타입: `IsSupportedBuilding`, refresh 분기, layout, 필요한 config query를 함께 확인한다.
- 배치 입력 변경: `PointerUtility`, drag state reset, copy/settings 모드, reservation 요청 흐름을 확인한다.
- 청크 로드 범위 변경: 카메라 requested set, `ChunkLoadRequest`, 자원 generated marker, unload 부재를 확인한다.

## 구현상 주의사항

- UI는 표시와 명령 생성 계층이다. ECS owner buffer나 task status를 직접 수정하지 않는다.
- 드론 선택은 `Stored` 상태를 제외한 `ActiveDrone`만 대상으로 하며 건물/공사 현장보다 클릭 우선순위가 높다.
- Visual Tree는 재생성될 수 있으므로 callback을 `Awake` 한 번만 연결하지 않는다.
- 별도 fullscreen BuildingUI UIDocument를 추가하면 모드 버튼 입력을 가릴 수 있다. 현재 DefaultUI 문서 안의 panel 구조를 유지한다.
- `blocking-ui`가 빠진 UI는 월드 클릭이 뒤로 통과한다.
- 컴파일 성공만으로 UI Toolkit element 이름, 화면 배치, pointer 차단, 실제 입력 동작은 검증되지 않는다.
