using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

public partial class BuildingUI
{
    public bool BindVisualTree(VisualElement rootElement = null)
    {
        UnbindVisualTree();

        VisualElement root = rootElement;
        if (root == null)
        {
            if (_uiDocument == null)
                _uiDocument = GetComponent<UIDocument>();

            if (_uiDocument == null || _uiDocument.rootVisualElement == null)
            {
                Debug.LogError($"[BuildingUI] 바인딩 실패: UIDocument 또는 rootVisualElement가 유효하지 않습니다. (GameObject: {gameObject.name})");
                return false;
            }

            root = _uiDocument.rootVisualElement;
        }

        _root = root;
        List<string> missingElements = new();

        _panel = QueryRequired<VisualElement>(_root, "building-panel", missingElements);
        _buildingTitleLabel = QueryRequired<Label>(_root, "building-title", missingElements);
        _statusLabel = QueryRequired<Label>(_root, "building-status", missingElements);
        _powerContainer = QueryRequired<VisualElement>(_root, "power-container", missingElements);
        _powerGridLabel = QueryRequired<Label>(_root, "power-grid", missingElements);
        _powerPrimaryLabel = QueryRequired<Label>(_root, "power-primary", missingElements);
        _powerSecondaryLabel = QueryRequired<Label>(_root, "power-secondary", missingElements);
        _powerTertiaryLabel = QueryRequired<Label>(_root, "power-tertiary", missingElements);
        _powerQuaternaryLabel = QueryRequired<Label>(_root, "power-quaternary", missingElements);
        _droneContainer = QueryRequired<VisualElement>(_root, "drone-container", missingElements);
        _droneBatteryProgress = QueryRequired<ProgressBar>(_root, "drone-battery-progress", missingElements);
        _droneBatteryLabel = QueryRequired<Label>(_root, "drone-battery", missingElements);
        _droneCargoLabel = QueryRequired<Label>(_root, "drone-cargo", missingElements);
        _droneCapabilityLabel = QueryRequired<Label>(_root, "drone-capability", missingElements);
        _droneTaskLabel = QueryRequired<Label>(_root, "drone-task", missingElements);
        _droneAssignmentLabel = QueryRequired<Label>(_root, "drone-assignment", missingElements);
        _beltContainer = QueryRequired<VisualElement>(_root, "belt-container", missingElements);
        _beltMaximumSpeedLabel = QueryRequired<Label>(_root, "belt-maximum-speed", missingElements);
        _beltItemContainer = QueryRequired<VisualElement>(_root, "belt-item-container", missingElements);
        _recipeTitleLabel = QueryRequired<Label>(_root, "recipe-title", missingElements);
        _currentRecipeLabel = QueryRequired<Label>(_root, "current-recipe", missingElements);
        _recipeContainer = QueryRequired<VisualElement>(_root, "recipe-container", missingElements);
        _inputTitleLabel = QueryRequired<Label>(_root, "input-title", missingElements);
        _inputContainer = QueryRequired<VisualElement>(_root, "input-container", missingElements);
        _dedicatedDroneStorageTitleLabel = QueryRequired<Label>(
            _root, "dedicated-drone-storage-title", missingElements);
        _dedicatedDroneStorageContainer = QueryRequired<VisualElement>(
            _root, "dedicated-drone-storage-container", missingElements);
        _outputTitleLabel = QueryRequired<Label>(_root, "output-title", missingElements);
        _outputContainer = QueryRequired<VisualElement>(_root, "output-container", missingElements);
        _progressTitleLabel = QueryRequired<Label>(_root, "progress-title", missingElements);
        _progressBar = QueryRequired<ProgressBar>(_root, "production-progress", missingElements);
        _remainingTimeLabel = QueryRequired<Label>(_root, "remaining-time", missingElements);
        _speedLabel = QueryRequired<Label>(_root, "production-speed", missingElements);
        _speedReasonLabel = QueryRequired<Label>(_root, "production-speed-reason", missingElements);
        _droneTransferContainer = QueryRequired<VisualElement>(
            _root, "drone-transfer-container", missingElements);
        _droneItemField = QueryRequired<DropdownField>(_root, "drone-item", missingElements);
        _droneQuantityField = QueryRequired<IntegerField>(_root, "drone-quantity", missingElements);
        _droneInsertButton = QueryRequired<Button>(_root, "drone-insert-button", missingElements);
        _droneRemoveButton = QueryRequired<Button>(_root, "drone-remove-button", missingElements);
        _closeButton = QueryRequired<Button>(_root, "close-button", missingElements);

        if (missingElements.Count > 0)
        {
            string missingList = string.Join(", ", missingElements);
            Debug.LogError($"[BuildingUI] 필수 UXML 요소 바인딩 실패 ({missingElements.Count}개 누락): [{missingList}] (GameObject: {gameObject.name})");
            UnbindVisualTree();
            return false;
        }

        _panel.pickingMode = PickingMode.Position;
        BuildDroneItemChoices();
        _droneInsertButton.clicked += RequestDroneItemInsertion;
        _droneRemoveButton.clicked += RequestDroneItemRemoval;
        _closeButton.clicked += Close;
        _recipeButtons.Clear();

        IsBound = true;
        return true;
    }

    private static T QueryRequired<T>(VisualElement root, string name, List<string> missingElements)
        where T : VisualElement
    {
        T element = root.Q<T>(name);
        if (element == null)
            missingElements.Add($"{name} ({typeof(T).Name})");

        return element;
    }

    public void UnbindVisualTree()
    {
        if (_droneInsertButton != null)
            _droneInsertButton.clicked -= RequestDroneItemInsertion;

        if (_droneRemoveButton != null)
            _droneRemoveButton.clicked -= RequestDroneItemRemoval;

        if (_closeButton != null)
            _closeButton.clicked -= Close;

        if (_inputContainer != null)
            _inputContainer.Clear();
        if (_outputContainer != null)
            _outputContainer.Clear();
        if (_dedicatedDroneStorageContainer != null)
            _dedicatedDroneStorageContainer.Clear();
        if (_beltItemContainer != null)
            _beltItemContainer.Clear();
        if (_recipeContainer != null)
            _recipeContainer.Clear();

        _root = null;
        _panel = null;
        _buildingTitleLabel = null;
        _statusLabel = null;
        _powerContainer = null;
        _powerGridLabel = null;
        _powerPrimaryLabel = null;
        _powerSecondaryLabel = null;
        _powerTertiaryLabel = null;
        _powerQuaternaryLabel = null;
        _droneContainer = null;
        _droneBatteryProgress = null;
        _droneBatteryLabel = null;
        _droneCargoLabel = null;
        _droneCapabilityLabel = null;
        _droneTaskLabel = null;
        _droneAssignmentLabel = null;
        _beltContainer = null;
        _beltMaximumSpeedLabel = null;
        _beltItemContainer = null;
        _recipeTitleLabel = null;
        _currentRecipeLabel = null;
        _recipeContainer = null;
        _inputTitleLabel = null;
        _inputContainer = null;
        _dedicatedDroneStorageTitleLabel = null;
        _dedicatedDroneStorageContainer = null;
        _outputTitleLabel = null;
        _outputContainer = null;
        _progressTitleLabel = null;
        _progressBar = null;
        _remainingTimeLabel = null;
        _speedLabel = null;
        _speedReasonLabel = null;
        _droneTransferContainer = null;
        _droneItemField = null;
        _droneQuantityField = null;
        _droneInsertButton = null;
        _droneRemoveButton = null;
        _closeButton = null;
        _transferItemTypes.Clear();
        _transferItemNames.Clear();
        _recipeButtons.Clear();
        IsBound = false;
    }

    private void SetCrafterLayout()
    {
        _buildingTitleLabel.text = "제작기";
        _inputTitleLabel.text = "투입 아이템";
        _outputTitleLabel.text = "생산 대기 아이템";
        _progressTitleLabel.text = "생산 진행도";
        _inputContainer.RemoveFromClassList("storage-grid");
        SetVisible(_powerContainer, true);
        SetVisible(_droneContainer, false);
        SetVisible(_beltContainer, false);

        SetVisible(_recipeTitleLabel, true);
        SetVisible(_currentRecipeLabel, true);
        SetVisible(_recipeContainer, true);
        SetVisible(_inputTitleLabel, true);
        SetVisible(_inputContainer, true);
        SetDedicatedDroneStorageVisible(false);
        SetVisible(_outputTitleLabel, true);
        SetVisible(_outputContainer, true);
        SetVisible(_droneTransferContainer, true);
        SetProgressVisible(true);
    }

    private void SetMinerLayout()
    {
        _buildingTitleLabel.text = "채굴기";
        _outputTitleLabel.text = "채굴 대기 아이템";
        _progressTitleLabel.text = "채굴 진행도";
        _inputContainer.RemoveFromClassList("storage-grid");
        SetVisible(_powerContainer, true);
        SetVisible(_droneContainer, false);
        SetVisible(_beltContainer, false);

        SetVisible(_recipeTitleLabel, false);
        SetVisible(_currentRecipeLabel, false);
        SetVisible(_recipeContainer, false);
        SetVisible(_inputTitleLabel, false);
        SetVisible(_inputContainer, false);
        SetDedicatedDroneStorageVisible(false);
        SetVisible(_outputTitleLabel, true);
        SetVisible(_outputContainer, true);
        SetVisible(_droneTransferContainer, false);
        SetProgressVisible(true);
    }

    private void SetStorageLayout(bool hasDedicatedDroneStorage)
    {
        _buildingTitleLabel.text = hasDedicatedDroneStorage
            ? "드론 정거장"
            : "창고";
        _inputTitleLabel.text = "보관 아이템";
        _inputContainer.AddToClassList("storage-grid");
        SetVisible(_powerContainer, false);
        SetVisible(_droneContainer, false);
        SetVisible(_beltContainer, false);

        SetVisible(_recipeTitleLabel, false);
        SetVisible(_currentRecipeLabel, false);
        SetVisible(_recipeContainer, false);
        SetVisible(_inputTitleLabel, true);
        SetVisible(_inputContainer, true);
        SetDedicatedDroneStorageVisible(hasDedicatedDroneStorage);
        SetVisible(_outputTitleLabel, false);
        SetVisible(_outputContainer, false);
        SetVisible(_droneTransferContainer, true);
        SetProgressVisible(false);
    }

    private void SetPowerOnlyLayout(string title)
    {
        _buildingTitleLabel.text = title;
        _inputContainer.RemoveFromClassList("storage-grid");

        SetVisible(_powerContainer, true);
        SetVisible(_droneContainer, false);
        SetVisible(_beltContainer, false);
        SetVisible(_recipeTitleLabel, false);
        SetVisible(_currentRecipeLabel, false);
        SetVisible(_recipeContainer, false);
        SetVisible(_inputTitleLabel, false);
        SetVisible(_inputContainer, false);
        SetDedicatedDroneStorageVisible(false);
        SetVisible(_outputTitleLabel, false);
        SetVisible(_outputContainer, false);
        SetVisible(_droneTransferContainer, false);
        SetProgressVisible(false);
    }

    private void SetMainFacilityLayout()
    {
        _buildingTitleLabel.text = "메인스테이션";
        _inputTitleLabel.text = "보관 아이템";
        _inputContainer.AddToClassList("storage-grid");

        SetVisible(_powerContainer, true);
        SetVisible(_droneContainer, false);
        SetVisible(_beltContainer, false);
        SetVisible(_recipeTitleLabel, false);
        SetVisible(_currentRecipeLabel, false);
        SetVisible(_recipeContainer, false);
        SetVisible(_inputTitleLabel, true);
        SetVisible(_inputContainer, true);
        SetDedicatedDroneStorageVisible(true);
        SetVisible(_outputTitleLabel, false);
        SetVisible(_outputContainer, false);
        SetVisible(_droneTransferContainer, true);
        SetProgressVisible(false);
    }

    private void SetDroneLayout()
    {
        _buildingTitleLabel.text = "드론";
        _inputContainer.RemoveFromClassList("storage-grid");

        SetVisible(_powerContainer, false);
        SetVisible(_droneContainer, true);
        SetVisible(_beltContainer, false);
        SetVisible(_recipeTitleLabel, false);
        SetVisible(_currentRecipeLabel, false);
        SetVisible(_recipeContainer, false);
        SetVisible(_inputTitleLabel, false);
        SetVisible(_inputContainer, false);
        SetDedicatedDroneStorageVisible(false);
        SetVisible(_outputTitleLabel, false);
        SetVisible(_outputContainer, false);
        SetVisible(_droneTransferContainer, false);
        SetProgressVisible(false);
    }

    private void SetConstructionSiteLayout(string buildingName)
    {
        _buildingTitleLabel.text = $"{buildingName} 건설";
        _inputTitleLabel.text = "필요 자재";
        _progressTitleLabel.text = "자재 납품률";
        _inputContainer.RemoveFromClassList("storage-grid");

        SetVisible(_powerContainer, false);
        SetVisible(_droneContainer, false);
        SetVisible(_beltContainer, false);
        SetVisible(_recipeTitleLabel, false);
        SetVisible(_currentRecipeLabel, false);
        SetVisible(_recipeContainer, false);
        SetVisible(_inputTitleLabel, true);
        SetVisible(_inputContainer, true);
        SetDedicatedDroneStorageVisible(false);
        SetVisible(_outputTitleLabel, false);
        SetVisible(_outputContainer, false);
        SetVisible(_droneTransferContainer, false);
        SetProgressVisible(true);
        SetVisible(_speedReasonLabel, false);
    }

    private void SetStaticBuildingLayout(string buildingName)
    {
        _buildingTitleLabel.text = buildingName;
        _inputContainer.RemoveFromClassList("storage-grid");

        SetVisible(_powerContainer, false);
        SetVisible(_droneContainer, false);
        SetVisible(_beltContainer, false);
        SetVisible(_recipeTitleLabel, false);
        SetVisible(_currentRecipeLabel, false);
        SetVisible(_recipeContainer, false);
        SetVisible(_inputTitleLabel, false);
        SetVisible(_inputContainer, false);
        SetDedicatedDroneStorageVisible(false);
        SetVisible(_outputTitleLabel, false);
        SetVisible(_outputContainer, false);
        SetVisible(_droneTransferContainer, false);
        SetProgressVisible(false);
    }

    private void SetBeltLayout()
    {
        _buildingTitleLabel.text = "벨트";
        _inputContainer.RemoveFromClassList("storage-grid");

        SetVisible(_powerContainer, false);
        SetVisible(_droneContainer, false);
        SetVisible(_beltContainer, true);
        SetVisible(_recipeTitleLabel, false);
        SetVisible(_currentRecipeLabel, false);
        SetVisible(_recipeContainer, false);
        SetVisible(_inputTitleLabel, false);
        SetVisible(_inputContainer, false);
        SetDedicatedDroneStorageVisible(false);
        SetVisible(_outputTitleLabel, false);
        SetVisible(_outputContainer, false);
        SetVisible(_droneTransferContainer, false);
        SetProgressVisible(false);
    }

    private void SetProgressVisible(bool visible)
    {
        SetVisible(_progressTitleLabel, visible);
        SetVisible(_progressBar, visible);
        SetVisible(_remainingTimeLabel, visible);
        SetVisible(_speedLabel, visible);
        SetVisible(_speedReasonLabel, visible);
    }

    private void SetDedicatedDroneStorageVisible(bool visible)
    {
        SetVisible(_dedicatedDroneStorageTitleLabel, visible);
        SetVisible(_dedicatedDroneStorageContainer, visible);
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
        int rowIndex = 0;

        for (ItemTypeEnum itemType = ItemTypeEnum.Iron_Ore;
             itemType < ItemTypeEnum.Count;
             itemType++)
        {
            if (!counts.TryGetValue(itemType, out int count))
                continue;

            SetItemRow(
                container,
                rowIndex,
                itemType,
                count,
                storageLimits.GetStorageLimit(itemType),
                detail,
                false);
            rowIndex++;
        }

        TrimItemRowsAndSetEmptyState(container, rowIndex);
    }

    private void SetStatus(string status, string statusClass)
    {
        _statusLabel.RemoveFromClassList("status-normal");
        _statusLabel.RemoveFromClassList("status-waiting");
        _statusLabel.RemoveFromClassList("status-error");
        _statusLabel.text = status;
        _statusLabel.AddToClassList(statusClass);
    }

    private static void CountItems<T>(
        DynamicBuffer<T> items,
        Dictionary<ItemTypeEnum, int> counts,
        System.Func<T, ItemTypeEnum> getItemType)
        where T : unmanaged, IBufferElementData
    {
        counts.Clear();

        for (int i = 0; i < items.Length; i++)
        {
            ItemTypeEnum itemType = getItemType(items[i]);
            counts.TryGetValue(itemType, out int count);
            counts[itemType] = count + 1;
        }
    }

    public static void SetItemRow(
        VisualElement container,
        int index,
        ItemTypeEnum itemType,
        int count,
        int capacity,
        string detail,
        bool isException)
    {
        VisualElement row = GetOrCreateItemRow(container, index);
        row.EnableInClassList("item-row-exception", isException);

        Label nameLabel = (Label)row[0];
        Label countLabel = (Label)row[1];
        Label detailLabel = (Label)row[2];

        nameLabel.text = GetItemDisplayName(itemType);
        countLabel.text = $"{count} / {capacity}";
        detailLabel.text = detail;
    }

    public static VisualElement GetOrCreateItemRow(VisualElement container, int index)
    {
        int currentRowCount = 0;
        VisualElement targetRow = null;

        for (int i = 0; i < container.childCount; i++)
        {
            VisualElement child = container[i];
            if (!child.ClassListContains("item-row"))
                continue;

            if (currentRowCount == index)
            {
                targetRow = child;
                break;
            }
            currentRowCount++;
        }

        if (targetRow == null)
        {
            targetRow = new VisualElement();
            targetRow.AddToClassList("item-row");

            Label nameLabel = new();
            nameLabel.AddToClassList("item-name");
            targetRow.Add(nameLabel);

            Label countLabel = new();
            countLabel.AddToClassList("item-count");
            targetRow.Add(countLabel);

            Label detailLabel = new();
            detailLabel.AddToClassList("item-detail");
            targetRow.Add(detailLabel);

            container.Add(targetRow);
        }

        targetRow.style.display = DisplayStyle.Flex;
        return targetRow;
    }

    public static void TrimItemRowsAndSetEmptyState(VisualElement container, int activeCount)
    {
        int currentRowCount = 0;
        for (int i = 0; i < container.childCount; i++)
        {
            VisualElement child = container[i];
            if (child.ClassListContains("item-row"))
            {
                if (currentRowCount >= activeCount)
                    child.style.display = DisplayStyle.None;
                else
                    child.style.display = DisplayStyle.Flex;

                currentRowCount++;
            }
            else if (!child.ClassListContains("empty-label"))
            {
                child.style.display = DisplayStyle.None;
            }
        }

        Label emptyLabel = GetOrCreateEmptyLabel(container);
        emptyLabel.style.display = activeCount == 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public static void SetStorageSlot(
        VisualElement container,
        int index,
        ItemTypeEnum itemType,
        int count,
        int stackLimit)
    {
        VisualElement slot = GetOrCreateStorageSlot(container, index);
        slot.RemoveFromClassList("storage-slot-empty");
        slot.AddToClassList("storage-slot-filled");
        slot.tooltip = $"{GetItemDisplayName(itemType)} {count} / {stackLimit}";

        Label itemLabel = (Label)slot[0];
        Label countLabel = (Label)slot[1];

        itemLabel.RemoveFromClassList("storage-slot-empty-label");
        itemLabel.AddToClassList("storage-slot-item");
        itemLabel.text = GetItemDisplayName(itemType);

        countLabel.style.display = DisplayStyle.Flex;
        countLabel.text = $"{count} / {stackLimit}";
    }

    public static void SetEmptyStorageSlot(
        VisualElement container,
        int index)
    {
        VisualElement slot = GetOrCreateStorageSlot(container, index);
        slot.RemoveFromClassList("storage-slot-filled");
        slot.AddToClassList("storage-slot-empty");
        slot.tooltip = string.Empty;

        Label itemLabel = (Label)slot[0];
        Label countLabel = (Label)slot[1];

        itemLabel.RemoveFromClassList("storage-slot-item");
        itemLabel.AddToClassList("storage-slot-empty-label");
        itemLabel.text = "빈 칸";

        countLabel.style.display = DisplayStyle.None;
        countLabel.text = string.Empty;
    }

    public static VisualElement GetOrCreateStorageSlot(VisualElement container, int index)
    {
        int currentSlotCount = 0;
        VisualElement targetSlot = null;

        for (int i = 0; i < container.childCount; i++)
        {
            VisualElement child = container[i];
            if (!child.ClassListContains("storage-slot"))
                continue;

            if (currentSlotCount == index)
            {
                targetSlot = child;
                break;
            }
            currentSlotCount++;
        }

        if (targetSlot == null)
        {
            targetSlot = new VisualElement();
            targetSlot.AddToClassList("storage-slot");

            Label itemLabel = new();
            targetSlot.Add(itemLabel);

            Label countLabel = new();
            countLabel.AddToClassList("storage-slot-count");
            targetSlot.Add(countLabel);

            container.Add(targetSlot);
        }

        targetSlot.style.display = DisplayStyle.Flex;
        return targetSlot;
    }

    public static void TrimStorageSlots(VisualElement container, int activeCount)
    {
        int currentSlotCount = 0;
        for (int i = 0; i < container.childCount; i++)
        {
            VisualElement child = container[i];
            if (child.ClassListContains("storage-slot"))
            {
                if (currentSlotCount >= activeCount)
                    child.style.display = DisplayStyle.None;
                else
                    child.style.display = DisplayStyle.Flex;

                currentSlotCount++;
            }
            else
            {
                child.style.display = DisplayStyle.None;
            }
        }
    }

    public static Label GetOrCreateEmptyLabel(VisualElement container)
    {
        Label emptyLabel = null;
        for (int i = 0; i < container.childCount; i++)
        {
            if (container[i] is Label label && label.ClassListContains("empty-label"))
            {
                emptyLabel = label;
                break;
            }
        }

        if (emptyLabel == null)
        {
            emptyLabel = new Label("없음");
            emptyLabel.AddToClassList("empty-label");
            container.Add(emptyLabel);
        }

        return emptyLabel;
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
            ItemTypeEnum.Drone => "드론",
            _ => "없음"
        };
    }

    private void SetPanelVisible(bool visible)
    {
        if (_panel != null)
            _panel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
