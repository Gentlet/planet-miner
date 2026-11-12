using System;
using System.Collections.Generic;

/// <summary>연구 정의의 DAG를 화면에만 배치한다. 선행 완료나 실제 활성 연구를 변경하지 않는다.</summary>
public sealed class ResearchUiNode
{
    public int Id;
    public string Name;
    public string Description;
    public string Materials;
    public string Rewards;
    public readonly List<int> Parents = new List<int>();
    public double Progress;
    public double RequiredProgress;
    public bool Completed;
    public bool Available;
    public bool Active;
    public int Depth;
    public int Row;
}

public static class ResearchTreeLayoutUtility
{
    public static bool TryArrange(IReadOnlyList<ResearchUiNode> nodes, int rootId, List<ResearchUiNode> visible)
    {
        visible.Clear();
        if (rootId == 0)
        {
            return false;
        }
        var depths = new Dictionary<int, int>();
        for (int pass = 0; pass < nodes.Count; pass++)
        {
            bool advanced = false;
            foreach (var node in nodes)
            {
                if (depths.ContainsKey(node.Id))
                {
                    continue;
                }
                bool ready = true;
                int depth = 0;
                foreach (int parent in node.Parents)
                {
                    if (!depths.TryGetValue(parent, out int parentDepth))
                    {
                        ready = false;
                        break;
                    }
                    depth = Math.Max(depth, parentDepth + 1);
                }
                if (ready)
                {
                    depths.Add(node.Id, depth);
                    advanced = true;
                }
            }
            if (!advanced)
            {
                break;
            }
        }
        if (depths.Count != nodes.Count || !depths.ContainsKey(rootId))
        {
            return false;
        }
        var included = new HashSet<int> { rootId };
        for (int pass = 0; pass < nodes.Count; pass++)
        {
            bool advanced = false;
            foreach (var node in nodes)
            {
                if (included.Contains(node.Id))
                {
                    continue;
                }
                foreach (int parent in node.Parents)
                {
                    if (included.Contains(parent))
                    {
                        included.Add(node.Id);
                        advanced = true;
                        break;
                    }
                }
            }
            if (!advanced)
            {
                break;
            }
        }
        var rows = new Dictionary<int, int>();
        foreach (var node in nodes)
        {
            if (!included.Contains(node.Id))
            {
                continue;
            }
            node.Depth = depths[node.Id] - depths[rootId];
            rows.TryGetValue(node.Depth, out int row);
            node.Row = row;
            rows[node.Depth] = row + 1;
            visible.Add(node);
        }
        return true;
    }

    public static double PlannedTotal(double requiredProgress, double progressPerCycle, int amount)
    {
        return Math.Ceiling(requiredProgress / progressPerCycle) * amount;
    }
}
