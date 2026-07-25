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
        SetUIVisible(_defaultUI, false);
        SetUIVisible(_constructionModeUI, true);
        _buildingPlacementController.SetEnable(true);
    }

    public void ExitConstructionMode()
    {
        _buildingPlacementController.SetEnable(false);
        SetUIVisible(_constructionModeUI, false);
        SetUIVisible(_defaultUI, true);
    }

    private static void SetUIVisible(MonoBehaviour ui, bool visible)
    {
        ui.enabled = visible;
        ui.GetComponent<UIDocument>().rootVisualElement.style.display =
            visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
