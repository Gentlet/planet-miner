using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[UpdateAfter(typeof(ItemTrackingSystem))]
[UpdateAfter(typeof(ResearchSelectionSystem))]
[UpdateAfter(typeof(CrafterSystem))]
public partial class ResearchSystem : SystemBase
{
    private const int InputBufferCycleCount = 2;

    private ChunkMapSystem _chunkMap;
    private ItemStorageSystem _itemStorage;
    private ItemTrackingSystem _itemTracking;
    private EntityQuery _researchBuildingQuery;
    private readonly List<Entity> _itemsInCell = new();
    private readonly List<int2> _footprintCells = new();
    private readonly List<BuildingInputItem> _inputItems = new();
    private readonly HashSet<Entity> _inputItemDeduplication = new();

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
        _itemStorage = World.GetExistingSystemManaged<ItemStorageSystem>();
        _itemTracking = World.GetExistingSystemManaged<ItemTrackingSystem>();
        _researchBuildingQuery = GetEntityQuery(
            ComponentType.ReadWrite<ResearchBuilding>(),
            ComponentType.ReadOnly<GridPosition>(),
            ComponentType.ReadOnly<BuildingFootprint>(),
            ComponentType.ReadOnly<Direction>(),
            ComponentType.ReadWrite<StoredItemElement>());

        RequireForUpdate<ResearchConfig>();
    }

    protected override void OnUpdate()
    {
        if (!EnsureSystems())
            return;

        _itemTracking.ApplyPendingChangesImmediate();
        Entity configEntity = SystemAPI.GetSingletonEntity<ResearchConfig>();
        ResearchState researchState = EntityManager
            .GetComponentData<ResearchState>(configEntity);
        DynamicBuffer<ResearchDefinitionElement> definitions = EntityManager
            .GetBuffer<ResearchDefinitionElement>(configEntity, true);
        using NativeArray<ResearchIngredientElement> ingredients =
            DynamicBufferCopyUtility.CreateNativeCopy(
                EntityManager.GetBuffer<ResearchIngredientElement>(
                    configEntity,
                    true),
                Allocator.Temp);
        using NativeArray<ResearchRewardElement> rewards =
            DynamicBufferCopyUtility.CreateNativeCopy(
                EntityManager.GetBuffer<ResearchRewardElement>(
                    configEntity,
                    true),
                Allocator.Temp);
        DynamicBuffer<ResearchStatModifierElement> statModifiers = EntityManager
            .GetBuffer<ResearchStatModifierElement>(configEntity, true);
        float researchSpeedMultiplier = statModifiers.GetStatMultiplier(
            ResearchStatModifierTypeEnum.ResearchSpeed);
        float deltaTime = SystemAPI.Time.DeltaTime;
        EntityCommandBuffer ecb = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(World.Unmanaged);

        using NativeArray<Entity> researchBuildings = _researchBuildingQuery
            .ToEntityArray(Allocator.Temp);

        if (researchState.activeResearchId.Length == 0 ||
            !definitions.TryGetDefinition(
                researchState.activeResearchId,
                out ResearchDefinitionElement activeResearch))
        {
            SetAllBuildingsInactive(researchBuildings, deltaTime);
            return;
        }

        bool researchCompleted = false;

        for (int i = 0; i < researchBuildings.Length; i++)
        {
            Entity buildingEntity = researchBuildings[i];
            ResearchBuilding researchBuilding = EntityManager
                .GetComponentData<ResearchBuilding>(buildingEntity);
            TickResetNotice(ref researchBuilding, deltaTime);
            ResetMismatchedCycle(
                ref researchBuilding,
                researchState.activeResearchId);
            CollectInputItems(
                buildingEntity,
                researchState.activeResearchId,
                ingredients);

            if (!researchBuilding.cycleActive)
            {
                TryStartCycle(
                    ref ecb,
                    buildingEntity,
                    researchState.activeResearchId,
                    ingredients,
                    ref researchBuilding);
                EntityManager.SetComponentData(buildingEntity, researchBuilding);
                continue;
            }

            float powerDeltaTime = PowerProductionUtility.GetProgressDeltaTime(
                EntityManager,
                buildingEntity,
                deltaTime);

            if (powerDeltaTime <= 0f)
            {
                researchBuilding.state = ResearchBuildingStateEnum.NoPower;
                EntityManager.SetComponentData(buildingEntity, researchBuilding);
                continue;
            }

            researchBuilding.state = ResearchBuildingStateEnum.Researching;
            researchBuilding.progress +=
                powerDeltaTime * researchBuilding.speed * researchSpeedMultiplier;

            if (researchBuilding.progress < activeResearch.cycleDuration)
            {
                EntityManager.SetComponentData(buildingEntity, researchBuilding);
                continue;
            }

            researchBuilding.cycleActive = false;
            researchBuilding.cycleResearchId = default;
            researchBuilding.progress = 0f;
            researchBuilding.state = ResearchBuildingStateEnum.WaitingForMaterials;
            EntityManager.SetComponentData(buildingEntity, researchBuilding);

            if (AddResearchProgress(
                    activeResearch,
                    EntityManager.GetBuffer<ResearchProgressElement>(
                        configEntity)))
            {
                CompleteResearch(
                    activeResearch.stableId,
                    rewards,
                    EntityManager.GetBuffer<ResearchProgressElement>(
                        configEntity),
                    EntityManager.GetBuffer<BuildingUnlockElement>(
                        configEntity),
                    EntityManager.GetBuffer<RecipeUnlockElement>(
                        configEntity),
                    EntityManager.GetBuffer<ResearchStatModifierElement>(
                        configEntity));
                researchState.activeResearchId = default;
                EntityManager.SetComponentData(configEntity, researchState);
                researchCompleted = true;
                break;
            }
        }

        if (researchCompleted)
            ResetBuildingsAfterCompletion(researchBuildings);
    }

    private void CollectInputItems(
        Entity buildingEntity,
        FixedString64Bytes researchId,
        NativeArray<ResearchIngredientElement> ingredients)
    {
        int2 anchor = EntityManager
            .GetComponentData<GridPosition>(buildingEntity)
            .gridPosition;
        DirectionEnum direction = EntityManager
            .GetComponentData<Direction>(buildingEntity)
            .dir;
        int2 footprintSize = EntityManager
            .GetComponentData<BuildingFootprint>(buildingEntity)
            .size;
        BuildingInputCollectionUtility.CollectItems(
            _chunkMap,
            EntityManager,
            anchor,
            footprintSize,
            direction,
            _footprintCells,
            _itemsInCell,
            _inputItemDeduplication,
            _inputItems);

        for (int i = 0; i < _inputItems.Count; i++)
        {
            BuildingInputItem inputItem = _inputItems[i];

            if (!CanStoreInputItem(
                    buildingEntity,
                    inputItem.Entity,
                    researchId,
                    ingredients))
                continue;

            _itemStorage.TryStoreItemImmediate(
                buildingEntity,
                inputItem.SourceCell,
                inputItem.Entity);
        }
    }

    private bool CanStoreInputItem(
        Entity buildingEntity,
        Entity itemEntity,
        FixedString64Bytes researchId,
        NativeArray<ResearchIngredientElement> ingredients)
    {
        if (!EntityManager.Exists(itemEntity))
            return false;

        if (!EntityManager.HasComponent<Item>(itemEntity))
            return false;

        if (EntityManager.HasComponent<StoredItem>(itemEntity))
            return false;

        ItemTypeEnum itemType = EntityManager
            .GetComponentData<Item>(itemEntity)
            .type;
        int amountPerCycle = ingredients.GetIngredientAmount(
            researchId,
            itemType);

        if (amountPerCycle <= 0)
            return false;

        DynamicBuffer<StoredItemElement> storedItems = EntityManager
            .GetBuffer<StoredItemElement>(buildingEntity, true);
        int capacity = amountPerCycle * InputBufferCycleCount;
        return storedItems.CountItems(itemType) < capacity;
    }

    private void TryStartCycle(
        ref EntityCommandBuffer ecb,
        Entity buildingEntity,
        FixedString64Bytes researchId,
        NativeArray<ResearchIngredientElement> ingredients,
        ref ResearchBuilding researchBuilding)
    {
        if (!HasAvailableIngredients(
                buildingEntity,
                researchId,
                ingredients))
        {
            researchBuilding.state = ResearchBuildingStateEnum.WaitingForMaterials;
            return;
        }

        float powerDeltaTime = PowerProductionUtility.GetProgressDeltaTime(
            EntityManager,
            buildingEntity,
            1f);

        if (powerDeltaTime <= 0f)
        {
            researchBuilding.state = ResearchBuildingStateEnum.NoPower;
            return;
        }

        ConsumeIngredients(
            ref ecb,
            buildingEntity,
            researchId,
            ingredients);
        researchBuilding.cycleResearchId = researchId;
        researchBuilding.progress = 0f;
        researchBuilding.cycleActive = true;
        researchBuilding.state = ResearchBuildingStateEnum.Researching;
    }

    private bool HasAvailableIngredients(
        Entity buildingEntity,
        FixedString64Bytes researchId,
        NativeArray<ResearchIngredientElement> ingredients)
    {
        DynamicBuffer<StoredItemElement> storedItems = EntityManager
            .GetBuffer<StoredItemElement>(buildingEntity, true);

        for (int i = 0; i < ingredients.Length; i++)
        {
            ResearchIngredientElement ingredient = ingredients[i];

            if (!ingredient.researchId.Equals(researchId))
                continue;

            int availableQuantity = 0;

            for (int storedIndex = 0; storedIndex < storedItems.Length; storedIndex++)
            {
                StoredItemElement storedItem = storedItems[storedIndex];

                if (storedItem.type != ingredient.itemType)
                    continue;

                if (EntityManager.HasComponent<DroneItemReservation>(
                        storedItem.itemEntity))
                    continue;

                availableQuantity++;
            }

            if (availableQuantity < ingredient.amount)
                return false;
        }

        return true;
    }

    private void ConsumeIngredients(
        ref EntityCommandBuffer ecb,
        Entity buildingEntity,
        FixedString64Bytes researchId,
        NativeArray<ResearchIngredientElement> ingredients)
    {
        for (int ingredientIndex = 0;
             ingredientIndex < ingredients.Length;
             ingredientIndex++)
        {
            ResearchIngredientElement ingredient = ingredients[ingredientIndex];

            if (!ingredient.researchId.Equals(researchId))
                continue;

            int remainingAmount = ingredient.amount;
            DynamicBuffer<StoredItemElement> storedItems = EntityManager
                .GetBuffer<StoredItemElement>(buildingEntity);

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

                _itemStorage.DestroyStoredItem(
                    ref ecb,
                    storedItems,
                    storedIndex);
                remainingAmount--;
            }
        }
    }

    private static bool AddResearchProgress(
        ResearchDefinitionElement definition,
        DynamicBuffer<ResearchProgressElement> progress)
    {
        int progressIndex = progress.FindProgressIndex(definition.stableId);

        if (progressIndex < 0)
            return false;

        ResearchProgressElement progressElement = progress[progressIndex];
        progressElement.progress = math.min(
            definition.requiredProgress,
            progressElement.progress + definition.progressPerCycle);
        progress[progressIndex] = progressElement;
        return progressElement.progress >= definition.requiredProgress;
    }

    private static void CompleteResearch(
        FixedString64Bytes researchId,
        NativeArray<ResearchRewardElement> rewards,
        DynamicBuffer<ResearchProgressElement> progress,
        DynamicBuffer<BuildingUnlockElement> buildingUnlocks,
        DynamicBuffer<RecipeUnlockElement> recipeUnlocks,
        DynamicBuffer<ResearchStatModifierElement> statModifiers)
    {
        int progressIndex = progress.FindProgressIndex(researchId);

        if (progressIndex < 0)
            return;

        ResearchProgressElement progressElement = progress[progressIndex];

        if (progressElement.completed)
            return;

        progressElement.completed = true;
        progress[progressIndex] = progressElement;

        for (int i = 0; i < rewards.Length; i++)
        {
            ResearchRewardElement reward = rewards[i];

            if (!reward.researchId.Equals(researchId))
                continue;

            switch (reward.type)
            {
                case ResearchRewardTypeEnum.BuildingUnlock:
                    if (!buildingUnlocks.IsBuildingUnlocked(reward.buildingType))
                    {
                        buildingUnlocks.Add(new BuildingUnlockElement
                        {
                            buildingType = reward.buildingType
                        });
                    }
                    break;

                case ResearchRewardTypeEnum.RecipeUnlock:
                    if (!recipeUnlocks.IsRecipeUnlocked(reward.recipeId))
                    {
                        recipeUnlocks.Add(new RecipeUnlockElement
                        {
                            recipeId = reward.recipeId
                        });
                    }
                    break;

                case ResearchRewardTypeEnum.StatModifier:
                    statModifiers.Add(new ResearchStatModifierElement
                    {
                        type = reward.statType,
                        percentBonus = reward.percentBonus
                    });
                    break;
            }
        }
    }

    private void SetAllBuildingsInactive(
        NativeArray<Entity> researchBuildings,
        float deltaTime)
    {
        for (int i = 0; i < researchBuildings.Length; i++)
        {
            Entity buildingEntity = researchBuildings[i];
            ResearchBuilding researchBuilding = EntityManager
                .GetComponentData<ResearchBuilding>(buildingEntity);
            TickResetNotice(ref researchBuilding, deltaTime);
            researchBuilding.cycleResearchId = default;
            researchBuilding.progress = 0f;
            researchBuilding.cycleActive = false;
            researchBuilding.state = ResearchBuildingStateEnum.NoActiveResearch;
            EntityManager.SetComponentData(buildingEntity, researchBuilding);
        }
    }

    private void ResetBuildingsAfterCompletion(
        NativeArray<Entity> researchBuildings)
    {
        for (int i = 0; i < researchBuildings.Length; i++)
        {
            Entity buildingEntity = researchBuildings[i];
            ResearchBuilding researchBuilding = EntityManager
                .GetComponentData<ResearchBuilding>(buildingEntity);
            bool lostActiveCycle = researchBuilding.cycleActive;
            researchBuilding.cycleResearchId = default;
            researchBuilding.progress = 0f;
            researchBuilding.cycleActive = false;
            researchBuilding.state = ResearchBuildingStateEnum.NoActiveResearch;
            researchBuilding.resetReason = lostActiveCycle
                ? ResearchCycleResetReasonEnum.ResearchCompleted
                : ResearchCycleResetReasonEnum.None;
            researchBuilding.resetNoticeRemaining = lostActiveCycle ? 2f : 0f;
            EntityManager.SetComponentData(buildingEntity, researchBuilding);
        }
    }

    private static void ResetMismatchedCycle(
        ref ResearchBuilding researchBuilding,
        FixedString64Bytes activeResearchId)
    {
        if (!researchBuilding.cycleActive)
            return;

        if (researchBuilding.cycleResearchId.Equals(activeResearchId))
            return;

        researchBuilding.cycleResearchId = default;
        researchBuilding.progress = 0f;
        researchBuilding.cycleActive = false;
        researchBuilding.resetReason = ResearchCycleResetReasonEnum.ResearchChanged;
        researchBuilding.resetNoticeRemaining = 2f;
    }

    private static void TickResetNotice(
        ref ResearchBuilding researchBuilding,
        float deltaTime)
    {
        if (researchBuilding.resetNoticeRemaining <= 0f)
            return;

        researchBuilding.resetNoticeRemaining = math.max(
            0f,
            researchBuilding.resetNoticeRemaining - deltaTime);

        if (researchBuilding.resetNoticeRemaining <= 0f)
            researchBuilding.resetReason = ResearchCycleResetReasonEnum.None;
    }

    private bool EnsureSystems()
    {
        if (_chunkMap == null)
            _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();

        if (_itemStorage == null)
            _itemStorage = World.GetExistingSystemManaged<ItemStorageSystem>();

        if (_itemTracking == null)
            _itemTracking = World.GetExistingSystemManaged<ItemTrackingSystem>();

        return _chunkMap != null &&
               _itemStorage != null &&
               _itemTracking != null;
    }
}
