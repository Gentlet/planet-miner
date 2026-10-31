using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>사용자 일반 회수의 바닥 자격과 기존 회수 사유의 유효성을 공유한다. 실물 소유권은 변경하지 않는다.</summary>
public static class DroneWorldItemRecoveryUtility
{
    public static bool IsFloorItem(EntityManager manager, Entity item, ItemTypeEnum type)
    {
        if (!DroneSchedulingUtility.IsWorldItem(manager, item, type))
        {
            return false;
        }

        using var belts = ReadBeltPositions(manager);
        return !ContainsBelt(belts, manager.GetComponentData<GridPosition>(item).Value);
    }

    public static bool IsReasonValid(EntityManager manager, in DroneLogisticsTask task, NativeArray<Entity> sites)
    {
        switch (task.RecoveryReason)
        {
            case DroneRecoveryReasonEnum.DroneDrop:
            case DroneRecoveryReasonEnum.BuildingDemolition:
                return true;
            case DroneRecoveryReasonEnum.SiteClearance:
                if (DroneSchedulingUtility.IsUnderSite(manager, task.Target, sites))
                {
                    return true;
                }
                break;
            case DroneRecoveryReasonEnum.UserRequested:
                break;
            default:
                return false;
        }

        // 자동 사유가 사라져도 승인된 사용자 의도는 현재 일반 바닥 자격으로 계속 진행한다.
        if (!task.HasExplicitRecoveryIntent && task.RecoveryReason != DroneRecoveryReasonEnum.UserRequested)
        {
            return false;
        }
        using var belts = ReadBeltPositions(manager);
        return !ContainsBelt(belts, manager.GetComponentData<GridPosition>(task.Target).Value);
    }

    internal static NativeArray<GridPosition> ReadBeltPositions(EntityManager manager)
    {
        // EndBuilding의 신설/철거와 Disabled 벨트도 현재 원본으로 판정한다. 이전 Sync의 인덱스로 대체하지 않는다.
        using var query = manager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<BeltComponent>(), ComponentType.ReadOnly<GridPosition>() },
            Options = EntityQueryOptions.IncludeDisabledEntities
        });
        return query.ToComponentDataArray<GridPosition>(Allocator.Temp);
    }

    internal static bool ContainsBelt(NativeArray<GridPosition> belts, int2 position)
    {
        foreach (var belt in belts)
        {
            if (math.all(belt.Value == position))
            {
                return true;
            }
        }
        return false;
    }
}
