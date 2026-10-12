# 전력 컴포넌트 계약

2026-10-12 Task 8.1에서 설정·컴포넌트 정의·실행 그룹 경계를 추가했다. 현재 제품 Writer/참가자 부착은 미구현이며 아래 생성·읽기/쓰기 주체는 후속 Task가 지켜야 할 계약이다. 설정 게시만 현재 BuildingConfigInitSystem이 수행한다. 실제 게임 규칙·에너지 단위·틱별 기대 결과와 검증 범위는 [전력 명세](../../Specifications/PowerGrid.md)를 따른다.

## 설정 원본

`BuildingConfigElement.Power`의 [BuildingPowerConfig](../../../Assets/Scripts/Components/Power/BuildingPowerConfig.cs)는 독립 ECS 컴포넌트가 아닌 unmanaged 포함 값이다. HasConfiguration은 필수 객체의 명시 여부, SupplyRange/ConnectionRange는 단일 셀 반경, MaximumGeneration/MaximumConsumption/CoalEnergyPerItem은 종류·Tier의 능력이다. JSON 역할별 필수 필드와 공개 게시/사전 등록 검증은 [BuildingPowerConfigParser](../../../Assets/Scripts/Config/BuildingPowerConfigParser.cs)가 공유한다. 해금 변경과 달리 전력 능력은 게시 후 불변이다. 기존 속도/용량 호환 버퍼에 전력 원본을 복제하지 않는다.

## 상태·프레임 계약

아래 10개 ECS 타입의 [정의](../../../Assets/Scripts/Components/Power/PowerComponents.cs)를 사용한다. 프레임 데이터는 일반 컴포넌트이며 enableable 요청이나 IRequestComponent가 아니다. 전력 전용 시계/틱 필드를 두지 않고 매 틱 준비에서 먼저 초기화한다. 영속 상태와 틱 내 계획을 구분한다. 실제 초기화 Writer는 아직 후속이며, 타입 삭제만으로 초기화 동작이 구현된 것은 아니다.

| 타입·부착 엔티티 | 원본/파생 및 필드 | 생성·Writer·Reader·소비 경계 |
| --- | --- | --- |
| PowerPole / 전신주·주 시설 | 등록 원본 StableId; 0은 미등록 | 후속 생성 연결이 부착, 토폴로지 소유자가 발급. 등록/해제·망 계산에서 읽는다. 철거 시 원본 참가자를 제거한다. |
| PowerGenerator / 발전 건물 | 발전 참가자 태그 | 8.7 생성 연결이 부착. 준비/정산은 BuildingType 종류·Tier로 능력을 조회한다. |
| PowerConsumer / 생산·연구·충전 건물 | 소비 참가자 태그 | 각 후속 도메인이 부착. 충전의 동적 수요와 정격 설정을 혼동하지 않는다. |
| PowerGrid / 망 엔티티 | 토폴로지 원본 식별 StableId | 8.2~8.3 토폴로지가 생성/변경/제거한다. ID 규칙은 후속 상세 설계다. |
| PowerGridConnection / 참가자 | 파생 PowerPole·Grid 참조 | 토폴로지 준비가 작성, 배분/진단이 읽는다. 생산은 이 내부 연결 대신 공급 결과를 읽는다. 철거/분리 시 다시 계산한다. |
| FuelEnergyState / 연료를 에너지로 전환하는 소비처 | 영속 RemainingEnergy | 연료 종류·소비처에 독립적인 잔량이다. 현재 전력 계약에서는 공급 실행의 연료 전환이 증가시키고 틱 말 정산이 실제 발전분만 차감한다. 동일 연료 도메인의 서로 다른 단계이며 새 입고/정산으로 음수나 중복 가용량을 만들지 않는다. 다른 연료/소비처의 실제 Writer는 별도 후속이다. |
| PowerDemand / 소비자 | 프레임 입력 RequestedEnergy | 소비 도메인이 준비에서 먼저 0으로 초기화하고 전력 외 작업 조건으로 작성한다. 전력 계획이 읽는다. 정전 자체로 수요를 0으로 만들지 않는다. |
| PowerSupplyResult / 소비자 | 프레임 공급 예산 SupplyRatio·AllocatedEnergy | 전력 소유자가 준비에서 먼저 0으로 초기화한다. 연료로 뒷받침된 뒤 한 번 게시하고 생산/충전은 이 예산 안에서만 사용한다. 실제 사용량으로 오해하지 않는다. |
| PowerUsageResult / 소비자 | 프레임 결과 UsedEnergy | 소비 도메인이 준비에서 먼저 0으로 초기화하고 Execution에서 성공분을 작성한다. 정산이 읽는다. 전력이 생산 진행을 대신 변경하지 않는다. |
| PowerGeneratorSupplyPlan / 발전기 | 파생 계획 AvailableEnergy·AllocatedEnergy | 전력 소유자가 준비에서 먼저 0으로 초기화하고 기존 잔량·보관 연료의 가용 에너지로 작성한다. 공급 실행은 StoredItemElement를 재검사하여 필요한 연료를 전환하고 정산은 배정 상한을 읽는다. 새 입고를 추가하지 않는다. |

망별 가용량·수요·배정량·사용량과 발전기별 실제 발전량은 전력 소유자의 일시 집계/정산 계산으로 처리한다. 독립 ECS 상태나 후속 계산의 별도 원본으로 중복 보관하지 않는다. 준비 초기화는 작업 불가·공급 없음·단절된 참가자도 포함하며 모든 초기화가 끝난 뒤 수요/계획을 작성한다. 앞선 틱의 값이 남아 예산으로 사용되지 않게 하는 계약이다.

실제 참가자 생성/철거는 기존 공통 생성과 EndBuilding에 연결하고, 주 시설 자동 시작은 Task 9.22가 수행한다. Baker나 JSON 정의의 존재만으로 위 상태가 부착된다고 가정하지 않는다.

## 실행 경계

- [PowerPreparationGroup](../../../Assets/Scripts/Phases/Power/PowerPreparationGroup.cs): BuildingReservationGroup의 OrderLast 자식. 소유 도메인별 프레임 데이터 초기화 → 소비 도메인 수요 준비 → 토폴로지/연료 가용량 계산 → 배분 계획 순서.
- [PowerSupplyExecutionGroup](../../../Assets/Scripts/Phases/Power/PowerSupplyExecutionGroup.cs): BuildingExecutionGroup의 OrderFirst 자식. 계획 검사·필요 석탄의 잔여 에너지 전환 → 한 번 공급 게시 순서. 일반 생산보다 앞선다.
- [PowerSettlementGroup](../../../Assets/Scripts/Phases/Power/PowerSettlementGroup.cs): SimulationCommitGroup 안에서 EndSimulationEntityCommandBufferSystem보다 먼저 실행. 실제 사용 수집 → 실제 발전 배분 → 연료 에너지 정산 순서.

현재 세 그룹은 비어 있다. 후속 Writer는 내부 순서와 Job 의존성을 함께 선언한다. 석탄 실물은 앞선 공급 실행에서 기존 보관 버퍼 선행 제거/Destroy 요청으로 소비하고 ItemLifecycle/EndBuilding이 삭제한다. 정산은 잔여 에너지만 갱신한다. 동일 그룹 순서만으로 공간 NativeContainer의 Fence나 Job 완료가 확보되지는 않는다.

## 순수 범위 조회

[PowerRangeUtility](../../../Assets/Scripts/Common/PowerRangeUtility.cs)는 캐시/연결 상태를 소유하지 않는다. Contains는 중심 ± 단일 range의 경계 포함 정사각형, AreCentersConnected는 한쪽 범위에 상대 중심이 포함되는 OR 판정이다. 큰 int 좌표의 차이는 long으로 계산하여 overflow로 잘못 연결하지 않는다. 이것은 실제 망 생성/분리나 전체 footprint 공급 선택의 구현이 아니다.
