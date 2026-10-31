using System;
using Unity.Entities;

public static class DroneTaskRequestUtility
{
    /// <summary>Command 내부 어댑터가 승인 부수효과 전에 검사할 발급 가능량이다. 실제 순번은 Submit만 변경한다.</summary>
    internal static ulong ReadAvailableRequestSequenceCount(EntityManager manager)
    {
        using var query = manager.CreateEntityQuery(typeof(DroneTaskRequestSequence));
        ulong next = query.IsEmptyIgnoreFilter ? 1 : query.GetSingleton<DroneTaskRequestSequence>().NextValue;
        return next == 0 ? 0 : ulong.MaxValue - next;
    }

    public static Entity SubmitCreate(EntityManager manager, DroneLogisticsTaskKindEnum kind, Entity target, ItemTypeEnum type, int quantity = 0) => Submit(manager, new DroneTaskRequest { Operation = DroneTaskRequestOperationEnum.Create, Kind = kind, Target = target, ItemType = type, Quantity = quantity });
    public static Entity SubmitCancel(EntityManager manager, Entity task) => Submit(manager, new DroneTaskRequest { Operation = DroneTaskRequestOperationEnum.Cancel, Task = task });
    /// <summary>외부는 시뮬레이션 전, 건물 Command 어댑터는 Command 내부에서 메인 스레드로 접수한다. 순번 오류는 접수 전에 거절한다.</summary>
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
