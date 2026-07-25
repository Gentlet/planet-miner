using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public class BuildingUI : MonoBehaviour
{
    private const float RefreshInterval = 0.05f;

    private readonly Dictionary<ItemTypeEnum, int> _storedCounts = new();
    private readonly Dictionary<ItemTypeEnum, int> _producedCounts = new();
    private readonly Dictionary<ItemTypeEnum, Button> _recipeButtons = new();
    private readonly HashSet<ItemTypeEnum> _listedInputTypes = new();

    private EntityManager _entityManager;
    private ChunkMapSystem _chunkMap;
    private Entity _configEntity;
    private Entity _selectedBuilding;
    private bool _selectionEnabled;
    private float _nextRefreshTime;

    private VisualElement _root;
    private VisualElement _panel;
    private UIDocument _uiDocument;
    private Label _statusLabel;
    private Label _currentRecipeLabel;
    private VisualElement _recipeContainer;
    private VisualElement _inputContainer;
    private VisualElement _outputContainer;
    private ProgressBar _progressBar;
    private Label _remainingTimeLabel;
    private Label _speedLabel;
    private Button _closeButton;

    private void Awake()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        _entityManager = world.EntityManager;
        _chunkMap = world.GetExistingSystemManaged<ChunkMapSystem>();
        _uiDocument = GetComponent<UIDocument>();
        _configEntity = Entity.Null;
        _selectedBuilding = Entity.Null;
    }

    private void OnEnable()
    {
        BindVisualTree();
        SetPanelVisible(false);
    }

    private void OnDisable()
    {
        UnbindVisualTree();
    }

    private void BindVisualTree()
    {
        UnbindVisualTree();

        _root = _uiDocument.rootVisualElement;
        _panel = _root.Q<VisualElement>("building-panel");
        _panel.pickingMode = PickingMode.Position;
        _statusLabel = _root.Q<Label>("building-status");
        _currentRecipeLabel = _root.Q<Label>("current-recipe");
        _recipeContainer = _root.Q<VisualElement>("recipe-container");
        _inputContainer = _root.Q<VisualElement>("input-container");
        _outputContainer = _root.Q<VisualElement>("output-container");
        _progressBar = _root.Q<ProgressBar>("production-progress");
        _remainingTimeLabel = _root.Q<Label>("remaining-time");
        _speedLabel = _root.Q<Label>("production-speed");
        _closeButton = _root.Q<Button>("close-button");

        _closeButton.clicked += Close;
        _recipeButtons.Clear();
    }

    private void UnbindVisualTree()
    {
        if (_closeButton != null)
            _closeButton.clicked -= Close;

        _root = null;
        _panel = null;
        _statusLabel = null;
        _currentRecipeLabel = null;
        _recipeContainer = null;
        _inputContainer = null;
        _outputContainer = null;
        _progressBar = null;
        _remainingTimeLabel = null;
        _speedLabel = null;
        _closeButton = null;
        _recipeButtons.Clear();
    }

    private void Update()
    {
        if (!_selectionEnabled)
            return;

        if (Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame &&
            IsOpen)
        {
            Close();
            return;
        }

        if (PointerUtility.WasLeftClickPressed())
            SelectBuildingUnderPointer();

        if (!IsOpen || Time.unscaledTime < _nextRefreshTime)
            return;

        _nextRefreshTime = Time.unscaledTime + RefreshInterval;
        Refresh();
    }

    public void SetSelectionEnabled(bool enabled)
    {
        _selectionEnabled = enabled;

        if (!_selectionEnabled)
            Close();
    }

    public void Close()
    {
        _selectedBuilding = Entity.Null;
        SetPanelVisible(false);
    }

    private void SelectBuildingUnderPointer()
    {
        Vector3 worldPosition = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        int2 gridCell = worldPosition.ToGridCell();

        if (!_chunkMap.TryGetBuilding(gridCell, out Entity buildingEntity) ||
            !_entityManager.Exists(buildingEntity) ||
            !_entityManager.HasComponent<Crafter>(buildingEntity))
        {
            Close();
            return;
        }

        _selectedBuilding = buildingEntity;
        _nextRefreshTime = 0f;
        SetPanelVisible(true);
        Refresh();
    }

    private void Refresh()
    {
        if (!_entityManager.Exists(_selectedBuilding) ||
            !_entityManager.HasComponent<Crafter>(_selectedBuilding) ||
            !_entityManager.HasBuffer<StoredItemElement>(_selectedBuilding) ||
            !_entityManager.HasBuffer<ProducedItemElement>(_selectedBuilding) ||
            !TryGetConfigEntity(out Entity configEntity))
        {
            Close();
            return;
        }

        Crafter crafter = _entityManager.GetComponentData<Crafter>(_selectedBuilding);
        DynamicBuffer<StoredItemElement> storedItems =
            _entityManager.GetBuffer<StoredItemElement>(_selectedBuilding, true);
        DynamicBuffer<ProducedItemElement> producedItems =
            _entityManager.GetBuffer<ProducedItemElement>(_selectedBuilding, true);
        DynamicBuffer<CrafterRecipeElement> recipes =
            _entityManager.GetBuffer<CrafterRecipeElement>(configEntity, true);
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients =
            _entityManager.GetBuffer<CrafterRecipeIngredientElement>(configEntity, true);
        DynamicBuffer<ItemStorageLimitElement> storageLimits =
            _entityManager.GetBuffer<ItemStorageLimitElement>(configEntity, true);

        CountItems(storedItems, _storedCounts);
        CountItems(producedItems, _producedCounts);

        BuildRecipeButtons(recipes, ingredients);

        bool hasRecipe = recipes.TryFindRecipe(
            crafter.selectedItemType,
            out CrafterRecipeElement selectedRecipe);

        UpdateRecipeSelection(crafter.selectedItemType);
        UpdateStatus(crafter, hasRecipe, selectedRecipe, ingredients);
        UpdateInventory(
            crafter,
            hasRecipe,
            selectedRecipe,
            ingredients,
            storageLimits);
        UpdateProgress(crafter, hasRecipe, selectedRecipe);
    }

    private bool TryGetConfigEntity(out Entity configEntity)
    {
        if (_configEntity != Entity.Null && _entityManager.Exists(_configEntity))
        {
            configEntity = _configEntity;
            return true;
        }

        using EntityQuery configQuery =
            _entityManager.CreateEntityQuery(ComponentType.ReadOnly<CrafterConfig>());

        if (configQuery.IsEmptyIgnoreFilter)
        {
            configEntity = Entity.Null;
            return false;
        }

        _configEntity = configQuery.GetSingletonEntity();
        configEntity = _configEntity;
        return true;
    }

    private void BuildRecipeButtons(
        DynamicBuffer<CrafterRecipeElement> recipes,
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients)
    {
        if (_recipeButtons.Count == recipes.Length)
            return;

        _recipeContainer.Clear();
        _recipeButtons.Clear();

        for (int recipeIndex = 0; recipeIndex < recipes.Length; recipeIndex++)
        {
            CrafterRecipeElement recipe = recipes[recipeIndex];
            ItemTypeEnum outputItemType = recipe.outputItemType;
            string ingredientText = GetIngredientText(ingredients, recipe.id);

            Button recipeButton = new(() => RequestRecipeChange(outputItemType))
            {
                text = $"{GetItemDisplayName(outputItemType)}\n{ingredientText}  ·  {recipe.craftTime:0.##}초"
            };

            recipeButton.AddToClassList("recipe-button");
            _recipeContainer.Add(recipeButton);
            _recipeButtons.Add(outputItemType, recipeButton);
        }
    }

    private void RequestRecipeChange(ItemTypeEnum outputItemType)
    {
        if (!_entityManager.Exists(_selectedBuilding))
            return;

        Crafter crafter = _entityManager.GetComponentData<Crafter>(_selectedBuilding);

        if (crafter.selectedItemType == outputItemType)
            return;

        Entity requestEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(requestEntity, new CrafterRecipeChangeRequest
        {
            crafterEntity = _selectedBuilding,
            selectedItemType = outputItemType
        });
    }

    private void UpdateRecipeSelection(ItemTypeEnum selectedItemType)
    {
        foreach (KeyValuePair<ItemTypeEnum, Button> pair in _recipeButtons)
            pair.Value.EnableInClassList("recipe-button-selected", pair.Key == selectedItemType);
    }

    private void UpdateStatus(
        Crafter crafter,
        bool hasRecipe,
        CrafterRecipeElement recipe,
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients)
    {
        _statusLabel.RemoveFromClassList("status-normal");
        _statusLabel.RemoveFromClassList("status-waiting");
        _statusLabel.RemoveFromClassList("status-error");

        string status;
        string statusClass;

        switch (crafter.state)
        {
            case CrafterStateEnum.NoRecipe:
                status = "레시피를 선택하세요";
                statusClass = "status-waiting";
                break;
            case CrafterStateEnum.Crafting:
                status = "제작 중";
                statusClass = "status-normal";
                break;
            case CrafterStateEnum.WaitingForOutput:
                status = "생산 아이템 보관함이 가득 찼습니다";
                statusClass = "status-error";
                break;
            case CrafterStateEnum.WaitingForExceptionItem:
                status = "새 레시피에 사용할 수 없는 재료를 꺼내야 합니다";
                statusClass = "status-error";
                break;
            default:
                if (crafter.speed <= 0f)
                {
                    status = "작동 정지";
                    statusClass = "status-error";
                }
                else if (!hasRecipe || !HasRequiredIngredients(recipe, ingredients))
                {
                    status = "재료 대기 중";
                    statusClass = "status-waiting";
                }
                else
                {
                    status = "생산 준비 중";
                    statusClass = "status-normal";
                }
                break;
        }

        _statusLabel.text = status;
        _statusLabel.AddToClassList(statusClass);

        _currentRecipeLabel.text = hasRecipe
            ? $"현재 레시피: {GetItemDisplayName(recipe.outputItemType)}"
            : "현재 레시피: 없음";
    }

    private bool HasRequiredIngredients(
        CrafterRecipeElement recipe,
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients)
    {
        for (int i = 0; i < ingredients.Length; i++)
        {
            CrafterRecipeIngredientElement ingredient = ingredients[i];

            if (ingredient.recipeId != recipe.id)
                continue;
            if (!_storedCounts.TryGetValue(ingredient.itemType, out int count) ||
                count < ingredient.amount)
                return false;
        }

        return true;
    }

    private void UpdateInventory(
        Crafter crafter,
        bool hasRecipe,
        CrafterRecipeElement recipe,
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients,
        DynamicBuffer<ItemStorageLimitElement> storageLimits)
    {
        _inputContainer.Clear();
        _outputContainer.Clear();
        _listedInputTypes.Clear();

        if (hasRecipe)
        {
            for (int i = 0; i < ingredients.Length; i++)
            {
                CrafterRecipeIngredientElement ingredient = ingredients[i];

                if (ingredient.recipeId != recipe.id)
                    continue;

                _storedCounts.TryGetValue(ingredient.itemType, out int count);
                int capacity = storageLimits.GetStorageLimit(ingredient.itemType);
                AddItemRow(
                    _inputContainer,
                    ingredient.itemType,
                    count,
                    capacity,
                    $"필요 {ingredient.amount}개",
                    false);
                _listedInputTypes.Add(ingredient.itemType);
            }
        }

        for (ItemTypeEnum itemType = ItemTypeEnum.Iron_Ore;
             itemType < ItemTypeEnum.Count;
             itemType++)
        {
            if (!_storedCounts.TryGetValue(itemType, out int count) ||
                _listedInputTypes.Contains(itemType))
                continue;

            int capacity = storageLimits.GetStorageLimit(itemType);
            AddItemRow(
                _inputContainer,
                itemType,
                count,
                capacity,
                hasRecipe ? "현재 레시피에서 사용 불가" : "보관 중",
                hasRecipe);
        }

        if (_inputContainer.childCount == 0)
            AddEmptyLabel(_inputContainer);

        for (ItemTypeEnum itemType = ItemTypeEnum.Iron_Ore;
             itemType < ItemTypeEnum.Count;
             itemType++)
        {
            if (!_producedCounts.TryGetValue(itemType, out int count))
                continue;

            int capacity = storageLimits.GetStorageLimit(itemType);
            AddItemRow(_outputContainer, itemType, count, capacity, "배출 대기", false);
        }

        if (_outputContainer.childCount == 0)
            AddEmptyLabel(_outputContainer);
    }

    private void UpdateProgress(
        Crafter crafter,
        bool hasRecipe,
        CrafterRecipeElement recipe)
    {
        float progressRatio = 0f;
        float requiredTime = 0f;

        if (hasRecipe && crafter.speed > 0f)
        {
            requiredTime = recipe.GetCraftTime(crafter.speed);

            if (crafter.state == CrafterStateEnum.WaitingForOutput)
                progressRatio = 1f;
            else if (crafter.state == CrafterStateEnum.Crafting)
                progressRatio = math.saturate(crafter.progress / requiredTime);
        }

        float progressPercent = progressRatio * 100f;
        _progressBar.value = progressPercent;
        _progressBar.title = $"{progressPercent:0}%";

        if (crafter.state == CrafterStateEnum.Crafting)
            _remainingTimeLabel.text =
                $"남은 시간: {math.max(0f, requiredTime - crafter.progress):0.0}초";
        else if (crafter.state == CrafterStateEnum.WaitingForOutput)
            _remainingTimeLabel.text = "제작 완료 · 배출 공간 대기";
        else
            _remainingTimeLabel.text = "생산 대기";

        if (!hasRecipe)
        {
            _speedLabel.text = $"생산 속도: ×{crafter.speed:0.##}";
            return;
        }

        if (crafter.speed <= 0f)
        {
            _speedLabel.text = "생산 속도: 정지";
            return;
        }

        float craftsPerMinute = 60f / requiredTime;
        _speedLabel.text =
            $"생산 속도: ×{crafter.speed:0.##}  ·  1회 {requiredTime:0.##}초  ·  분당 {craftsPerMinute:0.#}개";
    }

    private static void CountItems(
        DynamicBuffer<StoredItemElement> items,
        Dictionary<ItemTypeEnum, int> counts)
    {
        counts.Clear();

        for (int i = 0; i < items.Length; i++)
        {
            ItemTypeEnum itemType = items[i].type;
            counts.TryGetValue(itemType, out int count);
            counts[itemType] = count + 1;
        }
    }

    private static void CountItems(
        DynamicBuffer<ProducedItemElement> items,
        Dictionary<ItemTypeEnum, int> counts)
    {
        counts.Clear();

        for (int i = 0; i < items.Length; i++)
        {
            ItemTypeEnum itemType = items[i].type;
            counts.TryGetValue(itemType, out int count);
            counts[itemType] = count + 1;
        }
    }

    private static string GetIngredientText(
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients,
        int recipeId)
    {
        string result = string.Empty;

        for (int i = 0; i < ingredients.Length; i++)
        {
            CrafterRecipeIngredientElement ingredient = ingredients[i];

            if (ingredient.recipeId != recipeId)
                continue;
            if (result.Length > 0)
                result += ", ";

            result += $"{GetItemDisplayName(ingredient.itemType)} ×{ingredient.amount}";
        }

        return result;
    }

    private static void AddItemRow(
        VisualElement container,
        ItemTypeEnum itemType,
        int count,
        int capacity,
        string detail,
        bool isException)
    {
        VisualElement row = new();
        row.AddToClassList("item-row");

        if (isException)
            row.AddToClassList("item-row-exception");

        Label nameLabel = new(GetItemDisplayName(itemType));
        nameLabel.AddToClassList("item-name");

        Label countLabel = new($"{count} / {capacity}");
        countLabel.AddToClassList("item-count");

        Label detailLabel = new(detail);
        detailLabel.AddToClassList("item-detail");

        row.Add(nameLabel);
        row.Add(countLabel);
        row.Add(detailLabel);
        container.Add(row);
    }

    private static void AddEmptyLabel(VisualElement container)
    {
        Label emptyLabel = new("없음");
        emptyLabel.AddToClassList("empty-label");
        container.Add(emptyLabel);
    }

    private static string GetItemDisplayName(ItemTypeEnum itemType)
    {
        return itemType switch
        {
            ItemTypeEnum.Iron_Ore => "철 광석",
            ItemTypeEnum.Copper_Ore => "구리 광석",
            ItemTypeEnum.Coal => "석탄",
            ItemTypeEnum.Stone => "돌",
            ItemTypeEnum.Iron => "철",
            ItemTypeEnum.Copper => "구리",
            ItemTypeEnum.Iron_Stick => "철 막대",
            ItemTypeEnum.Copper_Stick => "구리 막대",
            _ => "없음"
        };
    }

    private void SetPanelVisible(bool visible)
    {
        if (_panel != null)
            _panel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private bool IsOpen => _selectedBuilding != Entity.Null;
}
