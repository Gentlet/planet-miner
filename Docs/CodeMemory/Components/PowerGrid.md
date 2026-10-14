# 전력 컴포넌트 계약

2026-10-14 Task 8.1~8.5를 구현했다. Decision은 선택/실제 Miner·Crafter 수요/가용량, Reservation은 토폴로지/예산, Execution은 공급 결과, StateApply/EndBuilding은 망 수명주기·공개 연결을 소유한다. 실제 연료 전환/연소·생산 사용·연구/충전은 후속이다. 규칙은 [전력 명세](../../Specifications/PowerGrid.md)를 따른다. 이번 Unity 컴파일·DLL 최신성과 선택 EditMode 76/76(전력 38·공통 생성 11·완공 8·철거 19)을 확인했으며 과거 실행과 합산하지 않는다. Play Mode·베이킹·Player 빌드·성능은 미수행이다.

## 설정 원본

`BuildingConfigElement.Power`의 [BuildingPowerConfig](../../../Assets/Scripts/Components/Power/BuildingPowerConfig.cs)는 독립 ECS 컴포넌트가 아닌 unmanaged 포함 값이다. HasConfiguration은 필수 객체의 명시 여부, SupplyRange/ConnectionRange는 단일 셀 반경, MaximumGeneration/MaximumConsumption/CoalEnergyPerItem은 종류·Tier의 능력이다. JSON 역할별 필수 필드와 공개 게시/사전 등록 검증은 [BuildingPowerConfigParser](../../../Assets/Scripts/Config/BuildingPowerConfigParser.cs)가 공유한다. 해금 변경과 달리 전력 능력은 게시 후 불변이다. 기존 속도/용량 호환 버퍼에 전력 원본을 복제하지 않는다.

## 상태·프레임 계약

아래 11개 ECS 타입의 [정의](../../../Assets/Scripts/Components/Power/PowerComponents.cs)를 사용한다. 8.5에서 PowerSupplyReservation을 추가하고 PowerDemand/PowerGeneratorSupplyPlan을 enableable 준비 상태로 변경했다. 활성은 준비 완료이며 수요/가용량 0도 정상 준비 완료다. 세 enableable 계획은 IRequestComponent가 아니며 매 틱 Decision이 초기화한다. 영속 FuelEnergyState와 틱 내 계획/게시 결과를 구분하고 별도 시계/Tick 필드를 두지 않는다.

| 타입·부착 엔티티 | 원본/파생 및 필드 | 생성·Writer·Reader·소비 경계 |
| --- | --- | --- |
| PowerPole / 전신주·주 시설 | 참가 태그 | 공통 BuildingLifecycleUtility가 직접 Spawn/현장 완공에서 부착한다. StableId/번호 발급 없이 Entity와 기존 PlacementStamp를 사용한다. |
| PowerGenerator / MainFacility·CoalGenerator | 발전 참가자 태그 | 8.5 공통 직접 생성/완공이 부착한다. 능력은 자신의 BuildingType 종류·Tier로 조회하며 전신주/주 시설 설정을 대신 조회하지 않는다. |
| PowerConsumer / MainFacility·Miner·Crafter·ResearchBuilding·DroneStation | 소비 참가자 태그 | 8.5 공통 생성/완공이 부착한다. 실제 Miner/Crafter 수요만 연결했고 연구/충전은 후속까지 0이다. 동적 충전 수요를 임의 정격량으로 대체하지 않는다. |
| PowerGrid / 망 엔티티 | 망 태그 | Apply의 Burst Job이 생성/병합/분리와 미사용/빈 망 삭제를 EndBuilding에 기록한다. 기존 망 전체를 조회하여 마지막 전신주 소실 뒤에도 회수한다. 전신주와 별도 Entity이며 번호 발급이 없다. |
| PowerGridConnection / 참가자 | 공개 PowerPole·Grid 참조 | 공통 생성은 전신주/주 시설을 자기 전신주·Null 망으로, 일반 참가자는 두 참조 Null로 초기화한다. Apply는 이번 틱 선택한 전신주의 새 망으로 연결하거나 범위 밖이면 해제한다. Disabled는 기존 참조만 정리하고 Prefab은 제외한다. 전신주 자체의 자기 참조/설치 기록은 유지한다. |
| FuelEnergyState / 연료를 에너지로 전환하는 소비처 | 영속 RemainingEnergy | 공통 생성은 CoalGenerator에 0으로 부착한다. 8.5 가용량/게시가 이미 전환된 잔량만 읽고 변경하지 않는다. 실물 전환과 실제 사용 에너지 차감은 8.6 후속이며 같은 틱 입고는 그 틱 공급에 포함하지 않는다. |
| PowerDemand / 소비자 | enableable 프레임 입력 RequestedEnergy | BuildingPowerDemandDecisionSystem이 Disabled/철거까지 값/활성을 초기화한다. 활성 소비자의 MinerDecision.CanMine, CrafterDecision.CanStartCraft/CanAdvance와 현재 정격 × 공통 DeltaTime으로 요청하고 준비 완료를 활성화한다. 연구/충전은 0이다. Budget/게시가 읽으며 공급 부족 자체로 수요를 지우지 않는다. |
| PowerSupplyResult / 소비자 | 공개 프레임 예산 SupplyRatio·AllocatedEnergy | PowerSupplyDecisionSystem이 모두 0으로 초기화하고 PowerSupplyExecutionSystem이 연결/수요/연료·총량 최종 검사 후 한 번 게시한다. 실패 시 이전 공급을 재사용하지 않는다. 실제 사용량과 미게시 예약을 구분한다. 생산/충전 Reader는 8.7/9.14/10A.3이다. |
| PowerUsageResult / 소비자 | 프레임 결과 UsedEnergy | BuildingPowerDemandDecisionSystem이 0으로 초기화한다. 실제 성공량 기록은 후속 각 소비 도메인이 소유한다. 배정량을 실제 사용량으로 복사하지 않으며 연료 정산 Writer는 8.6이다. |
| PowerGeneratorSupplyPlan / 발전기 | enableable AvailableEnergy·AllocatedEnergy·RepresentativePole | PowerSupplyDecisionSystem이 값/활성을 초기화하고 기초 정격/이미 전환된 잔량의 가용량을 활성화한다. Budget가 정격 기준 동일 가동률로 예산과 기준 대표를 작성하고 게시가 생존/연료 한도를 대조한다. 실제 발전량이나 공개 Grid Entity를 담지 않는다. 미전환 석탄·연료 차감은 8.6이다. |
| PowerSupplyReservation / 소비자 | enableable 미게시 RequestedEnergy·SupplyRatio·AllocatedEnergy·RepresentativePole | PowerSupplyDecisionSystem이 초기화하고 Budget가 기준 수요/대표와 배정량을 작성한 뒤 활성화한다. 게시가 기준 변화와 총량을 대조해 Result로 옮긴다. 연결/점유 스냅샷이나 영속 망 집계가 아니며 준비 실패를 정상 0으로 게시하지 않는다. |

망별 가용량·수요·배정량·사용량과 발전기별 실제 발전량은 전력 소유자의 일시 집계/정산 계산으로 처리한다. 독립 ECS 상태나 후속 계산의 별도 원본으로 중복 보관하지 않는다. 준비 초기화는 작업 불가·공급 없음·단절된 참가자도 포함하며 모든 초기화가 끝난 뒤 수요/계획을 작성한다. 앞선 틱의 값이 남아 예산으로 사용되지 않게 하는 계약이다.

공통 생성/완공은 위 역할과 필수 상태를 부착하며 준비 계획은 비활성·수량은 0으로 시작한다. 새 참가자는 EndBuilding 이후 다음 틱부터 공급에 참여한다. 기존 실제 철거/재선택 경로를 사용하고 승인 철거는 이번 틱부터 수요/발전/배정에서 제외한다. 실제 연료/생산/연구/충전과 주 시설 자동 시작은 각각 후속이다.

추가 계약 4개:

| 타입 | 생성·Writer·Reader·수명 |
| --- | --- |
| PowerGridTopologyDecision | 공통 생성이 부착한다. Reservation이 매 틱 비활성/철거 대상을 포함해 초기화하고 RepresentativePole·ExpectedGrid·RetainedGrid를 Burst Job으로 작성한다. 대표/유지 망은 준비 계획, ExpectedGrid는 최종 검사할 이전 공개 값의 스냅샷이다. 유지 망 Null은 신규 생성 계획이다. 후속 공급과 Apply가 읽으며 ECB 예약 Entity를 담지 않는다. |
| PowerGridConnectionDecision | 일반 참가자의 공통 생성이 비활성으로 부착한다. PowerGridConnectionDecisionSystem이 Decision에서 모든 런타임 값/활성을 초기화하고 활성 참가자의 선택 PowerPole을 병렬 Job으로 작성한다. enable은 선택 준비 완료, 활성 Null 값은 정상 범위 밖이다. Apply와 후속 공급 계획이 읽으며 예약 망 Entity는 포함하지 않는다. 건물 삭제 시 함께 제거된다. IRequestComponent가 아니다. |
| PowerGridConnectionIndex | ConnectionSync.OnCreate가 Persistent Entity→공개 연결 맵을 singleton에 생성한다. Synchronization은 Query의 공개 값만 Clear/재등록한다. 미연결 값은 제외하며 망 Lookup 검증·위치 복제는 하지 않는다. 종료 시 Fence 완료 후 Dispose한다. 실제 제품 Reader는 후속이다. |
| PowerGridConnectionIndexFence | Index와 같은 singleton에 붙인다. 기존 IFenceComponent 관례로 Reader/Writer를 등록하며 재할당·직접 쓰기·Dispose 전에 Complete한다. |

## 실행 경계

전력 전용 그룹은 없다. Reservation은 BuildingReservationGroup의 OrderLast에서 생존 원본을 검증하고 Burst IJob으로 연결 집합과 기존 망별 유지권을 정한 뒤 집합별 병합 계획을 작성한다. 1A는 가장 이른 남은 활성 멤버의 집합만 기존 망을 유지하고, 2A는 유지권 확정 후 병합한다. 계산/입력 해제 핸들을 state.Dependency에 등록한다. 정상 망 변경의 ECB Producer는 없으며 입력 오류 Fatal만 EndBuilding에 기록한다.

앞선 BuildingDecisionGroup의 PowerGridConnectionDecisionSystem은 활성 일반 참가자의 GridPosition·기본 Footprint·Direction과 전신주의 종류/Tier·위치·설정을 독립적으로 검증한다. 회전 직사각형 전체의 최소 셀 거리·PlacementStamp.Compare로 매 틱 전신주를 선택한다. Disabled·Prefab·승인 철거는 새 선택에서 제외한다. 전신주 Entity 배열과 읽기 전용 Config/Position/Type/Stamp Lookup을 IJobEntity가 읽으며 결정만 병렬로 작성한다. 토폴로지 Lookup과 준비 완료 의존성은 없다. 계산/배열 해제 핸들을 state.Dependency에 등록하고 별도 공간/연결 스냅샷이나 Fence를 만들지 않는다. 선택 준비 완료와 후속 토폴로지 준비 성공은 별개이며 Apply가 둘 다 확인한다.

Apply는 BuildingStateApplyGroup에서 BuildingLifecycleApplySystem보다 먼저 실행한다. 활성 전신주의 토폴로지와 활성 일반 참가자의 선택을 모두 최종 검사하며 준비 누락/Reservation 실패를 빈 망으로 처리하지 않는다. 선택 전신주의 현재 생존·활성·준비와 footprint 공급 경계도 확인한다. 분리하여 새 망이 필요한 집합은 대표의 ExpectedGrid가 기존 망이고 RetainedGrid가 Null일 수 있다. 대표/집합/공급 전신주는 재선택하지 않는다.

정상 검사 뒤 Burst IJob은 대표별 망 Entity를 준비하고 모든 런타임 연결을 Disabled까지 갱신한 뒤 기존 망 중 유지되지 않는 망을 삭제한다. 참가자 Entity 목록과 기존 연결/전신주/선택/비활성/철거의 읽기 전용 ComponentLookup을 사용하며 별도 연결 스냅샷 구조체/값 복사 배열은 없다. 비활성 전신주는 자기 참조/Null 망으로 정리한다. 활성 일반 참가자는 이번 틱 선택을 적용하며, 비활성 참가자 또는 선택 결정이 없는 기존 참조는 유효 전신주의 새 망으로 맞추거나 무효하면 둘 다 해제한다. 참가자·기존 망 Entity 목록과 적용 계획의 해제 핸들을 state.Dependency와 EndBuilding Producer에 등록하고 Lookup의 ECS 읽기 의존성도 연결한다. 예약 Entity는 같은 ECB 안에서만 참조하며 실제 공개는 EndBuilding이다.

수요/가용량과 초기화는 BuildingDecision, 배정은 TopologyReservation 뒤 OrderLast, 게시는 BuildingExecution OrderFirst이며 생산 앞이다. Budget/게시의 동기 검사는 해당 계획/결정 Writer를 CompleteDependencyBeforeRO로 기다린다. Job은 Entity 목록·읽기 전용 Lookup과 state.Dependency를 사용하고 입력 해제 핸들을 등록한다. 망별 합계는 Budget Job 내부의 임시 double Native 맵이며 float 에너지를 아래로 반올림해 소비자 합계가 발전기 배정을 넘지 않게 한다. 미세한 미배정량도 같은 틱에 재배분하지 않는다. Role별 원본 검증과 준비 성공 여부는 별개이며 최종 손상은 Fatal/공급 0으로 처리한다.

8.5는 기초 발전과 이미 전환된 FuelEnergyState만 게시 근거로 사용한다. 미전환 Coal을 세지 않으며 연료 전환/차감이나 생산 진행·성공량을 변경하지 않는다. 연구/충전 검증의 요청 입력은 fixture가 직접 제공하므로 실제 Producer 구현 증거가 아니다. 연결 인덱스는 이번 틱 계획을 대체하지 않고 예산은 기존 선택/Topology의 대표를 사용해 EndBuilding 전 새 망·병합·분리를 반영한다. 다른 ECB의 예약 Entity나 영속 집계/위치·연결 캐시는 없다.

## 순수 범위 조회

[PowerRangeUtility](../../../Assets/Scripts/Common/PowerRangeUtility.cs)는 캐시/연결 상태를 소유하지 않는다. Contains는 경계 포함 정사각형, AreCentersConnected는 연결 범위 OR 판정이다. TryGetFootprintDistanceSquared는 회전된 직사각형 전체의 가장 가까운 점유 셀로 공급 가능 여부와 최소 제곱거리를 구하며 선택/최종 검사가 공유한다. 큰 int 좌표 차이와 제곱거리는 long으로 계산한다. 실제 선택·망 수명주기·공개는 각 시스템이 소유한다.
