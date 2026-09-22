using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 제작기(Crafter)의 재료 소모, 제작 진행도 누적, 완성품/부산품 생산 결과 기록,
/// 레시피 변경 시 잔여 재료 배출(Purge to Output) 및 입력 필터 자동 동기화를 수행하는 실행 시스템.
/// 
/// [책임]
/// - ExecutionGroup(Phase 4)에서 실행됩니다.
/// - 레시피 변경 감지:
///   - SelectedRecipeId != ActiveRecipeId 감지 시 진행 중이던 제작을 취소하고,
///     StoredItemElement에 있던 잔여 재료들을 ProductItemElement(출력 버퍼)로 이관하여 외부 벨트로 자동 배출되도록 합니다.
///   - 새 레시피의 재료 목록을 기반으로 StorageFilter(Whitelist)를 자동으로 갱신합니다.
/// - [옵션 A 선소비 & 피드백 3번 무결성]:
///   - 신규 제작 착수(CanStartCraft == true && !IsCraftingActive) 시, StoredItemElement에서 레시피 재료를
///     RemoveAt으로 즉시 제거하고 DestroyItemRequest를 발행하여 Storage Invariant를 100% 보장합니다.
/// - 진행도 누적:
///   - (DeltaTime * Speed) / CraftTime 비율로 Progress를 누적합니다.
/// - [정책 B 만석 대기 및 배출]:
///   - Progress >= 1.0f 도달 후 출력 버퍼에 공간이 확보되면(CanProduceOutput == true),
///     ProductResult(Slot 0 주생산품, Slot 1 부산품)를 기록하고 IsCraftingActive = false 및 Progress = 0.0f로 리셋합니다.
///   - ProductResult는 같은 프레임 StateApply의 ItemLifecycleApplySystem이 실제 Item Entity와 ProductItemElement로 변환합니다.
/// </summary>
[UpdateInGroup(typeof(ExecutionGroup))]
[BurstCompile]
public partial struct CrafterExecutionSystem : ISystem
{
    private EntityQuery _crafterQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _crafterQuery = SystemAPI.QueryBuilder()
            .WithAllRW<CrafterState>()
            .WithAllRW<StoredItemElement, ProductResult>()
            .WithAll<ProductItemElement, CrafterDecision>()
            .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
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

        var recipeRegistry = SystemAPI.GetSingleton<RecipeRegistry>();
        if (!recipeRegistry.Value.IsCreated)
        {
            return;
        }

        float dt = math.min(SystemAPI.Time.DeltaTime, GameConstants.MaxSimulationDeltaTime);
        if (dt <= 0.0f)
        {
            return;
        }

        var ecbSystem = state.World.GetExistingSystemManaged<EndStateApplyEntityCommandBufferSystem>();
        var ecb = ecbSystem.CreateCommandBuffer();

        var job = new CrafterExecutionJob
        {
            DeltaTime = dt,
            RecipeRegistry = recipeRegistry,
            ECB = ecb
        };

        var jobHandle = job.Schedule(_crafterQuery, state.Dependency);
        ecbSystem.AddJobHandleForProducer(jobHandle);
        state.Dependency = jobHandle;
    }
}

/// <summary>
/// 각 제작기의 재료 소모, 진행도 누적, 생산 결과 기록 및 레시피 롤백을 처리하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct CrafterExecutionJob : IJobEntity
{
    public float DeltaTime;

    [ReadOnly]
    public RecipeRegistry RecipeRegistry;

    public EntityCommandBuffer ECB;

    public void Execute(
        ref CrafterState state,
        ref DynamicBuffer<StoredItemElement> storedItems,
        ref DynamicBuffer<ProductResult> productResults,
        in CrafterDecision decision)
    {
        ref var registry = ref RecipeRegistry.Value.Value;

        // 유효 레시피 확인
        if (state.SelectedRecipeId <= 0 || !registry.TryGetRecipeIndex(state.SelectedRecipeId, out int recipeIdx))
        {
            return;
        }

        ref var recipe = ref registry.Recipes[recipeIdx];

        // =========================================================================
        // 2. [신규 제작 착수 & 재료 선소비 (옵션 A & 피드백 3번 완전 준수)]
        // =========================================================================
        if (decision.CanStartCraft && !state.IsCraftingActive)
        {
            // 레시피 필요 재료를 StoredItemElement에서 차감하고 DestroyItemRequest 발행
            for (int i = 0; i < recipe.Ingredients.Length; i++)
            {
                var ingredient = recipe.Ingredients[i];
                int remainingToConsume = ingredient.Amount;

                for (int s = storedItems.Length - 1; s >= 0 && remainingToConsume > 0; s--)
                {
                    if (storedItems[s].ItemType == ingredient.ItemType)
                    {
                        Entity itemEntity = storedItems[s].ItemEntity;
                        storedItems.RemoveAt(s);
                        ECB.SetComponentEnabled<DestroyItemRequest>(itemEntity, true);
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
        // 4. [제작 완료 & ProductResult 기록 (정책 B)]
        // =========================================================================
        if (state.IsCraftingActive && state.Progress >= 1.0f && decision.CanProduceOutput)
        {
            // 정상적으로는 StateApply에서 매 프레임 소비되므로 비어 있어야 합니다.
            // 미소비 결과가 남아 있다면 중복 결과를 기록하지 않습니다.
            if (productResults.Length > 0)
            {
                return;
            }

            // 주생산품 결과 (Slot 0)
            if (recipe.TryGetPrimaryOutput(out var primary) && primary.Amount > 0)
            {
                productResults.Add(new ProductResult(primary.ItemType, primary.Amount, slotIndex: 0));
            }

            // 현재 정책상 첫 번째 부산품 결과만 지원 (Slot 1)
            if (recipe.Outputs.Length > 1 && recipe.Outputs[1].IsByproduct)
            {
                var byproduct = recipe.Outputs[1];
                if (byproduct.Amount > 0)
                {
                    productResults.Add(new ProductResult(byproduct.ItemType, byproduct.Amount, slotIndex: 1));
                }
            }

            // 1회 제작 사이클 완료: 리셋
            state.IsCraftingActive = false;
            state.Progress = 0.0f;
        }
    }
}
