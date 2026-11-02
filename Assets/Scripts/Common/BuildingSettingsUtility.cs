using System;
using Unity.Entities;

public static class BuildingSettingsUtility
{
    public static bool IsValidFilter(in StorageFilter filter, in FixedBitSet registeredItems)
    {
        if (filter.Mode != StorageFilterMode.AllowAll && filter.Mode != StorageFilterMode.Whitelist &&
            filter.Mode != StorageFilterMode.Blacklist)
        {
            return false;
        }
        return (filter.Mask.Part0 & ~registeredItems.Part0) == 0 &&
            (filter.Mask.Part1 & ~registeredItems.Part1) == 0;
    }

    public static FixedBitSet RegisteredItems(DynamicBuffer<ItemConfigElement> items)
    {
        FixedBitSet mask = default;
        foreach (var item in items)
        {
            if (item.ItemType != ItemTypeEnum.None)
            {
                mask.Set((byte)item.ItemType, true);
            }
        }
        return mask;
    }

    public static bool IsAllowed(EntityManager manager, BuildingTypeEnum type, in PlannedBuildingSettings settings)
    {
        if (settings.HasRecipe)
        {
            if (type != BuildingTypeEnum.Crafter || settings.RecipeId < 0)
            {
                return false;
            }
            if (settings.RecipeId > 0)
            {
                using var recipes = manager.CreateEntityQuery(typeof(RecipeRegistry), typeof(RecipeConfigElement));
                if (recipes.CalculateEntityCount() != 1)
                {
                    return false;
                }
                var entries = manager.GetBuffer<RecipeConfigElement>(recipes.GetSingletonEntity(), true);
                if (!RecipeConfigLookupUtility.TryGetRecipeIndex(entries, settings.RecipeId, out int index))
                {
                    return false;
                }
                if (!entries[index].IsUnlocked)
                {
                    return false;
                }
            }
        }
        if (settings.HasStorageFilter)
        {
            if (type != BuildingTypeEnum.Storage)
            {
                return false;
            }
            using var items = manager.CreateEntityQuery(typeof(ItemRegistry), typeof(ItemConfigElement));
            if (items.CalculateEntityCount() != 1)
            {
                return false;
            }
            var mask = RegisteredItems(manager.GetBuffer<ItemConfigElement>(items.GetSingletonEntity(), true));
            if (!IsValidFilter(settings.Filter, mask))
            {
                return false;
            }
        }
        return true;
    }

    public static Entity Submit(EntityManager manager, Entity target, in PlannedBuildingSettings settings)
    {
        if (!manager.Exists(target))
        {
            return Entity.Null;
        }
        if (!manager.HasComponent<BuildingType>(target))
        {
            return Entity.Null;
        }
        var type = manager.GetComponentData<BuildingType>(target).Type;
        if (!settings.HasValues || !IsAllowed(manager, type, settings))
        {
            return Entity.Null;
        }
        if (settings.HasRecipe)
        {
            Entity request = manager.CreateEntity(typeof(ChangeCrafterRecipeRequest));
            manager.SetComponentData(request, new ChangeCrafterRecipeRequest(target, settings.RecipeId));
            return request;
        }
        using var query = manager.CreateEntityQuery(typeof(StorageFilterRequestSequence));
        Entity sequenceEntity = query.IsEmptyIgnoreFilter
            ? manager.CreateEntity(typeof(StorageFilterRequestSequence)) : query.GetSingletonEntity();
        var sequence = manager.GetComponentData<StorageFilterRequestSequence>(sequenceEntity);
        if (sequence.LastIssued == ulong.MaxValue)
        {
            throw new InvalidOperationException("Storage filter request sequence exhausted.");
        }
        sequence.LastIssued++;
        manager.SetComponentData(sequenceEntity, sequence);
        Entity filterRequest = manager.CreateEntity(typeof(ChangeStorageFilterRequest));
        manager.SetComponentData(filterRequest, new ChangeStorageFilterRequest
        {
            Target = target, Filter = settings.Filter, Sequence = sequence.LastIssued
        });
        return filterRequest;
    }
}
