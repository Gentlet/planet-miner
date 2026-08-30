using System;
using UnityEngine;
using UnityEngine.UIElements;

public class DefaultUI : MonoBehaviour
{
    private Button constructionModeButton;

    public event Action ConstructionModeRequested;

    private void OnEnable()
    {
        UIDocument uiDocument = GetComponent<UIDocument>();
        constructionModeButton = uiDocument.rootVisualElement.Q<Button>("construction-mode-button");
        constructionModeButton.clicked += OnConstructionModeButtonClicked;
    }

    private void OnDisable()
    {
        if (constructionModeButton != null)
            constructionModeButton.clicked -= OnConstructionModeButtonClicked;

        constructionModeButton = null;
    }

    private void OnConstructionModeButtonClicked()
    {
        ConstructionModeRequested?.Invoke();
    }
}
