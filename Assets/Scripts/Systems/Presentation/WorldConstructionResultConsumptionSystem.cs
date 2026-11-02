using Unity.Collections;
using Unity.Entities;

/// <summary>
/// EndSimulation 공개 뒤 건설 입력의 결과만 소비한다. 미공개 요청/다른 호출자의 결과는 보존하며 UI 수명에 종속되지 않는다.
/// GameSimulation의 Fatal 차단 뒤에도 이미 공개된 결과를 정리하도록 Presentation에 둔다.
/// </summary>
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct WorldConstructionResultConsumptionSystem : ISystem
{
    private EntityQuery _results;

    public void OnCreate(ref SystemState state)
    {
        _results = state.GetEntityQuery(ComponentType.ReadOnly<WorldConstructionRequestOwner>(),
            ComponentType.ReadOnly<DroneTaskRequestResult>());
        state.RequireForUpdate(_results);
    }

    public void OnUpdate(ref SystemState state)
    {
        using var results = _results.ToEntityArray(Allocator.Temp);
        foreach (Entity request in results)
        {
            DroneTaskRequestUtility.TryConsumeResult(state.EntityManager, request, out _);
        }
    }
}
