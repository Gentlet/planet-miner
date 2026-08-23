using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;


public class BuildingPlacementController : MonoBehaviour
{
    private enum PointerDragModeEnum
    {
        None,
        Placement,
        Destruction,
        SettingsPaste,
        Count
    }

    private EntityManager _entityManager;
    private ChunkMapSystem _chunkMap;

    [SerializeField]
    private BuildingPlacementPreview _preview;
    private BuildingPlacementOperation _placementOperation;
    private readonly BuildingCopyInteraction _copyInteraction = new();
    private readonly BuildingSettingsTransfer _settingsTransfer = new();

    [SerializeField]
    private bool _enable = false;

    private PointerDragModeEnum _pointerDragMode;
    private bool _hasLastPointerDragCell;
    private int2 _lastPointerDragCell;

    public event Action PlacementSelectionCleared;

    private void Start()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        _placementOperation ??= CreateEmptyOperation();
    }

    private void Awake()
    {
        _chunkMap = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<ChunkMapSystem>();

        if (_chunkMap == null)
        {
            Debug.LogError("ChunkMapSystem not found.");
        }

        BuildingCopySelectionPreview copySelectionPreview =
            GetComponent<BuildingCopySelectionPreview>();

        if (copySelectionPreview == null)
            copySelectionPreview = gameObject.AddComponent<BuildingCopySelectionPreview>();

        _copyInteraction.AttachPreview(copySelectionPreview);
    }

    private void Update()
    {
        _preview.enabled = _enable;

        if (!_enable || _placementOperation == null)
        {
            ResetTransientInput();
            return;
        }

        Keyboard keyboard = Keyboard.current;
        HandleGlobalHotkeys(keyboard);

        if (!TryGetPointerGridCell(out int2 gridCell))
            return;

        if (TryHandleSettingsTransferInput(keyboard, gridCell))
            return;

        if (_copyInteraction.IsSelecting)
        {
            _preview.HidePreview();

            if (_copyInteraction.TryHandleSelectionInput(
                    gridCell,
                    _entityManager,
                    _chunkMap,
                    out BuildingPlacementOperation copyOperation))
            {
                _placementOperation = copyOperation;
            }

            return;
        }

        UpdatePlacementPreview(gridCell);

        if (_copyInteraction.IsPlacing)
            HandleCopyPlacementInput();
        else
            HandlePointerInput(gridCell);
    }

    private void HandleGlobalHotkeys(Keyboard keyboard)
    {
        if (keyboard == null)
            return;

        if (keyboard.cKey.wasPressedThisFrame &&
            (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed))
        {
            BeginCopySelection();
        }

        if (keyboard.rKey.wasPressedThisFrame &&
            !_copyInteraction.IsSelecting &&
            _placementOperation.Candidates.Count > 0)
        {
            _placementOperation.Rotate();
        }
    }

    private bool TryGetPointerGridCell(out int2 gridCell)
    {
        gridCell = default;

        if (Mouse.current == null || _chunkMap == null)
            return false;

        Vector3 pointerWorldPosition =
            Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        gridCell = pointerWorldPosition.ToGridCell();
        return true;
    }

    private void UpdatePlacementPreview(int2 gridCell)
    {
        _placementOperation.PrepareCandidates(gridCell, _chunkMap);
        _preview.UpdatePreviewPositions(_placementOperation, gridCell);
        _placementOperation.EvaluatePlacement(_chunkMap);
        _preview.UpdatePreviewColors(_placementOperation);
    }

    private void BeginCopySelection()
    {
        _copyInteraction.BeginSelection(_placementOperation);
        ResetPointerDrag();
        _preview.HidePreview();
    }

    private void HandleCopyPlacementInput()
    {
        if (PointerUtility.IsPointerOverUi() ||
            !Mouse.current.leftButton.wasPressedThisFrame ||
            !_placementOperation.CanPlace)
            return;

        CreateConstructionRequests();
    }

    private void HandlePointerInput(int2 gridCell)
    {
        if (_pointerDragMode == PointerDragModeEnum.Placement && !Mouse.current.leftButton.isPressed ||
            _pointerDragMode == PointerDragModeEnum.Destruction && !Mouse.current.rightButton.isPressed)
        {
            ResetPointerDrag();
        }

        bool isPointerOverUi = PointerUtility.IsPointerOverUi();

        if (_pointerDragMode == PointerDragModeEnum.None && !isPointerOverUi)
        {
            if (Mouse.current.leftButton.wasPressedThisFrame)
                _pointerDragMode = PointerDragModeEnum.Placement;
            else if (Mouse.current.rightButton.wasPressedThisFrame)
                _pointerDragMode = PointerDragModeEnum.Destruction;
        }

        if (_pointerDragMode == PointerDragModeEnum.None || isPointerOverUi)
            return;

        if (_hasLastPointerDragCell && math.all(_lastPointerDragCell == gridCell))
            return;

        _lastPointerDragCell = gridCell;
        _hasLastPointerDragCell = true;

        if (_pointerDragMode == PointerDragModeEnum.Placement)
        {
            if (_placementOperation.CanPlace)
                CreateConstructionRequests();
        }
        else
        {
            CreateDestroyOrCancelRequest(gridCell);
        }
    }

    private bool TryHandleSettingsTransferInput(
        Keyboard keyboard,
        int2 gridCell)
    {
        Mouse mouse = Mouse.current;

        if (mouse == null || keyboard == null)
            return false;

        if (_pointerDragMode == PointerDragModeEnum.SettingsPaste)
        {
            if (!mouse.leftButton.isPressed)
            {
                ResetPointerDrag();
                return true;
            }

            if (!PointerUtility.IsPointerOverUi())
                _settingsTransfer.PasteSettings(gridCell, _entityManager, _chunkMap);

            return true;
        }

        if (PointerUtility.IsPointerOverUi())
            return false;

        bool shiftPressed =
            keyboard.leftShiftKey.isPressed ||
            keyboard.rightShiftKey.isPressed;

        if (!shiftPressed)
            return false;

        if (mouse.rightButton.wasPressedThisFrame)
        {
            ActivateSettingsTransfer();
            _settingsTransfer.CopySettings(gridCell, _entityManager, _chunkMap);
            return true;
        }

        if (mouse.leftButton.wasPressedThisFrame)
        {
            ActivateSettingsTransfer();
            _pointerDragMode = PointerDragModeEnum.SettingsPaste;
            _settingsTransfer.PasteSettings(gridCell, _entityManager, _chunkMap);
            return true;
        }

        return false;
    }

    private void ActivateSettingsTransfer()
    {
        ClearOperation();
        _preview.HidePreview();
        PlacementSelectionCleared?.Invoke();
    }

    private void ResetPointerDrag()
    {
        _pointerDragMode = PointerDragModeEnum.None;
        _hasLastPointerDragCell = false;
        _settingsTransfer.ResetPasteVisited();
    }

    private void ResetTransientInput()
    {
        ResetPointerDrag();
        _copyInteraction.ResetSelectionDrag();
    }

    private void CreateConstructionRequests()
    {
        if (_placementOperation.Candidates.Count == 0)
            return;

        if (!TryReserveCandidates())
            return;

        List<int2> footprintCells = new();

        foreach (BuildingPlacementCandidate candidate in _placementOperation.Candidates)
        {
            Entity request = _entityManager.CreateEntity();
            _entityManager.AddComponentData(request,
                new ConstructionSiteCreateRequest
                {
                    type = candidate.type,
                    gridPosition = candidate.position,
                    dir = candidate.dir.NextDirection(_placementOperation.Direction),
                    selectedItemType = candidate.selectedItemType
                });
            DynamicBuffer<ConstructionSiteReservedCellElement> reservedCells =
                _entityManager.AddBuffer<ConstructionSiteReservedCellElement>(request);
            BuildingFootprintUtility.GetOccupiedCells(
                candidate.position,
                candidate.size,
                _placementOperation.GetDirection(candidate),
                footprintCells);

            for (int i = 0; i < footprintCells.Count; i++)
            {
                reservedCells.Add(new ConstructionSiteReservedCellElement
                {
                    cell = footprintCells[i]
                });
            }
        }
    }

    private void CreateDestroyOrCancelRequest(int2 gridCell)
    {
        Entity demolitionRequest = _entityManager.CreateEntity();
        _entityManager.AddComponentData(demolitionRequest, new DroneDemolitionRequest
        {
            gridPosition = gridCell
        });
        Entity cancelRequest = _entityManager.CreateEntity();
        _entityManager.AddComponentData(cancelRequest, new ConstructionCancelRequest
        {
            gridPosition = gridCell
        });
    }

    private bool TryReserveCandidates()
    {
        List<int2> reservedCells = new();
        List<int2> footprintCells = new();

        if (!BuildingPlacementReservationUtility.TryReserve(
                _placementOperation,
                _chunkMap,
                reservedCells,
                footprintCells))
        {
            _placementOperation.EvaluatePlacement(_chunkMap);
            return false;
        }

        return true;
    }

    public void SetEnable(bool enable, BuildingPlacementOperation placementOperation)
    {
        SetOperation(placementOperation);
        SetEnable(enable);
    }

    public void SetEnable(bool enable)
    {
        _enable = enable;

        if (!_enable)
            ExitCopyMode(true);

        _preview.enabled = _enable;
    }

    public bool TryCancelCopyMode()
    {
        if (!_copyInteraction.IsActive)
            return false;

        ExitCopyMode(true);
        return true;
    }

    private void ExitCopyMode(bool restorePreviousOperation)
    {
        _placementOperation = _copyInteraction.Exit(
            _placementOperation,
            restorePreviousOperation);
        ResetPointerDrag();
    }

    private void SetOperation(BuildingPlacementOperation operation)
    {
        _placementOperation = operation ?? CreateEmptyOperation();
        ExitCopyMode(false);
    }

    private static BuildingPlacementOperation CreateEmptyOperation()
    {
        return new BuildingPlacementOperation(new List<BuildingPlacementCandidate>());
    }

    public void ClearOperation()
    {
        SetOperation(CreateEmptyOperation());
    }

    public BuildingPlacementOperation Operation
    {
        get => _placementOperation;
        set => SetOperation(value);
    }

    public string CopyStatus => _copyInteraction.Status;
}
