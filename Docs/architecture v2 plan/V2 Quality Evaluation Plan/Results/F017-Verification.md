# F-017 구현·검증 — 월드 설정 지원 범위와 안전한 산술

- 날짜: 2026-10-11 (Asia/Seoul)
- 상태: 완료. 사용자 지시인 “전부 추천안으로 작업하자”에 따라 구현했다.
- 원본: [Q07](Q07.md), [품질 개선 Tasks](../V2%20Quality%20Improvement%20Tasks.md). F-011/F-016의 준비·실패 계약은 유지하고 F-022/R-01과 구분한다.

## 확정한 지원 범위

- 광맥 반경: 0 ≤ min ≤ max ≤ 2칸. 이는 현재 게임에서 쓰는 규모를 지원하는 작업량 정책이며 정수의 표현 최댓값이 아니다.
- 초기 청크 한 변: 1~3청크. 기존 0/생략 → 3 해석과 원점 주변 좌표 규칙을 유지한다. 음수와 상한 초과를 새 값으로 바꾸거나 잘라내지 않는다.
- 기본값과 상한은 GameConstants에서 관리한다. 기존 ResourceGenerationSettings 생성자의 0 이하 → 기본값 처리는 유지하되 JSON/공개 게시의 원시 음수 및 사전 등록 원본의 음수는 별도로 거절한다.
- 바이옴 영역: 양수 int. 전이 폭: 0~영역 크기. 현재 값 3/2로 제한하지 않으며 float 계산의 유한성과 int 좌표 변환 안전 조건을 추가한다.
- 활성 경계 굴곡은 int 월드 셀 입력 전체에서 노이즈 격자 및 영역 좌표와 이웃 좌표 연산이 표현 가능해야 한다. 진폭 0이면 노이즈 좌표를 사용하지 않으므로 그 변환 검사는 생략한다.
- 이 범위는 전체 지도의 게임상 최대 크기, 멀리 떨어진 셀의 표시 정밀도, 성능 또는 무한 메모리를 보장하지 않는다.

## 변경과 책임

1. 기존 WorldGenerationConfigLoader의 수치 검사를 공통 메서드로 구성해 JSON, 공개 PublishConfig, 사전 등록 Init에 연결했다. 초기 크기·자원 반경/매장량·바닥 수치에 같은 기준을 적용한다. 기존 종류/중복/목록/Sprite 검증은 유지한다.
2. 이미 사용 중인 Newtonsoft.Json으로 작성된 초기 크기, 반경/매장량, 영역/전이 폭의 정수 토큰을 검사한다. 소수, 문자열 숫자, int 범위 밖 정수를 JsonUtility가 다른 정수로 변환하기 전에 거절한다. 필드 누락 시의 기존 DTO 값과 후속 범위 검사는 유지한다. 패키지 추가나 JSON 변경은 없다.
3. 공개 게시의 수치 오류는 엔티티 생성 전에 ArgumentException으로 거절한다. 자원 전용 격리 게시와 기존 게시 중 예외의 미완성 엔티티 회수는 유지한다.
4. WorldGenerationConfigLoadSystem은 기존 전체 형태 검사 후 같은 수치 검사를 사용한다. 무효 사전 등록을 수정하거나 재게시하지 않고 기존 오류 로그·SimulationFatalError·자동 Init 비활성화를 재사용한다.
5. InitialChunkLoadBootstrapSystem도 같은 시드/초기 크기 검사를 큐 준비 전에 적용한다. 무효 초기 크기에서 큰 요청 버퍼가 만들어지는 것을 차단하며 이미 Fatal이면 오류를 중복 게시하지 않는다. 유효한 초기 요청의 큐·Pending/Ready·완료 수명은 유지한다.
6. 기존 ResourceGenerationConfigLookupUtility에 반경/매장량 범위 검사를 추가해 Loader와 생성기가 공유한다. 생성기의 무효 수치 입력은 생성 기록 전에 거절하며 기존 입력 clamp로 보정하지 않는다.
7. ResourceGenerationUtility는 광맥 중심, 후보 셀 및 거리 제곱을 long으로 계산한다. 대상 청크의 정수 경계를 통과한 셀만 int2로 변환한다. 후보/품목/셀 순서와 난수 호출 순서는 유지한다.
8. ChunkUtility는 완전한 청크가 int 셀 좌표로 표현되는지 검사한다. 지원 좌표를 벗어난 직접 bounds 호출은 ArgumentOutOfRangeException으로 거절한다. 주변 광맥 후보는 대상 청크 범위 밖에 있어도 long으로 계산할 수 있다.
9. FloorBiomeSampler는 전이 폭을 먼저 float로 변환해 청크 크기를 곱한다. 같은 샘플러의 순수 검사로 노이즈/영역 좌표 변환의 안전 조건을 도출하며 검증은 게시/Init 경계에서 수행한다.

설정 원본은 같은 ECS 엔티티와 버퍼이며 기존 World 수명을 따른다. 새 시스템·컴포넌트·캐시·Ready/Fatal 종류·설정 경로·partial 파일은 추가하지 않았다. 자원 EndCommand 확정, 다음 Command의 완료 등록, 최종 Synchronization의 공간 인덱스 갱신을 변경하지 않았다. 초기화 전체나 Command 묶음의 rollback 보장을 추가한 것은 아니다.

주요 소스: [상수](../../../../Assets/Scripts/Common/GameConstants.cs), [공통 자원 범위](../../../../Assets/Scripts/Common/ResourceGenerationConfigLookupUtility.cs), [Loader](../../../../Assets/Scripts/Config/WorldGenerationConfigLoader.cs), [Init](../../../../Assets/Scripts/Systems/Initialization/WorldGenerationConfigLoadSystem.cs), [Bootstrap](../../../../Assets/Scripts/Systems/Initialization/InitialChunkLoadBootstrapSystem.cs), [청크 변환](../../../../Assets/Scripts/Chunks/ChunkUtility.cs), [자원 생성](../../../../Assets/Scripts/Chunks/ResourceGenerationUtility.cs), [바닥 계산](../../../../Assets/Scripts/Chunks/FloorBiomeSampler.cs).

## 산술 코드 확인

- 반경 최대 2에서 max+1 ≤ 3, 두 거리 제곱의 합 ≤ 8이다. 주변 후보 범위는 1청크이며 반경 사각 순회는 축당 최대 5칸이다. 현재 네 품목의 대상 청크당 사각 반복량 상한은 9×4×25=900회다. 실행 시간 보장은 아니다.
- 초기 크기 최대 3에서 요청은 최대 9개, 원점 청크 좌표는 각 축 −1~1이다. 후속 셀 경계는 −16~31이므로 현재 초기 요청의 좌표·저장 수가 안전하다.
- 완전 청크 좌표는 각 축 −134,217,728~134,217,727에서 int 셀로 표현된다. 최대 반경의 이웃 후보까지 포함한 중간 셀은 −2,147,483,666~2,147,483,665에 이를 수 있어 long을 사용한다. int 변환은 대상 경계 검사 뒤에 수행한다.
- 자원 매장량은 max < int.MaxValue이며 max+1은 최대 int.MaxValue다. 양수 min과 순서 검사를 공유해 기존 난수 범위의 의미를 유지한다.
- 최대 int 영역/전이 값도 먼저 float로 변환하면 곱셈 결과는 유한하고 양수다. 전이 폭 0의 기존 반환과 전이 목록 정책을 유지한다.
- float로 변환한 int.MaxValue는 2^31로 반올림될 수 있다. 활성 노이즈 좌표의 절댓값 및 굴곡 이후 영역 좌표의 절댓값을 그 배타적 상한보다 작게 제한한다. 그 직전 float 값과의 간격을 고려하면 정수 변환 후 노이즈 +1 및 영역 이웃 ±1도 안전하다.
- 시드 해시의 의도적 uint 순환 연산은 변경하지 않았다.

## 실행 확인

### Unity 컴파일

- 연결된 Editor: Unity 6000.4.11f1, 대상 프로젝트 일치, ready, compiling:false, domainReloadInProgress:false, Play Mode stopped.
- 최종 recompile/status: completed, failed:false, errors:[], compilationFailed:false.
- Assembly-CSharp.dll: 2026-10-11T10:42:11.9927754Z. 최신 제품 소스 10:41:43.1096013Z보다 새롭다.
- Assembly-CSharp-Editor.dll: 2026-10-11T10:40:42.7088872Z. 최신 테스트 소스 10:36:43.7091698Z보다 새롭다.
- CLI shell의 STATUS_NO_INSTANCES와 Pipeline 비접속은 연결 경로의 제한이었다. 연결된 Unity MCP에서 Editor 상태와 컴파일을 확인했다. Editor 부재나 컴파일 실패로 해석하지 않았다.

### 정상 현재 결과의 수정 전후 비교

현재 JSON과 시드 1로 독립 World에서 3×3청크만 생성했다. 자원 결과를 위치/품목/매장량으로 정렬하고 바닥 48×48셀의 바이옴/변형/전이 선택을 해시했다.

- 자원: 수정 전후 모두 16개, 해시 A88ACA915E0A8673.
- 바닥: 수정 전후 모두 2,304셀, 해시 F1D3D3E13726FC8F.
- 이는 현재 작은 설정·시드의 실행 비교다. 다른 모든 설정/시드/좌표의 동일성은 코드의 연산 의미와 RNG 순서 보존으로 확인했으며 전수 실행으로 주장하지 않는다.

### 경계 선택 실행

독립 World와 임시 eval 코드로 28개 확인을 수행했다.

- 초기 0/1/2/3 허용 및 4 거절, 반경 3/범위 밖 정수 거절, 소수 초기 크기와 문자열 전이 폭 거절.
- 무효 파싱의 seed/size 초기값과 null 목록/바닥 출력 유지.
- 무효 초기/반경/바닥의 공개 게시 거절 시 설정 엔티티 0개 유지.
- 기존 0→3 게시 유지.
- 무효 초기/반경/바닥 사전 등록의 원본 엔티티 보존과 Fatal 1개. 무효 초기 크기 4의 원본 값 유지와 큐 미생성.
- 최대 int 영역/전이 값의 파싱·게시 및 int.MinValue/0/int.MaxValue의 바닥 소수 셀 샘플.
- 양 끝 완전 청크 bounds와 범위 밖 청크 거절.

추가로 좌표 양 끝의 청크 두 개를 각각 별도 World에서 반경 2로만 생성했다. 각각 자원 13개가 생겼으며 모두 요청한 청크 내부였다. 굴곡 진폭의 안전/초과 값과 노이즈 스케일 1/바로 다음 float 값의 경계 4개도 확인했다. 합계 34개 선택 확인이며 새 NUnit 테스트 메서드 34개를 작성한 것이 아니다.

### 기존 EditMode

- Phase4ResourceGenerationTests: 6/6.
- Phase4WorldGenerationConfigTests: 5/5.
- Phase4FloorBiomeGenerationTests: 3/3.
- Phase4ChunkLifecycleTests: 4/4.
- 합계 Passed 18 / Failed 0 / Skipped 0 / Inconclusive 0.
- 자원 테스트의 기존 반경 3~4 준비값만 승인된 최대 2에 맞췄다. 새 메서드나 assertion 보강은 없다. 통과 결과는 현재 지원 범위의 회귀이며 이전 반경 3~4 지원을 증명하지 않는다.

실행 JSON과 임시 eval 본문은 [F017 실행 근거](../../../../Logs/QualityEvaluation/F017-20261011/Verification.json)에 보존했다. Logs의 eval 본문은 Assets/Editor 테스트나 제품 컴파일 입력이 아니다.

## 보존·문서·미검증

- 현재 WorldGenerationConfig.json과 레거시 ResourceGenerationConfig.json, V2 메인/SubScene, EditorBuildSettings의 내용은 변경하지 않았다. 수정 전후 SHA-256 일치를 최종 확인했다.
- AGENTS, 현재 WorldAndResources 컴포넌트 문서, Task와 Q07 최신 안내를 갱신했다. 원본 평가/과거 검증 기록은 보존했다.
- F-017만 완료로 반영해 42/43이며 F-006은 미완료로 유지한다.
- 전체 EditMode, Play Mode, 실제 입력/카메라/바닥 렌더링, Player 빌드, 대규모 월드, 메모리/성능 측정은 실행하지 않았다.
- 건물 크기/드론 합산 R-01, 생산 속도 R-02, 다른 미완료 이슈, 카메라 로더 복원과 대규모 분할 로드는 포함하지 않았다.

