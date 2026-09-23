using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 제작기(Crafter)의 제작 조건(선택된 레시피, 입력 재료 완비 여부, 출력 버퍼 여유 공간)을 판정하는 의사결정 시스템.
/// 
/// [책임]
/// - DecisionGroup(Phase 2)에서 실행됩니다.
/// - RecipeRegistry Blob에서 선택된 레시피(SelectedRecipeId)를 조회합니다.
/// - [옵션 A 선소비 & 정책 B 만석 대기]:
///   1. 미착수 상태(!IsCraftingActive): StoredItemElement에 레시피 필요 재료가 모두 구비되어 있는지 확인하여 CanStartCraft 결정.
///   2. 진행 중 상태(IsCraftingActive && Progress < 1.0f): CanAdvance = true 및 Status = Crafting 설정.
///   3. 완료 상태(IsCraftingActive && Progress >= 1.0f): ProductItemElement에 주생산품/부산품 수용 공간(1스택 한도)이 있는지 검사하여
///      공간이 있으면 CanProduceOutput = true, 만석이면 Status = WaitingForOutput 설정 및 대기.
/// - 컴포넌트 활성화(EnabledRefRW)를 통해 유효 작업이 있는 제작기만 Phase 4 Execution의 대상으로 전달합니다.
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
            .WithAllRW<CrafterDecision, CrafterState>()
            .WithAll<StoredItemElement, ProductItemElement, ProductResult>()
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
        ref CrafterState state,
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
                decisionEnabled.ValueRW = false;
                return;
            }
            else
            {
                // 출력 버퍼가 완전히 비워졌으므로 대기 해제 및 정상 상태로 복귀
                state.Status = state.SelectedRecipeId > 0 ? CrafterStatusEnum.Idle : CrafterStatusEnum.NoRecipe;
            }
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
            decisionEnabled.ValueRW = false;
            state.Status = CrafterStatusEnum.NoRecipe;
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
            decisionEnabled.ValueRW = false;
            state.Status = CrafterStatusEnum.NoRecipe;
            return;
        }

        decision.RecipeId = state.SelectedRecipeId;
        decision.RecipeIndex = recipeIdx;
        ref var recipe = ref registry.Recipes[recipeIdx];

        // StateApply에서 아직 소비되지 않은 생산 결과가 있으면 새 제작/출력을 시작하지 않습니다.
        // 정상 프레임에서는 같은 프레임 StateApply에서 비워지지만, Phase 누락/지연 시 중복 생산을 방지하는 안전장치입니다.
        if (productResults.Length > 0)
        {
            decision.CanCraft = false;
            decision.CanStartCraft = false;
            decision.CanAdvance = false;
            decision.CanProduceOutput = false;
            state.Status = CrafterStatusEnum.WaitingForOutput;
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
                state.Status = CrafterStatusEnum.Crafting;
                decisionEnabled.ValueRW = true;
            }
            else
            {
                // Progress >= 1.0f: 제작 완료 상태 (정책 B: 출력 공간 확보 대기)
                decision.CanAdvance = false;
                decision.CanStartCraft = false;

                if (canProduceOutput)
                {
                    // 출력 버퍼에 여유가 있으므로 배출 승인
                    decision.CanCraft = true;
                    decision.CanProduceOutput = true;
                    state.Status = CrafterStatusEnum.Idle;
                    decisionEnabled.ValueRW = true;
                }
                else
                {
                    // 출력 버퍼 만석: 배출 대기
                    decision.CanCraft = false;
                    decision.CanProduceOutput = false;
                    state.Status = CrafterStatusEnum.WaitingForOutput;
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
                state.Status = CrafterStatusEnum.Idle;
                decisionEnabled.ValueRW = true;
            }
            else
            {
                // 재료 부족: 입력 대기
                decision.CanCraft = false;
                decision.CanStartCraft = false;
                decision.CanAdvance = false;
                decision.CanProduceOutput = false;
                state.Status = CrafterStatusEnum.WaitingForInput;
                decisionEnabled.ValueRW = false;
            }
        }
    }
}
