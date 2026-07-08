using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public static class PointerUtility
{
    private const string BlockingUiClassName = "blocking-ui";

    public static bool WasLeftClickPressed()
    {
        return Mouse.current != null &&
               Mouse.current.leftButton.wasPressedThisFrame &&
               !IsPointerOverUi();
    }

    public static bool WasRightClickPressed()
    {
        return Mouse.current != null &&
               Mouse.current.rightButton.wasPressedThisFrame &&
               !IsPointerOverUi();
    }

    public static bool IsPointerOverUi()
    {
        if (Mouse.current == null)
            return false;

        UIDocument[] uiDocuments = Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude);
        Vector2 mousePosition = Mouse.current.position.ReadValue();
        mousePosition = new(mousePosition.x, Screen.height - mousePosition.y);

        foreach (UIDocument uiDocument in uiDocuments)
        {
            if (uiDocument.rootVisualElement == null || uiDocument.rootVisualElement.panel == null)
                continue;

            IPanel panel = uiDocument.rootVisualElement.panel;
            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(panel, mousePosition);

            if (IsPointerOverBlockingUi(uiDocument, panel, panelPosition))
                return true;
        }

        return false;
    }

    private static bool IsPointerOverBlockingUi(UIDocument uiDocument, IPanel panel, Vector2 panelPosition)
    {
        VisualElement pickedElement = panel.Pick(panelPosition);

        if (IsBlockingUiElement(pickedElement))
            return true;

        bool isPointerOverBlockingUi = false;
        uiDocument.rootVisualElement.Query<VisualElement>(className: BlockingUiClassName).ForEach(element =>
        {
            if (isPointerOverBlockingUi)
                return;

            if (!CanReceivePointer(element))
                return;

            isPointerOverBlockingUi = element.worldBound.Contains(panelPosition);
        });

        return isPointerOverBlockingUi;
    }

    private static bool IsBlockingUiElement(VisualElement element)
    {
        while (element != null)
        {
            if (CanReceivePointer(element) && element.ClassListContains(BlockingUiClassName))
                return true;

            element = element.parent;
        }

        return false;
    }

    private static bool CanReceivePointer(VisualElement element)
    {
        return element.enabledInHierarchy &&
               element.pickingMode != PickingMode.Ignore &&
               element.resolvedStyle.display != DisplayStyle.None &&
               element.resolvedStyle.visibility == Visibility.Visible;
    }
}
