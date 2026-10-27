using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

public readonly struct WorldInputUiState
{
    public readonly bool BlocksPointer;
    public readonly bool BlocksAllInput;

    public WorldInputUiState(bool blocksPointer, bool blocksAllInput)
    {
        BlocksPointer = blocksPointer;
        BlocksAllInput = blocksAllInput;
    }
}

/// <summary>각 런타임 UIDocument가 입력 컨트롤러에 등록하는 UI 차단 판정자다. UI 상태는 원본 트리에서 읽는다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public sealed class WorldInputUiBlocker : MonoBehaviour
{
    public const string BlockingUiClass = "blocking-ui";
    public const string ModalUiClass = "modal-ui";

    private static readonly Func<VisualElement, Rect> ReadWorldClip = CreateWorldClipReader();

    [SerializeField] private WorldInputController _worldInput;
    private UIDocument _document;

    private void OnEnable()
    {
        _document = GetComponent<UIDocument>();
        if (_worldInput == null)
        {
            Debug.LogError($"{nameof(WorldInputUiBlocker)}: 입력 컨트롤러 참조가 필요합니다.", this);
            enabled = false;
            return;
        }

        _worldInput.RegisterUiBlocker(this);
    }

    private void OnDisable()
    {
        if (_worldInput == null)
        {
            return;
        }

        _worldInput.UnregisterUiBlocker(this);
    }

    public WorldInputUiState ReadState(Vector2 screenPosition)
    {
        if (_document == null)
        {
            return default;
        }

        VisualElement root = _document.rootVisualElement;
        if (root == null)
        {
            return default;
        }

        if (root.panel == null)
        {
            return default;
        }

        Vector2 topLeftPosition = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
        Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(root.panel, topLeftPosition);
        return ReadPanelState(root, panelPosition);
    }

    public static WorldInputUiState ReadPanelState(VisualElement root, Vector2 panelPosition)
    {
        if (root == null)
        {
            return default;
        }

        if (!IsVisible(root))
        {
            return default;
        }

        if (HasVisibleModal(root) || HasTextInputFocus(root))
        {
            return new WorldInputUiState(true, true);
        }

        bool blocked = IsBlockingRegion(root, panelPosition);
        foreach (VisualElement element in root.Query<VisualElement>(className: BlockingUiClass).Build())
        {
            if (IsBlockingRegion(element, panelPosition))
            {
                blocked = true;
                break;
            }
        }

        return new WorldInputUiState(blocked, false);
    }

    private static bool HasVisibleModal(VisualElement root)
    {
        if (root.ClassListContains(ModalUiClass) && TryGetVisibleBounds(root, out _))
        {
            return true;
        }

        foreach (VisualElement element in root.Query<VisualElement>(className: ModalUiClass).Build())
        {
            if (TryGetVisibleBounds(element, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasTextInputFocus(VisualElement root)
    {
        var focused = root.focusController?.focusedElement as VisualElement;
        if (focused == null)
        {
            return false;
        }

        if (focused != root && !root.Contains(focused))
        {
            return false;
        }

        if (!IsVisible(focused))
        {
            return false;
        }

        for (VisualElement element = focused; element != null; element = element.parent)
        {
            // 문자열/숫자 필드의 내부 포커스를 같은 TextInputBaseField 공통 계약으로 판별한다.
            if (element.ClassListContains(TextInputBaseField<string>.ussClassName))
            {
                return true;
            }

            if (element == root)
            {
                break;
            }
        }

        return false;
    }

    private static bool IsBlockingRegion(VisualElement element, Vector2 point)
    {
        return element.ClassListContains(BlockingUiClass) &&
            TryGetVisibleBounds(element, out Rect visibleBounds) && visibleBounds.Contains(point);
    }

    private static bool TryGetVisibleBounds(VisualElement element, out Rect visibleBounds)
    {
        visibleBounds = element.worldBound;
        if (!IsVisible(element))
        {
            return false;
        }

        // IResolvedStyle에 없는 조상 clipping은 UI Toolkit이 계산한 원본 경계를 읽는다.
        visibleBounds = IntersectBounds(visibleBounds, ReadWorldClip(element));

        if (element.panel != null)
        {
            visibleBounds = IntersectBounds(visibleBounds, element.panel.visualTree.worldBound);
        }

        return visibleBounds.width > 0f && visibleBounds.height > 0f;
    }

    private static Rect IntersectBounds(Rect left, Rect right)
    {
        return Rect.MinMaxRect(Mathf.Max(left.xMin, right.xMin), Mathf.Max(left.yMin, right.yMin),
            Mathf.Min(left.xMax, right.xMax), Mathf.Min(left.yMax, right.yMax));
    }

    private static Func<VisualElement, Rect> CreateWorldClipReader()
    {
        // Unity 6000.4의 계산된 clip getter를 한 번 연결한다. link.xml이 Player stripping을 막는다.
        PropertyInfo property = typeof(VisualElement).GetProperty("worldClip", BindingFlags.Instance | BindingFlags.NonPublic);
        if (property == null)
        {
            throw new NotSupportedException("현재 Unity에서 UI Toolkit worldClip 속성을 찾을 수 없습니다.");
        }

        MethodInfo getter = property.GetGetMethod(true);
        if (getter == null)
        {
            throw new NotSupportedException("현재 Unity에서 UI Toolkit worldClip 읽기를 지원하지 않습니다.");
        }

        return (Func<VisualElement, Rect>)Delegate.CreateDelegate(typeof(Func<VisualElement, Rect>), getter);
    }

    private static bool IsVisible(VisualElement element)
    {
        for (VisualElement current = element; current != null; current = current.parent)
        {
            if (current.resolvedStyle.display == DisplayStyle.None ||
                current.resolvedStyle.visibility != Visibility.Visible)
            {
                return false;
            }
        }

        return true;
    }
}
