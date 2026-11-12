using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// ItemConfigInitSystem이 JSON 파싱 중에만 이용하는 managed 입력 DTO. ECS에 게시하지 않고 파싱 후 설정 버퍼로 변환한다.
/// </summary>
[Serializable]
public class ItemConfigJsonData
{
    public List<ItemConfigJsonEntry> Items = new List<ItemConfigJsonEntry>();
}

/// <summary>품목 문자열과 스택 한도의 JSON 행. ItemConfigInitSystem이 파싱 중 품목 설정으로 변환하며 ECS에는 부착하지 않는다.</summary>
[Serializable]
public class ItemConfigJsonEntry
{
    public int ItemId;
    public string ItemType;
    public int MaxStack;
}

/// <summary>
/// 역할·목적: Initialization에서 품목별 최대 스택 설정을 한 번 게시한다.
/// 입력·생성: Resources/Config/ItemConfig 또는 사전 등록 API의 JSON을 읽는다. 모든 실제 품목의 명시적 양수 값이 필수이며 읽기/파싱/검증 실패는 중단 오류다.
/// 출력·소유권: ItemRegistry의 기존 품목 인덱스에 품목 ID와 MaxStack을 함께 게시한다. 유효한 사전 등록은 보존한다.
/// 이용: 입고 슬롯 예약, 생산 출력 공간 검사와 드론 보관 계획이 World 수명 동안 읽기 전용으로 이용한다.
/// 정리: 초기화 뒤 비활성화하며 게시 도중 예외는 이번 호출이 만든 미완성 엔티티만 회수한다. 게시된 설정을 시스템 종료 시 삭제하지 않는다.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class ItemConfigInitSystem : SystemBase
{
    public const string DefaultResourcePath = "Config/ItemConfig";

    protected override void OnUpdate()
    {
        if (SimulationFailureUtility.HasFatalError(EntityManager))
        {
            Enabled = false;
            return;
        }

        using var query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ItemRegistry>());
        if (!query.IsEmptyIgnoreFilter)
        {
            Entity registryEntity = query.GetSingletonEntity();
            if (!EntityManager.HasBuffer<ItemConfigElement>(registryEntity))
            {
                ReportFailure(EntityManager, "The pre-registered ItemRegistry requires an ItemConfigElement buffer.");
                Enabled = false;
                return;
            }

            try
            {
                ValidateRegisteredItems(EntityManager.GetBuffer<ItemConfigElement>(registryEntity, true));
            }
            catch (ArgumentException exception)
            {
                ReportFailure(EntityManager, exception.Message);
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

        try
        {
            string jsonText = jsonOverride ?? ReadConfigResource();
            var items = ParseItems(jsonText, requireItemIds: jsonOverride == null);
            Entity registryEntity = entityManager.CreateEntity(typeof(ItemRegistry), typeof(ItemConfigElement));
            try
            {
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
        catch (Exception exception)
        {
            ReportFailure(entityManager, exception.Message);
            return Entity.Null;
        }
    }

    private static void ReportFailure(EntityManager entityManager, string reason)
    {
        SimulationFailureUtility.RecordInitializationFailure(entityManager,
            $"[ItemConfigInitSystem] {reason} Game simulation stopped.",
            "Item configuration initialization failed. See error log.");
    }

    /// <summary>같은 World에 게시된 ID를 설정 로드 동안만 사용하는 조회표로 복사한다.</summary>
    public static IReadOnlyDictionary<int, ItemTypeEnum> ReadRegisteredItemIds(EntityManager manager)
    {
        var itemIds = new Dictionary<int, ItemTypeEnum>();
        using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<ItemRegistry>());
        if (query.IsEmptyIgnoreFilter)
        {
            return itemIds;
        }

        var items = manager.GetBuffer<ItemConfigElement>(query.GetSingletonEntity(), true);
        foreach (var item in items)
        {
            if (item.ItemId != 0)
            {
                itemIds.Add(item.ItemId, item.ItemType);
            }
        }
        return itemIds;
    }

    internal static ItemTypeEnum ResolveItemId(int itemId, IReadOnlyDictionary<int, ItemTypeEnum> itemIds)
    {
        if (itemIds == null)
        {
            throw new ArgumentException($"Resolving itemId {itemId} requires a registered item configuration.");
        }
        if (!itemIds.TryGetValue(itemId, out var itemType))
        {
            throw new ArgumentException($"Unknown itemId {itemId} in the registered item configuration.");
        }
        return itemType;
    }

    private static List<ItemConfigElement> ParseItems(string jsonText, bool requireItemIds)
    {
        var itemIds = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            throw new ArgumentException("Item JSON is null or empty.", nameof(jsonText));
        }
        var data = JsonUtility.FromJson<ItemConfigJsonData>(jsonText);
        if (data == null)
        {
            throw new ArgumentException("Parsed item configuration is null.", nameof(jsonText));
        }

        if (data.Items == null)
        {
            throw new ArgumentException("Item configuration requires an Items list.", nameof(jsonText));
        }
        var itemsByType = new Dictionary<ItemTypeEnum, ItemConfigElement>();
        foreach (var item in data.Items)
        {
            if (item == null)
            {
                throw new ArgumentException("Item configuration contains a null entry.", nameof(jsonText));
            }
            if (string.IsNullOrWhiteSpace(item.ItemType))
            {
                throw new ArgumentException("Item configuration contains an empty item name.", nameof(jsonText));
            }
            if (!Enum.TryParse<ItemTypeEnum>(item.ItemType, true, out var parsedType) ||
                !Enum.IsDefined(typeof(ItemTypeEnum), parsedType) ||
                !string.Equals(item.ItemType.Trim(), parsedType.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Unknown item name '{item.ItemType}'.", nameof(jsonText));
            }
            if (parsedType == ItemTypeEnum.None)
            {
                throw new ArgumentException("None is not a configurable item.", nameof(jsonText));
            }
            ValidateMaxStack(parsedType, item.MaxStack);
            if (requireItemIds || item.ItemId != 0)
            {
                if (!IsItemId(item.ItemId))
                {
                    throw new ArgumentException($"Item '{parsedType}' requires an ItemId in category 120.", nameof(jsonText));
                }
                if (!itemIds.Add(item.ItemId))
                {
                    throw new ArgumentException($"Duplicate ItemId {item.ItemId}.", nameof(jsonText));
                }
            }
            if (!itemsByType.TryAdd(parsedType, new ItemConfigElement(parsedType, item.MaxStack, item.ItemId)))
            {
                throw new ArgumentException($"Duplicate item definition '{parsedType}'.", nameof(jsonText));
            }
        }

        int count = Enum.GetValues(typeof(ItemTypeEnum)).Length;
        var items = new List<ItemConfigElement>(count);
        var missingItems = new List<string>();
        // None은 설정 항목이 아니다. 품목 번호와 버퍼 인덱스의 기존 대응을 위해 0번만 비워 둔다.
        items.Add(new ItemConfigElement(ItemTypeEnum.None, 0));
        for (int i = 1; i < count; i++)
        {
            var itemType = (ItemTypeEnum)i;
            if (!itemsByType.TryGetValue(itemType, out var item))
            {
                missingItems.Add(itemType.ToString());
                continue;
            }
            items.Add(item);
        }
        if (missingItems.Count > 0)
        {
            throw new ArgumentException($"Missing item definitions: {string.Join(", ", missingItems)}.", nameof(jsonText));
        }

        return items;
    }

    private static void ValidateMaxStack(ItemTypeEnum itemType, int maxStack)
    {
        if (maxStack <= 0)
        {
            throw new ArgumentException($"Item '{itemType}' requires an explicit positive MaxStack; received {maxStack}.");
        }
    }

    private static bool IsItemId(int itemId) => itemId / 100000 == 120 && itemId % 100000 != 0;

    private static void ValidateRegisteredItems(DynamicBuffer<ItemConfigElement> items)
    {
        int count = Enum.GetValues(typeof(ItemTypeEnum)).Length;
        if (items.Length != count)
        {
            throw new ArgumentException("The pre-registered ItemRegistry is missing its indexed item definitions.");
        }
        var itemIds = new HashSet<int>();
        for (int i = 0; i < count; i++)
        {
            var item = items[i];
            if (item.ItemType != (ItemTypeEnum)i)
            {
                throw new ArgumentException($"The pre-registered ItemRegistry has an invalid item at index {i}.");
            }
            if (item.ItemType == ItemTypeEnum.None)
            {
                if (item.MaxStack != 0 || item.ItemId != 0)
                {
                    throw new ArgumentException("The reserved None entry requires MaxStack and ItemId 0.");
                }
                continue;
            }
            if (item.ItemId != 0 && (!IsItemId(item.ItemId) || !itemIds.Add(item.ItemId)))
            {
                throw new ArgumentException($"The pre-registered ItemRegistry has an invalid or duplicate ItemId {item.ItemId}.");
            }
            ValidateMaxStack(item.ItemType, item.MaxStack);
        }
    }

    private static string ReadConfigResource()
    {
        var asset = Resources.Load<TextAsset>(DefaultResourcePath);
        if (asset == null)
        {
            throw new InvalidOperationException($"Item config resource was not found at '{DefaultResourcePath}'.");
        }
        return asset.text;
    }
}
