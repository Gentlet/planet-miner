using UnityEngine;
using UnityEngine.UIElements;

public class GameUIController : MonoBehaviour
{
    [SerializeField]
    private DefaultUI _defaultUI;

    [SerializeField]
    private ConstructionModeUI _constructionModeUI;

    [SerializeField]
    private BuildingPlacementController _buildingPlacementController;

    [SerializeField]
    private BuildingUI _buildingUI;

    private void Awake()
    {
        _defaultUI.ConstructionModeRequested += EnterConstructionMode;
        _constructionModeUI.ExitRequested += ExitConstructionMode;
    }

    private void Start()
    {
        ExitConstructionMode();
    }

    private void OnDestroy()
    {
        _defaultUI.ConstructionModeRequested -= EnterConstructionMode;
        _constructionModeUI.ExitRequested -= ExitConstructionMode;
    }

    public void EnterConstructionMode()
    {
        _buildingUI.SetSelectionEnabled(false);
        SetUIVisible(_defaultUI, false);
        SetUIVisible(_constructionModeUI, true);
        _buildingPlacementController.SetEnable(true);
    }

    public void ExitConstructionMode()
    {
        _buildingPlacementController.SetEnable(false);
        SetUIVisible(_constructionModeUI, false);
        SetUIVisible(_defaultUI, true);
        _buildingUI.SetSelectionEnabled(true);
    }

    private static void SetUIVisible(MonoBehaviour ui, bool visible)
    {
        ui.enabled = visible;
        ui.GetComponent<UIDocument>().rootVisualElement.style.display =
            visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
