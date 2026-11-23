using Unity.Mathematics;
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
        _workQuery = SystemAPI.QueryBuilder().WithAll<BuildingIdentity>().WithAll<ResearchCycleState>().WithAllRW<ResearchWorkPreparation>().WithAllRW<ResearchInputConfigured>().WithAllRW<Storage>().WithAllRW<StorageFilter>().WithAllRW<BuildingInputSlotElement>().WithAllRW<ResearchInputLimitElement>().WithAllRW<ResearchRecoveryElement>().WithAllRW<StorageSlotElement>().WithAll<ResearchContributionElement>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).Build();

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
            IdentityHandle = SystemAPI.GetComponentTypeHandle<BuildingIdentity>(true),
            ResearchCycleStateHandle = SystemAPI.GetComponentTypeHandle<ResearchCycleState>(true),
            ResearchWorkPreparationHandle = SystemAPI.GetComponentTypeHandle<ResearchWorkPreparation>(false),
            ResearchInputConfiguredHandle = SystemAPI.GetComponentTypeHandle<ResearchInputConfigured>(false),
            StorageHandle = SystemAPI.GetComponentTypeHandle<Storage>(false),
            StorageFilterHandle = SystemAPI.GetComponentTypeHandle<StorageFilter>(false),
            BuildingInputSlotElementHandle = SystemAPI.GetBufferTypeHandle<BuildingInputSlotElement>(false),
            ResearchInputLimitElementHandle = SystemAPI.GetBufferTypeHandle<ResearchInputLimitElement>(false),
            ResearchRecoveryElementHandle = SystemAPI.GetBufferTypeHandle<ResearchRecoveryElement>(false),
            StorageSlotElementHandle = SystemAPI.GetBufferTypeHandle<StorageSlotElement>(false),
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
            Demolition = SystemAPI.GetComponentLookup<PendingBuildingDemolition>(true)
        }.ScheduleParallel(_workQuery, state.Dependency);
    }
}

/// <summary>실물 소유권/수량은 변경하지 않고 입력 허용/회수 분류와 전력 외 자격만 준비한다.</summary>
[BurstCompile]
internal partial struct PrepareResearchWorkJob : IJobChunk
{
    [ReadOnly] public EntityTypeHandle EntityHandle;
    [ReadOnly] public ComponentTypeHandle<BuildingIdentity> IdentityHandle;
    [ReadOnly] public ComponentTypeHandle<ResearchCycleState> ResearchCycleStateHandle;
    public ComponentTypeHandle<ResearchWorkPreparation> ResearchWorkPreparationHandle;
    public ComponentTypeHandle<ResearchInputConfigured> ResearchInputConfiguredHandle;
    public ComponentTypeHandle<Storage> StorageHandle;
    public ComponentTypeHandle<StorageFilter> StorageFilterHandle;
    public BufferTypeHandle<BuildingInputSlotElement> BuildingInputSlotElementHandle;
    public BufferTypeHandle<ResearchInputLimitElement> ResearchInputLimitElementHandle;
    public BufferTypeHandle<ResearchRecoveryElement> ResearchRecoveryElementHandle;
    public BufferTypeHandle<StorageSlotElement> StorageSlotElementHandle;
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
        var buildingTypeValues = chunk.GetNativeArray(ref IdentityHandle);
        var researchCycleStateValues = chunk.GetNativeArray(ref ResearchCycleStateHandle);
        var researchWorkPreparationValues = chunk.GetNativeArray(ref ResearchWorkPreparationHandle);
        var researchWorkPreparationEnabled = chunk.GetEnabledMask(ref ResearchWorkPreparationHandle);
        var researchInputConfiguredEnabled = chunk.GetEnabledMask(ref ResearchInputConfiguredHandle);
        var storageValues = chunk.GetNativeArray(ref StorageHandle);
        var storageFilterValues = chunk.GetNativeArray(ref StorageFilterHandle);
        var buildingInputSlotElementValues = chunk.GetBufferAccessorRW(ref BuildingInputSlotElementHandle);
        var researchInputLimitElementValues = chunk.GetBufferAccessorRW(ref ResearchInputLimitElementHandle);
        var researchRecoveryElementValues = chunk.GetBufferAccessorRW(ref ResearchRecoveryElementHandle);
        var storedItemElementValues = chunk.GetBufferAccessorRW(ref StorageSlotElementHandle);
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
                storedValue,
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
    [ReadOnly] public ComponentLookup<PendingBuildingDemolition> Demolition;

    private void ExecuteEntity(Entity entity, in BuildingIdentity type, in ResearchCycleState cycle,
        ref ResearchWorkPreparation preparation, EnabledRefRW<ResearchWorkPreparation> prepared,
        EnabledRefRW<ResearchInputConfigured> configured, ref Storage storage, ref StorageFilter filter,
        ref DynamicBuffer<BuildingInputSlotElement> slots, ref DynamicBuffer<ResearchInputLimitElement> limits,
        ref DynamicBuffer<ResearchRecoveryElement> recovery, DynamicBuffer<StorageSlotElement> stored,
        in DynamicBuffer<ResearchContributionElement> results, ref RegistryReadContext registryRead)
    {
        preparation = new ResearchWorkPreparation { DefinitionIndex = -1 };
        prepared.ValueRW = true;
        limits.Clear();
        var ingredients = registryRead.Ingredients.Read(Ingredients, Registry);
        if (!configured.ValueRO)
        {
            ResearchMaterialUtility.ConfigureInputSlots(ingredients, registryRead.ItemConfigs.Read(ItemConfigs, Items), slots);
            storage = new Storage(slots.Length);
            stored.Resize(slots.Length, NativeArrayOptions.ClearMemory);
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
            // 완료 자체는 새 회수를 만들지 않는다. 남은 회수량은 현재 슬롯 수량으로 제한한다.
            for (int indexToCheck = recovery.Length - 1; indexToCheck >= 0; indexToCheck--)
            {
                var entry = recovery[indexToCheck];
                var slot = stored[entry.SlotIndex];
                int remaining = slot.ItemType == entry.ItemType ? math.min(entry.Quantity, slot.Quantity) : 0;
                if (remaining == 0) recovery.RemoveAt(indexToCheck);
                else
                {
                    entry.Quantity = remaining;
                    recovery[indexToCheck] = entry;
                }
            }
            return;
        }

        var definition = definitions[index];
        recovery.Clear();
        var remainingToKeep = new NativeHashMap<int, int>(definition.IngredientCount, Allocator.Temp);
        for (int ingredientIndex = 0; ingredientIndex < definition.IngredientCount; ingredientIndex++)
        {
            var ingredient = ingredients[definition.IngredientStart + ingredientIndex];
            remainingToKeep.TryAdd((int)ingredient.ItemType, ResearchMaterialUtility.MaximumBufferedQuantity(ingredient.Amount));
        }

        for (int slotIndex = 0; slotIndex < stored.Length; slotIndex++)
        {
            var item = stored[slotIndex];
            if (item.Quantity == 0) continue;
            remainingToKeep.TryGetValue((int)item.ItemType, out int limit);
            int kept = math.min(limit, item.Quantity);
            if (kept > 0) remainingToKeep[(int)item.ItemType] = limit - kept;
            if (item.Quantity > kept)
            {
                recovery.Add(new ResearchRecoveryElement
                {
                    SlotIndex = slotIndex,
                    ItemType = item.ItemType,
                    Quantity = item.Quantity - kept
                });
            }
        }
        remainingToKeep.Dispose();
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
        preparation.Speed = ResearchRewardUtility.EffectiveSpeed(config.Research.Speed, SpeedBonus);
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
            definition, ingredients, stored);
    }

}
