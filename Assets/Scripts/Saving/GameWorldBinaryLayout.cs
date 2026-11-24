/// <summary>월드 스냅샷의 바이너리 버전 1 필드 순서다.</summary>
internal static class GameWorldBinaryLayout
{
    internal static void World(GameSnapshotArchive archive, ref GameWorldSnapshot value)
    {
        archive.Record(ref value.World, Fields);
    }

    internal static void BuildingsAndItems(GameSnapshotArchive archive, ref GameWorldSnapshot value)
    {
        archive.List(ref value.Buildings, (GameSnapshotArchive item, ref SavedBuilding entry) => item.Record(ref entry, Fields));
        archive.List(ref value.WorldItems, (GameSnapshotArchive item, ref SavedWorldItem entry) => item.Record(ref entry, Fields));
    }

    internal static void ResearchAndConstruction(GameSnapshotArchive archive, ref GameWorldSnapshot value)
    {
        archive.Record(ref value.Research, Fields);
        archive.List(ref value.ConstructionSites, (GameSnapshotArchive item, ref SavedConstructionSite entry) => item.Record(ref entry, Fields));
    }

    internal static void Fields(GameSnapshotArchive archive, ref SavedWorld value)
    {
        archive.Value(ref value.Seed);
        archive.List(ref value.GeneratedChunks, (GameSnapshotArchive item, ref int[] entry) => item.Cell(ref entry));
        archive.List(ref value.PendingChunks, (GameSnapshotArchive item, ref int[] entry) => item.Cell(ref entry));
        archive.List(ref value.ResourceNodes, (GameSnapshotArchive item, ref SavedResourceNode entry) => item.Record(ref entry, Fields));
        archive.Value(ref value.MainFacilityId);
        archive.Value(ref value.PlacementTick);
        archive.Value(ref value.NextPlacementReceipt);
        archive.Value(ref value.NextDroneTaskSequence);
    }

    internal static void Fields(GameSnapshotArchive archive, ref SavedResourceNode value)
    {
        archive.Cell(ref value.Cell);
        archive.Value(ref value.ItemId);
        archive.Value(ref value.RemainingAmount);
    }

    internal static void Fields(GameSnapshotArchive archive, ref SavedBuilding value)
    {
        archive.Value(ref value.Id);
        archive.Value(ref value.BuildingTypeId);
        archive.Cell(ref value.Origin);
        archive.EnumValue(ref value.Direction);
        archive.Record(ref value.Stamp, Fields);
        archive.Value(ref value.Disabled);
        archive.OptionalRecord(ref value.Storage, Fields);
        archive.OptionalRecord(ref value.Routing, Fields);
        archive.OptionalRecord(ref value.Production, Fields);
    }

    internal static void Fields(GameSnapshotArchive archive, ref SavedPlacementStamp value)
    {
        archive.Value(ref value.Tick);
        archive.Value(ref value.Receipt);
        archive.Value(ref value.Order);
    }

    internal static void Fields(GameSnapshotArchive archive, ref SavedStorageFilter value)
    {
        archive.EnumValue(ref value.FilterMode);
        archive.List(ref value.FilterItemIds, (GameSnapshotArchive item, ref int entry) => item.Value(ref entry));
    }

    internal static void Fields(GameSnapshotArchive archive, ref SavedStorage value)
    {
        SavedStorageFilter filter = value;
        Fields(archive, ref filter);
        archive.List(ref value.Slots, (GameSnapshotArchive item, ref SavedStorageSlot entry) => item.Record(ref entry, Fields));
    }

    internal static void Fields(GameSnapshotArchive archive, ref SavedStorageSlot value)
    {
        archive.Value(ref value.Slot);
        archive.Value(ref value.ItemId);
        archive.Value(ref value.Quantity);
    }

    internal static void Fields(GameSnapshotArchive archive, ref SavedRouting value)
    {
        archive.Value(ref value.BeltId);
        archive.EnumValue(ref value.ForwardDirection);
        archive.Value(ref value.Cursor);
    }

    internal static void Fields(GameSnapshotArchive archive, ref SavedWorldItem value)
    {
        archive.Value(ref value.Id);
        archive.Cell(ref value.Cell);
        archive.Value(ref value.ItemId);
        archive.Maybe(ref value.BeltProgress, (GameSnapshotArchive item, ref float entry) => item.Value(ref entry));
        archive.Maybe(ref value.Direction, (GameSnapshotArchive item, ref DirectionEnum entry) => item.EnumValue(ref entry));
    }

    internal static void Fields(GameSnapshotArchive archive, ref SavedProduction value)
    {
        archive.Maybe(ref value.MiningProgress, (GameSnapshotArchive item, ref float entry) => item.Value(ref entry));
        archive.OptionalRecord(ref value.Crafting, Fields);
        archive.Maybe(ref value.RemainingFuelEnergy, (GameSnapshotArchive item, ref float entry) => item.Value(ref entry));
        archive.Maybe(ref value.ActiveFuelCapacity, (GameSnapshotArchive item, ref float entry) => item.Value(ref entry));
        archive.OptionalRecord(ref value.ResearchCycle, Fields);
        bool hasProducts = value.Products != null;
        archive.Value(ref hasProducts);
        if (hasProducts)
        {
            archive.List(ref value.Products, (GameSnapshotArchive item, ref SavedStorageSlot entry) => item.Record(ref entry, Fields));
        }
    }

    internal static void Fields(GameSnapshotArchive archive, ref SavedCrafting value)
    {
        archive.Value(ref value.RecipeId);
        archive.Value(ref value.Progress);
        archive.Value(ref value.IsActive);
        archive.EnumValue(ref value.Status);
    }

    internal static void Fields(GameSnapshotArchive archive, ref SavedResearch value)
    {
        archive.Value(ref value.ActiveResearchId);
        archive.List(ref value.Progress, (GameSnapshotArchive item, ref SavedResearchProgress entry) => item.Record(ref entry, Fields));
    }

    internal static void Fields(GameSnapshotArchive archive, ref SavedResearchProgress value)
    {
        archive.Value(ref value.ResearchId);
        archive.Value(ref value.Progress);
        archive.Value(ref value.IsCompleted);
    }

    internal static void Fields(GameSnapshotArchive archive, ref SavedResearchCycle value)
    {
        archive.Value(ref value.ResearchId);
        archive.Value(ref value.ElapsedTime);
        archive.Value(ref value.InputConfigured);
        archive.List(ref value.Recovery, (GameSnapshotArchive item, ref SavedStorageSlot entry) => item.Record(ref entry, Fields));
    }

    internal static void Fields(GameSnapshotArchive archive, ref SavedConstructionSite value)
    {
        archive.Value(ref value.Id);
        archive.Value(ref value.TargetBuildingId);
        archive.Cell(ref value.Origin);
        archive.EnumValue(ref value.Direction);
        archive.Record(ref value.Stamp, Fields);
        archive.Value(ref value.Disabled);
        archive.Value(ref value.AwaitingItemClearance);
        archive.Maybe(ref value.PlannedRecipeId, (GameSnapshotArchive item, ref int entry) => item.Value(ref entry));
        archive.OptionalRecord(ref value.PlannedStorageFilter, Fields);
        archive.List(ref value.Materials, (GameSnapshotArchive item, ref SavedConstructionMaterial entry) => item.Record(ref entry, Fields));
    }

    internal static void Fields(GameSnapshotArchive archive, ref SavedConstructionMaterial value)
    {
        archive.Value(ref value.ItemId);
        archive.Value(ref value.RequiredQuantity);
        archive.Value(ref value.DeliveredQuantity);
        archive.Value(ref value.ReservedQuantity);
    }

}
