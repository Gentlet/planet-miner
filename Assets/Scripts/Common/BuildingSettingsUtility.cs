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

    internal static bool IsAllowed(in BuildingConfigElement config, in PlannedBuildingSettings settings, ref SettingsReadContext read)
    {
        return IsAllowed(config.Crafting.IsPresent, config.ItemStorage.FilterEditable, settings, ref read);
    }

    private static bool IsAllowed(bool supportsRecipe, bool supportsFilter, in PlannedBuildingSettings settings, ref SettingsReadContext read)
    {
        if (settings.HasRecipe)
        {
            if (!supportsRecipe || settings.RecipeId < 0)
            {
                return false;
            }
            if (settings.RecipeId > 0)
            {
                var entries = read.Recipes;
                if (!entries.IsCreated || !RecipeConfigLookupUtility.TryGetRecipe(entries, settings.RecipeId, out var recipe))
                {
                    return false;
                }
                if (!recipe.IsUnlocked)
                {
                    return false;
                }
            }
        }
        if (settings.HasStorageFilter)
        {
            if (!supportsFilter)
            {
                return false;
            }
            if (!read.TryReadItemMask(out var mask) || !IsValidFilter(settings.Filter, mask))
            {
                return false;
            }
        }
        return true;
    }

    internal ref struct SettingsReadContext
    {
        private readonly EntityManager _manager;
        private bool _recipesRead;
        private DynamicBuffer<RecipeConfigElement> _recipes;
        private bool _itemMaskRead;
        private bool _hasItemMask;
        private FixedBitSet _itemMask;

        internal SettingsReadContext(EntityManager manager)
        {
            this = default;
            _manager = manager;
        }

        internal DynamicBuffer<RecipeConfigElement> Recipes
        {
            get
            {
                if (!_recipesRead)
                {
                    using var query = _manager.CreateEntityQuery(typeof(RecipeRegistry), typeof(RecipeConfigElement));
                    if (query.CalculateEntityCount() == 1)
                    {
                        _recipes = _manager.GetBuffer<RecipeConfigElement>(query.GetSingletonEntity(), true);
                    }
                    _recipesRead = true;
                }
                return _recipes;
            }
        }

        internal bool TryReadItemMask(out FixedBitSet mask)
        {
            if (!_itemMaskRead)
            {
                using var query = _manager.CreateEntityQuery(typeof(ItemRegistry), typeof(ItemConfigElement));
                if (query.CalculateEntityCount() == 1)
                {
                    _itemMask = RegisteredItems(_manager.GetBuffer<ItemConfigElement>(query.GetSingletonEntity(), true));
                    _hasItemMask = true;
                }
                _itemMaskRead = true;
            }
            mask = _itemMask;
            return _hasItemMask;
        }
    }

    public static bool IsEditableCrafter(EntityManager manager, Entity target) =>
        manager.HasComponent<CrafterState>(target) && !manager.HasComponent<PendingBuildingDemolition>(target) &&
        !manager.HasComponent<Disabled>(target) && !manager.HasComponent<Prefab>(target);

    public static Entity Submit(EntityManager manager, Entity target, in PlannedBuildingSettings settings)
    {
        if (!manager.HasComponent<BuildingIdentity>(target))
        {
            return Entity.Null;
        }
        var read = new SettingsReadContext(manager);
        if (!settings.HasValues || !IsAllowed(IsEditableCrafter(manager, target),
            manager.HasComponent<EditableStorageFilter>(target), settings, ref read))
        {
            return Entity.Null;
        }
        if (settings.HasRecipe)
        {
            if (!IsEditableCrafter(manager, target))
            {
                return Entity.Null;
            }
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
