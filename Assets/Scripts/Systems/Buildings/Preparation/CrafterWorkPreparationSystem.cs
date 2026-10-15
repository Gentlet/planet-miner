using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

[UpdateInGroup(typeof(SimulationPreparationGroup))]
public partial struct CrafterWorkPreparationSystem : ISystem
{
    public void OnCreate(ref SystemState state) { }
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new ResetCrafterWorkPreparationJob().ScheduleParallel(state.Dependency);
        Entity registry = SystemAPI.TryGetSingletonEntity<RecipeRegistry>(out var entity) ? entity : Entity.Null;
        state.Dependency = new PrepareCrafterWorkJob
        {
            Registry = registry,
            Recipes = SystemAPI.GetBufferLookup<RecipeConfigElement>(true),
            Ingredients = SystemAPI.GetBufferLookup<RecipeIngredientElement>(true),
            DestroyRequests = SystemAPI.GetComponentLookup<DestroyItemRequest>(true),
            Demolition = SystemAPI.GetComponentLookup<PendingBuildingDemolition>(true)
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile, WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
internal partial struct PrepareCrafterWorkJob : IJobEntity
{
    public Entity Registry;
    [ReadOnly] public BufferLookup<RecipeConfigElement> Recipes;
    [ReadOnly] public BufferLookup<RecipeIngredientElement> Ingredients;
    [ReadOnly] public ComponentLookup<DestroyItemRequest> DestroyRequests;
    [ReadOnly] public ComponentLookup<PendingBuildingDemolition> Demolition;

    private void Execute(Entity entity, ref CrafterWorkPreparation preparation,
        EnabledRefRW<CrafterWorkPreparation> prepared, in CrafterState state,
        in DynamicBuffer<StoredItemElement> stored, in DynamicBuffer<ProductItemElement> products,
        in DynamicBuffer<ProductResult> results)
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
        if (!Recipes.HasBuffer(Registry) || !Ingredients.HasBuffer(Registry))
        {
            prepared.ValueRW = false;
            return;
        }
        if (results.Length > 0 || !RecipeConfigLookupUtility.TryGetRecipeIndex(Recipes[Registry],
                state.SelectedRecipeId, out int recipeIndex))
        {
            return;
        }
        if (state.IsCraftingActive)
        {
            preparation.CanAdvance = state.Progress < 1f;
            return;
        }
        preparation.CanStartCraft = RecipeConfigLookupUtility.HasRequiredIngredients(
            Recipes[Registry][recipeIndex], Ingredients[Registry], stored, DestroyRequests);
        preparation.CanAdvance = preparation.CanStartCraft;
    }
}
