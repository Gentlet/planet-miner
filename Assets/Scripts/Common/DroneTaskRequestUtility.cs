using System;
using Unity.Entities;

public static class DroneTaskRequestUtility
{
    public static Entity SubmitCreate(EntityManager manager, DroneLogisticsTaskKindEnum kind, Entity target, ItemTypeEnum type, int quantity = 0) => Submit(manager, new DroneTaskRequest { Operation = DroneTaskRequestOperationEnum.Create, Kind = kind, Target = target, ItemType = type, Quantity = quantity });
    public static Entity SubmitCancel(EntityManager manager, Entity task) => Submit(manager, new DroneTaskRequest { Operation = DroneTaskRequestOperationEnum.Cancel, Task = task });
    /// <summary>시뮬레이션 전 메인 스레드 접수. 지원하지 않는 값도 결과로 거부하며 순번 오류는 접수 전에 거절한다.</summary>
    public static Entity Submit(EntityManager manager, DroneTaskRequest value)
    {
        using var query = manager.CreateEntityQuery(typeof(DroneTaskRequestSequence));
        bool create = query.IsEmptyIgnoreFilter;
        ulong next = create ? 1 : query.GetSingleton<DroneTaskRequestSequence>().NextValue;
        if (next == 0)
        {
            throw new InvalidOperationException("Drone task request sequence must be positive.");
        }

        if (next == ulong.MaxValue)
        {
            throw new InvalidOperationException("Drone task request sequence is exhausted.");
        }

        Entity sequence = create ? manager.CreateEntity(typeof(DroneTaskRequestSequence)) : query.GetSingletonEntity();
        manager.SetComponentData(sequence, new DroneTaskRequestSequence { NextValue = next + 1 });
        value.Sequence = next;
        Entity request = manager.CreateEntity(typeof(DroneTaskRequest), typeof(DroneTaskRequestDecision), typeof(DroneTaskRequestExecution));
        manager.SetComponentData(request, value);
        return request;
    }

    public static bool TryConsumeResult(EntityManager manager, Entity request, out DroneTaskRequestResult result)
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

        if (manager.HasComponent<DroneTaskRequest>(request))
        {
            return false;
        }

        if (!manager.HasComponent<DroneTaskRequestResult>(request))
        {
            return false;
        }

        result = manager.GetComponentData<DroneTaskRequestResult>(request);
        manager.DestroyEntity(request);
        return true;
    }

    internal static void RecordResult(EntityCommandBuffer ecb, Entity request, DroneTaskRequestOperationEnum operation, DroneTaskRequestStatusEnum status, Entity task)
    {
        ecb.RemoveComponent<DroneTaskRequest>(request);
        ecb.RemoveComponent<DroneTaskRequestDecision>(request);
        ecb.RemoveComponent<DroneTaskRequestExecution>(request);
        ecb.AddComponent(request, new DroneTaskRequestResult { Operation = operation, Status = status, Task = task });
    }
}
