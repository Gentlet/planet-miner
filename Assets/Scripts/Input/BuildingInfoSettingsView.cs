using System.Collections.Generic;
using UnityEngine.UIElements;

internal sealed class BuildingInfoSettingsView
{
    private readonly BuildingInfoController _controller;
    private readonly BuildingInfoItemCatalog _catalog;
    private readonly Button _recipeButton;
    private readonly Button _filterButton;
    private readonly VisualElement _recipeEditor;
    private readonly VisualElement _filterEditor;
    private readonly VisualElement _recipeGrid;
    private readonly VisualElement _recipeDetails;
    private readonly VisualElement _filterGrid;
    private readonly Label _recipeTitle;
    private readonly Label _recipeTime;
    private readonly Label _filterHelp;
    private readonly DropdownField _filterMode;
    private readonly Button _applyRecipe;
    private readonly Button _clearRecipe;
    private readonly BuildingInfoInventoryView _recipeInput;
    private readonly BuildingInfoInventoryView _recipeOutput;
    private readonly List<BuildingInfoSlot> _input = new();
    private readonly List<BuildingInfoSlot> _output = new();
    private readonly List<BuildingInfoRecipeChoice> _recipeChoices = new();
    private readonly List<BuildingInfoItemTile> _recipeTiles = new();
    private readonly List<ItemTypeEnum> _filterTypes = new();
    private readonly List<BuildingInfoItemTile> _filterTiles = new();
    private bool _recipeOpen;
    private bool _filterOpen;
    private int _recipeDraft;
    private FixedBitSet _filterDraft;

    public VisualElement Root { get; } = new();

    public BuildingInfoSettingsView(BuildingInfoController controller, BuildingInfoItemCatalog catalog)
    {
        _controller = controller;
        _catalog = catalog;
        _recipeButton = new Button(ToggleRecipe) { text = "레시피 선택", name = "open-recipe" };
        Root.Add(_recipeButton);
        _recipeEditor = new VisualElement { name = "recipe-editor" };
        Root.Add(_recipeEditor);
        _recipeGrid = BuildingInfoItemTile.NewGrid("recipe-options");
        _recipeEditor.Add(_recipeGrid);
        _recipeDetails = new VisualElement();
        _recipeEditor.Add(_recipeDetails);
        _recipeTitle = new Label { name = "recipe-preview-name" };
        _recipeDetails.Add(_recipeTitle);
        _recipeInput = new BuildingInfoInventoryView(catalog, "소모 재료", "recipe-ingredients");
        _recipeDetails.Add(_recipeInput.Root);
        _recipeTime = new Label { name = "recipe-time" };
        _recipeDetails.Add(_recipeTime);
        _recipeOutput = new BuildingInfoInventoryView(catalog, "결과품", "recipe-outputs");
        _recipeDetails.Add(_recipeOutput.Root);
        _clearRecipe = new Button(() => SelectRecipe(0)) { text = "선택 해제", name = "clear-recipe" };
        _applyRecipe = new Button(ApplyRecipe) { text = "적용", name = "apply-recipe" };
        _recipeEditor.Add(BuildActions(_clearRecipe, new Button(CloseEditors) { text = "취소", name = "cancel-recipe" }, _applyRecipe));

        _filterButton = new Button(ToggleFilter) { text = "입고 필터", name = "open-filter" };
        Root.Add(_filterButton);
        _filterEditor = new VisualElement { name = "filter-editor" };
        Root.Add(_filterEditor);
        _filterMode = new DropdownField("필터 방식", new List<string> { "전체 허용", "허용 목록", "차단 목록" }, 0)
        {
            name = "filter-mode"
        };
        _filterMode.RegisterValueChangedCallback(_ => RenderFilterSelection());
        _filterEditor.Add(_filterMode);
        _filterGrid = BuildingInfoItemTile.NewGrid("filter-options");
        _filterEditor.Add(_filterGrid);
        _filterHelp = new Label("좌클릭 선택 / 우클릭 해제");
        _filterEditor.Add(_filterHelp);
        _filterEditor.Add(BuildActions(new Button(CloseEditors) { text = "취소", name = "cancel-filter" },
            new Button(ApplyFilter) { text = "적용", name = "apply-filter" }));
        CloseEditors();
    }

    public void Render()
    {
        bool crafter = _controller.HasCrafting;
        bool filter = _controller.HasEditableFilter;
        Root.style.display = crafter || filter ? DisplayStyle.Flex : DisplayStyle.None;
        _recipeButton.style.display = crafter ? DisplayStyle.Flex : DisplayStyle.None;
        _recipeButton.SetEnabled(_controller.CanEditRecipe);
        _filterButton.style.display = filter ? DisplayStyle.Flex : DisplayStyle.None;
        if (_recipeOpen && !_controller.CanEditRecipe || _filterOpen && !filter)
        {
            CloseEditors();
        }

        if (crafter)
        {
            int current = _controller.Recipes.FindIndex(choice => choice.Id == _controller.SelectedRecipe);
            _recipeButton.text = current >= 0
                ? "레시피 선택  /  " + BuildingInfoItemCatalog.GetName(_controller.Recipes[current].Output)
                : "레시피 선택";
        }

        if (_recipeOpen && UpdateRecipeChoices())
        {
            SelectRecipe(_recipeDraft);
        }
        if (_filterOpen && UpdateFilterChoices())
        {
            RenderFilterSelection();
        }
    }

    public void CloseEditors()
    {
        _recipeOpen = false;
        _filterOpen = false;
        _recipeEditor.style.display = DisplayStyle.None;
        _filterEditor.style.display = DisplayStyle.None;
    }

    private static VisualElement BuildActions(params Button[] buttons)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        foreach (var button in buttons)
        {
            row.Add(button);
        }
        return row;
    }

    private void ToggleRecipe()
    {
        bool open = !_recipeOpen && _controller.CanEditRecipe;
        CloseEditors();
        if (!open)
        {
            return;
        }

        _recipeOpen = true;
        _recipeEditor.style.display = DisplayStyle.Flex;
        UpdateRecipeChoices();
        SelectRecipe(_controller.SelectedRecipe);
    }

    private bool UpdateRecipeChoices()
    {
        var live = _controller.Recipes;
        if (live.Count == _recipeChoices.Count)
        {
            int index = 0;
            while (index < live.Count && live[index].Id == _recipeChoices[index].Id)
            {
                index++;
            }
            if (index == live.Count)
            {
                return false;
            }
        }

        _recipeChoices.Clear();
        _recipeChoices.AddRange(live);
        _recipeTiles.Clear();
        _recipeGrid.Clear();
        foreach (var choice in _recipeChoices)
        {
            var tile = new BuildingInfoItemTile(_catalog, () => SelectRecipe(choice.Id)) { name = $"recipe-{choice.Id}" };
            tile.Show(choice.Output, null);
            _recipeTiles.Add(tile);
            if (_recipeChoices.Exists(other => other.Id != choice.Id && other.Output == choice.Output))
            {
                _recipeGrid.Add(BuildRecipeVariant(tile, choice));
            }
            else
            {
                _recipeGrid.Add(tile);
            }
        }
        return true;
    }

    private VisualElement BuildRecipeVariant(BuildingInfoItemTile tile, in BuildingInfoRecipeChoice choice)
    {
        _controller.ReadRecipeStacks(choice.Id, _input, _output);
        string ingredients = FormatRecipeStacks(_input);
        string results = FormatRecipeStacks(_output);
        string description = $"재료 {ingredients} / {choice.CraftTime:0.##}초\n결과 {results}";
        var variant = new VisualElement();
        variant.style.maxWidth = 152;
        variant.style.marginRight = 5;
        var detail = new Label(description);
        detail.style.whiteSpace = WhiteSpace.Normal;
        detail.pickingMode = PickingMode.Ignore;
        variant.Add(tile);
        variant.Add(detail);
        tile.tooltip = $"{BuildingInfoItemCatalog.GetName(choice.Output)} / {description}";
        return variant;
    }

    private static string FormatRecipeStacks(List<BuildingInfoSlot> slots)
    {
        return slots.Count == 0 ? "없음" :
            string.Join(" + ", slots.ConvertAll(slot => $"{BuildingInfoItemCatalog.GetName(slot.Type)} {slot.Quantity}"));
    }

    private void SelectRecipe(int id)
    {
        _recipeDraft = id;
        int index = _recipeChoices.FindIndex(choice => choice.Id == id);
        for (int tile = 0; tile < _recipeTiles.Count; tile++)
        {
            _recipeTiles[tile].SetSelected(tile == index);
        }

        _clearRecipe.SetEnabled(id != 0);
        _applyRecipe.SetEnabled(id != _controller.SelectedRecipe && (id == 0 || index >= 0));
        _recipeDetails.style.display = DisplayStyle.Flex;
        if (index < 0)
        {
            _recipeTitle.text = "레시피 미선택";
            _recipeTime.style.display = DisplayStyle.None;
            _recipeInput.Root.style.display = DisplayStyle.None;
            _recipeOutput.Root.style.display = DisplayStyle.None;
            return;
        }

        var recipe = _recipeChoices[index];
        _recipeTitle.text = BuildingInfoItemCatalog.GetName(recipe.Output);
        _recipeTime.text = $"◷  제작시간  {recipe.CraftTime:0.##}초";
        _recipeTime.style.display = DisplayStyle.Flex;
        _controller.ReadRecipeStacks(id, _input, _output);
        _recipeInput.Render(_input);
        _recipeOutput.Render(_output);
    }

    private void ApplyRecipe()
    {
        _controller.SubmitSettings(new PlannedBuildingSettings { HasRecipe = true, RecipeId = _recipeDraft });
        CloseEditors();
    }

    private void ToggleFilter()
    {
        bool open = !_filterOpen && _controller.HasEditableFilter;
        CloseEditors();
        if (!open)
        {
            return;
        }

        _filterOpen = true;
        _filterEditor.style.display = DisplayStyle.Flex;
        _filterDraft = _controller.Filter.Mask;
        _filterMode.index = (int)_controller.Filter.Mode;
        UpdateFilterChoices();
        RenderFilterSelection();
    }

    private bool UpdateFilterChoices()
    {
        if (_filterTypes.Count == _controller.ItemTypes.Count)
        {
            int index = 0;
            while (index < _filterTypes.Count && _filterTypes[index] == _controller.ItemTypes[index])
            {
                index++;
            }
            if (index == _filterTypes.Count)
            {
                return false;
            }
        }

        _filterTypes.Clear();
        _filterTypes.AddRange(_controller.ItemTypes);
        _filterTiles.Clear();
        _filterGrid.Clear();
        foreach (ItemTypeEnum type in _filterTypes)
        {
            var tile = new BuildingInfoItemTile(_catalog, () => SetFilterItem(type, true)) { name = $"filter-{type}" };
            tile.Show(type, null);
            tile.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 1)
                {
                    evt.StopPropagation();
                }
            });
            tile.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button == 1)
                {
                    evt.StopPropagation();
                    SetFilterItem(type, false);
                }
            });
            _filterTiles.Add(tile);
            _filterGrid.Add(tile);
        }
        return true;
    }

    private void SetFilterItem(ItemTypeEnum type, bool selected)
    {
        if (_filterMode.index == (int)StorageFilterMode.AllowAll)
        {
            return;
        }
        _filterDraft.Set((byte)type, selected);
        RenderFilterSelection();
    }

    private void RenderFilterSelection()
    {
        bool usesList = _filterMode.index != (int)StorageFilterMode.AllowAll;
        _filterGrid.SetEnabled(usesList);
        _filterHelp.style.display = usesList ? DisplayStyle.Flex : DisplayStyle.None;
        for (int index = 0; index < _filterTiles.Count; index++)
        {
            _filterTiles[index].SetSelected(usesList && _filterDraft.IsSet((byte)_filterTypes[index]));
        }
    }

    private void ApplyFilter()
    {
        var filter = new StorageFilter((StorageFilterMode)_filterMode.index) { Mask = _filterDraft };
        _controller.SubmitSettings(new PlannedBuildingSettings { HasStorageFilter = true, Filter = filter });
        CloseEditors();
    }
}
