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
    private readonly List<Entity> _items = new();

    public ChunkCell(int2 worldPosition)
    {
        this.worldPosition = worldPosition;
        building = Entity.Null;
        resourceEntity = Entity.Null;
        resourceType = ResourceTypeEnum.None;
        resourceAmount = 0;
        floor = FloorTypeEnum.Bare;
    }

    public int2 worldPosition { get; }
    public Entity building { get; private set; }
    public Entity resourceEntity { get; private set; }
    public ResourceTypeEnum resourceType { get; private set; }
    public int resourceAmount { get; private set; }
    public FloorTypeEnum floor { get; private set; }
    public IReadOnlyList<Entity> items => _items;
    public bool hasBuilding => building != Entity.Null;
    public bool hasResource => resourceType != ResourceTypeEnum.None && resourceAmount > 0;

    public bool TrySetBuilding(Entity building)
    {
        if (hasBuilding)
            return false;

        this.building = building;
        return true;
    }

    public bool TryRemoveBuilding(Entity building)
    {
        if (this.building != building)
            return false;

        this.building = Entity.Null;
        return true;
    }

    public void SetFloor(FloorTypeEnum floor)
    {
        this.floor = floor;
    }

    public bool TrySetResource(ResourceTypeEnum type, int amount, Entity entity)
    {
        if (hasResource || type == ResourceTypeEnum.None || type >= ResourceTypeEnum.Count || amount <= 0)
            return false;

        resourceType = type;
        resourceAmount = amount;
        resourceEntity = entity;
        floor = FloorTypeEnum.PlacedResource;
        return true;
    }

    public bool TryRemoveResource(Entity entity)
    {
        if (resourceEntity != entity)
            return false;

        ClearResource();
        return true;
    }

    public void ClearResource()
    {
        resourceEntity = Entity.Null;
        resourceType = ResourceTypeEnum.None;
        resourceAmount = 0;
        floor = FloorTypeEnum.Bare;
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
}

public class Chunk
{
    private readonly ChunkCell[] _cells = new ChunkCell[ChunkUtility.cellCount];

    public Chunk(int2 chunkPosition)
    {
        this.chunkPosition = chunkPosition;

        for (int y = 0; y < ChunkUtility.chunkSize; y++)
        {
            for (int x = 0; x < ChunkUtility.chunkSize; x++)
            {
                int2 localCell = new int2(x, y);
                int2 worldCell = chunkPosition * ChunkUtility.chunkSize + localCell;
                _cells[ChunkUtility.ToCellIndex(localCell)] = new ChunkCell(worldCell);
            }
        }
    }

    public int2 chunkPosition { get; }
    public bool hasGeneratedResources { get; private set; }
    public IReadOnlyList<ChunkCell> cells => _cells;

    public void MarkResourcesGenerated()
    {
        hasGeneratedResources = true;
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
}
