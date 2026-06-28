using NUnit.Framework;
using System;
using Unity.Entities;
using System.Collections.Generic;
using UnityEngine;

public class BuildingPrefabDatabaseAuthoring : MonoBehaviour
{
    [Serializable]
    public struct Entry
    {
        public BuildingTypeEnum type;
        public GameObject prefab;
    }

    [SerializeField]
    private List<Entry> _entries;

    private class Baker : Baker<BuildingPrefabDatabaseAuthoring>
    {
        public override void Bake(BuildingPrefabDatabaseAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            DynamicBuffer<BuildingPrefabElement> buffer = AddBuffer<BuildingPrefabElement>(entity);

            foreach(Entry entry in authoring._entries)
            {
                if (entry.prefab == null)
                    continue;

                buffer.Add(new BuildingPrefabElement
                {
                    type = entry.type,
                    prefab = GetEntity(entry.prefab, TransformUsageFlags.Dynamic)
                });
            }
        }
    }
}

