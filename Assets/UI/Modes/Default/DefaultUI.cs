using System;
using UnityEngine;
using UnityEngine.UIElements;

public partial class DefaultUI : MonoBehaviour
{
    private Button constructionModeButton;
    private Button researchButton;

    public event Action ConstructionModeRequested;

    private void OnEnable()
    {
        UIDocument uiDocument = GetComponent<UIDocument>();
        constructionModeButton = uiDocument.rootVisualElement.Q<Button>("construction-mode-button");
        researchButton = uiDocument.rootVisualElement.Q<Button>("research-button");
        constructionModeButton.clicked += OnConstructionModeButtonClicked;
        researchButton.clicked += OpenResearchPanel;
        BindResearchPanel(uiDocument.rootVisualElement);
    }

    private void OnDisable()
    {
        if (constructionModeButton != null)
            constructionModeButton.clicked -= OnConstructionModeButtonClicked;

        if (researchButton != null)
            researchButton.clicked -= OpenResearchPanel;

        constructionModeButton = null;
        researchButton = null;
        UnbindResearchPanel();
    }

    private void OnConstructionModeButtonClicked()
    {
        ConstructionModeRequested?.Invoke();
    }

    private void Update()
    {
        UpdateResearchPanel();
    }
}
