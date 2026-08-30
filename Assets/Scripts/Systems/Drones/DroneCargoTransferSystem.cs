using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

[UpdateAfter(typeof(DroneMovementSystem))]
public partial class DroneCargoTransferSystem : SystemBase
{
    private EntityQuery _droneQuery;
    private ItemStorageSystem _itemStorage;
    private DroneTaskReservationSystem _reservationSystem;
    private DroneStationNetworkSystem _networkSystem;
    private readonly List<DroneTaskReservedItemElement> _reservedItems = new();

    protected override void OnCreate()
    {
        _droneQuery = GetEntityQuery(
            ComponentType.ReadOnly<ActiveDrone>(),
            ComponentType.ReadWrite<DroneState>(),
            ComponentType.ReadWrite<DroneAssignment>(),
            ComponentType.ReadWrite<DroneCargo>(),
            ComponentType.ReadOnly<GridPosition>(),
            ComponentType.ReadWrite<StoredItemElement>());
        _itemStorage = World.GetOrCreateSystemManaged<ItemStorageSystem>();
        _reservationSystem = World.GetOrCreateSystemManaged<
            DroneTaskReservationSystem>();
        _networkSystem = World.GetOrCreateSystemManaged<
            DroneStationNetworkSystem>();
    }

    protected override void OnUpdate()
    {
        using NativeArray<Entity> droneEntities =
            _droneQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < droneEntities.Length; i++)
        {
            Entity droneEntity = droneEntities[i];
            DroneState state = EntityManager
                .GetComponentData<DroneState>(droneEntity);

            if (state.value == DroneStateEnum.PickingUp)
                PickUpReservedItems(droneEntity);
            else if (state.value == DroneStateEnum.Delivering)
                DeliverReservedItems(droneEntity);
            else if (state.value == DroneStateEnum.RecoveringCargo)
                RecoverCargoAtStorage(droneEntity);
        }
    }

    private void PickUpReservedItems(Entity droneEntity)
    {
        if (!TryGetReservation(
                droneEntity,
                out DroneAssignment assignment,
                out DroneTaskReservation reservation))
        {
            RequestTransferRecovery(droneEntity);
            return;
        }

        CopyReservedItems(assignment.reservationEntity);

        for (int i = 0; i < _reservedItems.Count; i++)
        {
            DroneTaskReservedItemElement reservedItem = _reservedItems[i];

            if (!TryTransferReservedItemToDrone(
                    droneEntity,
                    assignment.reservationEntity,
                    reservedItem))
            {
                UpdateCargoFromOwnedItems(droneEntity, reservation.itemType);
                RequestTransferRecovery(droneEntity);
                return;
            }
        }

        UpdateCargoFromOwnedItems(droneEntity, reservation.itemType);
        DroneCargo cargo = EntityManager
            .GetComponentData<DroneCargo>(droneEntity);

        if (cargo.quantity != reservation.quantity)
        {
            RequestTransferRecovery(droneEntity);
            return;
        }

        EntityManager.SetComponentData(
            droneEntity,
            new DroneState { value = DroneStateEnum.MovingToDelivery });
    }

    private void DeliverReservedItems(Entity droneEntity)
    {
        if (!TryGetReservation(
                droneEntity,
                out DroneAssignment assignment,
                out DroneTaskReservation reservation))
        {
            RequestTransferRecovery(droneEntity);
            return;
        }

        if (!EntityManager.Exists(assignment.destinationOwner))
        {
            RequestTransferRecovery(droneEntity);
            return;
        }

        while (EntityManager.GetBuffer<StoredItemElement>(
                   droneEntity,
                   true).Length > 0)
        {
            if (!_itemStorage.TryTransferReservedItemImmediate<
                    StoredItemElement>(
                    droneEntity,
                    0,
                    assignment.destinationOwner,
                    assignment.reservationEntity))
            {
                UpdateCargoFromOwnedItems(droneEntity, reservation.itemType);
                RequestTransferRecovery(droneEntity);
                return;
            }
        }

        EntityManager.SetComponentData(
            droneEntity,
            new DroneCargo { itemType = ItemTypeEnum.None });

        if (!_reservationSystem.TryCompleteReservationForDrone(
                assignment.reservationEntity,
                droneEntity))
        {
            RequestTransferRecovery(droneEntity);
            return;
        }

        DroneReturnRouteUtility.SetAwaitingDispatchAfterCompletion(
            EntityManager,
            droneEntity,
            assignment.taskEntity,
            assignment.reservationEntity);
    }

    private bool TryTransferReservedItemToDrone(
        Entity droneEntity,
        Entity reservationEntity,
        DroneTaskReservedItemElement reservedItem)
    {
        if (!EntityManager.Exists(reservedItem.sourceOwner))
            return false;

        if (reservedItem.sourceKind == DroneTaskItemSourceKind.Stored)
        {
            int itemIndex = FindItemIndex<StoredItemElement>(
                reservedItem.sourceOwner,
                reservedItem.itemEntity);

            if (itemIndex < 0)
                return false;

            return _itemStorage.TryTransferReservedItemImmediate<
                StoredItemElement>(
                reservedItem.sourceOwner,
                itemIndex,
                droneEntity,
                reservationEntity);
        }

        if (reservedItem.sourceKind == DroneTaskItemSourceKind.Produced)
        {
            int itemIndex = FindItemIndex<ProducedItemElement>(
                reservedItem.sourceOwner,
                reservedItem.itemEntity);

            if (itemIndex < 0)
                return false;

            return _itemStorage.TryTransferReservedItemImmediate<
                ProducedItemElement>(
                reservedItem.sourceOwner,
                itemIndex,
                droneEntity,
                reservationEntity);
        }

        return false;
    }

    private void RecoverCargoAtStorage(Entity droneEntity)
    {
        DroneAssignment assignment = EntityManager
            .GetComponentData<DroneAssignment>(droneEntity);

        if (!EntityManager.Exists(assignment.destinationOwner))
        {
            RequestTransferRecovery(droneEntity);
            return;
        }

        while (EntityManager.GetBuffer<StoredItemElement>(
                   droneEntity,
                   true).Length > 0)
        {
            if (!_itemStorage.TryTransferOwnedItemImmediate<StoredItemElement>(
                    droneEntity,
                    0,
                    assignment.destinationOwner))
            {
                RequestTransferRecovery(droneEntity);
                return;
            }
        }

        EntityManager.SetComponentData(
            droneEntity,
            new DroneCargo { itemType = ItemTypeEnum.None });
        TryReturnToStation(droneEntity, assignment);
    }

    private bool TryGetReservation(
        Entity droneEntity,
        out DroneAssignment assignment,
        out DroneTaskReservation reservation)
    {
        assignment = EntityManager
            .GetComponentData<DroneAssignment>(droneEntity);
        reservation = default;

        if (assignment.reservationEntity == Entity.Null)
            return false;

        if (!EntityManager.Exists(assignment.reservationEntity))
            return false;

        if (!EntityManager.HasComponent<DroneTaskReservation>(
                assignment.reservationEntity))
            return false;

        reservation = EntityManager.GetComponentData<DroneTaskReservation>(
            assignment.reservationEntity);
        return true;
    }

    private void CopyReservedItems(Entity reservationEntity)
    {
        _reservedItems.Clear();
        DynamicBuffer<DroneTaskReservedItemElement> reservedItems =
            EntityManager.GetBuffer<DroneTaskReservedItemElement>(
                reservationEntity,
                true);

        for (int i = 0; i < reservedItems.Length; i++)
            _reservedItems.Add(reservedItems[i]);
    }

    private int FindItemIndex<TElement>(
        Entity ownerEntity,
        Entity itemEntity)
        where TElement : unmanaged, IBufferElementData, IItemStorageElement
    {
        if (!EntityManager.HasBuffer<TElement>(ownerEntity))
            return -1;

        DynamicBuffer<TElement> items = EntityManager
            .GetBuffer<TElement>(ownerEntity, true);

        for (int i = 0; i < items.Length; i++)
        {
            if (items[i].ItemEntity == itemEntity)
                return i;
        }

        return -1;
    }

    private void UpdateCargoFromOwnedItems(
        Entity droneEntity,
        ItemTypeEnum itemType)
    {
        DynamicBuffer<StoredItemElement> cargoItems = EntityManager
            .GetBuffer<StoredItemElement>(droneEntity, true);
        EntityManager.SetComponentData(
            droneEntity,
            new DroneCargo
            {
                itemType = cargoItems.Length > 0
                    ? itemType
                    : ItemTypeEnum.None,
                quantity = cargoItems.Length
            });
    }

    private void TryReturnToStation(
        Entity droneEntity,
        DroneAssignment assignment)
    {
        if (DroneReturnRouteUtility.TrySetReturnRoute(
                EntityManager,
                _networkSystem,
                droneEntity,
                assignment,
                assignment.emergencyReturn))
            return;

        DroneRecoveryRequestUtility.Request(
            EntityManager,
            droneEntity,
            DroneRecoveryReasonEnum.TargetUnavailable);
    }

    private void RequestTransferRecovery(Entity droneEntity)
    {
        DroneRecoveryRequestUtility.Request(
            EntityManager,
            droneEntity,
            DroneRecoveryReasonEnum.TransferFailed);
    }
}
