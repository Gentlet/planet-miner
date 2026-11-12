using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Entities;
using UnityEngine;

/// <summary>초기 지급 JSON의 검증과 정의 게시만 소유한다. 실물 생성/보관은 지급 소유자가 처리한다.</summary>
public static class StartingItemConfigLoader
{
    public const string DefaultResourcePath = "Config/StartingItemConfig";

    public static List<StartingItemConfigElement> LoadFromResources(string resourcePath = DefaultResourcePath, IReadOnlyDictionary<int, ItemTypeEnum> itemIds = null)
    {
        var asset = Resources.Load<TextAsset>(resourcePath);
        if (asset == null)
        {
            throw new InvalidOperationException($"Starting item configuration was not found at '{resourcePath}'.");
        }

        return ParseJson(asset.text, itemIds, string.Equals(resourcePath, DefaultResourcePath, StringComparison.OrdinalIgnoreCase));
    }

    public static List<StartingItemConfigElement> ParseJson(string json, IReadOnlyDictionary<int, ItemTypeEnum> itemIds = null)
    {
        return ParseJson(json, itemIds, requireItemIds: false);
    }

    private static List<StartingItemConfigElement> ParseJson(string json, IReadOnlyDictionary<int, ItemTypeEnum> itemIds, bool requireItemIds)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("Starting item JSON must not be empty.", nameof(json));
        }

        var root = JObject.Parse(json);
        if (!(root["items"] is JArray items))
        {
            throw new ArgumentException("Starting item configuration requires an explicit items array.", nameof(json));
        }

        var definitions = new List<StartingItemConfigElement>(items.Count);
        foreach (var token in items)
        {
            if (!(token is JObject entry))
            {
                throw new ArgumentException("Starting item entries must be objects.", nameof(json));
            }

            ItemTypeEnum itemType = ReadItemType(entry, itemIds, requireItemIds);
            string itemName = itemType.ToString();

            var quantityToken = entry["quantity"];
            if (quantityToken == null)
            {
                throw new ArgumentException($"Starting quantity for '{itemName}' is missing.", nameof(json));
            }

            if (quantityToken.Type != JTokenType.Integer)
            {
                throw new ArgumentException($"Starting quantity for '{itemName}' must be an integer token.", nameof(json));
            }

            if (!int.TryParse(quantityToken.ToString(Formatting.None), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int quantity))
            {
                throw new ArgumentException($"Starting quantity for '{itemName}' must be an explicit Int32 integer.", nameof(json));
            }

            definitions.Add(new StartingItemConfigElement(itemType, quantity));
        }

        ValidateDefinitions(definitions);
        return definitions;
    }

    private static ItemTypeEnum ReadItemType(JObject entry, IReadOnlyDictionary<int, ItemTypeEnum> itemIds, bool requireItemIds)
    {
        var idToken = entry["itemId"];
        if (idToken != null)
        {
            if (idToken.Type != JTokenType.Integer ||
                !int.TryParse(idToken.ToString(Formatting.None), NumberStyles.Integer, CultureInfo.InvariantCulture, out int itemId))
            {
                throw new ArgumentException("Starting itemId must be an explicit Int32 integer.");
            }
            return ItemConfigInitSystem.ResolveItemId(itemId, itemIds);
        }
        if (requireItemIds)
        {
            throw new ArgumentException("Starting itemId is missing.");
        }

        var itemToken = entry["itemType"];
        if (itemToken == null)
        {
            throw new ArgumentException("Starting itemType is missing.");
        }

        if (itemToken.Type != JTokenType.String)
        {
            throw new ArgumentException("Starting itemType must be an explicit item name.");
        }

        string itemName = itemToken.Value<string>().Trim();
        if (!Enum.TryParse(itemName, true, out ItemTypeEnum itemType) ||
            !Enum.IsDefined(typeof(ItemTypeEnum), itemType) ||
            !string.Equals(itemName, itemType.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unknown starting item name '{itemName}'.");
        }
        return itemType;
    }

    public static Entity PublishConfig(EntityManager manager, IReadOnlyList<StartingItemConfigElement> definitions)
    {
        using var existing = manager.CreateEntityQuery(typeof(StartingItemConfig));
        if (!existing.IsEmptyIgnoreFilter)
        {
            throw new InvalidOperationException("Starting item configuration is already registered. Runtime replacement is not supported.");
        }

        ValidateDefinitions(definitions);
        Entity created = manager.CreateEntity(typeof(StartingItemConfig));
        try
        {
            var buffer = manager.AddBuffer<StartingItemConfigElement>(created);
            buffer.EnsureCapacity(definitions.Count);
            for (int i = 0; i < definitions.Count; i++)
            {
                buffer.Add(definitions[i]);
            }

            return created;
        }
        catch
        {
            // 기존 원본은 보존하고 이 게시 호출이 만든 미완성 엔티티만 회수한다.
            manager.DestroyEntity(created);
            throw;
        }
    }

    public static void ValidatePublishedConfig(DynamicBuffer<StartingItemConfigElement> definitions)
    {
        var itemTypes = new HashSet<ItemTypeEnum>();
        for (int i = 0; i < definitions.Length; i++)
        {
            ValidateEntry(definitions[i], itemTypes);
        }
    }

    private static void ValidateDefinitions(IReadOnlyList<StartingItemConfigElement> definitions)
    {
        if (definitions == null)
        {
            throw new ArgumentNullException(nameof(definitions));
        }

        var itemTypes = new HashSet<ItemTypeEnum>();
        for (int i = 0; i < definitions.Count; i++)
        {
            ValidateEntry(definitions[i], itemTypes);
        }
    }

    private static void ValidateEntry(in StartingItemConfigElement definition, HashSet<ItemTypeEnum> itemTypes)
    {
        if (definition.ItemType == ItemTypeEnum.None || !Enum.IsDefined(typeof(ItemTypeEnum), definition.ItemType))
        {
            throw new ArgumentException($"Invalid starting item type '{definition.ItemType}'.");
        }

        if (definition.Quantity <= 0)
        {
            throw new ArgumentException($"Starting quantity for '{definition.ItemType}' must be positive.");
        }

        if (!itemTypes.Add(definition.ItemType))
        {
            throw new ArgumentException($"Duplicate starting item '{definition.ItemType}'.");
        }
    }
}
