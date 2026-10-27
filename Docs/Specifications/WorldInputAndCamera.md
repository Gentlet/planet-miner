# 월드 입력과 카메라

## 범위와 확정 동작

키보드/마우스 입력을 카메라와 후속 월드 요청 컨트롤러에 연결한다. WASD/방향키 이동, Shift 2배 가속, 마우스 아래 월드 위치를 유지하는 휠 줌을 사용한다. 이동 속도는 8, 직교 크기는 시작 장면의 5와 범위 2~20을 기본으로 하고 카메라 위치는 제한하지 않는다. 수치는 CameraMovementController의 Inspector에서 조절한다. 휠은 Input System의 Uniform 값을 사용하며 Windows native 모드에서만 120 단위를 변환한다.

일반 UI 위에서는 월드 포인터 입력/휠을 차단하고 키보드 이동을 허용한다. 텍스트 필드에 포커스가 있거나 모달이 보이면 카메라를 포함한 월드 입력을 모두 차단한다. UI에서 시작한 누름은 월드로 이동해도 접수하지 않으며, 월드에서 시작한 누름이 UI/화면 밖으로 나가면 Cancelled를 한 번 전달하고 새 Press 전까지 재개하지 않는다. 포커스 상실/컨트롤러 비활성화/World 종료 또는 기본 World 교체에도 같은 취소 경계를 적용한다. World 부재 중 발생한 누름도 새 World로 인계하지 않는다. InputSystem.onEvent에서 실제 버튼/포인터 이벤트의 차단 이력을 보존하고 UI 위 휠 기여분을 제외하므로 같은 입력 갱신 안의 이동이나 UI 닫힘으로 차단이 사라지지 않는다.

배치/철거/복사/연구/청크 로딩의 제품 명령과 UI 화면은 각각의 후속 컨트롤러가 연결한다. 이 입력 기반은 그 기능이나 실제 화면 검증의 완료를 뜻하지 않는다.

## 구성과 수명

| 타입 | 단독 Writer와 소비 계약 |
| --- | --- |
| WorldInputController | Main Camera에 부착한다. 직렬화한 InputSystem_Actions를 복제하고 Keyboard&Mouse 바인딩의 Player/Move와 Player/Sprint만 활성화한다. 버튼/휠은 Mouse 이벤트에서 읽고, 휠은 UI 밖의 기여분만 합산한다. OnEnable/OnDisable에서 InputSystem.onEvent를 구독/해제하고 OnDisable은 복제본만 비활성화/해제한다. 원본/다른 컨트롤러의 입력을 끄지 않는다. 기본 World 참조는 입력 수명 구분용 식별자이며 ECS 원본 사본이 아니다. |
| WorldInputEventScope | 활성 입력 컨트롤러들이 공유하는 InputSettings 복제본의 게시/복원 소유자다. 첫 소유자가 원래 인스턴스/값을 보존한 채 복제본의 이벤트 병합만 비활성화하고 마지막 Dispose가 원래 인스턴스를 복원/복제본을 해제한다. 원본 설정 에셋을 수정하지 않는다. 메인 스레드의 입력 수명 동안 설정 인스턴스 교체는 이 소유자만 담당하며 다른 설정 Writer는 이 계약과 조정해야 한다. |
| CameraMovementController | WorldInputController의 호출로 카메라 Transform/orthographicSize만 변경한다. 시뮬레이션 위치나 GridPosition을 변경하지 않는다. |
| WorldInputUiBlocker | 각 런타임 UIDocument에 함께 부착하고 WorldInputController 참조를 지정한다. OnEnable/OnDisable에서 자신을 등록/해제한다. 매 프레임 검색한 별도 UI 원본을 만들지 않고 해당 Document의 현재 트리/포커스를 읽는다. |
| WorldInputFrame / WorldPointerGesture | 컨트롤러가 단독으로 작성하는 프레임 입력과 접수된 버튼 수명이다. ECS State/Request/Result가 아니며 재고/작업/선택 상태를 소유하지 않는다. |
| WorldPointerUtility | 카메라 광선과 z=0 XY 평면의 교점을 구하고 정수 중심 셀로 변환한다. 셀 경계는 floor(x+0.5), floor(y+0.5)이며 표현 범위를 벗어난 좌표는 거부한다. |

## 전달 시점과 후속 소비

WorldInputController의 LateUpdate는 입력 읽기 → UI 판정 → 이동/줌 → 최종 카메라의 월드/격자 좌표 계산 → CurrentFrame/FrameReady 전달 순서다. 생성된 프레임은 다음 LateUpdate나 취소로 대체한다. 구독자는 OnEnable에서 FrameReady를 연결하고 OnDisable에서 해제한다. 프레임 번호를 입력 접수 순번이나 ECS 틱 번호로 사용하지 않는다.

이벤트 병합이 켜져 있으면 InputSystem.onEvent보다 앞에서 FastMouse의 중간 위치가 사라질 수 있다. WorldInputEventScope는 활성 소유자가 있는 동안 병합을 비활성화해 UI 진입/이탈을 수집 경계까지 보존한다. 이 설정은 공유 Input System에 적용되며 컨트롤러마다 독립 설정인 것처럼 취급하지 않는다. 버튼 해제 전까지의 거부와 다음 LateUpdate까지의 이력은 수명을 분리한다. 휠은 UI 밖의 이벤트 값만 누적하고 최종 포인터도 UI 밖일 때 한 번 소비한다. 같은 갱신에서 UI로 들어왔다면 누적 휠도 버려 일반 UI 위에서는 휠 줌을 차단한다.

한 입력 갱신에 해제와 재누름이 함께 들어오면 Pressed/Released가 모두 포함될 수 있으며 Held는 최종 버튼 상태다. 최종 Held인 제스처는 다음 프레임에도 유지하고 이후 차단 시 Cancelled를 한 번 전달한다.

HasWorldPosition이 true일 때만 WorldPosition/GridCell을 월드 입력에 사용한다. Cancelled는 좌표가 없어도 진행 중인 입력을 끝내기 위해 소비한다. Released를 클릭 성공으로 해석하거나 UI에서 시작한 누름을 재구성하지 않는다. 기본 World가 없거나 이미 종료됐으면 월드 좌표/버튼 접수를 차단하되 카메라 자체는 UI 규칙에 따라 움직일 수 있다.

후속 컨트롤러는 원본을 직접 바꾸지 않고 기존 배치/취소/설정 등의 요청 API에 연결한다. LateUpdate에서 요청을 제출하면 그 이후의 시뮬레이션 실행이 소비하며 이미 지난 Command에 소급 적용하지 않는다. 실제 건설 모드/선택 상태/명령 매핑은 입력 기반에 중복 저장하지 않는다.

## UI 연결 계약

월드 포인터 차단 영역에는 `blocking-ui`를 붙인다. 모달의 표시 영역에는 `modal-ui`를 붙인다. 보이는 모달은 포인터 위치와 무관하게 모든 월드 입력을 차단한다. display:none/visibility:hidden인 요소나 그 하위 요소는 차단하지 않는다. 조상 overflow clipping과 패널 경계를 적용한 실제 노출 영역으로 판정하며 완전히 잘린 모달은 차단하지 않는다. 비활성화한 버튼/패널도 보이는 차단 영역이면 뒤의 월드 클릭을 허용하지 않는다.

TextInputBaseField의 공통 USS 클래스로 문자열/숫자 필드와 그 내부 포커스를 함께 판별한다. 등록한 Document의 자손인 포커스만 읽는다. 새 UIDocument를 추가할 때 WorldInputUiBlocker와 입력 컨트롤러 참조도 함께 구성해야 한다. 현재 제품 UI 화면은 아직 없으며 이 계약은 후속 UI가 사용하는 연결 기준이다.

Unity 6000.4.11f1의 공개 IResolvedStyle에는 계산된 overflow/clip 경계가 없다. WorldInputUiBlocker는 UI Toolkit의 비공개 worldClip getter를 캐시된 delegate로 읽으며 Input/link.xml이 해당 getter를 보존한다. 스타일 값을 별도 원본으로 추측하지 않는다. Unity 버전을 변경할 때 이 API 연결과 clipping 통합 사례를 다시 확인한다. 이 경로의 EditMode 검증과 Player/IL2CPP 검증을 구분한다.

## 검증

WorldInputPipelineTests는 공식 InputTestFixture의 격리 입력 런타임, 가상 Mouse/Keyboard, 카메라, 독립 ECS World와 등록된 UIDocument/Editor UI 트리를 사용한다. EditMode에서는 런타임 콜백을 Reflection으로 직접 호출하고 Unity SendMessage를 사용하지 않는다. 마우스 기준 줌과 최종 격자 전달, World 종료/즉시 교체/World 부재 중 누름/포커스 상실의 단일 취소, 대각선 가속/줌 한계, 입력 Action 및 공유 설정의 마지막 소유자 복원, 같은 입력 갱신의 UI 시작/진입 후 이탈/닫힘과 휠 차단, 텍스트 포커스/숨긴 모달을 검증한다. 이는 실제 Game View/Player 입력이나 시각 검증을 대신하지 않는다.

2026-10-27 검증은 `C:\Projects\unity\PlanetMiner\worktrees\task-10b1-input-camera`, `feature/task-10b1-input-camera`, 기준 커밋 `5d9a6db`의 미커밋 입력 변경에서 수행했다. Unity 6000.4.11f1 컴파일은 up_to_date/failed:false/errors 없음/compilationFailed:false와 최신 runtime/Editor DLL로 확인했다. 선택 EditMode WorldInputPipelineTests는 6/6 통과, 실패/생략/불확정 0이다. 초기 2/6 결과와 분리하며 공식 입력 런타임/viewport fixture를 보완하고 재누름 수명/클리핑 회귀를 같은 통합 사례로 확인했다.

저장된 V2 메인 장면을 preview scene으로 다시 읽어 Main Camera의 컨트롤러 각 1개, 기존 InputSystem_Actions 참조, 위치 (0,0,-10), 직교 크기 5, 이동 8/가속 2/줌 2~20과 기존 바닥 미리보기/SubScene 각 1개를 확인했다. 원시 실행 근거와 검수 요약은 작업본의 `Logs/Codex/Task10B1/`에 보존한다. 기존 검증 래퍼의 null TestFilter 오검출은 명시적 빈 배열로 우회했다. 선택 테스트는 변경된 Pipeline 명령 `test run`/`test status`를 사용했으며 운영 도구 코드는 변경하지 않았다.

같은 날 사용자의 명시적 요청으로 지정 작업본의 GUI Editor에서 V2 메인 장면을 실제 Play Mode로 실행했다. 프레임 진행과 MonoBehaviour LateUpdate를 확인하고, Input System 가상 Keyboard의 지속 입력으로 일반 이동 약 8/Shift 가속 약 16 단위/초 및 WASD/방향키 대각선 정규화를 측정했다. Game View의 마우스 클릭/휠 조작으로 UI 밖 클릭 전달, 일반 UI 클릭/휠 차단, 줌 5→4와 최소 2/최대 20을 확인했다. 줌 전후 커서 아래 월드 좌표 오차는 약 0.00000044였다.

실행 중에만 만든 UIDocument에 실제 WorldInputUiBlocker를 등록해 텍스트 포커스/보이는 모달에서 이동/가속/휠이 차단됨을 확인했다. 텍스트 포커스와 모달을 해제한 뒤 일반 UI 위에서 키보드 이동이 약 8 단위/초로 유지되는 사례는 가상 Keyboard/Mouse 입력을 실제 Play 루프에 공급해 측정했다. 이는 제품 UI 화면이나 사람이 물리 키를 지속해서 누른 검증과 구분한다. 임시 UI/관측 콜백/가상 장치는 제거했고, Play Mode 종료 후 장면 dirty:false, 카메라 위치 (0,0,-10)/직교 크기 5와 원래 입력 설정 복원을 확인했다. 실행 증거는 `Logs/Codex/Task10B1/PlayMode/report.json`에 기록한다.

Play 시작 전부터 있던 DroneStationLifecycleReservationSystem.OnCreate의 Burst BC1028과 시작 시 공통 Power/Drone 시스템의 Lookup 할당 경고 7건은 별도로 보존했다. 입력 사례 실행 후 콘솔 커서 9 이후 새 항목은 없었으며 프로젝트 전체 무오류 판정으로 확대하지 않는다. Player/IL2CPP 빌드/전체 EditMode는 미수행이다. 입력 감도/사용감, 포커스 복귀와 재활성화의 잔류 입력에 대한 사용자 최종 확인은 별도다. 제품 UI 화면과 실제 배치/철거 등 요청 연결은 후속 Task 범위이며 이 실행 결과로 미검증 범위를 완료 처리하지 않는다.

원본 반영 뒤의 선택 EditMode 확인에서는 처음 5/6 통과했고, RegisteredUi 사례의 첫 UI 차단 assertion이 실패했다. 동일한 원본 UI 프로브에서 10회 Editor update 후에도 runtime panel의 geometry가 NaN/clip이 0임을 확인했다. 공개 PickAll 호출은 geometry와 native hit-test만 준비하고 clip은 0으로 남겼다. 반면 해당 패널의 UpdateForRepaint 1회 호출은 실제 clip을 (0,0,1920,1080)으로 생성하고 기존 ReadState 차단을 true로 만들었다. 원본/Scene/입력 설정은 저장하지 않았고 프로브 정리와 입력 해시 불변을 확인했다. Task 9.5 반영으로 원본 버전이 바뀐 중간 관측은 별도로 구분했으며 최종 Repaint 관측은 aa6aa907 기준이다.

RegisteredUi 테스트는 UI 트리 구성/재구성, 텍스트 입력 추가, 모달 추가 뒤에 실제 Repaint 준비 경계를 명시적으로 실행한다. 테스트는 준비된 요소의 크기, native pick 결과와 실제 clip을 먼저 확인하고 기존 입력 차단/취소/포커스 assertion을 유지한다. 이는 게임 입력 가드나 클리핑 값을 바꾸지 않는다. 구현 타입이 비공개이므로 공개 UpdateForRepaint 메서드도 테스트에서 Reflection으로 호출하며 현재 Unity 버전의 API에 의존한다. 근거는 [Unity 6000.4.11f1 Panel의 Repaint 준비](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.4.11f1/Modules/UIElements/Core/Panel.cs#L1346-L1363)와 [VisualElement의 렌더 데이터 기반 clip](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.4.11f1/Modules/UIElements/Core/VisualElement.cs#L1298-L1324)이다. 이 테스트 보완의 최종 실행 결과는 별도로 기록한다.

보완 작업본 `C:\Projects\unity\PlanetMiner\worktrees\task-10b1-ui-fixture` / `feature/task-10b1-ui-fixture` / 기준 aa6aa907의 ui-fixture-v1에서 C# 컴파일 up_to_date/failed:false/errors 없음/compilationFailed:false, Runtime/Editor DLL 최신성과 선택 EditMode 6/6 통과(실패/생략/불확정 0)를 확인했다. 검증 중 입력 891개와 검수 파일 SHA는 불변이며 가드 독립 검수에서 지적이 없었다. 최초 import의 GUID 관련 C# 오류는 APIUpdater 이전 기록으로 보존했고, 갱신된 직렬화 패키지 캐시는 원본과 같은 SHA로 확인했다. 기존 Burst BC1028은 별도 제한으로 유지한다. 이 보완은 테스트/명세 두 파일이며 Runtime/씬/Packages/ProjectSettings를 변경하지 않았다. 보완 작업본의 실행 근거는 `Logs/Codex/Task10B1UiFixture/validate-01/`에 기록하며 원본 적용 후 확인 결과와 구분한다.
