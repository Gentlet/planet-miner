# 전력 컴포넌트 계약

2026-10-13 Task 8.1~8.4를 구현하고 2026-10-14 공급 전신주 선택을 Decision으로 이관했다. Decision은 일반 참가자의 공급 전신주를 선택하고 Reservation은 토폴로지를 준비한다. StateApply는 최종 검사·망 수명주기·연결 공개·참조/빈 망 정리를 소유한다. 실제 전력 배분/연료/생산/충전은 후속이다. 규칙은 [전력 명세](../../Specifications/PowerGrid.md)를 따른다. 이번 Unity 컴파일·DLL 최신성과 선택 EditMode 41/41(토폴로지/연결 22·기존 철거 19)를 확인했으며 이전 59사례 실행과 합산하지 않는다. Play Mode·베이킹·Player 빌드·성능은 미수행이다.

## 설정 원본

`BuildingConfigElement.Power`의 [BuildingPowerConfig](../../../Assets/Scripts/Components/Power/BuildingPowerConfig.cs)는 독립 ECS 컴포넌트가 아닌 unmanaged 포함 값이다. HasConfiguration은 필수 객체의 명시 여부, SupplyRange/ConnectionRange는 단일 셀 반경, MaximumGeneration/MaximumConsumption/CoalEnergyPerItem은 종류·Tier의 능력이다. JSON 역할별 필수 필드와 공개 게시/사전 등록 검증은 [BuildingPowerConfigParser](../../../Assets/Scripts/Config/BuildingPowerConfigParser.cs)가 공유한다. 해금 변경과 달리 전력 능력은 게시 후 불변이다. 기존 속도/용량 호환 버퍼에 전력 원본을 복제하지 않는다.

## 상태·프레임 계약

아래 10개 ECS 타입의 [정의](../../../Assets/Scripts/Components/Power/PowerComponents.cs)를 사용한다. 프레임 데이터는 일반 컴포넌트이며 enableable 요청이나 IRequestComponent가 아니다. 전력 전용 시계/틱 필드를 두지 않고 매 틱 준비에서 먼저 초기화한다. 영속 상태와 틱 내 계획을 구분한다. 실제 초기화 Writer는 아직 후속이며, 타입 삭제만으로 초기화 동작이 구현된 것은 아니다.

| 타입·부착 엔티티 | 원본/파생 및 필드 | 생성·Writer·Reader·소비 경계 |
| --- | --- | --- |
| PowerPole / 전신주·주 시설 | 참가 태그 | 공통 BuildingLifecycleUtility가 직접 Spawn/현장 완공에서 부착한다. StableId/번호 발급 없이 Entity와 기존 PlacementStamp를 사용한다. |
| PowerGenerator / 발전 건물 | 발전 참가자 태그 | 8.7 생성 연결이 부착. 준비/정산은 BuildingType 종류·Tier로 능력을 조회한다. |
| PowerConsumer / 생산·연구·충전 건물 | 소비 참가자 태그 | 각 후속 도메인이 부착. 충전의 동적 수요와 정격 설정을 혼동하지 않는다. |
| PowerGrid / 망 엔티티 | 망 태그 | Apply의 Burst Job이 생성/병합/분리와 미사용/빈 망 삭제를 EndBuilding에 기록한다. 기존 망 전체를 조회하여 마지막 전신주 소실 뒤에도 회수한다. 전신주와 별도 Entity이며 번호 발급이 없다. |
| PowerGridConnection / 참가자 | 공개 PowerPole·Grid 참조 | 공통 생성은 전신주/주 시설을 자기 전신주·Null 망으로, 일반 참가자는 두 참조 Null로 초기화한다. Apply는 이번 틱 선택한 전신주의 새 망으로 연결하거나 범위 밖이면 해제한다. Disabled는 기존 참조만 정리하고 Prefab은 제외한다. 전신주 자체의 자기 참조/설치 기록은 유지한다. |
| FuelEnergyState / 연료를 에너지로 전환하는 소비처 | 영속 RemainingEnergy | 연료 종류·소비처에 독립적인 잔량이다. 현재 전력 계약에서는 공급 실행의 연료 전환이 증가시키고 틱 말 정산이 실제 발전분만 차감한다. 동일 연료 도메인의 서로 다른 단계이며 새 입고/정산으로 음수나 중복 가용량을 만들지 않는다. 다른 연료/소비처의 실제 Writer는 별도 후속이다. |
| PowerDemand / 소비자 | 프레임 입력 RequestedEnergy | 소비 도메인이 준비에서 먼저 0으로 초기화하고 전력 외 작업 조건으로 작성한다. 전력 계획이 읽는다. 정전 자체로 수요를 0으로 만들지 않는다. |
| PowerSupplyResult / 소비자 | 프레임 공급 예산 SupplyRatio·AllocatedEnergy | 전력 소유자가 준비에서 먼저 0으로 초기화한다. 연료로 뒷받침된 뒤 한 번 게시하고 생산/충전은 이 예산 안에서만 사용한다. 실제 사용량으로 오해하지 않는다. |
| PowerUsageResult / 소비자 | 프레임 결과 UsedEnergy | 소비 도메인이 준비에서 먼저 0으로 초기화하고 Execution에서 성공분을 작성한다. 정산이 읽는다. 전력이 생산 진행을 대신 변경하지 않는다. |
| PowerGeneratorSupplyPlan / 발전기 | 파생 계획 AvailableEnergy·AllocatedEnergy | 전력 소유자가 준비에서 먼저 0으로 초기화하고 기존 잔량·보관 연료의 가용 에너지로 작성한다. 공급 실행은 StoredItemElement를 재검사하여 필요한 연료를 전환하고 정산은 배정 상한을 읽는다. 새 입고를 추가하지 않는다. |

망별 가용량·수요·배정량·사용량과 발전기별 실제 발전량은 전력 소유자의 일시 집계/정산 계산으로 처리한다. 독립 ECS 상태나 후속 계산의 별도 원본으로 중복 보관하지 않는다. 준비 초기화는 작업 불가·공급 없음·단절된 참가자도 포함하며 모든 초기화가 끝난 뒤 수요/계획을 작성한다. 앞선 틱의 값이 남아 예산으로 사용되지 않게 하는 계약이다.

전신주/주 시설과 Miner·Crafter·ResearchBuilding·CoalGenerator·DroneStation의 연결/선택 데이터를 공통 생성과 EndBuilding에 연결했다. 기존 철거 경로의 참조 정리와 다른 공급 전신주 재선택도 구현했다. 발전/소비 역할·연료·수요/공급 상태와 실제 생산 연결은 해당 후속 Task, 주 시설 자동 시작은 9.22다.

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

공급 전 수요/가용량·공급 게시·실제 사용 정산 Writer는 후속이다. 연결 인덱스는 이번 틱 계획을 대체하지 않으며 이전 Synchronization 자료를 같은 틱 생성/철거 판단의 원본으로 쓰지 않는다. 실제 철거는 기존 Command 승인/EndCommand와 BuildingLifecycleApply/EndBuilding 경로를 사용한다. N EndBuilding 생성/완공은 N+1부터 선택하고 N Command 철거 승인은 N 선택에서 제외한다.

## 순수 범위 조회

[PowerRangeUtility](../../../Assets/Scripts/Common/PowerRangeUtility.cs)는 캐시/연결 상태를 소유하지 않는다. Contains는 경계 포함 정사각형, AreCentersConnected는 연결 범위 OR 판정이다. TryGetFootprintDistanceSquared는 회전된 직사각형 전체의 가장 가까운 점유 셀로 공급 가능 여부와 최소 제곱거리를 구하며 선택/최종 검사가 공유한다. 큰 int 좌표 차이와 제곱거리는 long으로 계산한다. 실제 선택·망 수명주기·공개는 각 시스템이 소유한다.
