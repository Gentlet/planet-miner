using Unity.Collections;
using Unity.Entities;

/// <summary>
/// 종료 경계에 공개된 World UI 소유 결과만 소비한다. 미공개 요청/다른 호출자의 결과는 보존하며 UI 수명에 종속되지 않는다.
/// GameSimulation의 Fatal 차단 뒤에도 이미 공개된 결과를 정리하도록 Presentation에 둔다.
/// </summary>
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct WorldUiRequestResultConsumptionSystem : ISystem
{
    private EntityQuery _results;
    public void OnCreate(ref SystemState state)
    {
        _results = state.GetEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<WorldUiRequestOwner>() }, Any = new[] { ComponentType.ReadOnly<DroneTaskRequestResult>(), ComponentType.ReadOnly<DroneStationResult>(), ComponentType.ReadOnly<ResearchSelectionResult>() } });
        state.RequireForUpdate(_results);
    }

    public void OnUpdate(ref SystemState state)
    {
        using var results = _results.ToEntityArray(Allocator.Temp);
        foreach (Entity request in results)
        {
            var manager = state.EntityManager;
            ulong token = manager.GetComponentData<WorldUiRequestOwner>(request).FeedbackToken;
            string message;
            if (manager.HasComponent<DroneTaskRequestResult>(request))
            {
                if (!DroneTaskRequestUtility.TryConsumeResult(manager, request, out var result))
                {
                    continue;
                }

                message = result.Operation + ": " + result.Status;
            }
            else if (manager.HasComponent<ResearchSelectionResult>(request))
            {
                if (!ResearchSelectionRequestUtility.TryConsumeResult(manager, request, out var result))
                {
                    continue;
                }
                message = "연구 선택: " + result.Status;
            }
            else
            {
                if (!DroneStationRequestUtility.TryConsumeResult(manager, request, out var result))
                {
                    continue;
                }

                message = result.Operation + ": " + result.Status;
            }

            // TryConsumeResult는 요청을 파괴한다. 식별과 값만 전달하며 UI가 요청을 다시 조회하지 않는다.
            if (token != 0)
            {
                WorldUiRequestFeedbackRelay.Deliver(manager, token, new WorldUiRequestFeedback(request, message));
            }
        }
    }
}
