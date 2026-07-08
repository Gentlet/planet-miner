using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

public class InGameUI : MonoBehaviour
{
    [SerializeField]
    private BuildingPlacementController _bpc;

    private Button leftButton;
    private Button rightButton;

    private void Awake()
    {
        UIDocument uiDocument = GetComponent<UIDocument>();

        VisualElement root = uiDocument.rootVisualElement;

        leftButton = root.Q<Button>("left-button");
        rightButton = root.Q<Button>("right-button");

        leftButton.clicked += OnLeftButtonClicked;
        rightButton.clicked += OnRightButtonClicked;
    }

    private void OnDestroy()
    {
        if (leftButton != null)
            leftButton.clicked -= OnLeftButtonClicked;

        if (rightButton != null)
            rightButton.clicked -= OnRightButtonClicked;
    }

    private void OnLeftButtonClicked()
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

    private void OnRightButtonClicked()
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
}
