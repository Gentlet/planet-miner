using Unity.Entities;
using Unity.Transforms;

/// <summary>공개된 역할 전환 결과의 단계/완료와 예약 해제를 소유한다. 실제 수납 전에는 완료 수를 늘리지 않는다.</summary>
[UpdateInGroup(typeof(DroneStateApplyGroup), OrderLast = true)]
[UpdateAfter(typeof(DroneStationLifecycleApplySystem))]
public partial struct DroneStationRelocationApplySystem : ISystem
{
    private Entity _candidates;

    public void OnCreate(ref SystemState state)
    {
        _candidates = DroneSchedulingUtility.GetOrCreateCandidates(state.EntityManager);
    }

    public void OnUpdate(ref SystemState state)
    {
        state.CompleteDependency();
        var manager = state.EntityManager;
        var ecb = state.World.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>().CreateCommandBuffer();
        var intents = manager.GetBuffer<DroneStationRelocationIntentElement>(_candidates);
        foreach (var intent in intents)
        {
            // Reservation의 목적지 재계획과 Execution의 실제 출발 변경을 덮지 않도록 최신 원본에서 전이한다.
            if (!manager.HasBuffer<DroneStationRelocationMemberElement>(intent.Task)) continue;
            var roster = manager.GetBuffer<DroneStationRelocationMemberElement>(intent.Task);
            var relocation = roster[intent.MemberIndex];
            if (relocation.Finished) continue;
            if (intent.Finish)
            {
                if (manager.HasBuffer<DroneStationReservedSlotElement>(relocation.Destination))
                {
                    var rows = manager.GetBuffer<DroneStationReservedSlotElement>(relocation.Destination);
                    DroneStationRelocationUtility.ReleaseSlots(rows, intent.Task, intent.MemberIndex);
                }
                if (intent.Completed && manager.HasComponent<PreparingStationDemolition>(relocation.Source))
                {
                    var preparation = manager.GetComponentData<PreparingStationDemolition>(relocation.Source);
                    if (preparation.Task == relocation.Task)
                    {
                        preparation.CompletedCount++;
                        manager.SetComponentData(relocation.Source, preparation);
                    }
                }
                relocation.Finished = true;
                relocation.Request = Entity.Null;
                roster[intent.MemberIndex] = relocation;
                if (!intent.Completed && manager.Exists(intent.Drone) && manager.HasComponent<DroneStationRelocation>(intent.Drone))
                    ecb.RemoveComponent<DroneStationRelocation>(intent.Drone);
                continue;
            }
            relocation.Drone = intent.Drone;
            relocation.Stage = intent.Stage;
            relocation.Request = intent.Request;
            if (intent.SetDeparturePosition)
                relocation.DeparturePosition = manager.GetComponentData<LocalTransform>(intent.Drone).Position.xy;
            roster[intent.MemberIndex] = relocation;
        }
        intents.Clear();
    }
}
