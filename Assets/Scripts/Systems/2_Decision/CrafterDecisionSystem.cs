using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 제작기(Crafter)의 제작 조건(선택된 레시피, 입력 재료 완비 여부, 출력 버퍼 여유 공간)을 판정하는 의사결정 시스템.
/// 
/// [책임]
/// - DecisionGroup(Phase 2)에서 실행.
/// - RecipeRegistry Blob에서 선택된 레시피(SelectedRecipeId)를 조회.
/// - [제작/출력 판정]:
///   1. 미착수 상태(!IsCraftingActive): StoredItemElement에 레시피 필요 재료가 모두 구비되어 있는지 확인하여 CanStartCraft 결정.
///   2. 진행 중 상태(IsCraftingActive && Progress < 1.0f): CanAdvance = true 및 NextStatus = Crafting 결정.
///   3. 완료 상태(IsCraftingActive && Progress >= 1.0f): ProductItemElement에 주생산품/부산품 수용 공간(1스택 한도)이 있는지 검사하여
///      공간이 있으면 CanProduceOutput = true, 만석이면 NextStatus = WaitingForOutput 결정.
/// - CrafterState Read Only, Persistent State(Status) 직접 수정 금지.
/// - 실행 결정은 CrafterDecision, 상태 전이 결정은 CrafterStateDecision으로 분리해 기록.
/// - CrafterStateDecision은 StateApplyGroup의 CrafterStateApplySystem에서 실제 CrafterState.Status로 반영 후 비활성화.
/// - CrafterDecision 활성 상태는 Phase 4 Execution 처리 여부만 의미.
/// </summary>
[UpdateInGroup(typeof(DecisionGroup))]
[BurstCompile]
public partial struct CrafterDecisionSystem : ISystem
{
    private EntityQuery _crafterQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _crafterQuery = SystemAPI.QueryBuilder()
            .WithAllRW<CrafterDecision, CrafterStateDecision>()
            .WithAll<CrafterState, StoredItemElement, ProductItemElement, ProductResult>()
            .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
            .Build();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<RecipeRegistry>())
        {
            return;
        }

        var recipeRegistry = SystemAPI.GetSingleton<RecipeRegistry>();
        if (!recipeRegistry.Value.IsCreated)
        {
            return;
        }

        ItemRegistry itemRegistry = default;
        if (SystemAPI.HasSingleton<ItemRegistry>())
        {
            itemRegistry = SystemAPI.GetSingleton<ItemRegistry>();
        }

        var job = new CrafterDecisionJob
        {
            RecipeRegistry = recipeRegistry,
            ItemRegistry = itemRegistry
        };

        state.Dependency = job.ScheduleParallel(_crafterQuery, state.Dependency);
    }
}

/// <summary>
/// 각 제작기의 제작 조건을 병렬로 판정하는 Safe Burst Job.
/// </summary>
[BurstCompile]
public partial struct CrafterDecisionJob : IJobEntity
{
    [ReadOnly]
    public RecipeRegistry RecipeRegistry;

    [ReadOnly]
    public ItemRegistry ItemRegistry;

    public void Execute(
        ref CrafterDecision decision,
        EnabledRefRW<CrafterDecision> decisionEnabled,
        ref CrafterStateDecision stateDecision,
        EnabledRefRW<CrafterStateDecision> stateDecisionEnabled,
        in CrafterState state,
        in DynamicBuffer<StoredItemElement> storedItems,
        in DynamicBuffer<ProductItemElement> productItems,
        in DynamicBuffer<ProductResult> productResults)
    {
        // 0. 부산물/잔여 배출물 대기 상태 처리 (WaitingForByproductOutput)
        if (state.Status == CrafterStatusEnum.WaitingForByproductOutput)
        {
            if (productItems.Length > 0)
            {
                // 출력 버퍼에 잔여물이 남아있으므로 대기 유지 및 제작 차단
                decision.CanCraft = false;
                decision.CanStartCraft = false;
                decision.CanAdvance = false;
                decision.CanProduceOutput = false;
                decision.RecipeId = state.SelectedRecipeId;
                decision.RecipeIndex = -1;
                stateDecision.NextStatus = CrafterStatusEnum.WaitingForByproductOutput;
                stateDecisionEnabled.ValueRW = true;
                decisionEnabled.ValueRW = false;
                return;
            }
            // 출력 버퍼가 완전히 비워졌으면 아래 정상 판정 흐름으로 계속 진행.
            // 실제 Status 해제는 이번 Decision의 NextStatus가 StateApply에서 반영되며,
            // 같은 프레임 BuildingItemInputDecision은 기존 WaitingForByproductOutput 상태 참조 가능.
        }

        // 1. 레시피 유효성 검사
        if (state.SelectedRecipeId <= 0)
        {
            decision.CanCraft = false;
            decision.CanStartCraft = false;
            decision.CanAdvance = false;
            decision.CanProduceOutput = false;
            decision.RecipeId = 0;
            decision.RecipeIndex = -1;
            stateDecision.NextStatus = CrafterStatusEnum.NoRecipe;
            stateDecisionEnabled.ValueRW = true;
            decisionEnabled.ValueRW = false;
            return;
        }

        ref var registry = ref RecipeRegistry.Value.Value;
        if (!registry.TryGetRecipeIndex(state.SelectedRecipeId, out int recipeIdx))
        {
            decision.CanCraft = false;
            decision.CanStartCraft = false;
            decision.CanAdvance = false;
            decision.CanProduceOutput = false;
            decision.RecipeId = state.SelectedRecipeId;
            decision.RecipeIndex = -1;
            stateDecision.NextStatus = CrafterStatusEnum.NoRecipe;
            stateDecisionEnabled.ValueRW = true;
            decisionEnabled.ValueRW = false;
            return;
        }

        decision.RecipeId = state.SelectedRecipeId;
        decision.RecipeIndex = recipeIdx;
        ref var recipe = ref registry.Recipes[recipeIdx];

        // StateApply 미소비 생산 결과 존재 시 새 제작/출력 차단
        // 정상 프레임에서는 같은 프레임 StateApply에서 비워지지만, Phase 누락/지연 시 중복 생산을 방지하는 안전장치.
        if (productResults.Length > 0)
        {
            decision.CanCraft = false;
            decision.CanStartCraft = false;
            decision.CanAdvance = false;
            decision.CanProduceOutput = false;
            stateDecision.NextStatus = CrafterStatusEnum.WaitingForOutput;
            stateDecisionEnabled.ValueRW = true;
            decisionEnabled.ValueRW = false;
            return;
        }

        // 2. 출력 버퍼 여유 공간 검사 (다중 부산물 지원 및 All-or-Nothing 정책)
        // Slot 0: 주완성품, Slot 1..N: 각 부산물
        bool canProduceOutput = true;
        bool hasItemRegistry = ItemRegistry.Value.IsCreated;

        for (int outIdx = 0; outIdx < recipe.Outputs.Length; outIdx++)
        {
            ref var output = ref recipe.Outputs[outIdx];
            if (output.ItemType == ItemTypeEnum.None || output.Amount <= 0)
            {
                continue;
            }

            int countInSlot = 0;
            for (int p = 0; p < productItems.Length; p++)
            {
                if (productItems[p].SlotIndex == outIdx)
                {
                    countInSlot++;
                }
            }

            int maxStack = (hasItemRegistry && output.ItemType != ItemTypeEnum.None)
                ? ItemRegistry.Value.Value.GetMaxStack(output.ItemType)
                : 50;

            if (countInSlot + output.Amount > maxStack)
            {
                canProduceOutput = false;
                break;
            }
        }

        // 3. 제작 진행 상태에 따른 의사결정 분기
        if (state.IsCraftingActive)
        {
            // 이미 재료를 선소비하고 제작 중인 상태
            if (state.Progress < 1.0f)
            {
                // 진행도 누적 가능
                decision.CanCraft = true;
                decision.CanStartCraft = false;
                decision.CanAdvance = true;
                decision.CanProduceOutput = false;
                stateDecision.NextStatus = CrafterStatusEnum.Crafting;
                stateDecisionEnabled.ValueRW = true;
                decisionEnabled.ValueRW = true;
            }
            else
            {
                // Progress >= 1.0f: 제작 완료, 출력 공간 확보 전까지 대기
                decision.CanAdvance = false;
                decision.CanStartCraft = false;

                if (canProduceOutput)
                {
                    // 출력 버퍼에 여유가 있으므로 배출 승인
                    decision.CanCraft = true;
                    decision.CanProduceOutput = true;
                    stateDecision.NextStatus = CrafterStatusEnum.Idle;
                    stateDecisionEnabled.ValueRW = true;
                    decisionEnabled.ValueRW = true;
                }
                else
                {
                    // 출력 버퍼 만석: 배출 대기
                    decision.CanCraft = false;
                    decision.CanProduceOutput = false;
                    stateDecision.NextStatus = CrafterStatusEnum.WaitingForOutput;
                    stateDecisionEnabled.ValueRW = true;
                    decisionEnabled.ValueRW = true; // 대기 상태 감시 유지
                }
            }
        }
        else
        {
            // 신규 제작 대기 상태: StoredItemElement에 레시피 필요 재료가 완비되었는지 검사
            bool hasAllIngredients = true;

            for (int i = 0; i < recipe.Ingredients.Length; i++)
            {
                var ingredient = recipe.Ingredients[i];
                int foundCount = 0;

                for (int s = 0; s < storedItems.Length; s++)
                {
                    if (storedItems[s].ItemType == ingredient.ItemType)
                    {
                        foundCount++;
                    }
                }

                if (foundCount < ingredient.Amount)
                {
                    hasAllIngredients = false;
                    break;
                }
            }

            if (hasAllIngredients)
            {
                // 재료 완비: 신규 제작 시작 가능
                decision.CanCraft = true;
                decision.CanStartCraft = true;
                decision.CanAdvance = true;
                decision.CanProduceOutput = false;
                stateDecision.NextStatus = CrafterStatusEnum.Idle;
                stateDecisionEnabled.ValueRW = true;
                decisionEnabled.ValueRW = true;
            }
            else
            {
                // 재료 부족: 입력 대기
                decision.CanCraft = false;
                decision.CanStartCraft = false;
                decision.CanAdvance = false;
                decision.CanProduceOutput = false;
                stateDecision.NextStatus = CrafterStatusEnum.WaitingForInput;
                stateDecisionEnabled.ValueRW = true;
                decisionEnabled.ValueRW = false;
            }
        }
    }
}
