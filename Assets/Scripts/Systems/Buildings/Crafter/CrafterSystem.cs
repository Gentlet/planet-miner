using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateAfter(typeof(ItemTrackingSystem))]
[UpdateAfter(typeof(CrafterRecipeChangeSystem))]
public partial class CrafterSystem : SystemBase
{
    private ChunkMapSystem _chunkMap;
    private readonly List<Entity> _itemsInCell = new List<Entity>();
    private readonly Dictionary<int2, int> _reservedOutputItemCounts = new Dictionary<int2, int>();

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
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

        DynamicBuffer<CrafterRecipeElement> recipes = SystemAPI.GetSingletonBuffer<CrafterRecipeElement>(true);
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients = SystemAPI.GetSingletonBuffer<CrafterRecipeIngredientElement>(true);
        DynamicBuffer<ItemStorageLimitElement> storageLimits = SystemAPI.GetSingletonBuffer<ItemStorageLimitElement>(true);
        DynamicBuffer<ItemPrefabElement> itemPrefabs = SystemAPI.GetSingletonBuffer<ItemPrefabElement>(true);
        EntityCommandBuffer ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);
        float deltaTime = SystemAPI.Time.DeltaTime;

        _reservedOutputItemCounts.Clear();

        foreach (var (crafter, gridPosition, direction, depositedItems, crafterEntity) in
                 SystemAPI.Query<RefRW<Crafter>, RefRO<GridPosition>, RefRO<Direction>, DynamicBuffer<CrafterDepositedItemElement>>().WithEntityAccess())
        {
            int2 crafterCell = gridPosition.ValueRO.gridPosition;

            TryDepositItems(ref ecb, crafter.ValueRO, depositedItems, crafterEntity, crafterCell, recipes, ingredients, storageLimits);
            UpdateCrafting(ref crafter.ValueRW, depositedItems, recipes, ingredients, deltaTime);
            TryOutput(ref ecb, itemPrefabs, recipes, ingredients, ref crafter.ValueRW, crafterCell, direction.ValueRO.dir, depositedItems);
        }
    }

    private void TryDepositItems(
        ref EntityCommandBuffer ecb,
        in Crafter crafter,
        DynamicBuffer<CrafterDepositedItemElement> depositedItems,
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

            if (!CanDepositItem(itemEntity, depositedItems, ingredients, storageLimits, recipe.id, out ItemTypeEnum itemType))
                continue;

            DepositItem(ref ecb, depositedItems, crafterEntity, crafterCell, itemEntity, itemType);
        }
    }

    private bool CanDepositItem(
        Entity itemEntity,
        DynamicBuffer<CrafterDepositedItemElement> depositedItems,
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients,
        DynamicBuffer<ItemStorageLimitElement> storageLimits,
        int recipeId,
        out ItemTypeEnum itemType)
    {
        itemType = ItemTypeEnum.None;

        if (!EntityManager.Exists(itemEntity) ||
            !EntityManager.HasComponent<Item>(itemEntity) ||
            EntityManager.HasComponent<DepositedItem>(itemEntity))
            return false;

        itemType = EntityManager.GetComponentData<Item>(itemEntity).type;

        if (!ingredients.HasIngredient(recipeId, itemType))
            return false;

        int maxAmount = storageLimits.GetStorageLimit(itemType);

        return maxAmount > 0 && depositedItems.CountItems(itemType) < maxAmount;
    }

    private void DepositItem(
        ref EntityCommandBuffer ecb,
        DynamicBuffer<CrafterDepositedItemElement> depositedItems,
        Entity crafterEntity,
        int2 crafterCell,
        Entity itemEntity,
        ItemTypeEnum itemType)
    {
        depositedItems.Add(new CrafterDepositedItemElement
        {
            itemEntity = itemEntity,
            type = itemType
        });

        _chunkMap.TryUnregisterItem(crafterCell, itemEntity);

        if (EntityManager.HasComponent<LocalTransform>(itemEntity))
        {
            LocalTransform transform = EntityManager.GetComponentData<LocalTransform>(itemEntity);
            transform.Position = new float3(crafterCell.x, crafterCell.y, transform.Position.z);
            ecb.SetComponent(itemEntity, transform);
        }

        if (EntityManager.HasComponent<GridPosition>(itemEntity))
            ecb.SetComponent(itemEntity, new GridPosition { gridPosition = crafterCell });
        else
            ecb.AddComponent(itemEntity, new GridPosition { gridPosition = crafterCell });

        ecb.AddComponent(itemEntity, new DepositedItem { owner = crafterEntity });

        if (!EntityManager.HasComponent<Disabled>(itemEntity))
            ecb.AddComponent<Disabled>(itemEntity);
    }

    private static void UpdateCrafting(
        ref Crafter crafter,
        DynamicBuffer<CrafterDepositedItemElement> depositedItems,
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

        if (depositedItems.HasExceptionItem(ingredients, recipe.id))
        {
            crafter.state = CrafterStateEnum.WaitingForExceptionItem;
            crafter.progress = 0f;
            return;
        }

        if (crafter.state == CrafterStateEnum.WaitingForOutput)
            return;

        if (!depositedItems.HasIngredients(ingredients, recipe.id) || crafter.speed <= 0f)
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
        DynamicBuffer<CrafterDepositedItemElement> depositedItems)
    {
        if (crafter.state != CrafterStateEnum.WaitingForOutput)
            return;
        if (!recipes.TryFindRecipe(crafter.selectedItemType, out CrafterRecipeElement recipe))
        {
            crafter.state = CrafterStateEnum.NoRecipe;
            crafter.progress = 0f;
            return;
        }
        if (depositedItems.HasExceptionItem(ingredients, recipe.id))
        {
            crafter.state = CrafterStateEnum.WaitingForExceptionItem;
            crafter.progress = 0f;
            return;
        }
        if (!depositedItems.HasIngredients(ingredients, recipe.id))
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
        ConsumeIngredients(ref ecb, depositedItems, ingredients, recipe.id);

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
    }

    private static void ConsumeIngredients(
        ref EntityCommandBuffer ecb,
        DynamicBuffer<CrafterDepositedItemElement> depositedItems,
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients,
        int recipeId)
    {
        for (int ingredientIndex = 0; ingredientIndex < ingredients.Length; ingredientIndex++)
        {
            CrafterRecipeIngredientElement ingredient = ingredients[ingredientIndex];

            if (ingredient.recipeId != recipeId)
                continue;

            int remainingAmount = ingredient.amount;

            for (int depositedIndex = depositedItems.Length - 1; depositedIndex >= 0 && remainingAmount > 0; depositedIndex--)
            {
                CrafterDepositedItemElement depositedItem = depositedItems[depositedIndex];

                if (depositedItem.type != ingredient.itemType)
                    continue;

                ecb.DestroyEntity(depositedItem.itemEntity);
                depositedItems.RemoveAt(depositedIndex);
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
