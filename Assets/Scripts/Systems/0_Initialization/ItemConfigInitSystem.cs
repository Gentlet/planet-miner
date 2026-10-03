using System;
using System.Collections.Generic;
using System.IO;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// JSON 설정 직렬화를 위한 데이터 전송 객체 (DTO).
/// </summary>
[Serializable]
public class ItemConfigJsonData
{
    public int DefaultMaxStack = 50;
    public List<ItemConfigJsonEntry> Items = new List<ItemConfigJsonEntry>();
}

[Serializable]
public class ItemConfigJsonEntry
{
    public string ItemType;
    public int MaxStack;
}

/// <summary>
/// 시작 시 ItemRegistry와 ItemConfigElement 버퍼를 한 번 게시한다.
/// 게시 이후 설정은 읽기 전용이며, 버퍼의 수명은 초기화 시스템이 아닌 World에 속한다.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class ItemConfigInitSystem : SystemBase
{
    protected override void OnUpdate()
    {
        using var query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ItemRegistry>());
        if (!query.IsEmptyIgnoreFilter)
        {
            Entity registryEntity = query.GetSingletonEntity();
            if (!EntityManager.HasBuffer<ItemConfigElement>(registryEntity))
            {
                throw new InvalidOperationException("The pre-registered ItemRegistry requires an ItemConfigElement buffer.");
            }

            Enabled = false;
            return;
        }

        InitializeItemRegistry(EntityManager);
        Enabled = false;
    }

    /// <summary>
    /// 읽는 시스템을 실행하기 전, World당 설정을 한 번 게시한다. 사전 등록도 이 API를 사용한다.
    /// 기존 Registry가 있으면 입력을 읽거나 변경하기 전에 거부한다. 실행 중 교체/삭제는 지원하지 않는다.
    /// 반환 엔티티와 버퍼는 World가 소유하므로 호출자와 Init 시스템은 따로 해제하지 않는다.
    /// </summary>
    public static Entity InitializeItemRegistry(EntityManager entityManager, string jsonOverride = null)
    {
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<ItemRegistry>());
        if (!query.IsEmptyIgnoreFilter)
        {
            throw new InvalidOperationException("ItemRegistry is already registered. Runtime replacement is not supported.");
        }

        string jsonText = jsonOverride;
        if (string.IsNullOrEmpty(jsonText))
        {
            jsonText = TryReadConfigFile();
        }

        var items = ParseItems(jsonText, out int defaultMaxStack);
        Entity registryEntity = entityManager.CreateEntity(typeof(ItemRegistry), typeof(ItemConfigElement));
        try
        {
            entityManager.SetComponentData(registryEntity, new ItemRegistry { DefaultMaxStack = defaultMaxStack });
            var buffer = entityManager.GetBuffer<ItemConfigElement>(registryEntity);
            buffer.EnsureCapacity(items.Count);
            foreach (var item in items)
            {
                buffer.Add(item);
            }

            return registryEntity;
        }
        catch
        {
            // 이번 호출이 생성한 미완성 엔티티만 회수한다. 사전 등록 설정은 건드리지 않는다.
            entityManager.DestroyEntity(registryEntity);
            throw;
        }
    }

    private static List<ItemConfigElement> ParseItems(string jsonText, out int defaultMaxStack)
    {
        defaultMaxStack = 50;
        var customStacks = new Dictionary<ItemTypeEnum, int>();
        if (!string.IsNullOrEmpty(jsonText))
        {
            try
            {
                var data = JsonUtility.FromJson<ItemConfigJsonData>(jsonText);
                if (data != null)
                {
                    defaultMaxStack = data.DefaultMaxStack > 0 ? data.DefaultMaxStack : 50;
                    if (data.Items != null)
                    {
                        foreach (var item in data.Items)
                        {
                            if (Enum.TryParse<ItemTypeEnum>(item.ItemType, true, out var parsedType))
                            {
                                customStacks[parsedType] = item.MaxStack;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ItemConfigInitSystem] Failed to parse ItemConfig.json, falling back to defaults. Error: {ex.Message}");
            }
        }

        int count = Enum.GetValues(typeof(ItemTypeEnum)).Length;
        var items = new List<ItemConfigElement>(count);
        for (int i = 0; i < count; i++)
        {
            var itemType = (ItemTypeEnum)i;
            int maxStack = customStacks.TryGetValue(itemType, out int customValue)
                ? customValue
                : GetDefaultMaxStackFor(itemType, defaultMaxStack);
            items.Add(new ItemConfigElement(itemType, maxStack));
        }

        return items;
    }

    private static string TryReadConfigFile()
    {
        try
        {
            string path = Path.Combine(Application.streamingAssetsPath, "ItemConfig.json");
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }

            string altPath = Path.Combine(Application.dataPath, "StreamingAssets", "ItemConfig.json");
            if (File.Exists(altPath))
            {
                return File.ReadAllText(altPath);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ItemConfigInitSystem] Exception while reading config file: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// 파일이 없거나 정의되지 않은 아이템에 대한 기본 MaxStack을 반환.
    /// fallback으로 전달된 defaultMaxStack(ItemConfig.json에서 로드됨)을 사용.
    /// </summary>
    public static int GetDefaultMaxStackFor(ItemTypeEnum type, int defaultMaxStack)
    {
        switch (type)
        {
            case ItemTypeEnum.None:
                return 0;
            case ItemTypeEnum.Iron_Ore:
            case ItemTypeEnum.Copper_Ore:
            case ItemTypeEnum.Coal:
            case ItemTypeEnum.Stone:
                return 50;
            case ItemTypeEnum.Iron:
            case ItemTypeEnum.Copper:
            case ItemTypeEnum.Iron_Stick:
            case ItemTypeEnum.Copper_Stick:
                return 100;
            case ItemTypeEnum.Drone:
                return 1;
            default:
                return defaultMaxStack;
        }
    }
}
