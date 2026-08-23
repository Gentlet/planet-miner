using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[UpdateAfter(typeof(DroneCargoTransferSystem))]
public partial class DroneRecoverySystem : SystemBase
{
    private const int MaximumDropSearchRadius = 16;

    private EntityQuery _recoveryQuery;
    private EntityQuery _storageQuery;
    private EntityQuery _storageLimitQuery;
    private ItemStorageSystem _itemStorage;
    private DroneTaskReservationSystem _reservationSystem;
    private DroneStationNetworkSystem _networkSystem;

    protected override void OnCreate()
    {
        _recoveryQuery = GetEntityQuery(
            ComponentType.ReadOnly<ActiveDrone>(),
            ComponentType.ReadOnly<DroneRecoveryRequest>(),
            ComponentType.ReadWrite<DroneAssignment>(),
            ComponentType.ReadWrite<DroneState>(),
            ComponentType.ReadWrite<DroneCargo>(),
            ComponentType.ReadOnly<GridPosition>(),
            ComponentType.ReadWrite<StoredItemElement>());
        _storageQuery = GetEntityQuery(
            ComponentType.ReadOnly<Storage>(),
            ComponentType.ReadOnly<StoredItemElement>(),
            ComponentType.ReadOnly<GridPosition>(),
            ComponentType.ReadOnly<BuildingOccupant>());
        _storageLimitQuery = GetEntityQuery(
            ComponentType.ReadOnly<ItemStorageLimitElement>());
        _itemStorage = World.GetOrCreateSystemManaged<ItemStorageSystem>();
        _reservationSystem = World.GetOrCreateSystemManaged<
            DroneTaskReservationSystem>();
        _networkSystem = World.GetOrCreateSystemManaged<
            DroneStationNetworkSystem>();
    }

    protected override void OnUpdate()
    {
        using NativeArray<Entity> droneEntities =
            _recoveryQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < droneEntities.Length; i++)
            RecoverDrone(droneEntities[i]);
    }

    private void RecoverDrone(Entity droneEntity)
    {
        DroneRecoveryRequest request = EntityManager
            .GetComponentData<DroneRecoveryRequest>(droneEntity);
        DroneAssignment assignment = EntityManager
            .GetComponentData<DroneAssignment>(droneEntity);
        ReleaseAssignmentReservation(
            droneEntity,
            assignment,
            request.reason);
        assignment.taskEntity = Entity.Null;
        assignment.reservationEntity = Entity.Null;
        assignment.sourceOwner = Entity.Null;
        assignment.emergencyReturn =
            assignment.emergencyReturn ||
            request.reason == DroneRecoveryReasonEnum.InsufficientBattery;

        if (TryStartCargoRecovery(droneEntity, ref assignment))
        {
            EntityManager.SetComponentData(droneEntity, assignment);
            EntityManager.SetComponentData(
                droneEntity,
                new DroneState
                {
                    value = DroneStateEnum.MovingToRecoveryStorage
                });
            EntityManager.RemoveComponent<DroneRecoveryRequest>(droneEntity);
            return;
        }

        if (!TryDropRemainingCargo(droneEntity))
            return;

        ClearCargo(droneEntity);

        if (!DroneReturnRouteUtility.TrySetReturnRoute(
                EntityManager,
                _networkSystem,
                droneEntity,
                assignment,
                assignment.emergencyReturn))
            return;
        EntityManager.RemoveComponent<DroneRecoveryRequest>(droneEntity);
    }

    private void ReleaseAssignmentReservation(
        Entity droneEntity,
        DroneAssignment assignment,
        DroneRecoveryReasonEnum reason)
    {
        bool cancelTask =
            reason != DroneRecoveryReasonEnum.InsufficientBattery;

        if (assignment.reservationEntity != Entity.Null)
        {
            if (_reservationSystem.TryReleaseReservationForDrone(
                    assignment.reservationEntity,
                    droneEntity,
                    cancelTask))
                return;
        }

        if (cancelTask && assignment.taskEntity != Entity.Null)
        {
            _reservationSystem.CancelTaskAfterLostReservation(
                assignment.taskEntity);
        }
    }

    private bool TryStartCargoRecovery(
        Entity droneEntity,
        ref DroneAssignment assignment)
    {
        DynamicBuffer<StoredItemElement> cargoItems = EntityManager
            .GetBuffer<StoredItemElement>(droneEntity, true);

        if (cargoItems.Length == 0)
            return false;

        ItemTypeEnum itemType = cargoItems[0].type;
        int quantity = cargoItems.Length;

        if (TryFindRecoveryStorage(
                droneEntity,
                assignment.networkId,
                itemType,
                quantity,
                out Entity storageEntity))
        {
            assignment.destinationOwner = storageEntity;
            return true;
        }

        return false;
    }

    private bool TryFindRecoveryStorage(
        Entity droneEntity,
        int networkId,
        ItemTypeEnum itemType,
        int quantity,
        out Entity storageEntity)
    {
        storageEntity = Entity.Null;

        if (_storageLimitQuery.IsEmptyIgnoreFilter)
            return false;

        GridPosition dronePosition = EntityManager
            .GetComponentData<GridPosition>(droneEntity);
        Entity storageLimitEntity = _storageLimitQuery.GetSingletonEntity();
        using NativeArray<ItemStorageLimitElement> storageLimits =
            DynamicBufferCopyUtility.CreateNativeCopy(
                EntityManager.GetBuffer<ItemStorageLimitElement>(
                    storageLimitEntity,
                    true),
                Allocator.Temp);
        using NativeArray<Entity> storageEntities =
            _storageQuery.ToEntityArray(Allocator.Temp);
        float nearestDistanceSquared = float.MaxValue;

        for (int i = 0; i < storageEntities.Length; i++)
        {
            Entity candidateEntity = storageEntities[i];
            Storage storage = EntityManager
                .GetComponentData<Storage>(candidateEntity);
            DynamicBuffer<StoredItemElement> storedItems = EntityManager
                .GetBuffer<StoredItemElement>(candidateEntity, true);
            bool hasReservedCapacity = EntityManager.HasBuffer<
                DroneReservedStorageCapacityElement>(candidateEntity);
            DynamicBuffer<DroneReservedStorageCapacityElement>
                reservedCapacity = hasReservedCapacity
                    ? EntityManager.GetBuffer<
                        DroneReservedStorageCapacityElement>(
                        candidateEntity,
                        true)
                    : default;

            if (!StorageCapacityUtility.CanStoreAdditionalItems(
                    storedItems,
                    reservedCapacity,
                    hasReservedCapacity,
                    storage.capacity,
                    storageLimits,
                    itemType,
                    quantity,
                    DroneStationStorageUtility.GetStoredDroneCount(
                        EntityManager,
                        candidateEntity)))
                continue;

            int2 candidatePosition = EntityManager
                .GetComponentData<GridPosition>(candidateEntity)
                .gridPosition;

            if (!_networkSystem.TryGetNetworkIdAtCell(
                    candidatePosition,
                    out int candidateNetworkId))
                continue;

            if (candidateNetworkId != networkId)
                continue;

            float distanceSquared = math.distancesq(
                new float2(dronePosition.gridPosition),
                new float2(candidatePosition));

            if (distanceSquared >= nearestDistanceSquared)
                continue;

            nearestDistanceSquared = distanceSquared;
            storageEntity = candidateEntity;
        }

        return storageEntity != Entity.Null;
    }

    private bool TryDropRemainingCargo(Entity droneEntity)
    {
        GridPosition dronePosition = EntityManager
            .GetComponentData<GridPosition>(droneEntity);

        while (EntityManager.GetBuffer<StoredItemElement>(
                   droneEntity,
                   true).Length > 0)
        {
            if (!TryDropFirstCargoItem(
                    droneEntity,
                    dronePosition.gridPosition))
                return false;
        }

        return true;
    }

    private bool TryDropFirstCargoItem(Entity droneEntity, int2 originCell)
    {
        DynamicBuffer<StoredItemElement> cargoItems = EntityManager
            .GetBuffer<StoredItemElement>(droneEntity, true);

        if (cargoItems.Length == 0)
            return true;

        Entity itemEntity = cargoItems[0].itemEntity;

        for (int radius = 0; radius <= MaximumDropSearchRadius; radius++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if (math.max(math.abs(x), math.abs(y)) != radius)
                        continue;

                    if (_itemStorage.TryRestoreItemImmediate<StoredItemElement>(
                            droneEntity,
                            0,
                            originCell + new int2(x, y)))
                    {
                        DroneWorldItemRecoveryTaskUtility.TryCreateRequest(
                            EntityManager,
                            itemEntity,
                            0);
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private void ClearCargo(Entity droneEntity)
    {
        EntityManager.SetComponentData(
            droneEntity,
            new DroneCargo { itemType = ItemTypeEnum.None });
    }
}
