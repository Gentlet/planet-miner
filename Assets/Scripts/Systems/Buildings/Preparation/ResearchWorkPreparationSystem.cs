using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;

[UpdateInGroup(typeof(SimulationPreparationGroup))]
[UpdateBefore(typeof(BuildingPowerDemandPreparationSystem))]
public partial struct ResearchWorkPreparationSystem : ISystem
{
    private EntityQuery _workQuery;
    public void OnCreate(ref SystemState state)
    {
        _workQuery = SystemAPI.QueryBuilder().WithAll<BuildingType>().WithAll<ResearchCycleState>().WithAllRW<ResearchWorkPreparation>().WithAllRW<ResearchInputConfigured>().WithAllRW<Storage>().WithAllRW<StorageFilter>().WithAllRW<BuildingInputSlotElement>().WithAllRW<ResearchInputLimitElement>().WithAllRW<ResearchRecoveryElement>().WithAll<StoredItemElement>().WithAll<ResearchContributionElement>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).Build();

        state.RequireForUpdate<ResearchRegistry>();
        state.RequireForUpdate<ItemRegistry>();
        state.RequireForUpdate<BuildingConfig>();
        state.RequireForUpdate<ResearchStatBonuses>();
    }

    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new PrepareResearchWorkJob
        {
            EntityHandle = SystemAPI.GetEntityTypeHandle(),
            BuildingTypeHandle = SystemAPI.GetComponentTypeHandle<BuildingType>(true),
            ResearchCycleStateHandle = SystemAPI.GetComponentTypeHandle<ResearchCycleState>(true),
            ResearchWorkPreparationHandle = SystemAPI.GetComponentTypeHandle<ResearchWorkPreparation>(false),
            ResearchInputConfiguredHandle = SystemAPI.GetComponentTypeHandle<ResearchInputConfigured>(false),
            StorageHandle = SystemAPI.GetComponentTypeHandle<Storage>(false),
            StorageFilterHandle = SystemAPI.GetComponentTypeHandle<StorageFilter>(false),
            BuildingInputSlotElementHandle = SystemAPI.GetBufferTypeHandle<BuildingInputSlotElement>(false),
            ResearchInputLimitElementHandle = SystemAPI.GetBufferTypeHandle<ResearchInputLimitElement>(false),
            ResearchRecoveryElementHandle = SystemAPI.GetBufferTypeHandle<ResearchRecoveryElement>(false),
            StoredItemElementHandle = SystemAPI.GetBufferTypeHandle<StoredItemElement>(true),
            ResearchContributionElementHandle = SystemAPI.GetBufferTypeHandle<ResearchContributionElement>(true),
            Registry = SystemAPI.GetSingletonEntity<ResearchRegistry>(),
            Items = SystemAPI.GetSingletonEntity<ItemRegistry>(),
            Buildings = SystemAPI.GetSingletonEntity<BuildingConfig>(),
            SpeedBonus = SystemAPI.GetSingleton<ResearchStatBonuses>().ResearchSpeed,
            State = SystemAPI.GetComponentLookup<ResearchState>(true),
            Definitions = SystemAPI.GetBufferLookup<ResearchDefinitionElement>(true),
            Ingredients = SystemAPI.GetBufferLookup<ResearchIngredientElement>(true),
            Progress = SystemAPI.GetBufferLookup<ResearchProgressElement>(true),
            ItemConfigs = SystemAPI.GetBufferLookup<ItemConfigElement>(true),
            BuildingConfigs = SystemAPI.GetBufferLookup<BuildingConfigElement>(true),
            DestroyRequests = SystemAPI.GetComponentLookup<DestroyItemRequest>(true),
            Demolition = SystemAPI.GetComponentLookup<PendingBuildingDemolition>(true)
        }.ScheduleParallel(_workQuery, state.Dependency);
    }
}

/// <summary>실물 소유권/수량은 변경하지 않고 입력 허용/회수 분류와 전력 외 자격만 준비한다.</summary>
[BurstCompile]
internal partial struct PrepareResearchWorkJob : IJobChunk
{
    [ReadOnly] public EntityTypeHandle EntityHandle;
    [ReadOnly] public ComponentTypeHandle<BuildingType> BuildingTypeHandle;
    [ReadOnly] public ComponentTypeHandle<ResearchCycleState> ResearchCycleStateHandle;
    public ComponentTypeHandle<ResearchWorkPreparation> ResearchWorkPreparationHandle;
    public ComponentTypeHandle<ResearchInputConfigured> ResearchInputConfiguredHandle;
    public ComponentTypeHandle<Storage> StorageHandle;
    public ComponentTypeHandle<StorageFilter> StorageFilterHandle;
    public BufferTypeHandle<BuildingInputSlotElement> BuildingInputSlotElementHandle;
    public BufferTypeHandle<ResearchInputLimitElement> ResearchInputLimitElementHandle;
    public BufferTypeHandle<ResearchRecoveryElement> ResearchRecoveryElementHandle;
    [ReadOnly] public BufferTypeHandle<StoredItemElement> StoredItemElementHandle;
    [ReadOnly] public BufferTypeHandle<ResearchContributionElement> ResearchContributionElementHandle;

    private struct RegistryReadContext
    {
        public RegistryBufferReadCache<ResearchDefinitionElement> Definitions;
        public RegistryBufferReadCache<ResearchIngredientElement> Ingredients;
        public RegistryBufferReadCache<ResearchProgressElement> Progress;
        public RegistryBufferReadCache<ItemConfigElement> ItemConfigs;
        public RegistryBufferReadCache<BuildingConfigElement> BuildingConfigs;
    }

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
    {
        var entities = chunk.GetNativeArray(EntityHandle);
        var buildingTypeValues = chunk.GetNativeArray(ref BuildingTypeHandle);
        var researchCycleStateValues = chunk.GetNativeArray(ref ResearchCycleStateHandle);
        var researchWorkPreparationValues = chunk.GetNativeArray(ref ResearchWorkPreparationHandle);
        var researchWorkPreparationEnabled = chunk.GetEnabledMask(ref ResearchWorkPreparationHandle);
        var researchInputConfiguredEnabled = chunk.GetEnabledMask(ref ResearchInputConfiguredHandle);
        var storageValues = chunk.GetNativeArray(ref StorageHandle);
        var storageFilterValues = chunk.GetNativeArray(ref StorageFilterHandle);
        var buildingInputSlotElementValues = chunk.GetBufferAccessorRW(ref BuildingInputSlotElementHandle);
        var researchInputLimitElementValues = chunk.GetBufferAccessorRW(ref ResearchInputLimitElementHandle);
        var researchRecoveryElementValues = chunk.GetBufferAccessorRW(ref ResearchRecoveryElementHandle);
        var storedItemElementValues = chunk.GetBufferAccessorRO(ref StoredItemElementHandle);
        var researchContributionElementValues = chunk.GetBufferAccessorRO(ref ResearchContributionElementHandle);
        var registryRead = new RegistryReadContext();
        var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
        while (enumerator.NextEntityIndex(out int index))
        {
            var typeValue = buildingTypeValues[index];
            var cycleValue = researchCycleStateValues[index];
            var preparationRef = new RefRW<ResearchWorkPreparation>(researchWorkPreparationValues, index);
            var storageRef = new RefRW<Storage>(storageValues, index);
            var filterRef = new RefRW<StorageFilter>(storageFilterValues, index);
            var slotsValue = buildingInputSlotElementValues[index];
            var limitsValue = researchInputLimitElementValues[index];
            var recoveryValue = researchRecoveryElementValues[index];
            var storedValue = storedItemElementValues[index];
            var resultsValue = researchContributionElementValues[index];
            ExecuteEntity(entities[index],
                in typeValue,
                in cycleValue,
                ref preparationRef.ValueRW,
                researchWorkPreparationEnabled.GetEnabledRefRW<ResearchWorkPreparation>(index),
                researchInputConfiguredEnabled.GetEnabledRefRW<ResearchInputConfigured>(index),
                ref storageRef.ValueRW,
                ref filterRef.ValueRW,
                ref slotsValue,
                ref limitsValue,
                ref recoveryValue,
                in storedValue,
                in resultsValue, ref registryRead);
        }
    }

    public Entity Registry;
    public Entity Items;
    public Entity Buildings;
    public double SpeedBonus;
    [ReadOnly] public ComponentLookup<ResearchState> State;
    [ReadOnly] public BufferLookup<ResearchDefinitionElement> Definitions;
    [ReadOnly] public BufferLookup<ResearchIngredientElement> Ingredients;
    [ReadOnly] public BufferLookup<ResearchProgressElement> Progress;
    [ReadOnly] public BufferLookup<ItemConfigElement> ItemConfigs;
    [ReadOnly] public BufferLookup<BuildingConfigElement> BuildingConfigs;
    [ReadOnly] public ComponentLookup<DestroyItemRequest> DestroyRequests;
    [ReadOnly] public ComponentLookup<PendingBuildingDemolition> Demolition;

    private void ExecuteEntity(Entity entity, in BuildingType type, in ResearchCycleState cycle,
        ref ResearchWorkPreparation preparation, EnabledRefRW<ResearchWorkPreparation> prepared,
        EnabledRefRW<ResearchInputConfigured> configured, ref Storage storage, ref StorageFilter filter,
        ref DynamicBuffer<BuildingInputSlotElement> slots, ref DynamicBuffer<ResearchInputLimitElement> limits,
        ref DynamicBuffer<ResearchRecoveryElement> recovery, in DynamicBuffer<StoredItemElement> stored,
        in DynamicBuffer<ResearchContributionElement> results, ref RegistryReadContext registryRead)
    {
        preparation = new ResearchWorkPreparation { DefinitionIndex = -1 };
        prepared.ValueRW = true;
        limits.Clear();
        var ingredients = registryRead.Ingredients.Read(Ingredients, Registry);
        if (!configured.ValueRO)
        {
            ConfigureSlots(ingredients, registryRead.ItemConfigs.Read(ItemConfigs, Items), slots);
            storage = new Storage(slots.Length);
            filter = new StorageFilter(StorageFilterMode.Whitelist);
            foreach (var slot in slots)
            {
                filter.Mask.Set((byte)slot.ItemType, true);
            }
            configured.ValueRW = true;
        }

        var active = State[Registry].ActiveResearchId;
        var definitions = registryRead.Definitions.Read(Definitions, Registry);
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
        var kept = new NativeHashMap<int, ResearchItemLimit>(definition.IngredientCount, Allocator.Temp);
        bool hasReadMaximums = false;
        foreach (var item in stored)
        {
            if (!RecipeConfigLookupUtility.CanConsumeStoredItem(item.ItemEntity, DestroyRequests))
            {
                continue;
            }
            if (!hasReadMaximums)
            {
                for (int i = 0; i < definition.IngredientCount; i++)
                {
                    var ingredient = ingredients[definition.IngredientStart + i];
                    kept.TryAdd((int)ingredient.ItemType, new ResearchItemLimit
                    {
                        Maximum = ResearchMaterialUtility.MaximumBufferedQuantity(ingredient.Amount)
                    });
                }
                hasReadMaximums = true;
            }
            kept.TryGetValue((int)item.ItemType, out var limit);
            if (limit.KeptCount >= limit.Maximum)
            {
                recovery.Add(new ResearchRecoveryElement { Item = item.ItemEntity });
            }
            else
            {
                limit.KeptCount++;
                kept[(int)item.ItemType] = limit;
            }
        }
        kept.Dispose();
        if (Demolition.HasComponent(entity) || results.Length > 0)
        {
            return;
        }
        foreach (var progress in registryRead.Progress.Read(Progress, Registry))
        {
            if (progress.ResearchId == active && progress.IsCompleted)
            {
                return;
            }
        }

        if (!BuildingConfigLookupUtility.TryGetConfig(registryRead.BuildingConfigs.Read(BuildingConfigs, Buildings), type.BuildingId, out var config))
        {
            prepared.ValueRW = false;
            return;
        }
        preparation.DefinitionIndex = index;
        preparation.Speed = ResearchRewardUtility.EffectiveSpeed(config.Speed, SpeedBonus);
        preparation.CanAdvance = cycle.ResearchId == active && active != 0;
        if (recovery.Length > 0)
        {
            return;
        }
        for (int i = 0; i < definition.IngredientCount; i++)
        {
            var ingredient = ingredients[definition.IngredientStart + i];
            limits.Add(new ResearchInputLimitElement { ItemType = ingredient.ItemType, MaximumQuantity = ResearchMaterialUtility.MaximumBufferedQuantity(ingredient.Amount) });
        }
        preparation.CanStartCycle = cycle.ResearchId == 0 && ResearchMaterialUtility.HasMaterials(
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

    private struct ResearchItemLimit
    {
        public int Maximum;
        public int KeptCount;
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
