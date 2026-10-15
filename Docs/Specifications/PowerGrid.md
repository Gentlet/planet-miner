# 전력망 명세

2026-10-15 사용자 확정 1A·2A·3A·4A·5A·6A를 구현한다. 배정 에너지를 이번 틱 소비로 확정하며 미사용분은 환급하거나 재배분하지 않는다. Preparation은 작업 자격/수요, Power는 선택·망·가용량·배분·연료 소비·공개를, Building/Drone은 공개 공급에 따른 작업을 소유한다. 현재 검사는 실제 Writer와 공개 경계를 기준으로 좁혔으며 상세 계약은 아래 「Writer와 검사 경계」를 따른다. 연구·충전의 실제 Producer는 후속이고 과거 검증 수치는 각 변경 시점의 별도 기록이다.

## 설정 원본과 검증

전력 능력은 `BuildingConfigElement.Power`의 `BuildingPowerConfig`에 통합한다. 별도 PowerConfig/시계/Tick singleton은 없다. 능력은 게시 후 불변이며 종류·Tier별 명시 설정을 검증한다. maximumGeneration/maximumConsumption은 에너지/초, coalEnergyPerItem/RemainingEnergy/RequestedEnergy/AllocatedEnergy는 에너지 단위다. World 시간의 `min(DeltaTime, GameConstants.MaxSimulationDeltaTime)`을 모든 도메인이 공유한다.

- MainFacility는 기초 발전과 자기 셀의 가상 전신주, 후속 동적 충전 수요를 갖는다. 주변 셀 공급은 일반 전신주가 확장한다.
- CoalGenerator는 명시적 `storageCapacity` 슬롯과 Coal MaxStack을 사용한다. 1티어 임시 1슬롯·석탄 에너지 600은 유지한다. 별도 실물 10개 제한은 없다. Coal Whitelist만 허용한다.
- Miner/Crafter의 정격 수요는 작업 가능한 경우에만 작성한다. 제작 완료/출력 확정 대기는 수요 0이다.
- ResearchBuilding 및 DroneStation/MainFacility의 연구·충전 수요는 후속 도메인이 작성한다. 임의의 고정 충전 수요는 추가하지 않는다.

## 데이터 소유와 활성 상태

`PowerConsumer`와 `PowerGenerator`는 enableable 역할이다. 컴포넌트의 존재는 역할, 활성은 이번 틱 양수 요청/양수 발전 가용량을 뜻한다. 공급 부족 때문에 소비 의도를 비활성화하지 않는다. 공통 생성은 두 역할을 비활성으로 부착한다.

`PowerDemand`, `PowerGeneratorSupplyPlan`, `PowerSupplyReservation`의 활성은 각 단계의 준비 완료다. 정상적인 0도 준비 완료이며 역할 활성과 구분한다. 역할의 필수 구성은 공통 생성이 부착하고 설정은 게시 전에 검증한다. 매 단계에서 전체 구성을 재검사하지 않으며 미준비와 정상 0을 구분한다. Prefab·Disabled·승인 철거는 처리 쿼리에서 제외한다.

`PowerSupplyResult.AllocatedEnergy`는 Power에서 확정한 소비량이다. `PowerUsageResult`와 소비 이후 정산 Writer는 제거한다. 생산 진행·충전량은 배정 에너지를 초과하지 않으며, 미사용분은 손실이다. 망별 집계는 임시 double Native 맵, 개별 필드는 float이다. 아래 방향 반올림과 미세 미배정량도 손실로 처리하며 같은 틱 재배분하지 않는다.

`PowerFrameState`는 제거했다. 공통 `SimulationFatalError.StopScope`는 NextTick/RemainingDomains를 구분한다. Power 내부 검증 메시지는 PowerValidationInitializationSystem이 소유하는 비공개 Native scratch이며 원본·연결·위치 캐시나 시계가 아니다. 전력 원본은 설정, 전신주/건물 ECS, ItemOwnership/StoredItemElement, FuelEnergyState다.

## 실행 순서와 공개 경계

```text
Command → EndCommand → Preparation
    → Power: Decision → Reservation → Execution → StateApply → EndPower
    → Building: Decision → Reservation → Execution → StateApply → EndBuilding
    → Drone: Decision → Reservation → Execution → StateApply
    → SimulationCommit: EndSimulation → Synchronization
```

| 단계 | 책임 |
| --- | --- |
| Command/EndCommand | 철거 승인과 레시피 변경을 확정한다. 승인 철거는 이번 틱 전력 후보에서 제외한다. |
| Preparation | PowerSupplyResetPreparationSystem은 공급/검증 scratch를 초기화한다. MinerWorkPreparationSystem/CrafterWorkPreparationSystem이 전력 외 작업 자격을 준비하고 BuildingPowerDemandPreparationSystem이 요청과 소비 역할을 작성한다. 준비 enable은 완료이며 정상 작업 불가도 준비 완료다. |
| PowerDecision | 일반 참가자의 전체 회전 footprint 공급 전신주를 매 틱 선택한다. 발전 가용량은 정격 한도와 이전 잔량·현재 보관 Coal의 전환 가능 에너지로 제한한다. 계획·활성을 먼저 초기화한다. |
| PowerReservation | 토폴로지 연결 집합·유지권·대표를 준비한 뒤 망별 예산을 배분한다. 발전기별 기준 잔량·전환 실물 개수·소비 후 잔량도 작성한다. |
| PowerExecution | 계획·예약의 준비 완료와 실제 소비할 연료 수량/잔량/실물을 확인한다. 전체 발전기의 소비 조건 확인 후 연료를 전환/차감하고 공급을 게시한다. 연결/범위·수요·망 총량은 다시 계산하지 않는다. |
| PowerStateApply/EndPower | 망 생성·병합·분리·빈 망 삭제와 연결 변경을 같은 ECB에 기록한다. EndPower는 공급 Writer와 ECB Producer를 완료하고 연료 실물 삭제·망 변경을 Building 전에 확정한다. |
| Building | MinerDecisionSystem/CrafterDecisionSystem이 준비 자격과 공급으로 실행/완료 출력·상태 후보를 작성한다. 입출고·채굴/제작·수명주기를 기존 경계에서 수행한다. 진행은 공급률과 배정/요청 에너지 비율 중 작은 값으로 제한하고, 무전력 신규 제작은 재료를 소비하지 않는다. 이미 완료된 결과의 확정/출고는 전력 때문에 막지 않는다. |
| Drone | EndBuilding의 재고/건물을 읽는다. 후속 충전은 사전 수요와 공급을 사용하며 실행하지 못한 예산도 환급하지 않는다. |
| EndSimulation/Synchronization | 드론 결과와 최종 인덱스를 공개한다. 전력 연료 정산은 다시 실행하지 않는다. |

EndBuilding의 새 건물과 새 입고 석탄은 N+1부터 공급 계산에 참여한다. EndPower에서 비워진 보관 공간은 N Building 입고에 사용하지만 그 입고가 N 전력 예산을 늘리지는 않는다. 새 귀환 드론은 후속 충전 수요에 다음 틱부터 참여한다.

## 공급 선택과 망 수명주기

전신주 연결 범위는 종류/Tier별 OR 판정이며 공급 범위 중첩만으로 망을 병합하지 않는다. 일반 참가자는 회전 footprint의 가장 가까운 점유 셀 거리로 공급 가능한 전신주를 선택하고 동률은 PlacementStamp.Compare를 사용한다. 기존 연결이 유효해도 매 틱 재선택한다. ECS Disabled·Prefab·승인 철거는 새 선택에서 제외하고 비활성 참가자의 기존 참조는 정리한다.

분리 시 가장 이른 남은 활성 멤버의 집합이 기존 망 유지권을 갖고, 유지권을 확정한 뒤 병합한다. 준비 실패를 빈 망으로 처리하지 않는다. 예약 망 Entity는 같은 EndPower ECB 안에서만 사용하고, 배분은 공개 Grid 대신 이번 틱 선택/토폴로지 대표를 사용한다. 별도 위치·연결 스냅샷이나 영속 망 집계를 추가하지 않는다. 공개 연결 인덱스/Fence는 마지막 Synchronization의 기존 수명·Reader/Writer 계약을 유지한다.

## 배분과 연료 소비

기초 발전을 먼저 배정하고 부족분을 석탄 발전에 배정한다. 같은 종류의 발전기는 정격 기준 동일 가동률로 분담하며 연료 제한 발전기를 제외한 나머지가 가능한 범위에서 보충한다. 발전기별 배정량은 정격·가용 연료를 넘지 않는다. 망별 공통 공급률은 `min(1, 발전 배정 합계 / 총 요청량)`이며 연결된 무수요 망은 비율 1/에너지 0, 단절은 비율/에너지 0이다.

연료가 부족한 경우 공급을 먼저 게시한 뒤 줄이지 않는다. 배정 시 현재 잔량을 기준 값으로 보관하고 부족한 에너지에 필요한 정수 Coal 개수만 계획한다. 소비 직전에 기준 잔량/수량과 소비 후 에너지 한도를 확인한다. 실제 전환할 실물에만 생존·종류·Owner·활성 Destroy를 확인하고, 삭제 대상의 중복 참조가 소비하지 않는 슬롯에 남지 않는지도 검사한다. 전체 성공 뒤 StoredItemElement에서 참조를 먼저 제거하고 ItemLifecycleUtility의 공통 삭제 기록을 EndPower에 남긴다. 생성/삭제 Owner를 이중 실행하지 않는다.

`소비 후 잔량 = 이전 잔량 + 전환 개수 × CoalEnergyPerItem - 발전기 AllocatedEnergy`다. 소비자 실제 성공량을 다시 모으지 않는다. 예를 들어 잔량 0·Coal 1개·배정 10이면 600을 전환하고 10을 확정 소비해 590을 보존한다. 이후 작업을 하지 못해도 590이며 환급하지 않는다. 철거는 미전환 실물만 기존 반환 경로를 사용한다.

정상 틱은 `0 ≤ 소비자 AllocatedEnergy ≤ RequestedEnergy`, `0 ≤ 발전기 AllocatedEnergy ≤ AvailableEnergy ≤ 정격 × DeltaTime`, `소비자 배정 합계 ≤ 발전기 배정 합계`를 만족한다. 부동소수 반올림 차이와 미사용 예산은 손실이며 음수 잔량·연료 없는 공급·이중 실물 소비를 허용하지 않는다.

## 실패 정책과 검증 범위

Power 검증 실패는 내부 검증 결과에 첫 메시지를 기록하고 EndPower에서 공통 Fatal을 RemainingDomains로 게시한다. 정상 공급/연료/망 변경을 확정하지 않으며 BuildingSimulationGroup/DroneSimulationGroup은 같은 틱 실행을 건너뛴다. 이미 EndCommand에서 확정한 변경은 롤백하지 않는다. EndBuilding에서 새로 발생한 Fatal은 기존 다음 틱 전체 차단 계약을 유지한다. 실패 월드는 폐기하며 자동 복구/재개를 제공하지 않는다.

관련 EditMode는 실제 생성·완공·철거 경계, 준비 누락·소비 연료 부족/실물 오류, 부분 공급·무전력 착수/완료, 새 석탄의 다음 틱 사용과 전환 공간 재사용, 연료 확정 소비와 도메인 중단을 검증한다. 실제 연구·드론 충전·이동·Play Mode·베이킹·Player 빌드·성능은 별도 후속이다.


## 준비/결정과 Job 의존성

Preparation 이름을 유지한다. MinerWorkPreparation은 자원/출력 조건의 자격과 대상을, CrafterWorkPreparation은 재료/진행 자격만 전달한다. 기존 MinerDecision/CrafterDecision/CrafterStateDecision의 단독 Writer는 BuildingDecision이다. 전력 요청에 필요하지 않은 완료 출력/표시 상태 판단은 Building에서 수행하며 자원 탐색·재료 자격 검사는 준비 결과를 재사용한다. Execution은 실제 변경 전 자원/재료를 최종 검사한다.

PowerSimulationGroup은 단계 구성만 맡고 PowerValidationInitializationSystem이 Native 검증 결과와 마지막 접근 핸들의 수명을 소유한다. 생성 → Preparation Reset → Job/해제 핸들 등록 → EndPower 완료/공통 Fatal·ECB 게시 → 종료 시 완료/Dispose 순서다. Entity 목록은 필요한 처리에만 비동기 수집하며 해제를 의존성에 포함한다. 예산 Reservation과 연료 Execution은 로컬 Ready로 검사 완료를 전달하고 StateApply는 토폴로지/선택의 준비 여부만 확인한다. 공급 결과 초기화는 Preparation의 단독 책임이다. 중간 EntityManager 검증/CompleteDependencyBeforeRO나 Lookup 묶음은 없고 기존 Fence·ECB Producer 계약을 유지한다.

## Task 8.1 검증 근거와 한계

2026-10-12 Unity Editor 재컴파일은 completed/failed false/errors 없음, 리로드 종료와 runtime/editor DLL 최신성을 확인했다. 선택 EditMode는 Phase8PowerContractTests 26/26, Phase3BuildingRuntimeConfigTests 1/1, Phase7BuildingAuthoringPrefabTests 24/24, PrefabDatabaseInitializationTests 19/19로 총 70/70 통과했다.

이는 설정/게시/초기화 실패·Tier 분리·순수 범위와 기존 준비 관문의 검증이다. 위 실제 틱 실행·연료 전환·생산/충전·설치/철거 사례, Play Mode·베이킹·Player 빌드·성능은 실행하지 않았다. 각 후속 Task가 실제 GameSimulationGroup/ECB 경계에서 검증한다.

2026-10-12 후속 사용자 지시로 독립 ECS 타입 4개와 전력 프레임 Tick 필드를 제거하여 현재 계약은 10개다. 기존 시간 정책·프레임 초기화·보관 버퍼 재검사·일시 집계/정산으로 역할을 정리했다. 이번 정의 정리는 Editor 컴파일만 확인했으며 위 70사례는 최초 구현 검증 기록으로 유지한다. 실제 초기화/정산 Writer는 여전히 후속이다.

## 폐기한 Task 8.1~8.2 재구현의 과거 검증

2026-10-13 재구현 당시 Editor 컴파일과 선택 EditMode Phase8PowerTopologyTests 5/5, Phase8PowerContractTests 26/26, Phase7BuildingLifecycleTests 11/11로 42/42를 확인했다. 이후 소유권 통합·Lookup 정리는 CLI의 STATUS_PIPELINE_LOAD_PENDING으로 새 컴파일/선택 테스트를 시작하지 못했다.

사용자 요청으로 해당 재구현 코드·전용 테스트를 다시 롤백했다. 위 성공 결과는 폐기한 구현의 과거 기록이며 현재 기본 정의나 다음 재구현의 실행 증거로 사용하지 않는다. 실제 망 제거/분리·공급·연료/생산/충전, Play Mode·베이킹·Player 빌드·성능은 후속이다.

롤백에서는 추가 파일 8개 제거와 수정 C# 5개의 136a44f74 대비 Git 정규화 해시 일치, 기존 전력 전용 그룹 삭제 유지, git diff --check 통과를 확인했다. 롤백 이후 재컴파일도 STATUS_PIPELINE_LOAD_PENDING으로 시작되지 못했다.

## 2026-10-13 새 구현의 검증 상태

현재 Unity 참조와 Entities 소스 생성기를 사용한 Temp/PowerGridValidation의 별도 런타임/Editor C# 빌드는 각각 오류 0·경고 0으로 통과했다. Unity Editor 컴파일/리로드·Burst 실행·테스트 실행 성공을 뜻하지 않는다. CLI 재컴파일은 Pipeline 연결 오류로 시작하지 못했으며 선택 EditMode도 미실행이다.

새 Phase8PowerTopologyTests 5사례는 기존 GameSimulationGroup/건물 하위 그룹과 ECB 경계의 직접 Spawn·현장 완공→다음 틱 참여·중복 생성 방지, 기존 망 유지와 병합 공개, 다른 Tier의 OR/공급 범위 중첩 분리, 무효 참조 Fatal과 이전 결정 초기화, 공유 인덱스 Reader/재할당/빈 맵 수명을 다룬다. 기존 설정 테스트는 실제 망 생성/병합 경계를 다루지 않아 이 핵심 흐름 검증을 추가했다.

같은 날 연결 복구 후 Unity 컴파일 상태 up_to_date/failed false/errors 없음/compilationFailed false와 Editor ready·비컴파일·비리로드·Play stopped, runtime/Editor DLL 최신성을 확인했다. 선택 EditMode는 Phase8PowerTopologyTests 5/5, Phase8PowerContractTests 26/26, Phase7BuildingLifecycleTests 11/11, Phase7ConstructionCompletionTests 8/8로 이번 실행 총 50/50, 실패·생략·불확정 0이다. 위 최초 연결 실패와 미실행 상태를 해소했으며 Task 8.2 완료를 기록한다. 폐기한 구현의 과거 수치와 합산하지 않는다. Play Mode·베이킹·Player 빌드·성능·실제 공급/연료/생산/충전과 망 제거/분리는 이 검증 범위에 포함하지 않는다.

## 명명·페이즈 책임 분리 검증 (2026-10-13)

시스템/파일을 PowerGridTopologyReservationSystem으로 바꾸고 기존 meta GUID를 유지했다. 준비에서 정상 망 변경의 ECB 기록을 제거하여 BuildingStateApply의 PowerGridTopologyApplySystem으로 옮겼다. Reservation Job과 Apply Job은 각 단계의 ECS 의존성을 따르고 Apply 기록 핸들만 EndBuilding Producer에 등록한다. 기존 같은 틱 계획·EndBuilding 공개·다음 틱 신규 참여 계약은 유지한다.

분리 후 Unity 컴파일 completed/failed false/errors 없음/compilationFailed false, Editor ready·비컴파일·비리로드·Play stopped와 runtime/Editor DLL 최신성을 확인했다. 이번 선택 EditMode는 전력 토폴로지 6/6·공통 건물 생성 11/11·공사 완공 8/8로 25/25, 실패·생략·불확정 0이다. 전력 설정 26사례는 이번 변경의 대상이 아니어서 재실행하지 않았다. 앞선 50사례와 합산하지 않는다.

기존 병합 사례에 Reservation만 실행하고 ECB를 재생해도 망 변경이 없는지, Apply 기록도 Playback 전에는 공개되지 않는지 검사를 추가했다. 준비 후 공개 연결이 바뀌는 단계 사이의 실패는 기존 사례가 다루지 않아 망 생성/삭제/덮어쓰기 없이 Fatal로 중단하는 회귀 1사례를 추가했다. Play Mode·베이킹·Player 빌드·성능·실제 공급/연료/생산/충전과 제거/분리는 미수행이다.

## Task 8.3 검증 (2026-10-13)

1A·2A·3A와 빈 망/런타임 참조 정리를 구현했다. 기존 토폴로지 6사례에 7사례를 추가하여 실제 DemolishBuildingRequest의 Command/EndCommand/EndBuilding 경계에서 끝/다리/이전 대표 제거, 마지막/중복 제거·같은 셀 재설치, Disabled 해제/재활성화, 동시 분리·병합의 유지권 우선, 일반/Disabled 참가자 참조 정리, 입력 실패 시 기존 상태 보존을 검사한다. 일반 소비자 참조 입력은 fixture가 직접 준비하며 제품 등록/공급 전신주 검색을 구현한 것은 아니다.

Unity 컴파일 completed/failed false/errors 없음/compilationFailed false·Editor ready/비컴파일/비리로드/Play stopped와 runtime/Editor DLL 최신성을 확인했다. 관련 선택 EditMode는 Phase8PowerTopologyTests 13/13, Phase7BuildingDemolishTests 19/19로 이번 실행 32/32, 실패·생략·불확정 0이다. 과거 25/50사례와 합산하지 않는다. Play Mode·베이킹·Player 빌드·성능과 공급/연료/생산/충전은 미검증이다. 현재 매 틱 전체 쌍 비교의 IJob 구조를 유지하며 병렬화/변경 감지 최적화는 별도 개선이다.

후속 사용자 지시로 PowerGridConnectionSnapshot과 CollectConnections를 제거했다. Apply Job에는 참가자 Entity 목록과 읽기 전용 PowerGridConnection/PowerPole ComponentLookup을 전달하여 현재 값을 조회한다. Disabled 포함/Prefab 제외, 변경분만 기록, 자기 참조 보존·무효 선택 해제, 같은 ECB와 의존성 등록 계약은 유지한다. 연결 값/분류 배열의 매 틱 복사를 제거했으며 성능 향상을 측정한 것은 아니다. 변경 후 Unity 컴파일 완료와 동일한 선택 EditMode 13/13·19/19(32/32, 실패·생략·불확정 0)를 확인했다. 이전 실행과 합산하지 않는다.

## Task 8.4 구현·검증 (2026-10-13)

확정한 1A·2A·3A·4A를 적용했다. 기존 토폴로지 테스트의 GameSimulationGroup·Reservation·Apply·EndBuilding·Synchronization fixture에 공급 선택 Writer를 연결하고, 기존 13사례가 다루지 않는 일반 참가자의 공급 선택 통합 흐름 8사례를 추가했다. 다섯 실제 생성/완공 경로의 1×1·2×2·3×3·회전 비정사각형/경계/범위 밖, 중첩 범위의 전체 셀 최소 거리/설치 동률, 새 전신주의 다음 틱 참여와 기존 유효 선택 변경, 비활성 상태의 선택 제외/재활성화, 실제 철거의 같은 틱 재연결/최종 단절과 준비 후 범위 변경 실패의 보존을 확인했다.

Unity 컴파일 completed/failed false/errors 없음/compilationFailed false·Editor ready/비컴파일/비리로드/Play stopped와 runtime/Editor DLL 최신성을 확인했다. 선택 EditMode는 Phase8PowerTopologyTests 21/21, Phase7BuildingLifecycleTests 11/11, Phase7ConstructionCompletionTests 8/8, Phase7BuildingDemolishTests 19/19로 이번 59/59, 실패·생략·불확정 0이다. 과거 실행 수치를 합산하지 않는다. Play Mode·베이킹·Player 빌드·성능과 실제 전력 배분/발전/연료/생산/충전은 미검증이다.

## 공급 선택 Decision 이관·검증 (2026-10-14)

공급 선택은 수량/슬롯 선점이나 경합 중재 없이 전신주 대상을 판단하므로 PowerGridConnectionDecisionSystem으로 이름/파일을 변경하고 BuildingDecisionGroup으로 이관했다. meta GUID를 보존하고 테스트/Apply/문서 참조를 갱신했다. 토폴로지 Lookup과 준비 완료 검사를 제거하고 선택에 필요한 전신주의 필수 구성/설정만 독립 검증한다. 망 준비·유지권과 공개 망 유효성은 기존 Reservation/Apply가 계속 소유한다.

기존 중첩/재선택 테스트를 실제 Decision → Reservation 순서로 실행하고 새 전신주의 토폴로지가 Null인 상태에서도 선택이 완료되는지 확인했다. 기존 토폴로지 실패 보존 사례에는 실제 선택 참가자를 추가하여 선택 준비가 완료되어도 Apply가 후속 토폴로지 실패 시 기존 연결을 보존하는지 검사했다. 토폴로지 선행 검증을 제거하면서 전신주 GridPosition 누락이 Job Lookup 예외로 이어질 수 있어, Decision 자체가 입력을 거부하고 기존 연결을 보존하는 회귀 1사례를 추가했다.

변경 후 Unity 컴파일 completed/failed false/errors 없음/compilationFailed false, Editor ready·비컴파일·비리로드·Play stopped와 runtime/Editor DLL 최신성을 확인했다. 이번 선택 EditMode는 Phase8PowerTopologyTests 22/22·Phase7BuildingDemolishTests 19/19로 41/41, 실패·생략·불확정 0이다. 이전 59사례 실행과 합산하지 않는다. Play Mode·베이킹·Player 빌드·성능은 미수행이다.

## Task 8.5 구현·검증 (2026-10-14)

사용자 확정 1A·2A·3A·4A를 구현하고 공통 생성/완공의 역할·필수 프레임 상태를 연결했다. 기존 fixture에 필요한 경우 새 전력 시스템과 실제 Miner/Crafter Decision·ResourceSpatialSync를 추가하여 같은 GameSimulationGroup/ECB 경계에서 검증했다. 기존 22사례에 없던 공급 핵심/실패 흐름 16사례를 추가했다. 실제 작업 수요·완료 출력 대기·공통 dt 상한·생성 다음 틱 참여, 무발전/무수요/부족/충분, 정격/Tier 동일 가동률과 연료 제한, 기초 우선, 공개 Grid 변경 전의 새 망/병합/분리, 미전환 석탄 제외와 float 예산 한도, 게시 전 연료/대표/수요 변화의 전체 결과 거부, 토폴로지 준비 실패·필수 예약 누락 초기화 보호·비활성/실제 철거를 확인했다.

연구/충전 요청은 fixture가 직접 제공했다. 실제 Producer·실물 연료 전환/차감·생산 진행/실제 사용량·Play Mode 입력/렌더를 실행한 증거가 아니다. 기존 소비 도메인의 진행/선소비 코드를 변경하지 않았다.

Unity CLI 연결은 Pipeline 서버 대기 상태였으며 사용자가 서버를 시작한 뒤 복구했다. 저장소 검증 래퍼의 기존 CompileOnly 모드 선택 오류로 개별 CLI 검증 경로를 사용했다. 최초 Native 맵 using 변수 갱신 컴파일 오류 두 곳은 누적 메서드로 수정하고 이후 runtime/Editor 컴파일을 확인했다. 최종 상태는 completed/failed false/errors 없음/compilationFailed false, ready·비컴파일·비리로드·Play stopped이며 두 DLL의 최신성을 확인했다.

최종 선택 EditMode는 Phase8PowerTopologyTests 38/38, Phase7BuildingLifecycleTests 11/11, Phase7ConstructionCompletionTests 8/8, Phase7BuildingDemolishTests 19/19로 이번 76/76, 실패·생략·불확정 0이다. 중간 36사례 실행이나 이전 41/59사례와 합산하지 않는다. Play Mode·베이킹·Player 빌드·성능은 미수행이다.

## Power 페이즈 분리·Task 8.6~8.7 검증 (2026-10-15)

Preparation의 작업 의도/수요 → Power의 선택·망·가용량/배분·석탄 전환/확정 소비·EndPower 공개 → Building/Drone 작업을 연결했다. 원래 시스템 meta GUID를 보존하여 이동했고 일반 아이템 수명주기는 EndBuilding을 유지한다. 소비 이후 정산/PowerUsageResult는 제거했다.

Unity 컴파일 completed/failed false/errors 없음/compilationFailed false·Editor ready/비컴파일/비리로드와 최신 runtime/Editor DLL을 확인했다. 관련 선택 EditMode 157/157, 실패·생략·불확정 0이다. Play Mode·베이킹·Player 빌드·성능은 미수행이다.

이번 실행은 전력 70(Topology 44·Contract 26), 생산 33(Miner 5·EndToEnd 3·Crafter Execution 6·Input 19), 생성/완공/철거 54(Lifecycle 11·Completion 8·Demolish 19·ItemCreationDemolition 16)다. 실제 GameSimulationGroup/EndPower 경계에서 부분 공급·무전력 재료 보존·조기 완료 잔여분 소비·전환 공간의 같은 틱 입고·새 입고의 다음 틱 발전·철거 반환·전역 손상 시 연료/게시 보존과 같은 틱 Building/Drone 차단을 검사했다. 기존 생산/철거 fixture는 새 그룹 순서와 명시적인 전력 입력으로 갱신했으며 전력 입력 대체 fixture를 실제 Power 통합 증거로 확대하지 않는다. 연구/충전 수요의 fixture 입력은 실제 Producer가 아니다. 과거 실행은 합산하지 않는다.

## 준비/실행 결정 분리·공통 Fatal·Job 연결 검증 (2026-10-15)

Preparation 이름을 유지하고 준비 컴포넌트 두 개와 Building의 실행 결정을 분리했다. 기존 생산 Decision GUID를 보존해 Building으로 복귀시켰다. PowerFrameState 대신 공통 Fatal의 중단 범위와 비공개 검증 scratch를 사용한다. 입력 수집/검증·배분·소비·공개를 Job 의존성으로 연결하고 EndPower에서 완료한다.

이번 준비/결정 분리·공통 Fatal·Job 의존성 변경은 Unity 컴파일 completed/failed false/errors 없음/compilationFailed false·Editor ready/비컴파일/비리로드·최신 runtime/Editor DLL과 관련 선택 EditMode 170/170(실패·생략·불확정 0)으로 확인했다.

관련 선택 실행은 PowerTopology 47·PowerContract 26, Miner 5·생산 EndToEnd 3·Crafter Execution 6·Input 19, 생성 11·완공 8·철거 19·아이템 생성/철거 16, 공유 자원 생성/채굴 fixture 10으로 총 170사례다. 이전 157/76 등의 실행과 합산하지 않는다.

기존 흐름 안에서 준비 단계가 실행/완료 출력 결정을 덮어쓰지 않는 것을 검사했다. 추가로 중복 연료 참조의 전체 소비 거부, EndBuilding Fatal의 같은 틱 Drone 완주/다음 틱 차단, 활성 전신주 0개에서 미준비 선택을 빈 망으로 오인해 공개 망/참조를 삭제하지 않는 회귀를 확인했다. 기존 Owner 손상·부분 공급·무전력 신규 재료 보존·완료 출력·같은 틱 전환 공간 입고·새 연료 다음 틱 사용과 실제 생성/완공/철거는 유지했다.

공유 자원 fixture는 현재 지원 반경 상한 2와 명시적인 격리 전력 입력에 맞췄으며 제품의 생성 범위/게임 설정은 바꾸지 않았다. Editor는 Edit Mode인데 일시정지 플래그가 남아 테스트 시작이 지연되어 Editor API로 확인 후 해제했다. Play Mode·베이킹·Player 빌드·성능은 미수행이며 Job 이관을 성능 측정 결과로 해석하지 않는다.

## 검증 소유자와 Job 입력 분리 (2026-10-15)

PowerSimulationGroup의 필드·메모리 관리·완료·오류 게시 코드를 제거했다. PowerValidationInitializationSystem이 OnCreate 생성, Preparation의 Reset 요청, GetDependency/Register 접근 추적, EndPower의 완료/오류 인계, OnDestroy 완료/해제를 소유한다. OnUpdate는 초기화 이후 비활성화하며 저장 자료는 World 수명으로 유지한다. 공통 Fatal 게시자는 EndPower이고 중단 범위·소비/공개 시점은 유지한다.

PowerReadContext를 제거하고 각 Job에 필요한 Lookup을 직접 전달한다. 공유 검증 함수도 필요한 Lookup만 명시적 인자로 받는다. 당시 소비자/발전기 입력 검증을 별도 Job으로 나눴으나 아래 Writer 경계 정리에서 반복 구성 검사와 함께 제거했다. 공통 Lookup 생성기는 없다. 내부 검증 자료의 접근 순서와 ECB Producer/Fence 계약은 유지하며 병렬성/성능을 측정한 결과로 해석하지 않는다.

이번 소유자/Lookup 분리 변경은 Unity 컴파일 completed/failed false/errors 없음/compilationFailed false·Editor ready/비컴파일/비리로드·최신 runtime/Editor DLL과 관련 선택 EditMode 170/170(실패·생략·불확정 0)으로 확인했다. Play Mode·베이킹·Player 빌드·성능은 미수행이다.

## 검수 지적 보완 (2026-10-15)

예산 검증의 조기 반환이 후속 배분을 허용하던 경로를 호출별 Ready로 차단했다. 선택된 전신주 삭제/토폴로지 컴포넌트 제거를 발전기·소비자 검증 경로에서 각각 확인하는 4사례와 미준비 발전 계획·수요 2사례를 추가했다. 배분 예외 없음, 예약 미게시, 발전 계획·연료·공급·공개 연결 보존을 검사한다. 공급 결과 중복 초기화는 제거했으며 기존 누락 예약 테스트도 Preparation Reset부터 실행한다. 건물 구성 문서의 석탄 전환·생산 전력 반영 범위도 현재 구현에 맞췄다.

이번 보완은 Unity 컴파일 completed/failed false/errors 없음/compilationFailed false, Editor ready/비컴파일/비리로드, 최신 runtime/Editor DLL을 확인했다. 선택 EditMode는 Phase8PowerTopologyTests 53/53, Phase8PowerContractTests 26/26, Phase4MinerPipelineTests 5/5, Phase5CrafterExecutionTests 6/6, Phase5CrafterInputPipelineTests 19/19, Phase4EndToEndPipelineTests 3/3으로 총 112/112(실패·생략·불확정 0)이다. 이전 170/170과 별도 실행이며 Play Mode·베이킹·Player 빌드·성능은 미수행이다.

이번 선택 실행은 전력 73(Topology 47·Contract 26), 채굴/제작 33, 생성/완공/철거 54, 공유 자원 fixture 10으로 170사례다. 이전 실행과 합산하지 않는다. 기존 같은 틱 Power 차단·EndBuilding 오류의 다음 틱 차단·확정 소비·부분 공급·무전력 재료 보존·완료 출력·망 병합/분리·미준비 보존과 반복 World 정리 흐름을 유지했다. 소유권 이동/Lookup 필드 대입만 확인하는 별도 테스트는 추가하지 않았다.

## Writer와 검사 경계 (2026-10-15)

| 원본/계획 | Writer와 가시성 | 런타임 Reader의 계약 |
| --- | --- | --- |
| BuildingConfigElement.Power | BuildingConfigLoader가 JSON·공개·사전 등록을 검증하고 Initialization이 게시한다. 게시 후 불변이다. | 필요한 값을 조회하며 전력 설정 parser의 전체 검증을 반복하지 않는다. |
| 건물 종류·Tier·위치·방향·footprint·전력 필수 구성 | 공통 Spawn/완공이 EndBuilding에 부착한다. 승인 철거는 Command에 게시하고 실물 삭제는 EndBuilding이다. | Power 내부 Writer가 없으므로 생성된 구성을 사용한다. 각 단계가 존재·종류·설정·범위를 다시 검증하지 않는다. |
| PowerDemand·작업 준비 | Preparation의 단독 Writer. | Reservation은 준비 상태와 배분에 필요한 유한/비음수 수요를 검사한다. Execution은 수요를 다시 읽거나 대조하지 않는다. |
| PowerGridConnectionDecision | Power Decision의 단독 Writer. | Reservation이 대표 참조/준비를 확인하고 StateApply는 준비된 선택을 사용한다. |
| PowerGridTopologyDecision | Power Reservation의 단독 Writer. | Reservation의 대표/유지권을 후속 단계가 사용한다. ExpectedGrid 복사와 소비 직전/Apply의 재대조는 제거했다. |
| PowerGeneratorSupplyPlan·PowerSupplyReservation | 가용량은 Decision, 배정량·정수 연료 계획·소비자 공급률은 Reservation이 작성한다. | Execution은 준비 상태·실제 소비 조건을 확인하고 배분 결과를 게시한다. 대조 전용 대표·수요 사본은 제거했다. |
| FuelEnergyState·Coal 실물 | 생성 시 잔량 0. Power Execution이 잔량·저장 참조를 바꾸고 EndPower가 실물을 삭제한다. 이후 Building/Drone이 입출고한다. | 가용량은 현재 개수/잔량으로 계산하고, 실물 소비 검사는 Execution에서 삭제 대상에 한 번 수행한다. |
| PowerGridConnection·PowerGrid | StateApply가 EndPower에 기록한다. | Topology Reservation이 이전 공개 망을 읽어 유지권을 계산한다. 그 이후에는 같은 틱 원본 변경이 없으므로 Execution/StateApply가 다시 전체 검사하지 않는다. |

구성 불변식은 생성/변경 Writer가 유지한다. 소유 버퍼의 일반 Owner·참조 유일성은 기존 WorldInvariantValidationSystem의 Editor/Development Build 검사 대상이며 매 전력 단계에서 전체 버퍼를 순회하지 않는다. Topology의 이전 공개 망 검사는 유지권/삭제 대상 Entity를 안전하게 계획하는 한 지점에 남긴다. 단계 사이에 위치·연결·수요·필수 컴포넌트를 임의로 수정하는 외부 경로는 지원 계약이 아니며 해당 Writer가 추가되면 준비 시점과 의존성 경계를 다시 설계해야 한다.

Preparation/Decision의 소비자·발전기·선택 전체 검증 Job을 제거했다. 예산 검증 Lookup은 22개에서 9개, Execution은 26개에서 9개, StateApply 수집은 14개에서 2개로 줄었다. 준비 누락을 차단하는 Ready, 실제 소비 에너지/수량, 소비할 아이템의 Owner/종류/중복·Destroy와 전체 소비 전 검사, 기존 Fatal 범위·Fence·ECB Producer 등록은 유지한다. 성능 개선을 측정한 결과는 아니다.

테스트는 중간 Writer가 없는 구간에 공개 연결·위치·수요를 임의로 바꾸고 재검증을 요구하던 4사례를 제거했다. 공급 Reset 검사는 필수 컴포넌트 임의 제거 대신 정상 Preparation의 단독 초기화를 확인한다. 예산 준비 누락/무효 대표 6사례를 유지하고 소비 직전 준비 누락 2사례를 추가했다. 연료 Owner/중복 실패 사례는 실제 전환이 필요한 Miner 수요와 두 발전기를 사용해 어느 쪽도 부분 소비하지 않는지 검사한다.

Unity 컴파일 completed/failed false/errors 없음/compilationFailed false, Editor ready/비컴파일/비리로드와 최신 runtime/Editor DLL을 확인했다. 관련 선택 EditMode는 Phase8PowerTopologyTests 51/51, Phase8PowerContractTests 26/26, Phase4MinerPipelineTests 5/5, Phase4EndToEndPipelineTests 3/3, Phase5CrafterExecutionTests 6/6, Phase5CrafterInputPipelineTests 19/19, Phase7BuildingLifecycleTests 11/11, Phase7ConstructionCompletionTests 8/8, Phase7BuildingDemolishTests 19/19, Phase7ItemCreationDemolitionTests 16/16, Phase4ResourceAuthoringAndSpawnTests 10/10으로 총 174/174(실패·생략·불확정 0)이다. 이전 실행과 합산하지 않으며 Play Mode·베이킹·Player 빌드·성능은 미수행이다.
