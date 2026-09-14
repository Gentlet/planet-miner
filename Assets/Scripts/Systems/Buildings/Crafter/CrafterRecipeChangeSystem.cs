using Unity.Entities;

public partial class CrafterRecipeChangeSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireForUpdate<CrafterConfig>();
        RequireForUpdate<ResearchConfig>();
        RequireForUpdate<CrafterRecipeChangeRequest>();
    }

    protected override void OnUpdate()
    {
        DynamicBuffer<CrafterRecipeElement> recipes = SystemAPI.GetSingletonBuffer<CrafterRecipeElement>(true);
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients = SystemAPI.GetSingletonBuffer<CrafterRecipeIngredientElement>(true);
        DynamicBuffer<RecipeUnlockElement> recipeUnlocks = SystemAPI
            .GetSingletonBuffer<RecipeUnlockElement>(true);
        EntityCommandBuffer ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);

        foreach (var (request, requestEntity) in SystemAPI.Query<RefRO<CrafterRecipeChangeRequest>>().WithEntityAccess())
        {
            Entity crafterEntity = request.ValueRO.crafterEntity == Entity.Null ? requestEntity : request.ValueRO.crafterEntity;

            if (EntityManager.Exists(crafterEntity) &&
                EntityManager.HasComponent<Crafter>(crafterEntity) &&
                IsRequestedRecipeUnlocked(
                    request.ValueRO.selectedItemType,
                    recipes,
                    recipeUnlocks))
            {
                Crafter crafter = EntityManager.GetComponentData<Crafter>(crafterEntity);
                crafter.selectedItemType = request.ValueRO.selectedItemType;
                crafter.progress = 0f;
                crafter.state = GetRecipeState(crafterEntity, crafter.selectedItemType, recipes, ingredients);
                EntityManager.SetComponentData(crafterEntity, crafter);
                CreateIncompatibleIngredientRemovalRequests(
                    ref ecb,
                    crafterEntity,
                    crafter.selectedItemType,
                    recipes,
                    ingredients);
            }

            if (requestEntity != crafterEntity || !EntityManager.HasComponent<Crafter>(requestEntity))
                ecb.DestroyEntity(requestEntity);
            else
                ecb.RemoveComponent<CrafterRecipeChangeRequest>(requestEntity);
        }
    }

    private static bool IsRequestedRecipeUnlocked(
        ItemTypeEnum selectedItemType,
        DynamicBuffer<CrafterRecipeElement> recipes,
        DynamicBuffer<RecipeUnlockElement> recipeUnlocks)
    {
        if (!recipes.TryFindRecipe(
                selectedItemType,
                out CrafterRecipeElement recipe))
            return false;

        return recipeUnlocks.IsRecipeUnlocked(recipe.id);
    }

    private CrafterStateEnum GetRecipeState(
        Entity crafterEntity,
        ItemTypeEnum selectedItemType,
        DynamicBuffer<CrafterRecipeElement> recipes,
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients)
    {
        if (!selectedItemType.IsValid())
            return CrafterStateEnum.NoRecipe;
        if (!recipes.TryFindRecipe(selectedItemType, out CrafterRecipeElement recipe))
            return CrafterStateEnum.NoRecipe;
        if (EntityManager.HasBuffer<StoredItemElement>(crafterEntity))
        {
            DynamicBuffer<StoredItemElement> storedItems = EntityManager.GetBuffer<StoredItemElement>(crafterEntity);

            if (storedItems.HasExceptionItem(ingredients, recipe.id))
                return CrafterStateEnum.WaitingForExceptionItem;
        }

        return CrafterStateEnum.Idle;
    }

    private void CreateIncompatibleIngredientRemovalRequests(
        ref EntityCommandBuffer ecb,
        Entity crafterEntity,
        ItemTypeEnum selectedItemType,
        DynamicBuffer<CrafterRecipeElement> recipes,
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients)
    {
        if (!recipes.TryFindRecipe(
                selectedItemType,
                out CrafterRecipeElement recipe))
            return;

        if (!EntityManager.HasBuffer<StoredItemElement>(crafterEntity))
            return;

        DynamicBuffer<StoredItemElement> storedItems = EntityManager
            .GetBuffer<StoredItemElement>(crafterEntity, true);

        for (ItemTypeEnum itemType = ItemTypeEnum.Iron_Ore;
             itemType < ItemTypeEnum.Count;
             itemType++)
        {
            if (ingredients.HasIngredient(recipe.id, itemType))
                continue;

            int storedQuantity = storedItems.CountItems(itemType);

            if (storedQuantity <= 0)
                continue;

            DroneBuildingItemRequestUtility
                .TryCreateUncoveredRemovalRequest(
                    EntityManager,
                    ref ecb,
                    crafterEntity,
                    itemType,
                    storedQuantity);
        }
    }
}
