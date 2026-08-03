using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

public class ItemPrefabDatabaseAuthoring : MonoBehaviour
{
    [Serializable]
    public struct Entry
    {
        public ItemTypeEnum type;
        public GameObject prefab;
    }

    [SerializeField]
    private List<Entry> _entries = new List<Entry>();

    private class Baker : Baker<ItemPrefabDatabaseAuthoring>
    {
        public override void Bake(ItemPrefabDatabaseAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            DynamicBuffer<ItemPrefabElement> buffer = AddBuffer<ItemPrefabElement>(entity);

            foreach (Entry entry in authoring._entries)
            {
                if (entry.prefab == null)
                    continue;

                if (entry.type <= ItemTypeEnum.None || entry.type >= ItemTypeEnum.Count)
                    continue;

                buffer.Add(new ItemPrefabElement
                {
                    type = entry.type,
                    prefab = GetEntity(entry.prefab, TransformUsageFlags.Dynamic)
                });
            }
        }
    }
}
