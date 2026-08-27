using System.Collections.Generic;
using Unity.Entities;
using UnityEngine.UIElements;

public partial class BuildingUI
{
    private void BindVisualTree()
    {
        UnbindVisualTree();

        _root = _uiDocument.rootVisualElement;
        _panel = _root.Q<VisualElement>("building-panel");
        _panel.pickingMode = PickingMode.Position;
        _buildingTitleLabel = _root.Q<Label>("building-title");
        _statusLabel = _root.Q<Label>("building-status");
        _powerContainer = _root.Q<VisualElement>("power-container");
        _powerGridLabel = _root.Q<Label>("power-grid");
        _powerPrimaryLabel = _root.Q<Label>("power-primary");
        _powerSecondaryLabel = _root.Q<Label>("power-secondary");
        _powerTertiaryLabel = _root.Q<Label>("power-tertiary");
        _powerQuaternaryLabel = _root.Q<Label>("power-quaternary");
        _droneContainer = _root.Q<VisualElement>("drone-container");
        _droneBatteryProgress = _root.Q<ProgressBar>("drone-battery-progress");
        _droneBatteryLabel = _root.Q<Label>("drone-battery");
        _droneCargoLabel = _root.Q<Label>("drone-cargo");
        _droneCapabilityLabel = _root.Q<Label>("drone-capability");
        _droneTaskLabel = _root.Q<Label>("drone-task");
        _droneAssignmentLabel = _root.Q<Label>("drone-assignment");
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
        _speedReasonLabel = _root.Q<Label>("production-speed-reason");
        _droneTransferContainer = _root.Q<VisualElement>(
            "drone-transfer-container");
        _droneItemField = _root.Q<DropdownField>("drone-item");
        _droneQuantityField = _root.Q<IntegerField>("drone-quantity");
        _droneInsertButton = _root.Q<Button>("drone-insert-button");
        _droneRemoveButton = _root.Q<Button>("drone-remove-button");
        _closeButton = _root.Q<Button>("close-button");

        BuildDroneItemChoices();
        _droneInsertButton.clicked += RequestDroneItemInsertion;
        _droneRemoveButton.clicked += RequestDroneItemRemoval;
        _closeButton.clicked += Close;
        _recipeButtons.Clear();
    }

    private void UnbindVisualTree()
    {
        if (_droneInsertButton != null)
            _droneInsertButton.clicked -= RequestDroneItemInsertion;

        if (_droneRemoveButton != null)
            _droneRemoveButton.clicked -= RequestDroneItemRemoval;

        if (_closeButton != null)
            _closeButton.clicked -= Close;

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

        SetVisible(_recipeTitleLabel, true);
        SetVisible(_currentRecipeLabel, true);
        SetVisible(_recipeContainer, true);
        SetVisible(_inputTitleLabel, true);
        SetVisible(_inputContainer, true);
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

        SetVisible(_recipeTitleLabel, false);
        SetVisible(_currentRecipeLabel, false);
        SetVisible(_recipeContainer, false);
        SetVisible(_inputTitleLabel, false);
        SetVisible(_inputContainer, false);
        SetVisible(_outputTitleLabel, true);
        SetVisible(_outputContainer, true);
        SetVisible(_droneTransferContainer, false);
        SetProgressVisible(true);
    }

    private void SetStorageLayout()
    {
        _buildingTitleLabel.text = "창고";
        _inputTitleLabel.text = "보관 아이템";
        _inputContainer.AddToClassList("storage-grid");
        SetVisible(_powerContainer, false);
        SetVisible(_droneContainer, false);

        SetVisible(_recipeTitleLabel, false);
        SetVisible(_currentRecipeLabel, false);
        SetVisible(_recipeContainer, false);
        SetVisible(_inputTitleLabel, true);
        SetVisible(_inputContainer, true);
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
        SetVisible(_recipeTitleLabel, false);
        SetVisible(_currentRecipeLabel, false);
        SetVisible(_recipeContainer, false);
        SetVisible(_inputTitleLabel, false);
        SetVisible(_inputContainer, false);
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
        SetVisible(_recipeTitleLabel, false);
        SetVisible(_currentRecipeLabel, false);
        SetVisible(_recipeContainer, false);
        SetVisible(_inputTitleLabel, true);
        SetVisible(_inputContainer, true);
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
        SetVisible(_recipeTitleLabel, false);
        SetVisible(_currentRecipeLabel, false);
        SetVisible(_recipeContainer, false);
        SetVisible(_inputTitleLabel, false);
        SetVisible(_inputContainer, false);
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
        SetVisible(_recipeTitleLabel, false);
        SetVisible(_currentRecipeLabel, false);
        SetVisible(_recipeContainer, false);
        SetVisible(_inputTitleLabel, true);
        SetVisible(_inputContainer, true);
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
        SetVisible(_recipeTitleLabel, false);
        SetVisible(_currentRecipeLabel, false);
        SetVisible(_recipeContainer, false);
        SetVisible(_inputTitleLabel, false);
        SetVisible(_inputContainer, false);
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
