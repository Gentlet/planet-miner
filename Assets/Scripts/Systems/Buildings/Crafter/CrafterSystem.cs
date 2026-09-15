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
    private readonly List<int2> _footprintCells = new();
    private readonly List<BuildingBoundaryConnection> _boundaryConnections = new();
    private readonly List<int2> _outputCells = new();
    private readonly List<BuildingInputItem> _inputItems = new();
    private readonly HashSet<Entity> _inputItemDeduplication = new();

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
        _itemStorage = World.GetExistingSystemManaged<ItemStorageSystem>();
        _crafterOutputQuery = GetEntityQuery(
            ComponentType.ReadWrite<Crafter>(),
            ComponentType.ReadOnly<GridPosition>(),
            ComponentType.ReadOnly<BuildingFootprint>(),
            ComponentType.ReadOnly<Direction>(),
            ComponentType.ReadWrite<BuildingOutputCursor>(),
            ComponentType.ReadWrite<StoredItemElement>(),
            ComponentType.ReadWrite<ProducedItemElement>());

        RequireForUpdate<CrafterConfig>();
        RequireForUpdate<ItemPrefabElement>();
        RequireForUpdate<ResearchConfig>();
    }

    protected override void OnUpdate()
    {
        if (!EnsureSystems())
            return;

        using NativeArray<Entity> crafters = _crafterOutputQuery.ToEntityArray(Allocator.Temp);
        TryOutputProducedItems(crafters);

        using NativeArray<CrafterRecipeElement> recipes =
            DynamicBufferCopyUtility.CreateNativeCopy(
                SystemAPI.GetSingletonBuffer<CrafterRecipeElement>(true),
                Allocator.Temp);
        using NativeArray<CrafterRecipeIngredientElement> ingredients =
            DynamicBufferCopyUtility.CreateNativeCopy(
                SystemAPI.GetSingletonBuffer<CrafterRecipeIngredientElement>(true),
                Allocator.Temp);
        using NativeArray<ItemStorageLimitElement> storageLimits =
            DynamicBufferCopyUtility.CreateNativeCopy(
                SystemAPI.GetSingletonBuffer<ItemStorageLimitElement>(true),
                Allocator.Temp);
        EntityCommandBuffer ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(World.Unmanaged);
        float deltaTime = SystemAPI.Time.DeltaTime;
        DynamicBuffer<ResearchStatModifierElement> researchModifiers =
            SystemAPI.GetSingletonBuffer<ResearchStatModifierElement>(true);
        using NativeArray<RecipeUnlockElement> recipeUnlocks =
            DynamicBufferCopyUtility.CreateNativeCopy(
                SystemAPI.GetSingletonBuffer<RecipeUnlockElement>(true),
                Allocator.Temp);
        float craftingSpeedMultiplier = researchModifiers.GetStatMultiplier(
            ResearchStatModifierTypeEnum.CraftingSpeed);

        for (int i = 0; i < crafters.Length; i++)
        {
            Entity crafterEntity = crafters[i];
            Crafter crafter = EntityManager.GetComponentData<Crafter>(crafterEntity);

            if (!IsSelectedRecipeUnlocked(crafter, recipes, recipeUnlocks))
            {
                crafter.selectedItemType = ItemTypeEnum.None;
                crafter.progress = 0f;
                crafter.state = CrafterStateEnum.NoRecipe;
                EntityManager.SetComponentData(crafterEntity, crafter);
                continue;
            }

            int2 crafterCell = EntityManager.GetComponentData<GridPosition>(crafterEntity).gridPosition;
            DirectionEnum direction = EntityManager
                .GetComponentData<Direction>(crafterEntity)
                .dir;
            int2 footprintSize = EntityManager
                .GetComponentData<BuildingFootprint>(crafterEntity)
                .size;

            TryDepositItems(
                crafter,
                crafterEntity,
                crafterCell,
                direction,
                footprintSize,
                recipes,
                ingredients,
                storageLimits);

            DynamicBuffer<StoredItemElement> storedItems =
                EntityManager.GetBuffer<StoredItemElement>(crafterEntity);
            float progressDeltaTime = PowerProductionUtility
                .GetProgressDeltaTime(
                    EntityManager,
                    crafterEntity,
                    deltaTime) * craftingSpeedMultiplier;
            UpdateCrafting(
                ref crafter,
                storedItems,
                recipes,
                ingredients,
                progressDeltaTime);

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

    private static bool IsSelectedRecipeUnlocked(
        in Crafter crafter,
        NativeArray<CrafterRecipeElement> recipes,
        NativeArray<RecipeUnlockElement> recipeUnlocks)
    {
        if (!crafter.selectedItemType.IsValid())
            return true;

        if (!recipes.TryFindRecipe(
                crafter.selectedItemType,
                out CrafterRecipeElement recipe))
            return false;

        return recipeUnlocks.IsRecipeUnlocked(recipe.id);
    }

    private void TryOutputProducedItems(
        NativeArray<Entity> crafters)
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
            int2 footprintSize = EntityManager
                .GetComponentData<BuildingFootprint>(crafterEntity)
                .size;
            BuildingOutputCursor cursor = EntityManager
                .GetComponentData<BuildingOutputCursor>(crafterEntity);
            BuildingBeltConnectionUtility.TryOutputItem<ProducedItemElement>(
                _chunkMap,
                EntityManager,
                _itemStorage,
                crafterEntity,
                crafterCell,
                footprintSize,
                direction,
                ref cursor,
                _boundaryConnections,
                _outputCells);
            EntityManager.SetComponentData(crafterEntity, cursor);
        }
    }

    private void TryDepositItems(
        in Crafter crafter,
        Entity crafterEntity,
        int2 crafterCell,
        DirectionEnum direction,
        int2 crafterSize,
        NativeArray<CrafterRecipeElement> recipes,
        NativeArray<CrafterRecipeIngredientElement> ingredients,
        NativeArray<ItemStorageLimitElement> storageLimits)
    {
        if (!crafter.state.CanReceiveItems() || !crafter.selectedItemType.IsValid())
            return;
        if (!recipes.TryFindRecipe(crafter.selectedItemType, out CrafterRecipeElement recipe))
            return;

        BuildInputItems(crafterCell, crafterSize, direction);

        for (int i = 0; i < _inputItems.Count; i++)
        {
            BuildingInputItem inputItem = _inputItems[i];
            Entity itemEntity = inputItem.Entity;
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

            _itemStorage.TryStoreItemImmediate(
                crafterEntity,
                inputItem.SourceCell,
                itemEntity);
        }
    }

    private void BuildInputItems(
        int2 anchor,
        int2 size,
        DirectionEnum direction)
    {
        BuildingInputCollectionUtility.CollectItems(
            _chunkMap,
            EntityManager,
            anchor,
            size,
            direction,
            _footprintCells,
            _itemsInCell,
            _inputItemDeduplication,
            _inputItems);
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

        if (!EntityManager.Exists(itemEntity))
            return false;

        if (!EntityManager.HasComponent<Item>(itemEntity))
            return false;

        if (EntityManager.HasComponent<StoredItem>(itemEntity))
            return false;

        itemType = EntityManager.GetComponentData<Item>(itemEntity).type;

        if (!ingredients.HasIngredient(recipeId, itemType))
            return false;

        int maxAmount = storageLimits.GetStorageLimit(itemType);
        return maxAmount > 0 && storedItems.CountItems(itemType) < maxAmount;
    }

    private void UpdateCrafting(
        ref Crafter crafter,
        DynamicBuffer<StoredItemElement> storedItems,
        NativeArray<CrafterRecipeElement> recipes,
        NativeArray<CrafterRecipeIngredientElement> ingredients,
        float progressDeltaTime)
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

        if (!HasAvailableIngredients(storedItems, ingredients, recipe.id))
        {
            crafter.state = CrafterStateEnum.Idle;
            crafter.progress = 0f;
            return;
        }

        if (crafter.speed <= 0f)
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
        crafter.progress += progressDeltaTime;

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
        if (!HasAvailableIngredients(storedItems, ingredients, recipe.id))
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

                if (EntityManager.HasComponent<DroneItemReservation>(
                        storedItem.itemEntity))
                    continue;

                _itemStorage.DestroyStoredItem(ref ecb, storedItems, storedIndex);
                remainingAmount--;
            }
        }
    }

    private bool HasAvailableIngredients(
        DynamicBuffer<StoredItemElement> storedItems,
        NativeArray<CrafterRecipeIngredientElement> ingredients,
        int recipeId)
    {
        for (int ingredientIndex = 0;
             ingredientIndex < ingredients.Length;
             ingredientIndex++)
        {
            CrafterRecipeIngredientElement ingredient =
                ingredients[ingredientIndex];

            if (ingredient.recipeId != recipeId)
                continue;

            int availableQuantity = CountAvailableItems(
                storedItems,
                ingredient.itemType);

            if (availableQuantity < ingredient.amount)
                return false;
        }

        return true;
    }

    private int CountAvailableItems(
        DynamicBuffer<StoredItemElement> storedItems,
        ItemTypeEnum itemType)
    {
        int count = 0;

        for (int i = 0; i < storedItems.Length; i++)
        {
            StoredItemElement storedItem = storedItems[i];

            if (storedItem.type != itemType)
                continue;

            if (EntityManager.HasComponent<DroneItemReservation>(
                    storedItem.itemEntity))
                continue;

            count++;
        }

        return count;
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

    private bool EnsureSystems()
    {
        if (_chunkMap == null)
            _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();

        if (_itemStorage == null)
            _itemStorage = World.GetExistingSystemManaged<ItemStorageSystem>();

        return _chunkMap != null && _itemStorage != null;
    }
}
