using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

/// <summary>Presentation-only layout of a selected research and its reachable descendants.</summary>
public static class ResearchTreeLayout
{
    public static Dictionary<FixedString64Bytes, Vector2> Build(
        IReadOnlyList<FixedString64Bytes> researchIds,
        IReadOnlyList<ResearchPrerequisiteElement> prerequisites,
        FixedString64Bytes rootId)
    {
        var children = new Dictionary<FixedString64Bytes, List<FixedString64Bytes>>();
        foreach (var id in researchIds)
            children.Add(id, new List<FixedString64Bytes>());

        var positions = new Dictionary<FixedString64Bytes, Vector2>();
        if (!children.ContainsKey(rootId))
            return positions;

        foreach (var edge in prerequisites)
        {
            if (children.TryGetValue(edge.prerequisiteId, out var descendants) &&
                children.ContainsKey(edge.researchId))
                descendants.Add(edge.researchId);
        }

        var reachable = new HashSet<FixedString64Bytes> { rootId };
        var pending = new Queue<FixedString64Bytes>();
        pending.Enqueue(rootId);
        while (pending.Count > 0)
        {
            foreach (var child in children[pending.Dequeue()])
            {
                if (reachable.Add(child))
                    pending.Enqueue(child);
            }
        }

        var incoming = new Dictionary<FixedString64Bytes, int>();
        var depths = new Dictionary<FixedString64Bytes, int>();
        foreach (var id in reachable)
        {
            incoming[id] = 0;
            depths[id] = 0;
        }
        foreach (var parent in reachable)
        {
            foreach (var child in children[parent])
                incoming[child]++;
        }
        foreach (var id in researchIds)
        {
            if (reachable.Contains(id) && incoming[id] == 0)
                pending.Enqueue(id);
        }

        int maximumDepth = 0;
        int processed = 0;
        while (pending.Count > 0)
        {
            var parent = pending.Dequeue();
            processed++;
            foreach (var child in children[parent])
            {
                depths[child] = Mathf.Max(depths[child], depths[parent] + 1);
                maximumDepth = Mathf.Max(maximumDepth, depths[child]);
                if (--incoming[child] == 0)
                    pending.Enqueue(child);
            }
        }

        // The config parser rejects cycles; avoid a partial misleading graph if called otherwise.
        if (processed != reachable.Count)
            return positions;

        var widths = new int[maximumDepth + 1];
        foreach (var id in reachable)
            widths[depths[id]]++;
        int maximumWidth = 1;
        foreach (int width in widths)
            maximumWidth = Mathf.Max(maximumWidth, width);

        var slots = new int[widths.Length];
        // JSON definition order is the stable tie-breaker, independent of edge order.
        foreach (var id in researchIds)
        {
            if (!reachable.Contains(id))
                continue;
            int depth = depths[id];
            float column = (maximumWidth - widths[depth]) * 0.5f + slots[depth]++;
            positions.Add(id, new Vector2(column, depth));
        }
        return positions;
    }
}
