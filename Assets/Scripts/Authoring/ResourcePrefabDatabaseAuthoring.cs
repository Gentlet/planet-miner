using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// 프리팹 데이터베이스 로드 모드 (인스펙터 목록 직접 지정 vs Resources 폴더 자동 탐색).
/// </summary>
public enum ResourcePrefabLoadMode
{
    InspectorList,
    ResourcesAutoLoad
}

/// <summary>
/// 자원 노드 프리팹 데이터베이스 서브씬 Authoring 컴포넌트.
/// </summary>
[DisallowMultipleComponent]
public class ResourcePrefabDatabaseAuthoring : MonoBehaviour
{
    [Tooltip("프리팹 로드 방식 설정 (인스펙터 직접 지정 vs Resources 폴더 자동 탐색)")]
    public ResourcePrefabLoadMode LoadMode = ResourcePrefabLoadMode.InspectorList;

    [System.Serializable]
    public struct ResourcePrefabEntry
    {
        public ItemTypeEnum Type;
        public GameObject Prefab;

        public ResourcePrefabEntry(ItemTypeEnum type, GameObject prefab)
        {
            Type = type;
            Prefab = prefab;
        }
    }

    [Tooltip("인스펙터 목록 등록 방식 사용 시 자원 노드 프리팹 리스트")]
    public List<ResourcePrefabEntry> Prefabs = new List<ResourcePrefabEntry>();

    /// <summary>
    /// Resources/Prefabs/Resource 폴더에서 프리팹을 탐색하여 목록을 자동으로 채웁니다.
    /// </summary>
    [ContextMenu("Populate From Resources")]
    public void PopulateFromResources()
    {
        Prefabs.Clear();

        var loadedPrefabs = Resources.LoadAll<GameObject>("Prefabs/Resource");
        if (loadedPrefabs == null || loadedPrefabs.Length == 0)
        {
            return;
        }

        foreach (var prefab in loadedPrefabs)
        {
            string prefabName = prefab.name.Trim().Replace(" ", "_");

            if (Enum.TryParse<ItemTypeEnum>(prefabName, true, out var itemType))
            {
                if (itemType != ItemTypeEnum.None)
                {
                    Prefabs.Add(new ResourcePrefabEntry(itemType, prefab));
                }
            }
        }
    }

    public class ResourcePrefabDatabaseBaker : Baker<ResourcePrefabDatabaseAuthoring>
    {
        public override void Bake(ResourcePrefabDatabaseAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent<ResourcePrefabDatabase>(entity);
            var buffer = AddBuffer<ResourcePrefabElement>(entity);

            if (authoring.LoadMode == ResourcePrefabLoadMode.ResourcesAutoLoad)
            {
                var loadedPrefabs = Resources.LoadAll<GameObject>("Prefabs/Resource");
                if (loadedPrefabs != null)
                {
                    foreach (var prefab in loadedPrefabs)
                    {
                        string prefabName = prefab.name.Trim().Replace(" ", "_");
                        if (Enum.TryParse<ItemTypeEnum>(prefabName, true, out var itemType) && itemType != ItemTypeEnum.None)
                        {
                            Entity prefabEntity = GetEntity(prefab, TransformUsageFlags.Renderable);
                            buffer.Add(new ResourcePrefabElement(itemType, prefabEntity));
                        }
                    }
                }
            }
            else
            {
                for (int i = 0; i < authoring.Prefabs.Count; i++)
                {
                    var entry = authoring.Prefabs[i];
                    if (entry.Prefab != null && entry.Type != ItemTypeEnum.None)
                    {
                        Entity prefabEntity = GetEntity(entry.Prefab, TransformUsageFlags.Renderable);
                        buffer.Add(new ResourcePrefabElement(entry.Type, prefabEntity));
                    }
                }
            }
        }
    }
}
