using Unity.Collections;
using Unity.Entities;

[UpdateBefore(typeof(ResearchSystem))]
public partial class ResearchSelectionSystem : SystemBase
{
    private EntityQuery _requestQuery;

    protected override void OnCreate()
    {
        _requestQuery = GetEntityQuery(
            ComponentType.ReadOnly<ResearchSelectionRequest>());
        RequireForUpdate<ResearchConfig>();
        RequireForUpdate<ResearchSelectionRequest>();
    }

    protected override void OnUpdate()
    {
        Entity configEntity = SystemAPI.GetSingletonEntity<ResearchConfig>();
        DynamicBuffer<ResearchDefinitionElement> definitions =
            EntityManager.GetBuffer<ResearchDefinitionElement>(configEntity, true);
        DynamicBuffer<ResearchPrerequisiteElement> prerequisites =
            EntityManager.GetBuffer<ResearchPrerequisiteElement>(configEntity, true);
        DynamicBuffer<ResearchIngredientElement> ingredients =
            EntityManager.GetBuffer<ResearchIngredientElement>(configEntity, true);
        DynamicBuffer<ResearchProgressElement> progress =
            EntityManager.GetBuffer<ResearchProgressElement>(configEntity, true);
        ResearchState researchState = EntityManager
            .GetComponentData<ResearchState>(configEntity);
        EntityCommandBuffer ecb = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(World.Unmanaged);

        using NativeArray<Entity> requests = _requestQuery
            .ToEntityArray(Allocator.Temp);

        for (int i = 0; i < requests.Length; i++)
        {
            Entity requestEntity = requests[i];
            ResearchSelectionRequest request = EntityManager
                .GetComponentData<ResearchSelectionRequest>(requestEntity);

            if (CanSelectResearch(
                    request.researchId,
                    definitions,
                    prerequisites,
                    progress) &&
                !researchState.activeResearchId.Equals(request.researchId))
            {
                researchState.activeResearchId = request.researchId;
                EntityManager.SetComponentData(configEntity, researchState);
                ResetResearchBuildings(
                    request.researchId,
                    ingredients,
                    ref ecb);
            }

            ecb.DestroyEntity(requestEntity);
        }
    }

    private static bool CanSelectResearch(
        FixedString64Bytes researchId,
        DynamicBuffer<ResearchDefinitionElement> definitions,
        DynamicBuffer<ResearchPrerequisiteElement> prerequisites,
        DynamicBuffer<ResearchProgressElement> progress)
    {
        if (!definitions.TryGetDefinition(researchId, out _))
            return false;

        if (progress.IsCompleted(researchId))
            return false;

        return prerequisites.ArePrerequisitesCompleted(progress, researchId);
    }

    private void ResetResearchBuildings(
        FixedString64Bytes selectedResearchId,
        DynamicBuffer<ResearchIngredientElement> ingredients,
        ref EntityCommandBuffer ecb)
    {
        foreach (var (researchBuildingReference, buildingEntity) in
                 SystemAPI.Query<RefRW<ResearchBuilding>>().WithEntityAccess())
        {
            ResearchBuilding researchBuilding = researchBuildingReference.ValueRO;
            bool lostActiveCycle = researchBuilding.cycleActive;
            researchBuilding.cycleActive = false;
            researchBuilding.cycleResearchId = default;
            researchBuilding.progress = 0f;
            researchBuilding.state = ResearchBuildingStateEnum.WaitingForMaterials;
            researchBuilding.resetReason = lostActiveCycle
                ? ResearchCycleResetReasonEnum.ResearchChanged
                : ResearchCycleResetReasonEnum.None;
            researchBuilding.resetNoticeRemaining = lostActiveCycle ? 2f : 0f;
            researchBuildingReference.ValueRW = researchBuilding;

            CreateIncompatibleItemRemovalRequests(
                buildingEntity,
                selectedResearchId,
                ingredients,
                ref ecb);
        }
    }

    private void CreateIncompatibleItemRemovalRequests(
        Entity buildingEntity,
        FixedString64Bytes selectedResearchId,
        DynamicBuffer<ResearchIngredientElement> ingredients,
        ref EntityCommandBuffer ecb)
    {
        if (!EntityManager.HasBuffer<StoredItemElement>(buildingEntity))
            return;

        DynamicBuffer<StoredItemElement> storedItems = EntityManager
            .GetBuffer<StoredItemElement>(buildingEntity, true);

        for (ItemTypeEnum itemType = ItemTypeEnum.Iron_Ore;
             itemType < ItemTypeEnum.Count;
             itemType++)
        {
            if (ingredients.HasIngredient(selectedResearchId, itemType))
                continue;

            int storedQuantity = storedItems.CountItems(itemType);

            if (storedQuantity <= 0)
                continue;

            DroneBuildingItemRequestUtility.TryCreateUncoveredRemovalRequest(
                EntityManager,
                ref ecb,
                buildingEntity,
                itemType,
                storedQuantity);
        }
    }
}
