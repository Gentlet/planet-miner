# F-011 설정 로드 실패·필수 항목 누락과 공개 준비 계약

작업일: 2026-10-08. 현재 코드와 이번 실행 기록을 구분한다. 최초 Q05와 2026-10-06 재검토의 무료 완공 예시는 당시 코드 분석이며 실제 게임 재현 기록으로 바꾸지 않는다.

## 확정한 정책과 범위

- 현재 건물·아이템·레시피·월드(자원/바닥) 설정 로드와 프리팹 준비를 대상으로 한다. 아직 없는 전력·연구·드론 등의 로더는 추가하지 않는다.
- 파일 누락·읽기/파싱 실패·필수 데이터 누락은 상세 로그·해당 설정 미게시·기존 SimulationFatalError의 즉시 게시로 전체 게임 시뮬레이션을 차단한다. 앱 종료·자동 재시도·파일 실패 후 자동 기본값 대체는 추가하지 않는다.
- 건물은 None/Count/ConstructionSite를 제외한 완공 11종, 레시피는 ID 1~5 모두가 필수다. 건물 로더는 누락 종류를, 레시피 검증은 누락 ID 목록을 로그에 남긴다.
- 유효한 아이템 공통값·품목별 기본 규칙/예외, 정상 건물의 빈 자재 목록·기본 footprint와 기존 레시피 선택 필드 규칙은 유지한다.
- 격리 테스트는 필요한 설정을 직접 준비한다. 제품 전체 Init을 실행하는 경로와 명시적 부분 데이터 게시 API를 구분한다. 새 테스트/추가 assertion을 필수 조건으로 두지 않는다.

## 구현과 소유권

- SimulationFailureUtility를 확장해 초기화의 즉시 Fatal 게시와 실행 중 ECB 기록을 같은 기존 오류 데이터로 표현한다. 새 시스템·Utility·Ready/Fatal 컴포넌트·영속 캐시·설정 파일·partial 분리는 없다.
- BuildingConfigLoader는 전체 파일 파싱의 마지막에 필수 종류를 검사한다. Init의 사전 등록도 종류/게시 버퍼의 완전성을 확인한다. 검증 실패 시 두 out 목록은 null이다.
- ItemConfigInitSystem은 파일/파싱 실패를 기본값으로 숨기지 않는다. 유효한 공통값·품목별 예외로 기존 인덱스 순서의 전체 버퍼를 만들고 게시하며, 실패는 Entity.Null과 Fatal로 전달한다. 기존 Registry에 대한 직접 중복 API 호출의 입력 처리 전 거부는 유지한다.
- RecipeConfigLoader의 파일 로드는 필수 ID를 검사한다. InitializeRecipeRegistry의 명시 JSON과 자동 Init의 사전 등록도 같은 필수 목록을 요구한다. ParseJson/PublishConfig의 최소 부분 게시와 명시적 CreateDefaultConfig는 유지하지만 파일 실패 경로는 이를 호출하지 않는다. F-014의 재료 중복·수량 범위 거부는 유지한다.
- 월드 Init은 자원 전용 사전 등록을 전체 준비로 인정하지 않는다. 같은 엔티티의 자원 버퍼·바닥 설정·바이옴/변형 버퍼를 요구하며 불완전하면 보충/중복 게시 없이 Fatal로 거부한다. 명시적 자원 전용 API는 격리 구성용으로 유지한다.
- 건물·월드·아이템·레시피 게시 중 실패는 이번 호출이 만든 미완성 엔티티만 회수한다. 이미 게시된 다른 설정을 되돌리는 전역 rollback은 추가하지 않는다.
- GameSimulationGroup은 틱 시작에 프리팹 DB Ready와 각 설정 소유자의 게시 엔티티/필수 버퍼, Fatal을 확인한다. 필수 목록의 반복 검사는 Init에 있고 매 틱 Reader는 기존 ECS 데이터만 읽는다. DB Ready의 의미를 설정 Ready로 바꾸지 않는다.
- 배치는 무설정 overload로 진행하지 않는다. 공통 Spawn은 설정/대상 종류 누락을 거부하고 호출자의 ECB에 Fatal을 기록한다. 완공 실패 시 현장·자재 보존, 직접 요청의 기존 소비 계약과 Command/EndCommand → Building/EndBuilding → Drone/EndSimulation → Synchronization 경계는 유지한다. F-027 접수 정렬·최종 승인 점유 공유는 변경하지 않았다.
- 기존 테스트 fixture에 필요한 최소 게시 데이터를 준비하고 일부 레시피만 사용하는 사례는 전체 Init 대신 기존 ParseJson/PublishConfig로 명시적 부분 게시를 수행하도록 조정했다. 새 테스트 메서드나 assertion 보강은 없다.

## 검증 기록

- 원본 명령 기록은 `Logs/Codex/F011-20261008/`에 보존한다. 최종 통계와 초기 실패는 아래에 별도로 기록한다.
- 최초 Unity 래퍼는 STATUS_NO_INSTANCES로 재컴파일을 시작하지 못했다. 호스트 진단에서는 Editor 실행, Pipeline 로딩 대기, Safe Mode 아님을 확인했고 사용자가 컴파일/에셋 로딩 중이라고 확인했다. 이후 연결이 ready로 준비됐다. 초기 연결 실패를 컴파일 실패나 Editor 부재의 증거로 해석하지 않는다.
- `dotnet build Assembly-CSharp-Editor.csproj --no-restore --verbosity quiet /nologo`: 런타임/Editor C# 프로젝트 컴파일 경고 0·오류 0. `msbuild.log`와 게시 정리 후 `msbuild-final.log`를 보존한다. 이후 테스트 입력 수정과 최종 C# 정리는 아래 Unity 컴파일로 확인한다.
- 첫 Unity 재컴파일은 up_to_date·failed:false·오류 0과 Editor ready·비컴파일/비리로드·Play Mode stopped를 확인했다. 당시 두 어셈블리는 최신 C#보다 새로웠다. 이후 수정 뒤 recompile_status-repaired.json과 recompile_status-final.json은 completed·failed:false·오류 0이다.
- 선택 실행의 첫 완공 클래스는 8건 중 1건이 창고 설정 준비 누락으로 실패했다. 현장의 대상 종류를 직접 준비하도록 수정한 뒤 같은 8건이 통과했다. 프리팹 클래스는 15건 중 1건이 로딩 대기 중 설정 준비와 이후 유효 DB 준비가 자원 설정을 중복 생성하여 실패했다. 최소 설정 준비를 CreateValidDatabases로 옮긴 뒤 같은 15건이 통과했다. 최초 실패 status 파일을 덮어쓰지 않고 repaired/final 기록을 따로 보존한다.

- 제작 입력 클래스의 첫 실행은 19건 모두 실패했다. 직접 생성 helper에 건물 설정이 빠져 있었고, 그룹 builder가 레시피를 먼저 준비하여 기존 Init 호출과 충돌했다. 생성 직전에 필요한 건물 설정을 직접 준비하고 그룹 builder의 선행 레시피 등록을 제거했다. 이 실패의 원본 final-status 파일은 보존한다. 후속 verified 실행은 17/19 통과했고 직접 생성 두 건은 fixture 크기가 기존 2×2 대신 1×1이 되어 출력 위치가 달라져 실패했다. 최소 설정 준비가 기존 타입별 GetDefaultFootprint를 명시하도록 수정했으며 후속 실행은 별도 complete 기록으로 남긴다.

## 최종 확인

- Unity `recompile_status-verified.json`: completed, failed:false, 오류 0. 런타임 소스 최신 시각은 06:52:09.640 UTC, Assembly-CSharp.dll은 06:52:15.308 UTC다. Editor 소스 최신 시각은 06:54:31.797 UTC, Assembly-CSharp-Editor.dll은 06:54:36.756 UTC로 각 어셈블리는 해당 소스보다 최신이다.
- 최종 Editor 조회는 컴파일/리로드 중이 아니며 Play Mode가 paused인 상태였다. 앞선 재컴파일 확인 당시에는 ready·stopped였다. Play 시작/정지 명령은 실행하지 않았으며 마지막 상태를 보존했다. 이 조회를 Play Mode 작동 검증으로 사용하지 않는다.
- 관련 기존 선택 EditMode의 클래스별 마지막 성공 결과는 레시피 설정 7/7, 배치 18/18, 완공 8/8, 프리팹 초기화 15/15, 제작 입력 19/19, 직접 생성 9/9, 아이템 설정 2/2, 건물 설정 1/1이다. 합계 79/79, 최종 실패/생략/Inconclusive 0이다. 초기 fixture 실패와 재실행은 신규 검증 건수로 중복 합산하지 않는다.
- `git diff --check` 통과. 테스트 diff의 assertion/Test attribute 변경 0, 신규 런타임 파일 0, Config/에셋 변경 0이다. Task는 F-011만 추가 완료해 43개 중 27개 완료·16개 미완료이며 F-033 제외의 기존 변경을 보존했다.
- 최종 통계·어셈블리 시각과 상태는 `Logs/Codex/F011-20261008/verification.json`에 보존했다. 새 테스트가 없어 필수 누락 자체의 자동 회귀 사례를 추가로 실행했다고 주장하지 않는다.

## 실행하지 않은 범위

- 파일 누락/읽기 실패·일반 파싱 오류·새 필수 건물/레시피 누락과 벨트/연구 키/DroneStation 검증 오류에 대해 실제 장면에서 초기화→배치→완공을 연쇄 실행한 결과는 아니다. 선택 테스트의 정상 로드와 F-014 검증 오류를 이 실패 경로의 실행 증거로 확대하지 않는다.
- 기존 F-007 MaxStack·F-013 품목·F-015 레시피 ID 유효성/중복과 다른 입력 범위 문제는 자동으로 해결하거나 완료 처리하지 않는다. 필수 ID 존재 검사는 ID 유일성이나 특정 ID의 출력 품목 의미를 검증하는 정책이 아니다.
- 구현 당시에는 F-009 파일 실패의 독립 실행 검증과 F-016의 별도 종료 분류를 보류했다. 아래 관련 이슈 정리는 이후 사용자 요청에 따른 문서 재검토이며 F-016의 구현 완료와 실행 미검증을 구분한다. F-009의 독립 실행은 여전히 미수행이다.
- 전체 EditMode·Play Mode·실제 SubScene 베이킹·시각/입력·성능·다른 플랫폼의 파일 접근은 이번 검증 범위가 아니다.
- 시작부터 있던 Task/CurrentCodeReview의 F-033 제외 변경을 보존했다. 원본 평가·과거 실행 기록·CurrentCodeReview 본문을 이번 결과로 다시 쓰지 않았다. Config/에셋은 변경하지 않았다.

## 2026-10-08 관련 이슈 후속 정리

사용자 요청으로 현재 소스·원본 Q07의 F-016 조건과 F-011 원본 실행 기록을 재대조했다. 이번 후속 작업은 Task와 이 문서만 수정했다. 새 코드·테스트·Config·에셋 변경이나 새 컴파일·테스트 실행은 없다. 위 구현 당시 27/43 집계와 초기 실패 기록을 보존하고 현재 Task 집계는 아래 결과를 반영한다.

- **F-016 구현 완료로 분류:** WorldGenerationConfigLoadSystem은 기존 자원 설정에 HasCompleteConfig를 적용하고, 자원 버퍼·바닥 설정·비어 있지 않은 바이옴/변형 버퍼가 없으면 즉시 로그·Fatal로 거부한다. 완전한 기존 설정은 재게시하지 않고 자동 Init은 비활성화한다. 자원 전용 API의 역할은 Init을 실행하지 않는 명시적 격리 게시로 정리했고 GameSimulationGroup도 바닥을 포함한 게시 형태를 요구한다. 원래 문제의 불완전 상태를 전체 준비로 인정하는 분기가 제거됐음을 코드로 확인하고 기존 Unity 컴파일 근거를 재사용했다. 추가 해결 개수는 F-016 한 건이며 현재 43개 중 28개 완료·15개 미완료다.
- **F-016 실행 한계:** 부분/전체 Publish→Init 및 반복 초기화의 독립 실행은 없다. 기존 79/79·프리팹 15/15를 그 증거로 사용하지 않는다. InitialChunkLoadBootstrapSystem은 자원 설정만으로 Initialization에서 요청 큐를 작성할 수 있지만 Fatal과 설정 게이트가 게임 Command의 실행을 차단한다. 초기화 전체의 중지·요청 큐 회수·rollback을 확인하거나 새로 보장하는 정책은 아니다. [자동 Init](../../../../Assets/Scripts/Systems/Initialization/WorldGenerationConfigLoadSystem.cs), [완전성 검사·부분 게시](../../../../Assets/Scripts/Config/WorldGenerationConfigLoader.cs), [게임 시작 게이트](../../../../Assets/Scripts/Phases/GameSimulationGroup.cs), [Bootstrap](../../../../Assets/Scripts/Systems/Initialization/InitialChunkLoadBootstrapSystem.cs).
- **F-009 선택 실행 검증으로 축소:** 자동 fallback 제거·실패 공개의 구현은 F-011에서 끝났고 남은 것은 파일/읽기/일반 파싱/필수 누락의 독립 실행 기록이다. 체크 미완료는 별도 코드 수정이나 새 테스트를 F-011 완료에 강제한다는 뜻이 아니다. 정상 파일 연동은 Item 2/2·건물 Init 1/1·Recipe 7/7로 확인한 범위를 재사용하며 실패 경로의 실행으로 확대하지 않는다.
- **F-007·F-013·F-015·F-017의 입력 검증 공백은 유지:** Item 개별 override는 여전히 그대로 게시되고 Recipe 품목의 Enum.TryParse 성공/정의 여부와 추가 ID의 양수/유일성 검사는 완성되지 않았다. 필수 ID 1~5 존재는 ID 유일성 검사와 다르다. 월드 수치의 추가 상한·정수 연산도 변경하지 않았다. 오류로 판정한 입력의 미게시·로그·Fatal과 자동 fallback 제거는 재사용하므로 같은 중단 정책을 다시 결정하는 작업은 제외한다. [Item 파싱](../../../../Assets/Scripts/Systems/Initialization/ItemConfigInitSystem.cs), [Recipe 입력·필수 검사](../../../../Assets/Scripts/Config/RecipeConfigLoader.cs), [Recipe 첫 일치 조회](../../../../Assets/Scripts/Common/RecipeConfigLookupUtility.cs).
- **F-003·F-012·F-030의 실패 전달 경계 정리:** DroneStation 용량·연구 키 UTF-8 길이·벨트 속도 검증은 기존 코드다. 해당 거부 결과가 이제 Init의 Fatal과 배치/Spawn 거부로 연결된다. 검증을 다시 구현한 성과로 집계하거나 드론 운송·경계 문자열·고속 벨트의 새 실행으로 설명하지 않는다.
- **F-008의 기존 수명 계약 유지:** Item/Recipe의 기존 Registry 입력 처리 전 중복 거부와 World 버퍼 소유권을 유지한다. 전체 제품 Init의 필수 데이터 확인과 명시적 격리 부분 게시를 구분했다. Registry 교체·복구·새 캐시를 다시 설계할 범위가 아니다. 정상 파일/제작 회귀를 Reader 진행 중 거부·World 종료·실패 회수의 독립 실행으로 집계하지 않는다.
- **F-014·F-018·F-024·F-027·F-037·F-040의 기존 완료 근거 갱신:** 같은 F-011 원본 실행의 Recipe 7/7, 프리팹 15/15, 완공 8/8, 배치 18/18, 제작 입력 19/19, 직접 생성 9/9 중 각 이슈에 직접 해당하는 범위를 Task에 연결했다. F-014의 제작 실행 클래스 6건은 이번 실행에 없으며, 원래 32/32·161/161 등과 합산하지 않는다. 동일 테스트가 여러 이슈의 정상 대조군이어도 F-011 합계는 79건 그대로다. 배치 접수 정책·완공 거부 시 현장/자재 보존·무효 레시피 요청의 상태 보존은 재결정하지 않는다.
- **F-006·F-021·F-022의 미해결 경계 유지:** 초기화 차단은 실행 중 ProductResult.Clear의 보존/소비 정책을 정하지 않는다. ECS 테스트 프리팹은 실제 Baker/SubScene 베이킹의 증거가 아니며, fixture의 2×2 크기 복원은 DB footprint 권위의 해결이 아니다. 공통 Spawn의 요청→Config→타입 기본 크기와 dbFootprint 미사용은 그대로다. [생산 결과 소비](../../../../Assets/Scripts/Systems/Items/StateApply/ItemLifecycleApplySystem.cs), [공통 Spawn·크기 선택](../../../../Assets/Scripts/Common/BuildingLifecycleUtility.cs).
- 원본 Q04/Q05/Q06/Q07 등의 평가와 날짜가 있는 CurrentCodeReview, F-033 제외 및 다른 작업자의 기존 변경은 보존했다. 문서 변경의 `git diff --check`와 Task 체크 상태 집계를 확인했다.
