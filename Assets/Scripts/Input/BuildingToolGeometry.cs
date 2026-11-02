using System.Collections.Generic;
using Unity.Mathematics;

public struct BuildingBlueprintElement
{
    public int2 Offset;
    public PlacementRequestCandidateElement Candidate;
}

/// <summary>UI가 소유한 배치 정보만 계산한다. 점유/실물/예약을 변경하지 않는다.</summary>
public static class BuildingToolGeometry
{
    public static bool Overlaps(int2 origin, int2 baseSize, DirectionEnum direction, int2 first, int2 last)
    {
        int2 min = math.min(first, last);
        int2 max = math.max(first, last);
        int2 size = BuildingFootprintUtility.GetEffectiveSize(baseSize, direction);
        return origin.x <= (long)max.x && origin.y <= (long)max.y &&
            (long)origin.x + size.x - 1 >= min.x && (long)origin.y + size.y - 1 >= min.y;
    }

    public static bool TryLine(PlacementRequestCandidateElement template, int2 first, int2 last,
        int maximumCandidates, List<PlacementRequestCandidateElement> result)
    {
        result.Clear();
        int2 size = BuildingFootprintUtility.GetEffectiveSize(template.FootprintSize, template.Direction);
        if (size.x <= 0 || size.y <= 0)
        {
            return false;
        }
        long dx = (long)last.x - first.x;
        long dy = (long)last.y - first.y;
        bool horizontal = System.Math.Abs(dx) >= System.Math.Abs(dy);
        long distance = horizontal ? System.Math.Abs(dx) : System.Math.Abs(dy);
        int step = horizontal ? size.x : size.y;
        long count = distance / step + 1;
        if (count > maximumCandidates)
        {
            return false;
        }
        int sign = (horizontal ? dx : dy) < 0 ? -1 : 1;
        for (int i = 0; i < count; i++)
        {
            var candidate = template;
            long x = first.x + (horizontal ? (long)i * step * sign : 0);
            long y = first.y + (horizontal ? 0 : (long)i * step * sign);
            if (!TryPosition(x, y, out candidate.OriginPosition))
            {
                result.Clear();
                return false;
            }
            result.Add(candidate);
        }
        return true;
    }

    public static bool TryBlueprint(IReadOnlyList<BuildingBlueprintElement> blueprint, int2 anchor,
        int maximumCandidates, List<PlacementRequestCandidateElement> result)
    {
        result.Clear();
        if (blueprint.Count > maximumCandidates)
        {
            return false;
        }
        foreach (var entry in blueprint)
        {
            var candidate = entry.Candidate;
            if (!TryPosition((long)anchor.x + entry.Offset.x, (long)anchor.y + entry.Offset.y, out candidate.OriginPosition))
            {
                result.Clear();
                return false;
            }
            result.Add(candidate);
        }
        return true;
    }

    public static bool TryRotate(List<BuildingBlueprintElement> blueprint)
    {
        var rotated = new List<BuildingBlueprintElement>(blueprint.Count);
        foreach (var entry in blueprint)
        {
            var value = entry;
            int2 size = BuildingFootprintUtility.GetEffectiveSize(value.Candidate.FootprintSize, value.Candidate.Direction);
            if (!TryPosition(value.Offset.y, -(long)value.Offset.x - size.x + 1, out value.Offset))
            {
                return false;
            }
            value.Candidate.Direction = (DirectionEnum)(((int)value.Candidate.Direction + 1) % (int)DirectionEnum.Count);
            rotated.Add(value);
        }
        blueprint.Clear();
        blueprint.AddRange(rotated);
        return true;
    }

    public static bool TryPosition(long x, long y, out int2 position)
    {
        position = default;
        if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue)
        {
            return false;
        }
        position = new int2((int)x, (int)y);
        return true;
    }
}
