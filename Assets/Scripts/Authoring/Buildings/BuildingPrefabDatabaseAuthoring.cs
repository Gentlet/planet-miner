using System;
using Unity.Entities;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class BuildingPrefabDatabaseAuthoring : MonoBehaviour
{
    [Serializable]
    public struct Entry
    {
        public BuildingTypeEnum type;
        public GameObject prefab;
        public Vector2Int size;
    }

    [SerializeField]
    private List<Entry> _entries;

    private class Baker : Baker<BuildingPrefabDatabaseAuthoring>
    {
        public override void Bake(BuildingPrefabDatabaseAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            DynamicBuffer<BuildingPrefabElement> buffer = AddBuffer<BuildingPrefabElement>(entity);

            foreach (Entry entry in authoring._entries)
            {
                if (entry.prefab == null)
                    continue;

                int2 size = BuildingFootprintUtility.NormalizeSize(
                    new int2(entry.size.x, entry.size.y));

                buffer.Add(new BuildingPrefabElement
                {
                    type = entry.type,
                    prefab = GetEntity(entry.prefab, TransformUsageFlags.Dynamic),
                    size = size
                });
            }
        }
    }
}

