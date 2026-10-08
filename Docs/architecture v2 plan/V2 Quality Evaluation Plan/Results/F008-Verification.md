# F-008 검증 기록 — Item/Recipe 설정 버퍼 전환

- 날짜: 2026-10-02
- 원본 평가: [Q04](Q04.md), [Q06](Q06.md). 원본은 평가 당시 기록으로 보존한다.
- 사용자 선택: 시작 시 한 번만 게시, 실행 중 교체 거부, Blob 대신 ECS 버퍼, World 종료까지 유지. 새 테스트·assertion 보강은 일괄 필수 조건으로 두지 않는다.

## 변경 내용

- `ItemRegistry`는 기본 최대 적재량을 보관하고 같은 엔티티의 `ItemConfigElement` 버퍼를 읽는다. 품목 번호에 의한 직접 조회와 기존 범위 밖 fallback을 유지한다.
- `RecipeRegistry` 엔티티가 `RecipeConfigElement`, `RecipeIngredientElement`, `RecipeOutputElement` 버퍼를 소유한다. 레시피별 재료·출력 범위와 JSON 순서를 유지하며 출력 슬롯 0은 주생산품이다.
- 기존 두 초기화 시스템을 재사용한다. 공개 초기화 함수는 Entity를 반환하며, 설정의 수동 Dispose나 Init의 별도 소유 필드를 제거했다. Recipe 로더의 파싱 결과는 일반 C# 목록이며 게시 때 버퍼로 복사한다.
- 레시피의 기존 ID/주생산품 첫 일치 검색은 `Common/RecipeConfigLookupUtility`로 이동했다. 새 시스템·공간 캐시·프레임별 설정 복사를 추가하지 않았다.
- Job 소비자는 읽기 전용 BufferLookup을 등록·갱신하고 기존 state.Dependency에 실행 핸들을 등록한다. 레시피 변경 명령, 입력 슬롯 계산, 개발용 불변식 검사도 버퍼를 읽는다.
- 기존 테스트의 준비·조회·해제 코드를 전환했다. 맞춤 레시피는 처음부터 게시하며 `Phase5RecipeBlobTests`를 `Phase5RecipeConfigTests`로 바꾸고 .meta GUID를 유지했다. 새 테스트나 TestCase는 추가하지 않았다.

## 계약별 코드 확인

- 사전 등록: 공개 API로 읽는 시스템 실행 전에 게시한다. 자동 Init은 필요한 버퍼를 가진 기존 Registry를 사용하고 비활성화하며 소유권을 별도로 인수하지 않는다. 필수 버퍼가 빠진 사전 등록은 자동 Init에서 오류로 거부한다.
- 반복 호출/자동 초기화 후 외부 호출: 두 Initialize와 Recipe PublishConfig는 기존 Registry 확인을 입력 읽기·새 엔티티 생성보다 먼저 수행하고 InvalidOperationException으로 거부한다. 기존 설정을 반환하거나 조용히 무시하며 성공한 것처럼 처리하지 않는다.
- 읽는 Job 진행 중 재초기화: 위 guard에서 거부하므로 기존 버퍼를 쓰거나 해제하지 않는다. 직접 ECS API로 게시된 설정을 변경·삭제·재등록하는 동작은 지원하지 않는다.
- 게시 실패: 파싱 및 Recipe 범위 검증은 엔티티 생성 전이다. 생성 이후 예외는 이번 호출에서 생성한 엔티티만 삭제한다. 모든 버퍼 타입은 엔티티 생성 시 함께 구성하므로 게시 중 버퍼 참조를 얻은 뒤 구조 변경하지 않는다.
- 초기화 시스템 중도 제거: 별도 OnDestroy 해제가 없어 설정 엔티티/버퍼는 World에 남는다. 설정 수명이 Init 시스템의 수명에 묶이지 않는다.
- World 종료: 현재 설치된 Entities 6.4.0의 `World.Dispose()`는 CompleteAllTrackedJobs 후 시스템 및 엔티티 저장소를 해제한다. Job 등록과 ECS 버퍼 소유 경계를 사용하며 별도 Blob 할당·이중 해제 경로를 제거했다. 등록하지 않은 외부 Job까지 자동 완료한다고 주장하지 않는다.

## 유지한 범위

기존 Item 설정 파일 경로, Recipe Resources 경로, 기본값·파싱 보정, 중복 ID의 첫 일치, 재료 조회·집계 의미, 제작 입력 슬롯 정책과 주생산품/부산물 순서를 유지한다. F-007 및 F-013~F-015의 별도 입력 정책은 확장하지 않았다. 설정 버퍼 표현 자체가 외부의 직접 수정을 언어 수준에서 금지하는 것은 아니며 게시 후 읽기 전용 계약을 따른다.

## 검증 결과

- 정적 확인: 이전 Item/Recipe Blob 타입·생성 API의 소스 참조가 남지 않음. 기존 테스트/TestCase 수 및 rename .meta GUID 보존 확인.
- 컴파일: Unity 6000.4.11f1 `completed`, `failed=false`, `errors=[]`, `compilationFailed=false`. Editor는 ready/compiling=false/domainReloadInProgress=false/Play Mode stopped였다. 최신 C# 소스 이후의 `Assembly-CSharp.dll`, `Assembly-CSharp-Editor.dll` 갱신을 모두 확인했다.
- 초기 지연: `triggered` 및 appActive=false 상태는 성공으로 처리하지 않았다. AssetDatabase ForceUpdate와 CLI editor_focus로는 실제 창 활성 상태가 바뀌지 않았으며, 후속 작업에서 확인된 Unity 프로세스 창을 활성화한 뒤 대기 중 컴파일이 완료됐다. 프로젝트나 장면 내용은 이 복구 과정에서 변경하지 않았다.
- 기존 선별 EditMode: **48/48 통과**, 실패/생략/Inconclusive 0. `Phase3ItemConfigTests` 2, `Phase5RecipeConfigTests` 3, `Phase5CrafterInputSlotTests` 18, `Phase5CrafterInputPipelineTests` 19, `Phase5CrafterExecutionTests` 3, `Phase4EndToEndPipelineTests` 3. 클래스별로 순차 실행했으며 새 테스트를 추가하지 않았다.
- 근거: `Logs/Codex/F008-verification-summary.json`에 컴파일·Editor·어셈블리 최신성·테스트 통계를 합쳤다. 원본은 `F008-compile-request.json`, `F008-compile-status.json`, `F008-editor-status.json`, `F008-assembly-freshness.json`, `F008-<TestClass>-request.json`/`-result.json`에 보관했다. `git diff --check`도 통과했다.
- 실행 미검증: 중복 초기화/사전 등록/Job 진행 중 거부/Init 단독 제거/게시 실패 rollback의 각 경계 상황을 별도 실행 재현하지 않았다. Play Mode, 실제 SubScene 베이킹, 전체 EditMode, 성능·메모리 프로파일링은 수행하지 않는다.

## 레거시 근거

삭제 직전 커밋 `f85d2cad69219329c72692d5d4309a3cb8204a6b`의 `Assets/Scripts/Systems/Buildings/Crafter/CrafterConfigLoadSystem.cs`는 OnCreate에서 레시피와 아이템 저장 한도를 ECS 버퍼에 적재하고 OnUpdate는 비어 있었다. 런타임 외부 재로드 호출은 조사 범위에서 확인하지 못했다. 시작 시 1회 운용은 코드 구조로 확인했으며 명시적인 핫리로드 금지 기획의 존재를 주장하지 않는다. 작업 트리 복원 없이 git show/grep로 확인했다.

## 2026-10-08 F-007 후속 영향

위 2026-10-02 변경 내용의 ItemRegistry 기본 최대 적재량과 범위 밖 fallback은 F-007에서 제거됐다. 현재 ItemRegistry는 설정 엔티티 태그이고 같은 타입의 정적 조회가 원본 ItemConfigElement 버퍼를 읽으며 None·범위 밖·인덱스 불일치에 용량 0을 반환한다. 기존 입력 처리 전 중복 거부·World 소유/읽기 전용/1회 게시·게시 실패 시 이번 호출의 엔티티만 회수하는 계약은 유지한다.

이번 후속은 문서만 변경했다. 기존 완료와 당시 48/48 기록은 보존하며 태그/정적 조회 전환 후의 새 Unity 실행 증거로 합산하지 않는다. [직접 영향과 실행 한계](F007-Verification.md#2026-10-08-관련-이슈-후속-정리)를 따른다.

## 2026-10-08 F-013 후속 영향

품목·필수 주생산품·작성된 부산물의 검증은 기존 Recipe Loader 안에서 확장됐다. PublishConfig는 기존 Registry가 있으면 새 입력을 검사하기 전에 거부하며 전체 검증을 마친 뒤에만 엔티티를 만든다. 사전 등록 검사 실패는 기존 원본을 삭제/교체/보충하지 않고 제품 Init의 Fatal로 차단한다. World 소유·읽기 전용·1회 게시와 이번 호출의 미완성 엔티티만 회수하는 F-008 책임은 유지한다.

이번 안내는 문서 변경이며 기존 완료와 원본 48/48는 보존한다. F-013의 .NET 빌드는 Unity 컴파일 대기를 해소하지 않았고 후반 품목 오류/사전 등록 실패·Reader 수명·World 종료의 새 실행도 없다. ID 정책 F-015와 실행 중 결과 보존 F-006은 별도로 남는다. [F-013의 직접 영향과 한계](F013-Verification.md#2026-10-08-관련-이슈-후속-정리)를 따른다.
