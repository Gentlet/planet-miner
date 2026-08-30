using Unity.Mathematics;
using UnityEngine.InputSystem;

internal sealed class BuildingDemolitionSelectionInteraction
{
    private const string SelectionPrompt = "철거할 범위를 좌클릭 드래그하세요.";
    private const string SelectingStatus = "철거 범위 선택 중";

    private BuildingDemolitionSelectionPreview _selectionPreview;
    private bool _isSelecting;
    private bool _isDragging;
    private int2 _selectionStart;
    private int2 _selectionEnd;

    public void AttachPreview(BuildingDemolitionSelectionPreview selectionPreview)
    {
        _selectionPreview = selectionPreview;
    }

    public void BeginSelection()
    {
        _isSelecting = true;
        _isDragging = false;
        _status = SelectionPrompt;
        _selectionPreview.Hide();
    }

    public bool TryHandleSelectionInput(int2 gridCell, out GridBounds selectedBounds)
    {
        selectedBounds = default;
        bool isPointerOverUi = PointerUtility.IsPointerOverUi();

        if (!_isDragging)
        {
            if (Mouse.current.leftButton.wasPressedThisFrame && !isPointerOverUi)
            {
                _isDragging = true;
                _selectionStart = gridCell;
                _selectionEnd = gridCell;
                _status = SelectingStatus;
                ShowSelectionPreview();
            }

            return false;
        }

        if (Mouse.current.leftButton.isPressed)
        {
            _selectionEnd = gridCell;
            ShowSelectionPreview();
            return false;
        }

        if (!Mouse.current.leftButton.wasReleasedThisFrame)
            return false;

        if (isPointerOverUi)
        {
            Cancel();
            return false;
        }

        _selectionEnd = gridCell;
        selectedBounds = new GridBounds(_selectionStart, _selectionEnd);
        Cancel();
        return true;
    }

    public void Cancel()
    {
        _isSelecting = false;
        _isDragging = false;
        _status = string.Empty;
        _selectionPreview.Hide();
    }

    private void ShowSelectionPreview()
    {
        _selectionPreview.Show(new GridBounds(_selectionStart, _selectionEnd));
    }

    public bool IsSelecting => _isSelecting;
    public string Status => _status;

    private string _status = string.Empty;
}
