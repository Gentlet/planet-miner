# Architecture V2 코드 지도

기준: 2026-09-26 최초 조사 이후 관련 영역을 갱신했으며, 2026-09-30에 공사·철거·자원 ECB 및 품질 개선 계약을 현재 소스와 대조했다. 전체 파일 수를 재감사한 문서는 아니다. Unity가 생성하는 `Library/`의 C# 및 패키지 코드는 범위에 포함하지 않는다. 이 문서는 현재 파일의 역할과 연결 관계를 기록한다. 계획의 완료 표시나 과거 테스트 결과를 현재 실행 증거로 취급하지 않는다. 파일별 경로와 역할은 [CSharpFileIndex.md](CSharpFileIndex.md)를 참조한다.

## 실행 경계

프리팹 시작 관문과 실패 정책은 [AGENTS.md](../../../AGENTS.md)의 베이킹·프리팹 규칙을 따른다. `PrefabDatabaseInitializationSystem`이 준비를 게시하기 전이나 `SimulationFatalError`가 기록된 뒤에는 `GameSimulationGroup`을 실행하지 않는다. 런타임 프리팹 fallback은 제거되었고 테스트는 명시적인 DB를 제공한다.

`GameSimulationGroup`은 Unity `SimulationSystemGroup`에 속한다. 그룹 속성으로 명시된 순서는 다음과 같다.

```text
InitializationSystemGroup: ItemConfigInitSystem, RecipeInitSystem
SimulationSystemGroup
  GameSimulationGroup
    CommandGroup → DecisionGroup → ReservationGroup → ExecutionGroup
      → StateApplyGroup → SynchronizationGroup
```

- `CommandGroup`: 청크·자원 생성과 배치·철거 검증 및 레시피 변경을 처리한다. Command 구조 변경은 EndCommand에서 재생한다. 레시피는 기존 Dependency 완료 뒤 상태·잔여 입력/출력·전용 슬롯/필터를 갱신하며 무효 양수 ID는 상태 변경 전에 거부한다.
- `DecisionGroup`: 벨트 이동량, 건물 입출고, 채굴, 제작 가능 여부를 결정 컴포넌트에 기록한다. `CrafterDecisionSystem`은 상태 변경을 `CrafterStateDecision`으로 넘긴다.
- `ReservationGroup`: 건물 입고의 슬롯 경합과 외부 벨트 진입 경합을 처리한다. `BeltDestinationReservationSystem`은 건물 출고·Routing 후보 중 목적지당 최대 한 개를 `PlacementStamp`와 엔티티 인덱스로 승인한다. 일반 벨트 이동은 후보에 포함하지 않는다. T형 직접 합류의 지원 범위와 공통 진입 판정은 [AGENTS.md](../../../AGENTS.md)의 물류·생산 계약을 따른다.
- `ExecutionGroup`: 승인된 벨트 이동을 `BeltMovementState`·`GridPosition`·`LocalTransform`에 반영한다. 채굴/제작은 진행도를 누적하고 `ProductResult`를 기록한다. 제작 시작 시 재료 버퍼에서 아이템을 즉시 제거하고 `DestroyItemRequest`를 활성화한다.
- `StateApplyGroup`: 건물 입출고 버퍼와 소유권 요청, 제작기 상태, 아이템 생성/삭제, 공사 취소·수령·완공 및 건물 생성·철거를 반영한다. `BuildingItemStorageApplySystem`은 `ItemOwnershipApplySystem`보다 먼저 실행하도록 지정되어 있다. `EndStateApplyEntityCommandBufferSystem`은 그룹 마지막에 구조 변경을 재생한다.
- `SynchronizationGroup`: 벨트·건물·아이템·자원 공간 인덱스를 재구축한다. `WorldInvariantValidationSystem`은 개발 빌드/Editor에서 그룹 마지막에 검증한다.

같은 그룹 내 개별 시스템의 순서는 위에서 명시한 속성 외에 파일명 숫자나 이 문서의 나열 순서로 보장되지 않는다. 테스트 도우미가 개별 시스템을 직접 업데이트하는 경우와 실제 그룹 실행은 구분해야 한다.

## 상태 소유권과 읽기/쓰기 계약

| 데이터 | 원본/작성 위치 | 주요 소비자 |
| --- | --- | --- |
| `ItemOwnership.Owner` | 저장 아이템의 소유 건물, `Entity.Null`은 월드. `ItemOwnershipApplySystem`이 `TransferOwnershipRequest`를 소비해 변경 | `ItemSpatialSyncSystem`, 벨트/저장 결정, 불변식 검사 |
| `StoredItemElement` | 일반 저장 및 제작 재료 버퍼. 입출고 반영, 제작 시작, 레시피 변경 명령이 갱신 | 입고 예약, 일반 출고, 제작 결정, 불변식 검사 |
| `ProductItemElement` | 채굴기/제작기의 출력 대기 버퍼. `ItemLifecycleApplySystem`이 생산 결과를 추가하고 출고 반영이 제거 | 생산 가능량 판단, 생산품 출고, 불변식 검사 |
| `ProductResult` | Miner/Crafter Execution의 생산 의도. `ItemLifecycleApplySystem`이 실제 아이템 엔티티로 바꾸고 비움 | Item Lifecycle Apply |
| `BeltMovementState.Progress` | Execution의 타일 내 실제 진행도 | Decision, 입고 판단, 공간 검증 |
| `BeltMovementDecision.PlannedProgress` | Decision의 프레임 계획, Execution이 소비. F-030 속도 상한은 설정·공통 생성 경계에서 적용 | Execution, 불변식 검사 |
| 공간 인덱스 4종 | `*SpatialSyncSystem`이 각 인덱스를 `ClearJob` 후 populate. 아이템 인덱스는 월드 아이템만 포함 | 각 Decision/Execution/Reservation 시스템의 조회 및 검증 |

`BeltSpatialIndex`, `BuildingSpatialIndex`, `ResourceSpatialIndex`는 셀당 하나의 값을 저장하고, `ItemSpatialIndex`는 여러 아이템을 저장한다. 각 인덱스는 별도 `*Fence`를 통해 reader/writer `JobHandle`을 연결한다. `Complete()`는 재할당·종료와 프레임 말의 메인 스레드 검증에서 호출된다. 공간 인덱스는 원본 ECS 컴포넌트로부터 만든 파생 상태이므로 구조 변경 직후의 조회 시점에 주의한다.

## 주요 흐름

1. `ItemConfigInitSystem`은 `StreamingAssets/ItemConfig.json` 또는 기본값을 `ItemRegistryBlob`으로 만든다. `RecipeInitSystem`은 `Resources/Config/CrafterRecipeConfig` 또는 기본 레시피를 `RecipeRegistryBlob`으로 만든다.
2. 월드 아이템 생성 요청과 Miner/Crafter의 `ProductResult`는 `ItemLifecycleApplySystem`에서 아이템 엔티티가 된다. 요청의 Storage/Product 목적지에 대상 버퍼가 없으면 아이템을 만들지 않고 요청을 소비한다. F-005의 Command 철거 검증·StateApply 생성 폐기 계약은 [AGENTS.md](../../../AGENTS.md)의 실행 단계 규칙을 따른다.
3. `BeltMovementDecisionSystem`은 벨트 속도와 앞 아이템 간격으로 이동량을 계산하고 `BeltMovementExecutionSystem`이 이동과 위치를 확정한다. 별도로 출고·Routing Decision과 `BeltDestinationReservationSystem`은 `BeltEntryUtility`의 같은 공간 판정을 사용하며, 예약은 외부 진입 후보 사이의 경합을 중재한다.
4. 벨트 끝 아이템의 입고는 `BuildingItemInputDecisionSystem` → `BuildingStorageInputReservationSystem` → `BuildingItemStorageApplySystem` → `ItemOwnershipApplySystem` 순서로 처리한다. 일반 저장품 및 생산품 출고는 서로 다른 Decision 시스템이 작성하고 공통 Apply 시스템이 처리한다.
5. `MinerDecisionSystem`은 footprint 아래의 자원과 출력 용량을 확인한다. `MinerExecutionSystem`은 자원량과 진행도를 갱신하고 결과를 기록한다. `CrafterDecisionSystem`은 재료와 모든 출력 슬롯을 검사하고, `CrafterExecutionSystem`은 재료 선소비·진행·결과 기록을 한다. `CrafterStateApplySystem`은 결정된 상태를 반영한다.
6. StateApply의 ECB 재생 후 공간 인덱스가 갱신된다. 개발용 `WorldInvariantValidationSystem`은 아이템·공간·요청·벨트 간격·저장 버퍼·결정 소비·자원 인덱스 정합성을 확인한다.

## 현재 구현 범위와 증거의 한계

### 공사·철거와 품질 개선 연결 (2026-09-30 소스 대조)

- 배치 Command가 현장을 만들고, `ConstructionLifecycleApplySystem`이 내부 Job을 취소→자재 수령→완공 순서로 연결한다. 독립 Material/Cancel/Completion 시스템은 현재 경로가 아니다.
- 수령 Job은 요구량·진행·현장 Stored 버퍼를 직접 갱신하고 Owner/렌더 태그를 EndStateApply에 기록한다. 취소는 즉시 Cancelled를 설정해 뒤의 수령/완공을 막고 기존 실물을 반환한다. Completion은 별도 요청 없이 공통 `BuildingLifecycleUtility.SpawnBuilding`을 호출하며 성공 뒤에만 자재/현장 삭제를 기록한다.
- 철거 승인과 중복 제거는 `BuildingDemolitionCommandSystem`, 적용·기존 실물 반환·비용 환급은 `BuildingLifecycleApplySystem` 책임이다. 일반 생성·생산물·철거 환급은 `ItemLifecycleUtility`의 초기화를 공유하지만 각 호출자의 ECB·실패 정책은 유지한다.
- 2026-10-02 F-004는 기존 승인 요청과 입출고 후 소유 버퍼를 공유 조회하여 Ownership의 일반 이전, 공사 수령, 철거 반환과 유효한 Destroy의 적용 대상을 구분했다. 공사 수령만 Storage Apply 뒤로 명시하고 Ownership/Building Lifecycle의 상대 순서는 추가하지 않았다. 정책은 [AGENTS.md](../../../AGENTS.md)의 F-004 계약, 코드·컴파일 근거와 실행 미검증은 [검증 기록](../V2%20Quality%20Evaluation%20Plan/Results/F004-Verification.md)을 따른다.
- Footprint는 현장/완공 모두 회전 전 크기를 저장하며 점유 Reader가 한 번 회전한다. 배치는 세 인덱스 Writer를 기다리고, 일반 창고 입고 예약은 품목과 수량을 함께 보관한다. 세부 계약은 [AGENTS.md](../../../AGENTS.md)를 따른다.
- Validator는 Storage 없는 현장을 포함해 두 소유 버퍼의 중복·실존·Identity·Owner를 검사한다. 진단 파일은 World별 세션/보고 순번으로 구분한다. 검사 강화가 F-003/F-004의 생산 경계 문제 해결을 뜻하지 않는다.
- 이 절은 소스 대조 결과이며 공사 전체·실제 베이킹·Play Mode 성공을 주장하지 않는다. 이슈별 새 검증과 제한은 [품질 개선 Tasks](../V2%20Quality%20Evaluation%20Plan/V2%20Quality%20Improvement%20Tasks.md)에 기록한다.

### F-037 입력 슬롯 계산·런타임 연결·통합 검증 (2026-09-29 완료)

- 확정 규칙: 같은 품목의 요구량을 합산하고 `ceil(요구량 / ItemRegistry의 MaxStack)`만큼 품목 전용 입력 슬롯을 계산한다. 슬롯은 최대 스택까지 비축 가능하며 제작 1회분만 입고하는 수량 제한은 아니다.
- `BuildingInputSlotElement`는 건물 입력 슬롯별 허용 품목만 보관하는 공통 데이터다. 버퍼 인덱스는 `StoredItemElement.SlotIndex`와 대응하고 길이가 입력 용량이다. 제작기의 레시피 미선택 구성은 빈 버퍼다. 실제 보관품과 최대 스택은 기존 버퍼/레지스트리가 소유한다. 연구·발전 등 다른 건물의 슬롯 산정/입고 정책은 아직 구현하지 않았다.
- `BuildingInputSlotUtility.TryCalculate`는 건물 공통 입력 슬롯의 순수 계산이다. `RecipeBlob` 전체 대신 `ref BlobArray<RecipeIngredientBlob>` 재료 목록과 `ItemRegistryBlob`을 받는다. 기존 재료 요소의 품목/수량 형식을 재사용하며 제작 시간/출력물/건물 종류에는 의존하지 않는다. 최초 재료 등장 순서대로 동일 품목 슬롯을 연속 배치하고, 64비트로 중복 요구량을 합산한다. 반환값은 할당 없는 `FixedList512Bytes<BuildingInputSlotElement>`이며 ECS 상태에 직접 적용하지 않는다. 실패 사유는 `BuildingInputSlotCalculationErrorEnum`으로 반환한다.
- 0 이하 수량/최대 스택, None·미등록·인덱스 불일치 품목, `GameConstants.MaxStorageSlots` 초과는 오류 코드와 빈 결과로 반환한다. 임의 기본값, 슬롯 잘림, 부분 결과 적용은 없다. 빈 재료 목록은 성공/0슬롯이다. 입력 Blob 참조 유효성과 계산 실패 시 기존 상태 유지 등 호출자 처리는 연결 단계의 책임이다.
- 관련 테스트: `Phase5CrafterInputSlotTests`의 올림·중복 합산·품목 구분·상한·오류·Burst Job 호출 사례. 2026-09-29 공통 데이터/유틸리티 명칭과 재료 목록 입력 인자 변경까지 반영한 뒤 Unity 6000.4.11f1 연결 Editor에서 컴파일 `completed`, 오류 0, 어셈블리 최신성을 확인했다. 선택 EditMode 테스트 18/18 통과(실패/생략/Inconclusive 0). 검증 래퍼 결과는 `Logs/Codex/F037-building-input-slots-verification.json`에 보존했다. 최초 구현 시 실행한 원본 결과 `Logs/Codex/F037-stage1-tests.json`과 구분한다.
- 직접 Spawn/공사 완료의 공통 `BuildingLifecycleUtility`가 Crafter의 `CrafterStateDecision`을 비활성으로 초기화하고, `Storage(0)`·빈 Whitelist·빈 입력 슬롯 버퍼를 구성한다. 프리팹 경로도 런타임 구성을 초기화한다.
- 레시피 변경 Command는 `CrafterDecision`/`CrafterStateDecision`의 값·활성 상태를 수정하거나 두 컴포넌트를 입력 구성 검사 조건으로 요구하지 않는다. 뒤의 `CrafterDecisionSystem`이 변경된 상태를 기준으로 프레임 결정을 작성한다. 생성 시 컴포넌트 구성·초기화와 런타임 결정 작성의 책임을 구분한다.
- `CrafterRecipeCommandSystem`은 계산을 먼저 검증한 뒤 슬롯 수·품목 배정·필터를 함께 갱신한다. 레시피/아이템 설정 미게시 시 선택 요청을 유지하고, 잘못된 레시피/계산 실패는 기존 상태를 보존한 채 오류와 함께 요청을 소비한다. 레시피 해제는 설정 없이도 0슬롯/빈 필터로 처리한다. 잔여 입력은 원래 슬롯 구분을 유지하여 기존 출력 슬롯 뒤로 옮기고, 배출 완료 전 입고/제작 대기를 유지한다.
- 입고 Decision은 0슬롯을 거부하고, Reservation은 입력 슬롯 버퍼가 있는 건물에 해당 품목 슬롯만 배정한다. 같은 프레임의 예약 수량과 기존 보관량을 합산해 MaxStack을 적용한다. 버퍼가 없는 일반 창고의 기존 예약 규칙은 유지한다. 불변식 검사는 입력 슬롯 길이·보관 품목 대응을 확인하며, 미선택/무재료 Crafter의 정상적인 빈 입력 구성만 0슬롯으로 허용한다.
- 회귀 테스트는 `Phase5CrafterInputPipelineTests`에 있다. 개별 시스템 검증 15개에 더해 실제 정렬된 제작·물류 6단계 그룹을 사용하는 통합 4개가 있다. 직접 생성/공사 완료 × 전체 테스트 DB/선택 타입 테스트 DB의 네 경로에서 실제 아이템 요청→공급 창고→벨트→제작기 입고→선소비→생산→벨트→목적지 창고까지 실행한다. 레시피 미선택 입고 차단, 변경 시 잔여물 배출, 해제도 검증하고, 추가 수동 ECB 재생 없이 매 프레임 불변식 위반 0건을 확인한다.
- 3단계 결과: Unity 6000.4.11f1 재컴파일 `completed`, 오류 0, 런타임/테스트 어셈블리 최신성 확인. 입력 연결 EditMode 19/19 통과(실패/생략/Inconclusive 0). 로그는 `Logs/Codex/F037-stage3-integration-verification.json`, 상세 기록은 [F-037 검증 기록](../V2%20Quality%20Evaluation%20Plan/Results/F037-Verification.md). 실제 SubScene baked prefab, Play Mode, 전체 게임 시스템, 전체 EditMode는 실행하지 않았다. 기존 `RecipeBlob.TryFindIngredient` 및 제작 재료 집계(F-014)는 변경하지 않았다.
- 2단계 검증: Unity 재컴파일 `completed`, 오류 0·어셈블리 최신성 확인. 관련 EditMode 59개 사례가 각 최종 실행에서 통과했다(새 입력 연결 15, 기존 제작 8, 레시피 변경 2, 건물 생성 10, 공사 완료 8, 저장 소유권/결정/불변식 15, 기존 물류 통합 1). 최초 실행의 테스트 준비 오류는 설정 기반 MaxStack, 공간 인덱스 초기화, 실제 아이템 생성 계약에 맞춰 수정했다. 마지막 입력 연결 결과는 `Logs/Codex/F037-stage2-input-verification.json`; 이전 실패/중간 결과는 `Logs/Codex/F037-stage2-verification.json`과 구분한다. 전체 EditMode 및 Play Mode는 실행하지 않았다.

### Task 4.5~4.8 월드 생성 수명주기 (2026-09-27 갱신)

- `WorldGenerationConfigLoader`가 월드 시드·초기 크기·자원 설정을 검증하고 `WorldGenerationConfigLoadSystem`이 한 번 게시한다. 미정의 enum 숫자 값도 거부한다. `InitialChunkLoadBootstrapSystem`은 초기 N×N 청크를 요청 큐에 넣는다.
- `ChunkLoadCommandSystem`이 `GeneratedChunkTracker`의 완료 집합(`Map`)과 접수/반영 대기 집합(`Pending`)을 소유한다. 요청은 두 집합으로 중복 제거하고 `GeneratedChunkReadyElement`로 전달한다. 미처리 전달 버퍼를 프레임 시작에 지우지 않는다.
- `ResourceGenerationCommandSystem`은 설정별 프리팹을 업데이트당 한 번 조회한다. DB 지연, 필수 프리팹 누락, 소멸된 참조, LocalTransform 누락은 전체 청크 대기로 처리한다. 생성 가능한 자원 종류를 일부만 먼저 생성하지 않는다.
- 준비된 청크의 스폰 명령과 `GeneratedChunkCompletedElement`를 같은 `EndCommandEntityCommandBufferSystem`에 순서대로 기록하고 Ready를 소비한다. Command 끝에서 재생하여 같은 프레임 Decision 전에 실체화하지만, `ResourceSpatialIndex` 등록은 Synchronization에서 이루어진다. 즉시 Playback 우회 경로는 없다. Pending은 유지되며, 다음 Command에서 완료 알림을 소비한 뒤에만 Map으로 옮긴다. 정상적인 빈 청크도 완료 알림을 게시한다.
- 자원은 ECB 반영 프레임의 `ResourceSpatialSyncSystem`에서 등록되고 다음 프레임의 Miner가 조회한다. 공간 인덱스 소유자는 변경하지 않는다.
- 관련 회귀 테스트는 `Phase4WorldGenerationConfigTests`, `Phase4ChunkLifecycleTests`, `Phase4ResourceGenerationTests`, `Phase4ResourceAuthoringAndSpawnTests`다. 프레임 경계는 실제 GameSimulationGroup의 EndCommand·EndStateApply를 구분해 확인한다. 테스트용 ECS 프리팹 검증은 실제 SubScene 베이킹·시각 검증을 대체하지 않는다.
- 이번 개선 검증: Unity 6000.4.11f1 재컴파일 및 최신 런타임/테스트 어셈블리 확인 완료. 위 네 클래스의 EditMode 결과는 각각 9/9, 6/6, 7/7, 13/13 통과(총 35, Failed/Skipped/Inconclusive 0)다. Play Mode·실제 베이킹·시각·성능 측정은 수행하지 않았다.

### Task 4.9 바닥 생성 계약 (2026-09-27 갱신)

- `WorldGenerationConfig.json`의 `floor`가 레거시 바닥 파라미터·Grass/Dirt·전이 Sprite 목록을 보관하며, 자원과 단일 `worldSeed`를 공유한다. 별도 `FloorGenerationConfig.json`은 제거했다.
- `WorldGenerationConfigLoader`는 자원과 바닥 설정, Sprite Resources 참조를 모두 검증한 뒤 함께 게시한다. 바닥 파라미터는 `FloorGenerationSettings`, 바이옴과 변형은 동일 엔티티의 버퍼다.
- `FloorBiomeSampler`는 월드 셀 좌표의 `FloorTileSelection` 및 변형 Sprite 경로를 계산한다. 청크나 점유 상태를 소유하지 않으며 렌더링·메시·표시 수명주기는 Task 10B.6에 남는다.

아래 항목은 이 문서 최초 작성 시점의 범위 기록이며, 월드 생성은 위 갱신 절을 우선한다.

- Routing 갱신(2026-09-30): `SplitterDecisionSystem`/`MergerDecisionSystem`이 전달 후보를 만들고, `BeltDestinationReservationSystem`이 외부 진입을 승인하며, `RoutingApplySystem`이 이동·커서를 반영하고 결정을 소비한다. 최초 작성 당시의 Routing 런타임 미구현 설명은 현재 적용하지 않는다.
- 현재 건설 코어와 초기 청크 요청·자원 생성은 구현되어 있다. 전력·드론·연구·게임 UI 및 실제 바닥 청크 렌더링은 아직 구현되지 않았다. `BuildingTypeEnum`에 종류가 정의되어 있는 사실은 해당 기능의 런타임 구현을 뜻하지 않는다. 후속 계획은 상위 `Architecture V2 Tasks.md`를 참조한다.
- 최초 조사 당시 테스트 수와 결과는 현재 테스트 집합의 통과 증거가 아니다. 2026-09-30 품질 개선의 새 컴파일·선별 테스트 결과와 미검증 범위는 품질 개선 Tasks의 해당 이슈에 기록한다. 테스트 메서드가 존재한다는 사실은 현재 통과 증거가 아니다.
