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
    private Label copyStatusLabel;
    private BuildingTypeEnum? _selectedBuildingType;

    private const string SelectedButtonClass = "selected";

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
        copyStatusLabel = root.Q<Label>("copy-status-label");

        beltButton.clicked += OnBeltButtonClicked;
        minerButton.clicked += OnMinerButtonClicked;
        crafterButton.clicked += OnCrafterButtonClicked;
        splitterButton.clicked += OnSplitterButtonClicked;
        mergerButton.clicked += OnMergerButtonClicked;
        storageButton.clicked += OnStorageButtonClicked;
        powerPoleButton.clicked += OnPowerPoleButtonClicked;
        coalGeneratorButton.clicked += OnCoalGeneratorButtonClicked;
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

        beltButton = null;
        minerButton = null;
        crafterButton = null;
        splitterButton = null;
        mergerButton = null;
        storageButton = null;
        powerPoleButton = null;
        coalGeneratorButton = null;
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
    }
}
