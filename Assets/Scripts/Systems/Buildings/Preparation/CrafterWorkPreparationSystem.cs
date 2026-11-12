using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;

[UpdateInGroup(typeof(SimulationPreparationGroup))]
public partial struct CrafterWorkPreparationSystem : ISystem
{
    private EntityQuery _workQuery;
    public void OnCreate(ref SystemState state) {
        _workQuery = SystemAPI.QueryBuilder().WithAllRW<CrafterWorkPreparation>().WithAll<CrafterState>().WithAll<StoredItemElement>().WithAll<ProductItemElement>().WithAll<ProductResult>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).Build();
 }
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new ResetCrafterWorkPreparationJob().ScheduleParallel(state.Dependency);
        Entity registry = SystemAPI.TryGetSingletonEntity<RecipeRegistry>(out var entity) ? entity : Entity.Null;
        state.Dependency = new PrepareCrafterWorkJob
        {
            EntityHandle = SystemAPI.GetEntityTypeHandle(),
            CrafterWorkPreparationHandle = SystemAPI.GetComponentTypeHandle<CrafterWorkPreparation>(false),
            CrafterStateHandle = SystemAPI.GetComponentTypeHandle<CrafterState>(true),
            StoredItemElementHandle = SystemAPI.GetBufferTypeHandle<StoredItemElement>(true),
            ProductItemElementHandle = SystemAPI.GetBufferTypeHandle<ProductItemElement>(true),
            ProductResultHandle = SystemAPI.GetBufferTypeHandle<ProductResult>(true),
            Registry = registry,
            Recipes = SystemAPI.GetBufferLookup<RecipeConfigElement>(true),
            Ingredients = SystemAPI.GetBufferLookup<RecipeIngredientElement>(true),
            DestroyRequests = SystemAPI.GetComponentLookup<DestroyItemRequest>(true),
            Demolition = SystemAPI.GetComponentLookup<PendingBuildingDemolition>(true)
        }.ScheduleParallel(_workQuery, state.Dependency);
    }
}

[BurstCompile]
internal partial struct PrepareCrafterWorkJob : IJobChunk
{
    [ReadOnly] public EntityTypeHandle EntityHandle;
    public ComponentTypeHandle<CrafterWorkPreparation> CrafterWorkPreparationHandle;
    [ReadOnly] public ComponentTypeHandle<CrafterState> CrafterStateHandle;
    [ReadOnly] public BufferTypeHandle<StoredItemElement> StoredItemElementHandle;
    [ReadOnly] public BufferTypeHandle<ProductItemElement> ProductItemElementHandle;
    [ReadOnly] public BufferTypeHandle<ProductResult> ProductResultHandle;

    private struct RegistryReadContext
    {
        public RegistryBufferReadCache<RecipeConfigElement> Recipes;
        public RegistryBufferReadCache<RecipeIngredientElement> Ingredients;
    }

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
    {
        var entities = chunk.GetNativeArray(EntityHandle);
        var crafterWorkPreparationValues = chunk.GetNativeArray(ref CrafterWorkPreparationHandle);
        var crafterWorkPreparationEnabled = chunk.GetEnabledMask(ref CrafterWorkPreparationHandle);
        var crafterStateValues = chunk.GetNativeArray(ref CrafterStateHandle);
        var storedItemElementValues = chunk.GetBufferAccessorRO(ref StoredItemElementHandle);
        var productItemElementValues = chunk.GetBufferAccessorRO(ref ProductItemElementHandle);
        var productResultValues = chunk.GetBufferAccessorRO(ref ProductResultHandle);
        var registryRead = new RegistryReadContext();
        var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
        while (enumerator.NextEntityIndex(out int index))
        {
            var preparationRef = new RefRW<CrafterWorkPreparation>(crafterWorkPreparationValues, index);
            var stateValue = crafterStateValues[index];
            var storedValue = storedItemElementValues[index];
            var productsValue = productItemElementValues[index];
            var resultsValue = productResultValues[index];
            ExecuteEntity(entities[index],
                ref preparationRef.ValueRW,
                crafterWorkPreparationEnabled.GetEnabledRefRW<CrafterWorkPreparation>(index),
                in stateValue,
                in storedValue,
                in productsValue,
                in resultsValue, ref registryRead);
        }
    }

    public Entity Registry;
    [ReadOnly] public BufferLookup<RecipeConfigElement> Recipes;
    [ReadOnly] public BufferLookup<RecipeIngredientElement> Ingredients;
    [ReadOnly] public ComponentLookup<DestroyItemRequest> DestroyRequests;
    [ReadOnly] public ComponentLookup<PendingBuildingDemolition> Demolition;

    private void ExecuteEntity(Entity entity, ref CrafterWorkPreparation preparation,
        EnabledRefRW<CrafterWorkPreparation> prepared, in CrafterState state,
        in DynamicBuffer<StoredItemElement> stored, in DynamicBuffer<ProductItemElement> products,
        in DynamicBuffer<ProductResult> results, ref RegistryReadContext registryRead)
    {
        preparation = default;
        prepared.ValueRW = true;
        if (Demolition.HasComponent(entity))
        {
            return;
        }
        if (state.Status == CrafterStatusEnum.WaitingForByproductOutput && products.Length > 0)
        {
            return;
        }
        if (Registry == Entity.Null)
        {
            prepared.ValueRW = false;
            return;
        }
        if (!registryRead.Recipes.TryRead(Recipes, Registry, out var recipes) || !registryRead.Ingredients.TryRead(Ingredients, Registry, out var ingredients))
        {
            prepared.ValueRW = false;
            return;
        }
        if (results.Length > 0 || !RecipeConfigLookupUtility.TryGetRecipe(recipes,
                state.SelectedRecipeId, out var recipe))
        {
            return;
        }
        if (!recipe.IsUnlocked)
        {
            return;
        }
        if (state.IsCraftingActive)
        {
            preparation.CanAdvance = state.Progress < 1f;
            return;
        }
        preparation.CanStartCraft = RecipeConfigLookupUtility.HasRequiredIngredients(
            recipe, ingredients, stored, DestroyRequests);
        preparation.CanAdvance = preparation.CanStartCraft;
    }
}
