using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// 프리팹 데이터베이스 로드 모드.
/// </summary>
public enum ItemPrefabLoadMode
{
    InspectorList,
    ResourcesAutoLoad
}

/// <summary>
/// 역할·목적: Inspector 목록 또는 Resources/Prefabs/Item에서 품목→아이템 프리팹 DB를 베이킹한다.
/// 부착 대상: SubScene의 DB Authoring GameObject. Baker는 별도 DB 엔티티에 ItemPrefabDatabase/ItemPrefabElement를 게시한다.
/// 이용: PrefabDatabaseInitializationSystem이 원형의 ItemIdentity/필수 구성을 검증하고 생성·생산·환급 경계가 참조한다.
/// 수명·경계: DB 참조는 World 동안 불변으로 사용하는 계약이다. 실제 실물의 Owner·위치·요청은 ItemLifecycleUtility가 초기화한다.
/// </summary>
[DisallowMultipleComponent]
public class ItemPrefabDatabaseAuthoring : MonoBehaviour
{
    [Tooltip("프리팹 로드 방식 설정 (인스펙터 직접 지정 vs Resources 폴더 자동 탐색)")]
    public ItemPrefabLoadMode LoadMode = ItemPrefabLoadMode.InspectorList;

    /// <summary>Inspector에서 품목 enum과 원형 GameObject를 연결하는 베이킹 입력 값. 월드 실물 참조가 아니다.</summary>
    [System.Serializable]
    public struct ItemPrefabEntry
    {
        public ItemTypeEnum Type;
        public GameObject Prefab;

        public ItemPrefabEntry(ItemTypeEnum type, GameObject prefab)
        {
            Type = type;
            Prefab = prefab;
        }
    }

    [Tooltip("인스펙터 목록 등록 방식 사용 시 아이템 프리팹 리스트")]
    public List<ItemPrefabEntry> Prefabs = new List<ItemPrefabEntry>();

    /// <summary>
    /// Resources/Prefabs/Item 폴더에서 프리팹을 탐색하여 목록을 자동으로 채웁니다.
    /// </summary>
    [ContextMenu("Populate From Resources")]
    public void PopulateFromResources()
    {
        Prefabs.Clear();

        var loadedPrefabs = Resources.LoadAll<GameObject>("Prefabs/Item");
        if (loadedPrefabs == null || loadedPrefabs.Length == 0)
        {
            return;
        }

        foreach (var prefab in loadedPrefabs)
        {
            string prefabName = prefab.name.Replace(" Item", "").Trim();
            // Enum 이름과 일치 여부 확인 (언더스코어 및 공백 처리)
            string normalizedName = prefabName.Replace(" ", "_");

            if (Enum.TryParse<ItemTypeEnum>(normalizedName, true, out var itemType))
            {
                if (itemType != ItemTypeEnum.None)
                {
                    Prefabs.Add(new ItemPrefabEntry(itemType, prefab));
                }
            }
        }
    }

    /// <summary>선택된 로드 모드의 원형을 Dynamic 엔티티 참조로 등록한다. 이름 추정/목록 입력과 런타임 DB 검증을 구분한다.</summary>
    public class ItemPrefabDatabaseBaker : Baker<ItemPrefabDatabaseAuthoring>
    {
        public override void Bake(ItemPrefabDatabaseAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent<ItemPrefabDatabase>(entity);
            var buffer = AddBuffer<ItemPrefabElement>(entity);

            if (authoring.LoadMode == ItemPrefabLoadMode.ResourcesAutoLoad)
            {
                var loadedPrefabs = Resources.LoadAll<GameObject>("Prefabs/Item");
                if (loadedPrefabs != null)
                {
                    foreach (var prefab in loadedPrefabs)
                    {
                        string prefabName = prefab.name.Replace(" Item", "").Trim().Replace(" ", "_");
                        if (Enum.TryParse<ItemTypeEnum>(prefabName, true, out var itemType) && itemType != ItemTypeEnum.None)
                        {
                            Entity prefabEntity = GetEntity(prefab, TransformUsageFlags.Dynamic);
                            buffer.Add(new ItemPrefabElement(itemType, prefabEntity));
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
                        Entity prefabEntity = GetEntity(entry.Prefab, TransformUsageFlags.Dynamic);
                        buffer.Add(new ItemPrefabElement(entry.Type, prefabEntity));
                    }
                }
            }
        }
    }
}
