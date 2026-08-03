using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

public partial class ChunkMapSystem
{
    public bool IsBuildingOccupied(int2 cell)
    {
        return TryGetCellData(cell, out ChunkCell cellData) && cellData.HasBuilding;
    }

    public bool IsBuildingReserved(int2 cell)
    {
        return _reservedCells.Contains(cell);
    }

    public bool IsBuildingOccupiedOrReserved(int2 cell)
    {
        return IsBuildingOccupied(cell) || IsBuildingReserved(cell);
    }

    public bool TryGetBuilding(int2 cell, out Entity entity)
    {
        if (!TryGetCellData(cell, out ChunkCell cellData) || !cellData.HasBuilding)
        {
            entity = Entity.Null;
            return false;
        }

        entity = cellData.BuildingEntity;
        return true;
    }

    public bool TryReserveBuilding(int2 cell)
    {
        if (IsBuildingOccupiedOrReserved(cell))
            return false;

        EnsureReservedCapacity();
        GetOrCreateCellData(cell);

        return _reservedCells.Add(cell);
    }

    public bool TryUnreserveBuilding(int2 cell)
    {
        if (!IsBuildingReserved(cell))
            return false;

        return _reservedCells.Remove(cell);
    }

    private bool TryRegisterBuilding(EntityCommandBuffer ecb, Entity entity)
    {
        if (!EntityManager.HasComponent<BuildingOccupantRequest>(entity))
            return false;
        if (!EntityManager.HasComponent<GridPosition>(entity))
            return false;

        GridPosition pos = EntityManager.GetComponentData<GridPosition>(entity);

        EnsureReservedCapacity();
        ChunkCell cellData = GetOrCreateCellData(pos.gridPosition);

        if (!TryUnreserveBuilding(pos.gridPosition))
            UnityEngine.Debug.LogWarning($"Not Reserved Building Spawned. Cell : {pos.gridPosition}");

        if (!cellData.TrySetBuilding(entity))
        {
            UnityEngine.Debug.LogError($"Failed ChunkCell.TrySetBuilding. Type : {EntityManager.GetComponentData<BuildingType>(entity).ToString()}, Cell : {pos.gridPosition}");
            ecb.DestroyEntity(entity);
            return false;
        }

        if (EntityManager.HasComponent<Belt>(entity))
        {
            EnsureBeltCapacity();

            if (!_beltByCell.TryAdd(pos.gridPosition, entity))
            {
                cellData.TryRemoveBuilding(entity);
                UnityEngine.Debug.LogError($"Failed BeltIndex.TryAdd. Type : {EntityManager.GetComponentData<BuildingType>(entity).ToString()}, Cell : {pos.gridPosition}");
                ecb.DestroyEntity(entity);
                return false;
            }

            SortItemsForBelt(pos.gridPosition);

            if (cellData.Items.Count > 0)
                _activeBeltCells.Add(pos.gridPosition);
        }

        ecb.RemoveComponent<BuildingOccupantRequest>(entity);
        ecb.AddComponent<BuildingOccupant>(entity);

        return true;
    }

    public bool TryUnregisterBuilding(int2 cell, Entity entity)
    {
        if (!TryGetCellData(cell, out ChunkCell cellData))
            return false;

        if (cellData.BuildingEntity != entity)
            return false;

        cellData.TryRemoveBuilding(entity);

        if (_beltByCell.TryGetValue(cell, out Entity beltEntity) && beltEntity == entity)
        {
            _beltByCell.Remove(cell);
            _activeBeltCells.Remove(cell);
        }

        return true;
    }

    public void GetBuildingsInBounds(GridBounds bounds, List<Entity> results)
    {
        results.Clear();

        foreach (Chunk chunk in _chunks.Values)
        {
            int2 chunkMin = chunk.ChunkPosition * GameConstants.chunkSize;
            int2 chunkMax = chunkMin + new int2(GameConstants.chunkSize - 1);
            GridBounds chunkBounds = new(chunkMin, chunkMax);

            if (!chunkBounds.Overlaps(bounds))
                continue;

            foreach (ChunkCell cell in chunk.Cells)
            {
                if (!cell.HasBuilding || !bounds.Contains(cell.WorldPosition))
                    continue;

                results.Add(cell.BuildingEntity);
            }
        }
    }
}
