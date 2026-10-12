# 전력 설정과 실행 계약

2026-10-12 Task 8.1 구현과 사용자 확정 규칙. 설정 검증·ECS 게시·컴포넌트 정의·순수 범위 조회와 실행 그룹 경계는 구현했다. 그룹은 아직 비어 있으며 망 구성/등록·수요 작성·공급 게시·연료 전환/정산·생산/충전 연결은 Task 8.2~8.7, 9.14, 10A.3의 후속이다. 아래 후속 Writer의 순서와 기대 결과를 실제 전력 가동 검증으로 해석하지 않는다.

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

이번 틱 수요는 생산·충전 도메인이, 배분과 공급 결과는 전력 도메인이, 실제 사용 결과는 소비 도메인이 기록한다. 생산·충전은 PowerSupplyResult와 공통 World 시간 정책을 사용하며 전력망 내부 연결/집계 API를 실행하지 않는다. [컴포넌트별 계약](../CodeMemory/Components/PowerGrid.md)을 따른다.

maximumGeneration/maximumConsumption은 에너지/초, coalEnergyPerItem/RemainingEnergy 및 Requested/Allocated/UsedEnergy는 에너지 단위다. 모든 도메인은 같은 World 시간의 `DeltaTime = min(World DeltaTime, GameConstants.MaxSimulationDeltaTime)` 정책을 사용한다. 전력 전용 시계 singleton과 프레임 데이터의 Tick 필드는 두지 않는다.

매 틱 준비에서 전력 소유자는 모든 공급 결과·발전기 계획을, 소비 도메인은 모든 수요·실제 사용 결과를 먼저 0으로 초기화한다. 작업 불가·수요 없음·공급 없음·단절된 참가자도 초기화에서 제외하지 않는다. 초기화가 모두 끝난 뒤 이번 틱 수요/계획을 작성하고 공급 실행에서 예산을 한 번 게시한다. 이전 결과나 필수 데이터 누락을 전력 충분 상태로 보충하지 않는다. 실제 초기화 Writer는 후속이며 컴포넌트 삭제만으로 이 처리가 실행된 것은 아니다.

## 그룹과 같은 틱의 읽기/쓰기 순서

최상위 Command → Building → Drone → Commit → Synchronization 순서와 세 ECB 경계는 유지한다. 아래 세 그룹은 기존 도메인 안의 자식이며 각 도메인을 다시 실행하지 않는다.

| 경계 | 후속 Writer/Reader 계약 |
| --- | --- |
| Command/EndCommand | 승인 철거 대상은 이번 틱 토폴로지·수요·발전 후보에서 제외한다. 현장은 전력 참가자가 아니다. 레시피 변경은 뒤의 BuildingDecision에서 읽는다. |
| BuildingDecision | 기존 CanMine/CanStartCraft/CanAdvance를 작성한다. 전력 공급 여부로 작업 자격을 먼저 0으로 만들지 않는다. |
| BuildingReservation 마지막: **PowerPreparationGroup** | 각 소유 도메인이 프레임 수요/공급/사용/발전 계획을 먼저 0으로 초기화한다. 소비 도메인의 수요 준비 Writer가 전력 외 작업 자격 및 충전 후보/배터리로 PowerDemand를 준비한다. 전력 소유자는 생존 원본 ECS로 토폴로지·보관 연료를 읽어 연결/가용량·배분 계획을 작성한다. 망별 합계는 전력 소유자의 일시 집계다. 앞선 틱 공간 인덱스만으로 같은 틱 철거/생존을 판단하지 않는다. |
| BuildingExecution 첫 자식: **PowerSupplyExecutionGroup** | 연료 준비 Writer가 기존 StoredItemElement를 다시 읽고 실물 생존/ItemIdentity/Owner/활성 Destroy를 검사하여 배정량을 뒷받침할 만큼의 기존 석탄만 잔여 에너지로 전환한다. 신규 입고는 이 단계보다 뒤이므로 별도 연료 엔티티 스냅샷 버퍼를 두지 않는다. 이후 전력 게시 Writer가 이번 틱 공급을 한 번 게시한다. 아직 공급되지 않은 계획 자체를 소비하지 않는다. |
| 일반 BuildingExecution | 생산은 이번 틱 공급 비율·에너지 예산 안에서 진행하고 실제 사용량을 기록한다. 연료 전환/공급 게시와 생산 Job의 명시적 ECS 의존성을 연결한다. 그룹 순서만으로 Job 완료를 가정하지 않는다. |
| BuildingStateApply/EndBuilding | 전환 석탄은 기존 버퍼 선행 제거 → DestroyItemRequest 활성화 → ItemLifecycle → EndBuilding 삭제로 소비한다. 일반 입고와 완공/직접 생성이 확정된다. 이번 틱 새 석탄·전력 참가자는 이미 준비한 공급 예산에 추가하지 않고 다음 틱부터 사용한다. |
| DroneSimulation | 준비한 충전 후보·예산과 같은 World DeltaTime 정책으로 실제 충전분을 기록한다. 새 귀환 드론은 다음 틱 충전 후보이며 후속 배차 등으로 실행하지 못한 충전의 사용량은 0이다. |
| SimulationCommit 안: **PowerSettlementGroup** | 건물·드론의 이번 틱 실제 사용을 모아 기초 발전을 먼저 배정하고 부족분만 석탄 발전으로 정산한다. 발전기별 실제 발전량과 망별 합계는 정산 내 파생 계산이며 독립 ECS 결과/집계로 중복 보관하지 않는다. 연료 에너지를 갱신하고 이 시점에 새 석탄 Destroy 요청을 발행하지 않는다. 미사용 예산을 같은 틱에 재배분하지 않는다. |
| EndSimulation/Synchronization | 드론 구조 변경과 결과 공개 뒤 공간 인덱스를 갱신한다. 연료 실물 소비를 지연하여 활성 Destroy 요청을 남기거나 삭제 ECB를 추가 재생하지 않는다. |

PowerPreparationGroup 안은 **소유 도메인별 프레임 초기화 → 도메인 수요 준비 → 토폴로지/가용량 계획 → 배분 계획** 순서다. PowerSupplyExecutionGroup 안은 **기존 보관 연료 최종 검사/전환 → 공급 게시** 순서다. PowerSettlementGroup 안은 **현재 틱 실제 사용 수집/검사 → 실제 발전량/연료 부담량 계산 → 연료 차감** 순서다. 후속 시스템은 UpdateBefore/After와 Job 의존성으로 이 순서를 구체화한다. 현재 비어 있는 그룹을 실제 Writer 구현으로 표시하지 않는다.

준비에서 계산한 가용량/배정량은 PowerGeneratorSupplyPlan으로 전달하고 실제 연료는 공급 실행 시 기존 보관 버퍼를 재검사한다. 해당 구간의 연료 재고는 이 연료 도메인이 소비하며 새 입고는 뒤의 StateApply에 있다. ECB 구조 변경을 넘어 DynamicBuffer 핸들을 유지하지 않으며 필요하면 버퍼를 다시 얻는다. 연료로 뒷받침되지 않는 공급은 게시하지 않으며 예상 밖 계획/설정 손상은 기존 Fatal 정책으로 처리한다. 전체 틱 rollback·중단 월드 재개·자동 복구는 제공하지 않는다.

## 작업 수요와 무전력

- Miner는 CanMine일 때, Crafter는 CanStartCraft 또는 CanAdvance일 때 현재 정격량 × 공통 DeltaTime을 요청한다. Crafter가 이미 진행 완료 상태에서 결과 확정/출력 공간만 기다리면 수요 0이다. 미완료 Crafter의 출력 공간과 진행 허용 여부는 현재 결정을 유지한다.
- 공급률 0 또는 사용할 에너지 예산이 없으면 신규 제작 착수·재료 선소비를 대기하고 진행 중 상태를 보존한다. 완료된 결과 확정/출고는 전력 때문에 막지 않으며 기존 전체 출력 공간 검사는 유지한다.
- 실제 진행에 사용한 시간/충전량에 해당하는 에너지만 UsedEnergy로 기록하고 배정량을 넘지 않는다. 틱 중 완료 또는 최종 검사 실패로 사용하지 않은 부분은 연료에서 차감하지 않는다.
- 연구는 전력 외 착수 조건 또는 진행할 로컬 주기를 기준으로 수요를 준비한다. 실제 신규 착수는 기존 [연구 명세](ResearchBuilding.md)의 공급률 > 0 조건을 따른다.
- 수요 0의 공급률 표시는 1이어도 배정·사용 에너지는 0이며 전력 작업을 수행할 수 없다. 단절된 참가자의 공급 비율·배정량은 0이다. 공급 비율만 읽어 배정 에너지가 없는 작업을 실행하지 않는다.

## 석탄 전환과 실제 사용 정산

석탄 실물 → 잔여 에너지 전환과 실제 발전으로 에너지를 차감하는 연소를 구분한다. 전환은 필요한 정수 개수만, 연소는 실제 사용량만 수행한다. 예를 들어 잔량 0·석탄 1개·배정 10에서 실행 사용량이 0이면 준비 단계에서 전환한 600을 보존하고 실제 발전은 0이다. 같은 상황에서 실제 석탄 발전량이 4이면 잔여 에너지는 596이다. 전환 후 생긴 저장 공간을 이번 틱 입고 예약에 다시 추가하지 않는다.

정상 틱에서 아래를 대조한다.

- 소비자마다 `0 <= UsedEnergy <= AllocatedEnergy <= RequestedEnergy`.
- 발전기마다 `0 <= GeneratedEnergy <= AllocatedEnergy <= AvailableEnergy <= maximumGeneration × DeltaTime`.
- 망의 실제 발전 합계 = 소비자의 실제 사용 합계. 서로 다른 망의 발전/수요는 섞지 않는다.
- 생존 발전기의 석탄 에너지 보존: `이전 잔량 + (이전 실물 개수 + 이번 틱 입고) × coalEnergyPerItem = 이후 잔량 + 이후 실물 개수 × coalEnergyPerItem + 실제 석탄 발전 에너지`. 새 입고는 보존식에만 포함하고 이번 틱 발전 예산에는 포함하지 않는다. 철거로 제거한 상태와 반환 실물은 별도 수명주기 정산으로 구분한다.
- 마지막 연료가 최대 출력 한 틱분보다 적으면 그 에너지로 가능한 만큼만 배정한다. 부족한 발전을 먼저 게시한 뒤 연료 차감에서 줄이지 않는다.

## 범위와 경계 사례의 기대 결과

PowerRangeUtility는 단일 셀 반경의 경계를 포함하는 정사각형을 판정한다. range 5는 11×11셀, 주 시설 가상 공급 range 0은 자기 셀이다. 전신주는 한쪽 연결 범위에 상대 중심이 들어가면 연결한다(OR). 공급 영역 중첩만으로 망을 병합하지 않는다. 공급 선택은 회전 footprint 전체를 검사하는 Task 8.4의 계약으로 연결한다.

| 사례 | 확정 기대 결과 | 실행 검증 담당 |
| --- | --- | --- |
| 연료 없음 → 틱 N 신규 석탄 입고 | N 공급은 기존 연료만 사용; 새 석탄은 N+1부터 발전 가능 | 8.6/8.7 |
| 마지막 연료가 한 틱 최대 출력보다 부족 | 같은 틱에 연료로 가능한 부분 공급·진행; 잔량 음수/미지원 공급 없음 | 8.5~8.7 |
| N에 전신주/발전기/소비자 완공·직접 생성 | EndBuilding에 실체화하고 N+1부터 공급 계산에 참가 | 8.7 |
| N Command에서 공급 중 참가자 철거 승인 | N 계획부터 제외; EndBuilding 삭제 후 고아 연결 없음 | 8.3/8.7 |
| 재료 없음/진행 완료 대기 ↔ 작업 가능 | 해당 틱의 공개 도메인 결정으로 수요 0 ↔ 정격 수요; 전력이 없다는 이유로 수요를 지우지 않음 | 8.7/10A.3 |
| 계획 뒤 실행 실패·틱 중 작업 완료 | 실제 사용분만 정산; 미사용분 재배분 없음; 전환 연료의 미사용 에너지 보존 | 8.6/8.7 |
| N 새 귀환·후속 배차·완충 | 새 귀환은 N+1 충전; 실행하지 않은 초기 후보는 실제 사용 0 | 9.14 |
| range 경계·다른 Tier의 range | 경계 포함·정사각형·OR 연결; 전체 footprint 공급 선택과 구분 | 순수 판정 8.1; 망/참가자 실행 8.2~8.4 |

## Task 8.1 검증 근거와 한계

2026-10-12 Unity Editor 재컴파일은 completed/failed false/errors 없음, 리로드 종료와 runtime/editor DLL 최신성을 확인했다. 선택 EditMode는 Phase8PowerContractTests 26/26, Phase3BuildingRuntimeConfigTests 1/1, Phase7BuildingAuthoringPrefabTests 24/24, PrefabDatabaseInitializationTests 19/19로 총 70/70 통과했다.

이는 설정/게시/초기화 실패·Tier 분리·순수 범위와 기존 준비 관문의 검증이다. 위 실제 틱 실행·연료 전환·생산/충전·설치/철거 사례, Play Mode·베이킹·Player 빌드·성능은 실행하지 않았다. 각 후속 Task가 실제 GameSimulationGroup/ECB 경계에서 검증한다.

2026-10-12 후속 사용자 지시로 독립 ECS 타입 4개와 전력 프레임 Tick 필드를 제거하여 현재 계약은 10개다. 기존 시간 정책·프레임 초기화·보관 버퍼 재검사·일시 집계/정산으로 역할을 정리했다. 이번 정의 정리는 Editor 컴파일만 확인했으며 위 70사례는 최초 구현 검증 기록으로 유지한다. 실제 초기화/정산 Writer는 여전히 후속이다.
