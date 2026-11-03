using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>정보 패널의 표시와 콜백만 소유한다. 반복 갱신은 기존 행을 재사용하고 요청은 Controller에 전달한다.</summary>
[RequireComponent(typeof(BuildingInfoController), typeof(WorldInputController))]
public sealed class BuildingInfoView : MonoBehaviour
{
    private sealed class ActionRow
    {
        public readonly VisualElement Root = new VisualElement();
        public readonly Label Label = new Label();
        public readonly Button Button;
        public Action Click;
        public ActionRow()
        {
            Root.style.flexDirection = FlexDirection.Row;
            Label.style.flexGrow = 1;
            Label.style.whiteSpace = WhiteSpace.Normal;
            Root.Add(Label);
            Button = new Button(() => Click?.Invoke());
            Root.Add(Button);
        }
    }

    private BuildingInfoController _controller;
    private GameObject _rootObject;
    private PanelSettings _panelSettings;
    private VisualElement _panel;
    private Label _title;
    private Label _details;
    private Label _feedback;
    private DropdownField _area;
    private DropdownField _item;
    private IntegerField _quantity;
    private VisualElement _transport;
    private VisualElement _settings;
    private DropdownField _recipe;
    private DropdownField _filterMode;
    private readonly List<Toggle> _filterItems = new List<Toggle>();
    private readonly List<ItemTypeEnum> _filterTypes = new List<ItemTypeEnum>();
    private readonly List<int> _recipes = new List<int>();
    private readonly List<DroneTransportAreaEnum> _areas = new List<DroneTransportAreaEnum>();
    private VisualElement _filterList;
    private VisualElement _inventory;
    private VisualElement _tasks;
    private VisualElement _drones;
    private readonly List<Label> _itemRows = new List<Label>();
    private readonly List<ActionRow> _taskRows = new List<ActionRow>();
    private readonly List<ActionRow> _droneRows = new List<ActionRow>();
    private bool _resetDraft = true;
    private void OnEnable()
    {
        _controller = GetComponent<BuildingInfoController>();
        _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        _panelSettings.hideFlags = HideFlags.HideAndDontSave;
        _panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        _panelSettings.referenceResolution = new Vector2Int(1920, 1080);
        _rootObject = new GameObject("Building information")
        {
            hideFlags = HideFlags.DontSave
        };
        _rootObject.SetActive(false);
        _rootObject.transform.SetParent(transform, false);
        var document = _rootObject.AddComponent<UIDocument>();
        document.panelSettings = _panelSettings;
        document.sortingOrder = 10;
        _rootObject.AddComponent<WorldInputUiBlocker>().Initialize(GetComponent<WorldInputController>());
        _rootObject.SetActive(true);
        var root = document.rootVisualElement;
        root.pickingMode = PickingMode.Ignore;
        root.style.position = Position.Absolute;
        root.style.left = 0;
        root.style.top = 0;
        root.style.right = 0;
        root.style.bottom = 0;
        root.style.unityFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _panel = new VisualElement
        {
            name = "building-info"
        };
        _panel.AddToClassList(WorldInputUiBlocker.BlockingUiClass);
        _panel.style.position = Position.Absolute;
        _panel.style.right = 12;
        _panel.style.top = 12;
        _panel.style.bottom = 112;
        _panel.style.width = 410;
        _panel.style.backgroundColor = new Color(0.035f, 0.055f, 0.08f, 0.97f);
        _panel.style.color = Color.white;
        _panel.style.paddingLeft = 14;
        _panel.style.paddingRight = 14;
        _panel.style.paddingTop = 10;
        _panel.style.paddingBottom = 10;
        root.Add(_panel);
        _title = new Label
        {
            name = "title"
        };
        _title.style.fontSize = 20;
        _panel.Add(_title);
        _panel.Add(new Button(() => GetComponent<WorldSelectionController>().Clear()) { text = "닫기" });
        var content = new ScrollView();
        content.style.flexGrow = 1;
        _panel.Add(content);
        _details = new Label
        {
            name = "details"
        };
        _details.style.whiteSpace = WhiteSpace.Normal;
        content.Add(_details);
        content.Add(new Label("보관/입력/생산품"));
        _inventory = new VisualElement();
        content.Add(_inventory);
        BuildSettings(content);
        BuildTransport(content);
        content.Add(new Label("이 건물의 운반 작업"));
        _tasks = new VisualElement();
        content.Add(_tasks);
        content.Add(new Label("정거장 드론"));
        _drones = new VisualElement();
        content.Add(_drones);
        _feedback = new Label
        {
            name = "feedback"
        };
        _feedback.style.whiteSpace = WhiteSpace.Normal;
        _panel.Add(_feedback);
        _controller.Changed += Render;
        _controller.SelectionChanged += ResetDraft;
        _resetDraft = true;
        Render();
    }

    private void BuildSettings(VisualElement parent)
    {
        _settings = new VisualElement();
        parent.Add(_settings);
        _recipe = new DropdownField("레시피");
        _settings.Add(_recipe);
        _settings.Add(new Button(() =>
        {
            if (_recipe.index >= 0 && _recipe.index < _recipes.Count)
            {
                _controller.SetRecipe(_recipes[_recipe.index]);
            }
        }) { text = "레시피 적용", name = "apply-recipe" });
        _filterMode = new DropdownField("입고 필터", new List<string> { "전체 허용", "허용 목록", "차단 목록" }, 0);
        _settings.Add(_filterMode);
        _filterList = new VisualElement();
        _settings.Add(_filterList);
        _settings.Add(new Button(() =>
        {
            var filter = new StorageFilter((StorageFilterMode)_filterMode.index);
            for (int i = 0; i < _filterItems.Count; i++)
            {
                filter.Mask.Set((byte)_filterTypes[i], _filterItems[i].value);
            }

            _controller.SetFilter(filter);
        }) { text = "필터 적용", name = "apply-filter" });
    }

    private void BuildTransport(VisualElement parent)
    {
        _transport = new VisualElement();
        parent.Add(_transport);
        _transport.Add(new Label("일회성 운반 요청"));
        _area = new DropdownField("영역");
        _area.RegisterValueChangedCallback(OnAreaChanged);
        _item = new DropdownField("품목");
        _quantity = new IntegerField("수량")
        {
            value = 1,
            name = "transport-quantity"
        };
        _quantity.RegisterValueChangedCallback(OnQuantityChanged);
        _transport.Add(_area);
        _transport.Add(_item);
        _transport.Add(_quantity);
        _transport.Add(new Button(() => SubmitTransport(DroneLogisticsTaskKindEnum.StorageImport)) { text = "반입 요청", name = "import-items" });
        _transport.Add(new Button(() => SubmitTransport(DroneLogisticsTaskKindEnum.StorageExport)) { text = "반출 요청", name = "export-items" });
    }

    private void SubmitTransport(DroneLogisticsTaskKindEnum kind)
    {
        if (_item.index < 0 || _item.index >= _controller.ItemTypes.Count)
        {
            return;
        }

        if (_area.index < 0 || _area.index >= _areas.Count)
        {
            return;
        }

        _controller.RequestTransport(kind, _areas[_area.index], _controller.ItemTypes[_item.index], _quantity.value);
    }

    private void OnAreaChanged(ChangeEvent<string> change)
    {
        UpdateTransportButtons();
    }

    private void OnQuantityChanged(ChangeEvent<int> change)
    {
        UpdateTransportButtons();
    }

    private void UpdateTransportButtons()
    {
        if (_transport == null)
        {
            return;
        }

        bool valid = _area.index >= 0 && _area.index < _areas.Count && _quantity.value > 0;
        bool product = valid && _areas[_area.index] == DroneTransportAreaEnum.Product;
        _transport.Q<Button>("import-items").SetEnabled(valid && !product);
        _transport.Q<Button>("export-items").SetEnabled(valid);
    }

    private void ResetDraft()
    {
        _resetDraft = true;
        Render();
    }

    private void Render()
    {
        if (_panel == null)
        {
            return;
        }

        _panel.style.display = _controller.HasSelection ? DisplayStyle.Flex : DisplayStyle.None;
        if (!_controller.HasSelection)
        {
            return;
        }

        _title.text = _controller.Title;
        _details.text = _controller.Details;
        _feedback.text = _controller.Feedback;
        while (_itemRows.Count < _controller.Items.Count)
        {
            var label = new Label();
            _itemRows.Add(label);
            _inventory.Add(label);
        }

        for (int i = 0; i < _itemRows.Count; i++)
        {
            _itemRows[i].style.display = i < _controller.Items.Count ? DisplayStyle.Flex : DisplayStyle.None;
            if (i < _controller.Items.Count)
            {
                var row = _controller.Items[i];
                _itemRows[i].text = row.Area + " / " + row.Type + " " + row.Count;
            }
        }

        bool crafter = _controller.Type == BuildingTypeEnum.Crafter;
        bool storage = _controller.Type == BuildingTypeEnum.Storage;
        _settings.style.display = crafter || storage ? DisplayStyle.Flex : DisplayStyle.None;
        _recipe.style.display = crafter ? DisplayStyle.Flex : DisplayStyle.None;
        _settings.Q<Button>("apply-recipe").style.display = crafter ? DisplayStyle.Flex : DisplayStyle.None;
        _filterMode.style.display = storage ? DisplayStyle.Flex : DisplayStyle.None;
        _filterList.style.display = storage ? DisplayStyle.Flex : DisplayStyle.None;
        _settings.Q<Button>("apply-filter").style.display = storage ? DisplayStyle.Flex : DisplayStyle.None;
        bool transport = storage || crafter || _controller.Type == BuildingTypeEnum.MainFacility || _controller.Type == BuildingTypeEnum.DroneStation || _controller.Type == BuildingTypeEnum.CoalGenerator || _controller.Type == BuildingTypeEnum.ResearchBuilding || _controller.Type == BuildingTypeEnum.Miner;
        _transport.style.display = transport ? DisplayStyle.Flex : DisplayStyle.None;
        bool itemCatalogChanged = _item.choices.Count != _controller.ItemTypes.Count;
        if (!itemCatalogChanged)
        {
            for (int i = 0; i < _item.choices.Count; i++)
            {
                itemCatalogChanged |= _item.choices[i] != _controller.ItemTypes[i].ToString();
            }
        }

        if (itemCatalogChanged)
        {
            var choices = new List<string>();
            foreach (var type in _controller.ItemTypes)
            {
                choices.Add(type.ToString());
            }

            _item.choices = choices;
            _item.index = choices.Count > 0 ? 0 : -1;
            RebuildFilterItems();
        }

        bool recipesChanged = _recipes.Count != _controller.RecipeIds.Count;
        if (!recipesChanged)
        {
            for (int i = 0; i < _recipes.Count; i++)
            {
                recipesChanged |= _recipes[i] != _controller.RecipeIds[i];
            }
        }

        if (recipesChanged)
        {
            _recipes.Clear();
            _recipes.AddRange(_controller.RecipeIds);
            var choices = new List<string>();
            foreach (int id in _recipes)
            {
                choices.Add(id == 0 ? "선택 해제" : "레시피 " + id);
            }

            _recipe.choices = choices;
            _recipe.index = _recipes.IndexOf(_controller.SelectedRecipe);
        }

        if (_resetDraft)
        {
            _resetDraft = false;
            _recipe.index = _recipes.IndexOf(_controller.SelectedRecipe);
            _filterMode.index = (int)_controller.Filter.Mode;
            for (int i = 0; i < _filterItems.Count; i++)
            {
                _filterItems[i].SetValueWithoutNotify(_controller.Filter.Mask.IsSet((byte)_filterTypes[i]));
            }

            _areas.Clear();
            if (storage || _controller.Type == BuildingTypeEnum.MainFacility || _controller.Type == BuildingTypeEnum.DroneStation)
            {
                _areas.Add(DroneTransportAreaEnum.GeneralStorage);
            }

            if (crafter || _controller.Type == BuildingTypeEnum.CoalGenerator || _controller.Type == BuildingTypeEnum.ResearchBuilding)
            {
                _areas.Add(DroneTransportAreaEnum.Input);
            }

            if (crafter || _controller.Type == BuildingTypeEnum.Miner)
            {
                _areas.Add(DroneTransportAreaEnum.Product);
            }

            var areaNames = new List<string>();
            foreach (var area in _areas)
            {
                areaNames.Add(area.ToString());
            }

            _area.choices = areaNames;
            _area.index = areaNames.Count > 0 ? 0 : -1;
        }

        UpdateTransportButtons();
        RenderTasks();
        RenderDrones();
    }

    private void RebuildFilterItems()
    {
        _filterList.Clear();
        _filterItems.Clear();
        _filterTypes.Clear();
        foreach (var type in _controller.ItemTypes)
        {
            var toggle = new Toggle(type.ToString());
            _filterItems.Add(toggle);
            _filterTypes.Add(type);
            _filterList.Add(toggle);
        }

        _resetDraft = true;
    }

    private void RenderTasks()
    {
        while (_taskRows.Count < _controller.Tasks.Count)
        {
            var row = new ActionRow();
            _taskRows.Add(row);
            _tasks.Add(row.Root);
        }

        for (int i = 0; i < _taskRows.Count; i++)
        {
            var row = _taskRows[i];
            row.Root.style.display = i < _controller.Tasks.Count ? DisplayStyle.Flex : DisplayStyle.None;
            if (i >= _controller.Tasks.Count)
            {
                continue;
            }

            var data = _controller.Tasks[i];
            row.Label.text = data.Text;
            row.Button.text = "취소";
            row.Click = () => _controller.CancelTask(data.Task);
        }
    }

    private void RenderDrones()
    {
        while (_droneRows.Count < _controller.Drones.Count * 2)
        {
            var row = new ActionRow();
            _droneRows.Add(row);
            _drones.Add(row.Root);
        }

        for (int i = 0; i < _droneRows.Count; i++)
        {
            var row = _droneRows[i];
            row.Root.style.display = i < _controller.Drones.Count * 2 ? DisplayStyle.Flex : DisplayStyle.None;
            if (i >= _controller.Drones.Count * 2)
            {
                continue;
            }

            var data = _controller.Drones[i / 2];
            bool secondary = i % 2 != 0;
            if (secondary && data.Stored)
            {
                row.Root.style.display = DisplayStyle.None;
                continue;
            }

            var operation = data.Stored || (!secondary && data.Docked) ? DroneStationOperation.Activate : (secondary ? DroneStationOperation.Store : DroneStationOperation.Dock);
            row.Label.text = data.Text;
            row.Button.text = operation.ToString();
            row.Click = () => _controller.RequestStation(data.Drone, operation);
        }
    }

    private void OnDisable()
    {
        if (_controller != null)
        {
            _controller.Changed -= Render;
            _controller.SelectionChanged -= ResetDraft;
        }

        if (_area != null)
        {
            _area.UnregisterValueChangedCallback(OnAreaChanged);
        }

        if (_quantity != null)
        {
            _quantity.UnregisterValueChangedCallback(OnQuantityChanged);
        }

        if (_rootObject != null)
        {
            _rootObject.SetActive(false);
            Release(_rootObject);
        }

        if (_panelSettings != null)
        {
            Release(_panelSettings);
        }

        _panel = null;
        _itemRows.Clear();
        _taskRows.Clear();
        _droneRows.Clear();
        _filterItems.Clear();
        _filterTypes.Clear();
        _recipes.Clear();
        _areas.Clear();
    }

    private static void Release(UnityEngine.Object owned)
    {
        if (Application.isPlaying)
        {
            Destroy(owned);
        }
        else
        {
            DestroyImmediate(owned);
        }
    }
}
