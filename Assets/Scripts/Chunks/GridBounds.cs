using System.Collections.Generic;
using Unity.Mathematics;

public readonly struct GridBounds
{
    public GridBounds(int2 firstCell, int2 secondCell)
    {
        Min = math.min(firstCell, secondCell);
        Max = math.max(firstCell, secondCell);
    }

    public bool Contains(int2 cell)
    {
        return cell.x >= Min.x &&
               cell.y >= Min.y &&
               cell.x <= Max.x &&
               cell.y <= Max.y;
    }

    public bool Overlaps(GridBounds other)
    {
        return Max.x >= other.Min.x &&
               Max.y >= other.Min.y &&
               Min.x <= other.Max.x &&
               Min.y <= other.Max.y;
    }

    public void GetCells(List<int2> results)
    {
        results.Clear();

        for (int y = Min.y; y <= Max.y; y++)
        {
            for (int x = Min.x; x <= Max.x; x++)
                results.Add(new int2(x, y));
        }
    }

    public int2 Min { get; }
    public int2 Max { get; }
}
