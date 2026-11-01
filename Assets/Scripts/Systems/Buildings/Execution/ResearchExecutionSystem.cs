using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

[UpdateInGroup(typeof(BuildingExecutionGroup))]
public partial struct ResearchExecutionSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ResearchRegistry>();
    }

    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new ExecuteResearchJob
        {
            Registry = SystemAPI.GetSingletonEntity<ResearchRegistry>(),
            Definitions = SystemAPI.GetBufferLookup<ResearchDefinitionElement>(true),
            Ingredients = SystemAPI.GetBufferLookup<ResearchIngredientElement>(true),
            DestroyRequests = SystemAPI.GetComponentLookup<DestroyItemRequest>(false)
        }.Schedule(state.Dependency);
    }
}

/// <summary>전체 재료 묶음을 확인한 뒤 선소비하며, 확정 전력 시간 안에서 틱당 최대 한 주기만 완료한다.</summary>
[BurstCompile]
internal partial struct ExecuteResearchJob : IJobEntity
{
    public Entity Registry;
    [ReadOnly] public BufferLookup<ResearchDefinitionElement> Definitions;
    [ReadOnly] public BufferLookup<ResearchIngredientElement> Ingredients;
    public ComponentLookup<DestroyItemRequest> DestroyRequests;

    private void Execute(ref ResearchCycleState cycle, in ResearchWorkPreparation preparation, in ResearchDecision decision,
        ref DynamicBuffer<StoredItemElement> stored, ref DynamicBuffer<ResearchContributionElement> results)
    {
        if (preparation.DefinitionIndex < 0 || results.Length > 0)
        {
            return;
        }
        var definition = Definitions[Registry][preparation.DefinitionIndex];
        var ingredients = Ingredients[Registry];
        if (cycle.ResearchId.IsEmpty)
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
