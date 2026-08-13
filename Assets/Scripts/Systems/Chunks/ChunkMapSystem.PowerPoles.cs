using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

public partial class ChunkMapSystem
{
    private readonly List<int2> _registeredPowerPoleCells = new();
    private readonly List<int2> _powerPoleSupplyCells = new();

    public bool TryRegisterPowerPoleSupply(
        Entity powerPoleEntity,
        GridBounds supplyBounds)
    {
        supplyBounds.GetCells(_powerPoleSupplyCells);

        for (int i = 0; i < _powerPoleSupplyCells.Count; i++)
        {
            int2 cell = _powerPoleSupplyCells[i];

            if (!TryGetCellData(cell, out ChunkCell cellData))
                continue;

            if (cellData.HasCoveringPowerPole(powerPoleEntity))
                return false;
        }

        _registeredPowerPoleCells.Clear();

        for (int i = 0; i < _powerPoleSupplyCells.Count; i++)
        {
            int2 cell = _powerPoleSupplyCells[i];
            ChunkCell cellData = GetOrCreateCellData(cell);

            if (cellData.TryAddCoveringPowerPole(powerPoleEntity))
            {
                _registeredPowerPoleCells.Add(cell);
                continue;
            }

            RollbackPowerPoleSupplyRegistration(powerPoleEntity);
            return false;
        }

        return true;
    }

    public bool TryUnregisterPowerPoleSupply(
        Entity powerPoleEntity,
        GridBounds supplyBounds)
    {
        bool removedAllCells = true;

        supplyBounds.GetCells(_powerPoleSupplyCells);

        for (int i = 0; i < _powerPoleSupplyCells.Count; i++)
        {
            int2 cell = _powerPoleSupplyCells[i];

            if (!TryGetCellData(cell, out ChunkCell cellData))
            {
                removedAllCells = false;
                continue;
            }

            if (!cellData.TryRemoveCoveringPowerPole(powerPoleEntity))
                removedAllCells = false;
        }

        return removedAllCells;
    }

    public void GetPowerPolesCoveringCell(int2 cell, List<Entity> results)
    {
        results.Clear();

        if (!TryGetCellData(cell, out ChunkCell cellData))
            return;

        for (int i = 0; i < cellData.CoveringPowerPoles.Count; i++)
            results.Add(cellData.CoveringPowerPoles[i]);
    }

    private void RollbackPowerPoleSupplyRegistration(Entity powerPoleEntity)
    {
        for (int i = 0; i < _registeredPowerPoleCells.Count; i++)
        {
            int2 cell = _registeredPowerPoleCells[i];

            if (!TryGetCellData(cell, out ChunkCell cellData))
                continue;

            cellData.TryRemoveCoveringPowerPole(powerPoleEntity);
        }

        _registeredPowerPoleCells.Clear();
    }
}
