using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: Decision에서 제작 착수·진행·출력 가능 여부와 다음 제작 상태를 판단한다.
/// 입력·생성자: Command가 확정한 선택 레시피, RecipeRegistry, 기존 재료/생산품과 Execution의 미소비 ProductResult.
/// 출력·소유권: 제작기의 CrafterDecision/CrafterStateDecision과 enable 상태만 갱신한다. 재료·진행도 원본은 쓰지 않는다.
/// 이용: CrafterExecutionSystem이 선소비/진행/생산 결과를, CrafterStateApplySystem이 NextStatus를 반영한다.
/// 정리·가시화: 제작 결정은 다음 틱 갱신하고 상태 결정은 Apply 후 비활성화한다. 미소비 결과와 잔여 부산품은 중복 생산을 차단한다.
/// </summary>
[UpdateInGroup(typeof(DecisionGroup))]
[BurstCompile]
public partial struct CrafterDecisionSystem : ISystem
{
    private EntityQuery _crafterQuery;
    private ComponentLookup<PendingBuildingDemolition> _pendingDemolitionLookup;
    private BufferLookup<RecipeConfigElement> _recipeConfigLookup;
    private BufferLookup<RecipeIngredientElement> _recipeIngredientLookup;
    private BufferLookup<RecipeOutputElement> _recipeOutputLookup;
    private BufferLookup<ItemConfigElement> _itemConfigLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _pendingDemolitionLookup = state.GetComponentLookup<PendingBuildingDemolition>(true);
        _recipeConfigLookup = state.GetBufferLookup<RecipeConfigElement>(true);
        _recipeIngredientLookup = state.GetBufferLookup<RecipeIngredientElement>(true);
        _recipeOutputLookup = state.GetBufferLookup<RecipeOutputElement>(true);
        _itemConfigLookup = state.GetBufferLookup<ItemConfigElement>(true);

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

        _pendingDemolitionLookup.Update(ref state);
        _recipeConfigLookup.Update(ref state);
        _recipeIngredientLookup.Update(ref state);
        _recipeOutputLookup.Update(ref state);
        _itemConfigLookup.Update(ref state);

        Entity recipeRegistryEntity = SystemAPI.GetSingletonEntity<RecipeRegistry>();
        if (!_recipeConfigLookup.HasBuffer(recipeRegistryEntity) ||
            !_recipeIngredientLookup.HasBuffer(recipeRegistryEntity) ||
            !_recipeOutputLookup.HasBuffer(recipeRegistryEntity))
        {
            return;
        }

        ItemRegistry itemRegistry = default;
        Entity itemRegistryEntity = Entity.Null;
        if (SystemAPI.HasSingleton<ItemRegistry>())
        {
            itemRegistry = SystemAPI.GetSingleton<ItemRegistry>();
            itemRegistryEntity = SystemAPI.GetSingletonEntity<ItemRegistry>();
        }

        var job = new CrafterDecisionJob
        {
            RecipeRegistryEntity = recipeRegistryEntity,
            PendingDemolitionLookup = _pendingDemolitionLookup,
            RecipeConfigLookup = _recipeConfigLookup,
            RecipeIngredientLookup = _recipeIngredientLookup,
            RecipeOutputLookup = _recipeOutputLookup,
            ItemRegistry = itemRegistry,
            ItemRegistryEntity = itemRegistryEntity,
            ItemConfigLookup = _itemConfigLookup
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
    public Entity RecipeRegistryEntity;

    [ReadOnly]
    public ComponentLookup<PendingBuildingDemolition> PendingDemolitionLookup;

    [ReadOnly]
    public BufferLookup<RecipeConfigElement> RecipeConfigLookup;

    [ReadOnly]
    public BufferLookup<RecipeIngredientElement> RecipeIngredientLookup;

    [ReadOnly]
    public BufferLookup<RecipeOutputElement> RecipeOutputLookup;

    [ReadOnly]
    public ItemRegistry ItemRegistry;

    public Entity ItemRegistryEntity;

    [ReadOnly]
    public BufferLookup<ItemConfigElement> ItemConfigLookup;

    public void Execute(
        Entity building,
        ref CrafterDecision decision,
        EnabledRefRW<CrafterDecision> decisionEnabled,
        ref CrafterStateDecision stateDecision,
        EnabledRefRW<CrafterStateDecision> stateDecisionEnabled,
        in CrafterState state,
        in DynamicBuffer<StoredItemElement> storedItems,
        in DynamicBuffer<ProductItemElement> productItems,
        in DynamicBuffer<ProductResult> productResults)
    {
        if (PendingDemolitionLookup.HasComponent(building))
        {
            decision.CanCraft = false;
            decision.CanStartCraft = false;
            decision.CanAdvance = false;
            decision.CanProduceOutput = false;
            decision.RecipeId = state.SelectedRecipeId;
            decision.RecipeIndex = -1;
            stateDecision.NextStatus = state.Status;
            stateDecisionEnabled.ValueRW = false;
            decisionEnabled.ValueRW = false;
            return;
        }

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

        var recipes = RecipeConfigLookup[RecipeRegistryEntity];
        if (!RecipeConfigLookupUtility.TryGetRecipeIndex(recipes, state.SelectedRecipeId, out int recipeIdx))
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
        var recipe = recipes[recipeIdx];
        var ingredients = RecipeIngredientLookup[RecipeRegistryEntity];
        var outputs = RecipeOutputLookup[RecipeRegistryEntity];

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
        bool hasItemRegistry = ItemConfigLookup.HasBuffer(ItemRegistryEntity);

        for (int outIdx = 0; outIdx < recipe.OutputCount; outIdx++)
        {
            var output = outputs[recipe.OutputStart + outIdx];
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
                ? ItemRegistry.GetMaxStack(ItemConfigLookup[ItemRegistryEntity], output.ItemType)
                : 50;

            if (countInSlot + output.Amount > maxStack)
            {
                canProduceOutput = false;
                break;
            }
        }

        // 이번 Execution에서 처음 완료될 진행도도 현재 Decision에서는 진행 중이다.
        // 출력 승인은 Progress가 완료된 상태를 다음 Decision이 읽을 때 정하며, 출력 전체 공간을 확보한 뒤 결과를 기록한다.
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

            for (int i = 0; i < recipe.IngredientCount; i++)
            {
                var ingredient = ingredients[recipe.IngredientStart + i];
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
