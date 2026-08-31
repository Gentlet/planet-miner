using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

public enum FloorTypeEnum : byte
{
    Bare,
    PlacedResource,
    Count
}

public class ChunkCell
{
    private static readonly IReadOnlyList<Entity> emptyEntities =
        Array.Empty<Entity>();

    private readonly int2 _worldPosition;
    private List<Entity> _items;
    private List<Entity> _coveringPowerPoles;
    private List<Entity> _coveringDroneStations;
    private Entity _buildingEntity;
    private Entity _resourceEntity;
    private ResourceTypeEnum _resourceType;
    private FloorTypeEnum _floor;

    public ChunkCell(int2 worldPosition)
    {
        _worldPosition = worldPosition;
        _buildingEntity = Entity.Null;
        _resourceEntity = Entity.Null;
        _resourceType = ResourceTypeEnum.None;
        _floor = FloorTypeEnum.Bare;
    }

    public bool TrySetBuilding(Entity buildingEntity)
    {
        if (HasBuilding)
            return false;

        _buildingEntity = buildingEntity;
        return true;
    }

    public bool TryRemoveBuilding(Entity buildingEntity)
    {
        if (_buildingEntity != buildingEntity)
            return false;

        _buildingEntity = Entity.Null;
        return true;
    }

    public void SetFloor(FloorTypeEnum floor)
    {
        _floor = floor;
    }

    public bool TrySetResource(ResourceTypeEnum type, Entity entity)
    {
        if (HasResource || type == ResourceTypeEnum.None || type >= ResourceTypeEnum.Count)
            return false;

        _resourceType = type;
        _resourceEntity = entity;
        _floor = FloorTypeEnum.PlacedResource;
        return true;
    }

    public bool TryRemoveResource(Entity entity)
    {
        if (_resourceEntity != entity)
            return false;

        ClearResource();
        return true;
    }

    public void ClearResource()
    {
        _resourceEntity = Entity.Null;
        _resourceType = ResourceTypeEnum.None;
        _floor = FloorTypeEnum.Bare;
    }

    public bool TryAddItem(Entity item)
    {
        if (_items != null && _items.Contains(item))
            return false;

        _items ??= new List<Entity>();
        _items.Add(item);
        return true;
    }

    public bool RemoveItem(Entity item)
    {
        return _items != null && _items.Remove(item);
    }

    public void RemoveItemAt(int index)
    {
        if (_items == null)
            throw new ArgumentOutOfRangeException(nameof(index));

        _items.RemoveAt(index);
    }

    public void SwapItems(int firstIndex, int secondIndex)
    {
        if (_items == null)
            throw new ArgumentOutOfRangeException(nameof(firstIndex));

        (_items[firstIndex], _items[secondIndex]) = (_items[secondIndex], _items[firstIndex]);
    }

    public void ClearItems()
    {
        if (_items == null)
            return;

        _items.Clear();
    }

    public bool TryAddCoveringPowerPole(Entity powerPoleEntity)
    {
        if (_coveringPowerPoles != null &&
            _coveringPowerPoles.Contains(powerPoleEntity))
            return false;

        _coveringPowerPoles ??= new List<Entity>();
        _coveringPowerPoles.Add(powerPoleEntity);
        return true;
    }

    public bool TryRemoveCoveringPowerPole(Entity powerPoleEntity)
    {
        return _coveringPowerPoles != null &&
            _coveringPowerPoles.Remove(powerPoleEntity);
    }

    public bool HasCoveringPowerPole(Entity powerPoleEntity)
    {
        return _coveringPowerPoles != null &&
            _coveringPowerPoles.Contains(powerPoleEntity);
    }

    public bool TryAddCoveringDroneStation(Entity stationEntity)
    {
        if (_coveringDroneStations != null &&
            _coveringDroneStations.Contains(stationEntity))
            return false;

        _coveringDroneStations ??= new List<Entity>();
        _coveringDroneStations.Add(stationEntity);
        return true;
    }

    public bool TryRemoveCoveringDroneStation(Entity stationEntity)
    {
        return _coveringDroneStations != null &&
            _coveringDroneStations.Remove(stationEntity);
    }

    public bool HasCoveringDroneStation(Entity stationEntity)
    {
        return _coveringDroneStations != null &&
            _coveringDroneStations.Contains(stationEntity);
    }

    public int2 WorldPosition => _worldPosition;
    public Entity BuildingEntity => _buildingEntity;
    public Entity ResourceEntity => _resourceEntity;
    public ResourceTypeEnum ResourceType => _resourceType;
    public FloorTypeEnum Floor => _floor;
    public IReadOnlyList<Entity> Items => _items ?? emptyEntities;
    public IReadOnlyList<Entity> CoveringPowerPoles =>
        _coveringPowerPoles ?? emptyEntities;
    public IReadOnlyList<Entity> CoveringDroneStations =>
        _coveringDroneStations ?? emptyEntities;
    public bool HasBuilding => _buildingEntity != Entity.Null;
    public bool HasResource =>
        _resourceEntity != Entity.Null && _resourceType != ResourceTypeEnum.None;
}

public class Chunk
{
    private readonly int2 _chunkPosition;
    private readonly ChunkCell[] _cells = new ChunkCell[ChunkUtility.cellCount];
    private bool _hasGeneratedResources;

    public Chunk(int2 chunkPosition)
    {
        _chunkPosition = chunkPosition;

        for (int y = 0; y < GameConstants.chunkSize; y++)
        {
            for (int x = 0; x < GameConstants.chunkSize; x++)
            {
                int2 localCell = new int2(x, y);
                int2 worldCell = chunkPosition * GameConstants.chunkSize + localCell;
                _cells[ChunkUtility.ToCellIndex(localCell)] = new ChunkCell(worldCell);
            }
        }
    }

    public void MarkResourcesGenerated()
    {
        _hasGeneratedResources = true;
    }

    public ChunkCell GetCellByLocalPosition(int2 localCell)
    {
        return _cells[ChunkUtility.ToCellIndex(localCell)];
    }

    public ChunkCell GetCellByWorldPosition(int2 worldCell)
    {
        return GetCellByLocalPosition(ChunkUtility.ToLocalCell(worldCell));
    }

    public void ClearItems()
    {
        for (int i = 0; i < _cells.Length; i++)
            _cells[i].ClearItems();
    }

    public int2 ChunkPosition => _chunkPosition;
    public bool HasGeneratedResources => _hasGeneratedResources;
    public IReadOnlyList<ChunkCell> Cells => _cells;
}
