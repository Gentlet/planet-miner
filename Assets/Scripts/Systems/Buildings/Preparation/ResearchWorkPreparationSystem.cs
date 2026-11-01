using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

[UpdateInGroup(typeof(SimulationPreparationGroup))]
[UpdateBefore(typeof(BuildingPowerDemandPreparationSystem))]
public partial struct ResearchWorkPreparationSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ResearchRegistry>();
        state.RequireForUpdate<ItemRegistry>();
        state.RequireForUpdate<BuildingConfig>();
    }

    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new PrepareResearchWorkJob
        {
            Registry = SystemAPI.GetSingletonEntity<ResearchRegistry>(),
            Items = SystemAPI.GetSingletonEntity<ItemRegistry>(),
            Buildings = SystemAPI.GetSingletonEntity<BuildingConfig>(),
            State = SystemAPI.GetComponentLookup<ResearchState>(true),
            Definitions = SystemAPI.GetBufferLookup<ResearchDefinitionElement>(true),
            Ingredients = SystemAPI.GetBufferLookup<ResearchIngredientElement>(true),
            Progress = SystemAPI.GetBufferLookup<ResearchProgressElement>(true),
            ItemConfigs = SystemAPI.GetBufferLookup<ItemConfigElement>(true),
            BuildingConfigs = SystemAPI.GetBufferLookup<BuildingConfigElement>(true),
            DestroyRequests = SystemAPI.GetComponentLookup<DestroyItemRequest>(true),
            Demolition = SystemAPI.GetComponentLookup<PendingBuildingDemolition>(true)
        }.ScheduleParallel(state.Dependency);
    }
}

/// <summary>실물 소유권/수량은 변경하지 않고 입력 허용/회수 분류와 전력 외 자격만 준비한다.</summary>
[BurstCompile, WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)]
internal partial struct PrepareResearchWorkJob : IJobEntity
{
    public Entity Registry;
    public Entity Items;
    public Entity Buildings;
    [ReadOnly] public ComponentLookup<ResearchState> State;
    [ReadOnly] public BufferLookup<ResearchDefinitionElement> Definitions;
    [ReadOnly] public BufferLookup<ResearchIngredientElement> Ingredients;
    [ReadOnly] public BufferLookup<ResearchProgressElement> Progress;
    [ReadOnly] public BufferLookup<ItemConfigElement> ItemConfigs;
    [ReadOnly] public BufferLookup<BuildingConfigElement> BuildingConfigs;
    [ReadOnly] public ComponentLookup<DestroyItemRequest> DestroyRequests;
    [ReadOnly] public ComponentLookup<PendingBuildingDemolition> Demolition;

    private void Execute(Entity entity, in BuildingType type, in ResearchCycleState cycle,
        ref ResearchWorkPreparation preparation, EnabledRefRW<ResearchWorkPreparation> prepared,
        EnabledRefRW<ResearchInputConfigured> configured, ref Storage storage, ref StorageFilter filter,
        ref DynamicBuffer<BuildingInputSlotElement> slots, ref DynamicBuffer<ResearchInputLimitElement> limits,
        ref DynamicBuffer<ResearchRecoveryElement> recovery, in DynamicBuffer<StoredItemElement> stored,
        in DynamicBuffer<ResearchContributionElement> results)
    {
        preparation = new ResearchWorkPreparation { DefinitionIndex = -1 };
        prepared.ValueRW = true;
        limits.Clear();
        var ingredients = Ingredients[Registry];
        if (!configured.ValueRO)
        {
            ConfigureSlots(ingredients, ItemConfigs[Items], slots);
            storage = new Storage(slots.Length);
            filter = new StorageFilter(StorageFilterMode.Whitelist);
            foreach (var slot in slots)
            {
                filter.Mask.Set((byte)slot.ItemType, true);
            }
            configured.ValueRW = true;
        }

        var active = State[Registry].ActiveResearchId;
        var definitions = Definitions[Registry];
        int index = -1;
        for (int i = 0; i < definitions.Length; i++)
        {
            if (definitions[i].StableId == active)
            {
                index = i;
                break;
            }
        }
        if (index < 0)
        {
            // 완료 자체는 새 반환을 만들지 않는다. 기존 회수 대기만 실제 보관 관계에 맞춰 정리한다.
            for (int i = recovery.Length - 1; i >= 0; i--)
            {
                if (!ContainsStored(stored, recovery[i].Item))
                {
                    recovery.RemoveAt(i);
                }
            }
            return;
        }

        var definition = definitions[index];
        recovery.Clear();
        var kept = new NativeHashMap<int, int>(definition.IngredientCount, Allocator.Temp);
        foreach (var item in stored)
        {
            if (!RecipeConfigLookupUtility.CanConsumeStoredItem(item.ItemEntity, DestroyRequests))
            {
                continue;
            }
            int maximum = MaximumForItem(definition, ingredients, item.ItemType);
            kept.TryGetValue((int)item.ItemType, out int quantity);
            if (quantity >= maximum)
            {
                recovery.Add(new ResearchRecoveryElement { Item = item.ItemEntity });
            }
            else
            {
                kept[(int)item.ItemType] = quantity + 1;
            }
        }
        kept.Dispose();
        if (Demolition.HasComponent(entity) || results.Length > 0)
        {
            return;
        }
        foreach (var progress in Progress[Registry])
        {
            if (progress.ResearchId == active && progress.IsCompleted)
            {
                return;
            }
        }

        if (!BuildingConfigLookupUtility.TryGetConfig(BuildingConfigs[Buildings], type.Type, type.Tier, out var config))
        {
            prepared.ValueRW = false;
            return;
        }
        preparation.DefinitionIndex = index;
        preparation.Speed = config.Speed;
        preparation.CanAdvance = cycle.ResearchId == active && !active.IsEmpty;
        if (recovery.Length > 0)
        {
            return;
        }
        for (int i = 0; i < definition.IngredientCount; i++)
        {
            var ingredient = ingredients[definition.IngredientStart + i];
            limits.Add(new ResearchInputLimitElement { ItemType = ingredient.ItemType, MaximumQuantity = ingredient.Amount * 2 });
        }
        preparation.CanStartCycle = cycle.ResearchId.IsEmpty && ResearchMaterialUtility.HasMaterials(
            definition, ingredients, stored, DestroyRequests);
    }

    private static void ConfigureSlots(DynamicBuffer<ResearchIngredientElement> ingredients,
        DynamicBuffer<ItemConfigElement> items, DynamicBuffer<BuildingInputSlotElement> slots)
    {
        // 모든 정의의 품목별 최대치를 고정 슬롯으로 구성하여 선택 변경이 StoredItemElement의 슬롯을 재배치하지 않게 한다.
        slots.Clear();
        foreach (var item in items)
        {
            if (item.ItemType == ItemTypeEnum.None)
            {
                continue;
            }
            int maximum = 0;
            foreach (var ingredient in ingredients)
            {
                if (ingredient.ItemType == item.ItemType && ingredient.Amount > maximum)
                {
                    maximum = ingredient.Amount;
                }
            }
            if (maximum == 0)
            {
                continue;
            }
            long count = ResearchMaterialUtility.SlotCountForTwoCycles(maximum, item.MaxStack);
            for (int i = 0; i < count; i++)
            {
                slots.Add(new BuildingInputSlotElement(item.ItemType));
            }
        }
    }

    private static int MaximumForItem(in ResearchDefinitionElement definition,
        DynamicBuffer<ResearchIngredientElement> ingredients, ItemTypeEnum type)
    {
        for (int i = 0; i < definition.IngredientCount; i++)
        {
            var ingredient = ingredients[definition.IngredientStart + i];
            if (ingredient.ItemType == type)
            {
                return ingredient.Amount * 2;
            }
        }
        return 0;
    }

    private static bool ContainsStored(DynamicBuffer<StoredItemElement> stored, Entity item)
    {
        foreach (var entry in stored)
        {
            if (entry.ItemEntity == item)
            {
                return true;
            }
        }
        return false;
    }
}
