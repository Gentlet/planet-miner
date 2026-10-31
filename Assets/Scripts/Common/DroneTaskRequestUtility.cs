using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

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

    /// <summary>
    /// 시뮬레이션 전 메인 스레드 접수 시 양 끝 셀을 포함하는 범위의 바닥 실물을 한 번 선택한다.
    /// 활성 World 소유 실물 중 실제 벨트가 없는 셀만 선택한다.
    /// 반환값은 기존 Create 요청 핸들이며 각 결과는 EndSimulation 뒤 TryConsumeResult로 소비한다.
    /// 이후 유입/생성품을 계속 모집하는 영역 상태는 만들지 않는다.
    /// </summary>
    /// <exception cref="InvalidOperationException">선택분 전체의 접수번호가 부족하면 일부 요청도 접수하지 않고 거부한다.</exception>
    public static Entity[] SubmitRecoveryRange(EntityManager manager, int2 first, int2 last)
    {
        int2 min = math.min(first, last);
        int2 max = math.max(first, last);
        using var query = manager.CreateEntityQuery(typeof(ItemIdentity), typeof(ItemOwnership), typeof(GridPosition));
        using var items = query.ToEntityArray(Allocator.Temp);
        using var belts = DroneWorldItemRecoveryUtility.ReadBeltPositions(manager);
        var selected = new List<Entity>();
        foreach (Entity item in items)
        {
            int2 position = manager.GetComponentData<GridPosition>(item).Value;
            if (math.any(position < min) || math.any(position > max))
            {
                continue;
            }
            var type = manager.GetComponentData<ItemIdentity>(item).Type;
            if (!DroneSchedulingUtility.IsWorldItem(manager, item, type))
            {
                continue;
            }
            if (DroneWorldItemRecoveryUtility.ContainsBelt(belts, position))
            {
                continue;
            }
            selected.Add(item);
        }

        // 일괄 요청 도중 접수번호가 소진되어 일부만 접수되는 것을 막는다.
        if ((ulong)selected.Count > ReadAvailableRequestSequenceCount(manager))
        {
            throw new InvalidOperationException("Not enough drone task request sequences for recovery range.");
        }

        // Query 순서를 접수 순서로 사용하지 않고 이 일괄 요청의 발급 순서만 고정한다.
        selected.Sort((left, right) => left.Index != right.Index
            ? left.Index.CompareTo(right.Index) : left.Version.CompareTo(right.Version));
        var requests = new Entity[selected.Count];
        for (int i = 0; i < selected.Count; i++)
        {
            Entity item = selected[i];
            requests[i] = SubmitCreate(manager, DroneLogisticsTaskKindEnum.WorldItemRecovery, item,
                manager.GetComponentData<ItemIdentity>(item).Type);
        }
        return requests;
    }
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
