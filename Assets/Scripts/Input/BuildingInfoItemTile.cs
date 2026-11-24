using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class BuildingInfoItemCatalog
{
    [Serializable]
    private sealed class ItemTextures
    {
        public ItemTexture[] Items = Array.Empty<ItemTexture>();
    }

    [Serializable]
    private sealed class ItemTexture
    {
        public string ItemType = string.Empty;
        public string TextureResourcePath = string.Empty;
    }

    private readonly Dictionary<ItemTypeEnum, string> _paths = new();
    private readonly Dictionary<ItemTypeEnum, Texture2D> _textures = new();

    public BuildingInfoItemCatalog()
    {
        var asset = Resources.Load<TextAsset>(ItemConfigInitSystem.DefaultResourcePath);
        if (asset == null)
        {
            return;
        }

        var config = JsonUtility.FromJson<ItemTextures>(asset.text);
        if (config?.Items == null)
        {
            return;
        }

        foreach (var item in config.Items)
        {
            if (Enum.TryParse(item.ItemType, out ItemTypeEnum type) && !string.IsNullOrEmpty(item.TextureResourcePath))
            {
                _paths[type] = item.TextureResourcePath;
            }
        }
    }

    public Texture2D GetTexture(ItemTypeEnum type)
    {
        if (!_textures.TryGetValue(type, out var texture) && _paths.TryGetValue(type, out var path))
        {
            texture = Resources.Load<Texture2D>(path);
            _textures[type] = texture;
        }
        return texture;
    }

    public static string GetName(ItemTypeEnum item) => item switch
    {
        ItemTypeEnum.Iron_Ore => "철광석",
        ItemTypeEnum.Copper_Ore => "구리 광석",
        ItemTypeEnum.Coal => "석탄",
        ItemTypeEnum.Stone => "돌",
        ItemTypeEnum.Iron => "철",
        ItemTypeEnum.Copper => "구리",
        ItemTypeEnum.Iron_Stick => "철 막대",
        ItemTypeEnum.Copper_Stick => "구리 막대",
        ItemTypeEnum.Drone => "드론",
        _ => item.ToString().Replace('_', ' ')
    };
}

internal sealed class BuildingInfoItemTile : Button
{
    private static readonly Color EmptyBackground = new(0.09f, 0.13f, 0.18f);
    private static readonly Color FilledBackground = new(0.16f, 0.22f, 0.27f);
    private static readonly Color SelectedBackground = new(0.14f, 0.32f, 0.28f);
    private static readonly Color SlotBorder = new(0.37f, 0.49f, 0.55f);
    private static readonly Color SelectedBorder = new(0.42f, 0.90f, 0.74f);
    private readonly BuildingInfoItemCatalog _catalog;
    private readonly Image _icon;
    private readonly Label _value;
    private bool _filled;
    private bool _marked;

    public BuildingInfoItemTile(BuildingInfoItemCatalog catalog, Action action = null) : base(action)
    {
        _catalog = catalog;
        style.width = 44;
        style.height = 44;
        style.paddingLeft = 2;
        style.paddingRight = 2;
        style.marginLeft = 0;
        style.marginTop = 0;
        style.marginRight = 4;
        style.marginBottom = 4;
        style.borderLeftWidth = 1;
        style.borderRightWidth = 1;
        style.borderTopWidth = 1;
        style.borderBottomWidth = 1;
        style.color = Color.white;
        style.fontSize = 10;
        style.whiteSpace = WhiteSpace.Normal;
        _icon = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.ScaleToFit };
        _icon.style.position = Position.Absolute;
        _icon.style.left = 4;
        _icon.style.right = 4;
        _icon.style.top = 3;
        _icon.style.bottom = 3;
        Add(_icon);
        _value = new Label { pickingMode = PickingMode.Ignore };
        _value.style.position = Position.Absolute;
        _value.style.right = 2;
        _value.style.bottom = 1;
        _value.style.fontSize = 11;
        _value.style.color = Color.white;
        _value.style.backgroundColor = new Color(0.03f, 0.05f, 0.07f, 0.94f);
        Add(_value);
    }

    public void Show(ItemTypeEnum type, string value, string description = null, bool marked = false)
    {
        _filled = type != ItemTypeEnum.None || !string.IsNullOrEmpty(value);
        _marked = marked;
        Texture2D texture = type == ItemTypeEnum.None ? null : _catalog.GetTexture(type);
        _icon.image = texture;
        text = type != ItemTypeEnum.None && texture == null ? BuildingInfoItemCatalog.GetName(type) : string.Empty;
        tooltip = description ?? (type == ItemTypeEnum.None ? "빈 슬롯" : BuildingInfoItemCatalog.GetName(type));
        _value.text = value;
        _value.style.display = string.IsNullOrEmpty(value) ? DisplayStyle.None : DisplayStyle.Flex;
        SetSelected(false);
    }

    public void SetSelected(bool selected)
    {
        style.backgroundColor = selected ? SelectedBackground : _filled ? FilledBackground : EmptyBackground;
        Color border = selected || _marked ? SelectedBorder : SlotBorder;
        style.borderLeftColor = border;
        style.borderRightColor = border;
        style.borderTopColor = border;
        style.borderBottomColor = border;
    }

    public static VisualElement NewGrid(string name)
    {
        var grid = new VisualElement { name = name };
        grid.style.flexDirection = FlexDirection.Row;
        grid.style.flexWrap = Wrap.Wrap;
        return grid;
    }
}
