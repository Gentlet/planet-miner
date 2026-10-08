# Architecture V2 코드 지도

기준: 2026-10-06 현재 실행 그룹과 직접 연결된 코드 경로를 대조했다. 이 문서는 탐색용 코드 지도이며 설계·안전·실행 계약의 기준은 [AGENTS.md](../../../AGENTS.md), ECS 타입별 Writer/Reader·수명은 [컴포넌트 색인](../../CodeMemory/Components/README.md)이다. 주요 파일 경로는 [CSharpFileIndex.md](CSharpFileIndex.md)를 따른다. 아래 날짜가 붙은 과거 실행 결과는 당시 기록이며 현재 결과와 합산하지 않는다.

## 실행 경계

```text
InitializationSystemGroup: 설정·초기 청크 요청·프리팹 DB 준비
SimulationSystemGroup
  GameSimulationGroup
    Command / EndCommand
      → BuildingSimulation: Decision → Reservation → Execution → StateApply(마지막 완공)
      → EndBuilding
      → DroneSimulation: Decision → Reservation → Execution → StateApply
      → SimulationCommit / EndSimulation
      → Synchronization
```

- Command는 외부 요청 검증·배치/취소/철거 승인·레시피/청크 명령을 처리한다. 실제 생성/반환/요청 삭제의 Command 결과는 EndCommand에 확정한다.
- Building의 네 페이즈는 벨트·저장·생산·라우팅과 일반 아이템/건물 수명주기를 한 번 처리한다. ConstructionLifecycleApplySystem은 BuildingStateApply의 OrderLast에서 지난 틱 드론 도착량과 현재 월드 Owner/GridPosition·활성 Destroy로 완공을 판단한다. EndBuilding은 건물 그룹의 마지막 직접 자식이다.
- 건물 입출고·생성·철거 반환/환급·삭제는 EndBuilding 뒤 같은 틱 드론 입력에 보인다. 기존 공급원 종류·품목 제한은 유지한다. 이번 틱 건물 입고로 생산 판단을 반복하지 않는다.
- Drone의 네 페이즈는 생성/무효화/경로 의도·후보 → 현장 예약 → 명령/인계 계획 → 성공분 정산·최종 배정 공개 기록을 처리한다. 계획은 건물 종료 상태로 준비하고 드론 적용 중 새 실물·공간을 추가하지 않는다. 이번 틱 수집품은 다음 틱 공급 신호부터 사용한다.
- 새 작업·경로·배정과 행동 결과는 EndSimulation에서 실체화하며 다음 틱부터 이용한다. 같은 틱 마지막 납품/회수가 완료되어도 현장 완공은 다음 Building 틱이다. 공급원 재고·보관 공간의 영속 예약은 없다.
- Synchronization은 최상위 OrderLast로 최종 확정 후 공간 맵을 재구축한다. 각 NativeContainer의 Fence와 ECS Job 의존성은 그룹 순서와 별도로 유지한다. WorldInvariantValidationSystem은 내부 마지막 검증이다.
- Ready/Fatal은 GameSimulationGroup의 틱 시작에 검사한다. 중간 ECB에서 Fatal이 생겨도 현재 틱은 마치고 다음 틱부터 차단한다. 전체 rollback을 보장하지 않는다.

현재 상세 계약과 실행 원본은 [건설·드론 명세](../../Specifications/ConstructionAndDroneSupply.md), [드론 컴포넌트](../../CodeMemory/Components/DroneLogistics.md), [도메인 분리 검증](../V2%20Quality%20Evaluation%20Plan/Results/BuildingDroneDomainSplit-Verification.md)을 따른다. 프로젝트 ECS 정의는 103개이고 드론 계약은 그중 23개다. 수는 파일 수나 런타임 엔티티 수가 아니다.

같은 그룹 내부 순서는 UpdateBefore/UpdateAfter/OrderLast로 확인한다. 그룹 간 관계는 같은 부모의 그룹에 선언한다. 숫자 폴더·파일 나열·등록 순서로 실행이나 Job 완료를 추정하지 않는다. 각 시스템은 본체 한 파일을 유지한다.

## 상태 소유권과 읽기/쓰기 계약

| 데이터 | 원본/작성 위치 | 주요 소비자 |
| --- | --- | --- |
| `ItemOwnership.Owner` | 건물/현장/수행자에 수납한 실물의 Owner이며 Null은 월드. 생성 경계의 초기화, 일반 Transfer 적용, 드론 공통 TryTransferItem, 공사 취소·건물 철거의 기존 실물 반환이 각 책임에 따라 변경한다 | `ItemSpatialSyncSystem`, 벨트/저장 결정, 불변식 검사 |
| `StoredItemElement` | 창고·제작 재료·현장 도착 자재·드론 적재 실물. 일반 입출고/생산/레시피/취소/완공과 공통 실물 인계 경계가 갱신한다 | 입고 예약, 일반 출고, 제작 결정, 불변식 검사 |
| `ProductItemElement` | 채굴기/제작기의 출력 대기 실물. ItemLifecycle의 생산 결과·직접 Product Spawn, 레시피 Command의 잔여 재료 반환이 추가하고 출고 반영이 제거한다 | 생산 가능량 판단, 생산품 출고, 불변식 검사 |
| `ProductResult` | Miner/Crafter Execution의 생산 의도. `ItemLifecycleApplySystem`이 실제 아이템 엔티티로 바꾸고 비움 | Item Lifecycle Apply |
| `BeltMovementState.Progress` | Execution의 타일 내 실제 진행도 | Decision, 입고 판단, 공간 검증 |
| `BeltMovementDecision.PlannedProgress` | Decision의 프레임 계획, Execution이 소비. F-030 속도 상한은 설정·공통 생성 경계에서 적용 | Execution, 불변식 검사 |
| 공간 인덱스 4종 | `*SpatialSyncSystem`이 각 인덱스를 `ClearJob` 후 populate. 아이템 인덱스는 월드 아이템만 포함 | 각 Decision/Execution/Reservation 시스템의 조회 및 검증 |

`BeltSpatialIndex`, `BuildingSpatialIndex`, `ResourceSpatialIndex`는 셀당 하나의 값을 저장하고, `ItemSpatialIndex`는 여러 아이템을 저장한다. 각 인덱스는 별도 `*Fence`를 통해 reader/writer `JobHandle`을 연결한다. `Complete()`는 재할당·종료와 프레임 말의 메인 스레드 검증에서 호출된다. 공간 인덱스는 원본 ECS 컴포넌트로부터 만든 파생 상태이므로 구조 변경 직후의 조회 시점에 주의한다.

## 주요 흐름

1. `ItemConfigInitSystem`은 `StreamingAssets/ItemConfig.json`의 실제 모든 품목에 명시적 양수 MaxStack을 요구하며 검증 후 `ItemRegistry` 태그와 `ItemConfigElement` 버퍼로 게시한다. None 설정·공통/품목별 기본값 보충은 없다. `RecipeInitSystem`은 `Resources/Config/CrafterRecipeConfig`를 `RecipeRegistry`와 레시피·재료·출력 버퍼로 게시한다. 로드 실패 차단·시작 시 1회 게시·World 소유 수명·중복 게시 거부 계약은 AGENTS.md의 초기화 절을 따른다.
2. 월드 아이템 생성 요청과 Miner/Crafter의 `ProductResult`는 `ItemLifecycleApplySystem`에서 아이템 엔티티가 된다. 요청의 Storage/Product 목적지에 대상 버퍼가 없으면 아이템을 만들지 않고 요청을 소비한다. F-005의 Command 철거 검증·StateApply 생성 폐기 계약은 [AGENTS.md](../../../AGENTS.md)의 실행 단계 규칙을 따른다.
3. `BeltMovementDecisionSystem`은 벨트 속도와 앞 아이템 간격으로 이동량을 계산하고 `BeltMovementExecutionSystem`이 이동과 위치를 확정한다. 별도로 출고·Routing Decision과 `BeltDestinationReservationSystem`은 `BeltEntryUtility`의 같은 공간 판정을 사용하며, 예약은 외부 진입 후보 사이의 경합을 중재한다.
4. 벨트 끝 아이템의 입고는 `BuildingItemInputDecisionSystem` → `BuildingStorageInputReservationSystem` → `BuildingItemStorageApplySystem` → `ItemOwnershipApplySystem` 순서로 처리한다. 일반 저장품 및 생산품 출고는 서로 다른 Decision 시스템이 작성하고 공통 Apply 시스템이 처리한다.
5. `MinerDecisionSystem`은 footprint 아래의 자원과 출력 용량을 확인한다. `MinerExecutionSystem`은 자원량과 진행도를 갱신하고 결과를 기록한다. `CrafterDecisionSystem`은 재료와 모든 출력 슬롯을 검사하고, `CrafterExecutionSystem`은 재료 선소비·진행·결과 기록을 한다. `CrafterStateApplySystem`은 결정된 상태를 반영한다.
6. 건물/드론 처리와 EndSimulation 재생을 마친 뒤 공간 인덱스가 갱신된다. 개발용 `WorldInvariantValidationSystem`은 아이템·공간·요청·벨트 간격·저장 버퍼·결정 소비·자원 인덱스 정합성을 확인한다.

## 현재 구현 범위와 증거의 한계

현재 도메인 분리는 컴파일과 선택 EditMode 13개 클래스 181/181, 일회 허용된 실제 V2 Play Mode의 제어 입력 검증을 마쳤다. 실제 수행자·경로/관측/신호 Producer는 주입한 입력이므로 자동 운송의 증거는 아니다. 성능은 유휴 마커 326개 표본이며 변경 전후·대규모 부하 비교가 아니다. 아래 개발 이슈별 건수와 실행 범위는 해당 날짜의 기록으로 유지한다.

### 공사·철거와 품질 개선 연결 (2026-10-04 운송 제거 반영)

- 배치 Command가 현장을 만들고 `ConstructionCancelCommandSystem`은 같은 Command에서 취소를 처리한다. 취소와 완공은 단계별로 분리했으며 StateApply의 `ConstructionLifecycleApplySystem`은 완공 Job만 실행한다. 기존 운송 등록·실물 선점·수령·예약 정산·결과 처리 구조는 없다.
- 취소는 즉시 Cancelled를 설정해 중복 반환을 막고 보관 실물의 Owner·렌더·좌표 복원 및 현장/요청 삭제를 EndCommand에 반영한다. 같은 틱 재배치는 기존 공간 인덱스 점유로 거부하고 Synchronization 이후 새 요청부터 검증한다. 현재 BuildingStateApply 마지막의 Completion은 실제 도착량과 바닥 차단을 확인하고 공통 Spawn 성공 후에만 자재/현장을 EndBuilding에 삭제하며 실패 시 보존한다. 취소의 당시 실행 근거는 [취소 Command 이동 검증](../V2%20Quality%20Evaluation%20Plan/Results/ConstructionCancelCommand-Verification.md), 현재 완공 시점은 도메인 분리 검증을 따른다.
- `ConstructionSite.Progress` 필드와 생성자 인자는 제거했다. `ConstructionMaterialRequirementElement`의 예약량은 배정·공개 재검사·무효화와 3단계 실물 인계에서 정산하며, DroneTaskLifecycleApplySystem이 실제 공급한 수량을 DeliveredQuantity에 더한다. Storage/DroneStation/MainFacility의 공통 보관 구성은 유지한다. Progress 제거 당시 실행 근거는 [검증 기록](../V2%20Quality%20Evaluation%20Plan/Results/ConstructionProgressRemoval-Verification.md)을 따른다.
- [F-003 기록](../V2%20Quality%20Evaluation%20Plan/Results/F003-Verification.md)은 2026-10-02 당시 공사 운송 구현과 실행 결과를 보존한 과거 기록이다. 해당 운송 컴포넌트·요청·처리는 2026-10-04 제거되어 현재 구현 근거로 적용하지 않는다. 제거 범위와 새 검증은 [공사 운송 제거 검증 기록](../V2%20Quality%20Evaluation%20Plan/Results/ConstructionTransportRemoval-Verification.md)을 따른다.
- 철거 승인과 중복 제거는 BuildingDemolitionCommandSystem, 실제 반환/환급/파괴는 BuildingLifecycleApplySystem이 소유한다. EndCommand에서 PendingBuildingDemolition을 게시하고 요청을 모두 삭제한다. Decision은 건물 작업을 중단하며 새 아이템의 건물 입고도 ItemSpawnAdmissionDecisionSystem이 비활성화한다. Item Lifecycle/Ownership은 철거 요청과 상태를 조회하지 않는다. 실행 근거는 [철거 상태·동작 중단 검증](../V2%20Quality%20Evaluation%20Plan/Results/BuildingDemolitionStop-Verification.md)을 따른다.
- 기존 F-004의 같은 틱 입고/출고와 철거 충돌 처리는 앞단 중단으로 대체했다. Command가 보관 실물의 이전 Transfer와 ProductResult를 소비하고 이후 Decision 후보를 막는다. 활성 Destroy 제외, 기존 내용물 반환/비용 환급 및 일반 생성 실패 계약은 유지한다. [F-004 기록](../V2%20Quality%20Evaluation%20Plan/Results/F004-Verification.md)은 당시 결과를 보존하며 현재 계약은 AGENTS 및 새 철거 상태 검증을 따른다.
- Footprint는 현장/완공 모두 회전 전 크기를 저장하며 점유 Reader가 한 번 회전한다. 배치는 세 인덱스 Writer를 기다리고, 일반 창고 입고 예약은 품목과 수량을 함께 보관한다. 세부 계약은 [AGENTS.md](../../../AGENTS.md)를 따른다.
- Validator는 Storage 없는 현장을 포함해 두 소유 버퍼의 중복·실존·Identity·Owner를 검사한다. 진단 파일은 World별 세션/보고 순번과 CreateNew로 구분·보존한다. 기본 출력은 `Logs/InvariantErrors`이며 `SetLogDirectory`는 해당 World의 출력 경로만 바꾼다. Phase2BeltIntegrationTests는 사례마다 `Logs/Tests/Phase2BeltIntegrationTests/<실행 GUID>`를 지정하고 로그를 성공·실패 모두 보존하며 공용 로그를 정리하지 않는다. 이 검사는 공사 운송·자재 수령을 수행하지 않는다.
- 이 절은 소스 대조 결과이며 공사 전체·실제 베이킹·Play Mode 성공을 주장하지 않는다. 이슈별 새 검증과 제한은 [품질 개선 Tasks](../V2%20Quality%20Evaluation%20Plan/V2%20Quality%20Improvement%20Tasks.md)에 기록한다.

### F-037 입력 슬롯 계산·런타임 연결·통합 검증 (2026-09-29 완료)

- 확정 규칙: 같은 품목의 요구량을 합산하고 `ceil(요구량 / ItemRegistry의 MaxStack)`만큼 품목 전용 입력 슬롯을 계산한다. 슬롯은 최대 스택까지 비축 가능하며 제작 1회분만 입고하는 수량 제한은 아니다.
- `BuildingInputSlotElement`는 건물 입력 슬롯별 허용 품목만 보관하는 공통 데이터다. 버퍼 인덱스는 `StoredItemElement.SlotIndex`와 대응하고 길이가 입력 용량이다. 제작기의 레시피 미선택 구성은 빈 버퍼다. 실제 보관품과 최대 스택은 기존 버퍼/레지스트리가 소유한다. 연구·발전 등 다른 건물의 슬롯 산정/입고 정책은 아직 구현하지 않았다.
- `BuildingInputSlotUtility.TryCalculate`는 건물 공통 입력 슬롯의 순수 계산이다. `RecipeIngredientElement` 버퍼의 시작 위치·개수와 `ItemRegistry`/`ItemConfigElement`를 받는다. 기존 재료 요소의 품목/수량 형식을 재사용하며 제작 시간/출력물/건물 종류에는 의존하지 않는다. 최초 재료 등장 순서대로 동일 품목 슬롯을 연속 배치하고, 64비트로 중복 요구량을 합산한다. 반환값은 할당 없는 `FixedList512Bytes<BuildingInputSlotElement>`이며 ECS 상태에 직접 적용하지 않는다. 실패 사유는 `BuildingInputSlotCalculationErrorEnum`으로 반환한다.
- 0 이하 수량/최대 스택, None·미등록·인덱스 불일치 품목, `GameConstants.MaxStorageSlots` 초과는 오류 코드와 빈 결과로 반환한다. 임의 기본값, 슬롯 잘림, 부분 결과 적용은 없다. 빈 재료 목록은 성공/0슬롯이다. 입력 버퍼·범위 유효성과 계산 실패 시 기존 상태 유지 등 호출자 처리는 연결 단계의 책임이다.
- 관련 테스트: `Phase5CrafterInputSlotTests`의 올림·중복 합산·품목 구분·상한·오류·Burst Job 호출 사례. 2026-09-29 공통 데이터/유틸리티 명칭과 재료 목록 입력 인자 변경까지 반영한 뒤 Unity 6000.4.11f1 연결 Editor에서 컴파일 `completed`, 오류 0, 어셈블리 최신성을 확인했다. 선택 EditMode 테스트 18/18 통과(실패/생략/Inconclusive 0). 검증 래퍼 결과는 `Logs/Codex/F037-building-input-slots-verification.json`에 보존했다. 최초 구현 시 실행한 원본 결과 `Logs/Codex/F037-stage1-tests.json`과 구분한다.
- 직접 Spawn/공사 완료의 공통 `BuildingLifecycleUtility`가 Crafter의 `CrafterStateDecision`을 비활성으로 초기화하고, `Storage(0)`·빈 Whitelist·빈 입력 슬롯 버퍼를 구성한다. 프리팹 경로도 런타임 구성을 초기화한다.
- 레시피 변경 Command는 `CrafterDecision`/`CrafterStateDecision`의 값·활성 상태를 수정하거나 두 컴포넌트를 입력 구성 검사 조건으로 요구하지 않는다. 뒤의 `CrafterDecisionSystem`이 변경된 상태를 기준으로 프레임 결정을 작성한다. 생성 시 컴포넌트 구성·초기화와 런타임 결정 작성의 책임을 구분한다.
- `CrafterRecipeCommandSystem`은 계산을 먼저 검증한 뒤 슬롯 수·품목 배정·필터를 함께 갱신한다. 레시피/아이템 설정 미게시 시 선택 요청을 유지하고, 잘못된 레시피/계산 실패는 기존 상태를 보존한 채 오류와 함께 요청을 소비한다. 레시피 해제는 설정 없이도 0슬롯/빈 필터로 처리한다. 잔여 입력은 원래 슬롯 구분을 유지하여 기존 출력 슬롯 뒤로 옮기고, 배출 완료 전 입고/제작 대기를 유지한다.
- 입고 Decision은 0슬롯을 거부하고, Reservation은 입력 슬롯 버퍼가 있는 건물에 해당 품목 슬롯만 배정한다. 같은 프레임의 예약 수량과 기존 보관량을 합산해 MaxStack을 적용한다. 버퍼가 없는 일반 창고의 기존 예약 규칙은 유지한다. 불변식 검사는 입력 슬롯 길이·보관 품목 대응을 확인하며, 미선택/무재료 Crafter의 정상적인 빈 입력 구성만 0슬롯으로 허용한다.
- 당시 회귀는 `Phase5CrafterInputPipelineTests`의 개별 시스템 15개와 실제 정렬된 제작·물류 여섯 단계 통합 4개로 검증했다. 직접 생성/공사 완료 × 전체 테스트 DB/선택 타입 테스트 DB의 네 경로에서 아이템 요청→공급 창고→벨트→제작기 입고→선소비→생산→벨트→목적지 창고를 실행했고, 레시피 미선택 입고 차단·변경 시 잔여물 배출·해제와 불변식 위반 0건을 확인했다. 현재 fixture는 Command→Building→Synchronization이며 당시 그룹 설명과 구분한다.
- 3단계 결과: Unity 6000.4.11f1 재컴파일 `completed`, 오류 0, 런타임/테스트 어셈블리 최신성 확인. 입력 연결 EditMode 19/19 통과(실패/생략/Inconclusive 0). 로그는 `Logs/Codex/F037-stage3-integration-verification.json`, 상세 기록은 [F-037 검증 기록](../V2%20Quality%20Evaluation%20Plan/Results/F037-Verification.md). 실제 SubScene baked prefab, Play Mode, 전체 게임 시스템, 전체 EditMode는 실행하지 않았다. 당시 `RecipeBlob.TryFindIngredient` 및 제작 재료 집계(F-014)는 변경하지 않았다. F-008에서 데이터 표현을 버퍼로 바꿨으며 재료 첫 일치 조회·제작 집계의 의미는 유지한다.
- 2단계 검증: Unity 재컴파일 `completed`, 오류 0·어셈블리 최신성 확인. 관련 EditMode 59개 사례가 각 최종 실행에서 통과했다(새 입력 연결 15, 기존 제작 8, 레시피 변경 2, 건물 생성 10, 공사 완료 8, 저장 소유권/결정/불변식 15, 기존 물류 통합 1). 최초 실행의 테스트 준비 오류는 설정 기반 MaxStack, 공간 인덱스 초기화, 실제 아이템 생성 계약에 맞춰 수정했다. 마지막 입력 연결 결과는 `Logs/Codex/F037-stage2-input-verification.json`; 이전 실패/중간 결과는 `Logs/Codex/F037-stage2-verification.json`과 구분한다. 전체 EditMode 및 Play Mode는 실행하지 않았다.

### Task 4.5~4.8 월드 생성 수명주기 (2026-09-27 갱신)

- `WorldGenerationConfigLoader`가 월드 시드·초기 크기·자원 설정을 검증하고 `WorldGenerationConfigLoadSystem`이 한 번 게시한다. 미정의 enum 숫자 값도 거부한다. `InitialChunkLoadBootstrapSystem`은 초기 N×N 청크를 요청 큐에 넣는다.
- `ChunkLoadCommandSystem`이 `GeneratedChunkTracker`의 완료 집합(`Map`)과 접수/반영 대기 집합(`Pending`)을 소유한다. 요청은 두 집합으로 중복 제거하고 `GeneratedChunkReadyElement`로 전달한다. 미처리 전달 버퍼를 프레임 시작에 지우지 않는다.
- `ResourceGenerationCommandSystem`은 설정별 프리팹을 업데이트당 한 번 조회한다. DB 지연, 필수 프리팹 누락, 소멸된 참조, LocalTransform 누락은 전체 청크 대기로 처리한다. 생성 가능한 자원 종류를 일부만 먼저 생성하지 않는다.
- 준비된 청크의 스폰 명령과 `GeneratedChunkCompletedElement`를 같은 `EndCommandEntityCommandBufferSystem`에 순서대로 기록하고 Ready를 소비한다. Command 끝에서 재생하여 같은 프레임 Decision 전에 실체화하지만, `ResourceSpatialIndex` 등록은 Synchronization에서 이루어진다. 즉시 Playback 우회 경로는 없다. Pending은 유지되며, 다음 Command에서 완료 알림을 소비한 뒤에만 Map으로 옮긴다. 정상적인 빈 청크도 완료 알림을 게시한다.
- 자원은 ECB 반영 프레임의 `ResourceSpatialSyncSystem`에서 등록되고 다음 프레임의 Miner가 조회한다. 공간 인덱스 소유자는 변경하지 않는다.
- 관련 회귀 테스트는 `Phase4WorldGenerationConfigTests`, `Phase4ChunkLifecycleTests`, `Phase4ResourceGenerationTests`, `Phase4ResourceAuthoringAndSpawnTests`다. 당시 검증은 GameSimulationGroup의 EndCommand·EndStateApply 경계를 구분했다. 현재 생성/삭제 경계는 EndCommand·EndBuilding·EndSimulation이며 아래 건수는 당시 실행 기록이다. 테스트용 ECS 프리팹 검증은 실제 SubScene 베이킹·시각 검증을 대체하지 않는다.
- 이번 개선 검증: Unity 6000.4.11f1 재컴파일 및 최신 런타임/테스트 어셈블리 확인 완료. 위 네 클래스의 EditMode 결과는 각각 9/9, 6/6, 7/7, 13/13 통과(총 35, Failed/Skipped/Inconclusive 0)다. Play Mode·실제 베이킹·시각·성능 측정은 수행하지 않았다.

### Task 4.9 바닥 생성 계약 (2026-09-27 갱신)

- `WorldGenerationConfig.json`의 `floor`가 레거시 바닥 파라미터·Grass/Dirt·전이 Sprite 목록을 보관하며, 자원과 단일 `worldSeed`를 공유한다. 별도 `FloorGenerationConfig.json`은 제거했다.
- `WorldGenerationConfigLoader`는 자원과 바닥 설정, Sprite Resources 참조를 모두 검증한 뒤 함께 게시한다. 바닥 파라미터는 `FloorGenerationSettings`, 바이옴과 변형은 동일 엔티티의 버퍼다.
- `FloorBiomeSampler`는 월드 셀 좌표의 `FloorTileSelection` 및 변형 Sprite 경로를 계산한다. 청크나 점유 상태를 소유하지 않으며 렌더링·메시·표시 수명주기는 Task 10B.6에 남는다.

아래 항목은 이 문서 최초 작성 시점의 범위 기록이며, 월드 생성은 위 갱신 절을 우선한다.

- Routing 갱신(2026-09-30): `SplitterDecisionSystem`/`MergerDecisionSystem`이 전달 후보를 만들고, `BeltDestinationReservationSystem`이 외부 진입을 승인하며, `RoutingApplySystem`이 이동·커서를 반영하고 결정을 소비한다. 최초 작성 당시의 Routing 런타임 미구현 설명은 현재 적용하지 않는다.
- 현재 건설의 배치·현장 취소·완공, 초기 청크 요청·자원 생성, 드론 작업/배정·현장 공급 예약·행동 신호 기반 인계와 정산은 구현되어 있다. 실제 드론 이동·경로 계산·관측/행동 신호 생성·공통 적재량 초기화, 전력·연구·게임 UI 및 실제 바닥 청크 렌더링은 후속이다. `BuildingTypeEnum`에 종류가 정의되어 있는 사실은 해당 기능의 런타임 구현을 뜻하지 않는다. 후속 계획은 상위 `Architecture V2 Tasks.md`를 참조한다.
- 최초 조사 당시 테스트 수와 결과는 현재 테스트 집합의 통과 증거가 아니다. 2026-09-30 품질 개선의 새 컴파일·선별 테스트 결과와 미검증 범위는 품질 개선 Tasks의 해당 이슈에 기록한다. 테스트 메서드가 존재한다는 사실은 현재 통과 증거가 아니다.
