using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public class ConstructionModeUI : MonoBehaviour
{
    [SerializeField]
    private BuildingPlacementController _bpc;

    private Button beltButton;
    private Button minerButton;
    private Button crafterButton;
    private Button splitterButton;
    private Button mergerButton;
    private Button storageButton;
    private Button powerPoleButton;
    private Button coalGeneratorButton;
    private Button droneStationButton;
    private Label copyStatusLabel;
    private readonly Button[] taskPriorityButtons = new Button[
        DroneTaskPriorityUtility.MaximumNormalPriority];
    private readonly Action[] taskPriorityActions = new Action[
        DroneTaskPriorityUtility.MaximumNormalPriority];
    private BuildingTypeEnum? _selectedBuildingType;

    private const string SelectedButtonClass = "selected";
    private const int DefaultNormalTaskPriority = 5;

    public event Action ExitRequested;

    private void OnEnable()
    {
        UIDocument uiDocument = GetComponent<UIDocument>();

        VisualElement root = uiDocument.rootVisualElement;

        beltButton = root.Q<Button>("belt-button");
        minerButton = root.Q<Button>("miner-button");
        crafterButton = root.Q<Button>("crafter-button");
        splitterButton = root.Q<Button>("splitter-button");
        mergerButton = root.Q<Button>("merger-button");
        storageButton = root.Q<Button>("storage-button");
        powerPoleButton = root.Q<Button>("power-pole-button");
        coalGeneratorButton = root.Q<Button>("coal-generator-button");
        droneStationButton = root.Q<Button>("drone-station-button");
        copyStatusLabel = root.Q<Label>("copy-status-label");

        BindTaskPriorityButtons(root);

        beltButton.clicked += OnBeltButtonClicked;
        minerButton.clicked += OnMinerButtonClicked;
        crafterButton.clicked += OnCrafterButtonClicked;
        splitterButton.clicked += OnSplitterButtonClicked;
        mergerButton.clicked += OnMergerButtonClicked;
        storageButton.clicked += OnStorageButtonClicked;
        powerPoleButton.clicked += OnPowerPoleButtonClicked;
        coalGeneratorButton.clicked += OnCoalGeneratorButtonClicked;
        droneStationButton.clicked += OnDroneStationButtonClicked;
        _bpc.PlacementSelectionCleared += OnPlacementSelectionCleared;

        ClearBuildingSelection();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            if (!_bpc.TryCancelCopyMode())
                ExitRequested?.Invoke();

            return;
        }

        HandleBuildingHotkeys(keyboard);

        if (copyStatusLabel != null)
        {
            string copyStatus = _bpc.CopyStatus;
            copyStatusLabel.text = copyStatus;
            copyStatusLabel.style.display =
                string.IsNullOrEmpty(copyStatus) ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }

    private void HandleBuildingHotkeys(Keyboard keyboard)
    {
        if (keyboard == null)
            return;

        if (keyboard.digit1Key.wasPressedThisFrame)
            OnBeltButtonClicked();
        else if (keyboard.digit2Key.wasPressedThisFrame)
            OnSplitterButtonClicked();
        else if (keyboard.digit3Key.wasPressedThisFrame)
            OnMergerButtonClicked();
        else if (keyboard.digit4Key.wasPressedThisFrame)
            OnMinerButtonClicked();
        else if (keyboard.digit5Key.wasPressedThisFrame)
            OnCrafterButtonClicked();
        else if (keyboard.digit6Key.wasPressedThisFrame)
            OnStorageButtonClicked();
        else if (keyboard.digit7Key.wasPressedThisFrame)
            OnPowerPoleButtonClicked();
        else if (keyboard.digit8Key.wasPressedThisFrame)
            OnCoalGeneratorButtonClicked();
        else if (keyboard.digit9Key.wasPressedThisFrame)
            OnDroneStationButtonClicked();
    }

    private void OnDisable()
    {
        _bpc.PlacementSelectionCleared -= OnPlacementSelectionCleared;
        RemoveSelectedButtonStyle();

        if (beltButton != null)
            beltButton.clicked -= OnBeltButtonClicked;

        if (minerButton != null)
            minerButton.clicked -= OnMinerButtonClicked;

        if (crafterButton != null)
            crafterButton.clicked -= OnCrafterButtonClicked;

        if (splitterButton != null)
            splitterButton.clicked -= OnSplitterButtonClicked;

        if (mergerButton != null)
            mergerButton.clicked -= OnMergerButtonClicked;

        if (storageButton != null)
            storageButton.clicked -= OnStorageButtonClicked;

        if (powerPoleButton != null)
            powerPoleButton.clicked -= OnPowerPoleButtonClicked;

        if (coalGeneratorButton != null)
            coalGeneratorButton.clicked -= OnCoalGeneratorButtonClicked;

        if (droneStationButton != null)
            droneStationButton.clicked -= OnDroneStationButtonClicked;

        UnbindTaskPriorityButtons();

        beltButton = null;
        minerButton = null;
        crafterButton = null;
        splitterButton = null;
        mergerButton = null;
        storageButton = null;
        powerPoleButton = null;
        coalGeneratorButton = null;
        droneStationButton = null;
        copyStatusLabel = null;
    }

    private void OnBeltButtonClicked()
    {
        ToggleBuildingSelection(BuildingTypeEnum.Belt, beltButton);
    }

    private void OnMinerButtonClicked()
    {
        ToggleBuildingSelection(BuildingTypeEnum.Miner, minerButton);
    }

    private void OnCrafterButtonClicked()
    {
        ToggleBuildingSelection(BuildingTypeEnum.Crafter, crafterButton);
    }

    private void OnSplitterButtonClicked()
    {
        ToggleBuildingSelection(BuildingTypeEnum.Splitter, splitterButton);
    }

    private void OnMergerButtonClicked()
    {
        ToggleBuildingSelection(BuildingTypeEnum.Merger, mergerButton);
    }

    private void OnStorageButtonClicked()
    {
        ToggleBuildingSelection(BuildingTypeEnum.Storage, storageButton);
    }

    private void OnPowerPoleButtonClicked()
    {
        ToggleBuildingSelection(BuildingTypeEnum.PowerPole, powerPoleButton);
    }

    private void OnCoalGeneratorButtonClicked()
    {
        ToggleBuildingSelection(
            BuildingTypeEnum.CoalGenerator,
            coalGeneratorButton);
    }

    private void OnDroneStationButtonClicked()
    {
        ToggleBuildingSelection(
            BuildingTypeEnum.DroneStation,
            droneStationButton);
    }

    private void BindTaskPriorityButtons(VisualElement root)
    {
        for (int priority = DroneTaskPriorityUtility.MinimumNormalPriority;
             priority <= DroneTaskPriorityUtility.MaximumNormalPriority;
             priority++)
        {
            int buttonIndex = priority -
                              DroneTaskPriorityUtility.MinimumNormalPriority;
            int selectedPriority = priority;
            Button button = root.Q<Button>($"task-priority-{priority}");
            Action action = () => SetTaskPriority(selectedPriority);

            taskPriorityButtons[buttonIndex] = button;
            taskPriorityActions[buttonIndex] = action;
            button.clicked += action;
        }

        SetTaskPriority(DefaultNormalTaskPriority);
    }

    private void UnbindTaskPriorityButtons()
    {
        for (int i = 0; i < taskPriorityButtons.Length; i++)
        {
            Button button = taskPriorityButtons[i];
            Action action = taskPriorityActions[i];

            if (button != null && action != null)
                button.clicked -= action;

            taskPriorityButtons[i] = null;
            taskPriorityActions[i] = null;
        }
    }

    private void SetTaskPriority(int normalPriority)
    {
        _bpc.SetNormalTaskPriority(normalPriority);

        for (int priority = DroneTaskPriorityUtility.MinimumNormalPriority;
             priority <= DroneTaskPriorityUtility.MaximumNormalPriority;
             priority++)
        {
            int buttonIndex = priority -
                              DroneTaskPriorityUtility.MinimumNormalPriority;
            Button button = taskPriorityButtons[buttonIndex];

            if (button == null)
                continue;

            if (priority == normalPriority)
                button.AddToClassList(SelectedButtonClass);
            else
                button.RemoveFromClassList(SelectedButtonClass);
        }
    }

    private void ToggleBuildingSelection(BuildingTypeEnum type, Button button)
    {
        if (_selectedBuildingType == type)
        {
            ClearBuildingSelection();
            return;
        }

        RemoveSelectedButtonStyle();
        _selectedBuildingType = type;
        button.AddToClassList(SelectedButtonClass);

        List<BuildingPlacementCandidate> candidates = new()
        {
            new BuildingPlacementCandidate(
                type,
                int2.zero,
                DirectionEnum.Up,
                false,
                type == BuildingTypeEnum.Crafter
                    ? ItemTypeEnum.None
                    : default)
        };

        _bpc.Operation = new BuildingPlacementOperation(candidates);
    }

    private void ClearBuildingSelection()
    {
        RemoveSelectedButtonStyle();
        _selectedBuildingType = null;
        _bpc.ClearOperation();
    }

    private void OnPlacementSelectionCleared()
    {
        RemoveSelectedButtonStyle();
        _selectedBuildingType = null;
    }

    private void RemoveSelectedButtonStyle()
    {
        beltButton?.RemoveFromClassList(SelectedButtonClass);
        minerButton?.RemoveFromClassList(SelectedButtonClass);
        crafterButton?.RemoveFromClassList(SelectedButtonClass);
        splitterButton?.RemoveFromClassList(SelectedButtonClass);
        mergerButton?.RemoveFromClassList(SelectedButtonClass);
        storageButton?.RemoveFromClassList(SelectedButtonClass);
        powerPoleButton?.RemoveFromClassList(SelectedButtonClass);
        coalGeneratorButton?.RemoveFromClassList(SelectedButtonClass);
        droneStationButton?.RemoveFromClassList(SelectedButtonClass);
    }
}
