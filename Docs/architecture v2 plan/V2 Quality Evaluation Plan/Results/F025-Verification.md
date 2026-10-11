# F-025 — V2 빌드·편집 진입 통일과 세 DB 실제 베이킹

2026-10-11. [최초 평가 Q11](Q11.md), [현재 Task](../V2%20Quality%20Improvement%20Tasks.md). 사용자 확정: 빌드와 편집 모두 기존 V2로 통일하고 진단 바닥을 유지한다. 컴파일과 선택한 장면의 세 DB 실제 베이킹만 확인하며 Play Mode와 Player 빌드는 실행하지 않는다.

## 변경 결과

- 전역 `ProjectSettings/EditorBuildSettings.asset`의 단일 활성 장면을 `Assets/Scenes/V2 Test Scene.unity`와 현재 GUID로 변경했다.
- 현재 활성 Windows 프로필 `Assets/Settings/Build Profiles/Windows.asset`의 단일 활성 장면도 같은 V2로 변경했다. 기존 프로필의 별도 장면 목록 사용, 플랫폼과 다른 설정은 유지했다. 두 설정 파일의 diff는 각각 경로/GUID 두 줄 교체뿐이다.
- 실제 Editor는 이미 V2 메인/sub 장면을 열고 있었으며 모두 dirty=false였다. V2를 활성 편집 장면으로 유지하고 `EditorSceneManager.playModeStartScene`도 V2로 지정했다. 이 지정은 Editor별 로컬 설정으로 저장되며 저장소에 새 시작 코드나 장면을 추가하지 않는다. 다른 Editor에서도 AGENTS.md의 같은 편집 기준을 적용한다.
- 기존 `V2FloorBiomePreview` 1개와 SubScene 자동 로드를 유지했다. 제품 C#·테스트·Config·장면·프리팹은 변경하지 않았다. SampleScene 계열은 삭제/복원/이관하지 않고 레거시 참고로 보존했다.

수정은 연결된 Editor API로 수행했다. 전역 목록과 활성 프로필 목록을 각각 지정하고 파일 저장 후 실제 diff와 Editor의 유효 장면 목록을 대조했다. 새 DB·캐시·fallback·런타임 초기화·UI·입력·드론 기능은 추가하지 않았다. 크기/표시 이름은 Config, 건물 등록은 Type/Tier/Prefab이라는 현재 계약을 유지한다. Item/Resource의 모드는 현재 저장된 InspectorList를 사용하며 두 도메인의 Resources 자동 탐색 구현을 제거하지 않았다.

[설정 변경 결과](../../../../Logs/QualityImprovement/F025/entry-result.json), [설정 변경 코드](../../../../Logs/QualityImprovement/F025/ConfigureV2Entry.cs), [최종 유효 빌드 설정](../../../../Logs/QualityImprovement/F025/build-final.json), [보존 감사](../../../../Logs/QualityImprovement/F025/preservation-audit.json).

## 컴파일 확인

대상은 Unity 6000.4.11f1의 PlanetMiner Editor다. 현재 저장된 설정 변경 후 재컴파일 확인은 **up_to_date, failed:false, errors:[], compilationFailed:false**였다. 제품 C#을 바꾸지 않아 실제 새 어셈블리 생성은 필요하지 않았다. Editor ready/비컴파일/비리로드/Play Mode stopped와 `Assembly-CSharp.dll`이 최신 `Assets/Scripts` C#보다 새것임을 함께 확인했다.

- 최신 런타임 소스 수정: 2026-10-11 05:37:45.3496013 UTC.
- 실행 어셈블리 수정: 2026-10-11 05:37:52.4378924 UTC.

[컴파일 상태](../../../../Logs/QualityImprovement/F025/compile-result.json), [어셈블리 최신성](../../../../Logs/QualityImprovement/F025/assembly-freshness.json), [최종 Editor 상태](../../../../Logs/QualityImprovement/F025/editor-final.json).

## 선택한 실제 입력의 베이킹 확인

현재 V2 메인 장면의 활성 SubScene이 저장된 `V2 Test Scene/sub.unity`를 가리키고 AutoLoadScene=true임을 확인했다. 저장된 SubScene을 임시 PreviewScene으로 읽고, 세 DB 루트 전체를 설치된 Entities의 실제 `BakeGameObjects`에 전달했다. 원본 등록·프리팹·Config에 검증용 항목을 추가하지 않았다. 실제 Baker가 만든 독립 World에서 다음을 확인했다.

- Building/Item/Resource Authoring과 베이킹 DB는 각각 1개다.
- 건물 11개는 기존 모든 종류의 Tier 1이며 현재 Config 조합에 대응한다.
- 아이템 9개와 자원 4개는 현재 저장된 목록 그대로 베이킹됐다.
- **24/24 원형 참조**는 BakingSystem의 원본 GameObject→Entity 매핑과 정확히 일치한다. 각 원형 엔티티가 생존하고 Prefab/LocalTransform을 가진다.
- **9/9 아이템**은 실제 베이킹된 ItemIdentity가 DB 품목과 일치한다.
- 원형에 BuildingType/ItemOwnership/ResourceNode라는 런타임 상태를 미리 붙이지 않았음을 확인했다. 생성/소유권/자원 생성 책임을 Baker로 이동하지 않았다.

이어 같은 독립 World에서 현재 제품의 Building/Item/Recipe/World Config Init을 실행했다. 네 설정의 GameSimulationGroup 필수 태그/버퍼 구성이 각각 1개씩 준비되고 Fatal이 없음을 확인했다. `PrefabDatabaseInitializationSystem`이 실제 베이킹 DB를 검사하여 **PrefabDatabaseReady=1, SimulationFatalError=0, 검증 시스템 비활성화**로 완료했다. 이 검증은 명시적 Bake 산출물과 현재 초기 검사 계약을 확인한 것이며 자연 SubScene 스트리밍을 실행한 것은 아니다.

검증 World와 BlobAssetStore를 폐기하고 PreviewScene을 닫았다. DefaultGameObjectInjectionWorld를 교체하거나 게임 시뮬레이션을 실행하지 않았다. 종료 후 원래 V2 메인/sub 두 장면은 모두 dirty=false였고 V2 메인이 활성 상태였다. 실제 Bake 구간의 새 콘솔 Error/Warning은 각각 0건이다.

[실제 Baker 결과와 24개 매핑](../../../../Logs/QualityImprovement/F025/baker-result.json), [베이킹 검증 코드](../../../../Logs/QualityImprovement/F025/BakeThreeDatabases.cs), [최종 열린 장면](../../../../Logs/QualityImprovement/F025/scenes-final.json), [최종 콘솔 상태](../../../../Logs/QualityImprovement/F025/console-final.json).

## 도구 실패와 수정

초기 터미널 CLI는 STATUS_NO_INSTANCES/STATUS_PIPELINE_LOAD_PENDING을 반환했다. Pipeline 목록에서 대상 Editor 실행·Safe Mode 부재를 확인했고 사용자가 Start Server를 켰다. 터미널의 인스턴스 탐지는 이후에도 연결을 찾지 못했지만, Unity MCP의 대상 projectPath와 실제 API를 확인한 뒤 준비된 같은 Editor에서 설정·컴파일·베이킹을 진행했다. 이 연결 실패를 제품 컴파일 실패로 해석하지 않는다.

설정 변경용 임시 코드에서 경로 구분자 비교와 이미 활성인 장면을 다시 지정하는 반환값 검사가 실패해 수정했다. 전역 설정은 메모리 변경만으로 즉시 파일에 저장되지 않았다. persistent 설정 객체에 `SaveToSerializedFileAndForget`을 사용한 두 시도는 콘솔 Error 2건을 남겼으며 파일 저장에 실패했다. 다른 dirty 프로젝트 에셋이 없는 것을 확인한 뒤 정상 `AssetDatabase.SaveAssets`로 저장하여 전역 파일의 경로/GUID diff를 확인했다. 재현 코드도 올바른 저장 방식으로 고쳤다. 이 두 도구 오류는 콘솔을 지우지 않고 [원본 로그](../../../../Logs/QualityImprovement/F025/configuration-tool-errors.json)에 보존했다. 제품 컴파일/실제 Baker 오류와 구분한다.

## 완료 판단과 남은 범위

선택한 V2 경로가 전역/활성 프로필/현재 편집 기준과 일치하고, 실제 Bake의 세 DB·타입/티어·정확한 원형 참조·필수 구성과 제품 초기 검증을 확인했다. 현재 Task의 선택 경로 완료 기준을 충족하여 **F-025 한 건을 완료**했다. 집계는 **41/43**, 미완료는 **F-006/F-017**이다.

새 자동 테스트나 기존 assertion 보강, EditMode 테스트 묶음 실행은 없다. Play Mode·자동 SubScene 시작 실행·Player 빌드·실제 사용자 입력/UI·시각/성능·SampleScene의 실패 재현은 미실행이다. Editor 시작 장면 지정은 Play Mode 실행 근거가 아니다. 과거 F-022/티어 후속 Baker·도메인 분리 제어 입력 Play Mode·F-021/F-041/F-032 기록은 당시 범위로 보존하며 이번 실행에 합산하지 않는다. 주 시설 자동 부트스트랩·실제 드론 생성/이동·게임 UI/입력/카메라·실제 바닥 렌더링은 별도 후속이다.
