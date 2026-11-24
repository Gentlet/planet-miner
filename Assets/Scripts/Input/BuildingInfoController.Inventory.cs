using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

internal readonly struct BuildingInfoSlot
{
    public readonly ItemTypeEnum Type;
    public readonly int Quantity;
    public readonly int SourceIndex;
    public readonly bool CanActivateStored;
    public readonly int ReservedQuantity;

    public BuildingInfoSlot(ItemTypeEnum type, int quantity, int sourceIndex = -1, bool canActivateStored = false,
        int reservedQuantity = 0)
    {
        Type = type;
        Quantity = quantity;
        SourceIndex = sourceIndex;
        CanActivateStored = canActivateStored;
        ReservedQuantity = reservedQuantity;
    }
}

internal readonly struct BuildingInfoRecipeChoice
{
    public readonly int Id;
    public readonly ItemTypeEnum Output;
    public readonly float CraftTime;

    public BuildingInfoRecipeChoice(in RecipeConfigElement recipe, ItemTypeEnum output)
    {
        Id = recipe.Id;
        Output = output;
        CraftTime = recipe.CraftTime;
    }
}

public sealed partial class BuildingInfoController
{
    internal readonly List<BuildingInfoSlot> GeneralSlots = new();
    internal readonly List<BuildingInfoSlot> InputSlots = new();
    internal readonly List<BuildingInfoSlot> ProductSlots = new();
    internal readonly List<BuildingInfoSlot> DroneSlots = new();
    internal readonly List<BuildingInfoRecipeChoice> Recipes = new();
    internal bool HasPowerGridSection { get; private set; }
    internal BuildingInfoPowerGrid GridDisplay { get; private set; }
    internal bool HasDroneStationTarget { get; private set; }
    internal bool IsSelectedDroneDocked { get; private set; }
    internal string StationChargingSummary { get; private set; }

    private void ResetBuildingDisplay()
    {
        GeneralSlots.Clear();
        InputSlots.Clear();
        ProductSlots.Clear();
        DroneSlots.Clear();
        Recipes.Clear();
        HasPowerGridSection = false;
        GridDisplay = default;
        HasDroneStationTarget = false;
        IsSelectedDroneDocked = false;
        StationChargingSummary = string.Empty;
    }

    private void ReadInventorySlots(EntityManager manager, Entity entity)
    {
        if (manager.HasBuffer<StorageSlotElement>(entity))
        {
            ReadStorageSlots(manager, entity);
        }

        if (manager.HasBuffer<ProductSlotElement>(entity))
        {
            ReadProductSlots(manager, entity);
        }
    }

    private void ReadStorageSlots(EntityManager manager, Entity entity)
    {
        var items = manager.GetBuffer<StorageSlotElement>(entity, true);
        bool isStation = manager.HasComponent<DroneStation>(entity);
        int nonFuel = StationStorageUtility.NonFuelSlotCount(manager, entity, items.Length);
        int dedicatedStart = nonFuel - StationStorageUtility.ReadDedicatedCount(manager, entity);
        var docks = isStation && manager.HasBuffer<DockedDroneElement>(entity)
            ? manager.GetBuffer<DockedDroneElement>(entity, true) : default;
        var reservations = isStation && manager.HasBuffer<DroneStationReservedSlotElement>(entity)
            ? manager.GetBuffer<DroneStationReservedSlotElement>(entity, true) : default;

        if (docks.IsCreated)
        {
            ReadStationCharging(manager, entity, docks);
        }
        for (int index = 0; index < items.Length; index++)
        {
            var item = items[index];
            int reserved = reservations.IsCreated && index >= dedicatedStart && index < nonFuel
                ? DroneStationRelocationUtility.CountReservedDrones(reservations, index) : 0;
            var slot = new BuildingInfoSlot(item.ItemType, item.Quantity, index,
                isStation && index >= dedicatedStart && index < nonFuel && item.ItemType == ItemTypeEnum.Drone && item.Quantity > 0,
                reserved);
            if (index >= nonFuel)
            {
                InputSlots.Add(slot);
            }
            else if (index >= dedicatedStart)
            {
                DroneSlots.Add(slot);
            }
            else
            {
                (HasGeneralStorage ? GeneralSlots : InputSlots).Add(slot);
            }
        }
    }

    private void ReadStationCharging(EntityManager manager, Entity station, DynamicBuffer<DockedDroneElement> docks)
    {
        using var config = manager.CreateEntityQuery(typeof(DroneBatteryConfig));
        if (!config.HasSingleton<DroneBatteryConfig>())
        {
            return;
        }
        float maximum = config.GetSingleton<DroneBatteryConfig>().MaximumCharge;
        int charging = 0;
        int powerWaiting = 0;
        int storageWaiting = 0;
        foreach (var entry in docks)
        {
            if (!StationStorageUtility.MatchesDockedWorker(manager, station, entry.Drone) ||
                manager.HasComponent<Disabled>(entry.Drone))
            {
                continue;
            }
            if (manager.GetComponentData<DroneBatteryState>(entry.Drone).CurrentCharge >= maximum)
            {
                var assignment = manager.GetComponentData<DroneWorkerAssignment>(entry.Drone);
                var cargo = manager.GetComponentData<DroneCargoState>(entry.Drone);
                if (assignment.Assignment == Entity.Null && cargo.Quantity == 0)
                {
                    storageWaiting++;
                }
            }
            else if (manager.GetComponentData<DroneActivityState>(entry.Drone).Activity == DroneActivityKind.Charging)
            {
                charging++;
            }
            else
            {
                powerWaiting++;
            }
        }
        StationChargingSummary = $"충전 중 {charging}기 / 전력 대기 {powerWaiting}기 / 수납 대기 {storageWaiting}기";
    }

    private void ReadProductSlots(EntityManager manager, Entity entity)
    {
        var products = manager.GetBuffer<ProductSlotElement>(entity, true);
        int capacity = manager.HasComponent<MinerState>(entity) ? 1 : 0;
        if (HasCrafting && SelectedRecipe > 0)
        {
            using var registry = manager.CreateEntityQuery(typeof(RecipeRegistry), typeof(RecipeConfigElement));
            if (registry.HasSingleton<RecipeRegistry>() &&
                RecipeConfigLookupUtility.TryGetRecipe(manager.GetBuffer<RecipeConfigElement>(registry.GetSingletonEntity(), true),
                    SelectedRecipe, out var recipe))
            {
                capacity = recipe.OutputCount;
            }
        }
        if (!products.IsEmpty)
        {
            capacity = System.Math.Max(capacity, products[products.Length - 1].SlotIndex + 1);
        }

        int productIndex = 0;
        for (int slotIndex = 0; slotIndex < capacity; slotIndex++)
        {
            if (productIndex < products.Length && products[productIndex].SlotIndex == slotIndex)
            {
                var product = products[productIndex++];
                ProductSlots.Add(new BuildingInfoSlot(product.ItemType, product.Quantity));
            }
            else
            {
                ProductSlots.Add(default);
            }
        }
    }

    private void ReadRecipeChoices(EntityManager manager)
    {
        if (!HasCrafting)
        {
            return;
        }

        using var registry = manager.CreateEntityQuery(typeof(RecipeRegistry), typeof(RecipeConfigElement), typeof(RecipeOutputElement));
        if (!registry.HasSingleton<RecipeRegistry>())
        {
            return;
        }

        Entity entity = registry.GetSingletonEntity();
        var outputs = manager.GetBuffer<RecipeOutputElement>(entity, true);
        foreach (var recipe in manager.GetBuffer<RecipeConfigElement>(entity, true))
        {
            if (recipe.IsUnlocked && recipe.TryGetPrimaryOutput(outputs, out var primary))
            {
                Recipes.Add(new BuildingInfoRecipeChoice(recipe, primary.ItemType));
            }
        }
    }

    internal void ReadRecipeStacks(int recipeId, List<BuildingInfoSlot> input, List<BuildingInfoSlot> output)
    {
        input.Clear();
        output.Clear();
        if (!CanSubmit() || recipeId <= 0)
        {
            return;
        }

        var manager = _world.EntityManager;
        using var registry = manager.CreateEntityQuery(typeof(RecipeRegistry), typeof(RecipeConfigElement),
            typeof(RecipeIngredientElement), typeof(RecipeOutputElement));
        if (!registry.HasSingleton<RecipeRegistry>())
        {
            return;
        }

        Entity entity = registry.GetSingletonEntity();
        if (!RecipeConfigLookupUtility.TryGetRecipe(manager.GetBuffer<RecipeConfigElement>(entity, true), recipeId, out var recipe))
        {
            return;
        }

        var ingredients = manager.GetBuffer<RecipeIngredientElement>(entity, true);
        for (int index = recipe.IngredientStart; index < recipe.IngredientStart + recipe.IngredientCount; index++)
        {
            var ingredient = ingredients[index];
            input.Add(new BuildingInfoSlot(ingredient.ItemType, ingredient.Amount));
        }
        var outputs = manager.GetBuffer<RecipeOutputElement>(entity, true);
        for (int index = recipe.OutputStart; index < recipe.OutputStart + recipe.OutputCount; index++)
        {
            var product = outputs[index];
            output.Add(new BuildingInfoSlot(product.ItemType, product.Amount));
        }
    }

    private void FindDroneStation(EntityManager manager, Entity drone)
    {
        HasDroneStationTarget = TryGetDroneStation(manager, drone, out _, out bool docked);
        IsSelectedDroneDocked = docked;
    }

    private static bool TryGetDroneStation(EntityManager manager, Entity drone, out Entity station, out bool docked)
    {
        station = Entity.Null;
        docked = false;
        if (!manager.HasComponent<DroneWorker>(drone))
        {
            return false;
        }

        if (manager.HasComponent<DroneDocked>(drone))
        {
            station = manager.GetComponentData<DroneDocked>(drone).Station;
            docked = true;
            return DroneSchedulingUtility.IsActiveEntity(manager, station) && manager.HasComponent<DroneStation>(station);
        }

        if (!manager.HasComponent<GridPosition>(drone))
        {
            return false;
        }

        var position = manager.GetComponentData<GridPosition>(drone).Value;
        using var stations = manager.CreateEntityQuery(typeof(DroneStation), typeof(GridPosition),
            typeof(BuildingFootprint), typeof(Direction));
        using var entities = stations.ToEntityArray(Allocator.Temp);
        foreach (Entity candidate in entities)
        {
            var origin = manager.GetComponentData<GridPosition>(candidate).Value;
            var footprint = manager.GetComponentData<BuildingFootprint>(candidate);
            var direction = manager.GetComponentData<Direction>(candidate).dir;
            if (BuildingToolGeometry.Overlaps(origin, footprint.Size, direction, position, position))
            {
                station = candidate;
                return true;
            }
        }
        return false;
    }
}
