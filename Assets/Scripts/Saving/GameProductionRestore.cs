using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Entities;

/// <summary>공개 전 World에 영속 작업 원본과 재고를 받기 위한 슬롯 구성을 넣는다.</summary>
internal sealed class GameProductionRestore
{
    private readonly EntityManager _manager;
    private readonly DynamicBuffer<ItemConfigElement> _items;
    private readonly IReadOnlyDictionary<int, ItemTypeEnum> _itemTypes;
    private readonly DynamicBuffer<RecipeConfigElement> _recipes;
    private readonly DynamicBuffer<RecipeIngredientElement> _recipeIngredients;
    private readonly DynamicBuffer<ResearchDefinitionElement> _researchDefinitions;
    private readonly DynamicBuffer<ResearchIngredientElement> _researchIngredients;
    private readonly int _activeResearchId;

    public GameProductionRestore(EntityManager manager, DynamicBuffer<ItemConfigElement> items,
        IReadOnlyDictionary<int, ItemTypeEnum> itemTypes)
    {
        _manager = manager;
        _items = items;
        _itemTypes = itemTypes;
        using var recipeQuery = manager.CreateEntityQuery(typeof(RecipeRegistry));
        Entity recipeRegistry = recipeQuery.GetSingletonEntity();
        _recipes = manager.GetBuffer<RecipeConfigElement>(recipeRegistry, true);
        _recipeIngredients = manager.GetBuffer<RecipeIngredientElement>(recipeRegistry, true);
        using var researchQuery = manager.CreateEntityQuery(typeof(ResearchRegistry));
        Entity researchRegistry = researchQuery.GetSingletonEntity();
        _researchDefinitions = manager.GetBuffer<ResearchDefinitionElement>(researchRegistry, true);
        _researchIngredients = manager.GetBuffer<ResearchIngredientElement>(researchRegistry, true);
        _activeResearchId = manager.GetComponentData<ResearchState>(researchRegistry).ActiveResearchId;
    }

    public static void RestoreResearchProgress(EntityManager manager, SavedResearch saved, bool isLegacyFormat)
    {
        if (isLegacyFormat)
        {
            return;
        }
        using var researchQuery = manager.CreateEntityQuery(typeof(ResearchRegistry));
        Entity registry = researchQuery.GetSingletonEntity();
        var progress = manager.GetBuffer<ResearchProgressElement>(registry);
        progress.Clear();
        foreach (var entry in saved.Progress)
        {
            if (entry == null)
            {
                throw new InvalidDataException("연구 진척 기록이 비어 있습니다.");
            }
            progress.Add(new ResearchProgressElement
            {
                ResearchId = entry.ResearchId, Progress = entry.Progress, IsCompleted = entry.IsCompleted
            });
        }
        manager.SetComponentData(registry, new ResearchState { ActiveResearchId = saved.ActiveResearchId });
        try
        {
            ResearchConfigLoader.ValidatePublishedConfig(manager, registry);
        }
        catch (ArgumentException error)
        {
            throw new InvalidDataException("저장한 연구 진척이나 활성 연구가 현재 연구 정의와 일치하지 않습니다.", error);
        }
        ResearchRewardUtility.InitializeFromProgress(manager, registry);
    }

    public void RestoreBeforeStorage(Entity building, SavedProduction saved, bool isLegacyFormat)
    {
        bool isMiner = _manager.HasComponent<MinerState>(building);
        bool isCrafter = _manager.HasComponent<CrafterState>(building);
        bool storesFuelEnergy = _manager.HasComponent<FuelEnergyState>(building);
        bool isResearchFacility = _manager.HasComponent<ResearchCycleState>(building);
        bool hasProducts = _manager.HasBuffer<ProductSlotElement>(building);
        if (saved == null)
        {
            if (!isLegacyFormat && (isMiner || isCrafter || storesFuelEnergy || isResearchFacility || hasProducts))
            {
                throw new InvalidDataException("건물의 작업 상태 기록이 없습니다.");
            }
            return;
        }
        if (isLegacyFormat || isMiner != saved.MiningProgress.HasValue || isCrafter != (saved.Crafting != null) ||
            storesFuelEnergy != saved.RemainingFuelEnergy.HasValue || isResearchFacility != (saved.ResearchCycle != null) ||
            hasProducts != (saved.Products != null) || storesFuelEnergy != saved.ActiveFuelCapacity.HasValue)
        {
            throw new InvalidDataException("작업 상태 기록이 건물 역할과 일치하지 않습니다.");
        }

        if (isMiner)
        {
            var state = _manager.GetComponentData<MinerState>(building);
            state.Progress = ReadNonnegative(saved.MiningProgress.Value, "채굴 진행도");
            _manager.SetComponentData(building, state);
        }
        if (isCrafter)
        {
            RestoreCrafter(building, saved.Crafting);
        }
        if (storesFuelEnergy)
        {
            float remaining = ReadNonnegative(saved.RemainingFuelEnergy.Value, "남은 연료 에너지");
            float capacity = ReadNonnegative(saved.ActiveFuelCapacity.Value, "연료 에너지 최대치");
            if (capacity < remaining)
            {
                throw new InvalidDataException("연료 에너지 최대치가 잔량보다 작습니다.");
            }
            _manager.SetComponentData(building, new FuelEnergyState
            {
                RemainingEnergy = remaining, ActiveFuelCapacity = capacity
            });
        }
        if (isResearchFacility)
        {
            RestoreResearchCycle(building, saved.ResearchCycle);
        }
        if (hasProducts)
        {
            RestoreProducts(building, saved.Products);
        }
    }

    private void RestoreCrafter(Entity building, SavedCrafting saved)
    {
        if (saved.RecipeId < 0 || !float.IsFinite(saved.Progress) || saved.Progress < 0f || saved.Progress > 1f ||
            (saved.IsActive && saved.RecipeId == 0) || (!saved.IsActive && saved.Progress != 0f))
        {
            throw new InvalidDataException("제작 레시피나 재료 선소비 상태가 유효하지 않습니다.");
        }
        FixedList512Bytes<BuildingInputSlotElement> layout = default;
        if (saved.RecipeId != 0)
        {
            if (!RecipeConfigLookupUtility.TryGetRecipe(_recipes, saved.RecipeId, out var recipe) || !recipe.IsUnlocked ||
                !BuildingInputSlotUtility.TryCalculate(_recipeIngredients, recipe.IngredientStart, recipe.IngredientCount,
                    _items, out layout, out _))
            {
                throw new InvalidDataException("저장한 레시피의 입력 슬롯을 계산할 수 없습니다.");
            }
        }
        var slots = _manager.GetBuffer<BuildingInputSlotElement>(building);
        for (int index = 0; index < layout.Length; index++)
        {
            slots.Add(layout[index]);
        }
        PrepareInputStorage(building, slots.Length);

        var state = _manager.GetComponentData<CrafterState>(building);
        state.SelectedRecipeId = saved.RecipeId;
        state.ActiveRecipeId = saved.RecipeId;
        state.Progress = saved.Progress;
        state.IsCraftingActive = saved.IsActive;
        state.Status = saved.Status;
        _manager.SetComponentData(building, state);
    }

    private void RestoreResearchCycle(Entity building, SavedResearchCycle saved)
    {
        float elapsed = ReadNonnegative(saved.ElapsedTime, "연구 주기 진행 시간");
        if (saved.ResearchId == 0)
        {
            if (elapsed != 0f)
            {
                throw new InvalidDataException("진행 시간에 해당하는 연구 주기가 없습니다.");
            }
        }
        else
        {
            float duration = 0f;
            foreach (var definition in _researchDefinitions)
            {
                if (definition.StableId == saved.ResearchId)
                {
                    duration = definition.CycleDuration;
                    break;
                }
            }
            if (!saved.InputConfigured || saved.ResearchId != _activeResearchId || elapsed >= duration)
            {
                throw new InvalidDataException("시설의 연구 주기가 활성 연구 또는 주기 길이와 일치하지 않습니다.");
            }
        }

        if (saved.InputConfigured)
        {
            var slots = _manager.GetBuffer<BuildingInputSlotElement>(building);
            ResearchMaterialUtility.ConfigureInputSlots(_researchIngredients, _items, slots);
            PrepareInputStorage(building, slots.Length);
            _manager.SetComponentEnabled<ResearchInputConfigured>(building, true);
        }
        _manager.SetComponentData(building, new ResearchCycleState { ResearchId = saved.ResearchId, ElapsedTime = elapsed });
    }

    private void PrepareInputStorage(Entity building, int slotCount)
    {
        _manager.GetBuffer<StorageSlotElement>(building).Resize(slotCount, NativeArrayOptions.ClearMemory);
        _manager.SetComponentData(building, new Storage(slotCount));
    }

    private void RestoreProducts(Entity building, List<SavedStorageSlot> saved)
    {
        var products = _manager.GetBuffer<ProductSlotElement>(building);
        int previousSlot = -1;
        foreach (var entry in saved)
        {
            if (entry == null)
            {
                throw new InvalidDataException("생산품 슬롯 기록이 비어 있습니다.");
            }
            ItemTypeEnum type = GameWorldRestore.ResolveItem(entry.ItemId, _itemTypes);
            if (entry.Slot <= previousSlot || entry.Quantity <= 0 || entry.Quantity > ItemRegistry.GetMaxStack(_items, type))
            {
                throw new InvalidDataException("생산품 슬롯 번호 또는 수량이 유효하지 않습니다.");
            }
            products.Add(new ProductSlotElement(entry.Slot, type, entry.Quantity));
            previousSlot = entry.Slot;
        }
    }

    public void RestoreResearchRecovery(Entity building, SavedResearchCycle saved)
    {
        if (saved == null)
        {
            return;
        }
        var recovery = _manager.GetBuffer<ResearchRecoveryElement>(building);
        var slots = _manager.GetBuffer<StorageSlotElement>(building, true);
        var occupied = new HashSet<int>();
        foreach (var entry in saved.Recovery)
        {
            if (entry == null)
            {
                throw new InvalidDataException("연구 회수 슬롯 기록이 비어 있습니다.");
            }
            ItemTypeEnum type = GameWorldRestore.ResolveItem(entry.ItemId, _itemTypes);
            if ((uint)entry.Slot >= (uint)slots.Length || slots[entry.Slot].ItemType != type ||
                entry.Quantity <= 0 || entry.Quantity > slots[entry.Slot].Quantity)
            {
                throw new InvalidDataException("연구 회수 수량이 실제 보관 슬롯과 일치하지 않습니다.");
            }
            if (!occupied.Add(entry.Slot))
            {
                throw new InvalidDataException("연구 회수 슬롯 번호가 중복됩니다.");
            }
            recovery.Add(new ResearchRecoveryElement { SlotIndex = entry.Slot, ItemType = type, Quantity = entry.Quantity });
        }
    }

    private static float ReadNonnegative(float value, string label)
    {
        if (!float.IsFinite(value) || value < 0f)
        {
            throw new InvalidDataException($"저장한 {label} 값이 유효하지 않습니다.");
        }
        return value;
    }
}
