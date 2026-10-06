# 벨트·분배·합류 컴포넌트

[전체 색인](README.md) · [아이템·보관](ItemsAndStorage.md) · 확인일: 2026-10-03

2026-10-04 공사 운송 제거에 따른 BeltMovementState Writer 설명을 갱신했다. 이번 변경의 실행 결과와 한계는 [공사 운송 제거 검증 기록](../../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/ConstructionTransportRemoval-Verification.md)을 따른다.

현재 소스의 생성·쿼리·Job 본문을 확인한 정적 분석이다. 이번 문서 작업에서 Unity 컴파일, 테스트, Play Mode, 입력·화면·성능 실행은 검증하지 않았다. 공간 인덱스와 Fence 자체는 전체 색인의 공간 문서에서 다룬다.

일반 벨트 이동은 아이템의 `BeltMovementState` → Decision의 `BeltMovementDecision` → Execution의 진행도·격자·시각 위치 갱신으로 이어진다. Splitter/Merger는 건물의 영속 RoutingState를 읽어 Decision을 만들고 Reservation이 목적지 경합을 정리한 뒤 **StateApply의 `RoutingApplySystem`**이 실제 전달과 커서 갱신을 한다. `RoutingTransferDecision` 선언 주석의 Execution 설명과 달리 현재 실행 그룹은 StateApply다.

`BeltDestinationReservationSystem`의 후보는 건물 출고와 Routing 전달이다. 일반 `BeltMovementDecision`은 이 예약을 거치지 않는다. 공간 조회는 Synchronization에서 구축된 Belt/Item 맵과 각 Fence를 사용하며 실제 이동·출고·Routing 이후의 공간 위치는 다음 Synchronization에서 다시 등록한다. [일반 이동](../../../Assets/Scripts/Systems/2_Decision/BeltMovementDecisionSystem.cs), [Execution](../../../Assets/Scripts/Systems/4_Execution/BeltMovementExecutionSystem.cs), [예약 후보](../../../Assets/Scripts/Systems/3_Reservation/BeltDestinationReservationSystem.cs), [Routing 반영](../../../Assets/Scripts/Systems/5_StateApply/RoutingApplySystem.cs).

2026-10-04 철거가 Command에서 승인되면 EndCommand에 PendingBuildingDemolition이 게시된다. Belt Movement Decision은 현재 철거 벨트의 이동을 0으로 하고 다음 철거 벨트로 넘어가지 않도록 경계 직전까지 접근한다. Input Decision도 철거 출발 벨트/목적 건물을 제외하며 출고·Split/Merge는 철거 owner 및 입력/출력 벨트를 제외한다. 공간 인덱스를 조기에 변경하지 않는다. 실제 벨트 아이템 정지와 철거는 기존 EndStateApply, 인덱스 갱신은 Synchronization이다. [현재 검증](../../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/BuildingDemolitionStop-Verification.md)을 따른다.

## BeltComponent

- **종류·부착 대상:** 일반 `IComponentData`, 벨트 건물 엔티티에 붙는다. `Speed`는 초당 타일 진행 거리이며 개별 아이템의 진행도를 보관하지 않는다. 벨트 건물의 `GridPosition`, `Direction`과 함께 쓰인다.
- **생성:** 직접 건물 스폰과 공사 완료가 사용하는 `BuildingLifecycleUtility.AttachTypeSpecificComponents`가 Belt 타입에 부착한다. 전달된 속도가 양수면 그 값, 아니면 2.0을 선택하고 `GameConstants.MaxBeltSpeed`로 상한을 적용한다. 이와 별도로 설정 로더는 Belt 속도가 비유한 값이거나 상한을 초과하면 설정 파싱을 거부한다. 생성은 호출자의 EndStateApply ECB에서 확정된다.
- **Reader:** Synchronization의 `BeltSpatialSyncSystem`이 위치·방향·속도를 `BeltInfo`에 복사한다. 이후 `BeltMovementDecisionSystem`은 컴포넌트를 직접 읽기보다 이 맵의 속도로 이동량을 계산한다. 출고·Routing·입고는 같은 BeltInfo의 존재·방향을 사용한다.
- **처리:** 현재 `ItemSpacing=0.25`, `MaxSimulationDeltaTime=0.1`에서 `MaxBeltSpeed=(1-ItemSpacing)/MaxSimulationDeltaTime=7.5`다. 이동 Decision은 clamp된 delta time과 속도를 곱한 뒤 앞 아이템 간격·다음 셀 수용량으로 계획을 제한한다.
- **수명·결합:** 벨트와 함께 존속하고 철거 시 건물과 함께 소멸한다. 같은 타입 벨트 덮어쓰기는 배치 Command가 방향을 변경하는 경로이며, 확인한 정상 runtime 경로에 프레임별 Speed 변경은 없다. 공간 맵은 직접 컴포넌트 변경을 즉시 반영하는 구조가 아니다.
- **근거:** [선언](../../../Assets/Scripts/Components/Belts/BeltComponents.cs), [공통 생성](../../../Assets/Scripts/Common/BuildingLifecycleUtility.cs), [설정 속도 검증](../../../Assets/Scripts/Config/BuildingConfigLoader.cs#L137), [상수](../../../Assets/Scripts/Common/GameConstants.cs), [동기화](../../../Assets/Scripts/Systems/6_Synchronization/BeltSpatialSyncSystem.cs), [이동 판단](../../../Assets/Scripts/Systems/2_Decision/BeltMovementDecisionSystem.cs), [덮어쓰기](../../../Assets/Scripts/Systems/1_Command/BuildingPlacementCommandSystem.cs).

## BeltMovementState

- **종류·부착 대상:** 아이템의 `IComponentData, IEnableableComponent`. `Progress`는 현재 타일 입구 0, 중심 0.5, 출구 1의 영속 진행도다. 활성 상태는 일반 벨트 이동 Job의 처리 대상 여부를 결정한다.
- **초기화·활성화:** `ItemLifecycleUtility`는 모든 새 아이템에 미리 붙이고 비활성화한다. World 목적지로 벨트 셀에 스폰했다는 이유만으로 여기서 켜지지는 않는다. Storage Apply의 출고와 Routing Apply의 전달이 Progress=0과 활성 상태를 직접 기록한다.
- **Reader:** Decision의 일반 이동·건물 입고·두 출고·Splitter/Merger, Reservation의 목적지 검사가 진행도를 사용한다. 일반 이동과 `BeltEntryUtility`의 점유 집계는 월드 소유이며 활성 이동 상태인 아이템을 센다. 건물 입고의 쿼리는 enable 상태를 무시하고, Routing의 종단 후보 탐색도 별도의 enable 검사를 하지 않으므로 모든 Reader의 조건을 같다고 보지 않는다.
- **Writer·처리:** Execution의 `BeltMovementExecutionSystem`이 계획을 Progress에 더하고 다음 벨트로 넘어가면 GridPosition과 잔여 진행도를 갱신한다. 종단은 1에서 멈추며 위치는 `셀 중심 + 방향*(Progress-0.5)`다. StateApply의 출고/Routing은 새 벨트 입구로 재설정한다.
- **비활성·수명:** 일반 입고는 즉시 비활성화한다. Building Lifecycle은 철거한 벨트 셀에 있는 아이템의 비활성화를 EndStateApply ECB에 기록하며 활성 Destroy는 건너뛴다. 공사 수령에 의한 비활성화 경로는 제거되었다. 컴포넌트는 보관 중에도 남고 실물 삭제 시 소멸한다. 매 틱 소비되는 Decision과 달리 Progress는 보존된다.
- **근거:** [선언](../../../Assets/Scripts/Components/Belts/BeltComponents.cs), [초기화](../../../Assets/Scripts/Common/ItemLifecycleUtility.cs), [Execution](../../../Assets/Scripts/Systems/4_Execution/BeltMovementExecutionSystem.cs), [입출고](../../../Assets/Scripts/Systems/5_StateApply/BuildingItemStorageApplySystem.cs), [Routing](../../../Assets/Scripts/Systems/5_StateApply/RoutingApplySystem.cs), [철거](../../../Assets/Scripts/Systems/5_StateApply/BuildingLifecycleApplySystem.cs).

## BeltMovementDecision

- **종류·부착 대상:** 아이템의 일반 `IComponentData`. enableable이 아니다. `PlannedProgress`는 이번 틱에 더할 이동량이며 도착 목표 진행도나 절대 위치가 아니다. `IsBlocked`는 원하는 이동을 충분히 수행할 수 없는지 나타낸다.
- **생성:** 공통 아이템 스폰이 기본값으로 부착한다. 처리 대상은 같은 아이템의 활성 `BeltMovementState`와 각 Job에 요구되는 위치·소유권 등으로 결정하며 이 Decision 자체를 켜고 끄지 않는다.
- **Writer·Decision:** `BeltMovementDecisionSystem`은 월드 아이템과 현재 벨트를 확인하고 같은 타일 앞 아이템, 다음 타일 가장 가까운 아이템과 개수, 종단 여부로 이동량을 기록한다. 원하는 거리는 현재 벨트 속도×제한된 delta time이며 간격은 `ItemSpacing`, 경계 오차는 `AlignmentEpsilon`을 쓴다.
- **Consumer·Execution:** `BeltMovementExecutionSystem`은 계획을 읽은 즉시 `PlannedProgress=0`으로 소비하고 실제 상태/좌표/Transform에 반영한다. 월드 소유가 아니거나 현재 벨트가 없어도 계획을 0으로 만든다. `IsBlocked`는 Execution이 초기화하지 않고 다음 Decision이 다시 계산한다.
- **수명·결합:** 아이템 수명 동안 재사용된다. 별도 요청 엔티티 삭제나 ECB가 필요 없는 데이터 변경이다. 일반 벨트 계획은 목적지 예약 후보에 들어가지 않으며 다음 셀을 넘는 전방 간격 정책은 생성 속도 상한과 현재/다음 셀 조회의 조합에 의존한다.
- **근거:** [선언](../../../Assets/Scripts/Components/Belts/BeltDecisions.cs), [초기화](../../../Assets/Scripts/Common/ItemLifecycleUtility.cs), [Decision](../../../Assets/Scripts/Systems/2_Decision/BeltMovementDecisionSystem.cs), [Execution](../../../Assets/Scripts/Systems/4_Execution/BeltMovementExecutionSystem.cs), [예약 범위](../../../Assets/Scripts/Systems/3_Reservation/BeltDestinationReservationSystem.cs).

## SplitterRoutingState

- **종류·부착 대상:** Splitter 건물의 일반 `IComponentData`. `InputBelt`는 기준 입력 벨트, `ForwardDirection`은 기준 전방, `OutputCursor`는 다음 출력 탐색의 시작 포트다. 아이템을 보관하는 버퍼는 아니다.
- **초기화:** 공통 건물 생성은 `InputBelt=Null`, 건물 방향을 초기 ForwardDirection, 커서 0으로 기록한다. 직접 스폰/공사 완료 모두 같은 구성을 EndStateApply에 만든다.
- **Reader·Decision:** `SplitterDecisionSystem`은 기존 입력이 없거나 유효한 인접 유입 벨트가 아니면 후보를 다시 찾는다. 기준 후보는 유입 연결 조건과 `PlacementStamp`의 앞선 설치 순서를 사용한다. 기존 기준과 다른 입력을 선택하면 이번 탐색은 커서 0부터 시작하지만 영속 상태는 이 단계에서 쓰지 않는다.
- **후보 처리:** 입력 벨트 종단의 월드 아이템을 찾고 커서부터 forward→right→left의 세 포트를 순환 탐색한다. 존재·외향 연결·입구 여유가 있는 첫 출력으로 `RoutingTransferDecision`을 켠다. 포트가 막혔으면 다른 포트를 계속 검사하고 모두 불가하면 결정을 끈다.
- **Writer·수명:** Reservation을 통과한 전달을 StateApply의 `RoutingApplySystem`이 실제 적용할 때 SourceBelt와 방향을 저장하고 실제 출력 포트의 다음 인덱스로 커서를 전진시킨다. 승인되지 않은 틱에는 커서가 진행하지 않는다. 건물에 유지되며 철거 시 소멸한다.
- **근거:** [선언](../../../Assets/Scripts/Components/Routing/RoutingComponents.cs), [생성](../../../Assets/Scripts/Common/BuildingLifecycleUtility.cs), [Splitter Decision](../../../Assets/Scripts/Systems/2_Decision/SplitterDecisionSystem.cs), [포트 계산](../../../Assets/Scripts/Common/RoutingDirectionUtility.cs), [StateApply Writer](../../../Assets/Scripts/Systems/5_StateApply/RoutingApplySystem.cs).

## MergerRoutingState

- **종류·부착 대상:** Merger 건물의 일반 `IComponentData`. `OutputBelt`는 기준 출력 벨트, `ForwardDirection`은 기준 전방, `InputCursor`는 다음 입력 탐색 시작 포트다. 합류할 실물은 입력 벨트에 있고 Merger 내부에 수납되지 않는다.
- **초기화:** 공통 생성은 `OutputBelt=Null`, 건물 방향을 초기 전방, 커서 0으로 기록한다. 완공 건물 생성 경로가 EndStateApply ECB에서 부착한다.
- **Reader·Decision:** `MergerDecisionSystem`은 기존 출력이 없거나 유효한 인접 외향 벨트가 아니면 설치 순서를 고려하여 새 기준 출력을 찾는다. 출력 입구 여유가 없으면 후보를 만들지 않는다. 기준이 변경된 틱의 입력 탐색은 0번부터 시작한다.
- **후보 처리:** 커서부터 back→left→right 세 포트를 순환하며 유입 방향의 벨트와 종단 월드 아이템을 찾는다. 첫 가능한 입력 실물에서 기준 출력으로 `RoutingTransferDecision`을 만든다. 이번 단계는 영속 커서를 쓰지 않는다.
- **Writer·수명:** StateApply `RoutingApplySystem`이 실제 전달할 때 출력 벨트·전방을 저장하고 실제 SourceBelt가 있던 입력 포트의 다음 인덱스로 커서를 갱신한다. 대기/예약 탈락 중에는 현재 커서를 유지하고 건물 삭제 때 소멸한다. 다른 출고/Routing과의 공유 출력 경합은 목적지 예약이 처리한다.
- **근거:** [선언](../../../Assets/Scripts/Components/Routing/RoutingComponents.cs), [생성](../../../Assets/Scripts/Common/BuildingLifecycleUtility.cs), [Merger Decision](../../../Assets/Scripts/Systems/2_Decision/MergerDecisionSystem.cs), [포트 계산](../../../Assets/Scripts/Common/RoutingDirectionUtility.cs), [Writer](../../../Assets/Scripts/Systems/5_StateApply/RoutingApplySystem.cs), [공유 목적지](../../../Assets/Scripts/Systems/3_Reservation/BeltDestinationReservationSystem.cs).

## RoutingTransferDecision

- **종류·부착 대상:** Splitter/Merger 건물의 `IComponentData, IEnableableComponent`. `Item`, `SourceBelt`, `TargetBelt`가 이번 틱 전달 후보이며 `IRequestComponent`가 아니다. 공통 생성이 비활성 상태로 미리 부착한다.
- **Decision Writer:** `SplitterDecisionSystem`/`MergerDecisionSystem`은 비활성 결정도 재계산하는 쿼리로 해당 Router 상태를 읽는다. 종단 아이템과 연결된 목적지를 선택하면 세 필드를 채워 켜고, 후보가 없으면 비활성화한다. 실물은 이 단계에서 이동하지 않는다.
- **Reservation Writer:** `BeltDestinationReservationSystem`은 활성 Routing과 건물 출고를 목적지 셀로 묶는다. 입구 점유를 다시 검사한 뒤 셀당 최대 한 후보만 활성 상태로 유지한다. 양쪽 Stamp가 있으면 Tick→Order→Entity.Index의 작은 값 우선, 한쪽만 있으면 Stamp가 있는 후보 우선, 없으면 Entity.Index 순이다.
- **Consumer·StateApply:** `RoutingApplySystem`은 실물/목적지의 위치와 목적지 방향이 있으면 GridPosition, 존재하는 아이템 Direction, BeltMovementState=0 및 활성 상태, LocalTransform을 직접 바꾼다. Router의 영속 기준 벨트·커서도 실제 포트 기준으로 갱신한다. Owner와 소유 버퍼를 바꾸는 동작은 아니다.
- **소비·순서:** 성공 여부와 관계없이 Apply 마지막에 세 참조를 Null로 지우고 비활성화한다. Reservation 탈락은 비활성화만 하며 다음 Decision이 다시 계산한다. Routing Apply는 Ownership Apply 및 Building Lifecycle보다 먼저 실행하고 전달 자체는 ECB 구조 변경을 사용하지 않는다. 최종 공간 맵은 Synchronization이 갱신한다.
- **근거:** [선언](../../../Assets/Scripts/Components/Routing/RoutingDecisions.cs), [생성](../../../Assets/Scripts/Common/BuildingLifecycleUtility.cs), [Splitter](../../../Assets/Scripts/Systems/2_Decision/SplitterDecisionSystem.cs), [Merger](../../../Assets/Scripts/Systems/2_Decision/MergerDecisionSystem.cs), [Reservation](../../../Assets/Scripts/Systems/3_Reservation/BeltDestinationReservationSystem.cs), [Apply](../../../Assets/Scripts/Systems/5_StateApply/RoutingApplySystem.cs), [철거 순서](../../../Assets/Scripts/Systems/5_StateApply/BuildingLifecycleApplySystem.cs).
