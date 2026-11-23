using System;
using System.Collections.Generic;
using System.IO;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>현장의 승인된 배치, 실제 납품량과 공급 예약의 합계를 보존한다.</summary>
internal static class GameConstructionSave
{
    public static SavedConstructionSite Capture(EntityManager manager, Entity entity, string id,
        DynamicBuffer<ItemConfigElement> items)
    {
        var state = manager.GetComponentData<ConstructionSite>(entity);
        if ((state.Flags & ConstructionSiteFlags.Cancelled) != 0)
        {
            throw new InvalidOperationException("공사 취소가 정산된 뒤 저장할 수 있습니다.");
        }
        var stamp = manager.GetComponentData<PlacementStamp>(entity);
        var site = new SavedConstructionSite
        {
            Id = id,
            TargetBuildingId = state.TargetBuildingId,
            Origin = GameWorldSnapshot.WriteCell(manager.GetComponentData<GridPosition>(entity).Value),
            Direction = manager.GetComponentData<Direction>(entity).dir,
            Stamp = new SavedPlacementStamp { Tick = stamp.Tick, Receipt = stamp.ReceiptSequence, Order = stamp.Order },
            Disabled = manager.HasComponent<Disabled>(entity),
            AwaitingItemClearance = (state.Flags & ConstructionSiteFlags.AwaitingItemClearance) != 0
        };
        if (manager.HasComponent<PlannedBuildingSettings>(entity))
        {
            var settings = manager.GetComponentData<PlannedBuildingSettings>(entity);
            if (settings.HasRecipe)
            {
                site.PlannedRecipeId = settings.RecipeId;
            }
            if (settings.HasStorageFilter)
            {
                site.PlannedStorageFilter = new SavedStorageFilter();
                GameWorldCapture.CaptureFilter(settings.Filter, site.PlannedStorageFilter, items);
            }
        }
        foreach (var requirement in manager.GetBuffer<ConstructionMaterialRequirementElement>(entity, true))
        {
            site.Materials.Add(new SavedConstructionMaterial
            {
                ItemId = GameWorldCapture.ReadItemId(items, requirement.ItemType),
                RequiredQuantity = requirement.RequiredQuantity, DeliveredQuantity = requirement.DeliveredQuantity,
                ReservedQuantity = requirement.ReservedQuantity
            });
        }
        return site;
    }

    public static void Record(ref EntityCommandBuffer ecb, EntityManager manager, List<SavedConstructionSite> sites,
        HashSet<int2> occupied, IReadOnlyDictionary<int, ItemTypeEnum> itemTypes, Dictionary<string, SavedTaskTarget> taskTargets)
    {
        using var configQuery = manager.CreateEntityQuery(typeof(BuildingConfig), typeof(BuildingConfigElement),
            typeof(BuildingConstructionMaterialElement));
        Entity configEntity = configQuery.GetSingletonEntity();
        var configs = manager.GetBuffer<BuildingConfigElement>(configEntity, true);
        var materialConfigs = manager.GetBuffer<BuildingConstructionMaterialElement>(configEntity, true);
        var settingsRead = new BuildingSettingsUtility.SettingsReadContext(manager);

        foreach (var saved in sites)
        {
            if (saved == null)
            {
                throw new InvalidDataException("공사 현장 기록이 비어 있습니다.");
            }
            if (string.IsNullOrEmpty(saved.Id))
            {
                throw new InvalidDataException("공사 현장 저장 ID가 없습니다.");
            }
            if (!BuildingConfigLookupUtility.TryGetConfig(configs, saved.TargetBuildingId, out var config) ||
                config.BuildingType is BuildingTypeEnum.None or BuildingTypeEnum.MainFacility or BuildingTypeEnum.ConstructionSite or BuildingTypeEnum.Count ||
                !config.IsUnlocked)
            {
                throw new InvalidDataException("저장한 공사 현장의 건물 정의가 유효하지 않습니다.");
            }
            if (saved.Direction < DirectionEnum.Up || saved.Direction >= DirectionEnum.Count)
            {
                throw new InvalidDataException("공사 현장 방향이 유효하지 않습니다.");
            }

            int2 origin = GameWorldSnapshot.ReadCell(saved.Origin);
            GameWorldRestore.AddFootprint(occupied, origin, BuildingFootprintUtility.GetEffectiveSize(config.Footprint, saved.Direction));
            var flags = saved.AwaitingItemClearance ? ConstructionSiteFlags.AwaitingItemClearance : ConstructionSiteFlags.None;
            var state = new ConstructionSite(config.BuildingType, config.Tier, config.BuildingId, flags);
            var stamp = new PlacementStamp(saved.Stamp.Tick, saved.Stamp.Order, saved.Stamp.Receipt);
            var settings = new PlannedBuildingSettings
            {
                HasRecipe = saved.PlannedRecipeId.HasValue,
                RecipeId = saved.PlannedRecipeId.GetValueOrDefault()
            };
            if (saved.PlannedStorageFilter != null)
            {
                settings.HasStorageFilter = true;
                settings.Filter = GameWorldRestore.ReadFilter(saved.PlannedStorageFilter, itemTypes);
            }
            if (!BuildingSettingsUtility.IsAllowed(config.BuildingType, settings, ref settingsRead))
            {
                throw new InvalidDataException("공사 현장의 예정 건물 설정이 유효하지 않습니다.");
            }

            Entity entity = BuildingLifecycleUtility.CreateConstructionSite(ref ecb, origin, config.Footprint,
                saved.Direction, state, stamp, settings);
            if (!taskTargets.TryAdd(saved.Id, new SavedTaskTarget(entity, BuildingTypeEnum.ConstructionSite)))
            {
                throw new InvalidDataException("공사 현장 저장 ID가 중복됩니다.");
            }
            if (saved.Disabled)
            {
                ecb.AddComponent<Disabled>(entity);
            }
            var requirements = ecb.AddBuffer<ConstructionMaterialRequirementElement>(entity);
            BuildingConfigLookupUtility.PopulateRequirements(materialConfigs, config.BuildingId, ref requirements);
            RestoreDeliveries(saved.Materials, requirements, itemTypes);
        }
    }

    private static void RestoreDeliveries(List<SavedConstructionMaterial> materials,
        DynamicBuffer<ConstructionMaterialRequirementElement> requirements, IReadOnlyDictionary<int, ItemTypeEnum> itemTypes)
    {
        var byType = new Dictionary<ItemTypeEnum, SavedConstructionMaterial>();
        foreach (var material in materials)
        {
            if (material == null)
            {
                throw new InvalidDataException("공사 자재 기록이 비어 있습니다.");
            }
            if (!byType.TryAdd(GameWorldRestore.ResolveItem(material.ItemId, itemTypes), material))
            {
                throw new InvalidDataException("공사 자재 품목이 중복됩니다.");
            }
        }
        if (byType.Count != requirements.Length)
        {
            throw new InvalidDataException("공사 자재 목록이 현재 정의와 일치하지 않습니다.");
        }
        for (int index = 0; index < requirements.Length; index++)
        {
            var requirement = requirements[index];
            if (!byType.TryGetValue(requirement.ItemType, out var material) || material.RequiredQuantity != requirement.RequiredQuantity ||
                material.DeliveredQuantity < 0 || material.ReservedQuantity < 0 ||
                (long)material.DeliveredQuantity + material.ReservedQuantity > requirement.RequiredQuantity)
            {
                throw new InvalidDataException("공사 자재의 요구량 또는 실제 납품량이 유효하지 않습니다.");
            }
            requirement.DeliveredQuantity = material.DeliveredQuantity;
            requirement.ReservedQuantity = material.ReservedQuantity;
            requirements[index] = requirement;
        }
    }
}
