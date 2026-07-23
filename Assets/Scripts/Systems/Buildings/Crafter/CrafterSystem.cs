using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateAfter(typeof(ItemTrackingSystem))]
[UpdateAfter(typeof(CrafterRecipeChangeSystem))]
public partial class CrafterSystem : SystemBase
{
    private ChunkMapSystem _chunkMap;
    private ItemStorageSystem _itemStorage;
    private readonly List<Entity> _itemsInCell = new List<Entity>();
    private readonly Dictionary<int2, int> _reservedOutputItemCounts = new Dictionary<int2, int>();

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
        _itemStorage = World.GetExistingSystemManaged<ItemStorageSystem>();
        RequireForUpdate<CrafterConfig>();
        RequireForUpdate<ItemPrefabElement>();
    }

    protected override void OnUpdate()
    {
        if (_chunkMap == null)
        {
            _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
            if (_chunkMap == null)
                return;
        }

        if (_itemStorage == null)
        {
            _itemStorage = World.GetExistingSystemManaged<ItemStorageSystem>();
            if (_itemStorage == null)
                return;
        }

        DynamicBuffer<CrafterRecipeElement> recipes = SystemAPI.GetSingletonBuffer<CrafterRecipeElement>(true);
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients = SystemAPI.GetSingletonBuffer<CrafterRecipeIngredientElement>(true);
        DynamicBuffer<ItemStorageLimitElement> storageLimits = SystemAPI.GetSingletonBuffer<ItemStorageLimitElement>(true);
        DynamicBuffer<ItemPrefabElement> itemPrefabs = SystemAPI.GetSingletonBuffer<ItemPrefabElement>(true);
        EntityCommandBuffer ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);
        float deltaTime = SystemAPI.Time.DeltaTime;

        _reservedOutputItemCounts.Clear();

        foreach (var (crafter, gridPosition, direction, storedItems, crafterEntity) in
                 SystemAPI.Query<RefRW<Crafter>, RefRO<GridPosition>, RefRO<Direction>, DynamicBuffer<StoredItemElement>>().WithEntityAccess())
        {
            int2 crafterCell = gridPosition.ValueRO.gridPosition;

            TryDepositItems(ref ecb, crafter.ValueRO, storedItems, crafterEntity, crafterCell, recipes, ingredients, storageLimits);
            UpdateCrafting(ref crafter.ValueRW, storedItems, recipes, ingredients, deltaTime);
            TryOutput(ref ecb, itemPrefabs, recipes, ingredients, ref crafter.ValueRW, crafterCell, direction.ValueRO.dir, storedItems);
        }
    }

    private void TryDepositItems(
        ref EntityCommandBuffer ecb,
        in Crafter crafter,
        DynamicBuffer<StoredItemElement> storedItems,
        Entity crafterEntity,
        int2 crafterCell,
        DynamicBuffer<CrafterRecipeElement> recipes,
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients,
        DynamicBuffer<ItemStorageLimitElement> storageLimits)
    {
        if (!crafter.state.CanReceiveItems() || !crafter.selectedItemType.IsValid())
            return;
        if (!recipes.TryFindRecipe(crafter.selectedItemType, out CrafterRecipeElement recipe))
            return;

        _chunkMap.GetItems(crafterCell, _itemsInCell);

        for (int i = 0; i < _itemsInCell.Count; i++)
        {
            Entity itemEntity = _itemsInCell[i];

            if (!CanDepositItem(itemEntity, storedItems, ingredients, storageLimits, recipe.id, out ItemTypeEnum itemType))
                continue;

            _itemStorage.TryStoreItem(ref ecb, storedItems, crafterEntity, crafterCell, itemEntity);
        }
    }

    private bool CanDepositItem(
        Entity itemEntity,
        DynamicBuffer<StoredItemElement> storedItems,
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients,
        DynamicBuffer<ItemStorageLimitElement> storageLimits,
        int recipeId,
        out ItemTypeEnum itemType)
    {
        itemType = ItemTypeEnum.None;

        if (!EntityManager.Exists(itemEntity) ||
            !EntityManager.HasComponent<Item>(itemEntity) ||
            EntityManager.HasComponent<StoredItem>(itemEntity))
            return false;

        itemType = EntityManager.GetComponentData<Item>(itemEntity).type;

        if (!ingredients.HasIngredient(recipeId, itemType))
            return false;

        int maxAmount = storageLimits.GetStorageLimit(itemType);

        return maxAmount > 0 && storedItems.CountItems(itemType) < maxAmount;
    }

    private static void UpdateCrafting(
        ref Crafter crafter,
        DynamicBuffer<StoredItemElement> storedItems,
        DynamicBuffer<CrafterRecipeElement> recipes,
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients,
        float deltaTime)
    {
        if (!crafter.selectedItemType.IsValid() ||
            !recipes.TryFindRecipe(crafter.selectedItemType, out CrafterRecipeElement recipe))
        {
            crafter.state = CrafterStateEnum.NoRecipe;
            crafter.progress = 0f;
            return;
        }

        if (storedItems.HasExceptionItem(ingredients, recipe.id))
        {
            crafter.state = CrafterStateEnum.WaitingForExceptionItem;
            crafter.progress = 0f;
            return;
        }

        if (crafter.state == CrafterStateEnum.WaitingForOutput)
            return;

        if (!storedItems.HasIngredients(ingredients, recipe.id) || crafter.speed <= 0f)
        {
            crafter.state = CrafterStateEnum.Idle;
            crafter.progress = 0f;
            return;
        }

        if (crafter.state != CrafterStateEnum.Crafting)
        {
            crafter.state = CrafterStateEnum.Crafting;
            crafter.progress = 0f;
        }

        float requiredTime = recipe.GetCraftTime(crafter.speed);
        crafter.progress += deltaTime;

        if (crafter.progress >= requiredTime)
        {
            crafter.progress = requiredTime;
            crafter.state = CrafterStateEnum.WaitingForOutput;
        }
    }

    private void TryOutput(
        ref EntityCommandBuffer ecb,
        DynamicBuffer<ItemPrefabElement> itemPrefabs,
        DynamicBuffer<CrafterRecipeElement> recipes,
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients,
        ref Crafter crafter,
        int2 crafterCell,
        DirectionEnum direction,
        DynamicBuffer<StoredItemElement> storedItems)
    {
        if (crafter.state != CrafterStateEnum.WaitingForOutput)
            return;
        if (!recipes.TryFindRecipe(crafter.selectedItemType, out CrafterRecipeElement recipe))
        {
            crafter.state = CrafterStateEnum.NoRecipe;
            crafter.progress = 0f;
            return;
        }
        if (storedItems.HasExceptionItem(ingredients, recipe.id))
        {
            crafter.state = CrafterStateEnum.WaitingForExceptionItem;
            crafter.progress = 0f;
            return;
        }
        if (!storedItems.HasIngredients(ingredients, recipe.id))
        {
            crafter.state = CrafterStateEnum.Idle;
            crafter.progress = 0f;
            return;
        }

        Entity itemPrefab = FindPrefab(itemPrefabs, recipe.outputItemType);
        int2 outputCell = crafterCell + direction.ToInt2();

        if (itemPrefab == Entity.Null || !CanOutput(outputCell))
            return;

        ReserveOutputItem(outputCell);
        SpawnItem(ref ecb, itemPrefab, outputCell, recipe.outputItemType);
        ConsumeIngredients(ref ecb, storedItems, ingredients, recipe.id);

        crafter.progress = 0f;
        crafter.state = CrafterStateEnum.Idle;
    }

    private bool CanOutput(int2 outputCell)
    {
        _reservedOutputItemCounts.TryGetValue(outputCell, out int reservedCount);
        return _chunkMap.GetItemCount(outputCell) + reservedCount < GameConstants.MaximumItemInCell;
    }

    private void ReserveOutputItem(int2 outputCell)
    {
        _reservedOutputItemCounts.TryGetValue(outputCell, out int reservedCount);
        _reservedOutputItemCounts[outputCell] = reservedCount + 1;
    }

    private static void SpawnItem(ref EntityCommandBuffer ecb, Entity itemPrefab, int2 outputCell, ItemTypeEnum type)
    {
        Entity item = ecb.Instantiate(itemPrefab);
        ecb.SetComponent(item, LocalTransform.FromPosition(new float3(outputCell.x, outputCell.y, 0f)));
        ecb.AddComponent(item, new GridPosition { gridPosition = outputCell });
        ecb.AddComponent(item, new Item { type = type });
        ecb.AddComponent<ItemCellChanged>(item);
        ecb.SetComponentEnabled<ItemCellChanged>(item, true);
    }

    private void ConsumeIngredients(
        ref EntityCommandBuffer ecb,
        DynamicBuffer<StoredItemElement> storedItems,
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients,
        int recipeId)
    {
        for (int ingredientIndex = 0; ingredientIndex < ingredients.Length; ingredientIndex++)
        {
            CrafterRecipeIngredientElement ingredient = ingredients[ingredientIndex];

            if (ingredient.recipeId != recipeId)
                continue;

            int remainingAmount = ingredient.amount;

            for (int storedIndex = storedItems.Length - 1; storedIndex >= 0 && remainingAmount > 0; storedIndex--)
            {
                StoredItemElement storedItem = storedItems[storedIndex];

                if (storedItem.type != ingredient.itemType)
                    continue;

                _itemStorage.DestroyStoredItem(ref ecb, storedItems, storedIndex);
                remainingAmount--;
            }
        }
    }

    private static Entity FindPrefab(DynamicBuffer<ItemPrefabElement> prefabs, ItemTypeEnum type)
    {
        for (int i = 0; i < prefabs.Length; i++)
        {
            if (prefabs[i].type == type)
                return prefabs[i].prefab;
        }

        return Entity.Null;
    }
}
