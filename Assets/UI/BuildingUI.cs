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
    private Label _buildingTitleLabel;
    private Label _statusLabel;
    private Label _recipeTitleLabel;
    private Label _currentRecipeLabel;
    private VisualElement _recipeContainer;
    private Label _inputTitleLabel;
    private VisualElement _inputContainer;
    private Label _outputTitleLabel;
    private VisualElement _outputContainer;
    private Label _progressTitleLabel;
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
        _buildingTitleLabel = _root.Q<Label>("building-title");
        _statusLabel = _root.Q<Label>("building-status");
        _recipeTitleLabel = _root.Q<Label>("recipe-title");
        _currentRecipeLabel = _root.Q<Label>("current-recipe");
        _recipeContainer = _root.Q<VisualElement>("recipe-container");
        _inputTitleLabel = _root.Q<Label>("input-title");
        _inputContainer = _root.Q<VisualElement>("input-container");
        _outputTitleLabel = _root.Q<Label>("output-title");
        _outputContainer = _root.Q<VisualElement>("output-container");
        _progressTitleLabel = _root.Q<Label>("progress-title");
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
        _buildingTitleLabel = null;
        _statusLabel = null;
        _recipeTitleLabel = null;
        _currentRecipeLabel = null;
        _recipeContainer = null;
        _inputTitleLabel = null;
        _inputContainer = null;
        _outputTitleLabel = null;
        _outputContainer = null;
        _progressTitleLabel = null;
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
            !IsSupportedBuilding(buildingEntity))
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
        if (!_entityManager.Exists(_selectedBuilding))
        {
            Close();
            return;
        }

        if (!TryGetConfigEntity(out Entity configEntity))
        {
            Close();
            return;
        }

        DynamicBuffer<ItemStorageLimitElement> storageLimits =
            _entityManager.GetBuffer<ItemStorageLimitElement>(configEntity, true);

        if (_entityManager.HasComponent<Storage>(_selectedBuilding))
        {
            RefreshStorage(storageLimits);
            return;
        }

        if (_entityManager.HasComponent<Crafter>(_selectedBuilding))
        {
            RefreshCrafter(configEntity, storageLimits);
            return;
        }

        if (_entityManager.HasComponent<Miner>(_selectedBuilding))
        {
            RefreshMiner(storageLimits);
            return;
        }

        Close();
    }

    private void RefreshCrafter(
        Entity configEntity,
        DynamicBuffer<ItemStorageLimitElement> storageLimits)
    {
        if (!_entityManager.HasBuffer<StoredItemElement>(_selectedBuilding) ||
            !_entityManager.HasBuffer<ProducedItemElement>(_selectedBuilding))
        {
            Close();
            return;
        }

        SetCrafterLayout();
        Crafter crafter = _entityManager.GetComponentData<Crafter>(_selectedBuilding);
        DynamicBuffer<StoredItemElement> storedItems =
            _entityManager.GetBuffer<StoredItemElement>(_selectedBuilding, true);
        DynamicBuffer<ProducedItemElement> producedItems =
            _entityManager.GetBuffer<ProducedItemElement>(_selectedBuilding, true);
        DynamicBuffer<CrafterRecipeElement> recipes =
            _entityManager.GetBuffer<CrafterRecipeElement>(configEntity, true);
        DynamicBuffer<CrafterRecipeIngredientElement> ingredients =
            _entityManager.GetBuffer<CrafterRecipeIngredientElement>(configEntity, true);

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

    private void RefreshMiner(DynamicBuffer<ItemStorageLimitElement> storageLimits)
    {
        if (!_entityManager.HasBuffer<ProducedItemElement>(_selectedBuilding))
        {
            Close();
            return;
        }

        SetMinerLayout();
        Miner miner = _entityManager.GetComponentData<Miner>(_selectedBuilding);
        DynamicBuffer<ProducedItemElement> producedItems =
            _entityManager.GetBuffer<ProducedItemElement>(_selectedBuilding, true);

        CountItems(producedItems, _producedCounts);
        UpdateSimpleInventory(
            _outputContainer,
            _producedCounts,
            storageLimits,
            "배출 대기");
        UpdateMinerStatus(miner, producedItems, storageLimits);
        UpdateMinerProgress(miner);
    }

    private void RefreshStorage(
        DynamicBuffer<ItemStorageLimitElement> storageLimits)
    {
        if (!_entityManager.HasBuffer<StoredItemElement>(_selectedBuilding))
        {
            Close();
            return;
        }

        SetStorageLayout();
        Storage storage = _entityManager.GetComponentData<Storage>(_selectedBuilding);
        DynamicBuffer<StoredItemElement> storedItems =
            _entityManager.GetBuffer<StoredItemElement>(_selectedBuilding, true);

        CountItems(storedItems, _storedCounts);
        UpdateStorageInventory(
            _storedCounts,
            storageLimits,
            storage.capacity);
        int usedSlotCount = storedItems.GetUsedSlotCount(storageLimits);

        if (storage.capacity > 0 && usedSlotCount >= storage.capacity)
        {
            SetStatus(
                $"모든 창고 칸 사용 중 ({usedSlotCount} / {storage.capacity}칸)",
                "status-waiting");
        }
        else
        {
            SetStatus(
                $"창고 칸 사용량 ({usedSlotCount} / {storage.capacity}칸)",
                "status-normal");
        }
    }

    private bool IsSupportedBuilding(Entity buildingEntity)
    {
        return _entityManager.HasComponent<Crafter>(buildingEntity) ||
               _entityManager.HasComponent<Miner>(buildingEntity) ||
               _entityManager.HasComponent<Storage>(buildingEntity);
    }

    private void SetCrafterLayout()
    {
        _buildingTitleLabel.text = "제작기";
        _inputTitleLabel.text = "투입 아이템";
        _outputTitleLabel.text = "생산 대기 아이템";
        _progressTitleLabel.text = "생산 진행도";
        _inputContainer.RemoveFromClassList("storage-grid");

        SetVisible(_recipeTitleLabel, true);
        SetVisible(_currentRecipeLabel, true);
        SetVisible(_recipeContainer, true);
        SetVisible(_inputTitleLabel, true);
        SetVisible(_inputContainer, true);
        SetVisible(_outputTitleLabel, true);
        SetVisible(_outputContainer, true);
        SetProgressVisible(true);
    }

    private void SetMinerLayout()
    {
        _buildingTitleLabel.text = "채굴기";
        _outputTitleLabel.text = "채굴 대기 아이템";
        _progressTitleLabel.text = "채굴 진행도";
        _inputContainer.RemoveFromClassList("storage-grid");

        SetVisible(_recipeTitleLabel, false);
        SetVisible(_currentRecipeLabel, false);
        SetVisible(_recipeContainer, false);
        SetVisible(_inputTitleLabel, false);
        SetVisible(_inputContainer, false);
        SetVisible(_outputTitleLabel, true);
        SetVisible(_outputContainer, true);
        SetProgressVisible(true);
    }

    private void SetStorageLayout()
    {
        _buildingTitleLabel.text = "창고";
        _inputTitleLabel.text = "보관 아이템";
        _inputContainer.AddToClassList("storage-grid");

        SetVisible(_recipeTitleLabel, false);
        SetVisible(_currentRecipeLabel, false);
        SetVisible(_recipeContainer, false);
        SetVisible(_inputTitleLabel, true);
        SetVisible(_inputContainer, true);
        SetVisible(_outputTitleLabel, false);
        SetVisible(_outputContainer, false);
        SetProgressVisible(false);
    }

    private void SetProgressVisible(bool visible)
    {
        SetVisible(_progressTitleLabel, visible);
        SetVisible(_progressBar, visible);
        SetVisible(_remainingTimeLabel, visible);
        SetVisible(_speedLabel, visible);
    }

    private static void SetVisible(VisualElement element, bool visible)
    {
        element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void UpdateSimpleInventory(
        VisualElement container,
        Dictionary<ItemTypeEnum, int> counts,
        DynamicBuffer<ItemStorageLimitElement> storageLimits,
        string detail)
    {
        container.Clear();

        for (ItemTypeEnum itemType = ItemTypeEnum.Iron_Ore;
             itemType < ItemTypeEnum.Count;
             itemType++)
        {
            if (!counts.TryGetValue(itemType, out int count))
                continue;

            AddItemRow(
                container,
                itemType,
                count,
                storageLimits.GetStorageLimit(itemType),
                detail,
                false);
        }

        if (container.childCount == 0)
            AddEmptyLabel(container);
    }

    private void UpdateStorageInventory(
        Dictionary<ItemTypeEnum, int> counts,
        DynamicBuffer<ItemStorageLimitElement> storageLimits,
        int capacity)
    {
        _inputContainer.Clear();
        int createdSlotCount = 0;

        for (ItemTypeEnum itemType = ItemTypeEnum.Iron_Ore;
             itemType < ItemTypeEnum.Count && createdSlotCount < capacity;
             itemType++)
        {
            if (!counts.TryGetValue(itemType, out int count))
                continue;

            int stackLimit = storageLimits.GetStorageLimit(itemType);
            int effectiveStackLimit = stackLimit > 0 ? stackLimit : 1;
            int remainingCount = count;

            while (remainingCount > 0 && createdSlotCount < capacity)
            {
                int stackCount = remainingCount > effectiveStackLimit
                    ? effectiveStackLimit
                    : remainingCount;
                AddStorageSlot(
                    _inputContainer,
                    itemType,
                    stackCount,
                    effectiveStackLimit);
                remainingCount -= stackCount;
                createdSlotCount++;
            }
        }

        while (createdSlotCount < capacity)
        {
            AddEmptyStorageSlot(_inputContainer);
            createdSlotCount++;
        }
    }

    private void UpdateMinerStatus(
        Miner miner,
        DynamicBuffer<ProducedItemElement> producedItems,
        DynamicBuffer<ItemStorageLimitElement> storageLimits)
    {
        if (miner.speed <= 0f)
        {
            SetStatus("작동 정지", "status-error");
            return;
        }

        for (int i = 0; i < producedItems.Length; i++)
        {
            ItemTypeEnum itemType = producedItems[i].type;
            int capacity = storageLimits.GetStorageLimit(itemType);

            if (capacity > 0 &&
                _producedCounts.TryGetValue(itemType, out int count) &&
                count >= capacity)
            {
                SetStatus("채굴 아이템 보관함이 가득 찼습니다", "status-error");
                return;
            }
        }

        SetStatus("채굴 중", "status-normal");
    }

    private void UpdateMinerProgress(Miner miner)
    {
        float progressRatio = miner.speed > 0f
            ? math.saturate(miner.timer / miner.speed)
            : 0f;
        float progressPercent = progressRatio * 100f;

        _progressBar.value = progressPercent;
        _progressBar.title = $"{progressPercent:0}%";
        _remainingTimeLabel.text = miner.speed > 0f
            ? $"다음 채굴까지: {math.max(0f, miner.speed - miner.timer):0.0}초"
            : "채굴 대기";
        _speedLabel.text = miner.speed > 0f
            ? $"채굴 속도: {miner.speed:0.##}초당 1개"
            : "채굴 속도: 정지";
    }

    private void SetStatus(string status, string statusClass)
    {
        _statusLabel.RemoveFromClassList("status-normal");
        _statusLabel.RemoveFromClassList("status-waiting");
        _statusLabel.RemoveFromClassList("status-error");
        _statusLabel.text = status;
        _statusLabel.AddToClassList(statusClass);
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

    private static void AddStorageSlot(
        VisualElement container,
        ItemTypeEnum itemType,
        int count,
        int stackLimit)
    {
        VisualElement slot = new();
        slot.AddToClassList("storage-slot");
        slot.AddToClassList("storage-slot-filled");
        slot.tooltip = $"{GetItemDisplayName(itemType)} {count} / {stackLimit}";

        Label itemLabel = new(GetItemDisplayName(itemType));
        itemLabel.AddToClassList("storage-slot-item");

        Label countLabel = new($"{count} / {stackLimit}");
        countLabel.AddToClassList("storage-slot-count");

        slot.Add(itemLabel);
        slot.Add(countLabel);
        container.Add(slot);
    }

    private static void AddEmptyStorageSlot(VisualElement container)
    {
        VisualElement slot = new();
        slot.AddToClassList("storage-slot");
        slot.AddToClassList("storage-slot-empty");

        Label emptyLabel = new("빈 칸");
        emptyLabel.AddToClassList("storage-slot-empty-label");

        slot.Add(emptyLabel);
        container.Add(slot);
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
