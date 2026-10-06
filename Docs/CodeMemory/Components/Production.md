# 생산 컴포넌트: 채굴·제작·레시피·생산물

[전체 색인](README.md)

기준: **2026-10-03 현재 C# 정적 소스**. 이 문서는 현재 구현 경로를 정리하며, Unity 실행·컴파일·테스트·장면 연결은 이번 문서화 작업에서 검증하지 않았다. `CrafterStatusEnum` 같은 enum과 일반 C# 설정 목록은 독립 ECS 컴포넌트에 포함하지 않는다.

2026-10-04 공사 운송 제거에 따라 Product 버퍼의 공사 수령 Reader 설명을 갱신했다. 일반 생산·출고·철거 경로는 유지한다. 이번 변경의 실행 결과와 한계는 [공사 운송 제거 검증 기록](../../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/ConstructionTransportRemoval-Verification.md)을 따른다.

생산의 공통 흐름은 `Decision → Execution의 ProductResult 기록 → StateApply의 아이템 생성 예약 → EndStateApply의 실제 아이템/출력 버퍼 반영 → 이후 틱의 출고 Decision`이다. 제작은 여기에 `Command의 레시피·입력 슬롯 설정`과 `Execution의 재료 선소비`가 추가된다. 생산 건물의 런타임 컴포넌트는 직접 스폰과 공사 완료가 공유하는 `BuildingLifecycleUtility.SpawnBuilding → AttachTypeSpecificComponents`에서 EndStateApply ECB에 기록한다.

2026-10-04 철거 승인 상태와 앞단 동작 중단 계약을 반영했다. 아래 현재 설명의 컴파일·핵심 회귀 실행 근거는 [철거 상태·동작 중단 검증](../../architecture%20v2%20plan/V2%20Quality%20Evaluation%20Plan/Results/BuildingDemolitionStop-Verification.md)을 따른다. 이전 조사 당시의 정적 분석과 이번 실행 검증의 범위를 구분한다.

## MinerState

- **종류·부착 대상·목적:** 일반 `IComponentData`; 완공된 Miner 엔티티의 `MiningSpeed`와 미처리 작업량 `Progress`를 보관한다. [정의](../../../Assets/Scripts/Components/Production/Mining/MinerComponents.cs).
- **생성·초기화:** 공통 건물 생성 경로가 `MiningSpeed = speed > 0 ? speed : 1`, `Progress = 0`으로 추가한다. 같은 엔티티에 `MinerDecision`, `ProductResult`, `ProductItemElement`, `BuildingItemOutputDecision`도 구성한다. 실제 부착은 EndStateApply 재생 시점이다. [초기화](../../../Assets/Scripts/Common/BuildingLifecycleUtility.cs).
- **읽기·쓰기와 단계:** `MinerDecisionSystem`(Decision)의 쿼리·Job 입력에 포함되며, `MinerExecutionSystem`(Execution)이 활성 `MinerDecision`을 가진 채굴기의 `Progress`를 직접 갱신한다. Decision Job은 이 상태 값을 변경하지 않는다. [판정](../../../Assets/Scripts/Systems/2_Decision/MinerDecisionSystem.cs), [실행](../../../Assets/Scripts/Systems/4_Execution/MinerExecutionSystem.cs).
- **처리:** 대상 자원과 미소비 결과를 재확인한 뒤 `MiningSpeed × min(DeltaTime, MaxSimulationDeltaTime)`를 더한다. `Progress >= 1`이면 **한 틱에 최대 한 번** 1을 차감하고 슬롯 0의 `ProductResult` 한 개를 기록한다. 반복문으로 누적 작업량 전체를 처리하지 않으므로 차감 뒤에도 `Progress >= 1`이 남을 수 있다.
- **결합 계약·대기:** 출력이 다른 품목이거나 1스택 한도에 도달했거나 자원이 없으면 Decision이 비활성화되어 기존 진행도를 보존한다. 유한 자원은 완료 한 번에 `ResourceNode.Amount`를 1 줄이고 고갈 자원 삭제만 EndStateApply ECB에 기록한다. 무한 자원 설정이어도 판정·실행의 `Amount > 0` 조건은 유지한다.
- **종료:** 개별 사이클에서는 컴포넌트를 제거하지 않는다. 채굴기 철거로 건물 엔티티가 EndStateApply에서 삭제되거나 World가 종료되면 함께 사라진다. 철거와 겹친 완료 결과의 폐기 정책은 [ProductResult](#productresult)를 따른다.

## MinerDecision

- **종류·부착 대상·목적:** `IComponentData, IEnableableComponent`; Miner 엔티티의 이번 판정 결과 `CanMine`, `TargetResource`를 전달한다. 요청 엔티티가 아니다. [정의](../../../Assets/Scripts/Components/Production/Mining/MinerDecisions.cs).
- **생성·초기화:** 공통 건물 생성 경로에서 기본값으로 추가하고 비활성화한다. EndStateApply 이후 다음 Decision부터 판정 대상이 된다. [생성](../../../Assets/Scripts/Common/BuildingLifecycleUtility.cs).
- **Producer·단계:** `MinerDecisionSystem`(Decision)은 `IgnoreComponentEnabledState` 쿼리로 비활성 Miner도 다시 검사한다. `ResourceSpatialIndexFence`의 Writer 의존성을 받고 Reader 핸들을 등록하며, 회전 적용 Footprint의 좌하단부터 y/x 순으로 처음 발견한 `Amount > 0` 자원을 선택한다. [판정 시스템](../../../Assets/Scripts/Systems/2_Decision/MinerDecisionSystem.cs).
- **활성 조건:** 유효 자원이 있고 `ProductResult`가 비어 있으며, 모든 출력 실물이 선택 자원과 같은 품목이고 출력 개수가 품목별 MaxStack보다 적어야 한다. Item 설정 버퍼를 사용할 수 없으면 판정의 기본 한도는 50이다. 실패 시 `CanMine = false`와 비활성 상태를 기록하며, 자원을 찾았다면 `TargetResource` 값은 남긴다.
- **Consumer·반영:** `MinerExecutionSystem`(Execution)은 활성 결정만 조회하고 `CanMine`, 대상 존재, 잔량, 미소비 결과를 다시 확인한다. 자원이 같은 틱에 다른 채굴기로 고갈된 경우도 여기서 진행을 중단한다. 실행은 단일 워커 Job이며 `MinerState`·`ResourceNode`·`ProductResult`를 직접 변경한다. [실행 시스템](../../../Assets/Scripts/Systems/4_Execution/MinerExecutionSystem.cs).
- **반복·종료:** Execution은 이 결정을 비활성화하지 않는다. 다음 Decision이 값과 enable 상태를 다시 기록한다. 공간 인덱스/그 Fence가 없으면 Decision 시스템 자체가 반환하므로 그 업데이트에서는 새 판정이 나오지 않는다. 부착 건물 삭제 또는 World 종료 시 함께 소멸한다.

## CrafterState

- **종류·부착 대상·목적:** 일반 `IComponentData`; Crafter 엔티티의 선택/활성 레시피 ID, `Progress`, `Speed`, `Status`, 재료 선소비 여부인 `IsCraftingActive`를 보관한다. `Status`는 enum 값이고 enable 상태가 아니다. [정의](../../../Assets/Scripts/Components/Production/Crafting/CrafterComponents.cs).
- **생성·초기화:** 공통 생성 시 두 ID는 0, 진행도 0, `Speed = speed > 0 ? speed : 1`, `Status = NoRecipe`, `IsCraftingActive = false`다. 입력은 `Storage(0)`·빈 Whitelist·빈 `BuildingInputSlotElement`/`StoredItemElement`로 시작한다. [초기화](../../../Assets/Scripts/Common/BuildingLifecycleUtility.cs).
- **Command 쓰기:** `CrafterRecipeCommandSystem`은 새 레시피의 입력 슬롯 계산이 성공한 뒤 진행도/활성 제작을 초기화하고 두 ID를 같은 값으로 쓴다. 남은 입력 실물은 출력 버퍼로 옮기며, 출력 실물이 있으면 `WaitingForByproductOutput`, 없으면 선택 여부에 따라 `Idle`/`NoRecipe`를 기록한다. 현재 런타임은 `ActiveRecipeId` 비교로 레시피 변경을 자동 감지하지 않는다. [변경 처리](../../../Assets/Scripts/Systems/1_Command/CrafterRecipeCommandSystem.cs).
- **Decision 읽기·Execution 쓰기:** `CrafterDecisionSystem`은 상태를 읽어 실행/상태 전이 결정을 분리한다. `CrafterExecutionSystem`은 새 작업을 시작할 때 재료를 Stored 버퍼에서 먼저 제거하고 해당 실물의 `DestroyItemRequest`를 활성화한 뒤 제작을 활성화한다. 진행은 `min(DeltaTime, MaxSimulationDeltaTime) × Speed / max(0.01, CraftTime)`만큼 누적하고 1로 제한한다. [판정](../../../Assets/Scripts/Systems/2_Decision/CrafterDecisionSystem.cs), [실행](../../../Assets/Scripts/Systems/4_Execution/CrafterExecutionSystem.cs).
- **StateApply·타이밍:** `CrafterStateApplySystem`은 앞선 Decision의 `NextStatus`를 복사한다. Execution이 처음 진행도 1에 도달한 틱에는 그 Decision의 `CanProduceOutput`이 false이므로 이후 Decision에서 출력 가능 판정을 받는다. 실제 결과를 기록한 Execution은 `IsCraftingActive = false`, `Progress = 0`으로 초기화하며 Status 자체는 쓰지 않는다. 따라서 Status는 같은 틱 Execution 이후 상태를 새로 계산한 값이 아니다. [상태 반영](../../../Assets/Scripts/Systems/5_StateApply/CrafterStateApplySystem.cs).
- **다른 Reader·종료:** `BuildingItemInputDecisionSystem`은 `WaitingForByproductOutput` 동안 입고를 막는다. 출력이 비어도 Status 해제는 StateApply이므로 해당 틱 입고 판정은 기존 상태를 볼 수 있다. 개발용 `WorldInvariantValidationSystem`은 선택 레시피로 0입력 슬롯의 유효성을 확인한다. 레시피 변경은 선소비 재료를 복원하지 않고, 건물 삭제/World 종료로 상태가 소멸한다. [입고](../../../Assets/Scripts/Systems/2_Decision/BuildingItemInputDecisionSystem.cs), [검증](../../../Assets/Scripts/Validation/WorldInvariantValidationSystem.cs).

## CrafterDecision

- **종류·부착 대상·목적:** `IComponentData, IEnableableComponent`; Crafter 엔티티에 `CanCraft`, `CanStartCraft`, `CanAdvance`, `CanProduceOutput`, `RecipeId`, `RecipeIndex`를 보관하여 Execution에 전달한다. [정의](../../../Assets/Scripts/Components/Production/Crafting/CrafterDecisions.cs).
- **생성·Producer:** 공통 건물 생성 시 비활성화한다. `CrafterDecisionSystem`(Decision)은 비활성 상태를 무시하는 쿼리로 매번 다시 계산한다. Recipe Registry 및 세 설정 버퍼가 없으면 시스템은 반환한다. [생성](../../../Assets/Scripts/Common/BuildingLifecycleUtility.cs), [판정](../../../Assets/Scripts/Systems/2_Decision/CrafterDecisionSystem.cs).
- **판정 분기:** 잔여물 배출 대기·무효/미선택 레시피·미소비 `ProductResult`는 제작을 막는다. 미착수 시 각 레시피 재료 항목별로 Stored 실물 수를 세어 필요한 수량이 있는지 검사한다. 진행 중이고 Progress가 1 미만이면 진행을 승인한다. 이미 1 이상이면 레시피의 모든 유효 출력 슬롯이 `현재 개수 + 출력량 <= 품목 MaxStack`인지 확인하여 전체 결과의 출력을 함께 승인한다. 신규 착수와 진행은 출력 공간 부족만으로 차단하지 않는다.
- **Consumer·실제 사용 필드:** `CrafterExecutionSystem`(Execution)은 활성 결정만 조회하고 `CanStartCraft`, `CanAdvance`, `CanProduceOutput`으로 분기한다. 레시피는 `CrafterState.SelectedRecipeId`로 다시 검색한다. 현재 실행 코드는 `CanCraft`나 저장된 `RecipeId/RecipeIndex`를 직접 참조하지 않는다. [실행](../../../Assets/Scripts/Systems/4_Execution/CrafterExecutionSystem.cs).
- **enable 의미·소비:** 완료 후 출력 공간이 없을 때는 `CanCraft = false`여도 결정의 enable 상태를 true로 남긴다. 이때 세 실행 동작 플래그는 false다. Execution에서 결정을 비활성화하지 않으며 다음 Decision이 덮어쓴다. 결정을 프레임 끝에 반드시 비활성화하는 일회성 요청으로 해석하지 않는다.
- **결합·종료:** 상태 표시는 별도 [CrafterStateDecision](#crafterstatedecision)이 담당하고, 실물 생성은 [ProductResult](#productresult)를 거친다. 이 결정만으로 재료 삭제나 아이템 생성이 확정되지 않는다. 부착 건물 삭제 또는 World 종료 시 소멸한다.

## CrafterStateDecision

- **종류·부착 대상·목적:** `IComponentData, IEnableableComponent`; Crafter 엔티티의 표시/행동 상태 전이 후보 `NextStatus`를 보관한다. [정의](../../../Assets/Scripts/Components/Production/Crafting/CrafterDecisions.cs).
- **생성·초기화:** 공통 생성 경로가 `NextStatus = NoRecipe`로 추가하고 비활성화한다. `CrafterDecision`과 함께 EndStateApply 이후부터 존재한다. [초기화](../../../Assets/Scripts/Common/BuildingLifecycleUtility.cs).
- **Producer·단계:** `CrafterDecisionSystem`(Decision)이 선택 레시피, 재료, 진행, 잔여물 및 출력 공간에 따라 `NoRecipe`, `Idle`, `Crafting`, `WaitingForInput`, `WaitingForOutput`, `WaitingForByproductOutput`을 기록하고 활성화한다. 실행 불가 분기도 상태 전이 결정은 활성화한다. [판정](../../../Assets/Scripts/Systems/2_Decision/CrafterDecisionSystem.cs).
- **Consumer·반영:** `CrafterStateApplySystem`(StateApply)의 병렬 Job이 활성 결정의 `NextStatus`를 `CrafterState.Status`에 직접 복사하고 즉시 비활성화한다. 이 비구조 변경은 EndStateApply ECB까지 기다리지 않는다. [반영](../../../Assets/Scripts/Systems/5_StateApply/CrafterStateApplySystem.cs).
- **결합 계약:** Command의 레시피 변경은 `CrafterState.Status`를 직접 설정한다. 이 컴포넌트는 Decision 이후의 전이를 전달하며, 실행 동작 승인이나 완료 결과 생성은 `CrafterDecision`/`ProductResult`가 맡는다. `CrafterStateApplySystem`은 Execution 결과를 재평가하지 않는다.
- **반복·종료:** 적용 뒤 비활성 상태로 엔티티에 남아 다음 Decision에서 재사용된다. 부착 건물 삭제 또는 World 종료 시 함께 사라진다.

## ChangeCrafterRecipeRequest

- **종류·부착 대상·목적:** `IRequestComponent`를 구현한 일반 일회성 요청 컴포넌트; 독립 요청 엔티티에 `TargetCrafter`, `NewRecipeId`를 넣어 레시피 변경·해제를 요청한다. [정의](../../../Assets/Scripts/Components/Production/Crafting/CrafterRequests.cs).
- **생성 경로:** 요청의 데이터 생성자는 제공하지만 현재 `Assets/Scripts`에서는 이 요청을 실제 생성하는 게임 입력/UI Producer를 확인하지 못했다. 요청이 Command 전에 존재하면 `CrafterRecipeCommandSystem`이 처리한다. 생성자 정의 자체를 연결된 게임 입력 기능으로 해석하지 않는다.
- **Consumer·준비 검사:** `CrafterRecipeCommandSystem`(Command)이 의존성을 완료한 뒤 메인 스레드에서 처리한다. 음수 ID는 0으로 바꾼다. 대상이 유효 Crafter여야 하고 Storage·필터·입력 슬롯·Stored/Product 버퍼가 필요하다. 양수 ID인데 Item/Recipe 설정이 준비되지 않았다면 **요청을 유지**한다. 해제(0)는 설정이 없어도 처리한다. [소비 시스템](../../../Assets/Scripts/Systems/1_Command/CrafterRecipeCommandSystem.cs).
- **성공 처리:** 새 슬롯을 먼저 계산하고, 성공한 경우에만 진행/활성 제작을 초기화한다. 남은 Stored 실물을 기존 최대 출력 슬롯보다 뒤의 `baseSlot + 기존 입력 SlotIndex`로 옮기고 Stored를 비운다. 이어 `Storage.SlotCount`, 품목별 입력 슬롯, Whitelist, 두 레시피 ID와 Status를 함께 갱신한다. 같은 ID 요청도 이 처리 절차를 거치며 별도 무변경 분기는 없다. 슬롯 계산은 같은 품목 요구량을 합산한 `ceil(요구량/MaxStack)`을 사용한다. [슬롯 계산](../../../Assets/Scripts/Common/BuildingInputSlotUtility.cs).
- **실패·소비·ECB:** 잘못된 대상은 변경 없이 소비한다. 입력 구성 누락·알 수 없는 레시피·슬롯 계산 실패는 오류를 기록하고 기존 진행/슬롯/버퍼를 보존한 채 소비한다. 기존 컴포넌트/버퍼 변경은 Command에서 직접 적용하고 요청 삭제는 EndCommand ECB에 기록한다. 해당 ECB 시스템이 없는 독립 실행 경로에서는 임시 ECB를 즉시 재생한다.
- **결합·종료:** 잔여 실물은 동일 Crafter가 계속 소유하므로 Product 버퍼 이관 자체는 소유권 이전 요청을 만들지 않는다. 이미 선소비한 재료는 되돌리지 않는다. 처리 완료/거부 요청은 삭제되고, 설정 대기 요청은 이후 Command까지 살아 있으며 World 종료 시 사라진다.

## RecipeRegistry

- **종류·부착 대상·목적:** 필드 없는 일반 `IComponentData`; World의 단일 레시피 설정 엔티티를 식별한다. 같은 엔티티에 세 버퍼 `RecipeConfigElement`, `RecipeIngredientElement`, `RecipeOutputElement`가 붙는다. [정의](../../../Assets/Scripts/Components/Production/Crafting/RecipeConfigComponents.cs).
- **생성·초기화:** `RecipeInitSystem`(Initialization)이 `RecipeConfigLoader.LoadFromResources("Config/CrafterRecipeConfig")` 또는 공개 초기화 API의 JSON 입력을 읽고 `PublishConfig`로 엔티티와 세 버퍼를 직접 생성한다. 설정이 없거나 파싱 결과의 레시피 목록이 없거나 비어 있으면 하드코딩 기본 레시피 경로가 있다. [초기화](../../../Assets/Scripts/Systems/0_Initialization/RecipeInitSystem.cs), [로드·게시](../../../Assets/Scripts/Config/RecipeConfigLoader.cs).
- **사전 등록·실패:** 자동 Init은 기존 단일 Registry에 세 버퍼가 있으면 재사용하고 자신을 비활성화한다. 버퍼가 없으면 예외다. 공개 초기화/게시 API는 기존 Registry가 있으면 입력 처리 또는 새 엔티티 생성 전에 예외로 거부한다. 게시 도중 실패하면 그 호출에서 생성한 미완성 엔티티만 삭제한다.
- **Reader·단계:** `CrafterRecipeCommandSystem`(Command), `CrafterDecisionSystem`(Decision), `CrafterExecutionSystem`(Execution)이 Registry에서 설정 엔티티를 찾고 필요한 버퍼를 읽는다. 개발용 `WorldInvariantValidationSystem`(Synchronization OrderLast)도 0입력 슬롯의 유효성을 확인할 때 조회한다. Reader Job은 읽기 전용 BufferLookup과 `state.Dependency`를 사용한다.
- **수명·소유:** 설정 엔티티와 버퍼는 ECS World가 소유한다. 게시 후 World 종료까지 읽기 전용이며 런타임 교체·삭제·재등록은 지원하지 않는다. Init 시스템이 비활성화되거나 제거되어도 설정 엔티티를 따로 삭제하지 않는다. 별도 Persistent NativeContainer를 호출자가 Dispose하는 구조가 아니다.
- **탐색 연결:** [RecipeConfigLookupUtility](../../../Assets/Scripts/Common/RecipeConfigLookupUtility.cs)는 같은 버퍼의 입력 순서상 첫 ID 일치 또는 첫 주생산품 일치를 찾는 함수다. 별도 레지스트리 캐시나 갱신 시스템을 만들지 않는다.

## RecipeConfigElement

- **종류·부착 대상·목적:** `IBufferElementData`, 내부 버퍼 용량 0; Recipe Registry 엔티티의 레시피 목록 요소다. `Id`, `CraftTime`, `ConditionFlags`, 재료/출력 버퍼 내 시작 인덱스와 개수를 보관한다. [정의](../../../Assets/Scripts/Components/Production/Crafting/RecipeConfigComponents.cs).
- **생성·초기화:** `RecipeConfigLoader`가 JSON 순서로 일반 C# 목록을 만든 뒤 `PublishConfig`에서 ECS 버퍼로 복사한다. 양수가 아닌 제작 시간은 파싱 시 1초로 바꾼다. 게시 전 시작/개수 범위가 각 재료·출력 목록 범위를 벗어나지 않는지 확인한다. [파싱·게시](../../../Assets/Scripts/Config/RecipeConfigLoader.cs).
- **Reader·단계:** `CrafterRecipeCommandSystem`은 ID와 재료 범위로 입력 슬롯을 계산한다. `CrafterDecisionSystem`은 재료·출력 범위를 판정하고 `CrafterExecutionSystem`은 선소비·진행·결과 기록에 사용한다. 개발용 검증 시스템은 선택 레시피의 `IngredientCount == 0`인지 확인한다. [Command](../../../Assets/Scripts/Systems/1_Command/CrafterRecipeCommandSystem.cs), [Decision](../../../Assets/Scripts/Systems/2_Decision/CrafterDecisionSystem.cs), [Execution](../../../Assets/Scripts/Systems/4_Execution/CrafterExecutionSystem.cs).
- **현재 필드 의미:** `ConditionFlags`는 현재 JSON 로더가 0으로 기록하며 제작 시스템에서 조건 판정에 사용하지 않는다. 입력 JSON의 `conditions` 목록 존재만으로 실행 조건이 구현된 것은 아니다. `CraftTime`은 Execution에서 다시 최소 0.01초로 제한하여 사용한다.
- **조회·결합:** `TryGetPrimaryOutput`은 첫 `IsByproduct == false` 출력을 찾고 없으면 첫 출력을 사용한다. `TryFindIngredient`는 첫 동일 품목의 수량을 반환한다. 실제 실행의 출력 슬롯은 해당 레시피 출력 범위 내 순번이다. ID 검색은 첫 일치이며 버퍼 인덱스와 레시피 ID를 혼동하지 않는다. [조회 유틸리티](../../../Assets/Scripts/Common/RecipeConfigLookupUtility.cs).
- **수명·소비:** 일회성 결과가 아니라 게시 후 불변 설정이다. 제작 한 번이 끝나도 항목을 삭제하거나 수량을 변경하지 않는다. 같은 Registry 엔티티의 재료/출력 버퍼 범위를 계속 참조하며 World 종료 때 ECS가 해제한다.

## RecipeIngredientElement

- **종류·부착 대상·목적:** `IBufferElementData`, 내부 버퍼 용량 0; Recipe Registry 엔티티에서 모든 레시피의 재료 항목을 연속 보관한다. `ItemType`, `Amount`는 실물 엔티티 참조가 아닌 설정값이다. 각 레시피의 `IngredientStart/IngredientCount`로 범위를 구분한다. [정의](../../../Assets/Scripts/Components/Production/Crafting/RecipeConfigComponents.cs).
- **생성·초기화:** `RecipeConfigLoader`가 JSON 품목 문자열을 enum으로 변환하고 양수가 아닌 수량은 1로 바꿔 입력 순서대로 게시한다. 게시 후 버퍼는 읽기 전용이다. [로드·게시](../../../Assets/Scripts/Config/RecipeConfigLoader.cs).
- **Command Reader:** `CrafterRecipeCommandSystem → BuildingInputSlotUtility.TryCalculate`가 같은 품목 요구량을 합산한다. 등록 품목·양수 수량·유효 MaxStack·전체 슬롯 상한을 확인하고, 품목 최초 등장 순서로 필요한 수의 전용 입력 슬롯을 계산한다. 계산 실패 시 레시피 변경 전 상태를 유지한다. [슬롯 계산](../../../Assets/Scripts/Common/BuildingInputSlotUtility.cs).
- **Decision Reader:** `CrafterDecisionSystem`은 각 재료 항목을 순서대로 읽고, 해당 품목의 `StoredItemElement` 개수가 그 항목의 `Amount` 이상인지 검사한다. 이 판정은 입력 슬롯 계산의 품목별 합산과 별개이며 설정 항목별로 수행한다. [판정](../../../Assets/Scripts/Systems/2_Decision/CrafterDecisionSystem.cs).
- **Execution Reader·실물 소비:** `CrafterExecutionSystem`은 착수 시 각 항목의 수량만큼 Stored 버퍼를 뒤에서 순회하여 같은 품목 실물을 제거한다. 제거한 실물에 `DestroyItemRequest`가 있으면 활성화하고, `ItemLifecycleApplySystem`이 EndStateApply 삭제를 기록한다. 설정의 `Amount` 자체를 줄이지 않는다. [선소비](../../../Assets/Scripts/Systems/4_Execution/CrafterExecutionSystem.cs), [실물 삭제](../../../Assets/Scripts/Systems/5_StateApply/ItemLifecycleApplySystem.cs).
- **수명·결합:** 레시피 변경은 이 전역 버퍼를 수정하지 않고 Crafter의 입력 구성을 바꾼다. Init 제거 후에도 살아 있으며 World 종료 시 ECS가 해제한다. 소유 실물의 수명은 [StoredItemElement](ItemsAndStorage.md#storeditemelement)와 아이템 삭제 요청의 수명으로 따로 추적한다.

## RecipeOutputElement

- **종류·부착 대상·목적:** `IBufferElementData`, 내부 버퍼 용량 0; Recipe Registry 엔티티에 `ItemType`, `Amount`, `IsByproduct`로 레시피별 출력 설정을 연속 보관한다. 각 레시피의 `OutputStart/OutputCount` 범위를 사용한다. [정의](../../../Assets/Scripts/Components/Production/Crafting/RecipeConfigComponents.cs).
- **생성·초기화:** `RecipeConfigLoader`는 각 레시피의 첫 요소를 주생산품(`IsByproduct = false`), 뒤의 요소를 JSON 순서의 부산품(true)으로 만든다. 양수가 아닌 수량은 1로 바꾼다. 게시 후 불변이며 World 종료까지 유지한다. [파싱·게시](../../../Assets/Scripts/Config/RecipeConfigLoader.cs).
- **Decision Reader:** `CrafterDecisionSystem`은 유효 품목/양수 출력만 대상으로 `outIdx` 슬롯의 현재 Product 실물 수와 출력 수량을 더하여 MaxStack을 검사한다. 한 출력이라도 공간이 부족하면 완료된 제작의 전체 출력 승인을 보류한다. Item 설정 버퍼를 사용할 수 없을 때 품목 한도 판정은 50을 사용한다. [판정](../../../Assets/Scripts/Systems/2_Decision/CrafterDecisionSystem.cs).
- **Execution Reader:** `CrafterExecutionSystem`은 완료·출력 승인 시 같은 범위를 순회하여 유효 출력마다 `ProductResult(ItemType, Amount, slotIndex: outIdx)`를 기록한다. 실행의 슬롯은 `IsByproduct` 값을 재분류해서 정하지 않고 범위 내 순번을 그대로 사용한다. [실행](../../../Assets/Scripts/Systems/4_Execution/CrafterExecutionSystem.cs).
- **조회 결합:** `RecipeConfigElement.TryGetPrimaryOutput`과 `RecipeConfigLookupUtility.TryFindRecipeIndexByPrimaryOutput`은 `IsByproduct`를 주생산품 탐색에 사용한다. 실제 생성·보관·출고에서는 슬롯 0 우선 정책으로 연결된다. [조회](../../../Assets/Scripts/Common/RecipeConfigLookupUtility.cs), [출고](../../../Assets/Scripts/Systems/2_Decision/ProductItemOutputDecisionSystem.cs).
- **소비·종료:** 제작이 끝나도 설정 요소를 소비하지 않는다. 출력 예정 수량은 `ProductResult`, 실제 출력 대기 아이템은 `ProductItemElement`에 별도로 표현된다. 설정 버퍼 메모리는 Registry 엔티티/World 수명에 속한다.

## ProductResult

- **종류·부착 대상·목적:** `IBufferElementData`, 내부 버퍼 용량 4; 생산 건물에 부착된 임시 결과 버퍼다. `ItemType`, `Count`, `SlotIndex`는 아직 생성되지 않은 생산물의 종류·실물 개수·출력 슬롯을 표현한다. [정의](../../../Assets/Scripts/Components/Production/ProductResults.cs).
- **생성·Producer:** 공통 건물 생성 경로가 Miner/Crafter에 빈 버퍼를 추가한다. `MinerExecutionSystem`은 채굴 완료 때 슬롯 0에 개수 1을, `CrafterExecutionSystem`은 제작 출력 승인 때 각 출력 순번에 레시피 수량을 기록한다. 두 경로 모두 버퍼에는 직접 쓰고 실제 아이템은 만들지 않는다. [생성](../../../Assets/Scripts/Common/BuildingLifecycleUtility.cs), [채굴](../../../Assets/Scripts/Systems/4_Execution/MinerExecutionSystem.cs), [제작](../../../Assets/Scripts/Systems/4_Execution/CrafterExecutionSystem.cs).
- **Consumer·처리 순서:** `ItemLifecycleApplySystem`(StateApply)의 `ProductResultApplyJob`이 먼저 처리하고 그 시스템 안에서 일반 Spawn 요청, Destroy 요청 Job이 뒤따른다. 같은 엔티티에 `ProductItemElement`가 있어야 결과 쿼리에 포함된다. 유효한 결과의 `Count`만큼 프리팹 아이템 생성과 생산자 소유권 초기화, Product 버퍼 append를 EndStateApply ECB에 기록한다. [소비](../../../Assets/Scripts/Systems/5_StateApply/ItemLifecycleApplySystem.cs).
- **논리 소비와 실제 반영:** 소비 Job은 결과 버퍼를 즉시 Clear한다. 생성된 아이템 엔티티와 Product 참조가 실제로 존재하는 시점은 EndStateApply 재생 뒤다. 같은 틱의 출고 Decision은 이미 앞 단계에서 끝났으므로 그 생산물의 정상 출고 판정은 이후 틱에 가능하다.
- **실패·철거:** None 품목/0 이하 Count는 건너뛰고 Clear한다. 프리팹 누락은 중단 오류를 기록하고 해당 결과를 생성하지 않는다. Command에서 철거가 승인되면 이전 ProductResult를 먼저 Clear하고 Miner/Crafter Decision을 비활성화해 해당 틱의 재료·광물 소비, 진행, 새 결과 기록을 막는다. ItemLifecycleApplySystem에는 철거 요청/상태 분기가 없다. 앞선 틱에 이미 소비한 재료의 별도 보상이나 현재 틱 전체 rollback은 추가하지 않았다.
- **반복·끝:** 미소비 결과가 있으면 Miner/Crafter Decision이 추가 생산을 막고 Execution에도 중복 기록 방어가 있다. 정상 처리 뒤 빈 버퍼로 재사용한다. 생산 건물 삭제 또는 World 종료 시 버퍼도 사라진다. `ProductItemElement`의 실제 보관 실물과 구분한다.

## ProductItemElement

- **종류·부착 대상·목적:** `IBufferElementData`, 내부 버퍼 용량 8; Miner/Crafter 생산 건물의 실제 출력 대기 실물을 `ItemEntity`, 캐시한 `ItemType`, `SlotIndex`로 보관한다. 입력/보관 재료인 Stored 버퍼와 별개다. [정의](../../../Assets/Scripts/Components/Production/ProductComponents.cs).
- **생성·추가 경로:** 공통 건물 생성에서 빈 버퍼를 붙인다. `ItemLifecycleApplySystem`은 `ProductResult` 또는 목적지가 Product인 `SpawnItemRequest`를 처리하여 아이템 생성과 append를 EndStateApply ECB에 기록한다. `CrafterRecipeCommandSystem`은 레시피 변경 시 잔여 Stored 실물 참조를 이 버퍼에 직접 옮긴다. [생성](../../../Assets/Scripts/Common/BuildingLifecycleUtility.cs), [아이템 추가](../../../Assets/Scripts/Systems/5_StateApply/ItemLifecycleApplySystem.cs), [잔여물 이관](../../../Assets/Scripts/Systems/1_Command/CrafterRecipeCommandSystem.cs).
- **Decision Reader·역할 분리:** `MinerDecisionSystem`/`CrafterDecisionSystem`은 생산 여유를 확인하고, `ProductItemOutputDecisionSystem`은 건물 둘레의 외향 벨트와 입구 공간을 찾아 슬롯 0의 첫 실물을 우선 선택하며 없으면 버퍼 첫 실물을 선택한다. `StorageItemOutputDecisionSystem`은 이 버퍼가 있는 엔티티를 쿼리에서 제외한다. Product 버퍼가 존재하면 Crafter의 입력 Stored 재료를 일반 창고 출고로 내보내지 않는다. [생산물 출고](../../../Assets/Scripts/Systems/2_Decision/ProductItemOutputDecisionSystem.cs), [일반 창고 쿼리](../../../Assets/Scripts/Systems/2_Decision/StorageItemOutputDecisionSystem.cs).
- **예약·출고 소비:** 출고 후보는 `BuildingItemOutputDecision → BeltDestinationReservationSystem`을 거친다. `BuildingItemStorageApplySystem`(StateApply)은 승인된 실물을 Product 버퍼에서 직접 제거하고 위치·벨트 상태를 갱신한 뒤 `TransferOwnershipRequest(Entity.Null)`를 활성화한다. 아이템의 Direction은 이미 부착된 경우에만 갱신한다. `ItemOwnershipApplySystem`이 소유권/렌더 상태를 반영한다. 유효 Destroy 요청이 있는 실물은 출고를 취소한다. [예약](../../../Assets/Scripts/Systems/3_Reservation/BeltDestinationReservationSystem.cs), [출고 적용](../../../Assets/Scripts/Systems/5_StateApply/BuildingItemStorageApplySystem.cs), [소유권](../../../Assets/Scripts/Systems/5_StateApply/ItemOwnershipApplySystem.cs).
- **다른 Reader·정합성:** 철거 승인 Command가 Stored/Product에 남은 실물의 이전 Transfer 요청을 비활성화하며 건물 Lifecycle이 기존 실물을 반환한다. WorldInvariantValidationSystem은 실물 참조·소유자·슬롯/MaxStack·월드 공간 관계를 검사한다. 삭제된 요청 목록 조회 유틸리티는 사용하지 않는다. [철거 승인](../../../Assets/Scripts/Systems/1_Command/BuildingDemolitionCommandSystem.cs), [검증](../../../Assets/Scripts/Validation/WorldInvariantValidationSystem.cs).
- **종료·철거:** `BuildingLifecycleApplySystem`은 입출고 반영 뒤 철거 대상 버퍼에 남은 유효 실물을 건물 위치의 월드 아이템으로 반환하도록 EndStateApply ECB에 기록한다. 유효 Destroy 대상은 반환에서 제외한다. 정상 출고로 이미 버퍼를 떠난 실물은 출고를 유지한다. 건물 삭제로 버퍼 자체가 소멸해도 반환된 실물은 별도 엔티티로 살아 있으며 World 종료 시 함께 정리된다. [철거 적용](../../../Assets/Scripts/Systems/5_StateApply/BuildingLifecycleApplySystem.cs).
