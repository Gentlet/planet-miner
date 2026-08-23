using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public partial class DroneIdentityConversionSystem
{
    private const int MaximumDropSearchRadius = 16;
    private readonly List<int2> _dropCells = new();
    private readonly List<float3> _dropPositions = new();
    private readonly HashSet<int2> _plannedDropCells = new();

    public bool TryRestoreStoredDronesForStationDestruction(
        Entity stationEntity,
        int2 originCell,
        bool createRecoveryTasks)
    {
        if (!EntityManager.Exists(stationEntity))
            return false;

        if (!EntityManager.HasBuffer<StoredDroneElement>(stationEntity))
            return true;

        _storedDrones.Clear();
        DynamicBuffer<StoredDroneElement> storedDrones = EntityManager
            .GetBuffer<StoredDroneElement>(stationEntity, true);

        for (int i = 0; i < storedDrones.Length; i++)
            _storedDrones.Add(storedDrones[i].droneEntity);

        for (int i = 0; i < _storedDrones.Count; i++)
        {
            Entity droneEntity = _storedDrones[i];

            if (!IsValidStoredDrone(stationEntity, droneEntity))
                return false;
        }

        _dropCells.Clear();
        _dropPositions.Clear();
        _plannedDropCells.Clear();

        for (int i = 0; i < _storedDrones.Count; i++)
        {
            Entity droneEntity = _storedDrones[i];

            if (!TryFindDropPosition(
                    droneEntity,
                    originCell,
                    out int2 targetCell,
                    out float3 targetPosition))
                return false;

            _dropCells.Add(targetCell);
            _dropPositions.Add(targetPosition);
        }

        for (int i = 0; i < _storedDrones.Count; i++)
        {
            Entity droneEntity = _storedDrones[i];

            if (!TryConvertStoredDroneToWorldItem(
                    stationEntity,
                    droneEntity,
                    _dropCells[i],
                    _dropPositions[i]))
                return false;

            if (createRecoveryTasks)
            {
                DroneWorldItemRecoveryTaskUtility.TryCreateRequest(
                    EntityManager,
                    droneEntity,
                    0);
            }
        }

        return true;
    }

    private bool TryConvertStoredDroneToWorldItem(
        Entity stationEntity,
        Entity droneEntity,
        int2 targetCell,
        float3 targetPosition)
    {
        if (!IsValidStoredDrone(stationEntity, droneEntity))
            return false;

        ActiveDrone activeDrone = EntityManager
            .GetComponentData<ActiveDrone>(droneEntity);
        DroneBattery battery = EntityManager
            .GetComponentData<DroneBattery>(droneEntity);
        DroneState state = EntityManager
            .GetComponentData<DroneState>(droneEntity);
        DroneAssignment assignment = EntityManager
            .GetComponentData<DroneAssignment>(droneEntity);
        DroneCargo cargo = EntityManager
            .GetComponentData<DroneCargo>(droneEntity);

        if (!DroneStationStorageUtility.TryReleaseStoredDrone(
                EntityManager,
                droneEntity))
            return false;

        RemoveActiveDroneIdentity(droneEntity);
        EntityManager.AddComponentData(
            droneEntity,
            new Item { type = ItemTypeEnum.Drone });
        EntityManager.AddComponent<ItemCellChanged>(droneEntity);

        if (_itemTracking.TryRegisterItemImmediate(
                droneEntity,
                targetCell,
                targetPosition))
            return true;

        EntityManager.RemoveComponent<Item>(droneEntity);
        EntityManager.RemoveComponent<ItemCellChanged>(droneEntity);
        RestoreActiveDroneIdentity(
            stationEntity,
            droneEntity,
            activeDrone,
            battery,
            state,
            assignment,
            cargo);
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

        if (!EntityManager.HasBuffer<StoredItemElement>(droneEntity))
            return false;

        return EntityManager.GetBuffer<StoredItemElement>(
            droneEntity,
            true).Length == 0;
    }

    private void RemoveActiveDroneIdentity(Entity droneEntity)
    {
        if (EntityManager.HasBuffer<StoredItemElement>(droneEntity))
            EntityManager.RemoveComponent<StoredItemElement>(droneEntity);

        EntityManager.RemoveComponent<ActiveDrone>(droneEntity);
        EntityManager.RemoveComponent<DroneBattery>(droneEntity);
        EntityManager.RemoveComponent<DroneState>(droneEntity);
        EntityManager.RemoveComponent<DroneAssignment>(droneEntity);
        EntityManager.RemoveComponent<DroneCargo>(droneEntity);

        if (EntityManager.HasComponent<ValidationDrone>(droneEntity))
            EntityManager.RemoveComponent<ValidationDrone>(droneEntity);
    }

    private void RestoreActiveDroneIdentity(
        Entity stationEntity,
        Entity droneEntity,
        ActiveDrone activeDrone,
        DroneBattery battery,
        DroneState state,
        DroneAssignment assignment,
        DroneCargo cargo)
    {
        EntityManager.AddComponentData(droneEntity, activeDrone);
        EntityManager.AddComponentData(droneEntity, battery);
        EntityManager.AddComponentData(droneEntity, state);
        EntityManager.AddComponentData(droneEntity, assignment);
        EntityManager.AddComponentData(droneEntity, cargo);
        EntityManager.AddBuffer<StoredItemElement>(droneEntity);
        DroneStationStorageUtility.TryAddStoredDrone(
            EntityManager,
            stationEntity,
            droneEntity);
    }

    private bool TryFindDropPosition(
        Entity droneEntity,
        int2 originCell,
        out int2 targetCell,
        out float3 targetPosition)
    {
        float z = EntityManager.GetComponentData<LocalTransform>(droneEntity)
            .Position.z;

        for (int radius = 0; radius <= MaximumDropSearchRadius; radius++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if (math.max(math.abs(x), math.abs(y)) != radius)
                        continue;

                    targetCell = originCell + new int2(x, y);
                    targetPosition = new float3(
                        targetCell.x,
                        targetCell.y,
                        z);

                    if (_plannedDropCells.Contains(targetCell))
                        continue;

                    if (!_itemTracking.CanPlaceItemAt(
                            droneEntity,
                            targetCell,
                            targetPosition))
                        continue;

                    _plannedDropCells.Add(targetCell);
                    return true;
                }
            }
        }

        targetCell = default;
        targetPosition = default;
        return false;
    }
}
