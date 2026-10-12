# 건물 티어·표시 이름

2026-10-11 사용자 확정과 현재 구현 계약. 기존 F-022 크기 원본 계약을 종류·티어 단위로 확장한다.

## 정의와 편집

- 식별자는 기존 BuildingTypeEnum과 명시적 양수 int 티어의 조합이다. enum 값·순서는 유지한다. 같은 조합은 한 정의만 허용하며 번호 연속성은 요구하지 않는다.
- BuildingConfig.json의 각 행은 tier와 displayName을 필수로 작성한다. 모든 티어는 독립 행이며 크기·속도·슬롯 수·건설 자재·초기 해금·연구 식별자는 그 행의 설정을 사용한다. 다른 티어의 값을 상속하거나 낮은 티어를 대신 선택하지 않는다.
- 표시 이름은 설정 소유의 FixedString128Bytes(최대 125 UTF-8 bytes)다. 공백만 있는 이름·누락·다른 JSON 형식·용량 초과는 오류이며 중복 이름은 허용한다. 이름을 변경해도 종류·티어 식별은 바뀌지 않는다. 설치 건물별 별칭이나 UI는 이번 범위가 아니다.
- 기존 실제 11종은 전부 tier 1과 현재 종류명을 displayName에 명시했다. 기존 수치와 자재·해금 조건은 유지하고 실제 상위 티어 데이터/신규 프리팹은 추가하지 않았다. 구조는 모든 종류의 티어를 표현하며 우선 창고의 추가 티어 데이터를 후속으로 편집한다.
- BuildingPrefabDatabaseAuthoring.Prefabs는 Type/Tier/Prefab을 명시적으로 등록한다. 서로 다른 조합이 같은 프리팹을 참조할 수 있다. ResourcesAutoLoad·LoadMode·PopulateFromResources와 건물 DB 전용 자동 채우기 Inspector는 제거했다. Item/Resource의 자동 탐색은 유지한다.
- footprint 원본은 해당 조합의 BuildingConfigElement.Footprint다. 프리팹 목록/DB에 크기·표시 이름을 복제하지 않는다. 동일 조합의 크기는 고정하고 방향 적용 전 크기를 저장한다.

## 초기화와 오류

제품 Config Init은 모든 기존 종류의 1티어를 요구한다. JSON·공개 PublishConfig·사전 등록 검증에서 무효 종류/티어·빈 표시 이름·중복 조합·비양수 크기·다른 조합을 가리키는 건설 비용을 거절한다. 사전 등록의 호환 런타임 버퍼도 동일한 종류·티어 집합이어야 한다. PublishConfig의 명시적 부분 구성은 격리 사례에서 유지한다.

PrefabDatabaseInitializationSystem은 기존 단일 DB·원형 생존/Prefab/LocalTransform 검사와 함께 Config에 등록한 모든 종류·티어의 프리팹을 요구한다. 중복·비양수 티어·설정 없는 등록도 거절한다. 실패는 기존 로그·Ready 미게시·SimulationFatalError·자동 재시도 없음 규칙이다. Config Init 실패는 부분 게시하지 않으며 무효 사전 등록 원본은 보존한다.

## 상태 소유와 전달

1. 배치 Producer는 PlacementRequestCandidateElement.TargetType/TargetTier와 해당 Config의 기본 크기 스냅샷을 Submit으로 접수한다. Command는 같은 조합의 해금·크기를 검증한다.
2. EndCommand에서 현장을 만들며 ConstructionSite.TargetBuildingType/TargetTier와 요구 자재를 보존한다. 현장 분류 BuildingType은 ConstructionSite/Tier 0이며 실제 목표 티어의 소유자는 ConstructionSite다.
3. BuildingStateApply 마지막의 완공은 공통 SpawnBuilding에 종류·티어·기본 크기·기존 PlacementStamp를 전달한다. 실패하면 현장/도착 자재를 보존한다. 성공한 새 완공 건물의 BuildingType.Type/Tier와 런타임 속도·슬롯 수를 EndBuilding에서 확정한다.
4. 직접 SpawnBuildingRequest도 TargetTier를 필수로 전달한다. (0,0) 크기만 생략이며 해당 티어의 Config를 사용한다. 미등록 종류·티어/프리팹 누락은 기존 중단 오류이며 요청은 자동 재시도하지 않는다.
5. 철거 환급은 완공 건물의 종류·티어와 일치하는 비용 행만 사용한다. 기존 보관/생산 실물 반환은 유지한다.
6. BuildingSpatialSyncSystem은 BuildingInfo.Type/Tier를 각 점유 셀에 복사한다. 크기/방향은 기존 footprint 계산과 Fence를 따른다. 같은 종류·티어의 기존 벨트 방향 변경만 허용하고 다른 티어 위의 배치는 거절한다.

표시 이름은 Config 조회 결과로 제공하며 건물마다 복제하지 않는다. 해금 상태와 RequiredResearch는 티어별 행이다. SetBuildingUnlocked는 지정 조합만 갱신하지만 실제 연구 진행 Writer는 후속이다. 티어 변경·기존 건물 승급 Writer는 추가하지 않았다.

## 후속과 검증

실제 추가 창고 티어 수치/명칭/프리팹, 표시 이름과 티어 선택 UI, 그림 자동 배율·회전·위치 보정, 기존 건물 승급, 연구 진행 연동은 후속이다. 극단적인 크기의 산술 상한은 이번 구현 범위에 포함하지 않는다. F-025는 기존 V2를 빌드·편집 공통 진입으로 선택하고 세 DB의 실제 베이킹/초기 검증을 확인해 완료했다. Play Mode·Player 빌드 실행 근거는 추가하지 않았다.
