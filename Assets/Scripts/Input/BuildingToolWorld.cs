using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public readonly struct BuildingToolCatalogEntry
{
    public readonly BuildingConfigElement Config;
    public BuildingToolCatalogEntry(BuildingConfigElement config)
    {
        Config = config;
    }
}

public readonly struct BuildingToolPreviewItem
{
    public readonly PlacementRequestCandidateElement Candidate;
    public readonly PlacementValidationResult Validation;
    public BuildingToolPreviewItem(PlacementRequestCandidateElement candidate, PlacementValidationResult validation)
    {
        Candidate = candidate;
        Validation = validation;
    }

    internal bool MatchesPlacement(in BuildingToolPreviewItem other) =>
        Candidate.BuildingId == other.Candidate.BuildingId &&
        Candidate.OriginPosition.Equals(other.Candidate.OriginPosition) &&
        Candidate.FootprintSize.Equals(other.Candidate.FootprintSize) &&
        Candidate.Direction == other.Candidate.Direction && Validation.IsValid == other.Validation.IsValid;
}

/// <summary>입력/표시용 읽기와 공개 요청 접수만 담당한다. UI는 점유나 소유권 원본을 쓰지 않는다.</summary>
public static class BuildingToolWorld
{
    public static bool IsAvailable(World world)
    {
        if (world == null)
        {
            return false;
        }
        return world.IsCreated;
    }

    public static List<BuildingToolCatalogEntry> ReadCatalog(EntityManager manager)
    {
        var result = new List<BuildingToolCatalogEntry>();
        using var query = manager.CreateEntityQuery(typeof(BuildingConfig), typeof(BuildingConfigElement));
        if (query.CalculateEntityCount() != 1)
        {
            return result;
        }
        foreach (var config in manager.GetBuffer<BuildingConfigElement>(query.GetSingletonEntity(), true))
        {
            if (config.BuildingId > 0 && config.IsUnlocked && !config.Placement.BootstrapOnly)
            {
                result.Add(new BuildingToolCatalogEntry(config));
            }
        }
        return result;
    }

    public static bool TryCapture(EntityManager manager, Entity entity, out PlacementRequestCandidateElement candidate)
    {
        if (!TryReadDefinitions(manager, out var definitions))
        {
            candidate = default;
            return false;
        }
        return TryCapture(manager, entity, definitions, out candidate);
    }

    private static bool TryReadDefinitions(EntityManager manager, out DynamicBuffer<BuildingConfigElement> definitions)
    {
        using var query = manager.CreateEntityQuery(typeof(BuildingConfig), typeof(BuildingConfigElement));
        if (query.CalculateEntityCount() != 1)
        {
            definitions = default;
            return false;
        }
        definitions = manager.GetBuffer<BuildingConfigElement>(query.GetSingletonEntity(), true);
        return true;
    }

    private static bool TryCapture(EntityManager manager, Entity entity, in DynamicBuffer<BuildingConfigElement> definitions,
        out PlacementRequestCandidateElement candidate)
    {
        candidate = default;
        if (!manager.Exists(entity))
        {
            return false;
        }
        if (!manager.HasComponent<BuildingFootprint>(entity) ||
            !manager.HasComponent<GridPosition>(entity) || !manager.HasComponent<Direction>(entity))
        {
            return false;
        }
        if (manager.HasComponent<Disabled>(entity) || manager.HasComponent<Prefab>(entity) ||
            manager.HasComponent<PendingBuildingDemolition>(entity) || manager.HasComponent<PreparingStationDemolition>(entity))
        {
            return false;
        }
        int buildingId;
        PlannedBuildingSettings settings = default;
        if (manager.HasComponent<ConstructionSite>(entity))
        {
            var site = manager.GetComponentData<ConstructionSite>(entity);
            if ((site.Flags & ConstructionSiteFlags.Cancelled) != 0)
            {
                return false;
            }
            buildingId = site.TargetBuildingId;
            if (manager.HasComponent<PlannedBuildingSettings>(entity))
            {
                settings = manager.GetComponentData<PlannedBuildingSettings>(entity);
            }
        }
        else
        {
            if (!manager.HasComponent<BuildingIdentity>(entity))
                return false;
            buildingId = manager.GetComponentData<BuildingIdentity>(entity).BuildingId;
            if (manager.HasComponent<CrafterState>(entity))
            {
                settings.HasRecipe = true;
                settings.RecipeId = manager.GetComponentData<CrafterState>(entity).SelectedRecipeId;
            }
            if (manager.HasComponent<EditableStorageFilter>(entity) && manager.HasComponent<StorageFilter>(entity))
            {
                settings.HasStorageFilter = true;
                settings.Filter = manager.GetComponentData<StorageFilter>(entity);
            }
        }

        if (!BuildingConfigLookupUtility.TryGetConfig(definitions, buildingId, out var definition) || definition.Placement.BootstrapOnly)
        {
            return false;
        }

        candidate = new PlacementRequestCandidateElement(buildingId,
            manager.GetComponentData<BuildingFootprint>(entity).Size,
            manager.GetComponentData<GridPosition>(entity).Value,
            manager.GetComponentData<Direction>(entity).dir) { Settings = settings };
        return true;
    }

    public static List<Entity> Select(EntityManager manager, int2 first, int2 last)
    {
        return TryReadDefinitions(manager, out var definitions)
            ? Select(manager, first, last, definitions)
            : new List<Entity>();
    }

    private static List<Entity> Select(EntityManager manager, int2 first, int2 last, in DynamicBuffer<BuildingConfigElement> definitions)
    {
        var result = new List<Entity>();
        using var query = manager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<GridPosition>(), ComponentType.ReadOnly<BuildingFootprint>(), ComponentType.ReadOnly<Direction>() },
            Any = new[] { ComponentType.ReadOnly<BuildingIdentity>(), ComponentType.ReadOnly<ConstructionSite>() }
        });
        using var entities = query.ToEntityArray(Allocator.Temp);
        foreach (Entity entity in entities)
        {
            if (!TryCapture(manager, entity, definitions, out var candidate))
            {
                continue;
            }
            if (BuildingToolGeometry.Overlaps(candidate.OriginPosition, candidate.FootprintSize, candidate.Direction, first, last))
            {
                result.Add(entity);
            }
        }
        result.Sort((left, right) =>
        {
            int2 a = manager.GetComponentData<GridPosition>(left).Value;
            int2 b = manager.GetComponentData<GridPosition>(right).Value;
            int order = a.y.CompareTo(b.y);
            return order != 0 ? order : a.x.CompareTo(b.x);
        });
        return result;
    }

    public static List<BuildingBlueprintElement> Copy(EntityManager manager, int2 first, int2 last)
    {
        var result = new List<BuildingBlueprintElement>();
        if (!TryReadDefinitions(manager, out var definitions))
        {
            return result;
        }
        int2 pivot = math.min(first, last);
        foreach (Entity entity in Select(manager, first, last, definitions))
        {
            if (!TryCapture(manager, entity, definitions, out var candidate))
            {
                continue;
            }
            if (!BuildingToolGeometry.TryPosition((long)candidate.OriginPosition.x - pivot.x,
                (long)candidate.OriginPosition.y - pivot.y, out int2 offset))
            {
                return new List<BuildingBlueprintElement>();
            }
            candidate.OriginPosition = int2.zero;
            result.Add(new BuildingBlueprintElement { Candidate = candidate, Offset = offset });
        }
        return result;
    }

    public static Entity TargetAt(EntityManager manager, int2 cell)
    {
        var selected = Select(manager, cell, cell);
        return selected.Count > 0 ? selected[0] : Entity.Null;
    }

    public static void RequestTarget(EntityManager manager, Entity target)
    {
        if (!TryCapture(manager, target, out _))
        {
            return;
        }
        if (manager.HasComponent<ConstructionSite>(target))
        {
            Entity request = manager.CreateEntity(typeof(CancelConstructionRequest));
            manager.SetComponentData(request, new CancelConstructionRequest(target));
        }
        else
        {
            Entity request = DroneTaskRequestUtility.SubmitBuildingDemolition(manager, target);
            manager.AddComponent<WorldUiRequestOwner>(request);
        }
    }

    public static void RequestBuildingRemoval(EntityManager manager, int2 first, int2 last)
    {
        foreach (Entity entity in Select(manager, first, last))
        {
            RequestTarget(manager, entity);
        }
    }

    public static void RequestRange(EntityManager manager, int2 first, int2 last)
    {
        RequestBuildingRemoval(manager, first, last);
        foreach (Entity request in DroneTaskRequestUtility.SubmitRecoveryRange(manager, first, last))
        {
            manager.AddComponent<WorldUiRequestOwner>(request);
        }
    }

    public static bool Preview(EntityManager manager, IReadOnlyList<PlacementRequestCandidateElement> candidates,
        List<BuildingToolPreviewItem> preview)
    {
        preview.Clear();
        using var buildings = manager.CreateEntityQuery(typeof(BuildingSpatialIndex), typeof(BuildingSpatialIndexFence));
        using var resources = manager.CreateEntityQuery(typeof(ResourceSpatialIndex), typeof(ResourceSpatialIndexFence));
        using var items = manager.CreateEntityQuery(typeof(ItemSpatialIndex), typeof(ItemSpatialIndexFence));
        using var configs = manager.CreateEntityQuery(typeof(BuildingConfig), typeof(BuildingConfigElement));
        if (buildings.CalculateEntityCount() != 1 || resources.CalculateEntityCount() != 1 ||
            items.CalculateEntityCount() != 1 || configs.CalculateEntityCount() != 1)
        {
            return false;
        }
        buildings.GetSingleton<BuildingSpatialIndexFence>().State.GetReaderDependency().Complete();
        resources.GetSingleton<ResourceSpatialIndexFence>().State.GetReaderDependency().Complete();
        items.GetSingleton<ItemSpatialIndexFence>().State.GetReaderDependency().Complete();
        var native = new NativeArray<PlacementCandidate>(candidates.Count, Allocator.Temp);
        var validation = new NativeArray<PlacementValidationResult>(candidates.Count, Allocator.Temp);
        var settings = new NativeArray<PlacementValidationCode>(candidates.Count, Allocator.Temp);
        var definitions = manager.GetBuffer<BuildingConfigElement>(configs.GetSingletonEntity(), true).AsNativeArray();
        try
        {
            var settingsRead = new BuildingSettingsUtility.SettingsReadContext(manager);
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                native[i] = candidate;
                if (candidate.Settings.HasValues &&
                    BuildingConfigLookupUtility.TryGetConfig(definitions, candidate.BuildingId, out var config) &&
                    !BuildingSettingsUtility.IsAllowed(config, candidate.Settings, ref settingsRead))
                {
                    settings[i] = PlacementValidationCode.InvalidBuildingSettings;
                }
            }
            BuildingPlacementValidationUtility.ValidateBatchPlacement(manager, native, PlacementFlags.StrictAllOrNothing,
                buildings.GetSingleton<BuildingSpatialIndex>().Map.AsReadOnly(),
                resources.GetSingleton<ResourceSpatialIndex>().Map.AsReadOnly(),
                items.GetSingleton<ItemSpatialIndex>().Map.AsReadOnly(), validation,
                definitions, settingsRejections: settings);
            bool allowed = true;
            for (int i = 0; i < candidates.Count; i++)
            {
                preview.Add(new BuildingToolPreviewItem(candidates[i], validation[i]));
                allowed &= validation[i].IsValid;
            }
            return allowed;
        }
        finally
        {
            native.Dispose();
            validation.Dispose();
            settings.Dispose();
        }
    }
}
