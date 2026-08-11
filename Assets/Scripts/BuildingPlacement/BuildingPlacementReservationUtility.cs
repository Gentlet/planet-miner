using System.Collections.Generic;
using Unity.Mathematics;

public static class BuildingPlacementReservationUtility
{
    public static bool TryReserve(
        BuildingPlacementOperation operation,
        ChunkMapSystem chunkMap,
        List<int2> reservedCells,
        List<int2> footprintCells)
    {
        reservedCells.Clear();

        foreach (BuildingPlacementCandidate candidate in operation.Candidates)
        {
            BuildingFootprintUtility.GetOccupiedCells(
                candidate.position,
                candidate.size,
                operation.GetDirection(candidate),
                footprintCells);

            for (int i = 0; i < footprintCells.Count; i++)
            {
                int2 cell = footprintCells[i];

                if (chunkMap.TryReserveBuilding(cell))
                {
                    reservedCells.Add(cell);
                    continue;
                }

                Release(chunkMap, reservedCells);
                return false;
            }
        }

        return true;
    }

    public static void Release(
        ChunkMapSystem chunkMap,
        List<int2> reservedCells)
    {
        for (int i = 0; i < reservedCells.Count; i++)
            chunkMap.TryUnreserveBuilding(reservedCells[i]);

        reservedCells.Clear();
    }
}
