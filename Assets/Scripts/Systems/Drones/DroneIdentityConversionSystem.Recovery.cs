using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public partial class DroneIdentityConversionSystem
{
    private readonly List<Entity> _relocationTargets = new();
    private readonly Dictionary<Entity, int> _plannedRelocationCounts = new();
    private readonly HashSet<Entity> _uniqueStoredDrones = new();

    public bool TryRelocateStoredDronesForStationDestruction(
        Entity stationEntity)
    {
        if (!EntityManager.Exists(stationEntity))
            return false;

        if (!EntityManager.HasBuffer<StoredDroneElement>(stationEntity))
            return true;

        _storedDrones.Clear();
        _uniqueStoredDrones.Clear();
        DynamicBuffer<StoredDroneElement> storedDrones = EntityManager
            .GetBuffer<StoredDroneElement>(stationEntity, true);

        for (int i = 0; i < storedDrones.Length; i++)
        {
            Entity droneEntity = storedDrones[i].droneEntity;

            if (!_uniqueStoredDrones.Add(droneEntity))
                return false;

            _storedDrones.Add(droneEntity);
        }

        if (_storedDrones.Count == 0)
            return true;

        if (_storageLimitQuery.IsEmptyIgnoreFilter)
            return false;

        if (!EntityManager.HasComponent<DroneStationNetwork>(stationEntity))
            return false;

        if (!EntityManager.HasComponent<GridPosition>(stationEntity))
            return false;

        for (int i = 0; i < _storedDrones.Count; i++)
        {
            Entity droneEntity = _storedDrones[i];

            if (!IsValidStoredDrone(stationEntity, droneEntity))
                return false;
        }

        Entity storageLimitEntity = _storageLimitQuery.GetSingletonEntity();
        using NativeArray<ItemStorageLimitElement> storageLimits =
            DynamicBufferCopyUtility.CreateNativeCopy(
                EntityManager.GetBuffer<ItemStorageLimitElement>(
                    storageLimitEntity,
                    true),
                Allocator.Temp);
        using NativeArray<Entity> stationEntities =
            _stationQuery.ToEntityArray(Allocator.Temp);
        DroneStationNetwork sourceNetwork = EntityManager
            .GetComponentData<DroneStationNetwork>(stationEntity);
        int2 originCell = EntityManager
            .GetComponentData<GridPosition>(stationEntity)
            .gridPosition;

        _relocationTargets.Clear();
        _plannedRelocationCounts.Clear();

        for (int i = 0; i < _storedDrones.Count; i++)
        {
            if (!TryFindRelocationTarget(
                    stationEntity,
                    originCell,
                    sourceNetwork.networkId,
                    stationEntities,
                    storageLimits,
                    out Entity targetStation))
                return false;

            _relocationTargets.Add(targetStation);
            _plannedRelocationCounts.TryGetValue(
                targetStation,
                out int plannedCount);
            _plannedRelocationCounts[targetStation] = plannedCount + 1;
        }

        for (int i = 0; i < _storedDrones.Count; i++)
        {
            Entity droneEntity = _storedDrones[i];
            Entity targetStation = _relocationTargets[i];

            if (!DroneStationStorageUtility.TryReleaseStoredDrone(
                    EntityManager,
                    droneEntity))
                return false;

            DroneAssignment assignment = EntityManager
                .GetComponentData<DroneAssignment>(droneEntity);
            assignment.taskEntity = Entity.Null;
            assignment.reservationEntity = Entity.Null;
            assignment.sourceOwner = Entity.Null;
            assignment.destinationOwner = Entity.Null;
            assignment.returnStation = targetStation;
            assignment.networkId = EntityManager
                .GetComponentData<DroneStationNetwork>(targetStation)
                .networkId;
            assignment.emergencyReturn = false;
            EntityManager.SetComponentData(droneEntity, assignment);
            EntityManager.SetComponentData(
                droneEntity,
                new DroneState { value = DroneStateEnum.Returning });
        }

        return true;
    }

    private bool TryFindRelocationTarget(
        Entity destroyedStation,
        int2 originCell,
        int sourceNetworkId,
        NativeArray<Entity> stationEntities,
        NativeArray<ItemStorageLimitElement> storageLimits,
        out Entity targetStation)
    {
        targetStation = Entity.Null;

        for (int pass = 0; pass < 2; pass++)
        {
            bool currentNetworkOnly = pass == 0 && sourceNetworkId > 0;
            float nearestDistanceSquared = float.MaxValue;

            for (int i = 0; i < stationEntities.Length; i++)
            {
                Entity candidate = stationEntities[i];

                if (candidate == destroyedStation)
                    continue;

                if (!EntityManager.HasComponent<BuildingOccupant>(candidate))
                    continue;

                DroneStationNetwork candidateNetwork = EntityManager
                    .GetComponentData<DroneStationNetwork>(candidate);

                if (currentNetworkOnly &&
                    candidateNetwork.networkId != sourceNetworkId)
                    continue;

                if (!CanPlanRelocation(candidate, storageLimits))
                    continue;

                int2 candidateCell = EntityManager
                    .GetComponentData<GridPosition>(candidate)
                    .gridPosition;
                float distanceSquared = math.distancesq(
                    new float2(originCell),
                    new float2(candidateCell));

                if (distanceSquared > nearestDistanceSquared)
                    continue;

                if (distanceSquared == nearestDistanceSquared &&
                    targetStation != Entity.Null &&
                    candidate.Index >= targetStation.Index)
                    continue;

                targetStation = candidate;
                nearestDistanceSquared = distanceSquared;
            }

            if (targetStation != Entity.Null)
                return true;
        }

        return false;
    }

    private bool IsValidStoredDrone(Entity stationEntity, Entity droneEntity)
    {
        if (droneEntity == Entity.Null)
            return false;

        if (!EntityManager.Exists(droneEntity))
            return false;

        if (!EntityManager.HasComponent<StoredDrone>(droneEntity))
            return false;

        StoredDrone storedDrone = EntityManager.GetComponentData<StoredDrone>(
            droneEntity);

        if (storedDrone.stationEntity != stationEntity)
            return false;

        if (!EntityManager.HasComponent<ActiveDrone>(droneEntity))
            return false;

        if (!EntityManager.HasComponent<DroneBattery>(droneEntity))
            return false;

        if (!EntityManager.HasComponent<DroneState>(droneEntity))
            return false;

        if (!EntityManager.HasComponent<DroneAssignment>(droneEntity))
            return false;

        if (!EntityManager.HasComponent<DroneCargo>(droneEntity))
            return false;

        if (!EntityManager.HasComponent<GridPosition>(droneEntity))
            return false;

        if (!EntityManager.HasBuffer<StoredItemElement>(droneEntity))
            return false;

        return EntityManager.GetBuffer<StoredItemElement>(
            droneEntity,
            true).Length == 0;
    }

    private bool CanPlanRelocation(
        Entity stationEntity,
        NativeArray<ItemStorageLimitElement> storageLimits)
    {
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
        _plannedRelocationCounts.TryGetValue(
            stationEntity,
            out int plannedCount);
        int storedDroneCount = DroneStationStorageUtility.GetStoredDroneCount(
            EntityManager,
            stationEntity);

        return StorageCapacityUtility.CanStoreAdditionalItems(
            storedItems,
            reservedCapacity,
            hasReservedCapacity,
            storage.capacity,
            storageLimits,
            ItemTypeEnum.Drone,
            1,
            storedDroneCount + plannedCount,
            DroneStationStorageUtility.GetDedicatedDroneSlotCapacity(
                EntityManager,
                stationEntity));
    }
}
