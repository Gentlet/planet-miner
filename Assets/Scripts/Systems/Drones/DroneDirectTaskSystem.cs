using Unity.Collections;
using Unity.Entities;

[UpdateAfter(typeof(DroneMovementSystem))]
[UpdateBefore(typeof(DroneCargoTransferSystem))]
[UpdateBefore(typeof(DroneRecoverySystem))]
public partial class DroneDirectTaskSystem : SystemBase
{
    private EntityQuery _assignedTaskQuery;
    private EntityQuery _storageQuery;
    private EntityQuery _storageLimitQuery;
    private DroneStationNetworkSystem _networkSystem;
    private DroneTaskReservationSystem _reservationSystem;
    private ItemStorageSystem _itemStorage;

    protected override void OnCreate()
    {
        _assignedTaskQuery = GetEntityQuery(
            ComponentType.ReadOnly<DroneTask>(),
            ComponentType.ReadWrite<DroneTaskStatus>(),
            ComponentType.ReadWrite<DroneDirectTaskAssignment>());
        _storageQuery = GetEntityQuery(
            ComponentType.ReadOnly<Storage>(),
            ComponentType.ReadOnly<BuildingOccupant>(),
            ComponentType.ReadOnly<GridPosition>(),
            ComponentType.ReadOnly<StoredItemElement>());
        _storageLimitQuery = GetEntityQuery(
            ComponentType.ReadOnly<ItemStorageLimitElement>());
        _networkSystem = World.GetOrCreateSystemManaged<
            DroneStationNetworkSystem>();
        _reservationSystem = World.GetOrCreateSystemManaged<
            DroneTaskReservationSystem>();
        _itemStorage = World.GetOrCreateSystemManaged<ItemStorageSystem>();
    }

    protected override void OnUpdate()
    {
        using NativeArray<Entity> tasks =
            _assignedTaskQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < tasks.Length; i++)
            UpdateAssignedTask(tasks[i]);
    }
}
