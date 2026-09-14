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

`DefaultUI.Research`는 같은 UIDocument 안의 전역 연구 오버레이를 제어한다. 왼쪽 목록 선택은 그래프 루트와 상세 조회를 바꾸며, 그래프 노드 선택은 배치와 스크롤을 유지하고 상세 조회만 바꾼다. `ResearchTreeLayout`이 선행 관계로 위→아래 배치를 계산하며 수동 UI 좌표는 사용하지 않는다. 연구 시작 버튼만 `ResearchSelectionRequest`를 만든다. 상단에는 실제 활성 연구의 설명과 주기당 재료, 상세에는 조회 연구의 처음부터 완료 기준 총 재료량을 표시한다. 잠긴 노드도 조회할 수 있으며 미완료 선행 조건을 표시한다. 목록에서 루트 변경 때만 트리를 다시 만들고, 0.1초 갱신에서는 버튼과 `FixedString64Bytes` 키 딕셔너리 및 바인딩 시 캐싱한 `EntityQuery`를 재사용한다. 연구건물 수와 전역 확정 진척도는 ECS에서 직접 읽는다. 상세 규칙은 [ResearchSystem.md](ResearchSystem.md)를 따른다.

## 건설 모드 입력

`ConstructionModeUI`는 건물 버튼과 키보드 단축키를 `BuildingPlacementOperation`으로 변환한다. `BuildingButtonOrder`와 버튼·액션 배열은 UXML 표시 순서를 명시적으로 미러링하며, 숫자 키는 잠긴 버튼을 제외한 이 순서에 `1`부터 연속 배정되고 `0`은 열 번째 표시 버튼에 대응한다. `RefreshBuildingUnlocks()`가 갱신한 표시 상태를 같은 프레임의 단축키 처리에서 사용하므로 연구 해금 뒤에도 순서가 함께 바뀐다. 동일 버튼을 다시 누르면 선택을 해제한다. normal task priority 1~10 선택은 `BuildingPlacementController`에 전달되어 새 공사/철거 요청에 기록되며, 1이 가장 높고 10이 가장 낮고 기본 선택은 5다.

`BuildingPlacementController` 입력은 다음 역할로 분리된다.

- 좌클릭/drag: 현재 후보의 공사 요청 생성.
- 우클릭/drag: 해당 셀의 `DroneDemolitionRequest`와 `ConstructionCancelRequest` 생성.
- `Delete` 후 좌클릭 drag: 배치·복사 상태를 끝내고 붉은 경계의 철거 범위를 선택한다. 확정된 inclusive 범위는 건물 철거, 월드 아이템 회수, 공사 취소 요청으로 변환된다.
- `R`: 후보 묶음 회전.
- `Ctrl+C`: 등록된 건물 범위 선택과 blueprint 복사 모드.
- Shift+우클릭/좌클릭: 설정 복사/붙여넣기. 현재 crafter 레시피는 `CrafterRecipeChangeRequest`로 전달한다.

컨트롤러는 `Awake`에서 기본 ECS World, `ChunkMapSystem`, 월드 카메라를 확인한다. `_worldCamera`가 지정되지 않았으면 `Camera.main`을 사용하며, 필수 의존성을 얻지 못하면 입력 처리를 시작하지 않고 컴포넌트를 비활성화한다.

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
- `BuildingUI.cs`의 연구건물 분기: 활성 연구, 전역/로컬 진척도, 2주기 입력 용량, 회수 대기 재료, 전력과 유효 연구 속도, 짧은 주기 초기화 결과를 표시한다. 연구 선택 기능은 두지 않는다.

`BindVisualTree`는 패널이 사용하는 필수 UXML element를 한 번에 검증한다. 하나라도 없으면 누락 목록을 기록하고 다시 unbind하여 `IsBound == false`로 남기므로 `Update`의 선택·refresh가 진행되지 않는다. 정상 바인딩 때만 버튼 callback을 연결하며 `OnDisable` 또는 재바인딩 전에 해제한다.

0.05초 refresh 중 inventory row, storage slot, belt item row와 빈 상태 label은 기존 `VisualElement`를 재사용한다. 필요한 수만 갱신하고 남는 element는 숨기며, storage/crafter처럼 같은 container의 표현 형식이 바뀔 때 호환되지 않는 기존 child도 숨긴다. 전체 unbind나 레시피 버튼 목록 재구성처럼 구조 자체를 버리는 시점에만 container를 비운다.

건물 선택은 먼저 화면상의 활성 드론을 반경 기반으로 찾고, 그 다음 `ChunkMapSystem.TryGetConstructionSite`, `TryGetBuilding` 순으로 셀의 엔티티를 찾는다. 공사 현장은 기본 모드에서만 선택 가능하며, 실제 건물이 생성될 때까지 footprint 예약이 남아 있으면 UI는 완료 전환 상태를 표시한다. 벨트 UI는 선택 엔티티의 `GridPosition`으로 `ChunkMapSystem.GetItems`를 조회한다. UI는 ECS 컴포넌트와 config buffer를 읽어 표시한다.

레시피 변경과 드론 아이템 이동은 직접 buffer를 수정하지 않고 각각 `CrafterRecipeChangeRequest`, `DroneBuildingItemRequestUtility` 경로를 사용한다.

잠긴 건물과 제작 레시피는 각각 건설 UI와 제작기 UI에서 숨긴다. `ConstructionModeUI`는 매 프레임 쿼리를 생성하지 않고 캐싱된 `_buildingUnlockQuery`로 잠금 상태를 갱신하며, 선택된 건물이 잠기면 선택을 해제한다. 표시 계층 외에도 공사 생성 및 레시피 변경 시스템이 같은 해금 버퍼를 확인한다.

## 월드 작업 표시

`WorldTaskMarkerPresentationSystem`은 `StructuralChangePresentationSystemGroup`에서 활성 Demolition 및 RecoverWorldItem task를 관찰한다. 작업별로 하나의 `WorldTaskMarker` 프레젠테이션 Entity만 만들고, 공용 X mesh와 URP transparent material로 표시한다. 건물 표시는 footprint와 회전을 따르며, 월드 아이템 회수 표시는 실제 `LocalTransform` 위치를 고정 크기로 따른다. 표시 Entity는 입력 대상이나 `ChunkMapSystem` 점유 상태가 아니며, task 종료·대상 소멸·회수 아이템의 `StoredItem` 전환 시 제거된다.

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
- UXML element 이름 변경: `BindVisualTree`의 필수 element 목록, callback 해제, `BuildingUIBindingTests`를 함께 수정한다.
- 새 정보 패널 타입: `IsSupportedBuilding`, refresh 분기, layout, 필요한 config query를 함께 확인한다.
- 연구 UI 변경: 전역 `DefaultUI.Research`의 조회/시작 요청과 개별 `BuildingUI`의 읽기 전용 상태 표시를 분리하고, `ResearchTreeLayout`, 건물·레시피 해금 UI, 권위 시스템 검증과 `ResearchUITests`를 함께 확인한다. 상세 수명주기는 [`ResearchSystem.md`](ResearchSystem.md)를 따른다.
- 반복 갱신 UI 변경: 기존 row/slot/empty label 재사용과 다른 layout element 숨김 규칙을 유지하고 `BuildingUIPoolingTests`를 확인한다.
- 건설 버튼 순서·잠금·단축키 변경: UXML 표시 순서, `BuildingButtonOrder`, 버튼·액션 배열을 함께 맞추고 `ConstructionBuildingHotkeyUtilityTests`를 확인한다.
- 배치 입력 변경: `PointerUtility`, drag state reset, copy/settings 모드, reservation 요청 흐름을 확인한다.
- 청크 로드 범위 변경: 카메라 requested set, `ChunkLoadRequest`, 자원 generated marker, unload 부재를 확인한다.
- 월드 작업 표시 변경: task 종료/취소, 대상 소멸·저장 전환, footprint transform, Entities Graphics material/queue, `WorldTaskMarkerPresentationTests`를 함께 확인한다.

## 구현상 주의사항

- UI는 표시와 명령 생성 계층이다. ECS owner buffer나 task status를 직접 수정하지 않는다.
- 드론 선택은 `Stored` 상태를 제외한 `ActiveDrone`만 대상으로 하며 건물/공사 현장보다 클릭 우선순위가 높다.
- Visual Tree는 재생성될 수 있으므로 callback을 `Awake` 한 번만 연결하지 않는다.
- 필수 UXML element 누락을 부분 바인딩으로 허용하지 않는다. 실패한 tree에서는 선택과 refresh를 진행하지 않는다.
- 반복 refresh에서 container를 매번 `Clear`하거나 row/slot을 새로 만들지 않는다. `Set*`, `GetOrCreate*`, `Trim*` 경로로 기존 element를 갱신·숨긴다.
- UI의 매 프레임(`Update`) 또는 주기적 갱신(`0.05~0.1초`)에서 `CreateEntityQuery`를 매번 생성/해제하지 않는다. `Awake`, `OnEnable`, 또는 바인딩 수명주기에서 `EntityQuery`를 캐싱하여 재사용한다.
- 반복 갱신 루프에서 `FixedString64Bytes`를 `ToString()`으로 변환하여 키 조회를 수행하지 않는다. `FixedString64Bytes` 자체를 딕셔너리 키로 사용하여 GC 가비지를 방지한다.
- 별도 fullscreen BuildingUI UIDocument를 추가하면 모드 버튼 입력을 가릴 수 있다. 현재 DefaultUI 문서 안의 panel 구조를 유지한다.
- `blocking-ui`가 빠진 UI는 월드 클릭이 뒤로 통과한다.
- 컴파일 성공만으로 UI Toolkit element 이름, 화면 배치, pointer 차단, 실제 입력 동작은 검증되지 않는다.
