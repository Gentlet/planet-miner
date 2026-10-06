using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: Execution에서 선택 레시피의 재료를 선소비하고 제작 진행과 생산 결과를 반영한다.
/// 입력·생성자: Command의 레시피 선택/중단 상태, CrafterDecisionSystem의 착수·진행·출력 결정과 RecipeRegistry.
/// 출력·소유권: StoredItemElement를 먼저 제거하고 실물 DestroyItemRequest를 활성화한다. CrafterState의 진행/작업 여부와 ProductResult를 쓴다.
/// 이용·정리: ItemLifecycleApplySystem이 소비 실물을 삭제하고 생산 결과를 실물/출력 버퍼로 변환 후 비운다. Status 반영은 CrafterStateApplySystem의 책임이다.
/// 가시화: 값 변경은 Execution Job에서 반영하며 실물 생성/삭제는 EndStateApply에서 확정한다. 레시피 변경·잔여 재료 반환은 여기서 처리하지 않는다.
/// </summary>
[UpdateInGroup(typeof(ExecutionGroup))]
[BurstCompile]
public partial struct CrafterExecutionSystem : ISystem
{
    private EntityQuery _crafterQuery;
    private ComponentLookup<DestroyItemRequest> _destroyItemRequestLookup;
    private BufferLookup<RecipeConfigElement> _recipeConfigLookup;
    private BufferLookup<RecipeIngredientElement> _recipeIngredientLookup;
    private BufferLookup<RecipeOutputElement> _recipeOutputLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _destroyItemRequestLookup = state.GetComponentLookup<DestroyItemRequest>(false);
        _recipeConfigLookup = state.GetBufferLookup<RecipeConfigElement>(true);
        _recipeIngredientLookup = state.GetBufferLookup<RecipeIngredientElement>(true);
        _recipeOutputLookup = state.GetBufferLookup<RecipeOutputElement>(true);

        _crafterQuery = SystemAPI.QueryBuilder()
            .WithAllRW<CrafterState>()
            .WithAllRW<StoredItemElement, ProductResult>()
            .WithAll<ProductItemElement, CrafterDecision>()
            .Build();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<RecipeRegistry>())
        {
            return;
        }

        _recipeConfigLookup.Update(ref state);
        _recipeIngredientLookup.Update(ref state);
        _recipeOutputLookup.Update(ref state);

        Entity recipeRegistryEntity = SystemAPI.GetSingletonEntity<RecipeRegistry>();
        if (!_recipeConfigLookup.HasBuffer(recipeRegistryEntity) ||
            !_recipeIngredientLookup.HasBuffer(recipeRegistryEntity) ||
            !_recipeOutputLookup.HasBuffer(recipeRegistryEntity))
        {
            return;
        }

        float dt = math.min(SystemAPI.Time.DeltaTime, GameConstants.MaxSimulationDeltaTime);
        if (dt <= 0.0f)
        {
            return;
        }

        _destroyItemRequestLookup.Update(ref state);

        var job = new CrafterExecutionJob
        {
            DeltaTime = dt,
            RecipeRegistryEntity = recipeRegistryEntity,
            RecipeConfigLookup = _recipeConfigLookup,
            RecipeIngredientLookup = _recipeIngredientLookup,
            RecipeOutputLookup = _recipeOutputLookup,
            DestroyItemRequestLookup = _destroyItemRequestLookup
        };

        state.Dependency = job.Schedule(_crafterQuery, state.Dependency);
    }
}

/// <summary>
/// 각 제작기의 재료 선소비, 진행도 누적, 생산 결과 기록을 처리하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct CrafterExecutionJob : IJobEntity
{
    public float DeltaTime;

    public Entity RecipeRegistryEntity;

    [ReadOnly]
    public BufferLookup<RecipeConfigElement> RecipeConfigLookup;

    [ReadOnly]
    public BufferLookup<RecipeIngredientElement> RecipeIngredientLookup;

    [ReadOnly]
    public BufferLookup<RecipeOutputElement> RecipeOutputLookup;

    public ComponentLookup<DestroyItemRequest> DestroyItemRequestLookup;

    public void Execute(
        ref CrafterState state,
        ref DynamicBuffer<StoredItemElement> storedItems,
        ref DynamicBuffer<ProductResult> productResults,
        in CrafterDecision decision)
    {
        var recipes = RecipeConfigLookup[RecipeRegistryEntity];

        // 유효 레시피 확인
        if (state.SelectedRecipeId <= 0 ||
            !RecipeConfigLookupUtility.TryGetRecipeIndex(recipes, state.SelectedRecipeId, out int recipeIdx))
        {
            return;
        }

        var recipe = recipes[recipeIdx];
        var ingredients = RecipeIngredientLookup[RecipeRegistryEntity];
        var outputs = RecipeOutputLookup[RecipeRegistryEntity];

        // =========================================================================
        // 2. 신규 제작 착수 및 재료 선소비
        // =========================================================================
        if (decision.CanStartCraft && !state.IsCraftingActive)
        {
            // 소유 버퍼 참조를 먼저 제거하여 소비 실물이 재선택되지 않게 하고, 실제 삭제는 ItemLifecycle과 EndStateApply에 맡긴다.
            // 레시피 필요 재료를 StoredItemElement에서 차감하고 DestroyItemRequest 발행
            for (int i = 0; i < recipe.IngredientCount; i++)
            {
                var ingredient = ingredients[recipe.IngredientStart + i];
                int remainingToConsume = ingredient.Amount;

                for (int s = storedItems.Length - 1; s >= 0 && remainingToConsume > 0; s--)
                {
                    if (storedItems[s].ItemType == ingredient.ItemType)
                    {
                        Entity itemEntity = storedItems[s].ItemEntity;
                        storedItems.RemoveAt(s);

                        if (DestroyItemRequestLookup.HasComponent(itemEntity))
                        {
                            DestroyItemRequestLookup.SetComponentEnabled(itemEntity, true);
                        }

                        remainingToConsume--;
                    }
                }
            }

            state.IsCraftingActive = true;
            state.Progress = 0.0f;
        }

        // =========================================================================
        // 3. [진행도 누적 (Crafting Progress)]
        // =========================================================================
        if (state.IsCraftingActive && decision.CanAdvance)
        {
            float craftTime = math.max(0.01f, recipe.CraftTime);
            float deltaProgress = (DeltaTime * state.Speed) / craftTime;
            state.Progress = math.min(1.0f, state.Progress + deltaProgress);
        }

        // =========================================================================
        // 4. 제작 완료 및 다중 Output ProductResult 기록
        // =========================================================================
        if (state.IsCraftingActive && state.Progress >= 1.0f && decision.CanProduceOutput)
        {
            // 정상적으로는 StateApply에서 매 프레임 소비되므로 비어 있어야 .
            // 미소비 결과 존재 시 중복 ProductResult 기록 차단
            if (productResults.Length > 0)
            {
                return;
            }

            // Outputs 전체 순회: Slot 0 (주생산품) 및 Slot 1..N (다중 부산물) 결과 기록
            for (int outIdx = 0; outIdx < recipe.OutputCount; outIdx++)
            {
                var output = outputs[recipe.OutputStart + outIdx];
                if (output.ItemType != ItemTypeEnum.None && output.Amount > 0)
                {
                    productResults.Add(new ProductResult(output.ItemType, output.Amount, slotIndex: outIdx));
                }
            }

            // 1회 제작 사이클 완료: 리셋
            state.IsCraftingActive = false;
            state.Progress = 0.0f;
        }
    }
}
