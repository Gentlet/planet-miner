using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;

[UpdateInGroup(typeof(BuildingExecutionGroup))]
public partial struct ResearchExecutionSystem : ISystem
{
    private EntityQuery _workQuery;
    public void OnCreate(ref SystemState state)
    {
        _workQuery = SystemAPI.QueryBuilder().WithAllRW<ResearchCycleState>().WithAll<ResearchWorkPreparation>().WithAll<ResearchDecision>().WithAllRW<StoredItemElement>().WithAllRW<ResearchContributionElement>().Build();

        state.RequireForUpdate<ResearchRegistry>();
    }

    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new ExecuteResearchJob
        {
            ResearchCycleStateHandle = SystemAPI.GetComponentTypeHandle<ResearchCycleState>(false),
            ResearchWorkPreparationHandle = SystemAPI.GetComponentTypeHandle<ResearchWorkPreparation>(true),
            ResearchDecisionHandle = SystemAPI.GetComponentTypeHandle<ResearchDecision>(true),
            StoredItemElementHandle = SystemAPI.GetBufferTypeHandle<StoredItemElement>(false),
            ResearchContributionElementHandle = SystemAPI.GetBufferTypeHandle<ResearchContributionElement>(false),
            Registry = SystemAPI.GetSingletonEntity<ResearchRegistry>(),
            Definitions = SystemAPI.GetBufferLookup<ResearchDefinitionElement>(true),
            Ingredients = SystemAPI.GetBufferLookup<ResearchIngredientElement>(true),
            DestroyRequests = SystemAPI.GetComponentLookup<DestroyItemRequest>(false)
        }.Schedule(_workQuery, state.Dependency);
    }
}

/// <summary>전체 재료 묶음을 확인한 뒤 선소비하며, 확정 전력 시간 안에서 틱당 최대 한 주기만 완료한다.</summary>
[BurstCompile]
internal partial struct ExecuteResearchJob : IJobChunk
{
    public ComponentTypeHandle<ResearchCycleState> ResearchCycleStateHandle;
    [ReadOnly] public ComponentTypeHandle<ResearchWorkPreparation> ResearchWorkPreparationHandle;
    [ReadOnly] public ComponentTypeHandle<ResearchDecision> ResearchDecisionHandle;
    public BufferTypeHandle<StoredItemElement> StoredItemElementHandle;
    public BufferTypeHandle<ResearchContributionElement> ResearchContributionElementHandle;

    private struct RegistryReadContext
    {
        public RegistryBufferReadCache<ResearchDefinitionElement> Definitions;
        public RegistryBufferReadCache<ResearchIngredientElement> Ingredients;
    }

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
    {
        var researchCycleStateValues = chunk.GetNativeArray(ref ResearchCycleStateHandle);
        var researchWorkPreparationValues = chunk.GetNativeArray(ref ResearchWorkPreparationHandle);
        var researchDecisionValues = chunk.GetNativeArray(ref ResearchDecisionHandle);
        var storedItemElementValues = chunk.GetBufferAccessorRW(ref StoredItemElementHandle);
        var researchContributionElementValues = chunk.GetBufferAccessorRW(ref ResearchContributionElementHandle);
        var registryRead = new RegistryReadContext();
        var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
        while (enumerator.NextEntityIndex(out int index))
        {
            var cycleRef = new RefRW<ResearchCycleState>(researchCycleStateValues, index);
            var preparationValue = researchWorkPreparationValues[index];
            var decisionValue = researchDecisionValues[index];
            var storedValue = storedItemElementValues[index];
            var resultsValue = researchContributionElementValues[index];
            ExecuteEntity(ref cycleRef.ValueRW,
                in preparationValue,
                in decisionValue,
                ref storedValue,
                ref resultsValue, ref registryRead);
        }
    }

    public Entity Registry;
    [ReadOnly] public BufferLookup<ResearchDefinitionElement> Definitions;
    [ReadOnly] public BufferLookup<ResearchIngredientElement> Ingredients;
    public ComponentLookup<DestroyItemRequest> DestroyRequests;

    private void ExecuteEntity(ref ResearchCycleState cycle, in ResearchWorkPreparation preparation, in ResearchDecision decision,
        ref DynamicBuffer<StoredItemElement> stored, ref DynamicBuffer<ResearchContributionElement> results, ref RegistryReadContext registryRead)
    {
        if (preparation.DefinitionIndex < 0 || results.Length > 0)
        {
            return;
        }
        var definition = registryRead.Definitions.Read(Definitions, Registry)[preparation.DefinitionIndex];
        var ingredients = registryRead.Ingredients.Read(Ingredients, Registry);
        if (cycle.ResearchId == 0)
        {
            if (!decision.CanStartCycle)
            {
                return;
            }
            if (!ResearchMaterialUtility.HasMaterials(definition, ingredients, stored, DestroyRequests))
            {
                return;
            }
            // 먼저 소유 버퍼에서 제거하여 같은 틱 다른 소비자가 재사용하지 못하게 한다. 실물 삭제는 기존 Item Lifecycle에 맡긴다.
            for (int i = 0; i < definition.IngredientCount; i++)
            {
                var ingredient = ingredients[definition.IngredientStart + i];
                int remaining = ingredient.Amount;
                for (int j = stored.Length - 1; j >= 0 && remaining > 0; j--)
                {
                    var item = stored[j];
                    if (item.ItemType != ingredient.ItemType ||
                        !RecipeConfigLookupUtility.CanConsumeStoredItem(item.ItemEntity, DestroyRequests))
                    {
                        continue;
                    }
                    stored.RemoveAt(j);
                    DestroyRequests.SetComponentEnabled(item.ItemEntity, true);
                    remaining--;
                }
            }
            cycle = new ResearchCycleState { ResearchId = definition.StableId };
        }
        if (decision.CanAdvance)
        {
            cycle.ElapsedTime += decision.PoweredTime * preparation.Speed;
        }
        if (cycle.ElapsedTime < definition.CycleDuration)
        {
            return;
        }
        results.Add(new ResearchContributionElement { ResearchId = definition.StableId, Progress = definition.ProgressPerCycle });
        cycle = default;
    }
}
