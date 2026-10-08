# F-014 중복 재료 거부와 전체 재료 확인 후 소비

작업일: 2026-10-07. 현재 코드의 구현 기록과 실행하지 못한 검증을 구분한다. [Q06](Q06.md)과 [2026-10-06 재검토](CurrentCodeReview-2026-10-06.md)의 반례는 당시 코드 분석이며 실행 재현으로 바꾸어 해석하지 않는다.

## 확정한 정책

- 한 레시피의 같은 품목 재료 두 행은 합산하지 않고 설정 오류로 거부한다. [1,1], [2,3] 모두 재고와 무관하게 유효한 제작 입력이 아니다.
- 초기화 입력 실패는 해당 설정 전체를 게시하지 않고 오류를 로그에 남긴다. 즉시 SimulationFatalError를 게시해 기존 GameSimulationGroup의 전체 실행 차단을 사용한다. 앱 종료나 Editor Play Mode 종료는 추가하지 않는다.
- 모든 재료를 소비할 수 있을 때만 한 번에 선소비하고 제작을 시작한다. 부족하면 보관 버퍼·삭제 요청·진행도를 변경하지 않는다.
- 현재 수량 형식을 넘는 요구량을 거부한다. 중복은 합산 전에 거부하므로 중복 합계의 overflow가 제작 비용으로 사용되는 경로는 없다.

## 구현과 소유권

- `RecipeConfigLoader.ParseJson`은 JSON 재료 수량을 long으로 읽고 int 범위 밖이면 예외로 거부한다. 범위 안의 기존 비양수 수량 보정은 유지한다. 파싱과 공개 PublishConfig는 같은 중복 검증을 사용하며 Registry 생성 전에 실패한다. 직접 구성한 RecipeConfigData도 공개 게시에서 중복을 거부한다.
- `RecipeInitSystem.InitializeRecipeRegistry`는 입력 검증의 ArgumentException을 오류 로그와 즉시 SimulationFatalError로 바꾸고 Entity.Null을 반환한다. 자동 Init은 실패 후 또는 기존 중단 오류가 있으면 비활성화한다. 기존 Registry 재게시 거부는 입력 처리 전에 계속 수행한다.
- `RecipeConfigElement.TryFindIngredient`는 요청 품목이 유일하고 수량이 양수인 경우에만 수량을 반환한다. 중복/무효 수량은 false와 0으로 반환한다.
- 기존 `RecipeConfigLookupUtility.HasRequiredIngredients`를 Decision과 Execution이 공통 사용한다. 비활성 DestroyItemRequest가 있는 보관 실물만 소비 가능량으로 센다. 중복 재료 요구는 거부한다. 캐시나 새 시스템/컴포넌트는 만들지 않는다.
- Execution은 전체 재료를 최종 확인한 뒤에만 같은 조건의 Stored 참조를 제거하고 DestroyItemRequest를 활성화한다. IsCraftingActive/Progress 및 ProductResult Writer는 Execution, Status Writer는 기존 CrafterStateApply, 실제 아이템 삭제/생산물 생성은 기존 ItemLifecycle/EndBuilding을 유지한다.
- Command/EndCommand → Building Decision/Reservation/Execution/StateApply/EndBuilding → Drone → EndSimulation → Synchronization 순서와 Job Dependency 등록을 유지한다. 새 실행 순서 의존성은 추가하지 않았다. 입력 슬롯 수 계산은 BuildingInputSlotUtility를 수정하지 않고 재사용한다.

## 검증 결과

- 변경한 소스·테스트·문서의 `git diff --check`: 통과. 작업 전부터 있던 `planet miner.slnx` 변경과 기존 미추적 CurrentCodeReview 두 파일은 보존했다. 전체 diff 검사에서는 기존 slnx의 공백 경고가 있었으며 변경 범위 검사와 구분한다.
- `dotnet build Assembly-CSharp-Editor.csproj --verbosity quiet /nologo -p:RestoreIgnoreFailedSources=true`: 통과, 경고 0·오류 0. ProjectReference의 Assembly-CSharp와 Editor/테스트 C#을 컴파일한 MSBuild 결과이며 Unity Editor 재컴파일·Burst 실행·EditMode 실행 결과가 아니다. 최초 `--no-restore` 시도는 Editor 프로젝트의 project.assets.json 부재로 NETSDK1004가 났고, 복원 포함 빌드로 해결했다.
- Unity CLI status/pipeline list의 초기 진단은 해당 프로젝트 Editor가 실행 중이지만 Pipeline 연결이 준비되지 않은 상태였고, 최초 recompile도 `COMMAND_FAILED / No Pipeline instance found`로 시작하지 못했다. 이후 사용자가 정상 Editor 화면을 확인한 뒤 연결이 `ready`가 되어 최종 검증을 진행했다. 초기 실패는 로그로 보존하며 최종 성공과 구분한다.
- **Unity 컴파일 확인: 통과.** 최종 recompile/recompile_status는 `up_to_date`, `failed:false`, `errors:[]`, `compilationFailed:false`였다. Editor는 `ready`, `compiling:false`, `domainReloadInProgress:false`, `playMode:stopped`였다. 최신 C# 소스는 2026-10-07 14:39:54.647 UTC, Assembly-CSharp.dll은 14:41:38.812 UTC, Assembly-CSharp-Editor.dll은 14:41:38.665 UTC로 두 어셈블리 모두 최신 소스 이후였다. 단순 up_to_date 이름이나 오래된 DLL만으로 통과시키지 않았다.
- 기존 Phase5RecipeConfigTests에 중복 [1,1]/[2,3]/[int.MaxValue,1]의 직접 게시 거부·초기화 중단·자동 재시도 차단 3사례와 수량 2147483648의 거부 1사례를 추가했다.
- 기존 Phase5CrafterExecutionTests에 기본 드론 레시피의 뒤 재료 부족/정확/초과 재고 3사례를 추가했다. 부족 사례는 승인된 결정을 강제로 전달하여 Execution 자체의 무소비 방어를 확인하도록 구성했다. 소비 요청 수·실제 삭제·잔여·진행도를 함께 검사한다.
- **선택 EditMode: 32/32 통과.** Phase5RecipeConfigTests 7/7, Phase5CrafterExecutionTests 6/6, Phase5CrafterInputPipelineTests 19/19를 각각 연결된 Editor에서 실행하고 최종 completed와 통계를 확인했다. 실패/생략/Inconclusive는 모두 0이다. 추가 회귀 7사례가 포함돼 있다. 전체 EditMode가 아니며 Play Mode, 실제 SubScene 베이킹, 시각/입력 검증은 미실행이다.
- 원본 명령 로그는 `Logs/Codex/F014-20261007/`에 보존한다. `msbuild.log`와 `unity-recompile.json`은 초기 실패, `msbuild-restored.log`·`unity-recompile-ready.json`·`recompile_status.json`·`editor_status.json`과 각 `*-test-start.json`/`*-test-status.json`은 최종 컴파일/선택 실행 근거다.

## 적용 범위와 한계

- 재료 부족 시 무소비는 유효한 소유 버퍼의 실물 참조 유일성 계약을 전제로 한다. 손상된 버퍼의 중복 실물 참조·Owner 불일치 전체를 복구하는 변경은 아니다.
- 중복 재료의 정책과 재료 수량 범위만 다뤘다. F-013 품목 검증, F-015 ID 검증, F-010 공사 자재, 비유한 속도/시간과 출력 중복 규칙은 별도 문제로 유지한다.
- 현재 제공 JSON의 레시피 5개에는 중복 재료가 없어 Config/에셋을 수정하지 않았다. 기존 슬롯 Utility의 큰 수량 합산 테스트도 입력 슬롯 계산의 독립 계약으로 유지한다.
- 레거시 제거 직전 `f85d2ca`의 CrafterConfigParser.ParseIngredients와 CrafterSystem.HasAvailableIngredients/ConsumeIngredients·직접 호출자를 읽기 전용으로 확인했다. 중복 행 유지·행별 독립 검사·완료 시 순차 소비와 부족 검증 누락을 확인했으며 명시적인 중복 합산/거부 의도는 확인하지 못했다. 작업 트리를 과거 코드로 되돌리지 않았다.

## 2026-10-08 관련 이슈 문서 정리

[현재 Task 문서](../V2%20Quality%20Improvement%20Tasks.md)의 직접 관련 항목만 갱신했다. 이번 정리는 문서 변경이며 새 컴파일·테스트를 실행하지 않았다. 위 실행 결과는 2026-10-07의 원본 기록으로 유지한다.

- F-009: 중복 재료·수량 범위 초과 초기화 실패 네 사례의 실행 근거를 추가하고, 파일 부재·빈 자료·읽기/일반 파싱 실패와 Item 설정의 기본값 검증을 남은 범위로 구분했다. 전체 완료로 처리하지 않았다.
- F-013/F-015: 기존 파싱/게시 검증 경계와 초기화 오류 중단 경로를 재사용하도록 범위를 정리했다. 품목·ID 유효성 검증은 구현되지 않았으므로 미완료를 유지한다.
- F-037/F-040: 2026-10-07 입력 파이프라인 19/19를 기존 완료 항목의 최신 회귀 근거로 연결했다. 실제 베이킹·Play Mode·없는 양수/음수 요청의 미실행 조합은 기존 제한으로 유지한다.
- F-038: 평가 당시 설명과 수정 완료 상태를 구분하고 잘못된 절 아래의 2026-09-30 처리 기록을 해당 항목으로 옮겼다. 완료 상태는 유지한다.
- F-010: 현재 드론 경로의 여러 요구 행 예약/도착량 분배와 전체 완공 검사에 맞춰 기존 결함 종료/재분류 후보로 정리했다. F-014의 신규 해결이나 공사 중복 행의 실행 검증으로 집계하지 않으며, 종료/재분류 확정 전 체크박스는 유지한다. 레시피 중복 거부 정책을 공사 자재에 전용하지 않았다.
- F-011/F-006/F-032/F-017 및 재검토 R-01은 별도 문제로 유지했다. Q06/Q21/Q22와 날짜가 있는 2026-10-06 재검토의 원본 평가 기록은 수정하지 않았다.

## 2026-10-08 F-013 구현 이후의 경계

위 정리에서 품목 검증이 미구현이라고 안내한 F-013은 이후 정책 확정과 구현을 마쳤고 Unity 컴파일을 기다린다. ParseJson/PublishConfig의 ValidateConfig와 제품 Init의 ValidateRegisteredConfig는 같은 하위 검증을 사용하며 재료 중복 거부도 유지/적용한다. JSON 재료 long→int 범위·기존 비양수 보정과 Decision/Execution의 전체 재료 확인 후 선소비는 변경하지 않았다. 유효한 재료만 있어도 출력이 무효인 입력은 별도의 품목/출력 검사로 게시 전에 거부한다.

F-014의 기존 완료와 원본 32/32는 유지하고 새 품목 오류·확대된 사전 등록의 실행 검증으로 소급하지 않는다. F-009의 선택 검증 분리/추적 종료는 이후 최신 Task를 따르며 이번 후속에서 재개하지 않는다. F-015 ID 정책과 F-006 실행 중 결과 보존은 여전히 별도 문제다. 이번 안내는 문서 변경이며 새 소스/컴파일/테스트는 없다. [F-013 직접 영향 정리](F013-Verification.md#2026-10-08-관련-이슈-후속-정리)를 따른다.

## 2026-10-09 F-015 후속 번호 검사

F-015의 입력 전체 ID HashSet과 F-014의 레시피 내부 재료 품목 HashSet은 서로 다른 검사다. 공통 ValidateConfig/ValidateRegisteredConfig에서 번호의 양수/유일성과 기존 재료 중복/품목/출력 검사를 함께 수행하며 번호 검사가 재료 총량 검사를 대신하지 않는다. Decision/Execution의 전체 재료 선확인·선소비는 변경하지 않았다. 서로 다른 번호의 동일 생산품은 정상이다.

F-014의 완료와 원본 32/32를 유지하며 번호 오류·품목 오류·확대된 사전 등록이나 재료 소비의 새 실행으로 합산하지 않는다. F-007/F-013은 공유 컴파일로 완료됐고 F-015도 구현·컴파일 완료지만 F-006은 남는다. 이번 후속은 문서만 변경했으며 새 컴파일/테스트는 없다. [직접 영향과 실행 한계](F015-Verification.md#2026-10-09-관련-이슈-후속-정리)를 따른다.
