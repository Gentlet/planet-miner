# F-022 건물 크기 원본과 프리팹 DB 편집 계약

날짜: 2026-10-11. 사용자 결정 구현·코드 확인·Unity 컴파일·선별 EditMode·격리 실제 Baker 실행 완료. 원본 Q09와 후속 기록은 당시 근거로 보존한다.

현재 후속: 같은 날 종류·티어 지원과 별도 표시 이름을 구현하고 건물 Resources 자동 탐색을 제거했다. 아래 InspectorList/ResourcesAutoLoad 두 모드와 59건은 F-022 당시 실행 근거다. 후속의 현재 계약·111건과 단일 명시 등록 Baker 검증은 [별도 기록](BuildingTierAndDisplayName-Verification.md)을 따른다.

## 결정과 편집 흐름

- 건물 크기는 Assets/Resources/Config/BuildingConfig.json의 footprint에서만 편집한다. 같은 건물 종류의 크기는 고정한다. 티어 구분과 건물명 별도 지정은 후속 구현이며, 같은 종류·같은 티어의 고정 크기와 티어별 다른 크기를 고려할 조건만 남긴다.
- 모든 건물에 양수 두 축을 명시한다. 생략/null·빈/다른 길이 배열·소수·문자열 숫자·int로 표현할 수 없는 정수·0·음수는 오류다. Init은 기존 로그·설정 미게시·SimulationFatalError로 시뮬레이션을 차단한다. 사전 등록 오류는 원본을 보존한 채 차단한다. 종료·재시도는 추가하지 않는다.
- 프리팹 목록과 BuildingPrefabElement의 Size/FootprintSize를 제거했다. Inspector는 설정 파일의 편집 위치를 안내한다. Resources 자동 목록/베이킹에서도 기본 규격 표를 제거했고 PrefabLookupUtility는 프리팹만 반환한다.
- 배치 요청의 FootprintSize는 Config를 읽어 미리보기/사전 검사에 사용한 스냅샷이다. Config와 다른 값은 InvalidFootprint로 거절하고 승인 셀을 남기지 않는다. 직접 생성의 int2.zero만 생략이며 Config를 사용한다. 그 외 입력이 Config와 다르면 로그·생성 거절이다. 명시 요청이 설정을 덮어쓰지 않는다.

## 소유권과 실행 경계

Config Loader가 크기의 유일한 원본을 게시한다. JSON 원본의 숫자 형식은 이미 프로젝트가 참조하는 Newtonsoft.Json의 토큰으로 검사한다. 다른 설정 필드는 기존 JsonUtility 경로를 유지한다. 소수/문자열 숫자가 JsonUtility에서 정수로 보정되는 것을 실제 확인하여 이 검사를 추가했으며 새 패키지·설정 파일·런타임 크기 캐시는 만들지 않았다. int 표현 검사는 필드 형식에 관한 것이며 R-01의 면적/좌표 산술 상한을 해결한 것으로 해석하지 않는다.

Command는 기존 설정 NativeArray 스냅샷에서 크기와 해금을 함께 조회한다. 검증된 후보의 기본 크기를 EndCommand에서 현장에 저장한다. Construction은 현장의 크기를 공통 Spawn에 전달하며 Config 일치 확인 뒤 동일한 크기로 EndBuilding 생성한다. 실패하면 기존 현장/자재를 보존한다. 직접 생성도 같은 Config 크기를 사용한다. Synchronization·채굴·출고 등 기존 Reader는 인스턴스 크기를 읽고 방향을 한 번 적용한다.

BuildingFootprintUtility의 회전 계약, F-041 설치 표식/접수 순서, F-032 원본 자격, 세 ECB 경계와 공간 Fence는 유지했다. 직접 생성에 배치 충돌 검사나 공사 비용 납부를 새로 추가하지 않았다. 새 시스템·컴포넌트·Utility 클래스·partial 파일 분리는 없다.

기존 최소 fixture는 제품 기본값 API 대신 TestEntityFactory의 명시적 fixture 크기를 사용한다. 비정사각형 배치 사례는 같은 Storage 종류의 모든 후보를 같은 2×3 설정으로 맞췄고 기존 회전·접수 순서 검사 의도를 유지했다. DB 생성자의 제거된 인수와 목록 Size assertion도 새 데이터 계약에 맞췄다. 새 회귀는 실제 확인한 JSON 보정/누락 오류, 무효 사전 등록의 Fatal, 요청 크기 불일치가 후속 배치를 선점하는 위험을 검사하는 좁은 사례다. 테스트 보강을 일반 착수 조건으로 삼은 변경은 아니다.

## 검증 결과

- Unity 컴파일: completed, failed:false, errors:[], compilationFailed:false. Editor는 ready, compiling:false, domainReloadInProgress:false, Play Mode stopped이며 실행/테스트 DLL 최신성을 확인했다. [컴파일](../../../../Logs/QualityImprovement/F022/compile-current-status.json), [Editor](../../../../Logs/QualityImprovement/F022/editor-current-state.json), [어셈블리](../../../../Logs/QualityImprovement/F022/assembly-freshness.json).
- 최종 선별 EditMode는 설정/목록 12, 직접 생성 11, 배치 24, 완공 8, 기존 공사 통합 4로 총 59/59다. 실패/Skipped/Inconclusive는 0이다. 서로 다른 시점의 같은 사례를 중복 합산하지 않고 각 필터의 최종 유효 실행만 계산했다. [최초 실행](../../../../Logs/QualityImprovement/F022/verification-status.json), [완공 최종](../../../../Logs/QualityImprovement/F022/completion-verified-status.json), [설정 최종](../../../../Logs/QualityImprovement/F022/config-tests-verified.json).
- 실제 Baker: 저장된 V2 Test Scene/sub.unity의 건물 Authoring과 실제 프리팹 참조를 PreviewScene에서 읽고, 독립 World에서 설치된 Entities의 실제 BakeGameObjects를 실행했다. InspectorList와 ResourcesAutoLoad 각각 DB 11항목을 베이킹했다. 그 산출물을 그대로 사용해 설정만 2×3/해금/빈 건축 자재로 명시한 격리 Storage 사례의 배치→현장→완공 4방향과 직접 생성 4방향(생략/일치 입력)을 실행했다. 각 모드에서 현장 24셀, 최종 8건물의 정확한 48셀, 기본 크기 2×3, 방향, 완공 ReceiptSequence 보존과 Fatal 부재를 확인했다. 두 모드는 EditMode 59건과 별도의 실행 근거다. [최종 결과](../../../../Logs/QualityImprovement/F022/baker-final-verified.json), [검증 스크립트](../../../../Logs/QualityImprovement/F022/BakerSpawnProbe.cs).
- 실제 DB 장면은 연결된 Editor로 저장했다. Git diff는 목록의 이전 Size 11줄 삭제뿐이며 종류·프리팹 참조·다른 장면 값은 유지했다. 저장 전 대상의 기존 미저장 변경이 없음을 확인하고 활성 장면을 복구했다. [저장 결과](../../../../Logs/QualityImprovement/F022/scene-cleanup-verified.json).

## 실패와 복구

첫 실행은 56건 중 55건 통과했다. 무효 목표 타입 None/ConstructionSite를 만드는 기존 완공 사례가 새 fixture 크기 helper에서 먼저 예외를 냈다. 그 사례만 명시적 1×1을 전달하도록 고쳐 원래의 무효 타입 검사에 도달시켰으며 완공 8/8을 재확인했다. 원본 실패를 보존했고 제품 크기 fallback을 복원하지 않았다.

추가 형식 확인에서 기존 JsonUtility가 [1.5,1]과 문자열 숫자를 int로 보정함을 발견했다. 원본 토큰 검증과 세 사례를 추가한 뒤 설정 12/12 및 실제 Baker 두 모드를 다시 확인했다. [보정 발견 기록](../../../../Logs/QualityImprovement/F022/fractional-input-check.json).

Editor 도메인 리로드 중 CLI 연결 reset/network 오류가 있었다. 제출 성공/연결 오류를 컴파일 결과로 해석하지 않고 재연결 후 completed·오류 없음·최신 어셈블리를 확인한 뒤 해당 테스트만 실행했다. 격리 베이킹 eval 스크립트의 초기 using/확장 메서드/반사 멤버명 오류도 스크립트에서 수정한 뒤 최종 실행에 성공했다. 이 초기 eval 오류를 프로젝트 C# 컴파일 실패나 실제 Baker 실패 재현으로 합산하지 않는다.

## 범위와 미검증

실제 자동 SubScene 시작·게임 Init의 파일 오류 장면 연쇄·전체 GameSimulationGroup·모든 종류의 실제 프리팹 Spawn을 실행한 것은 아니다. Play Mode·플레이어 입력/UI·시각적 크기/회전·Player 빌드·성능은 미실행이다. 이전 11/11이나 이번 실제 Storage Baker 사례를 그 범위의 증거로 확대하지 않는다.

그림 자동 배율/회전/중심 복원, 티어/건물명 기능, 다중 칸 벨트·분배기·합류기 기능, R-01 극단적 산술 상한, F-025 빌드 진입 장면, F-006 생산 결과 보존은 이번 범위가 아니다. 설정과 대상 종류/DB 누락 거절, 기존 빈 건축 자재, 완공 실패 보존은 유지한다. F-022 한 건만 완료로 반영하여 최신 Task는 40/43, 미완료 F-006/F-017/F-025다.

## 2026-10-11 관련 이슈 후속 정리

별도 사용자 요청으로 남은 세 이슈의 최신 Task·원본 평가·직접 관련 현재 소스와 위 F-022 실행 기록을 대조했다. 이 후속에서는 문서만 수정하고 새 코드·Config·에셋 변경이나 컴파일·테스트·베이킹을 실행하지 않았다. 추가 완료/종료는 0건이며 40/43·미완료 F-006/F-017/F-025를 유지한다.

- **F-006 판단 보류 유지:** 잘못된 건물 크기는 Init의 미게시/Fatal 경로로 거절되므로 그 입력에서 생산을 시작하는 경로는 좁혀졌다. 그러나 [ProductResultApplyJob.Execute](../../../../Assets/Scripts/Systems/Items/StateApply/ItemLifecycleApplySystem.cs)의 Item 프리팹 누락→Fatal 기록→continue→마지막 productResults.Clear는 그대로다. 이미 생산한 결과의 보존/소비와 부분 생성 정책은 사용자 결정이 남는다. 건물 생성 성공/현장 보존과 생산 결과 보존을 통합하거나 자동 복구/보상을 추가하지 않는다.
- **F-017 미완료 유지:** [WorldGenerationConfigLoader](../../../../Assets/Scripts/Config/WorldGenerationConfigLoader.cs)는 초기 크기/반경/바이옴 region·transition의 산술·실행량을 고려한 상한을 정하지 않는다. [ResourceGenerationUtility](../../../../Assets/Scripts/Chunks/ResourceGenerationUtility.cs)의 maxRadius+1·제곱 거리와 [FloorBiomeSampler.SelectTransitionBiomeIndex](../../../../Assets/Scripts/Chunks/FloorBiomeSampler.cs)의 transition×ChunkSize가 float 변환 전에 int로 계산되는 경로도 유지된다. F-022의 개별 건물 축 형식/표현 범위 검증을 월드 지원 범위 해결로 사용하지 않는다. 기존 로그·Fatal 전달 계약을 재사용하고 지원 범위/연산 순서만 별도로 결정할 수 있다.
- **R-01 별도 검토 유지:** BuildingConfig를 유일한 크기 원본으로 정했으므로 후속 크기 상한을 여러 DB/기본표에 맞출 필요는 줄었다. 하지만 50,000×50,000은 각 축이 양수 int이고 F-022 검증을 통과하며, [BuildingSpatialSyncSystem.OnUpdate](../../../../Assets/Scripts/Systems/Synchronization/BuildingSpatialSyncSystem.cs)의 size.x*size.y와 누적 requiredCapacity/×2는 int다. 2,500,000,000의 면적은 int 양수 범위를 초과한다. 이는 코드/산술 분석이고 큰 맵 생성이나 overflow 실행을 하지 않았다. 좌표·면적·실행량·현장 자재 합산은 기존 별도 범위다.
- **F-025 미완료 유지:** [EditorBuildSettings](../../../../ProjectSettings/EditorBuildSettings.asset)의 활성 진입은 SampleScene이다. [SampleScene/Sub](../../../../Assets/Scenes/SampleScene/Sub.unity)의 Building/Item/Resource script GUID가 현재 Authoring과 다른 것을 직렬화로 대조했다. Building의 구형 _entries/type/prefab/size와 enum 숫자 대응도 남는다. 현재 이관은 종류/프리팹만 복구하고 크기는 Config에서 읽는 계약을 따라야 한다. V2 입력의 실제 Building Baker 두 모드는 해당 계열 선택 시 재사용할 정상 경로 근거이며 SampleScene 수리·세 DB 전체 검증·자동 시작·Player 빌드 근거는 아니다. 진입 장면 선택은 임의로 하지 않았다.
- **F-021 기존 종료에 근거 보강:** 실제 InspectorList/ResourcesAutoLoad와 4방향 Storage 배치/완공·직접 생성의 선택 검증 범위를 [기존 종료 기록](TechnicalIssueClosure-2026-10-08.md#f-021--실제-베이킹-근거를-반영하고-나머지-선택-검증-추적-종료)에 연결했다. 수동 ECS 59/59와 실제 Baker 두 모드의 수를 구분한다. 모든 등록 타입 구성, 오류 Authoring/등록 전 조합, Item/Resource 전체 Bake, 지속 자동 회귀의 공백을 해소한 것으로 쓰지 않는다. Task의 F-011 당시 실행 설명에 남았던 '미완료 유지' 표현은 당시 검증 경계로 정리하여 현재 종료 상태와 구분했다.

이번 판정은 현재 소스/직렬화 확인과 기존 실행의 직접 적용 범위에 한정한다. 과거 Q03/Q07/Q11과 종료 기록에는 후속 안내를 추가하고 원본 평가·실행 수를 보존한다.
