using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[UpdateAfter(typeof(ItemTrackingSystem))]
[UpdateAfter(typeof(CrafterRecipeChangeSystem))]
[UpdateAfter(typeof(MiningSystem))]
public partial class CrafterSystem : SystemBase
{
    private ChunkMapSystem _chunkMap;
    private ItemStorageSystem _itemStorage;
    private EntityQuery _crafterOutputQuery;
    private readonly List<Entity> _itemsInCell = new();

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
        _itemStorage = World.GetExistingSystemManaged<ItemStorageSystem>();
        _crafterOutputQuery = GetEntityQuery(
            ComponentType.ReadWrite<Crafter>(),
            ComponentType.ReadOnly<GridPosition>(),
            ComponentType.ReadOnly<Direction>(),
            ComponentType.ReadWrite<StoredItemElement>(),
            ComponentType.ReadWrite<ProducedItemElement>());

        RequireForUpdate<CrafterConfig>();
        RequireForUpdate<ItemPrefabElement>();
    }

    protected override void OnUpdate()
    {
        if (!EnsureSystems())
            return;

        using NativeArray<Entity> crafters = _crafterOutputQuery.ToEntityArray(Allocator.Temp);
        TryOutputProducedItems(crafters);

        using NativeArray<CrafterRecipeElement> recipes =
            CopyBuffer(SystemAPI.GetSingletonBuffer<CrafterRecipeElement>(true));
        using NativeArray<CrafterRecipeIngredientElement> ingredients =
            CopyBuffer(SystemAPI.GetSingletonBuffer<CrafterRecipeIngredientElement>(true));
        using NativeArray<ItemStorageLimitElement> storageLimits =
            CopyBuffer(SystemAPI.GetSingletonBuffer<ItemStorageLimitElement>(true));
        EntityCommandBuffer ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(World.Unmanaged);
        float deltaTime = SystemAPI.Time.DeltaTime;

        for (int i = 0; i < crafters.Length; i++)
        {
            Entity crafterEntity = crafters[i];
            Crafter crafter = EntityManager.GetComponentData<Crafter>(crafterEntity);
            int2 crafterCell = EntityManager.GetComponentData<GridPosition>(crafterEntity).gridPosition;

            TryDepositItems(
                crafter,
                crafterEntity,
                crafterCell,
                recipes,
                ingredients,
                storageLimits);

            DynamicBuffer<StoredItemElement> storedItems =
                EntityManager.GetBuffer<StoredItemElement>(crafterEntity);
            UpdateCrafting(
                ref crafter,
                storedItems,
                recipes,
                ingredients,
                deltaTime);

            TryCompleteCraft(
                ref ecb,
                recipes,
                ingredients,
                storageLimits,
                ref crafter,
                crafterEntity,
                storedItems,
                EntityManager.GetBuffer<ProducedItemElement>(crafterEntity));

            EntityManager.SetComponentData(crafterEntity, crafter);
        }
    }

    private void TryOutputProducedItems(NativeArray<Entity> crafters)
    {
        for (int i = 0; i < crafters.Length; i++)
        {
            Entity crafterEntity = crafters[i];
            DynamicBuffer<ProducedItemElement> producedItems =
                EntityManager.GetBuffer<ProducedItemElement>(crafterEntity);

            if (producedItems.Length == 0)
                continue;

            int2 crafterCell = EntityManager.GetComponentData<GridPosition>(crafterEntity).gridPosition;
            DirectionEnum direction = EntityManager.GetComponentData<Direction>(crafterEntity).dir;
            int2 outputCell = crafterCell + direction.ToInt2();
            _itemStorage.TryRestoreProducedItemImmediate(crafterEntity, 0, outputCell);
        }
    }

    private void TryDepositItems(
        in Crafter crafter,
        Entity crafterEntity,
        int2 crafterCell,
        NativeArray<CrafterRecipeElement> recipes,
        NativeArray<CrafterRecipeIngredientElement> ingredients,
        NativeArray<ItemStorageLimitElement> storageLimits)
    {
        if (!crafter.state.CanReceiveItems() || !crafter.selectedItemType.IsValid())
            return;
        if (!recipes.TryFindRecipe(crafter.selectedItemType, out CrafterRecipeElement recipe))
            return;

        _chunkMap.GetItems(crafterCell, _itemsInCell);

        for (int i = 0; i < _itemsInCell.Count; i++)
        {
            Entity itemEntity = _itemsInCell[i];
            DynamicBuffer<StoredItemElement> storedItems =
                EntityManager.GetBuffer<StoredItemElement>(crafterEntity);

            if (!CanDepositItem(
                    itemEntity,
                    storedItems,
                    ingredients,
                    storageLimits,
                    recipe.id,
                    out _))
                continue;

            _itemStorage.TryStoreItemImmediate(crafterEntity, crafterCell, itemEntity);
        }
    }

    private bool CanDepositItem(
        Entity itemEntity,
        DynamicBuffer<StoredItemElement> storedItems,
        NativeArray<CrafterRecipeIngredientElement> ingredients,
        NativeArray<ItemStorageLimitElement> storageLimits,
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
        NativeArray<CrafterRecipeElement> recipes,
        NativeArray<CrafterRecipeIngredientElement> ingredients,
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

    private void TryCompleteCraft(
        ref EntityCommandBuffer ecb,
        NativeArray<CrafterRecipeElement> recipes,
        NativeArray<CrafterRecipeIngredientElement> ingredients,
        NativeArray<ItemStorageLimitElement> storageLimits,
        ref Crafter crafter,
        Entity crafterEntity,
        DynamicBuffer<StoredItemElement> storedItems,
        DynamicBuffer<ProducedItemElement> producedItems)
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

        int storageLimit = storageLimits.GetStorageLimit(recipe.outputItemType);

        if (storageLimit <= 0 ||
            producedItems.CountItems(recipe.outputItemType) >= storageLimit)
            return;

        CreateItemSpawnRequest(ref ecb, crafterEntity, recipe.outputItemType);
        ConsumeIngredients(ref ecb, storedItems, ingredients, recipe.id);
        crafter.progress = 0f;
        crafter.state = CrafterStateEnum.Idle;
    }

    private void ConsumeIngredients(
        ref EntityCommandBuffer ecb,
        DynamicBuffer<StoredItemElement> storedItems,
        NativeArray<CrafterRecipeIngredientElement> ingredients,
        int recipeId)
    {
        for (int ingredientIndex = 0; ingredientIndex < ingredients.Length; ingredientIndex++)
        {
            CrafterRecipeIngredientElement ingredient = ingredients[ingredientIndex];

            if (ingredient.recipeId != recipeId)
                continue;

            int remainingAmount = ingredient.amount;

            for (int storedIndex = storedItems.Length - 1;
                 storedIndex >= 0 && remainingAmount > 0;
                 storedIndex--)
            {
                StoredItemElement storedItem = storedItems[storedIndex];

                if (storedItem.type != ingredient.itemType)
                    continue;

                _itemStorage.DestroyStoredItem(ref ecb, storedItems, storedIndex);
                remainingAmount--;
            }
        }
    }

    private static void CreateItemSpawnRequest(
        ref EntityCommandBuffer ecb,
        Entity owner,
        ItemTypeEnum itemType)
    {
        Entity requestEntity = ecb.CreateEntity();
        ecb.AddComponent(requestEntity, new ItemSpawnRequest
        {
            owner = owner,
            itemType = itemType
        });
    }

    private static NativeArray<T> CopyBuffer<T>(DynamicBuffer<T> buffer)
        where T : unmanaged, IBufferElementData
    {
        NativeArray<T> copy = new NativeArray<T>(buffer.Length, Allocator.Temp);

        for (int i = 0; i < buffer.Length; i++)
            copy[i] = buffer[i];

        return copy;
    }

    private bool EnsureSystems()
    {
        if (_chunkMap == null)
            _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();

        if (_itemStorage == null)
            _itemStorage = World.GetExistingSystemManaged<ItemStorageSystem>();

        return _chunkMap != null && _itemStorage != null;
    }
}
