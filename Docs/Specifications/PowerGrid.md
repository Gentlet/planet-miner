# 전력 설정과 실행 계약

2026-10-14 Task 8.1~8.5를 구현했다. 선택·실제 Miner/Crafter 수요·발전 가용량은 Decision, 토폴로지·전력 예산 배정은 Reservation, 공급 결과 게시는 Execution, 망 수명주기/연결 기록은 StateApply와 EndBuilding이 소유한다. 공통 생성/완공에 발전/소비 역할과 프레임 상태를 부착했다. 실제 석탄 입고/전환/연료 정산은 8.6, 생산 반영/실제 사용량 기록은 8.7, 연구·충전은 각 후속 Task다. 전력 전용 그룹은 추가하지 않는다.

## 설정 원본과 검증

- 유일한 입력은 `Assets/Resources/Config/BuildingConfig.json`이다. 기존 `BuildingConfigInitSystem`/`BuildingConfigLoader`가 전력 필드까지 검증하여 같은 `BuildingConfigElement` 버퍼에 게시한다. 별도 PowerConfig 로더·singleton·호환 전력 원본을 만들지 않는다. 기존 PowerConfig.json과 meta는 이관 후 제거했다.
- 건물별 선택 키는 기존 BuildingTypeEnum + 명시적 양수 Tier다. 다른 Tier의 값 상속/대체는 없다. `BuildingConfigElement.Power: BuildingPowerConfig`는 능력 설정이며 실제 수요·공급·진행·연료 잔량을 기록하지 않는다.
- 아래 종류는 `power` 객체와 역할별 필드를 모두 명시한다. 누락·null·잘못된 JSON 형식·알 수 없는/다른 역할의 필드·비정상 숫자는 실패다. range는 JSON 정수 0~int.MaxValue이며 전력/에너지는 유한 양수다. 비대상 종류의 power 객체도 거절한다.
- MainFacility의 가상 전신주 supplyRange는 0으로 고정한다. connectionRange는 자신의 Tier 정의에서 읽으며 초기 1티어는 기존 일반 전신주와 같은 8을 명시했다. PowerPole 정의를 대신 조회하는 fallback은 없다.
- CoalGenerator의 storageCapacity는 명시적 정수 1~GameConstants.MaxStorageSlots다. 현재 1티어는 사용자 지정 임시 1슬롯이며 슬롯당 용량은 기존 ItemRegistry의 Coal MaxStack 50이다. 별도 fuelStorageCapacity/실물 10개 제한을 두지 않는다.
- coalEnergyPerItem은 발전기 종류·Tier별 연료 효율이며 1티어의 임시값 600은 후속 밸런스 조정 대상이다.
- JSON·공개 PublishConfig·사전 등록 ValidatePublishedConfig는 같은 전력 값 계약을 적용한다. HasConfiguration은 필수 power 객체의 명시 여부를 보존하여 ECS 입력의 누락도 검출한다. 무효 공개 입력은 엔티티를 생성하지 않으며 Init 실패는 기존 로그·미게시·SimulationFatalError·자동 재시도 없음 정책을 따른다. 무효 사전 등록 원본은 보존한다.

| 종류 | power 필수 필드 | 현재 1티어 값 |
| --- | --- | --- |
| PowerPole | supplyRange, connectionRange | 5, 8 |
| MainFacility | supplyRange, connectionRange, maximumGeneration | 0, 8, 40 |
| CoalGenerator | maximumGeneration, coalEnergyPerItem | 100, 600; storageCapacity 1 |
| Miner | maximumConsumption | 10 |
| Crafter | maximumConsumption | 20 |
| ResearchBuilding | maximumConsumption | 20 |

DroneStation/MainFacility의 충전은 후속 드론 도메인이 후보별로 작성하는 동적 수요다. 현재 DroneConfig의 충전 능력/전력 변환 계약은 Task 9.14에서 연결하며 임의의 고정 최대 소비량을 새로 부여하지 않는다. power 객체가 없는 일반 건물을 필수 전력 소비자로 취급하지 않는다.

## 데이터 소유와 단위

설정 원본은 BuildingConfig, 토폴로지 원본은 전신주 등록 상태와 연결 집합, 실물 원본은 기존 ItemOwnership/StoredItemElement, 연료 에너지 원본은 FuelEnergyState다. FuelEnergyState는 연료 종류·소비처에 독립적인 잔량이며 현재 석탄 설정을 다른 연료 정의로 자동 확장한 것은 아니다. 망 집계·연결·셀 공급 조회는 이 원본에서 계산하는 파생 데이터다. 기존 건물 점유 인덱스와 같은 원본을 소유하는 공간 캐시를 추가하지 않는다.

사용자 확정(2026-10-12): 전신주와 망은 각각 PowerPole/PowerGrid 태그가 부착된 별도 Entity로 식별하며 StableId나 번호 발급 카운터를 두지 않는다. PowerGridConnection은 기존 Entity 참조를 유지한다. 토폴로지의 처리 순서·병합 대표 전신주 선택과 공급 거리 동률 처리는 기존 PlacementStamp.Compare의 설치 기록 유무·기록 비교·격자 좌표 순서를 재사용한다. Entity의 숫자 순서는 설치 순서가 아니다. 현재 생성/병합·제거/분리 수명주기와 Task 8.4의 일반 건물 공급 전신주 선택을 구현했다.

이번 틱 수요는 생산·충전 도메인이, 배분과 공급 결과는 전력 도메인이, 실제 사용 결과는 소비 도메인이 기록한다. 생산·충전은 PowerSupplyResult와 공통 World 시간 정책을 사용하며 전력망 내부 연결/집계 API를 실행하지 않는다. [컴포넌트별 계약](../CodeMemory/Components/PowerGrid.md)을 따른다.

maximumGeneration/maximumConsumption은 에너지/초, coalEnergyPerItem/RemainingEnergy 및 Requested/Allocated/UsedEnergy는 에너지 단위다. 모든 도메인은 같은 World 시간의 `DeltaTime = min(World DeltaTime, GameConstants.MaxSimulationDeltaTime)` 정책을 사용한다. 전력 전용 시계 singleton과 프레임 데이터의 Tick 필드는 두지 않는다.

매 틱 Decision에서 PowerSupplyDecisionSystem은 공급 결과·미게시 예약·발전기 계획을, BuildingPowerDemandDecisionSystem은 수요·실제 사용 결과를 먼저 0으로 초기화한다. Disabled·승인 철거·무수요·단절도 기존 필드의 초기화에서 제외하지 않고 Prefab은 제외한다. 각 필드를 독립 초기화하여 다른 필수 컴포넌트가 누락되어도 이전 결과를 남기지 않는다. 수요/가용량의 활성은 준비 완료이며 값 0도 정상 준비 완료다. Reservation은 별도 PowerSupplyReservation에 예산을 배정하고 Execution이 최종 검사 후 한 번 게시한다. 이전 결과나 필수 데이터 누락을 전력 충분 상태로 보충하지 않는다.

## 전신주 생성·망 준비·공개 기획

- 공통 직접 Spawn/현장 완공에서 일반 전신주와 주 시설에 전신주 태그·프레임 결정과 자기 전신주/미연결 망의 초기 연결을 부착한다. Task 8.4는 Miner·Crafter·ResearchBuilding·CoalGenerator·DroneStation에 초기 미연결 PowerGridConnection과 비활성 PowerGridConnectionDecision을 부착한다. 발전/소비 역할·수요/공급/연료 상태와 생산 연결은 해당 후속 Task, 주 시설 자동 생성은 9.22다.
- 전신주 연결은 생존 ECS·BuildingConfig의 종류/Tier ConnectionRange·GridPosition.Value·기존 설치 기록을 사용한다. 승인 철거와 비활성/Prefab 참가자를 제외하고 이전 공간 인덱스만으로 이번 틱 생존을 판단하지 않는다.
- 망은 전신주와 별도 Entity로 관리한다. 별도 StableId/번호 발급은 사용하지 않고 기존 PlacementStamp.Compare로 순서를 정한다.
- 한쪽 정사각형 연결 범위에 상대 중심이 들어가면 연결한다(OR). 독립 집합은 별도 망이며 공급 범위 중첩만으로 병합하지 않는다. 병합 시 기존 유효 망 소속 전신주 중 가장 이른 설치 멤버의 망 Entity를 유지하고 나머지는 소속 갱신과 같은 ECB 경계에 제거한다. 새 전신주의 설치 기록이 더 빨라도 기존 망 중에서 유지 대상을 선택한다.
- 이번 틱 연결 계산 결과는 같은 틱 공급 계획에 사용하고 실제 망 생성/소속 변경은 EndBuilding에 공개한다. N틱 EndBuilding에서 생성된 전신주는 N+1부터 공급 계산에 참여한다. 준비 결과에는 살아 있는 대표 전신주를 사용하고 ECB 예약 망 Entity는 생성/연결을 기록한 동일 ECB 안에서만 참조한다.
- 망 준비는 기존 BuildingReservation 마지막에서 Burst Job으로 수행하며 전력 전용 페이즈를 추가하지 않는다. 메인 스레드의 입력 검증 실패는 이전 결정을 비운 뒤 EndBuilding에 Fatal을 게시하며 잘못된 망 참조를 새 망으로 보충하지 않는다. Apply는 활성 전신주의 준비 결과가 없거나 구성이 누락되면 정상 변경을 시작하지 않아, 입력 실패를 빈 망으로 오인해 기존 망/참조를 삭제하지 않는다.
- PowerGridTopologyReservationSystem은 연결/대표/유지 망 계획만 작성한다. 망 생성·연결 변경·병합 망 삭제 기록은 BuildingStateApply의 PowerGridTopologyApplySystem으로 분리했다. Apply는 현재 생존/활성과 ExpectedGrid 스냅샷·대표 계획을 최종 대조하며 연결 집합/대표를 재탐색하지 않는다. 실패 시 정상 망 변경 없이 Fatal만 기록한다. 정상 변경은 같은 EndBuilding ECB 안에서 생성/연결/삭제하며 예약 망 Entity를 다른 단계로 전달하지 않는다.
- 관련 파생 자료의 수명·갱신은 BuildingSpatialSyncSystem의 기존 인덱스/Fence 관례에 맞춘다. Sync는 Query에서 받은 공개 자료를 수집하며 참조 망 Entity를 Lookup으로 재검증하지 않는다. 연결 유효성은 망 생성·병합·정리 소유자가 보장하고 미연결 자료는 등록하지 않는다. 기존 건물 위치/점유 원본을 복제하지 않는다.
- 이번 틱 대표 전신주·유지할 망·최종 검사할 이전 공개 값은 PowerGridTopologyDecision으로 전달한다. RepresentativePole은 살아 있는 전신주, RetainedGrid/ExpectedGrid는 살아 있는 망 또는 Null이며 예약 Entity가 아니다. 공개 연결은 PowerGridConnectionIndex의 Entity→연결 맵으로 수집하고 PowerGridConnectionSyncSystem이 기존 Fence 관례로 생성/재구축/해제한다. 별도 준비 맵·Fence는 없으며 ECS 결정의 Job 의존성과 공개 Native 맵 Fence를 구분한다.

## 같은 틱의 읽기/쓰기 기획 순서

Task 8.3 사용자 확정 정책(2026-10-13):

- **1A — 분리 유지권:** 기존 망의 남은 활성 전신주 중 PlacementStamp.Compare가 가장 앞선 멤버의 연결 집합만 그 망 Entity를 유지할 수 있다. 다른 집합은 새 망을 생성한다. 이전 대표가 제거되면 남은 멤버로 유지권을 정한다.
- **2A — 분리 후 병합:** 각 기존 망의 유지권을 먼저 한 집합에 부여한다. 집합은 자신이 유지권을 가진 망 중 가장 이른 기존 멤버의 망을 유지한다. 같은 집합이 다른 망을 병합하여 남는 Entity는 다른 집합에 재배정하지 않고 삭제한다. 유지권이 없는 집합은 새 망을 생성한다.
- **3A — 비활성:** Disabled 전신주는 이번 틱 계산에서 제외하고 Grid를 Null로 해제한다. 자기 PowerPole 참조·건물 엔티티·PlacementStamp는 보존한다. 재활성화 시 미연결 참가자로 다시 연결하며, 과거 소속을 복원하거나 기존 망을 위해 비활성 상태의 빈 망을 유지하지 않는다.

Apply는 활성 전신주의 토폴로지와 활성 일반 참가자의 공급 선택을 모두 최종 검사한 뒤 런타임 PowerGridConnection을 Disabled 참가자까지 수집한다. 일반 참가자는 이번 틱 선택 전신주의 새 망으로 두 참조를 맞추며 활성 Null 선택이면 둘 다 해제한다. 비활성 참가자는 새 선택 없이 기존 전신주의 새 망으로 참조를 맞추거나 사라진/비활성/철거된 선택을 해제한다. 전신주 자체는 자기 참조를 유지한다. Prefab은 변경하지 않는다.

기존 PowerGrid 엔티티 전체를 별도 조회하므로 마지막 전신주 제거나 외부 제거 뒤에도 빈 망을 회수한다. 모든 정상 참조 변경 뒤 사용하지 않는 기존 망을 같은 EndBuilding ECB에 삭제하며 생성 예약 Entity는 그 ECB 안에서만 사용한다. 철거 자체는 기존 DemolishBuildingRequest → Command 승인/EndCommand → BuildingLifecycleApply/EndBuilding 경로를 재사용한다. 주 시설의 파괴 불가 규칙은 유지한다.

Task 8.4 사용자 확정 정책과 구현(2026-10-13):

- **1A — 전체 footprint:** 회전 후 점유 셀 중 하나라도 경계 포함 정사각형 공급 범위에 들어오면 후보다. 전신주 중심에서 직사각형 전체의 가장 가까운 점유 셀을 구해 최소 제곱거리로 비교한다. 동률은 PlacementStamp.Compare를 사용한다. 공급 범위 중첩은 망 병합 조건이 아니다.
- **2A — 매 틱 재선택:** 기존 선택이 유효해도 더 가까운 전신주로 변경한다. 발전 가능량이나 공급률로 전신주를 선택하지 않는다.
- **3A — 생성 연결:** 위 다섯 일반 건물 종류의 직접 생성과 완공에 연결/선택 데이터를 부착한다. PowerPole/MainFacility의 자기 전신주 참조는 보존한다. 발전/소비/연료/충전 역할과 실제 Writer의 초기화는 후속이다.
- **4A — 활성 선택:** Disabled·Prefab·승인 철거 참가자는 새 선택에서 제외한다. 비활성 참가자는 기존 참조 정리만 받고 활성화된 다음 준비에서 다시 선택한다. 현장은 참가자가 아니다.

PowerGridConnectionDecisionSystem은 BuildingDecisionGroup에서 실행한다. 모든 런타임 결정의 값/활성을 먼저 비우고 활성 일반 참가자의 위치/회전/크기와 활성 전신주의 종류/Tier·위치·설정을 검증한 뒤 Burst IJobEntity를 병렬 예약한다. 입력은 전신주 Entity 목록과 읽기 전용 설정/위치/종류/설치 Lookup이며 연결/점유 스냅샷이나 별도 준비 맵을 만들지 않는다. 토폴로지 준비 결과를 읽지 않는다. PowerGridConnectionDecision의 활성은 선택 준비 완료, PowerPole Null은 공급 범위 밖이며 토폴로지 준비 성공을 뜻하지 않는다.

Apply는 선택 전신주의 생존·활성·준비 상태와 현재 footprint의 공급 경계를 최종 검사하고 전신주를 재선택하지 않는다. 준비 누락이면 정상 변경을 시작하지 않으며 유효하지 않은 준비는 Fatal만 기록한다. 선택 전신주와 새/유지 망의 매핑은 기존 Apply의 같은 ECB에서 해결한다. N EndBuilding의 생성/완공은 N+1 선택부터 참여하고 N Command의 철거 승인은 N 선택에서 제외한다. 공유 공개 인덱스는 Synchronization에서만 갱신한다.

최상위 Command → Building → Drone → Commit → Synchronization 순서와 세 ECB 경계는 유지한다. 아래 준비·공급 실행·사용 정산은 보존한 기획 역할이며 별도 전력 그룹 정의는 제거했다. 실제 배치는 기존 시스템/그룹 관례에 맞춰 재구현하고 각 도메인을 다시 실행하지 않는다.

| 경계 | 현재 및 후속 Writer/Reader 계약 |
| --- | --- |
| Command/EndCommand | 승인 철거 대상은 이번 틱 토폴로지·수요·발전 후보에서 제외한다. 현장은 전력 참가자가 아니다. 레시피 변경은 뒤의 BuildingDecision에서 읽는다. |
| BuildingDecision | 기존 작업/연결 결정 뒤 BuildingPowerDemandDecisionSystem이 실제 Miner/Crafter 작업 자격으로 수요를 준비한다. PowerSupplyDecisionSystem은 전력 소유 필드를 초기화하고 MainFacility의 정격량과 CoalGenerator의 이미 전환된 RemainingEnergy로 가용량을 준비한다. 공급 여부로 작업 자격을 먼저 지우지 않는다. |
| BuildingReservation 마지막: **예산 배정** | PowerBudgetReservationSystem은 TopologyReservation 뒤 이번 틱 선택/대표 계획과 준비된 수요/가용량으로 망별 예산을 배정한다. 소비자 PowerSupplyReservation과 발전기 AllocatedEnergy를 기록한다. 집계는 Job 안의 임시 Native 맵이며 이전 공개 Grid/인덱스나 영속 집계는 쓰지 않는다. |
| BuildingExecution 시작의 **공급 게시** | PowerSupplyExecutionSystem은 배정 기준 대표/수요, 현재 선택/공급 경계와 발전/연료 한도·망별 배정 합계를 최종 검사하고 PowerSupplyResult를 한 번 게시한다. 8.5는 미전환 석탄을 공급하지 않는다. 8.6의 실물 조회/전환은 공급 게시 전에 연결하며 신규 입고는 다음 틱부터 쓴다. |
| 일반 BuildingExecution | 8.7에서 생산을 이번 틱 공급 비율·에너지 예산에 연결하고 실제 사용량을 기록한다. 현재 Miner/Crafter Execution의 진행/선소비 동작은 변경하지 않았다. 공급 게시와 후속 생산 Job의 명시적 ECS 의존성을 연결하며 그룹 순서만으로 Job 완료를 가정하지 않는다. |
| BuildingStateApply/EndBuilding | 준비된 망 계획을 최종 검사하여 생성/병합/분리·비활성 해제·모든 런타임 참조 정리·빈 망 삭제를 같은 ECB에 기록하고 공개한다. 전신주/일반 건물의 물리적 삭제는 기존 BuildingLifecycleApply가 소유한다. 후속 전환 석탄은 기존 ItemLifecycle/EndBuilding으로 소비하며 새 석탄·전력 참가자는 다음 틱부터 공급에 참여한다. |
| DroneSimulation | 준비한 충전 후보·예산과 같은 World DeltaTime 정책으로 실제 충전분을 기록한다. 새 귀환 드론은 다음 틱 충전 후보이며 후속 배차 등으로 실행하지 못한 충전의 사용량은 0이다. |
| SimulationCommit 안: **후속 사용 정산 단계** | 8.6/8.7 이후 건물·드론의 이번 틱 실제 사용을 모아 기초 발전을 먼저 배정하고 부족분만 석탄 발전으로 정산한다. 발전기별 실제 발전량과 망별 합계는 정산 내 파생 계산이며 독립 ECS 결과/집계로 중복 보관하지 않는다. 연료 에너지를 갱신하고 이 시점에 새 석탄 Destroy 요청을 발행하지 않는다. 미사용 예산을 같은 틱에 재배분하지 않는다. |
| EndSimulation/Synchronization | 드론 구조 변경과 결과 공개 뒤 공간 인덱스를 갱신한다. 연료 실물 소비를 지연하여 활성 Destroy 요청을 남기거나 삭제 ECB를 추가 재생하지 않는다. |

3A에 따라 순수 선택·수요·가용량 판단과 소유 필드 초기화는 Decision, 유한 예산 배정은 Reservation, 최종 공급 게시는 Execution이다. Demand는 MinerDecisionSystem/CrafterDecisionSystem 뒤, Budget는 PowerGridTopologyReservationSystem 뒤 OrderLast, SupplyExecution은 OrderFirst이며 Miner/Crafter Execution보다 앞이다. Entity 목록·읽기 전용 Lookup과 state.Dependency를 사용하고 임시 입력 해제 핸들을 등록한다. 실제 사용 정산 역할은 SimulationCommit의 EndSimulation 앞이며 8.6/8.7 후속이다.

발전기 가용량/배정량은 PowerGeneratorSupplyPlan, 소비자 미게시 예산은 PowerSupplyReservation으로 전달한다. 배정 기준 대표와 수요는 게시 직전 대조할 예산의 근거이며 공개 망 Entity나 위치/연결 원본의 캐시가 아니다. Budget/게시의 메인 스레드 검사는 해당 ECS Writer를 명시적으로 완료하고 Job은 Lookup 의존성을 등록한다. 미준비 입력은 정상 0으로 게시하지 않고, 잘못된 입력/손상된 배정은 EndBuilding에 Fatal을 기록하고 공급 결과를 초기값 0으로 유지한다. 정상 Result 필드 게시에는 ECB를 쓰지 않으며 망 생성/연결의 EndBuilding 경계와 구분한다. 전체 틱 rollback·중단 월드 재개·자동 복구는 제공하지 않는다.

Task 8.5 사용자 확정 1A·2A·3A·4A:

- **1A:** 같은 망의 소비자는 요청 에너지에 비례한 공통 공급 비율을 적용한다. 기초 발전을 먼저 배정하고 같은 종류의 발전기는 정격 최대 출력 기준 동일 가동률로 나눈다. 연료 한도에 걸린 발전기를 고정하고 나머지가 부족분을 분담한다. 틱당 정격 10·10, 가용 1·10, 석탄 부담 5.5이면 배정은 1·4.5다. Tier별 정격 차이도 적용한다.
- **2A:** MainFacility/CoalGenerator에 발전 역할·계획을, MainFacility/Miner/Crafter/ResearchBuilding/DroneStation에 소비 역할·수요/예약/공급/사용 상태를 공통 생성/완공에서 부착한다. CoalGenerator의 FuelEnergyState는 0으로 생성한다. 실제 Miner/Crafter 수요 Producer만 이번에 연결하며 연구/충전의 수요는 후속까지 0이다. 공통 생성은 여전히 실제 게임 입력/주 시설 자동 생성 Producer를 대신하지 않는다.
- **3A:** 위 역할에 맞춘 Decision → Reservation → Execution을 적용한다. PowerDemand/PowerGeneratorSupplyPlan/PowerSupplyReservation의 활성은 단계별 준비 완료이며 IRequestComponent가 아니다. 무수요·무발전의 준비 완료 0과 준비 실패/미실행을 구분한다.
- **4A:** MainFacility는 정격 × 공통 DeltaTime, CoalGenerator는 min(정격 × DeltaTime, 이미 전환된 RemainingEnergy)만 공급 근거로 사용한다. StoredItemElement의 미전환 Coal은 8.5 가용량에 포함하지 않는다. 실물 조회/전환·연료 차감은 8.6, 진행량/실제 사용량은 8.7이며 그 전에도 공급을 수요/가용량 한도 내에서 게시한다.

망별 합계는 double 임시 계산, 개별 에너지 필드는 float이다. 발전기 float 배정 합계를 기준으로 공통 소비 비율을 계산하고 에너지 변환을 아래로 반올림하여 소비자 총 예산이 발전기 배정을 넘지 않게 한다. 미세한 미배정량과 실행되지 않은 예산도 같은 틱에 재배분하지 않는다. 단절은 비율/배정 0, 연결된 망의 총 수요 0은 비율 1/배정 0이다.

## 작업 수요와 무전력

- Miner는 CanMine일 때, Crafter는 CanStartCraft 또는 CanAdvance일 때 현재 정격량 × 공통 DeltaTime을 요청한다. Crafter가 이미 진행 완료 상태에서 결과 확정/출력 공간만 기다리면 수요 0이다. 미완료 Crafter의 출력 공간과 진행 허용 여부는 현재 결정을 유지한다.
- 공급률 0 또는 사용할 에너지 예산이 없으면 신규 제작 착수·재료 선소비를 대기하고 진행 중 상태를 보존한다. 완료된 결과 확정/출고는 전력 때문에 막지 않으며 기존 전체 출력 공간 검사는 유지한다.
- 후속 실제 진행에 사용한 시간/충전량에 해당하는 에너지만 UsedEnergy로 기록하고 배정량을 넘지 않는다. 현재는 Decision의 0 초기화만 연결했으며 성공분 기록은 8.7/9.14/10A.3이다. 틱 중 완료 또는 최종 검사 실패로 사용하지 않은 부분은 연료에서 차감하지 않는다.
- 연구는 전력 외 착수 조건 또는 진행할 로컬 주기를 기준으로 수요를 준비한다. 실제 신규 착수는 기존 [연구 명세](ResearchBuilding.md)의 공급률 > 0 조건을 따른다.
- 수요 0의 공급률 표시는 1이어도 배정·사용 에너지는 0이며 전력 작업을 수행할 수 없다. 단절된 참가자의 공급 비율·배정량은 0이다. 공급 비율만 읽어 배정 에너지가 없는 작업을 실행하지 않는다.

## 석탄 전환과 실제 사용 정산

이 절은 8.6 이후의 확정 계약이며 8.5의 현재 전환/연소 구현이 아니다. 석탄 실물 → 잔여 에너지 전환과 실제 발전으로 에너지를 차감하는 연소를 구분한다. 전환은 필요한 정수 개수만, 연소는 실제 사용량만 수행한다. 예를 들어 잔량 0·석탄 1개·배정 10에서 실행 사용량이 0이면 공급 실행에서 전환한 600을 보존하고 실제 발전은 0이다. 같은 상황에서 실제 석탄 발전량이 4이면 잔여 에너지는 596이다. 전환 후 생긴 저장 공간을 이번 틱 입고 예약에 다시 추가하지 않는다.

정상 틱에서 아래를 대조한다.

- 소비자마다 `0 <= UsedEnergy <= AllocatedEnergy <= RequestedEnergy`.
- 발전기마다 `0 <= GeneratedEnergy <= AllocatedEnergy <= AvailableEnergy <= maximumGeneration × DeltaTime`.
- 망의 실제 발전 합계 = 소비자의 실제 사용 합계. 서로 다른 망의 발전/수요는 섞지 않는다.
- 생존 발전기의 석탄 에너지 보존: `이전 잔량 + (이전 실물 개수 + 이번 틱 입고) × coalEnergyPerItem = 이후 잔량 + 이후 실물 개수 × coalEnergyPerItem + 실제 석탄 발전 에너지`. 새 입고는 보존식에만 포함하고 이번 틱 발전 예산에는 포함하지 않는다. 철거로 제거한 상태와 반환 실물은 별도 수명주기 정산으로 구분한다.
- 마지막 연료가 최대 출력 한 틱분보다 적으면 그 에너지로 가능한 만큼만 배정한다. 부족한 발전을 먼저 게시한 뒤 연료 차감에서 줄이지 않는다.

## 범위와 경계 사례의 기대 결과

PowerRangeUtility는 단일 셀 반경의 경계를 포함하는 정사각형을 판정한다. range 5는 11×11셀, 주 시설 가상 공급 range 0은 자기 셀이다. 전신주는 한쪽 연결 범위에 상대 중심이 들어가면 연결한다(OR). 공급 영역 중첩만으로 망을 병합하지 않는다. TryGetFootprintDistanceSquared는 회전된 직사각형 전체의 가장 가까운 점유 셀을 사용해 공급 가능 여부와 최소 제곱거리를 반환하며 Task 8.4의 선택/최종 검사가 공유한다.

| 사례 | 확정 기대 결과 | 실행 검증 담당 |
| --- | --- | --- |
| 연료 없음 → 틱 N 신규 석탄 입고 | N 공급은 기존 연료만 사용; 새 석탄은 N+1부터 발전 가능 | 8.6/8.7 |
| 마지막 연료가 한 틱 최대 출력보다 부족 | 같은 틱에 연료로 가능한 부분 공급·진행; 잔량 음수/미지원 공급 없음 | 8.5~8.7 |
| N에 전신주/발전기/소비자 완공·직접 생성 | EndBuilding에 실체화하고 N+1부터 공급 계산에 참가 | 연결 8.2~8.4; 수요/공급 게시 8.5; 생산 8.7 |
| N Command에서 공급 중 참가자 철거 승인 | N 계획부터 제외; EndBuilding 삭제 후 고아 연결 없음 | 8.3/8.7 |
| 재료 없음/진행 완료 대기 ↔ 작업 가능 | 해당 틱의 공개 도메인 결정으로 수요 0 ↔ 정격 수요; 전력이 없다는 이유로 수요를 지우지 않음 | 8.7/10A.3 |
| 계획 뒤 실행 실패·틱 중 작업 완료 | 실제 사용분만 정산; 미사용분 재배분 없음; 전환 연료의 미사용 에너지 보존 | 8.6/8.7 |
| N 새 귀환·후속 배차·완충 | 새 귀환은 N+1 충전; 실행하지 않은 초기 후보는 실제 사용 0 | 9.14 |
| range 경계·다른 Tier의 range | 경계 포함·정사각형·OR 연결; 전체 footprint 공급 선택과 구분 | 순수 판정 8.1; 망/참가자 실행 8.2~8.4 |

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
