# 드론 정거장 등록·범위·네트워크

2026-10-22 Task 9.2의 사용자 확정 1A·2A·3A·4A·5A와 단일 Range·정사각형 활동 영역 계약이다. 정거장 연결과 공개 조회를 구현하며 작업 필터(9.5), 수행자/수용량(9.3·9.3.1), 귀환(9.13), 충전(9.14), 정거장 철거의 드론 재배치(9.19)는 후속이다. [Lifecycle 계약](DroneLifecycle.md)의 작업·예약·화물 소유자와 공개 규칙은 유지한다.

## 설정과 활동 영역

- 유일한 설정 출처는 `BuildingConfig.json`의 MainFacility/DroneStation 종류·티어별 `droneStationRangeInChunks`다. 두 기존 Tier 1은 1이다. `BuildingConfigElement.DroneStationRangeInChunks`로 게시하며 다른 종류는 0이다. DroneConfig.json의 두 활동 범위 축 필드는 제거하고 다른 미연결 드론 설정은 유지한다.
- 대상 종류에는 명시적인 int JSON 정수가 필수다. 누락·소수·문자열·int 범위 초과·새 필드의 X/Y 분리는 파싱 실패이며 출력 목록을 게시하지 않는다. 다른 건물 종류에 범위 설정을 추가해도 거부한다. 음수는 0으로 사용한다. 기존 초기화의 미게시·공통 Fatal·자동 재시도 없음 계약을 재사용한다.
- 범위는 앵커가 속한 청크와 무관하다. `BuildingFootprintUtility.GetEffectiveSize`로 회전한 크기의 긴 변을 기준으로 정사각형을 만든다. 짧은 축의 부족한 셀을 양쪽으로 나누며 홀수로 남는 1셀은 양의 축에 둔다. 실제 점유 footprint와 위치를 수정하지 않는다.
- 이 정사각형의 양쪽 경계를 `max(0, Range) × GameConstants.ChunkSize`만큼 확장한다. 경계 셀을 포함한다. 좌표·여백·청크 배수의 합산은 long을 사용하며 넓은 영역을 셀별로 열거하지 않는다.

| 위치 / 기본 크기 / 방향 / Range | 활동 영역 (포함 경계) | 크기 |
| --- | --- | --- |
| (0,0) / 3×3 / Up / 1 | (-16,-16) ~ (18,18) | 35×35 |
| (0,0) / 2×4 / Up / 1 | (-17,-16) ~ (18,19) | 36×36 |
| (0,0) / 2×4 / Right / 1 | (-16,-17) ~ (19,18) | 36×36 |
| (0,0) / 2×3 / Up / 0 | (0,0) ~ (2,2) | 3×3 |

## 참가·연결·식별

공통 `BuildingLifecycleUtility`는 주 시설과 드론 정거장에 DroneStation, DroneStationNetworkDecision(초기 비활성), DroneStationNetwork(초기 Null)를 함께 부착한다. 직접 스폰과 공사 완공이 같은 구성을 사용한다. 공사 현장·Prefab·Disabled·철거 승인 대상은 활동 정거장에서 제외한다. 전력 공급, PowerConsumer enable, 배터리·보관 공간은 망 참가 조건이 아니다. 충전·수용 가능 여부는 후속 소유자가 별도로 판정한다.

두 정거장 영역이 한 셀 이상 겹치면 연결한다. 공유 셀이 없는 단순 인접은 연결하지 않는다. 연결은 전이적이며 A↔B와 B↔C이면 같은 망이다. 대표는 기존 PlacementStamp 비교(표식 유무·Tick·접수 순서·Order·좌표)를 사용한다. 정상 건물 배치의 고유 점유/좌표 계약을 신뢰하며 Entity 생성 번호로 동률을 보완하지 않는다. 대표 정거장 Entity가 런타임 망 식별자이며 별도 망 Entity·영속 번호는 없다. 대표 제거·이동·비활성화·병합·분리로 식별자가 바뀔 수 있다.

정거장별 범위를 보존한 합집합이 망의 활동 영역이다. 망 전체의 외곽 사각형에 있는 빈 공간을 포함하지 않는다. 전력망과 식별자·연결 규칙·참가 수명을 공유하지 않는다.

## Writer·소비자·공개 경계

| 데이터 | 단독 Writer / 단계 | 소비·수명 |
| --- | --- | --- |
| 종류·티어·위치·방향·footprint | 기존 건물 생성/수명주기 원본 | 이번 틱 EndBuilding 확정 상태를 사용 |
| 범위 설정 | 기존 BuildingConfig 초기화/게시 | World 설정 수명; 실행 중 재로드 없음 |
| DroneStation 역할/기본 구성 | 공통 건물 생성 → EndBuilding | 건물 삭제와 함께 제거 |
| DroneStationNetworkDecision | DroneStationNetworkDecisionSystem / DroneDecision OrderFirst | 매 틱 전체 정거장을 초기화한 뒤 활성 참가자만 enable. 같은 틱 판단은 이 연결 결과와 건물 원본을 읽음 |
| DroneStationNetwork | DroneStationNetworkApplySystem / DroneStateApply → EndSimulation | 준비된 결과를 공개하고 비활성/철거 승인 참가자의 연결을 Null로 비움 |
| DroneStationSpatialIndex / Fence | DroneStationSpatialSyncSystem / Synchronization | 최종 공개 연결과 건물 원본으로 Clear/Populate. World 종료 때 Fence 완료 후 Dispose |

Decision의 입력 Entity 목록은 비동기로 수집하고 deferred array로 전달한다. 정거장별 범위와 union-find 부모는 해당 호출의 TempJob 자료이며 마지막 사용 뒤 의존성에 연결해 해제한다. 연결 성분의 경합하는 union/대표 변경은 단일 Burst Job이 처리한다. 정거장 쌍 비교는 O(n²)이며 성능 측정/증분 알고리즘은 이번 검증 범위가 아니다. 호출별 자료를 ECS 위치·연결의 별도 영속 원본으로 게시하지 않는다.

공통 생성으로 보장된 종류·티어 설정 행을 읽을 수 없으면 Decision은 EndSimulation에 공통 Fatal을 기록하고 연결 결과를 준비하지 않는다. 전력 부족·정거장 부재는 오류가 아니다. 기존 NextTick Fatal과 현재 틱 rollback 부재는 유지한다.

예: A와 C 사이를 연결하는 B가 이번 EndBuilding에서 생성되면 이번 DroneDecision의 연결 결과는 A·B·C를 같은 망으로 나타낸다. 공개 DroneStationNetwork는 EndSimulation 전까지 이전 값이다. 같은 틱 후속 판단은 이전 공개 인덱스를 사용하지 않는다. 새 작업/배정/경로는 기존 계약대로 다음 틱부터 사용한다. 실제 작업 후보에 범위 필터를 연결하는 것은 9.5다.

## 공개 범위 조회와 공유 자료 수명

DroneStationSpatialIndex.Map은 정거장 Entity → DroneStationCoverage(대표·활동 영역)이다. 위치·셀 점유를 별도 원본으로 소유하지 않으며 셀 수에 비례한 메모리를 할당하지 않는다. Contains는 소속 정거장 영역 중 하나가 셀을 포함하는지 검사한다. 현재 제품 Reader는 후속이며 실제 경로·에너지·수용량 성공을 이 맵으로 대신 판단하지 않는다.

소유자는 인덱스와 Fence를 같은 singleton에 생성한다. Job Reader는 자신의 Dependency와 GetReaderDependency를 결합하고 최종 핸들을 AddReader에 등록한다. 동기 Reader는 마지막 Writer를 기다린다. Writer는 GetWriterDependency로 이전 Writer와 모든 Reader를 기다리고 SetWriter에 최종 핸들을 등록한다. Capacity 변경·Dispose는 Fence.Complete 뒤 수행한다. 0개일 때도 Clear한다. ECS 컴포넌트 의존성과 Native 맵 Fence, EndSimulation ECB Producer 등록은 별개다.

## 검증

새 DroneStationNetworkTests는 기존 테스트가 다루지 않는 생성/연결/공개/조회 흐름과 정사각형 계산·망 분리·대표 변경을 검증한다. EndBuilding 스폰 뒤 공개 전 판단, 무전력 참가, 연결 정거장 제거/Disabled/철거 승인, 이동/재연결, 포함 경계와 단순 인접, 회전/홀수 여백, 망 내부 미포함 영역, 초기 용량 초과와 빈 맵, 종류·티어별 범위·음수와 큰 정수, 무효 JSON의 미게시를 다룬다.

2026-10-22 Unity 6000.4.11f1에서 최종 `recompile_status=up_to_date`, `failed=false`, `errors=[]`, `compilationFailed=false`와 Editor ready/비컴파일/비리로드/Play stopped를 확인했다. Runtime DLL(06:45:13 UTC)은 마지막 runtime 소스(06:43:41 UTC)보다 새롭고, Editor DLL(07:01:49 UTC)은 마지막 테스트 소스(07:01:40 UTC)보다 새롭다. 검증 래퍼의 기존 CompileOnly 모드 판정 오류로 개별 Unity CLI 경로를 사용했고, Pipeline 0.9의 그룹형 명령으로 컴파일·선택 EditMode를 확인했다.

| 선택 EditMode 클래스 | 최종 통과/전체 |
| --- | ---: |
| DroneStationNetworkTests | 14/14 |
| Phase3BuildingRuntimeConfigTests | 5/5 |
| Phase8PowerContractTests | 26/26 |
| Phase7BuildingLifecycleTests | 11/11 |
| Phase7ConstructionCompletionTests | 8/8 |
| Phase7BuildingAuthoringPrefabTests | 24/24 |
| Phase7EndToEndConstructionPipelineTests | 5/5 |
| DroneLifecycleIntegrationTests | 6/6 |
| 합계 | 99/99 |

최종 실패·생략·불확정은 모두 0이다. 기존 건설 통합 fixture의 첫 실행은 5개 중 3개가 실패했다. Preparation/Power 없는 옛 그룹 구성으로 Command→Building 순서가 연결되지 않았고 Miner 준비·공급 입력도 없었다. BND92-E01(P2)을 수용해 해당 fixture에 현재 그룹 체인·MinerWorkPreparation과 명시적 격리 공급을 보완하고 기존 assertion 변경 없이 5/5 재실행을 확인했다. 이 fixture는 실제 전력망/배분/연료 통합의 증거가 아니다.

독립 검수 Task92-v1은 초기화·Decision·StateApply 공개·Synchronization·컴포넌트·guard·경계 7역할로 수행했다. 정거장 게임 코드의 확인 범위에서 지적이 없었고, 기존 fixture P2 1건은 Task92-v2 경계 재검수에서 해소를 확인했다. 일반 검수는 프로필의 Luna/xhigh, 경계는 Sol/xhigh를 사용했다. Command·Preparation·Reservation·Execution의 제품 Writer는 바뀌지 않아 그 독립 역할은 추가 실행하지 않았다. 최종 문서의 링크/Task 집계와 검수 이후 코드·설정 동일성도 별도로 확인한다.

이 수치는 이번 선택 테스트의 최종 결과이며 과거 174/174 등과 합산하지 않는다. 전체 EditMode·Play Mode·실제 드론 이동·베이킹·Player 빌드·성능 검증은 미수행이다.
