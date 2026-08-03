using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.InputSystem;

internal sealed class BuildingCopyInteraction
{
    private const string SelectionPrompt = "복사할 범위를 좌클릭 드래그하세요.";
    private const string SelectingStatus = "복사 범위 선택 중";
    private const string EmptySelectionStatus = "선택한 범위에 복사할 건물이 없습니다.";
    private const string PlacingStatus = "복사 배치 중 · 좌클릭 설치 / R 회전 / Esc 취소";

    private enum PlacementInteractionModeEnum
    {
        Normal,
        CopySelecting,
        CopyPlacing,
        Count
    }

    private PlacementInteractionModeEnum _interactionMode;
    private BuildingPlacementOperation _operationBeforeCopy;
    private BuildingCopySelectionPreview _selectionPreview;
    private readonly List<Entity> _buildingsInCopyBounds = new();
    private bool _isSelectionDragging;
    private int2 _selectionStart;
    private int2 _selectionEnd;
    private string _status = string.Empty;

    public void AttachPreview(BuildingCopySelectionPreview selectionPreview)
    {
        _selectionPreview = selectionPreview;
    }

    public void BeginSelection(BuildingPlacementOperation currentOperation)
    {
        if (_interactionMode == PlacementInteractionModeEnum.Normal)
            _operationBeforeCopy = currentOperation;

        _interactionMode = PlacementInteractionModeEnum.CopySelecting;
        _status = SelectionPrompt;
        ResetSelectionDrag();
    }

    public bool TryHandleSelectionInput(
        int2 gridCell,
        EntityManager entityManager,
        ChunkMapSystem chunkMap,
        out BuildingPlacementOperation copyOperation)
    {
        copyOperation = null;
        bool isPointerOverUi = PointerUtility.IsPointerOverUi();

        if (!_isSelectionDragging)
        {
            if (Mouse.current.leftButton.wasPressedThisFrame && !isPointerOverUi)
            {
                _isSelectionDragging = true;
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
            ResetSelectionDrag();
            _status = SelectionPrompt;
            return false;
        }

        _selectionEnd = gridCell;
        ShowSelectionPreview();
        bool createdCopyOperation = TryCreateCopyOperation(
            entityManager,
            chunkMap,
            out copyOperation);
        _isSelectionDragging = false;
        return createdCopyOperation;
    }

    public BuildingPlacementOperation Exit(
        BuildingPlacementOperation currentOperation,
        bool restorePreviousOperation)
    {
        if (restorePreviousOperation && _operationBeforeCopy != null)
            currentOperation = _operationBeforeCopy;

        _operationBeforeCopy = null;
        _interactionMode = PlacementInteractionModeEnum.Normal;
        _status = string.Empty;
        ResetSelectionDrag();
        return currentOperation;
    }

    public void ResetSelectionDrag()
    {
        _isSelectionDragging = false;

        if (_selectionPreview != null)
            _selectionPreview.Hide();
    }

    private void ShowSelectionPreview()
    {
        _selectionPreview.Show(new GridBounds(_selectionStart, _selectionEnd));
    }

    private bool TryCreateCopyOperation(
        EntityManager entityManager,
        ChunkMapSystem chunkMap,
        out BuildingPlacementOperation copyOperation)
    {
        GridBounds bounds = new(_selectionStart, _selectionEnd);
        chunkMap.GetBuildingsInBounds(bounds, _buildingsInCopyBounds);

        if (_buildingsInCopyBounds.Count == 0)
        {
            _status = EmptySelectionStatus;
            copyOperation = null;
            return false;
        }

        copyOperation = BuildingCopyBlueprintBuilder.Create(
            entityManager,
            _buildingsInCopyBounds,
            bounds.Min);
        _interactionMode = PlacementInteractionModeEnum.CopyPlacing;
        _status = PlacingStatus;
        ResetSelectionDrag();
        return true;
    }

    public bool IsActive => _interactionMode != PlacementInteractionModeEnum.Normal;
    public bool IsSelecting => _interactionMode == PlacementInteractionModeEnum.CopySelecting;
    public bool IsPlacing => _interactionMode == PlacementInteractionModeEnum.CopyPlacing;
    public string Status => _status;
}
