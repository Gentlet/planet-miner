using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public partial class ChunkMapSystem
{
    private readonly List<int2> _buildingFootprintCells = new();
    private readonly List<int2> _registeredBuildingCells = new();
    private readonly HashSet<Entity> _buildingQueryDeduplication = new();

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

    public int2 GetBuildingSize(BuildingTypeEnum type)
    {
        if (_buildingPrefabQuery.IsEmptyIgnoreFilter)
            return new int2(1, 1);

        DynamicBuffer<BuildingPrefabElement> definitions =
            _buildingPrefabQuery.GetSingletonBuffer<BuildingPrefabElement>(true);
        return definitions.GetFootprintSize(type);
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

    public bool TryRegisterConstructionSite(
        Entity siteEntity,
        DynamicBuffer<ConstructionSiteReservedCellElement> reservedCells)
    {
        if (siteEntity == Entity.Null)
            return false;

        if (!EntityManager.Exists(siteEntity))
            return false;

        if (reservedCells.Length == 0)
            return false;

        for (int i = 0; i < reservedCells.Length; i++)
        {
            int2 cell = reservedCells[i].cell;

            if (!IsBuildingReserved(cell))
                return false;

            if (_constructionSiteByCell.ContainsKey(cell))
                return false;
        }

        for (int i = 0; i < reservedCells.Length; i++)
            _constructionSiteByCell.Add(reservedCells[i].cell, siteEntity);

        return true;
    }

    public bool TryGetConstructionSite(int2 cell, out Entity siteEntity)
    {
        return _constructionSiteByCell.TryGetValue(cell, out siteEntity);
    }

    public void UnregisterConstructionSite(
        Entity siteEntity,
        DynamicBuffer<ConstructionSiteReservedCellElement> reservedCells,
        bool releaseReservations)
    {
        for (int i = 0; i < reservedCells.Length; i++)
        {
            int2 cell = reservedCells[i].cell;

            if (_constructionSiteByCell.TryGetValue(cell, out Entity owner) &&
                owner == siteEntity)
                _constructionSiteByCell.Remove(cell);

            if (releaseReservations)
                TryUnreserveBuilding(cell);
        }
    }

    public void ReleaseBuildingReservation(
        int2 anchor,
        BuildingTypeEnum type,
        DirectionEnum direction)
    {
        BuildingFootprintUtility.GetOccupiedCells(
            anchor,
            GetBuildingSize(type),
            direction,
            _buildingFootprintCells);
        ReleaseBuildingReservations(_buildingFootprintCells);
    }

    private bool TryRegisterBuilding(EntityCommandBuffer ecb, Entity entity)
    {
        if (!EntityManager.HasComponent<BuildingOccupantRequest>(entity))
            return false;

        if (!EntityManager.HasComponent<GridPosition>(entity) ||
            !EntityManager.HasComponent<BuildingType>(entity) ||
            !EntityManager.HasComponent<BuildingFootprint>(entity) ||
            !EntityManager.HasComponent<Direction>(entity))
        {
            Debug.LogError($"Building registration failed because spatial components are missing. Entity: {entity}");
            ecb.DestroyEntity(entity);
            return false;
        }

        int2 anchor = EntityManager.GetComponentData<GridPosition>(entity).gridPosition;
        BuildingTypeEnum type = EntityManager.GetComponentData<BuildingType>(entity).type;
        int2 footprintSize = EntityManager
            .GetComponentData<BuildingFootprint>(entity)
            .size;
        DirectionEnum direction = EntityManager.GetComponentData<Direction>(entity).dir;
        BuildingFootprintUtility.GetOccupiedCells(
            anchor,
            footprintSize,
            direction,
            _buildingFootprintCells);

        for (int i = 0; i < _buildingFootprintCells.Count; i++)
        {
            int2 cell = _buildingFootprintCells[i];

            if (!IsBuildingOccupied(cell))
                continue;

            Debug.LogError(
                $"Building registration failed because its footprint is occupied. Type: {type}, Anchor: {anchor}, Cell: {cell}");
            ReleaseBuildingReservations(_buildingFootprintCells);
            ecb.DestroyEntity(entity);
            return false;
        }

        _registeredBuildingCells.Clear();

        for (int i = 0; i < _buildingFootprintCells.Count; i++)
        {
            int2 cell = _buildingFootprintCells[i];
            ChunkCell cellData = GetOrCreateCellData(cell);

            if (cellData.TrySetBuilding(entity))
            {
                _registeredBuildingCells.Add(cell);
                continue;
            }

            RollbackBuildingRegistration(entity);
            ReleaseBuildingReservations(_buildingFootprintCells);
            Debug.LogError(
                $"Building registration failed while writing its footprint. Type: {type}, Anchor: {anchor}, Cell: {cell}");
            ecb.DestroyEntity(entity);
            return false;
        }

        if (EntityManager.HasComponent<Belt>(entity) &&
            !TryRegisterBelt(entity, anchor))
        {
            RollbackBuildingRegistration(entity);
            ReleaseBuildingReservations(_buildingFootprintCells);
            ecb.DestroyEntity(entity);
            return false;
        }

        bool missingReservation = false;

        for (int i = 0; i < _buildingFootprintCells.Count; i++)
        {
            if (!TryUnreserveBuilding(_buildingFootprintCells[i]))
                missingReservation = true;
        }

        if (missingReservation)
        {
            Debug.LogWarning(
                $"Building spawned without a complete footprint reservation. Type: {type}, Anchor: {anchor}");
        }

        ecb.RemoveComponent<BuildingOccupantRequest>(entity);
        ecb.AddComponent<BuildingOccupant>(entity);
        return true;
    }

    private bool TryRegisterBelt(Entity entity, int2 anchor)
    {
        EnsureBeltCapacity();

        if (!_beltByCell.TryAdd(anchor, entity))
        {
            Debug.LogError($"Failed BeltIndex.TryAdd. Entity: {entity}, Cell: {anchor}");
            return false;
        }

        Belt belt = EntityManager.GetComponentData<Belt>(entity);
        belt.installationOrder = ++_nextBeltInstallationOrder;
        EntityManager.SetComponentData(entity, belt);

        SortItemsForBelt(anchor);

        if (TryGetCellData(anchor, out ChunkCell cellData) && cellData.Items.Count > 0)
            _activeBeltCells.Add(anchor);

        return true;
    }

    private void RollbackBuildingRegistration(Entity entity)
    {
        for (int i = 0; i < _registeredBuildingCells.Count; i++)
        {
            if (TryGetCellData(_registeredBuildingCells[i], out ChunkCell cellData))
                cellData.TryRemoveBuilding(entity);
        }

        _registeredBuildingCells.Clear();
    }

    private void ReleaseBuildingReservations(List<int2> cells)
    {
        for (int i = 0; i < cells.Count; i++)
            TryUnreserveBuilding(cells[i]);
    }

    public bool TryUnregisterBuilding(Entity entity)
    {
        if (!EntityManager.Exists(entity) ||
            !EntityManager.HasComponent<GridPosition>(entity) ||
            !EntityManager.HasComponent<BuildingType>(entity) ||
            !EntityManager.HasComponent<BuildingFootprint>(entity) ||
            !EntityManager.HasComponent<Direction>(entity))
            return false;

        int2 anchor = EntityManager.GetComponentData<GridPosition>(entity).gridPosition;
        int2 footprintSize = EntityManager
            .GetComponentData<BuildingFootprint>(entity)
            .size;
        DirectionEnum direction = EntityManager.GetComponentData<Direction>(entity).dir;
        BuildingFootprintUtility.GetOccupiedCells(
            anchor,
            footprintSize,
            direction,
            _buildingFootprintCells);

        bool removedAllCells = true;

        for (int i = 0; i < _buildingFootprintCells.Count; i++)
        {
            if (!TryGetCellData(_buildingFootprintCells[i], out ChunkCell cellData) ||
                !cellData.TryRemoveBuilding(entity))
            {
                removedAllCells = false;
            }
        }

        if (_beltByCell.TryGetValue(anchor, out Entity beltEntity) && beltEntity == entity)
        {
            _beltByCell.Remove(anchor);
            _activeBeltCells.Remove(anchor);
        }

        return removedAllCells;
    }

    public void GetBuildingsInBounds(GridBounds bounds, List<Entity> results)
    {
        results.Clear();
        _buildingQueryDeduplication.Clear();

        foreach (Chunk chunk in _chunks.Values)
        {
            int2 chunkMin = chunk.ChunkPosition * GameConstants.chunkSize;
            int2 chunkMax = chunkMin + new int2(GameConstants.chunkSize - 1);
            GridBounds chunkBounds = new(chunkMin, chunkMax);

            if (!chunkBounds.Overlaps(bounds))
                continue;

            foreach (ChunkCell cell in chunk.Cells)
            {
                if (!cell.HasBuilding ||
                    !bounds.Contains(cell.WorldPosition) ||
                    !_buildingQueryDeduplication.Add(cell.BuildingEntity))
                    continue;

                results.Add(cell.BuildingEntity);
            }
        }
    }
}
