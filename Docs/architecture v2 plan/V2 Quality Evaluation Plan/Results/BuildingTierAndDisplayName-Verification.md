# 건물 티어·표시 이름 구현·검증

2026-10-11. [사용자 확정 명세](../../../Specifications/BuildingTierAndDisplayName.md). F-022의 크기 원본을 종류·티어로 확장한 별도 후속이며 새 F 이슈 완료 수에 합산하지 않는다.

## 변경과 범위

- 기존 enum 값/순서를 유지하고 Config/호환 설정/건축 비용/프리팹/배치·직접 생성 요청/공사 목표/완공 BuildingType/BuildingInfo에 명시적 Tier를 연결했다. 조회와 해금 변경·자재 요구·환급은 종류·티어가 모두 일치하는 행만 사용한다.
- displayName은 Config의 필수 표시 이름이며 식별에 사용하지 않는다. 공백 이름·누락·다른 JSON 형식·125 UTF-8 bytes 초과를 거절하고 중복 이름은 허용한다. 티어는 명시적 양수 int이며 연속 번호를 요구하지 않는다. JSON의 소수·문자열·int 범위 밖 티어도 원시 토큰 경계에서 거절한다.
- JSON·공개 게시·사전 등록은 무효 키/중복 조합/빈 이름/비양수 크기/정의 없는 비용을 거절한다. 사전 등록 호환 버퍼도 같은 키 집합이어야 한다. Init의 기존 로그·미게시·Fatal·자동 재시도 없음 정책과 무효 사전 등록 원본 보존을 유지했다.
- Prefab 초기 검증은 Config에 등록한 모든 조합과 기존 11종의 1티어를 요구한다. 비양수 티어·중복·설정 없는 등록·누락·원형 문제는 Ready 미게시/Fatal이다. 같은 프리팹 참조를 여러 조합에서 사용할 수 있다.
- 건물 LoadMode/ResourcesAutoLoad/PopulateFromResources와 전용 자동 채우기 Inspector·meta를 제거했다. Item/Resource의 자동 채우기는 유지했다.
- 실제 BuildingConfig.json의 기존 11종은 tier 1/displayName=현재 종류명을 명시했다. 두 필드를 제외한 JSON 구조/값은 HEAD와 11행 모두 동일함을 비교했다. 새 상위 티어 데이터/실제 신규 프리팹은 추가하지 않았다.
- V2 SubScene의 기존 건물 DB 11항목은 Unity Editor 직렬화 API로 Tier 1을 기록했다. Git diff는 LoadMode 제거·Tier 1 추가뿐이며 기존 Type/Prefab 참조는 그대로다. SampleScene/빌드 진입 설정은 변경하지 않았다.
- 다른 티어의 기존 벨트 위 배치는 거절한다. 같은 종류·티어는 기존 방향 변경을 유지한다. 현장의 BuildingType은 ConstructionSite/Tier 0이며 목표는 ConstructionSite.TargetBuildingType/TargetTier다. 공통 Spawn에 목표 티어·기본 크기·설치 표식을 전달한다.

## 실행 근거

Unity CLI는 사용자 승인으로 1.0.0-beta.12에서 beta.13으로 업데이트했다. 대상 Editor는 프로젝트 지정으로 연결한 Unity 6000.4.11f1이다. 최초에는 대상 Pipeline 서버가 꺼져 연결이 불가능했으며 사용자가 Start Server를 실행한 뒤 연결했다. 다른 프로젝트의 열린 Editor에는 작업하지 않았다.

초기 컴파일에서 기존 벨트 회귀 assertion을 다른 테스트 메서드에 삽입한 위치 오류(CS0103: beltPos/beltEntity)를 발견하고 올바른 기존 벨트 통합 사례로 옮겼다. 수정 후 completed/failed:false/errors:[]와 Editor ready/비컴파일/비리로드를 확인했다. 최종 선별 실행은 기존 Verify-Unity.ps1의 임시 복사본에 CLI caller/skill 라벨만 추가해 수행했다. 최종 compile은 up_to_date이며 런타임/테스트 DLL의 최신성을 래퍼가 확인했다.

선별 EditMode **111/111 통과**, Failed/Skipped/Inconclusive 0, CLI 경고 0:

- Phase7BuildingAuthoringPrefabTests: 24/24
- PrefabDatabaseInitializationTests: 19/19
- Phase3BuildingRuntimeConfigTests: 1/1
- Phase7PlacementCommandTests: 24/24
- Phase7BuildingLifecycleTests: 11/11
- Phase7ConstructionCompletionTests: 8/8
- Phase7BuildingDemolishTests: 19/19
- Phase7EndToEndConstructionPipelineTests: 5/5

[최종 EditMode 결과](../../../../Logs/QualityImprovement/BuildingTierAndDisplayName/editmode-result.json).

새 통합 사례는 같은 Storage 종류의 비연속 3티어와 중복 표시 이름을 JSON으로 추가·게시하고, 1티어 해금으로 잠긴 3티어를 우회하지 못하는지 확인한다. 3티어 해금 후 Copper 3개 요구·부분 도착의 완공 보류·2×3 기본 크기의 Right 회전 3×2 점유·7슬롯·별도 테스트 프리팹 payload·설치 표식 보존·Left 직접 Spawn의 생략 크기·Copper 3개만 환급을 검사한다. 도착 자재는 fixture로 실물/Owner/도착량을 함께 준비하며 드론 운송을 실행한 증거는 아니다. 기존 벨트 통합 사례에 다른 티어 요청의 방향 변경/승급 방지 assertion을 추가했다.

초기화 오류 검증은 기존 사전 등록 보존 사례를 확장하고, malformed JSON 메타데이터와 추가 티어 프리팹 누락/중복/무효 티어/설정 없는 등록의 Ready 미게시·Fatal을 확인한다. 기존 fixture 생성자는 1티어·표시 이름을 명시하도록 기계적으로 이행했으며 미관련 드론/라우팅 전체 테스트를 추가 실행하지 않았다.

실제 Baker 검증은 저장된 V2 SubScene을 PreviewScene으로 읽어 **11항목 모두 Tier 1**임을 확인하고, 그 PreviewScene 안에서만 같은 Storage 프리팹을 공유하는 3티어를 추가했다. 설치된 Entities의 실제 BakeGameObjects로 독립 World에 **12항목**을 베이킹했다. 실제 산출물로 3티어 2×3 창고의 4방향 현장→완공 4건/직접 Spawn 4건을 실행하여 현장 24셀·최종 정확한 48셀·7슬롯·목표/완공/인덱스의 3티어·완공 ReceiptSequence·Fatal 부재를 확인했다. 이 8건은 EditMode 111건과 별도의 실행 근거이며 실제 프로젝트 데이터에는 3티어를 저장하지 않았다.

[실제 Baker 최종 결과](../../../../Logs/QualityImprovement/BuildingTierAndDisplayName/baker-result.json), [검증 스크립트](../../../../Logs/QualityImprovement/BuildingTierAndDisplayName/BakerSpawnProbe.cs), [장면 이행 스크립트](../../../../Logs/QualityImprovement/BuildingTierAndDisplayName/MigrateBuildingPrefabTiers.cs).

## 미실행·후속

전체 EditMode·Play Mode·플레이어 입력/UI·시각적 그림 보정·자동 SubScene 게임 시작·Player 빌드·성능·저장 호환성은 실행하지 않았다. 실제 신규 티어 밸런스/명칭/프리팹, 선택/표시 UI, 기존 건물 승급, 실제 연구 Writer는 후속이다. R-01 산술 상한·F-006/F-017/F-025 등 기존 미완료 항목과 전체 타입/아이템/자원 실제 Bake의 공백을 이번 선택 검증으로 해소했다고 쓰지 않는다. F-022/기존 F-021 실행 기록은 당시 구조의 근거로 보존한다.

## 2026-10-11 관련 이슈 후속 정리

구현 이후 별도 사용자 요청으로 최신 Task·직접 코드·위 실행 원본을 대조했다. 이번 후속은 문서만 변경하고 새 컴파일/테스트/Bake는 실행하지 않았다. 위 111건과 실제 Baker 8건은 같은 구현의 기존 근거이며 이슈마다 다시 합산하지 않는다.

- **종료한 후속 범위:** F-022에서 남겼던 종류·티어 지원과 별도 표시 이름 구조, 건물 프리팹 명시 등록/자동 탐색 제거를 구현 완료로 정리했다. 실제 상위 티어 밸런스·명칭·프리팹, UI·그림 보정·기존 건물 승급·연구 Writer는 계속 후속이다. Architecture Task 7.2/7.3.1/7.3/7.5/7.7/7.8에 현재 키·상태 소유자·실행 경계를 추가하고 초기 구현 설명/과거 테스트 수와 구분했다.
- **기존 완료·추적 종료 유지:** F-009/F-011/F-012의 입력/문자열/오류 전달, F-018/F-019의 등록 키·필수 원형, F-020의 티어별 환급, F-021의 명시 Baker 경로, F-022/F-023의 크기 원본/회전, F-024의 완공 보존, F-041의 설치 표식 승계에 직접 영향과 선택 검증 한계를 연결했다. 삭제한 건물 자동 수집 경로를 현재 필수 검증 대상으로 안내하지 않는다. Item/Resource 자동 수집은 남아 있고 전수/오류 Bake·자동 시작·Player 빌드의 공백을 종료했다고 쓰지 않는다.
- **미완료 범위 유지:** F-025의 새 이관 계약은 건물 Type/Tier/Prefab과 Config의 명시 tier/displayName/footprint다. 활성 빌드는 여전히 SampleScene이므로 진입 장면 선택·구형 GUID/enum·세 DB 검증/이관을 남겼다. F-017의 월드 산술과 R-01의 면적·좌표·자재 누적/실행량 상한은 개별 tier/int 검증과 구분하여 유지한다. F-006은 ProductResultApplyJob의 누락 프리팹 진단 뒤 마지막 Clear가 현재 소스에 남아 있으므로 보존/소비 정책 보류를 유지한다.

[품질 개선 Tasks](../V2%20Quality%20Improvement%20Tasks.md)의 관련 14개 항목을 갱신했다. 추가 F 완료/종료는 **0건**, 집계는 **40/43**, 미완료는 **F-006/F-017/F-025**다. 지원 구조 후속의 완료를 기존 F-022에 다시 합산하지 않는다. [Architecture Tasks](../../Architecture%20V2%20Tasks.md)와 [R-01 현재 검토](CurrentCodeReview-2026-10-06.md#r-01--건물-크기와-새-현장-자재-합산의-산술-한계)도 같은 경계로 연결했다.
