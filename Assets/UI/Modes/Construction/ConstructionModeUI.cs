using System;
using System.Collections.Generic;
using Unity.Entities;
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
    private Button researchBuildingButton;
    private Label copyStatusLabel;
    private static readonly BuildingTypeEnum[] BuildingButtonOrder =
    {
        BuildingTypeEnum.Belt,
        BuildingTypeEnum.Splitter,
        BuildingTypeEnum.Merger,
        BuildingTypeEnum.Miner,
        BuildingTypeEnum.Crafter,
        BuildingTypeEnum.Storage,
        BuildingTypeEnum.PowerPole,
        BuildingTypeEnum.CoalGenerator,
        BuildingTypeEnum.DroneStation,
        BuildingTypeEnum.ResearchBuilding
    };
    private readonly Button[] buildingButtons = new Button[
        BuildingButtonOrder.Length];
    private readonly Action[] buildingButtonActions = new Action[
        BuildingButtonOrder.Length];
    private readonly bool[] buildingButtonVisibility = new bool[
        BuildingButtonOrder.Length];
    private readonly Button[] taskPriorityButtons = new Button[
        DroneTaskPriorityUtility.MaximumNormalPriority];
    private readonly Action[] taskPriorityActions = new Action[
        DroneTaskPriorityUtility.MaximumNormalPriority];
    private BuildingTypeEnum? _selectedBuildingType;
    private EntityManager _entityManager;
    private EntityQuery _buildingUnlockQuery;
    private bool _hasBuildingUnlockQuery;

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
        researchBuildingButton = root.Q<Button>("research-building-button");
        copyStatusLabel = root.Q<Label>("copy-status-label");

        BindBuildingButtonOrder();
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
        researchBuildingButton.clicked += OnResearchBuildingButtonClicked;
        _bpc.PlacementSelectionCleared += OnPlacementSelectionCleared;

        ClearBuildingSelection();
        RefreshBuildingUnlocks();
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

        RefreshBuildingUnlocks();
        HandleBuildingHotkeys(keyboard);

        if (copyStatusLabel != null)
        {
            string copyStatus = _bpc.InteractionStatus;
            copyStatusLabel.text = copyStatus;
            copyStatusLabel.style.display =
                string.IsNullOrEmpty(copyStatus) ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }

    private void HandleBuildingHotkeys(Keyboard keyboard)
    {
        if (keyboard == null)
            return;

        int visibleHotkeySlot = GetPressedBuildingHotkeySlot(keyboard);

        if (visibleHotkeySlot < 0)
            return;

        int buttonIndex = ConstructionBuildingHotkeyUtility
            .GetButtonIndexForVisibleSlot(
                buildingButtonVisibility,
                visibleHotkeySlot);

        if (buttonIndex < 0)
            return;

        buildingButtonActions[buttonIndex]?.Invoke();
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

        if (researchBuildingButton != null)
            researchBuildingButton.clicked -= OnResearchBuildingButtonClicked;

        ClearBuildingButtonOrder();
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
        researchBuildingButton = null;
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

    private void OnResearchBuildingButtonClicked()
    {
        ToggleBuildingSelection(
            BuildingTypeEnum.ResearchBuilding,
            researchBuildingButton);
    }

    private void BindBuildingButtonOrder()
    {
        buildingButtons[0] = beltButton;
        buildingButtons[1] = splitterButton;
        buildingButtons[2] = mergerButton;
        buildingButtons[3] = minerButton;
        buildingButtons[4] = crafterButton;
        buildingButtons[5] = storageButton;
        buildingButtons[6] = powerPoleButton;
        buildingButtons[7] = coalGeneratorButton;
        buildingButtons[8] = droneStationButton;
        buildingButtons[9] = researchBuildingButton;

        buildingButtonActions[0] = OnBeltButtonClicked;
        buildingButtonActions[1] = OnSplitterButtonClicked;
        buildingButtonActions[2] = OnMergerButtonClicked;
        buildingButtonActions[3] = OnMinerButtonClicked;
        buildingButtonActions[4] = OnCrafterButtonClicked;
        buildingButtonActions[5] = OnStorageButtonClicked;
        buildingButtonActions[6] = OnPowerPoleButtonClicked;
        buildingButtonActions[7] = OnCoalGeneratorButtonClicked;
        buildingButtonActions[8] = OnDroneStationButtonClicked;
        buildingButtonActions[9] = OnResearchBuildingButtonClicked;
    }

    private void ClearBuildingButtonOrder()
    {
        for (int i = 0; i < buildingButtons.Length; i++)
        {
            buildingButtons[i] = null;
            buildingButtonActions[i] = null;
            buildingButtonVisibility[i] = false;
        }
    }

    private static int GetPressedBuildingHotkeySlot(Keyboard keyboard)
    {
        if (keyboard.digit1Key.wasPressedThisFrame)
            return 0;
        if (keyboard.digit2Key.wasPressedThisFrame)
            return 1;
        if (keyboard.digit3Key.wasPressedThisFrame)
            return 2;
        if (keyboard.digit4Key.wasPressedThisFrame)
            return 3;
        if (keyboard.digit5Key.wasPressedThisFrame)
            return 4;
        if (keyboard.digit6Key.wasPressedThisFrame)
            return 5;
        if (keyboard.digit7Key.wasPressedThisFrame)
            return 6;
        if (keyboard.digit8Key.wasPressedThisFrame)
            return 7;
        if (keyboard.digit9Key.wasPressedThisFrame)
            return 8;
        if (keyboard.digit0Key.wasPressedThisFrame)
            return 9;

        return -1;
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
        if (button == null)
            return;

        if (button.resolvedStyle.display == DisplayStyle.None)
            return;

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
        researchBuildingButton?.RemoveFromClassList(SelectedButtonClass);
    }

    private bool EnsureBuildingUnlockQuery()
    {
        World world = World.DefaultGameObjectInjectionWorld;

        if (world == null || !world.IsCreated)
            return false;

        if (!_hasBuildingUnlockQuery || _entityManager != world.EntityManager)
        {
            _entityManager = world.EntityManager;
            _buildingUnlockQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ResearchConfig>(),
                ComponentType.ReadOnly<BuildingUnlockElement>());
            _hasBuildingUnlockQuery = true;
        }

        return true;
    }

    private void RefreshBuildingUnlocks()
    {
        if (!EnsureBuildingUnlockQuery())
            return;

        if (_buildingUnlockQuery.IsEmptyIgnoreFilter)
            return;

        DynamicBuffer<BuildingUnlockElement> unlocks = _buildingUnlockQuery
            .GetSingletonBuffer<BuildingUnlockElement>(true);
        for (int i = 0; i < BuildingButtonOrder.Length; i++)
        {
            bool isVisible = unlocks.IsBuildingUnlocked(
                BuildingButtonOrder[i]);
            buildingButtonVisibility[i] = isVisible;
            SetBuildingButtonVisible(buildingButtons[i], isVisible);
        }

        if (_selectedBuildingType.HasValue &&
            !unlocks.IsBuildingUnlocked(_selectedBuildingType.Value))
            ClearBuildingSelection();
    }

    private static void SetBuildingButtonVisible(
        Button button,
        bool isVisible)
    {
        if (button == null)
            return;

        button.style.display = isVisible
            ? DisplayStyle.Flex
            : DisplayStyle.None;
    }
}
