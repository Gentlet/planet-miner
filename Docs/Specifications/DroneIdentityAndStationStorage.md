# 드론 Identity 전환과 정거장 보관

2026-10-22 Task 9.3. 사용자 확정 1A/2A/3A/수정 5/6A/7A와 [Lifecycle](DroneLifecycle.md)의 엄격한 수납 자격을 구현한다. [생성 기반](DroneCreation.md)과 [정거장 네트워크](DroneStationNetwork.md)를 재사용한다. 실제 경로/이동/도착과 최소 비상 귀환은 [이동 명세](DroneMovement.md)를 따른다. 정상 귀환/정박/수납 자동 흐름과 실제 충전은 후속이다.

## 설정과 슬롯 원본

- BuildingConfigElement.DroneStationStorageCapacity는 종류/티어별 드론 전용 슬롯 수다. 현재 DroneStation/MainFacility는 각각 5칸이며 일반 storageCapacity 20/50과 별개다. JSON의 명시적 양수 정수가 필수이고 총 슬롯은 MaxStorageSlots 이하다. 다른 종류에 이 설정을 쓰면 거부한다. 사전 등록/공개 Publish도 같은 검증을 적용한다.
- 공통 건물 생성은 Storage.SlotCount에 일반+전용의 총 슬롯 수, DroneStationStorage에 전용 수, 빈 DockedDroneElement 버퍼를 부착한다. 일반 슬롯은 앞 구간, 전용 슬롯은 뒤 구간이다. 위치/footprint/회전 원본과 기존 정거장 망을 복제하지 않는다.
- 드론 아이템은 ItemConfig의 실제 MaxStack 50으로 스택한다. 활성 정박 드론은 한 슬롯 전체를 점유한다. 두 표현을 같은 슬롯에 혼합하지 않는다. 귀환 보관은 전용 구간부터 선택하고 부족하면 일반 구간을 공유한다. 일반 아이템은 전용 구간을 사용하지 않는다.
- DroneConfig의 미사용 stationStorageCapacity=13을 제거했다. DroneConfigInitializationSystem은 carryingCapacity=3과 maximumBattery=100을 읽어 공통 DroneCapacityState/DroneBatteryConfig를 한 번 게시한다. 현재량은 개체의 DroneBatteryState 하나가 소유한다. 유효한 기존 singleton은 보존하며 중복/무효/로드 실패는 원본 보존과 공통 Fatal, 미게시, 기본값/자동 재시도 없음이다. 둘 다 준비된 경우 재초기화 입력을 읽지 않는다.

## 요청과 공개 경계

외부 입력은 시뮬레이션 전 메인 스레드에서 DroneStationRequestUtility.Submit으로 접수한다. World 순번 순서로 처리하며 같은 수행자에 이번 틱 한 요청만 승인한다. 단순 입고가 자동 활성화를 일으키지 않는다.

| 단계 | 소유자와 출력 |
| --- | --- |
| DroneDecision | DroneStationLifecycleDecisionSystem의 병렬 Burst IJobEntity가 EndBuilding 원본에서 요청별 활성화/정박/수납 자격을 DroneStationDecision에 작성한다. |
| DroneReservation | DroneStationLifecycleReservationSystem의 단일 Burst IJob이 기존 작업 예약 선택 뒤 Sequence 순으로 요청과 슬롯 경합을 중재한다. Approved/SlotIndex/거부 Status를 작성하며 실물이나 영속 예약을 만들지 않는다. |
| DroneStateApply | 작업 Publish 기록 뒤 단일 Burst 데이터 Job이 최신 실물/동작/공간을 검사하고 역할/양쪽 보관 관계/비관리형 표시/결과를 EndSimulation ECB에 기록한다. Job 완료 뒤 메인 스레드는 승인된 표시 목록의 관리형 RenderMeshArray만 별도 EndSimulation ECB에 기록한다. 선택한 슬롯을 새로 확장하지 않는다. |
| EndSimulation | 역할과 보관 관계, 요청/Decision 제거와 같은 요청 Entity의 DroneStationResult를 함께 공개한다. 외부 TryConsumeResult가 결과를 한 번 소비한다. |
| Synchronization | 기존 인덱스가 최종 원본을 읽고 WorldInvariantValidationSystem은 전용 슬롯/정박 양방향 관계를 검사한다. |

슬롯 예산은 각 단계 호출의 임시 자료다. 예약 계획 뒤 선행 드론 행동이 공간을 사용하면 최종 적용은 거부할 수 있다. 이번 틱 활성화로 새로 비는 공간은 같은 계획에서 재사용하지 않는다. 앞선 슬롯 예약이 실패해도 다른 요청에 예산을 확대/재배분하지 않는다. 기존 미공개 작업 배정/일반 운반 승인이나 행동 요청이 있는 수행자는 보관 전환하지 않는다. 일반 운반은 Take 후 승인 잔량이 0이어도 PublicationQueued이면 같은 EndSimulation 전 Busy를 유지한다. 새 수행자는 다음 틱부터 보이고 최초 관측 CanAcceptTask=false를 유지한다.

### Job 의존성과 임시 자료

Decision은 요청별 독립 출력을 병렬로 쓴다. Reservation/Apply는 ToEntityListAsync의 deferred array와 선행 state.Dependency를 결합해 예약하며 Job 안에서 요청을 Sequence로 정렬한다. 미공개 배정/행동의 수행자 집합, 정거장별 슬롯 범위와 평탄한 슬롯 예산은 해당 Job의 Native 임시 자료이며 단계 간 영속 캐시로 공유하지 않는다. Decision/최종 Apply의 공통 자격 규칙은 역할 구성을 EntityQueryMask로, 값은 RO Lookup으로 확인한다.

StateApply의 데이터 Job은 원본 ECS를 바꾸지 않고 ECB와 Actor/Prefab 표시 목록을 작성한다. 데이터 ECB의 Producer handle을 EndSimulation에 등록하고 관리형 표시 목록을 읽는 경계에서 한 번 Complete한다. 뒤에 생성한 관리형 표시 ECB도 같은 EndSimulation에서 재생한다. 비동기 입력 목록은 마지막 접근 뒤 Dispose하고 표시 목록도 완료 뒤 해제한다. 새 공유 Fence나 Persistent 저장소는 없다. 관리형 shared component 기록에 필요한 대기가 남으므로 전체 파이프라인의 비동기화나 성능 향상을 검증했다고 해석하지 않는다.

## 세 가지 전환

| 요청 | 조건/변경 |
| --- | --- |
| Activate | 정거장이 실제 소유한 드론 아이템을 같은 Entity의 수행자로 바꾼다. ItemIdentity/ItemOwnership/아이템 요청/벨트 역할과 정거장 보관 참조를 제거하고 생성 API의 Worker/관측/빈 배정/화물/Idle을 구성한다. 새 개체는 설정 최대치로 최초 충전하고 기존 유효 배터리는 그대로 둔다. 정박 수행자의 Activate는 정박 관계만 해제하며 배터리를 채우지 않는다. |
| Dock | 활성 수행자의 실제 LocalTransform/GridPosition이 정거장의 회전 footprint 안이고 Idle이어야 한다. 빈 슬롯 하나를 점유하는 DroneDocked와 DockedDroneElement를 함께 게시한다. 활성 표시/배터리/화물/기존 배정은 보존하며 수납 아이템으로 숨기지 않는다. Movement가 실제 도착을 제공하며 자동 Dock 요청 Producer는 9.13 후속이다. |
| Store | 실제 정거장 내부, Idle, 완충, 배정/진행 중 행동 없음, 빈 화물/None 출처, 보관 공간의 모든 조건이 필요하다. 같은 Entity의 Worker 역할/화물 버퍼/정박 관계를 제거하고 공통 아이템 초기화로 Drone ItemIdentity/Stored Owner/비활성 이동과 요청을 구성한다. 정거장 StoredItemElement와 숨김을 함께 게시한다. 기존 정박 슬롯이 있으면 그 슬롯을 유지한다. |

표시는 검증된 단일 루트 프리팹의 MaterialMeshInfo/RenderMeshArray/RenderBounds와 TRS/PostTransformMatrix를 사용한다. 논리 Entity를 파괴/재생성하지 않고 다른 역할의 배터리를 삭제하지 않는다. 생성 helper의 실패 전에는 아이템/보관 원본을 제거하지 않는다.

DroneActivityState는 Idle/Moving/Acting/Charging/WaitingForCharge의 수행자 자체 동작이며 작업/배정 상태를 복제하지 않는다. 생성 초기 Writer는 DroneSpawnUtility, 이동/대기 갱신 Writer는 DroneMovementExecutionSystem이다. 실제 충전 Writer는 후속이다. DroneDocked는 활성 정박 관계의 원본, DockedDroneElement는 같은 소유자의 정거장 역참조다. 수납 아이템의 관계는 기존 ItemOwnership/StoredItemElement를 사용한다.

## 벨트/기존 보관과의 연결

정거장/주 시설의 일반 아이템 벨트 입출고는 유지한다. 드론 아이템 벨트 입고는 전용 구간의 실제 스택 여유와 같은 틱 PendingAdditions를 확인한다. 일반 구간만 비어 있으면 드론 벨트 입고를 허용하지 않는다. 활성 정박 슬롯에도 입고하지 않는다. 벨트 출고는 드론 아이템을 건너뛰며 일반 아이템끼리 기존 버퍼 FIFO를 유지한다.

드론 운송의 보관 공간 선택/슬롯 계획/실제 인계도 같은 슬롯 허용 규칙과 활성 정박 점유를 읽는다. 공통 ItemOwnership 인계는 정거장의 호환되지 않는 슬롯이나 활성 정박 슬롯을 거부한다. 일반 아이템 생성 요청의 기존 명시 슬롯 입력/승인 계약은 유지하며 자동 배치/스택 예약 Producer는 추가하지 않는다.

활성 정박 드론이 있는 정거장의 철거는 현재 Command에서 거부한다. 전체 수용량 선검사와 다른 정거장으로의 재배치는 Task 9.19 후속이다. 일반 보관 아이템의 기존 철거 반환은 유지한다.

## 검증 상태

2026-10-22 Job 분리 변경은 Unity 컴파일 completed/failed false/errors 없음/compilationFailed false와 Burst 활성/동기 컴파일 설정에서 관련 선택 EditMode 119/119(실패/생략/불확정 0)를 확인했다. Lifecycle 18, 생성 6, 작업 배정 39, 실물 인계 33, 재목표 17, 통합 6이다. 서로 다른 요청 archetype의 180건을 처리해 접수 순서/100건 슬롯 한도/버퍼 순서/EndSimulation 전 원본 보존을 확인했다. 기존 왕복 테스트에 서로 다른 RenderMeshArray를 추가해 데이터/관리형 표시가 Playback 전 보존되고 함께 전환되는 경계도 검사했다.

Job 분리 후 실제 SubScene Baker/제품 설정/공통 생성의 같은 Entity 왕복 probe도 다시 통과했다. Decision/Reservation 사이에 UpdateAndComplete를 두지 않고 의존성을 연결한 직접 실행도 통과했으며 전체 게임 그룹 통합 실행과 구분한다. 이 격리 Baker 결과에는 RenderMeshArray가 없으므로 실제 Baker의 관리형 shared component 교체 증거로 해석하지 않는다. 해당 분기는 위 EditMode의 명시적 shared component 입력으로 검증했다. Task93Jobs v1의 Decision/Reservation/StateApply/경계/guard 독립 5개 역할 검수는 고정 소스 해시에서 지적 없이 완료했다. Play Mode/화면 표시/실제 비행/충전/Player 빌드/성능은 미수행이다.

이전 Task 9.3 구현의 Unity 컴파일과 선택 EditMode 340/340(실패/생략/불확정 0)은 아래 별도 실행 기록이다. 초기 실행에서 기존 fixture의 필수 ItemConfig 부재 3건과 Command 이전 설정 부재로 발생한 Fatal 2건을 보완했다. 기존 필터 거부 검사는 현재의 TargetBuilding 초기화 계약과 일치시켰고 새 Coal 회귀 1건의 기대 로그는 기존 전력 역할 오류에 맞췄다. 실패를 숨기거나 아이템/작업 정리 assertion을 제거하지 않았다.

저장된 실제 V2 SubScene 루트들을 설치된 Entities Baker로 격리 World에 베이킹하고 제품 설정/공통 건물/아이템 생성과 요청 시스템을 실행했다. Ready1/Fatal0, 정거장 총25/전용5, 공통 적재량3, 동일 Entity의 Item→Worker→Item, 활성/아이템 MaterialMeshInfo 교체, 활성 scale0.5, 최종 Owner/숨김/배터리100을 확인했다. 최대 배터리는 제품 DroneConfig 로더의 실제 입력이다. probe는 내부 BakingUtility reflection을 사용하는 검증 도구이며 제품 코드에 추가하지 않는다.

독립 6개 역할의 v1 검수에서 일반 Storage의 기존 공개/사전 등록 용량 검증 누락과 보관 타입의 JSON 정수 형식 검사를 보완했다. Coal 용량은 기존 전력 역할 검사가 이미 거부하므로 중복 검사 없이 재사용한다. 게시 중 예외는 이번 호출이 만든 Entity만 회수하도록 보완했으며 실제 게시 예외를 주입해 재현하지는 않았다. 기존 상태 보존/잘못된 입력은 회귀로, 예외 회수는 정적 재검수로 구분한다.

정거장 필수 버퍼 누락은 이를 제거하는 정상 제품 Writer가 확인되지 않은 내부 불변식 위반이다. 정상 경로의 P2 회귀로 분류하지 않고 P3 진단/방어 보완으로 Command 거부와 BuildingType 기준 누락 진단을 추가했다. 누락 진단 분기는 정적으로 확인했다. 추가 P3인 Disabled 정거장 소유권 진단 오보는 구성/등록/역방향 쿼리의 비활성 포함 범위를 맞추고 실제 Dock→Disabled→최종 Synchronization/validator에서 무위반을 실행 확인했다. v1의 독립 6개 역할, v2의 초기화/guard/경계, v3의 guard/경계 집중 재검수를 완료했고 해당 지적을 해소했다. Play Mode/화면 표시/실제 비행/충전/Player 빌드/성능 검증은 포함하지 않는다.

이전 구현의 선택 묶음은 다음 18개 클래스다. 이번 Job 변경의 119개와 합산하지 않는다.

| 테스트 | 통과 수 |
| --- | ---: |
| DroneConfigurationTests / DroneStationLifecycleTests | 24 / 17 |
| Phase3StorageDecisionTests / Phase3StorageOwnershipTests | 7 / 4 |
| DronePrefabAndSpawnTests / PrefabDatabaseInitializationTests / DroneStationNetworkTests | 6 / 27 / 14 |
| Phase3BuildingRuntimeConfigTests | 5 |
| Phase7BuildingLifecycleTests / Phase7BuildingAuthoringPrefabTests / Phase7PlacementCommandTests / Phase7EndToEndConstructionPipelineTests | 11 / 24 / 24 / 5 |
| DroneItemTransferTests / DroneTaskSchedulingTests / DroneRetargetingTests / DroneLifecycleIntegrationTests | 33 / 39 / 17 / 6 |
| Phase8PowerContractTests / Phase8PowerTopologyTests | 26 / 51 |

활성 역할 전환은 이동 State/Decision/Result도 생성/제거한다. 최소 비상 귀환의 실제 정거장 도착은 자동 Dock/Store/충전 성공이 아니며, 활성 배정/화물을 보존한 충전 대기는 후속 정상 귀환/충전 소유자의 입력을 기다린다. 실제 이동/관측은 [이동 명세](DroneMovement.md)를 따른다.
