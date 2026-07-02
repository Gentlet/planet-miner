using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

public enum FloorTypeEnum : byte
{
    Bare,
    PlacedResource
}

public static class ChunkUtility
{
    public const int chunkSize = 16;
    public const int cellCount = chunkSize * chunkSize;

    public static int2 ToChunkPosition(int2 cell)
    {
        return new int2(
            FloorDiv(cell.x, chunkSize),
            FloorDiv(cell.y, chunkSize));
    }

    public static int2 ToLocalCell(int2 cell)
    {
        int2 chunkPosition = ToChunkPosition(cell);
        return cell - chunkPosition * chunkSize;
    }

    public static int ToCellIndex(int2 localCell)
    {
        return localCell.y * chunkSize + localCell.x;
    }

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;

        if (remainder != 0 && value < 0)
            quotient--;

        return quotient;
    }
}

public sealed class ChunkCell
{
    private readonly List<Entity> _items = new();

    public ChunkCell(int2 worldPosition)
    {
        this.worldPosition = worldPosition;
        building = Entity.Null;
        floor = FloorTypeEnum.Bare;
    }

    public int2 worldPosition { get; }
    public Entity building { get; private set; }
    public FloorTypeEnum floor { get; private set; }
    public IReadOnlyList<Entity> items => _items;
    public bool hasBuilding => building != Entity.Null;

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
    public IReadOnlyList<ChunkCell> cells => _cells;

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
