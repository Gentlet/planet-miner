using Unity.Entities;
using Unity.Mathematics;

/// <summary>지정한 실물 영역의 자격/재고/입력 한도를 읽는다. 자동 상대 탐색과 영속 예약은 소유하지 않는다.</summary>
public static class DroneTransportEndpointUtility
{
    public static bool IsArea(EntityManager manager, Entity owner, DroneTransportAreaEnum area)
    {
        if (area == DroneTransportAreaEnum.GeneralStorage)
        {
            return DroneSchedulingUtility.IsStorage(manager, owner);
        }

        if (!DroneSchedulingUtility.IsActiveEntity(manager, owner))
        {
            return false;
        }

        if (!manager.HasComponent<BuildingType>(owner) || !manager.HasComponent<GridPosition>(owner))
        {
            return false;
        }

        if (manager.HasComponent<PendingBuildingDemolition>(owner))
        {
            return false;
        }

        var type = manager.GetComponentData<BuildingType>(owner).Type;
        if (area == DroneTransportAreaEnum.Product)
        {
            return (type == BuildingTypeEnum.Miner || type == BuildingTypeEnum.Crafter) && manager.HasBuffer<ProductItemElement>(owner);
        }

        if (area != DroneTransportAreaEnum.Input)
        {
            return false;
        }

        return (type == BuildingTypeEnum.Crafter || type == BuildingTypeEnum.CoalGenerator || type == BuildingTypeEnum.ResearchBuilding) && manager.HasComponent<Storage>(owner) && manager.HasBuffer<StoredItemElement>(owner);
    }

    public static bool IsTarget(EntityManager manager, in DroneLogisticsTask task)
    {
        if (!IsArea(manager, task.Target, task.Area))
        {
            return false;
        }

        if (task.Purpose == DroneTransportPurposeEnum.ResearchRecovery)
        {
            return task.Kind == DroneLogisticsTaskKindEnum.StorageExport && task.Area == DroneTransportAreaEnum.Input && ResearchRecoveryUtility.HasTaskNeed(manager, task);
        }

        return task.Purpose == DroneTransportPurposeEnum.Explicit && (task.Kind == DroneLogisticsTaskKindEnum.StorageExport || task.Area != DroneTransportAreaEnum.Product);
    }

    public static int CountSource(EntityManager manager, Entity owner, ItemTypeEnum type, DroneTransportAreaEnum area, DroneTransportPurposeEnum purpose = default)
    {
        if (!IsArea(manager, owner, area))
        {
            return 0;
        }

        if (purpose == DroneTransportPurposeEnum.ResearchRecovery)
        {
            return ResearchRecoveryUtility.Count(manager, owner, type);
        }

        if (area == DroneTransportAreaEnum.GeneralStorage)
        {
            if (type == ItemTypeEnum.Drone && manager.HasComponent<PreparingStationDemolition>(owner))
            {
                return 0;
            }

            int general = manager.GetComponentData<Storage>(owner).SlotCount - StationStorageUtility.ReadDedicatedCount(manager, owner);
            int quantity = 0;
            foreach (var entry in manager.GetBuffer<StoredItemElement>(owner, true))
            {
                if (entry.SlotIndex < general && entry.ItemType == type && ItemOwnershipApplySystem.CanTransferItem(manager, entry.ItemEntity, type, owner))
                {
                    quantity++;
                }
            }

            return quantity;
        }

        int count = 0;
        if (area == DroneTransportAreaEnum.Product)
        {
            foreach (var item in manager.GetBuffer<ProductItemElement>(owner, true))
            {
                if (item.ItemType == type && ItemOwnershipApplySystem.CanTransferItem(manager, item.ItemEntity, type, owner))
                {
                    count++;
                }
            }
        }
        else
        {
            foreach (var item in manager.GetBuffer<StoredItemElement>(owner, true))
            {
                if (item.ItemType == type && ItemOwnershipApplySystem.CanTransferItem(manager, item.ItemEntity, type, owner))
                {
                    count++;
                }
            }
        }

        return count;
    }

    public static int SourceQuantity(EntityManager manager, in DroneLogisticsTask task, Entity source)
    {
        var area = task.Kind == DroneLogisticsTaskKindEnum.StorageExport ? task.Area : DroneTransportAreaEnum.GeneralStorage;
        return CountSource(manager, source, task.ItemType, area, task.Purpose);
    }

    public static DroneTransportAreaEnum DestinationArea(in DroneLogisticsTask task)
    {
        return task.Kind == DroneLogisticsTaskKindEnum.StorageImport ? task.Area : DroneTransportAreaEnum.GeneralStorage;
    }

    public static int DestinationSpace(EntityManager manager, in DroneLogisticsTask task, Entity destination, Entity registry, int wanted,
        Entity deliveryAssignment = default)
    {
        var area = DestinationArea(task);
        int space = DroneSchedulingUtility.CountStorageSpace(manager, destination, task.ItemType, registry, wanted, area,
            generalOnly: area == DroneTransportAreaEnum.GeneralStorage, deliveryAssignment: deliveryAssignment);
        return area == DroneTransportAreaEnum.Input ? math.min(space, InputRemaining(manager, destination, task.ItemType)) : space;
    }

    /// <summary>현재 공개 연구가 없거나 회수 대기이면 0이다. EndBuilding 뒤에는 준비 단계의 오래된 한도를 사용하지 않는다.</summary>
    public static int InputMaximum(EntityManager manager, Entity owner, ItemTypeEnum type)
    {
        if (!manager.HasComponent<ResearchCycleState>(owner))
        {
            return int.MaxValue;
        }

        if (manager.HasBuffer<ResearchRecoveryElement>(owner) && !manager.GetBuffer<ResearchRecoveryElement>(owner, true).IsEmpty)
        {
            return 0;
        }

        using var query = manager.CreateEntityQuery(typeof(ResearchRegistry), typeof(ResearchState), typeof(ResearchDefinitionElement), typeof(ResearchIngredientElement), typeof(ResearchProgressElement));
        if (query.CalculateEntityCount() != 1)
        {
            return 0;
        }

        Entity registry = query.GetSingletonEntity();
        var active = manager.GetComponentData<ResearchState>(registry).ActiveResearchId;
        if (active == 0)
        {
            return 0;
        }

        foreach (var progress in manager.GetBuffer<ResearchProgressElement>(registry, true))
        {
            if (progress.ResearchId == active && progress.IsCompleted)
            {
                return 0;
            }
        }

        var ingredients = manager.GetBuffer<ResearchIngredientElement>(registry, true);
        foreach (var definition in manager.GetBuffer<ResearchDefinitionElement>(registry, true))
        {
            if (definition.StableId != active)
            {
                continue;
            }

            for (int i = 0; i < definition.IngredientCount; i++)
            {
                var ingredient = ingredients[definition.IngredientStart + i];
                if (ingredient.ItemType == type)
                {
                    return ResearchMaterialUtility.MaximumBufferedQuantity(ingredient.Amount);
                }
            }
        }

        return 0;
    }

    public static int InputRemaining(EntityManager manager, Entity owner, ItemTypeEnum type)
    {
        int maximum = InputMaximum(manager, owner, type);
        if (maximum == int.MaxValue)
        {
            return maximum;
        }

        int stored = 0;
        foreach (var entry in manager.GetBuffer<StoredItemElement>(owner, true))
        {
            if (entry.ItemType == type)
            {
                stored++;
            }
        }

        return math.max(0, maximum - stored);
    }
}
