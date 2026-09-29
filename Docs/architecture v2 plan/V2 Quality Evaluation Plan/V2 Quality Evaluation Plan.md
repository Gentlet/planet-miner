# V2 품질 평가 계획 및 진행 기록

작성일: 2026-09-28  
상태: Q00·Q01a·Q01b·Q02·Q03·Q04·Q05·Q06·Q07·Q08·Q09·Q10·Q11·Q12·Q13·Q14·Q15·Q16·Q17·Q18·Q19·Q20·Q21·Q22·Q23·Q24·Q25·Q26·Q27·Q28·Q29·Q30a·Q30b·Q31 평가 완료 / 종합 평가 종료

## 1. 확정한 목적과 범위

현재 구현된 V2의 **안정성, 유지보수성, V2 설계 적합성**을 평가한다. 출시 준비도나 미구현 기능의 완성도를 점수화하는 작업은 아니다.

**V2 설계 적합성의 핵심은 시스템 간 직접 결합과 변경 영향 범위를 줄였는지 확인하는 것이다.** 한 시스템을 이해하거나 수정하기 위해 다른 시스템의 내부 구현과 세부 실행 순서를 얼마나 알아야 하는지를 모든 도메인 평가에서 확인한다. Phase별로 파일을 나누거나 요청 컴포넌트를 사용한 사실만으로 이 목표를 달성했다고 판단하지 않는다.

| 항목 | 확정 기준 |
| --- | --- |
| 목적 | 현재 코드의 안정성과 유지보수성, V2 구조에 맞는 책임·데이터·실행 흐름인지 확인 |
| 대상 | 현재 구현된 V2 코드와 그 구현에 연결된 Config, 로더, Authoring, Baker, 프리팹 DB, 관련 Editor 도구 및 테스트 |
| 연결 확인 | 위 계약을 검증하는 데 필요한 프리팹·SubScene·장면·설정 참조만 포함 |
| 검증 | 코드 분석 후 필요한 관련 EditMode 테스트 |
| 문제 처리 | 평가 문서에 근거와 영향 기록. 수정은 별도 결정 |

미구현 기능은 그 자체로 결함으로 처리하지 않는다. 다만 현재 구현이 그 기능을 필수 조건으로 사용하여 실행 경로가 끊기는 경우에는 해당 의존성을 평가한다. 전력·드론·연구·UI 등은 평가 시점에 실제 V2 구현이나 직접 의존성이 확인된 범위만 포함한다.

실제 플레이, 입력, 시각 결과, 성능 수치, 빌드 플랫폼별 동작은 이번 코드 분석과 EditMode 결과만으로 보장하지 않는다. 필요하면 사용자 수동 확인 항목으로 남긴다.

## 2. 기준 문서와 판단 방법

- 프로젝트 공통 규칙과 검증 절차: [AGENTS.md](../../../AGENTS.md)
- V2 설계 의도: [Architecture V2 Plan_0.2.md](../Architecture%20V2%20Plan_0.2.md)의 구현 판단 기준, 2.1 핵심 목표, 14 System Dependency 규칙과 해당 도메인 절
- 요청 수명주기: [Command/Event 규약](../Architecture%20V2%20Command%20Event%20Standard.md)
- 베이킹과 런타임 경계: [Authoring/Prefab 규약](../Architecture%20V2%20Authoring%20Prefab%20Contract.md)
- 탐색 보조: [CodeMemory](../CodeMemory/README.md)

이 문서는 평가 순서와 결과를 관리한다. 공통 개발 규칙 전문을 복제하거나 새로운 게임 규칙을 정의하지 않는다.

현재 동작은 소스의 실제 작성자·소비자·쿼리·분기·Job 의존성·ECB 재생 시점으로 확인한다. 설계 적합성은 그 동작을 합의된 V2 원칙과 비교하여 판단한다. 현재 코드가 존재한다는 이유만으로 설계에 적합하다고 결론내리지 않는다.

문서와 코드가 다르면 `문서 불일치`, `구현 결함`, `설계 판단 필요`를 구분한다. 오래된 문서만을 근거로 코드를 결함으로 판정하지 않으며, 의도가 불명확하면 질문을 기록한다.

### 계획 작성 시 확인한 범위

- 현재 소스·테스트 파일 경로, V2 핵심 원칙, Authoring/Baker 클래스, 검증 래퍼의 인자를 확인했다.
- `ConstructionLifecycleApplySystem`에는 취소 → 자재 수령 → 완료 Job을 순차 예약하는 코드가 있고, `BuildingLifecycleApplySystem`에는 철거 처리 코드가 있다. 관련 완료·취소·철거 테스트 파일도 존재한다.
- 이 내용은 제공된 AGENTS.md의 일부 미구현 설명과 다르므로 공사 완료·취소·철거를 아래 평가 대상에 포함했다. 호출 경로 전체의 정합성이나 실제 테스트 통과를 확인한 것은 아니다.
- 기존 루트 `PROJECT_EVALUATION_PLAN.md`의 작업 트리 삭제 상태는 변경하지 않았다.
- 이번 작성에서는 코드·에셋을 수정하지 않았고 컴파일·EditMode·Play Mode를 실행하지 않았다. 품질 판정과 문제 등록은 아직 하지 않았다.

## 3. 작은 단계로 진행하는 방식

실행 요청 예시는 [단계별 명령 프롬프트](V2%20Quality%20Evaluation%20Prompts.md)를 사용한다. 각 프롬프트는 해당 단계 하나만 수행하며, 최신 범위와 정책은 이 계획을 따른다.

**기본 진행 단위는 아래 표의 한 행이다.** 한 번의 평가 요청으로 전체 단계를 자동 진행하지 않는다. 여러 단계를 명시적으로 요청하면 그 범위만 진행한다.

1. 대상 단계의 파일·심볼과 직접 의존성, 관련 테스트를 좁힌다.
2. 상태 원본, 작성자, 소비자, 처리 순서와 실패·대기 경로를 추적한다.
3. 세 평가 축으로 검토하고 문제 후보에 구체적인 근거를 붙인다.
4. 실행 확인이 필요한 질문을 정한 후 그 질문에 대응하는 기존 EditMode 테스트를 선택한다.
5. 결과·미확인 사항·문제 ID·다음 단계를 기록하고 해당 단계에서 멈춘다.

한 단계에 독립적인 흐름이 여러 개 발견되면 `Q12a`, `Q12b`처럼 나눈다. 관련 없는 영역으로 평가를 확대하지 않는다. 다른 영역의 문제가 의심되면 후속 단계에 연결하고, 현재 판단에 꼭 필요한 직접 의존성만 추가 확인한다.

평가 중 소스·Config·프리팹·기존 설계 문서를 수정하지 않는다. 테스트 추가·수정이 필요하면 검증 공백과 제안 사례를 기록하고 별도 수정 결정으로 넘긴다. 기존 관련 테스트 실행과 평가 기록 갱신은 평가 범위에 포함한다.

### 공통 평가 질문

| 축 | 확인할 질문 | 판단 근거 예시 |
| --- | --- | --- |
| 안정성 | 누락·중복·무효 참조·재진입·경합·실패 시 상태가 일관적인가? | 조건 분기, 소유 버퍼와 엔티티 상태, 예약 결과, ECB 전후 상태, 관련 테스트 assertion |
| 유지보수성 | 책임과 계약을 이해하기 쉬운가? 변경 시 수정 위치가 명확한가? | 중복 규칙, 숨은 순서 의존, 큰 메서드의 서로 다른 책임, 명칭·주석 불일치, 테스트 재현 가능성 |
| V2 적합성 | 시스템 간 직접 결합과 변경 영향이 제한되는가? 상태 소유권, Phase 책임, 데이터 통신, 원본/파생 데이터 구분이 지켜지는가? | 공개 데이터 계약과 내부 구현 의존의 구분, 실제 상태 작성 경로, 요청 소비 정책, Job/Fence 의존성, Baker와 Spawn Owner의 경계 |

Managed 시스템이나 메인 스레드 처리가 있다는 사실만으로 부적합 판정하지 않는다. 해당 책임과 필요성을 확인한다. 성능은 코드상 위험 후보까지 기록하며, 측정 없이 병목이나 성능 개선 효과를 확정하지 않는다.

### 시스템 간 의존성 평가 — 모든 도메인의 필수 항목

V2 계획의 목표는 의존성 0이 아니다. 공개된 컴포넌트·요청·결과·조회 계약을 통한 협력과 정확성에 필요한 Phase/Job/Fence/ECB 의존성은 존재할 수 있다. 평가 대상은 그 계약을 넘어 타 시스템의 내부 상태·처리 순서·정리 방식에 기대는 결합이다.

| 구분 | 확인할 질문 | 남길 근거 |
| --- | --- | --- |
| 직접 호출·수명주기 제어 | 다른 시스템의 Update·초기화·정리·활성화를 직접 제어하는가? 프레임워크의 그룹 실행·ECB 접근인지 게임 도메인 간 제어인지 구분했는가? | 호출자→대상, 호출 목적, 대체 가능한 데이터 계약 |
| 공유 상태·소유권 | 다른 도메인이 소유한 상태를 직접 쓰거나, 여러 시스템이 같은 규칙을 각자 유지하는가? | 상태별 Owner와 실제 Reader/Writer, 조정 책임과 중복 규칙 |
| 시간·실행 순서 | 공개된 Phase·요청 소비·ECB 경계만으로 이해할 수 있는가? 특정 시스템이 먼저 실행하거나 정리한다는 숨은 가정이 있는가? | UpdateBefore/After, Job/Fence, 데이터 가시성, 순서가 필요한 이유와 누락 시 영향 |
| 데이터 계약 | 요청을 보내려면 소비자의 내부 버퍼·임시 플래그·중간 상태를 알아야 하는가? | Producer/Consumer, 입력·출력·성공/실패/대기 계약, 내부 상태 노출 |
| 순환·의존 집중 | A→B→A 관계가 공개된 요청/결과 흐름인가, 서로의 내부를 바꿔야 하는 순환 결합인가? 한 시스템이나 Utility에 무관한 도메인 지식이 집중되는가? | 관계 경로와 이유, 책임 경계, 변경이 전파되는 지점 |
| 변경 영향·검증 독립성 | 한 도메인의 내부 규칙을 바꾸면 어떤 다른 시스템을 함께 수정해야 하는가? 관련 시스템 전체를 기동해야만 국소 계약을 검증할 수 있는가? | 변경 사례 하나와 영향 파일/이유, 테스트의 필수 협력 계약과 불필요한 준비 의존 |

각 단계에서 확인한 관계만 아래 표에 누적한다. Q01b에서 기록 형식과 Phase/ECB 공통 경계를 정하고, 도메인 내부까지 한 번에 전체 조사하지 않는다. Q30b에서 누적 결과로 전체 관계와 핵심 문제를 정리한다.

| 의존하는 시스템 → 대상 시스템/데이터 | 의존 형태 | 공개 계약 또는 숨은 가정 | 필요한 이유 | 변경 전파 범위 | 근거·판정·문제 ID |
| --- | --- | --- | --- | --- | --- |
| 평가 시 기록 | 호출 / 읽기 / 쓰기 / 순서 / 수명주기 | 계약과 구현 세부를 구분 | 정확성·책임 기준 | 관련 파일·심볼과 이유 | 코드 위치 / 필요한 계약·개선할 결합·판단 보류 |

변경 영향 분석은 코드를 실제로 수정하는 실험이 아니다. 예를 들어 저장 용량 계산의 내부 구현을 바꾸되 공개 계약은 유지한다고 가정하고, 현재 호출·데이터 흐름상 함께 수정해야 할 위치를 추적한다. 공개 계약 자체가 바뀔 때의 정상적인 소비자 변경과 구분한다.

의존성 개수나 UpdateAfter 개수만으로 품질을 판정하지 않는다. 필요한 동기화를 제거하거나 여러 책임을 하나의 거대한 시스템으로 합쳐 의존 관계가 줄어 보이는 경우도 개선으로 간주하지 않는다. 과거 구현과 동일 범위의 비교 근거가 없으면 “V1보다 의존성이 감소했다”고 확정하지 않고, 현재 V2의 결합 상태와 목표 적합성을 평가한다.

## 4. 단계별 진행표

아래 테스트는 **선택 후보**다. 각 단계의 실제 검증 질문에 맞춰 클래스 또는 메서드로 좁힌다. 파일이나 클래스의 존재는 테스트 통과 증거가 아니다. 같은 테스트를 여러 단계에서 참조하더라도 소스·환경·검증 대상이 같으면 기존 실행 근거를 연결하고 불필요하게 재실행하지 않는다.

모든 경로는 프로젝트 루트 기준이다. 단계 번호 `Qxx`는 평가 순서이며 런타임 Phase나 개발 마일스톤 번호가 아니다.

| ID | 한 단계의 평가 범위 / 시작점 | 중점 확인 및 남길 결과 | 관련 테스트 후보 | 상태 |
| --- | --- | --- | --- | --- |
| Q00 | 현재 기준선과 대상 확정 | HEAD·작업 트리 변경·Unity/패키지 버전, 실제 구현 목록과 문서 차이, 평가 제외 범위 기록 | 실행 없음 | [평가 완료](Results/Q00.md) |
| Q01a | `Assets/Scripts/Phases/`, 두 End ECB 시스템 | 6단계와 실제 정렬, 구조 변경 기록·재생 경계 표 | 관련 도메인의 그룹 통합 테스트를 필요 시 선택 | [평가 완료](Results/Q01a.md) |
| Q01b | Phase/ECB 공통 의존 계약과 평가 기준선 | 공개된 실행·데이터 가시성 계약과 내부 순서 가정 구분, 의존 관계표 시작. 도메인별 관계는 후속 단계에서 누적 | 필요 시 Q01a 실행 근거 재사용 | [평가 완료](Results/Q01b.md) |
| Q02 | Item 요청·상태 컴포넌트와 `ItemOwnershipApplySystem` | 상태 Owner, 소유권·렌더 태그·버퍼 변경의 역할 분담과 요청 소비 추적 | `Phase3StorageOwnershipTests`, `Phase1ItemIntegrationTests` | [평가 완료](Results/Q02.md) |
| Q03 | `ItemLifecycleApplySystem` | 생성·삭제·생산 결과 소비, 중복/무효 요청, 저장품 삭제 전 참조 정리 | `Phase1ItemIntegrationTests`, `Phase1ItemAuthoringPrefabTests` | [평가 완료](Results/Q03.md) |
| Q04 | `ItemConfigInitSystem`, Item Config·Registry | 설정 입력→검증→게시, 기본값·수명·MaxStack 계약 | `Phase3ItemConfigTests` | [평가 완료](Results/Q04.md) |
| Q05 | `BuildingConfigLoader`, 설정 게시 시스템·컴포넌트 | 건물/자재/런타임 설정의 일관성, 검증 실패와 게시 범위 | `Phase3BuildingRuntimeConfigTests`, `Phase7ConstructionContractTests` | [평가 완료](Results/Q05.md) |
| Q06 | `RecipeConfigLoader`, `RecipeInitSystem`, 레시피 Config | 재료·주생산품·부산물·Blob 게시와 해제 | `Phase5RecipeBlobTests` | [평가 완료](Results/Q06.md) |
| Q07 | `WorldGenerationConfigLoader`와 게시 시스템 | 자원·바닥 통합 검증, Sprite 참조, 부분 게시 방지 | `Phase4WorldGenerationConfigTests`, `Phase4FloorBiomeGenerationTests` | [평가 완료](Results/Q07.md) |
| Q08 | Item Authoring·DB·Baker와 직접 관련 Inspector | 정적 식별 정보와 런타임 초기화, 중복 등록·누락 정책 | `Phase0AuthoringPrefabContractTests`, `Phase1ItemAuthoringPrefabTests` | [평가 완료](Results/Q08.md) |
| Q09 | Building DB Authoring·Baker와 직접 관련 Inspector | 타입·크기·방향·DB 참조와 Spawn 계약 | `Phase7BuildingAuthoringPrefabTests`, `Phase0AuthoringPrefabContractTests` | [평가 완료](Results/Q09.md) |
| Q10 | Resource DB Authoring·Baker | 자원 프리팹 계약, 준비 대기와 생성 경계 | `Phase4ResourceAuthoringAndSpawnTests` | [평가 완료](Results/Q10.md) |
| Q11 | 위 DB의 실제 프리팹·SubScene·장면 연결 | 필요한 직렬화 참조만 추적, 테스트용 프리팹과 실제 연결 차이 기록 | 관련 Authoring 테스트의 보장 범위 확인; 실제 베이킹은 별도 증거가 있어야 검증됨으로 기록 | 완료 — 코드·직렬화 분석; 실제 베이킹 미확인. [결과](Results/Q11.md) |
| Q12 | `*SpatialIndex`, `*SpatialSyncSystem` | 원본/인덱스, Fence, Clear·재등록·용량·Dispose, 동일 프레임 가시성. 필요 시 인덱스별 분할 | `Phase3BuildingSpatialIndexTests`, 관련 통합 테스트 | [평가 완료](Results/Q12.md) — 코드 분석·선택 EditMode 3건 통과 |
| Q13 | Belt Decision→Execution | 간격·수용량·경계 이동, enable 상태, 이동 계획 소비 | `Phase2BeltDecisionTests`, `Phase2BeltExecutionTests`, `Phase2BeltIntegrationTests` | [평가 완료](Results/Q13.md) |
| Q14 | Building Input→Storage Reservation→Apply | 필터·슬롯·MaxStack·동시 입고와 소유권 인계 | `Phase3BuildingInputDecisionTests`, `Phase3StorageDecisionTests`, `Phase3StorageInvariantTests` | [평가 완료](Results/Q14.md) |
| Q15 | Storage/Product Output→예약→Apply | 출고 선택, 목적지 용량, 거부 시 상태 보존, 생산품 슬롯 우선순위 | `Phase3BuildingOutputDecisionTests`, `Phase6BeltDestinationReservationTests` | [평가 완료](Results/Q15.md) |
| Q16 | Splitter/Merger→목적지 예약→Routing Apply | 포트·회전·경합·커서·설치 우선순위, 일반 벨트 이동과 예약 범위 구분 | `Phase6RoutingContractTests`, `Phase6SplitterPipelineTests`, `Phase6MergerPipelineTests`, `Phase6ConnectionChangeTests` | [평가 완료](Results/Q16.md) |
| Q17 | 초기 청크 Bootstrap→Load Command | 중복 요청, Pending/완료 추적, 다음 프레임 완료 알림 소비 | `Phase4ChunkLifecycleTests` | [평가 완료](Results/Q17.md) |
| Q18 | Resource Generation→ECB→Spatial Sync | 결정론·청크 경계·프리팹 준비 대기·빈 청크 완료 | `Phase4ResourceGenerationTests`, `Phase4ResourceAuthoringAndSpawnTests` | [평가 완료](Results/Q18.md) |
| Q19 | `FloorBiomeSampler`, `ChunkUtility`, `V2FloorBiomePreview` | 좌표·바이옴·전이·Sprite 선택, 진단 표시와 게임 상태 책임 | `Phase4FloorBiomeGenerationTests`; 시각 결과는 수동 확인 항목 | [평가 완료](Results/Q19.md) |
| Q20 | Miner Decision→Execution→ProductResult | 자원 선택, 출력 여유, 진행도·고갈 처리, 아이템 생성 인계 | `Phase4MinerComponentTests`, `Phase4MinerPipelineTests` | [평가 완료](Results/Q20.md) |
| Q21 | Crafter Decision→Execution→State Apply | 재료 선소비, 진행 상태, 전체 출력 슬롯 용량, 작업 중단 경로 | `Phase5CrafterExecutionTests` | [평가 완료](Results/Q21.md) — 코드 분석·선택 EditMode 5건 통과 |
| Q22 | `CrafterRecipeCommandSystem` | 레시피 변경·진행 초기화·잔여물 이동·입고/제작 재개 조건 | `Phase5RecipeChangePipelineTests` | [평가 완료](Results/Q22.md) — 코드 분석·선택 EditMode 2건 통과 |
| Q23 | Placement Request→검증→현장 생성 | footprint 회전, 점유·해금·채굴 자원, 묶음 정책·여러 요청 경합 | `Phase7PlacementCommandTests`, `Phase7ConstructionContractTests` | [평가 완료](Results/Q23.md) — 코드 분석·선택 EditMode 6건 통과 |
| Q24 | 공사 자재 수령 Job | 잔여 요구량·중복 공급·예약량·보관 버퍼·소유권의 일관성 | `Phase7ConstructionMaterialTests` | [평가 완료](Results/Q24.md) — 코드 분석·선택 EditMode 4건 통과 |
| Q25 | 공사 완료 Job→`BuildingLifecycleUtility` | 완료 조건, 자재 소비와 스폰 성공/실패, 현장 제거의 일관성 | `Phase7ConstructionCompletionTests`, `Phase7BuildingLifecycleTests` | [평가 완료](Results/Q25.md) — 코드 분석·선택 EditMode 5건 통과 |
| Q26 | 공사 취소 Job | 취소/공급/완료 경합, 도착 자재 반환, 중복 취소와 참조 정리 | `Phase7ConstructionCancelTests` | [평가 완료](Results/Q26.md) — 코드 분석·선택 EditMode 5건 통과 |
| Q27 | 완공 건물 직접 Spawn 경로 | 타입별 초기화, DB/Config 누락 정책, 공사 완료 경로와 공통 계약 | `Phase7BuildingLifecycleTests`, `Phase7BuildingAuthoringPrefabTests` | [평가 완료](Results/Q27.md) — 코드 분석·선택 EditMode 5건 통과 |
| Q28 | 건물 철거 Job | 저장품·생산품·환급, 벨트 아이템 보존, 물류 반영 순서와 중복 요청 | `Phase7BuildingDemolishTests` | [평가 완료](Results/Q28.md) — 코드 분석·선택 EditMode 7건 통과 |
| Q29 | Validation 및 테스트 지원 코드 | 실제 불변식 검사 범위, Factory와 Spawn/Baker 차이, 수동 호출과 그룹 정렬 차이 | `Phase3StorageInvariantTests`와 필요한 관련 사례 | [평가 완료](Results/Q29.md) — 코드 분석; 새 실행 없음 |
| Q30a | 도메인 연결부 최종 확인 | 앞 단계에서 남은 경계 문제만 추적; 통합 검증 질문별로 분할 가능 | `Phase4EndToEndPipelineTests`, `Phase6EndToEndPipelineTests`, `Phase7EndToEndConstructionPipelineTests` 중 필요한 것 | [평가 완료](Results/Q30a.md) — 코드 분석·선택 EditMode 4건 통과 |
| Q30b | 시스템 간 의존성 종합 평가 | 누적 관계표·필요 시 의존도 그림, 숨은 순서 의존・상태 교차 쓰기・순환 결합・책임 집중, 변경 영향과 분리 후보 정리 | 앞 단계 근거 재사용, 미확인 결합에 필요한 사례만 선택 | [평가 완료](Results/Q30b.md) — 누적·현재 코드 종합; 기존 실행 근거 재사용 |
| Q31 | 종합 보고 | 세 축별 결과와 의존성 목표 적합성, 우선순위, 검증 공백, 별도 수정 후보와 사용자 결정 사항 정리 | 새 실행은 미해결 검증 질문이 있을 때만 | [평가 완료](Results/Q31.md) — 최종 종합·우선순위·검증 공백·결정 사항; 새 실행 없음 |

## 5. 검증 및 완료 기준

Unity CLI 실행은 평가 시점의 Unity CLI 스킬과 [AGENTS.md의 검증 절차](../../../AGENTS.md)를 따른다. 관련 테스트 실행에는 `Tools/Codex/Verify-Unity.ps1 -TestFilter '<선택한 테스트>'`를 우선 사용한다. 문서 작성이나 읽기 전용 분석만으로 새 컴파일을 실행하지 않는다. 관련 테스트를 실행할 때는 래퍼의 컴파일 확인을 거친다.

전체 EditMode 테스트는 별도 명시적 요청이 있을 때만 실행한다. Play Mode는 이번 평가의 기본 실행 범위가 아니다. 테스트를 실행하지 않은 단계에는 이유를 적는다. 연결 실패·컴파일 실패·타임아웃·Skipped·Inconclusive를 성공으로 처리하지 않는다.

단계 완료는 결함이 없거나 수정되었다는 뜻이 아니다. 다음 기록이 갖춰지면 결함이 있어도 평가 단계는 완료할 수 있다.

- [ ] 평가 범위와 실제 읽은 파일·심볼을 기록했다.
- [ ] 핵심 상태의 Owner, 작성자/소비자, 실행·ECB 경계를 설명할 수 있다.
- [ ] 안정성·유지보수성·V2 적합성 각각의 판단과 근거를 남겼다.
- [ ] 시스템 간 의존 관계와 변경 영향 사례를 기록하고, 필요한 계약과 개선할 결합을 구분했다. 해당 없음이면 이유를 남겼다.
- [ ] 필요한 테스트 결과 또는 미실행 이유·검증 공백을 남겼다.
- [ ] 문제와 설계 질문을 등록하거나, 검토 범위 내 발견 사항 없음을 기록했다.
- [ ] 남은 의문과 다음 단계를 기록했다.

상태는 `미착수 / 분석 중 / 검증 대기 / 평가 완료 / 보류`를 사용한다. 필요한 검증이 막혔다면 `검증 대기` 또는 `보류`로 남긴다. 실행 검증이 필요 없다고 판단한 경우에는 이유를 적고 코드 분석 범위에서 완료할 수 있다.

## 6. 결과와 문제 기록 규칙

### 결과 문서 저장 방식

- 계획 문서는 평가 목적·기준·순서·양식과 진행 상태를 관리한다. 단계별 분석·판정·실행 근거·발견 문제는 별도 결과 문서에 기록한다.
- 결과 파일은 이 문서와 같은 폴더의 `Results/` 아래에 단계당 하나씩 만든다. 파일명은 `Q00.md`, `Q01a.md`, `Q01b.md`처럼 단계 ID를 사용한다. 미착수 단계의 빈 결과 파일은 미리 만들지 않는다.
- 각 결과 문서 상단에 계획 문서 링크와 단계 제목을 둔다. 분석 중 또는 검증 대기 상태에서도 같은 파일을 갱신하며, 완료 후 진행표에 해당 파일을 연결한다.
- 발견 문제의 상세 내용은 최초 발견 단계 결과 문서에 둔다. 다른 단계에서는 원문을 링크하며 중복 등록하지 않는다. 문제 ID는 전체 결과 문서에서 유일하게 유지한다.
- 시스템 의존 관계도 해당 단계 결과에 기록하고, Q30b 결과에서 출처 링크와 함께 종합한다. 최종 종합 보고는 `Results/Q31.md`에 작성한다.
- 재평가 시 이전 기준선과 판정을 지우지 않고 같은 결과 파일에 재평가 일자·변경된 근거·판정을 추가한다. 테스트 원본 로그는 결과 문서와 구분해 경로로 연결한다.
- 기존 Q00 기록은 `Results/Q00.md`로 이동했다. 당시 확인 사실·문제 ID·미검증 상태는 그대로 보존했다.

### 근거 수준

| 표시 | 의미 |
| --- | --- |
| 코드 확인 | 특정 소스·분기·호출 관계로 확인. 런타임 재현을 의미하지 않음 |
| EditMode 확인 | 실행 시점·필터·최종 상태·통계를 기록한 테스트 근거가 있음 |
| 사용자 수동 확인 | 사용자가 전달한 환경·절차·결과가 있음 |
| 미확인 | 정적 분석만으로 결론을 내리지 못했거나 검증이 생략·차단됨 |

### 문제 분류와 우선순위

유형은 `안정성 결함 / 유지보수 위험 / V2 설계 불일치 / 문서 불일치 / 검증 공백 / 설계 판단 필요`로 구분한다. 하나의 문제에 여러 평가 축을 연결할 수 있다.

- `P1`: 현재 사용 가능한 경로에서 충돌, 아이템 유실·복제, 상태 손상 등 핵심 동작에 큰 영향을 줄 수 있음.
- `P2`: 특정 조건의 잘못된 동작, 책임 중복·숨은 의존 등 실제 변경이나 검증을 어렵게 하는 문제.
- `P3`: 영향이 제한적인 명명·주석·가독성·국소 중복 문제.
- `판단 보류`: 의도나 영향이 확인되지 않아 우선순위를 확정할 수 없음.

영향과 근거 수준을 별도로 기록한다. 테스트로 재현하지 않았더라도 명확한 소스 근거로 문제를 기록할 수 있지만, 실행 확인으로 표현하지 않는다. 취향 차이만으로 유지보수 결함을 만들지 않는다.

설계 판단은 `적합 / 일부 불일치 / 불일치 / 판단 보류 / 해당 없음`으로 기록한다. 미검토 범위를 포함한 전체 점수나 근거 없는 백분율은 사용하지 않는다. `발견 사항 없음`도 해당 검토·검증 범위로 한정한다.

### 단계 기록 양식

평가를 시작할 때 아래 양식을 복사하여 `Results/<단계 ID>.md`에 기록한다. 예: `Results/Q01a.md`. 계획 문서에는 상세 평가 결과를 추가하지 않고, 진행표의 상태와 결과 문서 링크 및 다음 단계만 갱신한다.

```markdown
### Qxx — 단계 이름

- 평가일 / 평가 상태:
- 기준: Git HEAD, 관련 작업 트리 변경, Unity/패키지 버전 또는 Q00 기준선 참조
- 범위: 읽은 파일과 주요 심볼, 직접 의존성, 제외한 범위
- 흐름: 상태 원본 → 작성자/소비자 → 적용·소비·ECB 경계
- 안정성 판단 / 근거:
- 유지보수성 판단 / 근거:
- V2 적합성 판단 / 적용 원칙 / 근거:
- 시스템 간 의존 관계: 호출·상태 읽기/쓰기·순서·수명주기, 공개 계약과 숨은 가정
- 의존성 판정: 필요한 계약 / 개선할 결합 / 판단 보류, 근거 또는 해당 없음 사유
- 변경 영향 사례: 공개 계약 유지 여부, 함께 수정해야 할 위치와 이유
- 테스트 선택 이유와 검증 질문:
- 실행 근거: 실행 시각, 명령/필터, 컴파일 및 테스트 최종 상태, 결과 파일 경로
- 통계: Passed / Failed / Skipped / Inconclusive, 경고·생략 항목
- 미실행 또는 차단 사유:
- 발견 문제 ID:
- 미확인 사항 / 사용자 수동 확인 / 설계 질문:
- 완료 조건 충족 여부 / 다음 단계:
```

### 문제 기록 양식

```markdown
#### F-001 — 구체적인 문제 제목

- 발견 단계 / 유형 / 평가 축:
- 우선순위와 이유:
- 근거 수준: 코드 확인 / EditMode 확인 / 사용자 수동 확인 / 미확인
- 위치: 파일 경로, 심볼, 평가 당시 줄 번호
- 기대 계약과 출처:
- 확인한 코드 동작 또는 실행 결과:
- 발생 조건과 영향:
- 근거: 분기·데이터 흐름 또는 테스트명·실패 결과
- 미확인 사항 / 반대 근거:
- 수정 방향 후보: 코드 변경 없이 제안만 기록
- 필요한 추가 검증:
- 처리: 미결정 / 수정 승인 / 보류 / 수정 불필요
- 별도 수정 작업과 재검증 근거: 결정 또는 수행 후 기록
```

## 7. 현재 진행 상황

- 평가 계획: 작성 완료.
- 실제 평가: [Q00 완료](Results/Q00.md), [Q01a 완료](Results/Q01a.md), [Q01b 완료](Results/Q01b.md), [Q02 완료](Results/Q02.md), [Q03 완료](Results/Q03.md), [Q04 완료](Results/Q04.md), [Q05 완료](Results/Q05.md), [Q06 완료](Results/Q06.md), [Q07 완료](Results/Q07.md), [Q08 완료](Results/Q08.md), [Q09 완료](Results/Q09.md), [Q10 완료](Results/Q10.md), [Q11 완료](Results/Q11.md), [Q12 완료](Results/Q12.md), [Q13 완료](Results/Q13.md), [Q14 완료](Results/Q14.md), [Q15 완료](Results/Q15.md), [Q16 완료](Results/Q16.md), [Q17 완료](Results/Q17.md), [Q18 완료](Results/Q18.md), [Q19 완료](Results/Q19.md), [Q20 완료](Results/Q20.md), [Q21 완료](Results/Q21.md), [Q22 완료](Results/Q22.md), [Q23 완료](Results/Q23.md), [Q24 완료](Results/Q24.md), [Q25 완료](Results/Q25.md), [Q26 완료](Results/Q26.md), [Q27 완료](Results/Q27.md), [Q28 완료](Results/Q28.md), [Q29 완료](Results/Q29.md), [Q30a 완료](Results/Q30a.md), [Q30b 완료](Results/Q30b.md), [Q31 완료](Results/Q31.md). Q11은 코드·직렬화 분석 완료, 실제 베이킹 미확인. Q12는 네 공간 인덱스·Reader/Writer·Fence 분석과 선택 EditMode 3건 통과, 경합 강제 재현·실제 플레이·성능은 미확인. Q13은 Belt Decision→Execution·직접 진입 의존성 분석과 선택 EditMode 5건 통과, 신규 F-029·F-030은 코드 확인이며 실행 재현은 미수행. Q14는 입고 판단·슬롯 예약·적용·소유권 인계 분석과 선택 EditMode 3건 통과, 신규 F-031·F-032는 코드 확인이며 실행 재현은 미수행. Q15는 저장품/생산품 출고·목적지 예약·적용 분석과 선택 EditMode 4건 통과, 신규 F-033은 오래된 승인에 대한 조건부 위험이며 실행 재현은 미수행. 생산품 슬롯 우선순위는 코드 확인. Q16은 Splitter/Merger·목적지 예약·Routing Apply 분석과 선택 EditMode 8개 메서드/14개 사례 통과, 신규 F-034는 코드 확인이며 실행 재현은 미수행. Q17은 초기 Bootstrap→Load·Pending/완료 인계 분석과 선택 EditMode 3건 통과, 완료 알림의 EndCommand·다음 Command 소비는 Q01a/Q10 실행 근거 재사용. 신규 ID 없이 기존 F-002/F-017에 연결했고 극단 크기·재초기화·ECB 실패 복구는 미확인. Q18은 Resource Generation→EndCommand→Spatial Sync의 결정론·청크 경계·준비/완료·Reader/Writer 분석과 선택 EditMode 4건 통과, 준비 대기·빈 청크 완료는 기존 실행 근거 재사용. 신규 F-035는 청크 간 RNG 검증의 assertion 공백이며 현재 생성 오류 재현을 뜻하지 않음. Q19는 FloorBiomeSampler·ChunkUtility·Preview의 순수 계산/진단 표시 책임 분석과 선택 EditMode 1건 통과. 신규 ID 없이 F-016/F-017에 연결했으며 실제 화면·전이 시각 품질·픽셀 정렬은 미확인. Q20은 Miner Decision→Execution→ProductResult 분석과 선택 EditMode 4건 통과. 신규 F-036은 고속 채굴 잔여 진행도와 0~1 주석의 불일치이며 실행 재현은 미수행. 테스트 helper의 추가 ECB 재생을 실제 그룹 순서 보장과 구분했고 기존 F-005/F-006/F-007/F-023에 연결했다. Q21은 Crafter Decision→Execution→State Apply의 선소비·진행·전체 출력 용량·중단 및 Reader/Writer·변경 영향 분석과 선택 EditMode 5건 통과. 신규 F-037은 실제 Crafter 생성의 필수 상태 결정·입고 컴포넌트 누락, F-038은 Execution 책임 주석 불일치다. F-037의 실행 재현은 미수행이며 Factory 테스트와 실제 Spawn 보장을 구분했고 기존 F-014/F-006에 연결했다. Q22는 Recipe Command의 진행 초기화·잔여물 이동·출고/입고/제작 재개 및 Reader/Writer·변경 영향 분석과 선택 EditMode 2건 통과. 신규 F-039는 main-thread Lookup의 명시적 Job 완료 경계가 없는 조건부 위험이며 실행 재현은 미수행, F-040은 무효 새 ID 수용 시 기존 작업 취소 정책의 판단 보류다. 유효 변경·재개 시점의 테스트와 실제 출고·active 작업 취소 검증을 구분했다. Q23은 Placement Request→검증→현장 생성의 회전·점유·해금·채굴 자원·묶음 정책 및 Reader/Writer·변경 영향 분석과 선택 EditMode 6건 통과. 기존 F-023/F-026/F-027을 재확인했고, 신규 F-041은 별도 요청의 동일 PlacementStamp 발급과 후속 동률 처리 의존이다. 신규 문제와 요청 간 경합·비정사각형 전체 흐름의 실행 재현은 미수행이며 설계 질문·테스트 보강 제안을 남겼다. Q24는 ConstructionMaterialApplyJob의 잔여량·중복 공급·예약량·보관/소유권과 Reader/Writer·변경 영향 분석 및 선택 EditMode 4건 통과. Q02의 단일 정상 수령 근거를 재사용하고 F-003/F-010을 재확인했다. 신규 F-042는 보관 버퍼 없는 현장의 부분 수령 승인으로 정상 Placement 구성과 구분한 코드 확인 문제이며 실행 재현은 미수행이다. 테스트의 축약 현장과 실제 수령→완공 경계, 예약·소유 인계 질문을 기록했다. Q25는 공사 완료→BuildingLifecycleUtility의 완료 조건·자재/현장 수명·스폰 성공/실패·Reader/Writer·변경 영향 분석과 선택 EditMode 5건 통과. 신규 ID 없이 기존 P1 F-024의 실패 반환 무시·원본 삭제를 재확인했고, 직접 스폰 거부 실행과 완료 경로의 손실 재현 미수행을 구분했다. F-023/F-003/F-010/F-011/F-037/F-042 등의 직접 영향과 실패 보상·예약/clearance 인계 질문을 기록했다. Q26은 공사 취소 Job의 Cancel→Material→Completion 경합·기존 자재 반환·중복 취소·참조 정리 및 Reader/Writer·변경 영향 분석과 선택 EditMode 5건 통과. 신규 P3 F-043은 경합 테스트의 반대 순서 주석과 새 공급 거부/수령 후 반환을 위치로 구별하지 못하는 검증 공백이며 런타임 취소 순서 결함은 아니다. F-003/F-042의 실물 인계 영향을 연결하고 외부 예약·반환품 상태·재생 실패 복구를 미확인으로 구분했다. Q27은 직접 Spawn의 타입별 초기화·DB/Config 누락·공통 완료 계약·Reader/Writer·변경 영향 분석과 선택 EditMode 5건 통과. 신규 ID 없이 F-037/F-023/F-022를 재확인하고 F-024/F-018의 직접 영향을 연결했다. 기존 DB 누락 거부·lookup/Resources 검증 근거를 재사용했으며 Crafter 실제 생성 후 제작·비정사각형 Sync·실제 Baker 연결은 미확인이다. Q28은 철거의 기존 저장품/생산품 반환·비용 환급·벨트 최신 위치 정리·중복 마킹·물류 및 ECB 순서·Reader/Writer·변경 영향 분석과 선택 EditMode 7건 통과. 기존 내용물 반환 근거를 재사용하고 신규 ID 없이 F-004/F-005/F-020을 재확인했다. 동시 입고/생성 대기 결함 재현·실제 출고 경합·중복 환급 수량은 미확인으로 구분했다. Q29는 Validator의 실제 검사 범위·Reader/Writer·Factory/Spawn/Baker 구성 차이·수동 호출/그룹 정렬과 변경 영향을 코드로 평가했다. 신규 F-044~F-047은 소유 참조 검증 누락·진단 로그 덮어쓰기·테스트의 공용 로그 삭제·총합 assertion의 검출 공백이다. 기존 F-021/F-026/F-037 등을 연결했으며 새 컴파일·테스트는 실행하지 않았다. 반례 실행·실제 Bake·Player는 미확인이고 보강 제안으로 남겼다. Q30a는 생산→최종 수납·합류기 정체 복구·공사 취소 후 소유권/점유 해제·철거/바닥 정리 후 재건축의 연결부를 평가하고 선택 EditMode 4건이 통과했다. 신규 F-048은 한쪽 도착만으로 통과하는 생산·분배 통합 assertion 공백이다. 같은 틱 생산/입고·철거, 실제 Crafter 생성/재개, 비정사각형·별도 배치·실패 prefab 경합은 기존 문제에 연결하고 미확인으로 유지했다. Q30b는 누적 관계·상태 Writer·직접 호출/순환/책임 집중을 현재 핵심 소스와 대조해 종합했다. 공개 데이터 왕복과 필수 Job/Fence/ECB를 유지할 계약으로 구분하고, 소유/폐기·입력 의미·생성 표현·준비/테스트 결합의 변경 영향과 분리 후보를 기록했다. 신규 문제 없이 최초 발견 문서에 연결했고 새 실행 없이 기존 근거를 재사용했다. 미확인 경합·실제 Bake/Player·성능·V1 비교는 명시적으로 남겼다. Q31은 세 축과 의존성 목표를 일부 불일치/일부 달성으로 종합하고, 기존 F-001~F-048의 원래 우선순위와 P1 5건의 수정 후보·검증 공백·사용자 결정 사항을 정리했다. 신규 문제·컴파일·테스트 없이 기존 근거를 재사용했으며 평가 완료와 결함 수정/미확인 동작 검증을 구분했다. 계획상 평가는 종료했고 수정·재검증은 자동 진행하지 않는다.
- 발견 사항과 상세 판단: [Q00 결과 문서](Results/Q00.md), [Q01a 결과 문서](Results/Q01a.md), [Q01b 결과 문서](Results/Q01b.md), [Q02 결과 문서](Results/Q02.md), [Q03 결과 문서](Results/Q03.md), [Q04 결과 문서](Results/Q04.md), [Q05 결과 문서](Results/Q05.md), [Q06 결과 문서](Results/Q06.md), [Q07 결과 문서](Results/Q07.md), [Q08 결과 문서](Results/Q08.md), [Q09 결과 문서](Results/Q09.md), [Q10 결과 문서](Results/Q10.md), [Q11 결과 문서](Results/Q11.md), [Q12 결과 문서](Results/Q12.md), [Q13 결과 문서](Results/Q13.md), [Q14 결과 문서](Results/Q14.md), [Q15 결과 문서](Results/Q15.md), [Q16 결과 문서](Results/Q16.md), [Q17 결과 문서](Results/Q17.md), [Q18 결과 문서](Results/Q18.md), [Q19 결과 문서](Results/Q19.md), [Q20 결과 문서](Results/Q20.md), [Q21 결과 문서](Results/Q21.md), [Q22 결과 문서](Results/Q22.md), [Q23 결과 문서](Results/Q23.md), [Q24 결과 문서](Results/Q24.md), [Q25 결과 문서](Results/Q25.md), [Q26 결과 문서](Results/Q26.md), [Q27 결과 문서](Results/Q27.md), [Q28 결과 문서](Results/Q28.md), [Q29 결과 문서](Results/Q29.md), [Q30a 결과 문서](Results/Q30a.md), [Q30b 결과 문서](Results/Q30b.md), [Q31 최종 종합 보고](Results/Q31.md) 참조.
- 다음 작업: **계획상 평가 종료**. Q31의 수정 후보·결정 사항을 바탕으로 별도 요청된 수정 또는 재검증만 진행한다.
- 수정 결정: 없음.
