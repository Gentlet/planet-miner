# F-007 MaxStack 입력 검증과 소비자 용량 계약

작업일: 2026-10-08. 코드 분석·.NET 컴파일과 Unity 실행 검증을 구분한다. 최초 Q04의 무효 설정 예시는 조건부 코드 분석이며 정상 JSON에서 실행 재현한 결과가 아니다.

## 확정 정책과 구현

- 기존 ItemConfig.json이 값의 출처다. 실제 9품목의 MaxStack을 모두 명시하고 양수만 허용한다. 품목 항목·값 누락, 0·음수, 빈/오타/정의되지 않은 이름·숫자 이름, 같은 품목의 중복은 오류다. 정상 이름의 대소문자 무시는 유지한다.
- JSON의 None 항목과 재정의 지원, GetDefaultMaxStackFor의 switch 기본값, 공통 DefaultMaxStack과 양수 기본값 보충을 제거했다. 실제 9품목의 기존 50/100/1 값은 모두 보존했다. enum None과 숫자 순서는 유지하고 인덱스 대응을 위해 버퍼 0번에만 내부 None/0을 둔다.
- ParseItems는 전체 입력을 확인한 후 결과를 반환한다. 실패하면 새 Registry를 만들지 않고 기존 ReportFailure/SimulationFailureUtility로 상세 로그·미게시·즉시 SimulationFatalError를 전달한다. 앱 종료·자동 재시도·전체 실패 후 기본값 대체는 없다.
- 자동 Init의 사전 등록 경계도 전체 버퍼 길이·품목 인덱스·실제 품목의 양수 MaxStack·내부 None/0을 검사한다. 실패한 사전 등록 원본은 삭제/보충하지 않고 시뮬레이션을 차단한다. 공개 API의 기존 Registry 중복 호출은 입력 처리 전 거부한다.
- ItemRegistry는 기존 설정 엔티티를 식별하는 태그가 됐다. 같은 타입의 정적 GetMaxStack이 원본 ItemConfigElement 버퍼만 조회하며 None·범위 밖·인덱스 불일치에는 용량 0을 반환한다. 설치된 Entities 소스의 GetSingleton/GetComponentData는 0크기 태그의 값 조회를 거부하므로 Reader를 singleton 엔티티+원본 버퍼 접근으로 함께 조정했다.
- 일반 창고 입고 예약은 빈 슬롯 탐색 전에 maxStack <= 0을 거부한다. 기존 F-031의 PendingSlot 품목·수량과 양수 한도의 잔여 용량 재사용은 유지한다. F-037의 제작 입력 슬롯 계산·실패 시 기존 상태 보존, 생산·드론의 용량 판단과 실제 성공분 정산, Validator의 저장/출력 슬롯 초과 검사는 같은 원본 값을 사용한다.
- 생산·예약·진단의 설정 부재 50 보충도 제거했다. 정상 GameSimulationGroup은 기존 준비/Fatal 검사를 유지한다. 읽기 전용 BufferLookup과 기존 state.Dependency, Command/Building/Drone/Commit 경계는 변경하지 않았다. Init 내부 참조·별도 원본 캐시·새 시스템/컴포넌트/Utility·partial 분리는 없다.

## 기존 테스트의 직접 영향

- 새 테스트 메서드를 추가하지 않았다. Phase3ItemConfigTests의 명시 JSON을 실제 모든 품목을 포함하도록 바꾸고 기존 기본값/조회 assertion을 새 게시 계약에 맞췄다. 다른 fixture의 제거된 scalar 설정/값 조회 호출과 BuildingInputSlotUtility 인자만 조정했다.
- TestEntityFactory의 격리 설정은 필요한 데이터를 명시적으로 준비한다. 제품 Init의 파일 로드나 제거한 기본값 함수를 대체 호출하지 않는다.
- 기존 Phase5CrafterInputSlotTests에는 0/-1 한도 거부 사례가 있지만 이번 Unity 실행 기록으로 사용하지 않는다. 과거 F-011의 정상 설정 2/2도 이번 무효 입력의 실행 증거로 합산하지 않는다.

## 확인한 결과

- 최종 `dotnet build Assembly-CSharp-Editor.csproj --no-restore --verbosity quiet /nologo`는 종료 코드 0, 경고 0·오류 0이다. 원본 출력은 `Logs/Codex/F007-20261008/msbuild-final.log`에 보존했다.
- `git diff --check` 통과. 현재 C# 소스/테스트에서 DefaultMaxStack·GetDefaultMaxStackFor와 ItemRegistry 태그의 GetSingleton/GetComponentData 호출이 남지 않았음을 확인했다.
- JSON 비교는 실제 9개 항목 중 기존 한도와 같은 항목 9개, None/DefaultMaxStack 필드 0개다. `Logs/Codex/F007-20261008/config-comparison.json`은 파일 비교 결과이며 C# 파서 실행 결과가 아니다.
- Serena initial_instructions와 프로젝트 활성화는 같은 대화의 사전 분석에서 수행했다. 구현 시작에 get_current_config로 같은 프로젝트/csharp ready를 확인했고 get_symbols_overview·find_symbol·find_referencing_symbols로 관련 정의와 직접 호출자를 확인했다. Git 역사 구간은 일반 파일/셸 도구로 확인했다.

## Unity 검증 대기와 한계

- 기본 shell의 Unity status는 STATUS_NO_INSTANCES였다. pipeline list는 해당 프로젝트가 실행 중·Pipeline 설치·Safe Mode 아님으로 보고했지만 reachable server는 0이었다. 별도의 프로세스 조회에서도 PlanetMiner 프로젝트로 열린 Editor를 확인했다.
- 제한 밖의 읽기 전용 editor_status도 No Pipeline instance로 실패했다. 연결된 PlanetMiner Unity Develop 도구는 workspace 목록을 반환했지만 project command는 INVALID_ARGUMENT으로 실행 결과를 확보하지 못했다. 실패를 재컴파일 통과로 해석하지 않는다.
- Library의 Unity 어셈블리는 수정된 소스보다 오래된 상태였다. 따라서 .NET 빌드를 Unity 재컴파일·Burst/Editor 로드 통과의 증거로 바꾸지 않는다. Unity 컴파일과 선택 EditMode는 연결 확보 뒤 확인할 항목으로 남긴다.
- 무효 JSON·사전 등록 오류→Fatal·일반 창고의 무효 한도 거부를 실제 게임에서 연쇄 실행하지 않았다. 전체 EditMode·Play Mode·실제 Bake·입력·시각·성능 검증은 수행하지 않았다.
- Task의 F-007 완료 체크와 현황 합계는 Unity 검증 대기로 유지한다.

## 2026-10-08 관련 이슈 후속 정리

사용자 요청으로 F-007 변경의 직접 호출자와 관련 Task를 재대조했다. 이번 후속은 문서만 변경했으며 코드·테스트·Config·에셋 수정과 새 컴파일·테스트 실행은 없다. 위 .NET 결과는 F-007 구현 시 실행이고 이번 문서 작업의 새 결과가 아니다.

- **F-008 — 기존 설정 수명 해결을 유지:** [InitializeItemRegistry](../../../../Assets/Scripts/Systems/Initialization/ItemConfigInitSystem.cs)의 기존 Registry 입력 처리 전 거부와 게시 실패 시 이번 호출의 엔티티만 회수하는 경계를 확인했다. ItemRegistry의 공통값 필드는 없어졌지만 World 소유 버퍼·읽기 전용 Job 조회·시작 시 1회 게시·런타임 교체 금지는 유지한다. Init 내부 값 조회·별도 원본 캐시·새 Dispose 경계를 추가할 필요가 없다. 과거 48/48는 당시 버퍼 전환의 실행 기록이고 태그/정적 조회 전환 후의 새 실행이 아니다.
- **F-009 — 선택 검증 종료를 유지하고 대상 갱신:** 삭제된 기본값 테스트를 복원할 이유는 없다. 현재 [Phase3ItemConfigTests](../../../../Assets/Editor/Tests/Phase3ItemConfigTests.cs)는 전체 명시 JSON과 정상 파일 연동을 검사하도록 조정됐다. 향후 선택 실행의 Item 입력은 파일 부재/읽기/일반 파싱/필수 누락뿐 아니라 0·음수·값 누락·None·무효 이름·중복과 사전 등록 거부를 구분한다. 이를 새 테스트/실행의 필수 조건으로 바꾸지 않는다. F-011 당시 Item 2/2를 현재 코드의 실행이나 새 오류 사례의 통과로 재사용하지 않는다.
- **F-011 — 기존 실패 정책을 새 입력 검사에 연결:** [ParseItems](../../../../Assets/Scripts/Systems/Initialization/ItemConfigInitSystem.cs)는 실제 모든 품목의 명시적 양수 값을 검사하고 완성 전에는 새 Registry를 만들지 않는다. 값 검증 실패는 기존 상세 로그·미게시·즉시 Fatal로 전달하고 [GameSimulationGroup](../../../../Assets/Scripts/Phases/GameSimulationGroup.cs)의 기존 시작 게이트가 적용된다. [ValidateRegisteredItems](../../../../Assets/Scripts/Systems/Initialization/ItemConfigInitSystem.cs)도 전체 품목·양수 값·내부 None/0을 검사하며 잘못된 기존 원본을 보충/삭제하지 않는다. F-011 당시 유지한 Item 공통값/품목별 기본값 규칙은 최신 사용자 선택으로 폐기됐다. 다른 설정의 실패 계약과 기존 완료는 유지하며 79/79를 새 실패 경계의 실행 증거로 합산하지 않는다.
- **F-031 — 정상 예약 해결 유지, 빈 슬롯의 무효 한도 예외 제거:** [BuildingStorageInputReservationJob](../../../../Assets/Scripts/Systems/Buildings/Reservation/BuildingStorageInputReservationSystem.cs)은 슬롯을 고르기 전에 0 이하 한도를 거부한다. 그 뒤에는 기존 PendingSlot의 ItemType/Count 집계와 동일 품목의 잔여 용량 재사용을 유지한다. 해당 구현을 다시 고칠 작업이나 새 완료로 집계하지 않는다. F-007의 정상/무효 입고와 Owner 반영은 이번에 실행하지 않았다.
- **F-037 — 실제 제작기 연결 해결 유지, 설정 검증 책임 구분:** [BuildingInputSlotUtility](../../../../Assets/Scripts/Common/BuildingInputSlotUtility.cs)의 ceil(품목별 요구량/MaxStack) 계산·최대 스택까지 비축·None/미등록/0 이하 방어는 유지한다. 한도 값의 원본은 ItemConfigElement이고 조회는 ItemRegistry의 정적 함수다. 입력 Utility와 fixture에서 없어진 scalar 인자를 제거했으며 이 API 변경 후 제작 연결·Burst·Bake를 새로 검증하지 않았다. 이전 19/19를 현재 API의 실행 증거로 소급하지 않는다.
- **F-040 — 기존 작업 보존과 요청 해제 계약 유지:** [CrafterRecipeCommandSystem.OnUpdate](../../../../Assets/Scripts/Systems/Command/CrafterRecipeCommandSystem.cs)는 없는 양수 ID와 슬롯 계산 실패를 상태 변경 전에 거부한다. 기존 Progress/Active·입력/출력 버퍼·필터 보존, 유효 변경과 0/음수 요청의 해제는 그대로다. 무효 Item 설정의 초기화 Fatal과 이미 준비된 설정에서의 무효 변경 요청 거부는 다른 경계다. 기존 완료는 유지하며 새 회귀 실행은 없다.

관련되지만 완료로 전환하지 않는 범위도 확인했다.

- **F-013은 미완료:** [RecipeConfigLoader.ParseJson](../../../../Assets/Scripts/Config/RecipeConfigLoader.cs)의 재료·주생산품·부산물 TryParse 성공 여부와 정의된 품목/None 검사는 F-007에서 바꾸지 않았다. PublishConfig는 범위와 재료 중복을 검사하지만 Item 설정의 이름 검증을 호출하지 않는다. 공통 한도 조회의 None/범위 밖 용량 0은 소비자 방어이며 무효 레시피의 전체 미게시·Fatal을 구현한 것이 아니다. 의도적인 무출력 허용 여부와 레시피별 None 의미는 이 항목에서 별도로 정한다.
- **F-006은 정책 미확정:** [ProductResultApplyJob.Execute](../../../../Assets/Scripts/Systems/Items/StateApply/ItemLifecycleApplySystem.cs)는 예상 밖 프리팹 누락을 기록한 뒤에도 마지막에 ProductResult를 Clear한다. 시작 시 무효 Item 설정 차단은 실행 중 실패/부분 생성의 결과 보존·복구·보상을 결정하지 않는다.
- **F-032와 F-017 등은 이번 직접 정리 대상에서 제외:** 입고 예약의 용량 거부는 비활성 원본 아이템의 후보 선택 자격을 수정하지 않는다. 양수 MaxStack 검증도 월드 설정의 큰 반경·전이 폭 정수 연산을 보장하지 않는다. 이 변경을 근거로 다른 품목/레시피/산술 문제나 R-01/R-02를 자동 완료·구현 범위에 넣지 않는다.

추가 완료/추적 종료는 **0건**이다. Task의 기존 34/43 완료·9개 미완료와 F-007의 Unity 검증 대기를 유지한다. F-008/F-011의 원본 실행 기록과 기술 종료 문서에는 당시 Item 기본값 계약이 F-007에서 변경됐다는 후속 주석만 추가했으며 원본 통과 수·실패·집계를 다시 쓰지 않았다. 문서 변경의 git diff --check는 통과했고, 후속 시작/종료에 비교한 기존 C#/테스트/JSON 변경 파일 20개의 SHA256은 모두 동일했다.

## 2026-10-08 F-013 구현 이후의 경계

위 후속 정리에서 F-013 품목 검증이 미구현이고 None/무출력 의미를 결정해야 한다고 설명한 부분은 F-013 적용 전 상태다. 이후 사용자는 정식 이름만 허용·실제 주생산품 필수·없는 부산물 생략을 확정했고 RecipeConfigLoader의 파싱/직접 게시와 제품 Init의 사전 등록에 해당 검증을 추가했다. Item과 Recipe의 이름 의미는 일치하지만 각각의 기존 로더가 검증을 소유한다. Item 버퍼 0번 내부 None/0은 레시피 품목으로 허용하지 않는다.

F-007의 MaxStack 명시적 양수·원본 조회·일반 입고 거부 계약은 변경하지 않았다. F-013은 코드/.NET 빌드 확인·Unity 컴파일 대기이며 F-007의 Unity 대기도 그대로다. 이번 안내는 문서 변경이고 추가 완료·새 컴파일/테스트는 없다. [F-013의 직접 영향과 남은 범위](F013-Verification.md#2026-10-08-관련-이슈-후속-정리)를 따른다.
