using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public partial class DroneStationNetworkSystem
{
    private void SynchronizeStationRegistrations()
    {
        CollectCurrentStations();
        UnregisterMissingStations();

        for (int i = 0; i < _currentStations.Count; i++)
            SynchronizeStationRegistration(_currentStations[i]);
    }

    private void CollectCurrentStations()
    {
        _currentStations.Clear();
        _currentStationSet.Clear();

        using NativeArray<Entity> stationEntities =
            _stationQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < stationEntities.Length; i++)
        {
            Entity stationEntity = stationEntities[i];
            _currentStations.Add(stationEntity);
            _currentStationSet.Add(stationEntity);
        }

        _currentStations.Sort(CompareEntities);
    }

    private void UnregisterMissingStations()
    {
        _staleStations.Clear();

        foreach (Entity stationEntity in _registrations.Keys)
        {
            if (!_currentStationSet.Contains(stationEntity))
                _staleStations.Add(stationEntity);
        }

        for (int i = 0; i < _staleStations.Count; i++)
        {
            Entity stationEntity = _staleStations[i];
            StationRegistration registration = _registrations[stationEntity];
            _chunkMap.TryUnregisterDroneStationRange(
                stationEntity,
                registration.ActivityBounds);
            _registrations.Remove(stationEntity);

            if (!EntityManager.Exists(stationEntity))
                continue;

            if (EntityManager.HasComponent<DroneStationNetwork>(stationEntity))
            {
                EntityManager.RemoveComponent<DroneStationNetwork>(
                    stationEntity);
            }
        }
    }

    private void SynchronizeStationRegistration(Entity stationEntity)
    {
        GridPosition gridPosition = EntityManager
            .GetComponentData<GridPosition>(stationEntity);
        DroneStation station = EntityManager
            .GetComponentData<DroneStation>(stationEntity);
        GridBounds activityBounds = DroneStationRangeUtility
            .GetActivityBounds(
                gridPosition.gridPosition,
                station.activityRangeInChunks);

        bool hasCurrentRegistration = _registrations.TryGetValue(
                stationEntity,
                out StationRegistration currentRegistration);

        if (hasCurrentRegistration)
        {
            if (BoundsEqual(
                    currentRegistration.ActivityBounds,
                    activityBounds))
                return;

            _chunkMap.TryUnregisterDroneStationRange(
                stationEntity,
                currentRegistration.ActivityBounds);
        }

        if (_chunkMap.TryRegisterDroneStationRange(
                stationEntity,
                activityBounds))
        {
            _registrations[stationEntity] =
                new StationRegistration(activityBounds);
            return;
        }

        if (hasCurrentRegistration)
        {
            bool restored = _chunkMap.TryRegisterDroneStationRange(
                stationEntity,
                currentRegistration.ActivityBounds);

            if (!restored)
                _registrations.Remove(stationEntity);
        }

        Debug.LogError(
            $"Failed to register drone station activity range. Station : {stationEntity}, " +
            $"Min : {activityBounds.Min}, Max : {activityBounds.Max}");

        if (EntityManager.HasComponent<DroneStationNetwork>(stationEntity))
            EntityManager.RemoveComponent<DroneStationNetwork>(stationEntity);
    }

    private static bool BoundsEqual(GridBounds left, GridBounds right)
    {
        return math.all(left.Min == right.Min) &&
               math.all(left.Max == right.Max);
    }

    private static int CompareEntities(Entity left, Entity right)
    {
        int indexComparison = left.Index.CompareTo(right.Index);

        if (indexComparison != 0)
            return indexComparison;

        return left.Version.CompareTo(right.Version);
    }
}
