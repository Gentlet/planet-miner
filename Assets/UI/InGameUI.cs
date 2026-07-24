using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

public class InGameUI : MonoBehaviour
{
    [SerializeField]
    private BuildingPlacementController _bpc;

    private Button beltButton;
    private Button minerButton;
    private Button crafterButton;
    private Button splitterButton;
    private Button mergerButton;

    private void Awake()
    {
        UIDocument uiDocument = GetComponent<UIDocument>();

        VisualElement root = uiDocument.rootVisualElement;

        beltButton = root.Q<Button>("belt-button");
        minerButton = root.Q<Button>("miner-button");
        crafterButton = root.Q<Button>("crafter-button");
        splitterButton = root.Q<Button>("splitter-button");
        mergerButton = root.Q<Button>("merger-button");

        beltButton.clicked += OnBeltButtonClicked;
        minerButton.clicked += OnMinerButtonClicked;
        crafterButton.clicked += OnCrafterButtonClicked;
        splitterButton.clicked += OnSplitterButtonClicked;
        mergerButton.clicked += OnMergerButtonClicked;
    }

    private void OnDestroy()
    {
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
    }

    private void OnBeltButtonClicked()
    {
        _bpc.Operation = new BuildingPlacementOperation(new List<BuildingPlacementCandidate>());

        for (int i = 0; i < 1; i++)
        {
            for (int j = 0; j < 1; j++)
            {
                _bpc.Operation.Candidates.Add(
                    new BuildingPlacementCandidate(
                    BuildingTypeEnum.Belt,
                    new int2(i, j),
                    DirectionEnum.Up,
                    false)
                    );
            }
        }
    }

    private void OnMinerButtonClicked()
    {
        _bpc.Operation = new BuildingPlacementOperation(new List<BuildingPlacementCandidate>());

        for (int i = 0; i < 1; i++)
        {
            for (int j = 0; j < 1; j++)
            {
                _bpc.Operation.Candidates.Add(
                    new BuildingPlacementCandidate(
                    BuildingTypeEnum.Miner,
                    new int2(i, j),
                    DirectionEnum.Up,
                    false)
                    );
            }
        }
    }

    private void OnCrafterButtonClicked()
    {
        _bpc.Operation = new BuildingPlacementOperation(new List<BuildingPlacementCandidate>());
        _bpc.Operation.Candidates.Add(
            new BuildingPlacementCandidate(
                BuildingTypeEnum.Crafter,
                int2.zero,
                DirectionEnum.Up,
                false,
                ItemTypeEnum.Iron));
    }

    private void OnSplitterButtonClicked()
    {
        _bpc.Operation = new BuildingPlacementOperation(new List<BuildingPlacementCandidate>());
        _bpc.Operation.Candidates.Add(
            new BuildingPlacementCandidate(
                BuildingTypeEnum.Splitter,
                int2.zero,
                DirectionEnum.Up,
                false));
    }

    private void OnMergerButtonClicked()
    {
        _bpc.Operation = new BuildingPlacementOperation(new List<BuildingPlacementCandidate>());
        _bpc.Operation.Candidates.Add(
            new BuildingPlacementCandidate(
                BuildingTypeEnum.Merger,
                int2.zero,
                DirectionEnum.Up,
                false));
    }
}
