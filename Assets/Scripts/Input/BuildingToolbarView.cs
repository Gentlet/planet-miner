using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;

/// <summary>선택 가능한 건물/현재 키/모드의 UI 투영이다. WorldConstructionController의 요청 경계를 호출한다.</summary>
[RequireComponent(typeof(WorldConstructionController))]
public sealed class BuildingToolbarView : MonoBehaviour
{
    private WorldConstructionController _tools;
    private WorldInputController _input;
    private GameObject _rootObject;
    private PanelSettings _panel;
    private VisualElement _buttons;
    private Label _status;
    private int _catalogVersion = -1;
    private uint _settingsRevision;

    private void OnEnable()
    {
        _tools = GetComponent<WorldConstructionController>();
        _input = GetComponent<WorldInputController>();
        _panel = ScriptableObject.CreateInstance<PanelSettings>();
        _panel.hideFlags = HideFlags.HideAndDontSave;
        _panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        _panel.referenceResolution = new Vector2Int(1920, 1080);
        _rootObject = new GameObject("Construction toolbar") { hideFlags = HideFlags.DontSave };
        _rootObject.SetActive(false);
        _rootObject.transform.SetParent(transform, false);
        var document = _rootObject.AddComponent<UIDocument>();
        document.panelSettings = _panel;
        _rootObject.AddComponent<WorldInputUiBlocker>().Initialize(_input);
        _rootObject.SetActive(true);
        var root = document.rootVisualElement;
        root.pickingMode = PickingMode.Ignore;
        root.style.position = Position.Absolute;
        root.style.left = 0;
        root.style.top = 0;
        root.style.right = 0;
        root.style.bottom = 0;
        root.style.justifyContent = Justify.FlexEnd;
        root.style.unityFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var bar = new VisualElement();
        bar.AddToClassList(WorldInputUiBlocker.BlockingUiClass);
        bar.style.backgroundColor = new Color(0.035f, 0.055f, 0.08f, 0.96f);
        bar.style.paddingTop = 8;
        bar.style.paddingBottom = 8;
        bar.style.paddingLeft = 10;
        bar.style.paddingRight = 10;
        bar.style.marginLeft = 12;
        bar.style.marginRight = 12;
        bar.style.marginBottom = 12;
        root.Add(bar);
        _buttons = new VisualElement();
        _buttons.style.flexDirection = FlexDirection.Row;
        _buttons.style.flexWrap = Wrap.Wrap;
        bar.Add(_buttons);
        _status = new Label();
        _status.style.color = Color.white;
        _status.style.fontSize = 14;
        bar.Add(_status);
        _tools.Changed += Refresh;
        _catalogVersion = -1;
        Refresh();
    }

    private void Refresh()
    {
        if (_buttons == null)
        {
            return;
        }
        if (_catalogVersion != _tools.CatalogVersion || _settingsRevision != _input.SettingsRevision)
        {
            _catalogVersion = _tools.CatalogVersion;
            _settingsRevision = _input.SettingsRevision;
            _buttons.Clear();
            for (int i = 0; i < _tools.Catalog.Count; i++)
            {
                var config = _tools.Catalog[i].Config;
                string key = i < 10 ? Key("SelectSlot" + (i + 1)) + "  " : string.Empty;
                AddButton(key + config.DisplayName + " T" + config.Tier,
                    () => _tools.SelectBuilding(config.BuildingType, config.Tier));
            }
            AddButton(Key("Copy") + " 복사", _tools.BeginCopyRange);
            AddButton(Key("RangeDemolish") + " 범위 작업", _tools.BeginDemolitionRange);
            AddButton(Key("Cancel") + " 해제", _tools.CancelTool);
        }
        _status.text = _tools.Status + "    " + Key("Rotate") + " 회전 / " + Key("SettingsModifier") + "+우/좌클릭 설정 복사/붙이기";
    }

    private string Key(string action)
    {
        string modifier = string.Empty;
        foreach (var binding in _input.GetRebindableBindings())
        {
            if (binding.ActionName != "Construction/" + action)
            {
                continue;
            }
            string part = string.IsNullOrEmpty(binding.EffectivePath) ? string.Empty :
                InputControlPath.ToHumanReadableString(binding.EffectivePath,
                    InputControlPath.HumanReadableStringOptions.OmitDevice);
            if (action == "Copy")
            {
                if (string.Equals(binding.Name, "modifier", System.StringComparison.OrdinalIgnoreCase))
                {
                    modifier = part;
                }
                else if (string.Equals(binding.Name, "button", System.StringComparison.OrdinalIgnoreCase))
                {
                    if (modifier.Length > 0 && part.Length > 0)
                    {
                        return modifier + "+" + part;
                    }
                    modifier = string.Empty;
                }
                continue;
            }
            if (part.Length > 0)
            {
                return part;
            }
        }
        return "미지정";
    }

    private void AddButton(string text, System.Action click)
    {
        var button = new Button(click) { text = text };
        button.style.minWidth = 94;
        button.style.height = 42;
        button.style.marginRight = 5;
        button.style.marginBottom = 5;
        button.style.backgroundColor = new Color(0.13f, 0.22f, 0.3f);
        button.style.color = Color.white;
        button.style.fontSize = 13;
        _buttons.Add(button);
    }

    private void OnDisable()
    {
        if (_tools != null)
        {
            _tools.Changed -= Refresh;
        }
        _buttons = null;
        _status = null;
        if (_rootObject != null)
        {
            _rootObject.SetActive(false);
            DestroyOwned(_rootObject);
            _rootObject = null;
        }
        if (_panel != null)
        {
            DestroyOwned(_panel);
            _panel = null;
        }
    }

    private static void DestroyOwned(Object value)
    {
        if (Application.isPlaying)
        {
            Destroy(value);
        }
        else
        {
            DestroyImmediate(value);
        }
    }
}
