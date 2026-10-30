using System;
using Unity.Entities;

public static class DroneStationRequestUtility
{
    /// <summary>외부 입력 또는 정거장 Decision의 메인 스레드 접수. 요청은 순번 순서로 중재하고 같은 수행자의 중복 적용을 막는다.</summary>
    public static Entity Submit(EntityManager manager, Entity drone, Entity station, DroneStationOperation operation)
    {
        using var query = manager.CreateEntityQuery(typeof(DroneStationRequestSequence));
        bool createSequence = query.IsEmptyIgnoreFilter;
        Entity sequenceEntity = createSequence
            ? manager.CreateEntity(typeof(DroneStationRequestSequence)) : query.GetSingletonEntity();
        ulong sequence = createSequence ? 1 : manager.GetComponentData<DroneStationRequestSequence>(sequenceEntity).NextValue;
        if (sequence == 0)
        {
            throw new InvalidOperationException("Drone station request sequence must be positive.");
        }
        if (sequence == ulong.MaxValue)
        {
            throw new InvalidOperationException("Drone station request sequence exhausted.");
        }
        manager.SetComponentData(sequenceEntity, new DroneStationRequestSequence { NextValue = sequence + 1 });
        Entity request = manager.CreateEntity(typeof(DroneStationRequest), typeof(DroneStationDecision));
        manager.SetComponentData(request, new DroneStationRequest
        {
            Drone = drone, Station = station, Operation = operation, Sequence = sequence
        });
        return request;
    }

    public static bool TryConsumeResult(EntityManager manager, Entity request, out DroneStationResult result)
    {
        result = default;
        if (request == Entity.Null)
        {
            return false;
        }
        if (!manager.Exists(request))
        {
            return false;
        }
        if (!manager.HasComponent<DroneStationResult>(request))
        {
            return false;
        }
        result = manager.GetComponentData<DroneStationResult>(request);
        manager.DestroyEntity(request);
        return true;
    }
}
