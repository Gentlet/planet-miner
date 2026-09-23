using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
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
/// ItemRegistryBlob을 빌드하고 ItemRegistry 싱글톤 엔티티를 1회 초기화하는 시스템.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class ItemConfigInitSystem : SystemBase
{
    private BlobAssetReference<ItemRegistryBlob> _blobReference;

    protected override void OnUpdate()
    {
        if (SystemAPI.HasSingleton<ItemRegistry>())
        {
            Enabled = false;
            return;
        }

        // 싱글톤이 없을 때만 1회 생성 및 초기화
        _blobReference = InitializeItemRegistry(EntityManager);
        Enabled = false;
    }

    protected override void OnDestroy()
    {
        if (_blobReference.IsCreated)
        {
            _blobReference.Dispose();
        }
        base.OnDestroy();
    }

    /// <summary>
    /// ItemRegistry 싱글톤 및 BlobAsset을 초기화.
    /// jsonOverride가 주어지면 해당 문자열을 파싱하고, 없으면 StreamingAssets/ItemConfig.json 또는 기본 폴백을 사용.
    /// </summary>
    public static BlobAssetReference<ItemRegistryBlob> InitializeItemRegistry(EntityManager entityManager, string jsonOverride = null)
    {
        string jsonText = jsonOverride;
        if (string.IsNullOrEmpty(jsonText))
        {
            jsonText = TryReadConfigFile();
        }

        BlobAssetReference<ItemRegistryBlob> blobRef;
        if (!string.IsNullOrEmpty(jsonText))
        {
            blobRef = BuildBlobAssetFromJson(jsonText);
        }
        else
        {
            blobRef = BuildDefaultFallbackBlobAsset();
        }

        var query = entityManager.CreateEntityQuery(typeof(ItemRegistry));
        Entity singletonEntity;

        if (query.CalculateEntityCount() > 0)
        {
            singletonEntity = query.GetSingletonEntity();
            entityManager.SetComponentData(singletonEntity, new ItemRegistry(blobRef));
        }
        else
        {
            singletonEntity = entityManager.CreateEntity(typeof(ItemRegistry));
            entityManager.SetComponentData(singletonEntity, new ItemRegistry(blobRef));
        }

        return blobRef;
    }

    /// <summary>
    /// JSON 문자열로부터 ItemRegistryBlob BlobAsset을 빌드.
    /// </summary>
    public static BlobAssetReference<ItemRegistryBlob> BuildBlobAssetFromJson(string jsonText)
    {
        int defaultMaxStack = 50;
        var customStacks = new Dictionary<ItemTypeEnum, int>();

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

        return BuildBlobAsset(defaultMaxStack, customStacks);
    }

    /// <summary>
    /// 기본 폴백 ItemRegistryBlob BlobAsset을 빌드.
    /// </summary>
    public static BlobAssetReference<ItemRegistryBlob> BuildDefaultFallbackBlobAsset(int defaultMaxStack = 50)
    {
        return BuildBlobAsset(defaultMaxStack, null);
    }

    private static BlobAssetReference<ItemRegistryBlob> BuildBlobAsset(int defaultMaxStack, Dictionary<ItemTypeEnum, int> customStacks)
    {
        using (var builder = new BlobBuilder(Allocator.Temp))
        {
            ref var root = ref builder.ConstructRoot<ItemRegistryBlob>();
            root.DefaultMaxStack = defaultMaxStack;

            int count = System.Enum.GetValues(typeof(ItemTypeEnum)).Length;
            var itemsArray = builder.Allocate(ref root.Items, count);

            for (int i = 0; i < count; i++)
            {
                var itemType = (ItemTypeEnum)i;
                int maxStack;

                if (customStacks != null && customStacks.TryGetValue(itemType, out int customValue))
                {
                    maxStack = customValue;
                }
                else
                {
                    maxStack = GetDefaultMaxStackFor(itemType, defaultMaxStack);
                }

                itemsArray[i] = new ItemDataBlob(itemType, maxStack);
            }

            return builder.CreateBlobAssetReference<ItemRegistryBlob>(Allocator.Persistent);
        }
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
