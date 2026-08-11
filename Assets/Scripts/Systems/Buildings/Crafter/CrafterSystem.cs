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
    private readonly List<InputItem> _inputItems = new();
    private readonly HashSet<Entity> _inputItemDeduplication = new();

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
        _itemStorage = World.GetExistingSystemManaged<ItemStorageSystem>();
        _crafterOutputQuery = GetEntityQuery(
            ComponentType.ReadWrite<Crafter>(),
            ComponentType.ReadOnly<GridPosition>(),
            ComponentType.ReadOnly<Direction>(),
            ComponentType.ReadWrite<BuildingOutputCursor>(),
            ComponentType.ReadWrite<StoredItemElement>(),
            ComponentType.ReadWrite<ProducedItemElement>());

        RequireForUpdate<CrafterConfig>();
        RequireForUpdate<ItemPrefabElement>();
        RequireForUpdate<BuildingPrefabElement>();
    }

    protected override void OnUpdate()
    {
        if (!EnsureSystems())
            return;

        using NativeArray<Entity> crafters = _crafterOutputQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<BuildingPrefabElement> buildingDefinitions =
            DynamicBufferCopyUtility.CreateNativeCopy(
                SystemAPI.GetSingletonBuffer<BuildingPrefabElement>(true),
                Allocator.Temp);
        int2 crafterSize =
            buildingDefinitions.GetFootprintSize(BuildingTypeEnum.Crafter);
        TryOutputProducedItems(crafters, crafterSize);

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

        for (int i = 0; i < crafters.Length; i++)
        {
            Entity crafterEntity = crafters[i];
            Crafter crafter = EntityManager.GetComponentData<Crafter>(crafterEntity);
            int2 crafterCell = EntityManager.GetComponentData<GridPosition>(crafterEntity).gridPosition;
            DirectionEnum direction = EntityManager
                .GetComponentData<Direction>(crafterEntity)
                .dir;

            TryDepositItems(
                crafter,
                crafterEntity,
                crafterCell,
                direction,
                crafterSize,
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

    private void TryOutputProducedItems(
        NativeArray<Entity> crafters,
        int2 crafterSize)
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
            BuildingOutputCursor cursor = EntityManager
                .GetComponentData<BuildingOutputCursor>(crafterEntity);
            BuildingBeltConnectionUtility.TryOutputItem<ProducedItemElement>(
                _chunkMap,
                EntityManager,
                _itemStorage,
                crafterEntity,
                crafterCell,
                crafterSize,
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
            InputItem inputItem = _inputItems[i];
            Entity itemEntity = inputItem.entity;
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
                inputItem.sourceCell,
                itemEntity);
        }
    }

    private void BuildInputItems(
        int2 anchor,
        int2 size,
        DirectionEnum direction)
    {
        _inputItems.Clear();
        _inputItemDeduplication.Clear();
        BuildingFootprintUtility.GetOccupiedCells(
            anchor,
            size,
            direction,
            _footprintCells);

        for (int cellIndex = 0;
             cellIndex < _footprintCells.Count;
             cellIndex++)
        {
            int2 buildingCell = _footprintCells[cellIndex];
            _chunkMap.GetItems(buildingCell, _itemsInCell);

            for (int itemIndex = 0;
                 itemIndex < _itemsInCell.Count;
                 itemIndex++)
            {
                Entity itemEntity = _itemsInCell[itemIndex];

                if (_inputItemDeduplication.Contains(itemEntity))
                    continue;

                _inputItemDeduplication.Add(itemEntity);
                _inputItems.Add(new InputItem
                {
                    entity = itemEntity,
                    sourceCell = buildingCell
                });
            }
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

    private bool EnsureSystems()
    {
        if (_chunkMap == null)
            _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();

        if (_itemStorage == null)
            _itemStorage = World.GetExistingSystemManaged<ItemStorageSystem>();

        return _chunkMap != null && _itemStorage != null;
    }

    private struct InputItem
    {
        public Entity entity;
        public int2 sourceCell;
    }
}
