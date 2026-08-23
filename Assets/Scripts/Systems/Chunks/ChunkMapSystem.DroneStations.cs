using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

public partial class ChunkMapSystem
{
    private readonly List<int2> _droneStationRangeCells = new();
    private readonly List<int2> _registeredDroneStationCells = new();

    public bool TryRegisterDroneStationRange(
        Entity stationEntity,
        GridBounds activityBounds)
    {
        if (stationEntity == Entity.Null)
            return false;

        activityBounds.GetCells(_droneStationRangeCells);

        for (int i = 0; i < _droneStationRangeCells.Count; i++)
        {
            int2 cell = _droneStationRangeCells[i];

            if (!TryGetCellData(cell, out ChunkCell cellData))
                continue;

            if (cellData.HasCoveringDroneStation(stationEntity))
                return false;
        }

        _registeredDroneStationCells.Clear();

        for (int i = 0; i < _droneStationRangeCells.Count; i++)
        {
            int2 cell = _droneStationRangeCells[i];
            ChunkCell cellData = GetOrCreateCellData(cell);

            if (cellData.TryAddCoveringDroneStation(stationEntity))
            {
                _registeredDroneStationCells.Add(cell);
                continue;
            }

            RollbackDroneStationRangeRegistration(stationEntity);
            return false;
        }

        return true;
    }

    public bool TryUnregisterDroneStationRange(
        Entity stationEntity,
        GridBounds activityBounds)
    {
        if (stationEntity == Entity.Null)
            return false;

        bool removedAllCells = true;
        activityBounds.GetCells(_droneStationRangeCells);

        for (int i = 0; i < _droneStationRangeCells.Count; i++)
        {
            int2 cell = _droneStationRangeCells[i];

            if (!TryGetCellData(cell, out ChunkCell cellData))
            {
                removedAllCells = false;
                continue;
            }

            if (!cellData.TryRemoveCoveringDroneStation(stationEntity))
                removedAllCells = false;
        }

        return removedAllCells;
    }

    public void GetDroneStationsCoveringCell(
        int2 cell,
        List<Entity> results)
    {
        results.Clear();

        if (!TryGetCellData(cell, out ChunkCell cellData))
            return;

        for (int i = 0; i < cellData.CoveringDroneStations.Count; i++)
            results.Add(cellData.CoveringDroneStations[i]);
    }

    private void RollbackDroneStationRangeRegistration(Entity stationEntity)
    {
        for (int i = 0; i < _registeredDroneStationCells.Count; i++)
        {
            int2 cell = _registeredDroneStationCells[i];

            if (!TryGetCellData(cell, out ChunkCell cellData))
                continue;

            cellData.TryRemoveCoveringDroneStation(stationEntity);
        }

        _registeredDroneStationCells.Clear();
    }
}
