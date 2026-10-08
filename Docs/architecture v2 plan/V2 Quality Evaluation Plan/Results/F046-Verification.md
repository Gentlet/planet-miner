# F-046 테스트 진단 로그의 실행별 격리와 보존

작업일: 2026-10-08. [Q29](Q29.md)와 [2026-10-06 재검토](CurrentCodeReview-2026-10-06.md)의 공용 삭제 위험을 현재 소스에서 확인하고 수정했다. 실제 과거 파일 삭제 피해는 조사하거나 재현하지 않았다. 수정 전 테스트는 실행하지 않았다.

> 최종 후속 상태: Unity 컴파일과 선택 EditMode 4/4, 공용 원본 8개 보존 및 성공·제어 실패 실행의 로그 보존을 확인해 F-046를 완료했다. 초기 연결 실패와 문서 영향 검토는 아래에 당시 기록으로 남기며 최신 근거는 [최종 검증 절](#2026-10-08-최종-unity-및-로그-보존-검증)을 따른다.

## 현재 소스 확인과 선택

- 수정 전 `Phase2BeltIntegrationTests.SetUp/TearDown`은 `CleanLogDirectory`를 호출해 공용 `Logs/InvariantErrors/invariant_error_*.txt` 전체를 삭제했다. 정상 사례와 간격 위반 사례의 파일 검사도 같은 공용 경로를 읽었다.
- Validator의 출력 경로는 `OnCreate`에서 정한 인스턴스 필드였고 외부 설정 API는 없었다. 기존 F-045의 World별 세션 GUID·독립 보고 순번·`FileMode.CreateNew`와 파일 Writer를 재사용했다. 새 진단 시스템·sink·캐시·병렬 기록 구현은 만들지 않았다.
- 세션 파일명으로 공용 폴더의 자기 파일을 골라 정리하는 방법보다, 해당 Validator 인스턴스의 출력 경로를 바꾸는 작은 API와 사례별 전용 디렉터리가 기존 파일 검사에도 직접 적용된다. 파일 삭제는 필요하지 않으므로 성공·실패 모두 보고를 보존한다.

## 변경과 소유권

- [WorldInvariantValidationSystem.cs](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Scripts/Validation/WorldInvariantValidationSystem.cs>)에 `SetLogDirectory(string)`를 추가했다. null과 빈 경로를 각각 거부하고 절대 경로로 정규화한 뒤 해당 인스턴스의 `_logDirectory`만 변경한다. 디렉터리 생성·실제 파일 기록은 기존 `ReportViolation`이 소유한다. 기본 공용 출력, 위반 수, 세션 ID, 보고 순번, CreateNew, 기록 실패 처리와 Debug.Break는 유지한다.
- [Phase2BeltIntegrationTests.cs](<C:/Projects/unity/PlanetMiner/planet miner/Assets/Editor/Tests/Phase2BeltIntegrationTests.cs>)는 SetUp마다 `Logs/Tests/Phase2BeltIntegrationTests/<실행 GUID>`를 새로 지정하고 NUnit 출력에 경로를 남긴다. 기존 파일 검사와 assertion은 해당 사례의 경로를 읽는다. 예를 들어 첫 실행의 보고가 남아 있어도 두 번째 실행은 다른 GUID 경로를 읽고 기록한다.
- 공용 패턴 삭제 helper와 TearDown override를 제거했다. 상속한 `EcsWorldTestFixture.TearDown`은 World와 ECS 참조만 해제한다. 성공·assertion 실패·수동 중단 뒤에도 테스트 보고를 삭제하지 않으므로 실패 근거가 남는다. 정상 실행에서는 기존 Writer가 호출되지 않으면 빈 로그 디렉터리도 만들지 않는다.
- Phase3StorageInvariantTests를 복원하지 않았고 새 테스트·assertion 보강·게임플레이 변경·시스템 그룹/Job/ECB 변경은 없다. 정리·이동·덮어쓰기 검증을 위해 실제 공용 로그나 기존 파일을 sentinel로 사용하지 않았다. 파일 삭제 명령을 실행하지 않았다.
- 직접 관련 CodeMemory의 Validator 설명과 C# 파일 색인을 갱신했다. AGENTS의 ECS 소유권·단계·검증 규칙은 바뀌지 않아 추가 갱신이 필요하지 않았다. 과거 Results와 다른 이슈 기록은 보존한다.

## 최초 검증 결과

- **코드 확인:** 해당 fixture에서 File.Delete·Directory.Delete·CleanLogDirectory·공용 InvariantErrors 경로가 제거됐다. SetUp은 인스턴스별 경로 설정, TearDown은 기존 World 해제만 수행하며 Validator에는 파일 삭제 경로가 없다. F-045의 보고 생성 부분은 수정하지 않았다.
- 변경 범위의 `git diff --check`는 통과했다. Git의 LF→CRLF 안내는 있었으며 코드/문서 공백 오류는 없었다. 시작 작업 트리는 clean이었고 최종 변경은 두 C# 파일, 직접 관련 CodeMemory 두 파일, Task와 이 기록으로 한정했다. 실제 Task 체크 집계는 43개 중 완료 28개·미완료 15개다.
- **C# 빌드 통과:** `dotnet build Assembly-CSharp-Editor.csproj --no-restore --verbosity quiet /nologo`가 종료 코드 0, 경고 0·오류 0으로 완료됐다. 프로젝트 참조의 런타임과 Editor/기존 테스트 코드를 컴파일한 MSBuild 결과다. Unity Editor 재컴파일·Burst·EditMode 실행 결과는 아니다. [원본 빌드 로그](<C:/Projects/unity/PlanetMiner/planet miner/Logs/Codex/F046-20261008-182e2410/msbuild.log>).
- **Unity CLI 확인 미완료:** 프로젝트 검증 래퍼는 `-CompileOnly -TestFilter @()`로 컴파일 전용 모드를 지정했지만 `STATUS_PIPELINE_LOAD_PENDING`으로 컴파일을 시작하지 못했다. 직접 recompile도 `COMMAND_FAILED / No Pipeline instance found`를 반환했다. Editor 프로세스 실행은 확인했지만 CLI에서 준비 상태·컴파일/리로드 종료를 확인하지 못했다. Pipeline discovery 파일 부재를 읽기 전용으로 확인했으며 패키지·Editor 설치·설정 변경이나 재시작으로 우회하지 않았다. [래퍼 결과](<C:/Projects/unity/PlanetMiner/planet miner/Logs/Codex/F046-20261008-182e2410/compile-wrapper.json>), [직접 재컴파일 결과](<C:/Projects/unity/PlanetMiner/planet miner/Logs/Codex/F046-20261008-182e2410/recompile-start-initial.json>), [인스턴스 진단](<C:/Projects/unity/PlanetMiner/planet miner/Logs/Codex/F046-20261008-182e2410/pipeline-list.json>).
- **실행 미확인:** 임시 경로의 sentinel 보존, 두 fixture 실행의 독립 보고, assertion 실패 후 보고 보존은 실제 fixture 실행으로 확인하지 못했다. 별도 임시 검증·선택 EditMode·전체 EditMode·Play Mode를 실행하지 않았다. 실행하지 않은 이유는 Unity 연결과 수정된 테스트 어셈블리의 로드 상태를 확인하지 못했기 때문이다. 코드상 삭제가 없다는 확인과 C# 빌드 성공을 파일 보존 실행 증거로 바꾸지 않는다.

## 최초 검증 뒤 남은 범위와 Task 상태

- 구현은 반영했지만 Unity CLI의 최종 컴파일 확인과 보존 동작 실행 근거가 남아 있어 F-046의 완료 체크를 하지 않고 미완료로 둔다. 실제 체크 상태에 따른 진행 집계는 변하지 않는다.
- 연결이 복구되면 runtime/test 어셈블리 최신성과 Editor ready·비컴파일·비리로드 상태를 확인한 뒤 가장 작은 관련 기존 테스트 또는 테스트 전용 위치의 임시 검증으로 남은 보존 질문을 확인한다. 수정 전 공용 삭제 코드는 재실행하지 않는다.
- 보존한 보고는 자동으로 삭제하지 않아 반복 위반 실행 시 테스트 로그가 누적된다. 자동 보관 기한이나 새 정리 정책을 추가하지 않았다. 저장장치 쓰기 실패 때 완전한 보고 보존·재시도까지 보장하는 변경은 아니며 기존 F-045의 예외 처리 범위를 유지한다.

## 2026-10-08 직접 관련 이슈 영향 정리

이 절은 현재 소스와 편집 직전 최신 Task를 확인한 문서 영향 정리다. 이후 사용자가 승인한 최종 실행 검증은 아래 절에 따로 기록하며 최초 검증 결과를 덮어쓰지 않는다. 과거 Q29·전역 코드 검토·F-044/F-045의 2026-09-30 기록은 당시 근거로 보존했다.

- **F-044:** Phase2 fixture가 공용 진단 파일을 정리하는 원인은 제거됐다. 동일 Validator에 전용 경로를 지정할 수 있으므로 향후 위반 검증을 격리하기 위해 검사 시스템을 다시 만들 필요가 없다. 소유 버퍼 검사 로직·위반 count·기존 assertion은 바뀌지 않았다. 중복/dead/wrong-owner 참조·정상 현장·성능의 정확성은 이번 수정으로 새로 검증되지 않았고, 기존 완료 상태와 실행 한계를 유지한다.
- **F-045:** 테스트 경로 격리에 기존 세션 ID·보고 순번·CreateNew Writer를 재사용했다. 로그 파일명·식별자·sink를 새로 설계하는 작업은 필요하지 않다. 최종 후속에서는 동일 이름의 두 fixture World가 다른 실행 경로·보고 세션으로 기록하고 먼저 만든 보고를 보존한 제한된 사례를 확인했다. 같은 시각의 여러 보고·카운터 초기화·쓰기 실패 보존은 여전히 미검증이며 다중 World 전 조합으로 확대하지 않는다. 기존 완료 상태를 유지한다.
- **F-046:** 삭제 원인은 제거됐고 출력/파일 검사는 실행별 경로로 좁혀졌다. 문서 영향 검토 당시 남아 있던 Editor 컴파일·어셈블리 로드·기존 파일 보존·두 실행의 독립 보고·실패 후 보고 보존은 아래 최종 검증에서 확인했다. 정책 결정이나 새 로그 구현을 기다리는 항목은 아니며 완료 체크를 반영했다.
- 출력 경로를 설정하는 현재 호출자는 Phase2BeltIntegrationTests뿐이다. 다른 Validator fixture의 출력까지 자동으로 격리하거나 그 테스트의 assertion 문제를 해결한 것으로 확대하지 않는다. F-003의 소유 인계, F-026의 Fence 완료 및 제외된 assertion 보강 이슈는 이번 파일 부수효과 수정으로 해결되지 않으므로 상태를 바꾸지 않았다.
- 문서 영향 검토 시점은 43개 중 33개 완료/추적 종료·10개 미완료였다. 이후 최종 검증으로 F-046만 한 건 추가해 34개 완료/추적 종료·9개 미완료로 갱신했다. 다른 대화의 기술 종료 5건과 F-044/F-045의 기존 완료는 보존하며 다시 합산하지 않는다. 앞 절의 28/15는 최초 구현 기록 당시 집계이며 현재 진행 현황으로 사용하지 않는다.

## 2026-10-08 최종 Unity 및 로그 보존 검증

사용자의 추가 승인으로 기존 연결 복구·Editor 컴파일·선택 EditMode·임시 보존 검증을 수행했다. 프로젝트 소스·기존 테스트 assertion·장면·설정·Editor/패키지 설치는 추가로 변경하지 않았다. 원본 명령/스냅샷/임시 스크립트는 `Logs/Codex/F046-20261008-followup-af22cd7f/`에 보존한다.

### 연결과 컴파일

- 초기 재확인도 STATUS_PIPELINE_LOAD_PENDING이었고 실행 중인 Editor와 discovery 파일 부재를 확인했다. Computer Use 스킬로 기존 Editor를 선택·활성화했다. 첫 활성화는 시간 초과였으며 현재 창을 다시 선택한 한 번의 재시도 후 CLI가 port 7800의 해당 프로젝트를 ready로 발견했다. 설치·업그레이드·재시작·프로젝트 설정 변경은 하지 않았다.
- CLI editor_status는 처음 playing/paused로 표시했지만 실제 Unity API는 isPlaying:false/isPaused:true/isPlayingOrWillChangePlaymode:false였다. isPlaying=false 접수만으로 표시는 바뀌지 않았고, 남은 isPaused 플래그를 해제한 뒤 ready/stopped를 확인했다. 새 Play Mode를 시작하거나 진행하지 않았다. [실제 플래그](<C:/Projects/unity/PlanetMiner/planet miner/Logs/Codex/F046-20261008-followup-af22cd7f/editor-actual-play-flags.json>), [준비 상태](<C:/Projects/unity/PlanetMiner/planet miner/Logs/Codex/F046-20261008-followup-af22cd7f/editor_status-ready.json>).
- 프로젝트 래퍼의 CompileOnly가 성공했고 최종 recompile_status는 **up_to_date, failed:false, errors:[], compilationFailed:false**였다. 런타임 소스 07:33:10.1047289 UTC·테스트 소스 07:33:10.3995580 UTC보다 Assembly-CSharp.dll 07:57:50.3783739 UTC·Assembly-CSharp-Editor.dll 07:57:51.3752969 UTC가 모두 최신이다. MSBuild 결과로 대체하거나 up_to_date 이름만으로 통과시키지 않았다. [최종 컴파일 상태](<C:/Projects/unity/PlanetMiner/planet miner/Logs/Codex/F046-20261008-followup-af22cd7f/final-recompile_status.json>), [소스/어셈블리 시각](<C:/Projects/unity/PlanetMiner/planet miner/Logs/Codex/F046-20261008-followup-af22cd7f/assembly-times.json>).
- 최종 Editor는 **ready, compiling:false, domainReloadInProgress:false, playMode:stopped**였다. [최종 상태](<C:/Projects/unity/PlanetMiner/planet miner/Logs/Codex/F046-20261008-followup-af22cd7f/final-editor_status.json>).
- 최신 사용자 지침에 따라 Serena initial_instructions·절대 경로 activate_project를 수행하고 `find_symbol`로 SetLogDirectory/Phase2 SetUp, `find_referencing_symbols`로 SetLogDirectory의 실제 Phase2 호출을 확인했다. 온보딩·메모리 작성은 하지 않았다. 프로젝트 활성화가 생성한 `.serena/` 설정/인덱스는 도구 메타데이터이며 게임 코드 변경이 아니다.

### 선택 테스트와 보존 결과

- 수정된 fixture의 전용 경로와 공용 삭제 제거를 먼저 재확인했다. 관련 기존 **Phase2BeltIntegrationTests만 EditMode로 실행해 completed, total 4, passed 4, failed/skipped/inconclusive 0**을 확인했다. 전체 EditMode·Play Mode는 실행하지 않았다. [최종 테스트 상태](<C:/Projects/unity/PlanetMiner/planet miner/Logs/Codex/F046-20261008-followup-af22cd7f/final-test_status.json>).
- 실행 전에 공용 Logs/InvariantErrors의 원본 8개 파일을 읽기 전용으로 스냅샷했다. 선택 테스트와 임시 검증 뒤에도 8개 모두 존재하며 길이와 SHA256이 일치하고 공용 파일 수는 여전히 8개다. 원본을 sentinel/실패 주입 대상으로 삼거나 삭제·이동·덮어쓰지 않았다. [실행 전 원본](<C:/Projects/unity/PlanetMiner/planet miner/Logs/Codex/F046-20261008-followup-af22cd7f/common-before.json>), [최종 보존 비교](<C:/Projects/unity/PlanetMiner/planet miner/Logs/Codex/F046-20261008-followup-af22cd7f/preservation-final.json>).
- 정상 Test Runner 실행의 간격 위반 탐지 사례가 만든 보고는 TearDown 후 `a16219dbef9a4af5b4301683d825f3a7` 경로에 남았다. 별도 임시 fixture 실행은 새 `cc5bd658cca84c80a9c6e415d1799d87` 경로를 사용했고, 이후에도 먼저 만든 보고의 SHA256은 변하지 않았다. 두 World 이름은 Phase2BeltIntegrationTests로 같지만 보고 세션은 `ed7d627ccc0f4b7e95750977705461c7`과 `08fa80621437431eb7c77a42cb7c51f7`로 달랐다. [두 World의 보고 식별](<C:/Projects/unity/PlanetMiner/planet miner/Logs/Codex/F046-20261008-followup-af22cd7f/two-world-reports.json>).
- 임시 검증은 실제 기존 fixture의 SetUp·Test04·상속한 TearDown을 호출했다. 신규 GUID 경로가 전용 테스트 루트 안인지 확인하고, 기존 디렉터리가 없을 때만 CreateNew로 임시 sentinel을 먼저 만들었다. 기존 Test04의 진단 보고 생성 후 제어된 NUnit AssertionException을 발생시켜 finally에서 실제 TearDown을 실행했다. sentinel의 내용과 보고의 SHA256은 그대로였고 World 해제도 확인했다. 기존 NUnit 문맥은 복원했다. **예상 실패 관측·sentinel 보존·보고 보존·World 해제 모두 true**다. 새 NUnit 테스트를 등록하거나 기존 assertion을 바꾸지 않았으며 이 직접 호출을 선택 테스트 4건의 실패/통과 통계에 합산하지 않는다. [최종 임시 결과](<C:/Projects/unity/PlanetMiner/planet miner/Logs/Codex/F046-20261008-followup-af22cd7f/retention-probe-final.json>), [실행한 임시 스크립트](<C:/Projects/unity/PlanetMiner/planet miner/Logs/Codex/F046-20261008-followup-af22cd7f/retention-probe.cs>).
- 최초 임시 스크립트는 NUnit 문맥 setter의 접근 수준 때문에 eval 컴파일에 실패해 fixture를 실행하지 못했다. 현재 바이너리 API를 확인하고 임시 스크립트의 문맥 설정/복원만 고친 뒤 위 최종 결과를 얻었다. 초기 실패 로그/스크립트도 보존했다. [초기 컴파일 실패](<C:/Projects/unity/PlanetMiner/planet miner/Logs/Codex/F046-20261008-followup-af22cd7f/retention-probe-result.json>).

### 완료 판정과 한계

Editor 컴파일·선택 테스트·기존 파일 보존·독립 실행·제어된 실패 뒤 실제 TearDown의 보존 기준을 충족해 F-046를 완료했다. 편집 직전 최신 Task를 확인해 해당 절과 관련 F-044/F-045 설명·링크, P2/전체 진행 집계만 갱신했다. F-044/F-045는 기존 완료를 유지하며 새 완료는 F-046 한 건이다. 과거 평가·초기 실패·다른 대화의 기술 종료 기록은 보존한다.

실패 종료 확인은 임시 NUnit 문맥에서 제어된 assertion 실패를 거친 실제 fixture 호출이며, Test Runner 자체를 실패시키거나 프로세스를 강제 종료한 검증은 아니다. 저장장치 쓰기 실패·강제 종료·자동 보관 기한·모든 Validator fixture의 경로 격리·F-044 검출 전 조합·F-045 같은 시각/카운터 초기화 전 조합은 확인하지 않았다. 확인한 두 보고는 다른 시각에 생성됐다. 컴파일/EditMode를 게임 입력·시각·성능·Player 빌드의 검증으로 확대하지 않는다.
