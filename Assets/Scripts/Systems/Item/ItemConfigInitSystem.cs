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
/// 게임 시작 시(InitializationSystemGroup) ItemConfig.json 또는 기본값으로
/// ItemConfig 싱글톤 및 DynamicBuffer<ItemConfigElement>를 1회 초기화하는 시스템.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class ItemConfigInitSystem : SystemBase
{
    protected override void OnUpdate()
    {
        if (SystemAPI.HasSingleton<ItemConfig>())
        {
            Enabled = false;
            return;
        }

        // 싱글톤이 없을 때만 1회 생성 및 초기화
        InitializeItemConfig(EntityManager);
        Enabled = false;
    }

    /// <summary>
    /// ItemConfig 싱글톤 및 버퍼를 초기화합니다.
    /// jsonOverride가 주어지면 해당 문자열을 파싱하고, 없으면 StreamingAssets/ItemConfig.json 또는 기본 폴백을 사용합니다.
    /// </summary>
    public static Entity InitializeItemConfig(EntityManager entityManager, string jsonOverride = null)
    {
        var query = entityManager.CreateEntityQuery(typeof(ItemConfig));
        Entity singletonEntity;
        DynamicBuffer<ItemConfigElement> buffer;

        if (query.CalculateEntityCount() > 0)
        {
            singletonEntity = query.GetSingletonEntity();
            buffer = entityManager.GetBuffer<ItemConfigElement>(singletonEntity);
        }
        else
        {
            singletonEntity = entityManager.CreateEntity(typeof(ItemConfig));
            buffer = entityManager.AddBuffer<ItemConfigElement>(singletonEntity);
        }

        // 1. JSON 내용 획득
        string jsonText = jsonOverride;
        if (string.IsNullOrEmpty(jsonText))
        {
            jsonText = TryReadConfigFile();
        }

        int defaultMaxStack = 50;
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

        // 2. 싱글톤 설정 저장
        entityManager.SetComponentData(singletonEntity, new ItemConfig(defaultMaxStack));

        // 3. 버퍼를 (int)ItemTypeEnum.Count 크기에 맞춰 인덱스 1:1로 채우기
        buffer.ResizeUninitialized((int)ItemTypeEnum.Count);
        for (int i = 0; i < (int)ItemTypeEnum.Count; i++)
        {
            var itemType = (ItemTypeEnum)i;
            int maxStack;

            if (customStacks.TryGetValue(itemType, out int customValue))
            {
                maxStack = customValue;
            }
            else
            {
                maxStack = GetDefaultMaxStackFor(itemType, defaultMaxStack);
            }

            buffer[i] = new ItemConfigElement(itemType, maxStack);
        }

        return singletonEntity;
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
    /// 파일이 없거나 정의되지 않은 아이템에 대한 기본 MaxStack을 반환합니다.
    /// fallback으로 전달된 defaultMaxStack(ItemConfig.json에서 로드됨)을 사용합니다.
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
