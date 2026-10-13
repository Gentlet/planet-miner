# 전력 컴포넌트 계약

2026-10-13 롤백 뒤 Task 8.1~8.2를 구현하고 준비와 적용 책임을 분리했다. Reservation은 연결/대표/유지 망 계획을, StateApply는 최종 검사와 망 생성/병합 ECB 기록을 소유한다. 설정 게시·공통 전신주 부착·공개 연결 인덱스도 유지한다. 공급/연료/생산/충전과 제거/분리는 후속이다. 규칙은 [전력 명세](../../Specifications/PowerGrid.md)를 따른다. 분리 후 Unity 컴파일·DLL 최신성과 선택 EditMode 25/25(전력 토폴로지 6·공통 생성 11·공사 완공 8)를 확인했다. 앞선 50사례는 분리 전 실행이며 이번 수치와 합산하지 않는다. Play Mode·베이킹·Player 빌드·성능은 미수행이다.

## 설정 원본

`BuildingConfigElement.Power`의 [BuildingPowerConfig](../../../Assets/Scripts/Components/Power/BuildingPowerConfig.cs)는 독립 ECS 컴포넌트가 아닌 unmanaged 포함 값이다. HasConfiguration은 필수 객체의 명시 여부, SupplyRange/ConnectionRange는 단일 셀 반경, MaximumGeneration/MaximumConsumption/CoalEnergyPerItem은 종류·Tier의 능력이다. JSON 역할별 필수 필드와 공개 게시/사전 등록 검증은 [BuildingPowerConfigParser](../../../Assets/Scripts/Config/BuildingPowerConfigParser.cs)가 공유한다. 해금 변경과 달리 전력 능력은 게시 후 불변이다. 기존 속도/용량 호환 버퍼에 전력 원본을 복제하지 않는다.

## 상태·프레임 계약

아래 10개 ECS 타입의 [정의](../../../Assets/Scripts/Components/Power/PowerComponents.cs)를 사용한다. 프레임 데이터는 일반 컴포넌트이며 enableable 요청이나 IRequestComponent가 아니다. 전력 전용 시계/틱 필드를 두지 않고 매 틱 준비에서 먼저 초기화한다. 영속 상태와 틱 내 계획을 구분한다. 실제 초기화 Writer는 아직 후속이며, 타입 삭제만으로 초기화 동작이 구현된 것은 아니다.

| 타입·부착 엔티티 | 원본/파생 및 필드 | 생성·Writer·Reader·소비 경계 |
| --- | --- | --- |
| PowerPole / 전신주·주 시설 | 참가 태그 | 공통 BuildingLifecycleUtility가 직접 Spawn/현장 완공에서 부착한다. StableId/번호 발급 없이 Entity와 기존 PlacementStamp를 사용한다. |
| PowerGenerator / 발전 건물 | 발전 참가자 태그 | 8.7 생성 연결이 부착. 준비/정산은 BuildingType 종류·Tier로 능력을 조회한다. |
| PowerConsumer / 생산·연구·충전 건물 | 소비 참가자 태그 | 각 후속 도메인이 부착. 충전의 동적 수요와 정격 설정을 혼동하지 않는다. |
| PowerGrid / 망 엔티티 | 망 태그 | PowerGridTopologyApplySystem의 Burst Job이 준비 계획에 따른 생성/병합을 EndBuilding에 기록한다. 전신주와 별도 Entity이며 번호 발급이 없다. 제거/분리는 8.3 후속이다. |
| PowerGridConnection / 참가자 | 공개 PowerPole·Grid 참조 | 공통 생성은 자기 전신주/Null 망으로 초기화한다. Reservation이 입력을 검증하고 Apply가 계획과 현재 값을 대조한 뒤 변경을 EndBuilding에 기록한다. Sync/후속 진단이 공개 값을 읽는다. 생산은 공급 결과를 읽는다. |
| FuelEnergyState / 연료를 에너지로 전환하는 소비처 | 영속 RemainingEnergy | 연료 종류·소비처에 독립적인 잔량이다. 현재 전력 계약에서는 공급 실행의 연료 전환이 증가시키고 틱 말 정산이 실제 발전분만 차감한다. 동일 연료 도메인의 서로 다른 단계이며 새 입고/정산으로 음수나 중복 가용량을 만들지 않는다. 다른 연료/소비처의 실제 Writer는 별도 후속이다. |
| PowerDemand / 소비자 | 프레임 입력 RequestedEnergy | 소비 도메인이 준비에서 먼저 0으로 초기화하고 전력 외 작업 조건으로 작성한다. 전력 계획이 읽는다. 정전 자체로 수요를 0으로 만들지 않는다. |
| PowerSupplyResult / 소비자 | 프레임 공급 예산 SupplyRatio·AllocatedEnergy | 전력 소유자가 준비에서 먼저 0으로 초기화한다. 연료로 뒷받침된 뒤 한 번 게시하고 생산/충전은 이 예산 안에서만 사용한다. 실제 사용량으로 오해하지 않는다. |
| PowerUsageResult / 소비자 | 프레임 결과 UsedEnergy | 소비 도메인이 준비에서 먼저 0으로 초기화하고 Execution에서 성공분을 작성한다. 정산이 읽는다. 전력이 생산 진행을 대신 변경하지 않는다. |
| PowerGeneratorSupplyPlan / 발전기 | 파생 계획 AvailableEnergy·AllocatedEnergy | 전력 소유자가 준비에서 먼저 0으로 초기화하고 기존 잔량·보관 연료의 가용 에너지로 작성한다. 공급 실행은 StoredItemElement를 재검사하여 필요한 연료를 전환하고 정산은 배정 상한을 읽는다. 새 입고를 추가하지 않는다. |

망별 가용량·수요·배정량·사용량과 발전기별 실제 발전량은 전력 소유자의 일시 집계/정산 계산으로 처리한다. 독립 ECS 상태나 후속 계산의 별도 원본으로 중복 보관하지 않는다. 준비 초기화는 작업 불가·공급 없음·단절된 참가자도 포함하며 모든 초기화가 끝난 뒤 수요/계획을 작성한다. 앞선 틱의 값이 남아 예산으로 사용되지 않게 하는 계약이다.

전신주/주 시설의 전신주 상태만 공통 생성과 EndBuilding에 연결했다. 발전기/소비자 부착·철거 정리는 8.7, 주 시설 자동 시작은 9.22가 수행한다.

추가 계약 3개:

| 타입 | 생성·Writer·Reader·수명 |
| --- | --- |
| PowerGridTopologyDecision | 공통 생성이 부착한다. Reservation이 매 틱 비활성/철거 대상을 포함해 초기화하고 RepresentativePole·ExpectedGrid·RetainedGrid를 Burst Job으로 작성한다. 대표/유지 망은 준비 계획, ExpectedGrid는 최종 검사할 이전 공개 값의 스냅샷이다. 유지 망 Null은 신규 생성 계획이다. 후속 공급과 Apply가 읽으며 ECB 예약 Entity를 담지 않는다. |
| PowerGridConnectionIndex | ConnectionSync.OnCreate가 Persistent Entity→공개 연결 맵을 singleton에 생성한다. Synchronization은 Query의 공개 값만 Clear/재등록한다. 미연결 값은 제외하며 망 Lookup 검증·위치 복제는 하지 않는다. 종료 시 Fence 완료 후 Dispose한다. 실제 제품 Reader는 후속이다. |
| PowerGridConnectionIndexFence | Index와 같은 singleton에 붙인다. 기존 IFenceComponent 관례로 Reader/Writer를 등록하며 재할당·직접 쓰기·Dispose 전에 Complete한다. |

## 실행 경계

전력 전용 그룹 정의는 삭제 상태를 유지한다. PowerGridTopologyReservationSystem은 BuildingReservationGroup의 OrderLast다. 생존 ECS·종류/Tier 설정·설치 기록·공개 망을 메인 스레드에서 검사하고 Burst IJob으로 연결 집합·대표·유지 망 계획만 작성한다. 계산/입력 해제 핸들을 state.Dependency에 등록한다. 정상 망 변경의 ECB Producer는 없으며 입력 오류 Fatal만 기존 EndBuilding 경계에 기록한다.

PowerGridTopologyApplySystem은 BuildingStateApplyGroup에서 BuildingLifecycleApplySystem보다 먼저 실행한다. 준비된 대표의 생존·활성·소속 계획과 참가자의 현재 공개 망을 최종 검사하며 연결 집합/대표를 재선택하지 않는다. 정상 입력을 모두 검사한 뒤 Burst IJob으로 망 생성·연결 변경·병합으로 불필요해진 망 삭제를 같은 EndBuilding ECB에 기록한다. 기록/입력 해제 핸들을 state.Dependency와 EndBuilding Producer에 등록한다. 검사 실패는 Fatal만 기록하며 정상 망 변경을 시작하지 않는다. 실제 공개는 EndBuilding이고 N틱 생성자는 N+1부터 참가한다.

공급 전 수요/가용량·공급 게시·실제 사용 정산 Writer는 후속이다. 연결 인덱스는 이번 틱 계획을 대체하지 않으며 이전 Synchronization 자료를 같은 틱 생성/철거 판단의 원본으로 쓰지 않는다. 분리·빈 망·철거/일반 참가자 참조 정리는 8.3~8.4/8.7에 남아 있다.

## 순수 범위 조회

[PowerRangeUtility](../../../Assets/Scripts/Common/PowerRangeUtility.cs)는 캐시/연결 상태를 소유하지 않는다. Contains는 중심 ± 단일 range의 경계 포함 정사각형, AreCentersConnected는 한쪽 범위에 상대 중심이 포함되는 OR 판정이다. 큰 int 좌표의 차이는 long으로 계산하여 overflow로 잘못 연결하지 않는다. 이것은 실제 망 생성/분리나 전체 footprint 공급 선택의 구현이 아니다.
