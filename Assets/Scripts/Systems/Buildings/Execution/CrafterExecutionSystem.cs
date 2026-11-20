using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: Execution에서 선택 레시피의 재료를 선소비하고 제작 진행과 생산 결과를 반영한다.
/// 입력·생성자: Command의 레시피 선택/중단 상태, CrafterDecisionSystem의 착수·진행·출력 결정과 RecipeRegistry.
/// 출력: 재료 수량을 차감하고 진행도와 이번 틱 생산 결과를 작성한다.
/// 착수 실패: 모든 요구 재료의 슬롯 수량을 먼저 검사하며 부족하면 아무 재료도 소비하지 않는다.
/// 이용·정리: ItemLifecycleApplySystem이 ProductResult를 출력 슬롯에 더하고 비운다. Status 반영은 CrafterStateApplySystem의 책임이다.
/// 가시화: 재료 수량과 진행은 Execution Job에서 반영한다. 레시피 변경과 잔여 재료 반환은 Command가 소유한다.
/// </summary>
[UpdateInGroup(typeof(BuildingExecutionGroup))]
[BurstCompile]
public partial struct CrafterExecutionSystem : ISystem
{
    private EntityQuery _crafterQuery;

    private BufferLookup<RecipeConfigElement> _recipeConfigLookup;
    private BufferLookup<RecipeIngredientElement> _recipeIngredientLookup;
    private BufferLookup<RecipeOutputElement> _recipeOutputLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ResearchStatBonuses>();

        _recipeConfigLookup = state.GetBufferLookup<RecipeConfigElement>(true);
        _recipeIngredientLookup = state.GetBufferLookup<RecipeIngredientElement>(true);
        _recipeOutputLookup = state.GetBufferLookup<RecipeOutputElement>(true);

        _crafterQuery = SystemAPI.QueryBuilder()
            .WithAllRW<CrafterState>()
            .WithAllRW<StorageSlotElement, ProductResult>()
            .WithAll<ProductSlotElement, CrafterDecision>()
            .Build();
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

        // 완료 묶음의 출력은 시간이 흐르지 않은 틱에도 처리한다.
        float dt = math.clamp(SystemAPI.Time.DeltaTime, 0f, GameConstants.MaxSimulationDeltaTime);

        var job = new CrafterExecutionJob
        {
            CrafterStateHandle = SystemAPI.GetComponentTypeHandle<CrafterState>(false),
            StorageSlotElementHandle = SystemAPI.GetBufferTypeHandle<StorageSlotElement>(false),
            ProductResultHandle = SystemAPI.GetBufferTypeHandle<ProductResult>(false),
            CrafterDecisionHandle = SystemAPI.GetComponentTypeHandle<CrafterDecision>(true),
            DeltaTime = dt,
            SpeedBonus = SystemAPI.GetSingleton<ResearchStatBonuses>().CraftingSpeed,
            RecipeRegistryEntity = recipeRegistryEntity,
            RecipeConfigLookup = _recipeConfigLookup,
            RecipeIngredientLookup = _recipeIngredientLookup,
            RecipeOutputLookup = _recipeOutputLookup,

        };

        state.Dependency = job.Schedule(_crafterQuery, state.Dependency);
    }
}

/// <summary>
/// 각 제작기의 재료 선소비, 진행도 누적, 생산 결과 기록을 처리하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct CrafterExecutionJob : IJobChunk
{
    public ComponentTypeHandle<CrafterState> CrafterStateHandle;
    public BufferTypeHandle<StorageSlotElement> StorageSlotElementHandle;
    public BufferTypeHandle<ProductResult> ProductResultHandle;
    [ReadOnly] public ComponentTypeHandle<CrafterDecision> CrafterDecisionHandle;

    private struct RegistryReadContext
    {
        public RegistryBufferReadCache<RecipeConfigElement> RecipeConfigLookup;
        public RegistryBufferReadCache<RecipeIngredientElement> RecipeIngredientLookup;
        public RegistryBufferReadCache<RecipeOutputElement> RecipeOutputLookup;
    }

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
    {
        var crafterStateValues = chunk.GetNativeArray(ref CrafterStateHandle);
        var storedItemElementValues = chunk.GetBufferAccessorRW(ref StorageSlotElementHandle);
        var productResultValues = chunk.GetBufferAccessorRW(ref ProductResultHandle);
        var crafterDecisionValues = chunk.GetNativeArray(ref CrafterDecisionHandle);
        var registryRead = new RegistryReadContext();
        var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
        while (enumerator.NextEntityIndex(out int index))
        {
            var stateRef = new RefRW<CrafterState>(crafterStateValues, index);
            var storedItemsValue = storedItemElementValues[index];
            var productResultsValue = productResultValues[index];
            var decisionValue = crafterDecisionValues[index];
            ExecuteEntity(
                ref stateRef.ValueRW,
                ref storedItemsValue,
                ref productResultsValue,
                in decisionValue, ref registryRead);
        }
    }

    public double SpeedBonus;
    public float DeltaTime;

    public Entity RecipeRegistryEntity;

    [ReadOnly]
    public BufferLookup<RecipeConfigElement> RecipeConfigLookup;

    [ReadOnly]
    public BufferLookup<RecipeIngredientElement> RecipeIngredientLookup;

    [ReadOnly]
    public BufferLookup<RecipeOutputElement> RecipeOutputLookup;

    private void ExecuteEntity(
        ref CrafterState state,
        ref DynamicBuffer<StorageSlotElement> storedItems,
        ref DynamicBuffer<ProductResult> productResults,
        in CrafterDecision decision, ref RegistryReadContext registryRead)
    {
        var recipes = registryRead.RecipeConfigLookup.Read(RecipeConfigLookup, RecipeRegistryEntity);

        // 유효 레시피 확인
        if (state.SelectedRecipeId <= 0)
        {
            return;
        }

        var recipe = recipes[decision.RecipeIndex];
        var ingredients = registryRead.RecipeIngredientLookup.Read(RecipeIngredientLookup, RecipeRegistryEntity);
        var outputs = registryRead.RecipeOutputLookup.Read(RecipeOutputLookup, RecipeRegistryEntity);

        // =========================================================================
        // 2. 신규 제작 착수 및 재료 선소비
        // =========================================================================
        float poweredDeltaTime = DeltaTime * decision.PowerTimeRatio;
        if (decision.CanStartCraft && !state.IsCraftingActive)
        {
            if (poweredDeltaTime <= 0f)
            {
                return;
            }
            // 재료 묶음이 부족하면 입력 수량과 진행도를 보존한다.
            if (!RecipeConfigLookupUtility.HasRequiredIngredients(recipe, ingredients, storedItems))
            {
                return;
            }

            for (int i = 0; i < recipe.IngredientCount; i++)
            {
                var ingredient = ingredients[recipe.IngredientStart + i];
                InventorySlotUtility.Consume(
                    storedItems, ingredient.ItemType, ingredient.Amount);
            }

            state.IsCraftingActive = true;
            state.Progress = 0.0f;
        }

        // =========================================================================
        // 3. [진행도 누적 (Crafting Progress)]
        // =========================================================================
        if (state.IsCraftingActive && decision.CanAdvance && poweredDeltaTime > 0f)
        {
            float craftTime = math.max(0.01f, recipe.CraftTime);
            float deltaProgress = (poweredDeltaTime * ResearchRewardUtility.EffectiveSpeed(state.Speed, SpeedBonus)) / craftTime;
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
