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
    private readonly int2 _worldPosition;
    private readonly List<Entity> _items = new();
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
        if (hasBuilding)
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
        if (hasResource || type == ResourceTypeEnum.None || type >= ResourceTypeEnum.Count)
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

    public void AddItem(Entity item)
    {
        if (_items.Contains(item))
            return;

        _items.Add(item);
    }

    public bool RemoveItem(Entity item)
    {
        return _items.Remove(item);
    }

    public void ClearItems()
    {
        _items.Clear();
    }

    #region Properties
    public int2 worldPosition { get => _worldPosition; }
    public Entity buildingEntity { get => _buildingEntity; private set => _buildingEntity = value; }
    public Entity resourceEntity { get => _resourceEntity; private set => _resourceEntity = value; }
    public ResourceTypeEnum resourceType { get => _resourceType; private set => _resourceType = value; }
    public FloorTypeEnum floor { get => _floor; private set => _floor = value; }
    public IReadOnlyList<Entity> items { get => _items; }
    public bool hasBuilding { get => _buildingEntity != Entity.Null; }
    public bool hasResource { get => _resourceEntity != Entity.Null && _resourceType != ResourceTypeEnum.None; }
    #endregion
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

    #region Properties
    public int2 chunkPosition { get => _chunkPosition; }
    public bool hasGeneratedResources { get => _hasGeneratedResources; private set => _hasGeneratedResources = value; }
    public IReadOnlyList<ChunkCell> cells { get => _cells; }
    #endregion
}
