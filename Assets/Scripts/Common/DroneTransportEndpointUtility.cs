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

        if (!manager.HasComponent<BuildingIdentity>(owner) || !manager.HasComponent<GridPosition>(owner))
        {
            return false;
        }

        if (manager.HasComponent<PendingBuildingDemolition>(owner) ||
            manager.HasComponent<PreparingStationDemolition>(owner))
        {
            return false;
        }

        if (area == DroneTransportAreaEnum.Product)
        {
            return manager.HasBuffer<ProductSlotElement>(owner);
        }

        if (area != DroneTransportAreaEnum.Input)
        {
            return false;
        }

        return (manager.HasBuffer<BuildingInputSlotElement>(owner) || manager.HasComponent<FuelStorageSlots>(owner)) && manager.HasComponent<Storage>(owner) && manager.HasBuffer<StorageSlotElement>(owner);
    }

    public static bool IsTarget(EntityManager manager, in DroneLogisticsTask task)
    {
        if (task.Purpose == DroneTransportPurposeEnum.DemolitionRecovery)
            return task.Kind == DroneLogisticsTaskKindEnum.StorageExport &&
                DroneDemolitionUtility.IsInventorySource(manager, task.DemolitionRoot, task.Target, task.Area);

        if (!IsArea(manager, task.Target, task.Area) ||
            (task.Kind == DroneLogisticsTaskKindEnum.StorageExport &&
             manager.HasComponent<PreparingStationDemolition>(task.Target)))
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
        if (!IsArea(manager, owner, area)) return 0;
        return CountInventory(manager, owner, type, area, purpose);
    }

    internal static bool CanCollectFrom(EntityManager manager, in DroneLogisticsTask task, Entity source,
        DroneTransportAreaEnum area) =>
        task.Purpose == DroneTransportPurposeEnum.DemolitionRecovery
            ? task.Target == source && task.Area == area &&
              DroneDemolitionUtility.IsInventorySource(manager, task.DemolitionRoot, source, area)
            : IsArea(manager, source, area) && !manager.HasComponent<PreparingStationDemolition>(source);

    private static int CountInventory(EntityManager manager, Entity owner, ItemTypeEnum type,
        DroneTransportAreaEnum area, DroneTransportPurposeEnum purpose)
    {
        if (purpose == DroneTransportPurposeEnum.ResearchRecovery) return ResearchRecoveryUtility.Count(manager, owner, type);
        if (area == DroneTransportAreaEnum.Product)
        {
            int outputQuantity = 0;
            foreach (var slot in manager.GetBuffer<ProductSlotElement>(owner, true))
                if (slot.ItemType == type) outputQuantity += slot.Quantity;
            return outputQuantity;
        }

        if (type == ItemTypeEnum.Drone && manager.HasComponent<PreparingStationDemolition>(owner)) return 0;
        var inventory = manager.GetBuffer<StorageSlotElement>(owner, true);
        ReadInventoryRange(manager, owner, area, inventory.Length, out int start, out int end);
        int quantity = 0;
        for (int slot = start; slot < end; slot++)
            if (inventory[slot].ItemType == type) quantity += inventory[slot].Quantity;
        return quantity;
    }

    internal static void ReadInventoryRange(EntityManager manager, Entity owner, DroneTransportAreaEnum area,
        int total, out int start, out int end)
    {
        start = 0;
        end = total;
        if (area == DroneTransportAreaEnum.GeneralStorage)
        {
            end = StationStorageUtility.NonFuelSlotCount(manager, owner, total) - StationStorageUtility.ReadDedicatedCount(manager, owner);
        }
        else if (area == DroneTransportAreaEnum.Input && manager.HasComponent<FuelStorageSlots>(owner))
        {
            var fuel = manager.GetComponentData<FuelStorageSlots>(owner);
            start = fuel.Start;
            end = fuel.Start + fuel.Count;
        }
    }

    public static int SourceQuantity(EntityManager manager, in DroneLogisticsTask task, Entity source)
    {
        var area = SourceArea(task);
        return CanCollectFrom(manager, task, source, area)
            ? CountInventory(manager, source, task.ItemType, area, task.Purpose)
            : 0;
    }

    internal static DroneTransportAreaEnum SourceArea(in DroneLogisticsTask task)
    {
        return task.Kind == DroneLogisticsTaskKindEnum.StorageExport ? task.Area : DroneTransportAreaEnum.GeneralStorage;
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
            generalOnly: UsesGeneralStorageSlots(manager, destination, area, task.ItemType), deliveryAssignment: deliveryAssignment);
        return area == DroneTransportAreaEnum.Input ? math.min(space, InputRemaining(manager, destination, task.ItemType)) : space;
    }

    internal static bool UsesGeneralStorageSlots(EntityManager manager, Entity destination, DroneTransportAreaEnum area,
        ItemTypeEnum type) => area == DroneTransportAreaEnum.GeneralStorage &&
        (type != ItemTypeEnum.Drone || !manager.HasComponent<DroneStationStorage>(destination));

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
        foreach (var entry in manager.GetBuffer<StorageSlotElement>(owner, true))
        {
            if (entry.ItemType == type)
            {
                stored += entry.Quantity;
            }
        }

        return math.max(0, maximum - stored);
    }
}
