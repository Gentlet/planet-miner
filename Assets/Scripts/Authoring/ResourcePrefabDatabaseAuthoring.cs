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
/// 역할·목적: Inspector 목록 또는 Resources/Prefabs/Resource에서 자원 품목→노드 프리팹 DB를 베이킹한다.
/// 부착 대상: SubScene의 DB Authoring GameObject. Baker는 별도 DB 엔티티에 ResourcePrefabDatabase/ResourcePrefabElement를 게시한다.
/// 이용: PrefabDatabaseInitializationSystem의 필수 원형 검사와 ResourceGenerationCommandSystem의 청크 자원 생성이 참조한다.
/// 수명·경계: 원형 참조만 제공하며 노드 품목/잔량/위치는 ResourceGenerationUtility가 생성 시 초기화한다. DB는 World 동안 불변으로 사용하는 계약이다.
/// </summary>
[DisallowMultipleComponent]
public class ResourcePrefabDatabaseAuthoring : MonoBehaviour
{
    [Tooltip("프리팹 로드 방식 설정 (인스펙터 직접 지정 vs Resources 폴더 자동 탐색)")]
    public ResourcePrefabLoadMode LoadMode = ResourcePrefabLoadMode.InspectorList;

    /// <summary>Inspector에서 자원 품목과 노드 원형을 연결하는 베이킹 입력 값. 생성된 자원 엔티티나 잔량 상태가 아니다.</summary>
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

    /// <summary>선택된 로드 모드의 원형을 Renderable 엔티티 참조로 등록한다. 실제 노드 생성과 필수 DB 검증은 런타임 경계가 담당한다.</summary>
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
