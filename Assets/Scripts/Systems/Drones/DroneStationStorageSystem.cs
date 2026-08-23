using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[UpdateAfter(typeof(DroneMovementSystem))]
[UpdateBefore(typeof(DroneIdentityConversionSystem))]
public partial class DroneStationStorageSystem : SystemBase
{
    private EntityQuery _droneQuery;
    private EntityQuery _stationQuery;
    private EntityQuery _storageLimitQuery;
    private DroneStationNetworkSystem _networkSystem;

    protected override void OnCreate()
    {
        _droneQuery = GetEntityQuery(
            ComponentType.ReadOnly<ActiveDrone>(),
            ComponentType.ReadOnly<DroneBattery>(),
            ComponentType.ReadWrite<DroneState>(),
            ComponentType.ReadOnly<DroneAssignment>());
        _stationQuery = GetEntityQuery(
            ComponentType.ReadOnly<DroneStation>(),
            ComponentType.ReadOnly<Storage>(),
            ComponentType.ReadOnly<GridPosition>(),
            ComponentType.ReadOnly<StoredItemElement>(),
            ComponentType.ReadOnly<StoredDroneElement>());
        _storageLimitQuery = GetEntityQuery(
            ComponentType.ReadOnly<ItemStorageLimitElement>());
        _networkSystem = World.GetOrCreateSystemManaged<
            DroneStationNetworkSystem>();
    }

    protected override void OnUpdate()
    {
        if (_storageLimitQuery.IsEmptyIgnoreFilter)
            return;

        Entity storageLimitEntity = _storageLimitQuery.GetSingletonEntity();
        using NativeArray<ItemStorageLimitElement> storageLimits =
            DynamicBufferCopyUtility.CreateNativeCopy(
                EntityManager.GetBuffer<ItemStorageLimitElement>(
                    storageLimitEntity,
                    true),
                Allocator.Temp);
        using NativeArray<Entity> droneEntities =
            _droneQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < droneEntities.Length; i++)
            TryStoreReturnedDrone(droneEntities[i], storageLimits);
    }

    private void TryStoreReturnedDrone(
        Entity droneEntity,
        NativeArray<ItemStorageLimitElement> storageLimits)
    {
        if (EntityManager.HasComponent<StoredDrone>(droneEntity))
            return;

        DroneState state = EntityManager.GetComponentData<DroneState>(
            droneEntity);

        if (state.value != DroneStateEnum.Stored &&
            state.value != DroneStateEnum.AwaitingCharge &&
            state.value != DroneStateEnum.AwaitingStorage)
            return;

        DroneAssignment assignment = EntityManager
            .GetComponentData<DroneAssignment>(droneEntity);
        Entity stationEntity = assignment.returnStation;

        if (!CanStoreDrone(stationEntity, storageLimits))
        {
            if (!TryRerouteToAvailableStation(
                    droneEntity,
                    ref assignment,
                    storageLimits))
            {
                EntityManager.SetComponentData(
                    droneEntity,
                    new DroneState
                    {
                        value = DroneStateEnum.AwaitingStorage
                    });
                return;
            }

            EntityManager.SetComponentData(droneEntity, assignment);
            EntityManager.SetComponentData(
                droneEntity,
                new DroneState { value = DroneStateEnum.Returning });
            return;
        }

        if (!DroneStationStorageUtility.TryAddStoredDrone(
                EntityManager,
                stationEntity,
                droneEntity))
        {
            EntityManager.SetComponentData(
                droneEntity,
                new DroneState { value = DroneStateEnum.AwaitingStorage });
            return;
        }

        DroneBattery battery = EntityManager.GetComponentData<DroneBattery>(
            droneEntity);
        DroneStateEnum storedState = battery.current >= battery.maximum
            ? DroneStateEnum.Stored
            : DroneStateEnum.AwaitingCharge;
        EntityManager.SetComponentData(
            droneEntity,
            new DroneState { value = storedState });
    }

    private bool TryRerouteToAvailableStation(
        Entity droneEntity,
        ref DroneAssignment assignment,
        NativeArray<ItemStorageLimitElement> storageLimits)
    {
        GridPosition dronePosition = EntityManager
            .GetComponentData<GridPosition>(droneEntity);
        Entity nearestStation = Entity.Null;
        float nearestDistanceSquared = float.MaxValue;
        bool restrictToCurrentNetwork = assignment.networkId > 0;

        using NativeArray<Entity> stationEntities =
            _stationQuery.ToEntityArray(Allocator.Temp);

        for (int pass = 0; pass < 2; pass++)
        {
            bool currentNetworkOnly = restrictToCurrentNetwork && pass == 0;

            for (int i = 0; i < stationEntities.Length; i++)
            {
                Entity candidate = stationEntities[i];

                if (!CanStoreDrone(candidate, storageLimits))
                    continue;

                if (currentNetworkOnly &&
                    !IsStationInNetwork(candidate, assignment.networkId))
                    continue;

                int2 candidatePosition = EntityManager
                    .GetComponentData<GridPosition>(candidate)
                    .gridPosition;
                float distanceSquared = math.distancesq(
                    new float2(dronePosition.gridPosition),
                    new float2(candidatePosition));

                if (distanceSquared >= nearestDistanceSquared)
                    continue;

                nearestStation = candidate;
                nearestDistanceSquared = distanceSquared;
            }

            if (nearestStation != Entity.Null)
                break;
        }

        if (nearestStation == Entity.Null)
            return false;

        assignment.returnStation = nearestStation;

        if (_networkSystem.TryGetNetworkId(
                nearestStation,
                out int networkId))
            assignment.networkId = networkId;

        return true;
    }

    private bool IsStationInNetwork(Entity stationEntity, int networkId)
    {
        if (!_networkSystem.TryGetNetworkId(
                stationEntity,
                out int stationNetworkId))
            return false;

        return stationNetworkId == networkId;
    }

    private bool CanStoreDrone(
        Entity stationEntity,
        NativeArray<ItemStorageLimitElement> storageLimits)
    {
        if (stationEntity == Entity.Null)
            return false;

        if (!EntityManager.Exists(stationEntity))
            return false;

        if (!EntityManager.HasComponent<DroneStation>(stationEntity))
            return false;

        if (!EntityManager.HasComponent<Storage>(stationEntity))
            return false;

        if (!EntityManager.HasBuffer<StoredItemElement>(stationEntity))
            return false;

        if (!EntityManager.HasBuffer<StoredDroneElement>(stationEntity))
            return false;

        Storage storage = EntityManager.GetComponentData<Storage>(
            stationEntity);
        DynamicBuffer<StoredItemElement> storedItems = EntityManager
            .GetBuffer<StoredItemElement>(stationEntity, true);
        bool hasReservedCapacity = EntityManager.HasBuffer<
            DroneReservedStorageCapacityElement>(stationEntity);
        DynamicBuffer<DroneReservedStorageCapacityElement> reservedCapacity =
            hasReservedCapacity
                ? EntityManager.GetBuffer<DroneReservedStorageCapacityElement>(
                    stationEntity,
                    true)
                : default;
        return StorageCapacityUtility.CanStoreAdditionalItems(
            storedItems,
            reservedCapacity,
            hasReservedCapacity,
            storage.capacity,
            storageLimits,
            ItemTypeEnum.Drone,
            1,
            DroneStationStorageUtility.GetStoredDroneCount(
                EntityManager,
                stationEntity));
    }
}
