using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

public class ResourcePrefabDatabaseAuthoring : MonoBehaviour
{
    [Serializable]
    public struct Entry
    {
        public ResourceTypeEnum type;
        public GameObject prefab;
    }

    [SerializeField]
    private List<Entry> _entries = new List<Entry>();

    private class Baker : Baker<ResourcePrefabDatabaseAuthoring>
    {
        public override void Bake(ResourcePrefabDatabaseAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            DynamicBuffer<ResourcePrefabElement> buffer = AddBuffer<ResourcePrefabElement>(entity);

            foreach (Entry entry in authoring._entries)
            {
                if (entry.type <= ResourceTypeEnum.None || entry.type >= ResourceTypeEnum.Count || entry.prefab == null)
                    continue;

                buffer.Add(new ResourcePrefabElement
                {
                    type = entry.type,
                    prefab = GetEntity(entry.prefab, TransformUsageFlags.Dynamic)
                });
            }
        }
    }
}
