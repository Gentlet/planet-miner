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
            if (config.BuildingId > 0 && config.IsUnlocked && config.BuildingType != BuildingTypeEnum.None &&
                config.BuildingType != BuildingTypeEnum.ConstructionSite && config.BuildingType != BuildingTypeEnum.MainFacility)
            {
                result.Add(new BuildingToolCatalogEntry(config));
            }
        }
        return result;
    }

    public static bool TryCapture(EntityManager manager, Entity entity, out PlacementRequestCandidateElement candidate,
        out BuildingTypeEnum role)
    {
        candidate = default;
        role = default;
        if (!manager.Exists(entity))
        {
            return false;
        }
        if (!manager.HasComponent<BuildingType>(entity) || !manager.HasComponent<BuildingFootprint>(entity) ||
            !manager.HasComponent<GridPosition>(entity) || !manager.HasComponent<Direction>(entity))
        {
            return false;
        }
        if (manager.HasComponent<Disabled>(entity) || manager.HasComponent<Prefab>(entity) ||
            manager.HasComponent<PendingBuildingDemolition>(entity) || manager.HasComponent<PreparingStationDemolition>(entity))
        {
            return false;
        }
        var building = manager.GetComponentData<BuildingType>(entity);
        int buildingId = building.BuildingId;
        BuildingTypeEnum type = building.Type;
        PlannedBuildingSettings settings = default;
        if (type == BuildingTypeEnum.ConstructionSite)
        {
            var site = manager.GetComponentData<ConstructionSite>(entity);
            if ((site.Flags & ConstructionSiteFlags.Cancelled) != 0)
            {
                return false;
            }
            type = site.TargetBuildingType;
            buildingId = site.TargetBuildingId;
            if (manager.HasComponent<PlannedBuildingSettings>(entity))
            {
                settings = manager.GetComponentData<PlannedBuildingSettings>(entity);
            }
        }
        else if (type == BuildingTypeEnum.Crafter && manager.HasComponent<CrafterState>(entity))
        {
            settings.HasRecipe = true;
            settings.RecipeId = manager.GetComponentData<CrafterState>(entity).SelectedRecipeId;
        }
        else if (type == BuildingTypeEnum.Storage && manager.HasComponent<StorageFilter>(entity))
        {
            settings.HasStorageFilter = true;
            settings.Filter = manager.GetComponentData<StorageFilter>(entity);
        }
        if (type == BuildingTypeEnum.None || type == BuildingTypeEnum.ConstructionSite || type == BuildingTypeEnum.MainFacility)
        {
            return false;
        }
        candidate = new PlacementRequestCandidateElement(buildingId,
            manager.GetComponentData<BuildingFootprint>(entity).Size,
            manager.GetComponentData<GridPosition>(entity).Value,
            manager.GetComponentData<Direction>(entity).dir) { Settings = settings };
        role = type;
        return true;
    }

    public static List<Entity> Select(EntityManager manager, int2 first, int2 last)
    {
        var result = new List<Entity>();
        using var query = manager.CreateEntityQuery(typeof(BuildingType), typeof(GridPosition), typeof(BuildingFootprint), typeof(Direction));
        using var entities = query.ToEntityArray(Allocator.Temp);
        foreach (Entity entity in entities)
        {
            if (!TryCapture(manager, entity, out var candidate, out _))
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
        int2 pivot = math.min(first, last);
        foreach (Entity entity in Select(manager, first, last))
        {
            if (!TryCapture(manager, entity, out var candidate, out _) || candidate.BuildingId <= 0)
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
        if (!TryCapture(manager, target, out _, out _))
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
            Entity request = DroneTaskRequestUtility.SubmitCreate(manager, DroneLogisticsTaskKindEnum.BuildingDemolition,
                target, ItemTypeEnum.None);
            manager.AddComponent<WorldUiRequestOwner>(request);
        }
    }

    public static void RequestRange(EntityManager manager, int2 first, int2 last)
    {
        foreach (Entity entity in Select(manager, first, last))
        {
            RequestTarget(manager, entity);
        }
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
        buildings.GetSingleton<BuildingSpatialIndexFence>().GetReaderDependency().Complete();
        resources.GetSingleton<ResourceSpatialIndexFence>().GetReaderDependency().Complete();
        items.GetSingleton<ItemSpatialIndexFence>().GetReaderDependency().Complete();
        var native = new NativeArray<PlacementCandidate>(candidates.Count, Allocator.Temp);
        var validation = new NativeArray<PlacementValidationResult>(candidates.Count, Allocator.Temp);
        var settings = new NativeArray<PlacementValidationCode>(candidates.Count, Allocator.Temp);
        var definitions = manager.GetBuffer<BuildingConfigElement>(configs.GetSingletonEntity(), true).AsNativeArray();
        try
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                native[i] = candidate;
                if (candidate.Settings.HasValues &&
                    BuildingConfigLookupUtility.TryGetConfig(definitions, candidate.BuildingId, out var config) &&
                    !BuildingSettingsUtility.IsAllowed(manager, config.BuildingType, candidate.Settings))
                {
                    settings[i] = PlacementValidationCode.InvalidBuildingSettings;
                }
            }
            BuildingPlacementValidationUtility.ValidateBatchPlacement(native, PlacementFlags.StrictAllOrNothing,
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
