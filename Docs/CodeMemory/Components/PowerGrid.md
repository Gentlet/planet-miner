# 전력 컴포넌트 계약

2026-10-15 준비/결정 분리·공통 Fatal과 실제 Writer 기준의 검사 범위를 반영한다. 현재 Writer/공개 경계와 검증 수치는 [전력 명세](../../Specifications/PowerGrid.md#writer와-검사-경계-2026-10-15)를 따른다. 이전 실행 수치는 당시 변경의 별도 기록이다.

## 설정 원본

BuildingConfigElement.Power/BuildingPowerConfig는 종류·Tier별 불변 능력이다. BuildingPowerConfigParser가 수치/역할 검증을 공유한다. 정격은 에너지/초, CoalEnergyPerItem·Requested/Allocated/Remaining은 에너지다. 공통 World DeltaTime 상한을 사용하고 시계/Tick 필드는 없다.

## 상태·프레임 계약

PowerComponents.cs의 10개 타입을 사용한다. PowerConsumer/PowerGenerator의 enable은 양수 요청/양수 가용량, PowerDemand/PowerGeneratorSupplyPlan/PowerSupplyReservation의 enable은 준비 완료다. 준비 완료 0과 비활성 역할, 준비 실패를 구분한다. IRequestComponent는 아니다.

| 타입 | 생성/Writer/Reader와 공개 |
| --- | --- |
| PowerPole | 공통 생성이 전신주/주 시설에 부착한다. Entity·PlacementStamp로 식별하고 설치 순서를 판단한다. |
| PowerGenerator | 공통 생성은 비활성으로 부착한다. PowerSupplyDecision이 가용량에 따라 활성화하며 집계가 읽는다. |
| PowerConsumer | 공통 생성은 비활성으로 부착한다. Preparation의 수요 Writer가 활성화하며 전력이 부족해도 양수 요청 의도는 유지한다. |
| PowerGrid | Apply가 생성/병합/분리·빈 망 삭제를 EndPower에 기록한다. 전신주와 별도 Entity다. |
| PowerGridConnection | 공통 생성은 미연결 상태를 부착하고 전신주/주 시설의 자기 참조를 유지한다. Apply가 모든 런타임 참조를 정리하여 EndPower에 공개한다. |
| FuelEnergyState | CoalGenerator에 0으로 생성한다. 가용량이 읽고 PowerSupplyExecution만 확정 소비 후 RemainingEnergy를 기록한다. |
| PowerDemand | Preparation은 Disabled·철거까지 초기화하고 Miner/Crafter 작업 의도로 RequestedEnergy를 작성한다. Budget과 Building의 공급 반영이 읽는다. 연구/충전 Producer는 후속이다. |
| PowerSupplyResult | 틱 시작에 0으로 초기화한다. PowerSupplyExecution이 한 번 게시하고 EndPower가 Writer를 완료한다. AllocatedEnergy는 확정 소비이며 Miner/Crafter와 후속 연구/충전이 읽는다. |
| PowerGeneratorSupplyPlan | Decision은 AvailableEnergy를, Reservation은 AllocatedEnergy·ExpectedFuelEnergy·FuelItemCount·RemainingEnergyAfterConsumption을 작성한다. Execution이 기준/한도를 최종 검사하고 소비한다. |
| PowerSupplyReservation | Reservation이 공급률/배정량을 준비하고 Execution이 준비된 값을 공개한다. 대조 전용 수요·대표 복사는 없다. 원본 캐시가 아니다. |

PowerUsageResult는 제거했다. 소비 이후 연료 정산이나 미사용 예산 환급은 없다. CoalGenerator 공통 생성은 설정 슬롯 수·StoredItemElement·Coal Whitelist를 부착한다. 새 입고는 다음 틱 연료이며 EndPower에서 전환으로 비워진 공간은 이번 틱 입고에 사용한다.

## 연결 결정·공유 인덱스

PowerGridTopologyDecision은 Reservation이 연결 집합·기존 망 유지권·대표를 준비하고 최종 Apply가 읽는다. RepresentativePole은 이번 틱 대표, RetainedGrid는 유지권이다. 대조 전용 ExpectedGrid는 제거했다. 예약 ECB Entity를 전달하지 않는다.

PowerGridConnectionDecision은 PowerDecision이 전체 회전 footprint 최소 셀 거리·PlacementStamp.Compare로 매 틱 선택한다. 활성 Null은 정상 범위 밖이다. 토폴로지 준비와 독립적이며 Reservation이 대표 참조를 확인한 뒤 Apply가 준비된 선택을 사용한다. 비활성 참가자는 재선택하지 않고 기존 참조만 정리한다.

PowerGridConnectionIndex/IFenceComponent인 Fence는 PowerGridConnectionSyncSystem이 같은 singleton에 생성/갱신/해제한다. 마지막 Synchronization에서 공개 연결을 Clear/재등록하고 기존 Reader/Writer·재할당·Dispose 의존성을 유지한다. 위치·점유·미공개 예산은 저장하지 않는다.

## 실행 경계와 소스

Preparation → Power(Decision → Reservation → Execution → StateApply → EndPower) → Building → EndBuilding → Drone → EndSimulation → Synchronization 순서다. 이번 틱 선택/대표로 배분하고 EndPower에서 공개한다. 수요·가용량·예산·최종 검사와 배열 해제는 ECS Job 의존성을 등록한다. Entity 목록 수집·원본/계획 검증·배분·소비·게시·해제를 Job 의존성으로 연결한다. 중간 EntityManager 조회/Complete 호출 대신 EndPower에서 전체 체인과 ECB Producer를 기다린다.

Systems/Power/{Decision,Reservation,Execution,StateApply}가 전력 책임을, Systems/Buildings/Preparation이 작업/수요 준비를 소유한다. PowerFuelUtility는 소유/중복/연료 한도와 정수 전환을 공유하고 ItemLifecycleUtility.RecordDestroy는 실물 삭제의 공통 기록 경로다. 실제 소비할 연료 조건을 전체 발전기에 대해 확인한 뒤 소비/게시하며 실패 시 정상 망 변경을 기록하지 않는다. 생성 구성·위치/범위·수요·망 총량을 후속 단계에서 다시 대조하지 않는다.

PowerRangeUtility의 경계 포함 정사각형, OR 연결 범위, 회전 footprint 최소 거리(long 계산)는 유지한다. 별도 위치/연결 스냅샷·영속 망 집계는 추가하지 않는다.

## 준비와 실행 결정

MinerWorkPreparation/CrafterWorkPreparation의 Writer는 Preparation이고 Reader는 수요 Writer와 BuildingDecision이다. 준비 enable은 완료를 뜻하며 정상 작업 불가와 미준비를 구분한다. PowerDemand는 준비 데이터만 읽고 기존 MinerDecision/CrafterDecision을 읽지 않는다. BuildingDecision은 준비 자격과 PowerSupplyResult로 실행/완료 출력·상태 후보를 작성한다. 무전력 신규 착수 금지·완료 출력 허용·배정 잔여분 소비는 유지한다.

## 공통 실패와 비공개 scratch

PowerFrameState는 제거했다. PowerSimulationGroup은 단계 구성만 담당한다. PowerValidationInitializationSystem이 비공개 NativeReference<PowerValidationResult>와 마지막 접근 JobHandle을 생성/초기화/해제하는 소유자다. 메모리는 World 수명으로 재사용하지만 메시지 값은 매 틱 Reset Job으로 초기화한다. 각 시스템은 GetDependency/Register로 모든 접근과 입력 해제를 연결한다. 첫 오류를 보존하고 후속 정상 변경 Job을 차단한다. EndPower는 소유자의 CompleteAndTryConsumeFailure로 체인 완료/첫 오류를 인계받아 공통 SimulationFatalError.StopScope=RemainingDomains로 게시한다. 오류의 중복 인계 방지 표시는 소유자가 관리하며 초기화 시스템 자체는 오류를 게시하지 않는다. Building/Drone은 공통 StopScope를 읽으며 기존 NextTick 오류는 현재 틱을 마친다. 원본 rollback/자동 복구는 없다.

PowerReadContext와 반복 역할/선택 검증 Job은 제거했다. 필요한 조회 핸들만 직접 전달한다. 예산 Reservation은 준비·대표·유한 수요/가용량만 검사하고 Execution은 계획/예약 준비와 실제 소비할 연료만 검사한다. 두 단계의 로컬 Ready는 조기 반환 후 후속 변경을 차단하며 마지막 Reader 이후 해제한다. StateApply 수집은 준비된 토폴로지/선택만 확인하고 원본 구성을 다시 검사하지 않는다. 공급 결과 초기화는 Preparation의 단독 책임이다. 구성 불변식은 생성/변경 Writer가, 일반 소유 버퍼 정합성은 기존 개발용 검사도 확인한다. 정수 연료 전환/소비 실물 검사를 마친 뒤 CommitPowerSupplyJob이 버퍼·잔량·공급을 변경한다. 공유 인덱스/Fence·ECB Producer 계약은 유지한다.

## 검증 소유자/입력 분리 검증

이번 소유자/Lookup 분리 변경은 Unity 컴파일 completed/failed false/errors 없음/compilationFailed false·Editor ready/비컴파일/비리로드·최신 runtime/Editor DLL과 관련 선택 EditMode 170/170(실패·생략·불확정 0)으로 확인했다. Play Mode·베이킹·Player 빌드·성능은 미수행이다.

2026-10-15 검수 보완: 예산 검증의 조기 반환을 로컬 Ready로 배분에 전달하고 공급 결과 중복 초기화를 제거했다. 선택 전신주 삭제/토폴로지 제거의 발전기·소비자 경로 4사례와 미준비 발전 계획·수요 2사례를 추가했다. Unity 컴파일 completed/failed false/errors 없음/compilationFailed false·Editor ready/비컴파일/비리로드·최신 runtime/Editor DLL과 관련 선택 EditMode 112/112(실패·생략·불확정 0)을 확인했다. 대상과 실행 한계는 [전력 명세의 검수 지적 보완](../../Specifications/PowerGrid.md#검수-지적-보완-2026-10-15)을 따른다. 이전 170/170과 합산하지 않는다.

2026-10-15 Writer 경계 정리에서는 중간 Writer가 없는 구간의 재검증과 대조 전용 계획 필드를 제거했다. 세부 소유자·남긴 검사·변경된 테스트는 전력 명세의 「Writer와 검사 경계」를 따른다.

이번 Writer 경계 정리는 Unity 컴파일 completed/failed false/errors 없음/compilationFailed false·Editor ready/비컴파일/비리로드·최신 runtime/Editor DLL과 관련 선택 EditMode 174/174(실패·생략·불확정 0)으로 확인했다. 이전 170/170·112/112과 별도 실행이며 Play Mode·베이킹·Player 빌드·성능은 미수행이다.
