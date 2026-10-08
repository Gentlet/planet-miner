# F-015 — 레시피 정의 번호 검증

작업일: 2026-10-09. 사용자 확정 정책에 따른 코드 변경·컴파일 확인과 입력 반례의 실행 검증을 구분한다.

## 확정 정책

- 정의 ID는 현재 int 범위의 명시적 양수이며 전체 입력 안에서 유일해야 한다. 번호 생략 시 DTO의 0, 명시적 0·음수, 내용이 같더라도 중복 ID는 오류다.
- 번호를 자동으로 붙이거나 앞/뒤 정의를 우선 선택하지 않는다. 입력 행 순서는 보존하고 유일한 ID로 선택하는 제작법은 행 순서와 무관하다.
- 서로 다른 ID의 동일 생산품은 정상이다. 생산품으로 찾는 보조 함수의 우선순위는 이번 범위에 추가하지 않는다.
- 제품 전체 초기화의 필수 ID 1~5, 격리 최소 게시 API의 범위, 0 이하 변경 요청의 명시적 해제, 설정 1회 게시·World 소유·불변 수명은 유지한다.
- 제품 로드 오류는 기존 로그·레시피 설정 전체 미게시·즉시 SimulationFatalError로 시뮬레이션을 차단한다. 자동 기본값/재시도·런타임 교체·복구는 추가하지 않는다.

## 사전 분석과 레거시 근거

- Serena initial_instructions를 읽고 대상 절대 경로로 activate_project를 수행했다. 구현 시작에는 get_current_config로 planet miner/C# ready를 확인했으며 find_symbol과 진단을 사용했다.
- 최신 Task F-015, Q06 원본, F-011/F-013/F-014/F-008과 2026-10-06 전역 검토의 직접 관련 부분을 확인했다. 품목·중복 재료·필수 번호의 존재·1회 게시 검사가 정의 번호의 양수/유일성을 대신하지 않음을 현재 소스로 대조했다.
- 현재 레거시 파일은 없어 삭제 전 커밋 f85d2cad69219329c72692d5d4309a3cb8204a6b를 git show/grep로 확인했다. CrafterConfigParser.ParseRecipes는 0 이하 번호를 원본 행 위치+1로 바꾸고 중복 번호에 오류를 남긴 뒤 후행 정의를 건너뛰었다. CrafterConfigLoadSystem은 남은 정의를 시작에 게시했다.
- CrafterRecipeBufferExtension.TryFindRecipe는 생산품 종류의 첫 일치다. 실제 CrafterRecipeChangeSystem/CrafterSystem/DroneItemDestinationUtility와 설정 복사 요청이 생산품 선택을 사용했고, 찾은 번호는 재료 연결·연구 해금에 사용됐다. 자동 번호와 중복 건너뛰기는 코드로 확인했지만 이를 V2에서도 유지할 기획이나 명시적 우선순위 설명은 확인하지 못했다. 작업 트리 복원은 하지 않았다.

## 구현과 책임

- C# 변경은 RecipeConfigLoader 한 파일이다. ValidateConfig와 ValidateRegisteredConfig가 각각 입력 전체의 임시 HashSet<int>를 만들고 같은 내부 ValidateRecipeId를 사용한다. 0 이하와 중복은 번호를 포함한 ArgumentException으로 거부한다. 품목/출력/재료 검증과 구분하며 생산품의 유일성 검사는 추가하지 않는다.
- ParseJson은 기존 ValidateConfig를 거쳐 반환하고 PublishConfig도 기존 Registry 중복 거부 후 전체 검증을 마쳐야 새 엔티티를 만든다. 후반 중복도 부분 Registry를 만들지 않는다. 게시 복사 실패 시 이번 호출의 미완성 엔티티만 회수하는 경로는 그대로다.
- RecipeInitSystem은 수정하지 않았다. InitializeRecipeRegistry의 기존 예외 처리→ReportFailure→로그/Fatal을 사용한다. 자동 Init의 사전 등록도 기존 ValidateRegisteredConfig/ArgumentException 처리로 차단하며 이미 등록된 원본을 삭제·수정·보충하지 않는다. 직접 Loader API는 기존처럼 예외를 전달하고 스스로 Fatal을 게시하지 않는다.
- ValidateRequiredRecipes/ValidateRequiredRecipeIds는 제품 전체의 필수 1~5 존재 검사로 유지한다. 명시적 ParseJson/PublishConfig에 전체 필수 목록을 요구하지 않는다.
- 조회·Command·Decision·Execution·개발용 Validator와 시스템 실행 순서/소유권은 변경하지 않았다. 소비자가 Init 내부 필드나 별도 원본을 읽는 결합은 없다. 새 시스템·컴포넌트·Utility 클래스·영속 캐시·Registry·오류 경로·partial 파일·Config/에셋 변경은 없다.
- AGENTS.md, Production 컴포넌트 문서, Task의 F-015 정책/현재 상태와 집계를 갱신했다. 과거 평가는 원래 실행 기록으로 유지한다.

## 확인 결과

- 코드로 확인: 번호 누락은 기존 DTO의 0으로 남아 검증에서 거부된다. 두 최초 입력 경계와 사전 등록은 모두 양수/유일성을 검사한다. 유일한 ID의 행 순서 교환은 ID 조회 결과를 바꾸지 않고 서로 다른 ID의 동일 생산품은 이 검사에 걸리지 않는다. 이 설명은 개별 입력의 실행 재현 결과가 아니다.
- Serena Loader 진단은 오류/경고가 없었다. LSP 진단은 Unity 컴파일의 대체 근거로 사용하지 않는다.
- dotnet build Assembly-CSharp-Editor.csproj --no-restore --verbosity quiet /nologo: 종료 코드 0, 경고 0·오류 0. 런타임/Editor C# 빌드이며 원본은 Logs/Codex/F015-20261009/msbuild.log다.
- 샌드박스의 Unity status는 STATUS_NO_INSTANCES였고 pipeline list는 대상 Editor 실행·Pipeline 설치·Safe Mode 아님·reachable server 0을 보고했다. 이는 Editor 부재나 컴파일 실패로 판정하지 않았다. 단일 editor_status를 샌드박스 밖에서 실행하자 대상 Editor ready 연결을 확인해 같은 Editor에서 검증했다. CLI/Pipeline 업데이트·Editor 설치/재시작은 하지 않았다.
- Unity command recompile --focus false와 최종 recompile_status: up_to_date, failed:false, errors:[], compilationFailed:false. 최종 Editor는 ready, compiling:false, domainReloadInProgress:false, playMode:stopped다. 접수 결과만으로 통과시키지 않았다.
- 최신 런타임 소스 UTC 2026-10-08 16:31:57.5537929, Assembly-CSharp.dll UTC 16:33:22.9020184로 최신성을 확인했다. 최신 Editor 소스 UTC 10:20:40.1875759, Assembly-CSharp-Editor.dll UTC 16:15:03.7228086로 Editor 어셈블리도 해당 소스보다 최신이다.
- 변경 범위 git diff --check는 통과했다. 원본 연결/컴파일/어셈블리 기록은 Logs/Codex/F015-20261009/에 보존한다. Git의 LF→CRLF 정규화 안내는 공백 오류와 구분했다.

## 실행 한계와 완료 범위

- 사용자 지시에 따라 새 테스트·TestCase·assertion 보강은 추가하지 않았다. 이번 작업은 코드 분석·.NET/Unity 컴파일 확인으로 종료하며 EditMode/Play Mode와 eval 기반 입력 검증은 실행하지 않았다.
- 누락/0/음수/중복, 중복 행 순서 교환, 서로 다른 번호의 동일 생산품, 직접 게시/사전 등록의 오류 전달·원본 보존은 개별 실행 재현하지 않았다. 실제 SubScene 베이킹·입력·시각·성능도 미검증이다.
- F-008의 48/48, F-011의 79/79, F-014의 32/32와 F-007/F-013의 이전 빌드/대기 기록을 이번 번호 정책의 실행 통과로 합산하지 않는다. 다른 이슈의 완료 상태 재평가는 이번 범위에 포함하지 않는다.
- F-015 한 건만 구현·컴파일 확인 완료로 반영한다. Task는 35/43 완료·8개 미완료이며 다른 체크 상태와 과거 실행 통계를 유지한다.

## 2026-10-09 관련 이슈 후속 정리

위 완료 범위는 F-015 구현 단계 당시 기록이다. 이후 사용자의 관련 이슈 정리 요청에 따라 현재 Task·소스·기존 원본 컴파일 결과를 대조했다. 이번 후속은 문서만 변경하며 기존 C#/테스트/Config와 F-015 변경을 보존한다. 새 컴파일·테스트·eval·Play Mode 실행은 없다.

- **F-007 — 공유 컴파일로 완료:** ItemConfigInitSystem의 전체 명시 품목·정식 이름·None/중복·양수 한도와 사전 등록의 인덱스/내부 None/0 검사, 일반 입고 예약의 빈 슬롯 이전 0 이하 방어를 현재 소스로 확인했다. 이 소스들은 같은 Assembly-CSharp.csproj에 포함돼 있고 확인된 최신 Unity DLL보다 오래됐다. 원본 컴파일/Editor 준비/어셈블리 최신성으로 남은 Unity 컴파일 대기를 해소해 코드·컴파일 기준으로 완료했다. 무효 한도의 실제 입고/Owner와 입력 실패 연쇄 실행은 미수행이다. [F-007 완료 근거](F007-Verification.md#2026-10-09-f-015-공유-컴파일-확인).
- **F-013 — 공유 컴파일로 완료:** 현재 RecipeConfigLoader/RecipeInitSystem의 정식 품목·필수 주생산품·출력 역할/양수 수량·사전 등록 검증이 같은 최신 .NET/Unity 컴파일에 포함됐다. F-015는 여기에 번호 검사만 추가했고 품목 검사를 유지한다. 이전 후속의 남은 필수 Unity 컴파일을 충족해 완료했다. 품목 오류→미게시/Fatal→재료 보존의 실행 재현은 미수행이다. [F-013 완료 근거](F013-Verification.md#2026-10-09-f-015-공유-컴파일-확인).
- **F-008 — 기존 완료 유지:** Registry 입력 처리 전 재등록 거부·전체 검사 뒤 생성·이번 호출의 미완성 엔티티만 회수·사전 등록 원본 보존·World 소유/불변 수명을 유지한다. 중복 정의 번호 거부를 런타임 교체 지원으로 확대하지 않는다. Reader 수명/World 종료의 새 실행은 없다.
- **F-009 — 선택 검증 종료 유지:** 번호 누락/0/음수/중복도 기존 초기화 실패 경로를 사용하지만 번호 오류·파일 부재/읽기/일반 파싱의 독립 실행 통과가 추가된 것은 아니다. 새 테스트/assertion·삭제된 기본값 테스트 복원을 필수로 되살리지 않는다.
- **F-011 — 실패/필수 목록 계약 유지:** 필수 1~5의 존재와 정의 번호의 양수/유일성은 별도다. 추가 정의 오류도 기존 미게시/로그/Fatal과 사전 등록 원본 보존을 사용하며 격리 최소 게시에는 필수 전체 목록을 강제하지 않는다. 새로운 시작 게이트·오류 경로·fallback/재시도는 없다.
- **F-014 — 재료 계약 유지:** 입력 전체의 ID 유일성과 레시피 내부 재료 품목 유일성은 별도 검사다. 전체 재료 선확인·선소비와 기존 중복 재료 거부를 유지했다. 서로 다른 ID의 동일 생산품은 정상이다.
- **F-031/F-037 — 기존 예약·제작 연결 완료 유지:** 공유 컴파일에 F-007의 원본 한도 조회/예약 API와 현재 제작 코드가 포함되지만 입고 예약 알고리즘·Crafter 생성 구성·전용 슬롯·실제 제작 연결은 F-015에서 변경하지 않았다. 새로운 Owner/제작 흐름 실행·실제 Baker/SubScene 검증으로 집계하지 않는다.
- **F-040 — 변경 요청 계약 유지:** 정의 ID의 0/음수 거부는 NewRecipeId 0/음수 해제를 바꾸지 않는다. 준비된 Registry에 없는 양수 요청은 상태 변경 전에 거부하고 기존 Progress/Active·Stored/Product·Owner·필터를 보존한다. 요청/조회/상태 적용은 변경하지 않았다.
- **F-006/F-017 — 남은 범위 유지:** 정의 번호 오류의 시작 차단은 유효한 설정에서 생긴 ProductResult의 프리팹 누락/부분 생성·Clear 정책이나 월드 수치의 정수 산술을 해결하지 않는다. 생산품 우선순위·새 설정 기능도 포함하지 않는다.

공유 컴파일 근거는 앞 절의 실제 원본 recompile-status.json·editor-status-final.json·assembly-freshness.json·msbuild.log다. F-007/F-013 관련 소스가 프로젝트 Compile 항목에 포함되고 해당 DLL보다 오래됨을 확인했으며, .NET 통과를 Unity 통과로 바꾸거나 현재 상태를 새 컴파일 명령으로 보고하지 않았다. 원본 48/48·79/79·32/32의 실행 통계는 유지한다.

이번 후속의 추가 완료는 **F-007/F-013 2건**이다. Task는 **37/43 완료·6개 미완료**이며 나머지 체크 상태는 유지한다. 새 테스트/assertion은 필수 조건이 아니고 입력 반례와 실제 게임 실행의 미검증 범위를 문서에서 유지한다.

문서 정리 전후에 비교한 관련 C#·테스트 helper·Item/Recipe JSON 10개는 SHA256 **10/10 동일**했다. Task 체크는 37개 완료/추적 종료·6개 미완료로 일치하며 남은 항목은 F-006/F-017/F-022/F-025/F-032/F-041이다. 새 문서 링크의 대상/후속 heading과 변경 문서의 git diff --check를 확인했다. 기존 F-015 소스 변경과 원본 컴파일 로그는 보존했고 이번 후속에서 새 컴파일·테스트·커밋은 하지 않았다.
