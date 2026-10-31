using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public static class DroneDemolitionUtility
{
    public static bool IsTarget(EntityManager manager, Entity target)
    {
        if (target == Entity.Null || !manager.HasComponent<BuildingType>(target)) return false;
        if (manager.HasComponent<Prefab>(target) || manager.HasComponent<IndestructibleBuilding>(target)) return false;
        var type = manager.GetComponentData<BuildingType>(target).Type;
        return type != BuildingTypeEnum.None && type != BuildingTypeEnum.ConstructionSite &&
            type != BuildingTypeEnum.MainFacility &&
            manager.HasComponent<GridPosition>(target) && manager.HasComponent<BuildingFootprint>(target) &&
            manager.HasComponent<Direction>(target);
    }

    public static bool IsApprovedTarget(EntityManager manager, Entity target) =>
        IsTarget(manager, target) && (manager.HasComponent<PendingBuildingDemolition>(target) ||
            manager.HasComponent<PreparingStationDemolition>(target));

    public static bool IsCurrentTask(EntityManager manager, in DroneLogisticsTask task)
    {
        if (!IsApprovedTarget(manager, task.Target)) return false;
        Entity linked = manager.HasComponent<PreparingStationDemolition>(task.Target)
            ? manager.GetComponentData<PreparingStationDemolition>(task.Target).Task
            : manager.GetComponentData<PendingBuildingDemolition>(task.Target).Task;
        return manager.HasComponent<DroneLogisticsTask>(linked) &&
            manager.GetComponentData<DroneLogisticsTask>(linked).CreationSequence == task.CreationSequence;
    }

    public static bool CanConfirm(EntityManager manager, Entity entity, uint revision, uint observationRevision)
    {
        if (!manager.HasComponent<DroneTaskAssignment>(entity)) return false;
        var assignment = manager.GetComponentData<DroneTaskAssignment>(entity);
        if (assignment.Revision != revision || assignment.State != DroneTaskAssignmentStateEnum.AwaitingDelivery ||
            assignment.NextAction != DroneActionKindEnum.DemolishBuilding) return false;
        if (!manager.HasComponent<DroneLogisticsTask>(assignment.Task)) return false;
        var task = manager.GetComponentData<DroneLogisticsTask>(assignment.Task);
        if (task.Kind != DroneLogisticsTaskKindEnum.BuildingDemolition || task.State != DroneLogisticsTaskStateEnum.Open ||
            task.Target != assignment.Destination || !IsCurrentTask(manager, task)) return false;
        if (manager.HasComponent<PreparingStationDemolition>(task.Target) &&
            !DroneStationRelocationUtility.IsComplete(manager, task.Target)) return false;
        Entity worker = assignment.Worker;
        if (!DroneSchedulingUtility.IsActiveEntity(manager, worker) ||
            manager.GetComponentData<DroneWorkerAssignment>(worker).Assignment != entity) return false;
        if (!manager.GetBuffer<StoredItemElement>(worker, true).IsEmpty) return false;
        var observation = manager.GetComponentData<DroneWorkerObservation>(worker);
        if (observation.Revision != observationRevision) return false;
        float charge = manager.GetComponentData<DroneBatteryState>(worker).CurrentCharge;
        if (!math.isfinite(charge) || charge < 0f) return false;
        if (!DroneTargetPointUtility.TryGetTargetPoint(manager, task.Target, out float2 point)) return false;
        return math.all(manager.GetComponentData<LocalTransform>(worker).Position.xy == point) &&
            math.all(observation.Position.xy == point);
    }
}
