using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;


public class BuildingPlacementController : MonoBehaviour
{
    private enum PlacementInteractionModeEnum
    {
        Normal,
        CopySelecting,
        CopyPlacing,
        Count
    }

    private enum PointerDragModeEnum
    {
        None,
        Placement,
        Destruction,
        SettingsPaste,
        Count
    }

    private struct BuildingSettingsSnapshot
    {
        public BuildingTypeEnum buildingType;
        public ItemTypeEnum crafterRecipe;
    }

    private EntityManager _entityManager;
    private ChunkMapSystem _chunkMap;

    [SerializeField]
    private BuildingPlacementPreview _preview;
    private BuildingPlacementOperation _bpo;
    private BuildingPlacementOperation _operationBeforeCopy;

    [SerializeField]
    private bool _enable = false;

    private PlacementInteractionModeEnum _interactionMode;
    private PointerDragModeEnum _pointerDragMode;
    private bool _hasLastPointerDragCell;
    private int2 _lastPointerDragCell;
    private BuildingSettingsSnapshot? _settingsClipboard;
    private readonly HashSet<Entity> _settingsPasteVisited = new();
    private BuildingCopySelectionPreview _copySelectionPreview;
    private readonly List<Entity> _buildingsInCopyBounds = new();
    private bool _isCopySelectionDragging;
    private int2 _copySelectionStart;
    private int2 _copySelectionEnd;
    private string _copyStatus = string.Empty;

    public event Action PlacementSelectionCleared;

    private void Start()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        _bpo ??= CreateEmptyOperation();
    }

    private void Awake()
    {
        _chunkMap = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<ChunkMapSystem>();

        if (_chunkMap == null)
        {
            Debug.LogError("ChunkMapSystem not found.");
        }

        _copySelectionPreview = GetComponent<BuildingCopySelectionPreview>();
        if (_copySelectionPreview == null)
            _copySelectionPreview = gameObject.AddComponent<BuildingCopySelectionPreview>();
    }

    void Update()
    {
        _preview.enabled = _enable;


        if (!_enable || _bpo == null)
        {
            ResetPointerDrag();
            ResetCopySelectionDrag();
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null &&
            keyboard.cKey.wasPressedThisFrame &&
            (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed))
        {
            BeginCopySelection();
        }

        if (keyboard != null &&
            keyboard.rKey.wasPressedThisFrame &&
            _interactionMode != PlacementInteractionModeEnum.CopySelecting &&
            _bpo.Candidates.Count > 0)
        {
            _bpo.Rotate();
        }

        if (Mouse.current != null && _chunkMap != null)
        {
            Vector3 pos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            int2 gridCell = pos.ToGridCell();

            if (TryHandleSettingsTransferInput(keyboard, gridCell))
                return;

            if (_interactionMode == PlacementInteractionModeEnum.CopySelecting)
            {
                _preview.HidePreview();
                HandleCopySelectionInput(gridCell);
                return;
            }

            _preview.UpdatePreviewPositions(_bpo, gridCell);
            _bpo.EvaluatePlacement(_chunkMap);
            _preview.UpdatePreviewColors(_bpo);

            if (_interactionMode == PlacementInteractionModeEnum.CopyPlacing)
                HandleCopyPlacementInput();
            else
                HandlePointerInput(gridCell);
        }
    }

    private void BeginCopySelection()
    {
        if (_interactionMode == PlacementInteractionModeEnum.Normal)
            _operationBeforeCopy = _bpo;

        _interactionMode = PlacementInteractionModeEnum.CopySelecting;
        _copyStatus = "복사할 범위를 좌클릭 드래그하세요.";
        ResetPointerDrag();
        ResetCopySelectionDrag();
        _preview.HidePreview();
        _copySelectionPreview.Hide();
    }

    private void HandleCopySelectionInput(int2 gridCell)
    {
        bool isPointerOverUi = PointerUtility.IsPointerOverUi();

        if (!_isCopySelectionDragging)
        {
            if (Mouse.current.leftButton.wasPressedThisFrame && !isPointerOverUi)
            {
                _isCopySelectionDragging = true;
                _copySelectionStart = gridCell;
                _copySelectionEnd = gridCell;
                _copyStatus = "복사 범위 선택 중";
                ShowCopySelectionPreview();
            }

            return;
        }

        if (Mouse.current.leftButton.isPressed)
        {
            _copySelectionEnd = gridCell;
            ShowCopySelectionPreview();
            return;
        }

        if (!Mouse.current.leftButton.wasReleasedThisFrame)
            return;

        if (isPointerOverUi)
        {
            ResetCopySelectionDrag();
            _copyStatus = "복사할 범위를 좌클릭 드래그하세요.";
            return;
        }

        _copySelectionEnd = gridCell;
        ShowCopySelectionPreview();
        CreateCopyOperation();
        _isCopySelectionDragging = false;
    }

    private void ShowCopySelectionPreview()
    {
        _copySelectionPreview.Show(
            new GridBounds(_copySelectionStart, _copySelectionEnd));
    }

    private void CreateCopyOperation()
    {
        GridBounds bounds = new(_copySelectionStart, _copySelectionEnd);
        _chunkMap.GetBuildingsInBounds(bounds, _buildingsInCopyBounds);

        if (_buildingsInCopyBounds.Count == 0)
        {
            _copyStatus = "선택한 범위에 복사할 건물이 없습니다.";
            return;
        }

        _bpo = BuildingCopyBlueprintBuilder.Create(
            _entityManager,
            _buildingsInCopyBounds,
            bounds.Min);
        _interactionMode = PlacementInteractionModeEnum.CopyPlacing;
        _copyStatus = "복사 배치 중 · 좌클릭 설치 / R 회전 / Esc 취소";
        _copySelectionPreview.Hide();
    }

    private void HandleCopyPlacementInput()
    {
        if (PointerUtility.IsPointerOverUi() ||
            !Mouse.current.leftButton.wasPressedThisFrame ||
            !_bpo.GetCanPlace)
            return;

        CreateSpawnRequest();
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
            if (_bpo.GetCanPlace)
                CreateSpawnRequest();
        }
        else
        {
            CreateDestroyRequest(gridCell);
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
                PasteSettings(gridCell);

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
            CopySettings(gridCell);
            return true;
        }

        if (mouse.leftButton.wasPressedThisFrame)
        {
            ActivateSettingsTransfer();
            _pointerDragMode = PointerDragModeEnum.SettingsPaste;
            PasteSettings(gridCell);
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

    private void CopySettings(int2 gridCell)
    {
        _settingsClipboard = null;

        if (!_chunkMap.TryGetBuilding(gridCell, out Entity building) ||
            !_entityManager.HasComponent<Crafter>(building))
        {
            return;
        }

        Crafter crafter = _entityManager.GetComponentData<Crafter>(building);
        _settingsClipboard = new BuildingSettingsSnapshot
        {
            buildingType = BuildingTypeEnum.Crafter,
            crafterRecipe = crafter.selectedItemType
        };
    }

    private void PasteSettings(int2 gridCell)
    {
        if (!_settingsClipboard.HasValue ||
            !_chunkMap.TryGetBuilding(gridCell, out Entity building) ||
            !_settingsPasteVisited.Add(building))
        {
            return;
        }

        BuildingSettingsSnapshot snapshot = _settingsClipboard.Value;

        if (snapshot.buildingType != BuildingTypeEnum.Crafter ||
            !_entityManager.HasComponent<Crafter>(building))
        {
            return;
        }

        Crafter targetCrafter =
            _entityManager.GetComponentData<Crafter>(building);

        if (targetCrafter.selectedItemType == snapshot.crafterRecipe)
            return;

        Entity request = _entityManager.CreateEntity();
        _entityManager.AddComponentData(request, new CrafterRecipeChangeRequest
        {
            crafterEntity = building,
            selectedItemType = snapshot.crafterRecipe
        });
    }

    private void ResetPointerDrag()
    {
        _pointerDragMode = PointerDragModeEnum.None;
        _hasLastPointerDragCell = false;
        _settingsPasteVisited.Clear();
    }

    private void ResetCopySelectionDrag()
    {
        _isCopySelectionDragging = false;

        if (_copySelectionPreview != null)
            _copySelectionPreview.Hide();
    }

    private void CreateSpawnRequest()
    {
        if (_bpo.Candidates.Count == 0)
            return;

        if (!TryReserveCandidates())
            return;

        foreach (var candidate in _bpo.Candidates)
        {
            Entity request = _entityManager.CreateEntity();
            _entityManager.AddComponentData(request,
                new BuildingSpawnRequest
                {
                    type = candidate.type,
                    gridPosition = candidate.position,
                    dir = candidate.dir.NextDirection(_bpo.GetDirection),
                    selectedItemType = candidate.selectedItemType
                });
        }
    }

    private void CreateDestroyRequest(int2 gridCell)
    {
        Entity request = _entityManager.CreateEntity();
        _entityManager.AddComponentData(request, new BuildingDestroyRequest
        {
            gridPosition = gridCell
        });
    }

    private bool TryReserveCandidates()
    {
        List<int2> reservedCells = new();

        foreach (var candidate in _bpo.Candidates)
        {
            if (_chunkMap.TryReserveBuilding(candidate.position))
            {
                reservedCells.Add(candidate.position);
                continue;
            }

            foreach (int2 reservedCell in reservedCells)
                _chunkMap.TryUnreserveBuilding(reservedCell);

            _bpo.EvaluatePlacement(_chunkMap);
            return false;
        }

        return true;
    }

    public void SetEnable(bool enable, BuildingPlacementOperation bpo)
    {
        SetOperation(bpo);
        SetEnable(enable);
    }

    public void SetEnable(bool enable)
    {
        _enable = enable;

        if (!_enable)
        {
            if (_interactionMode != PlacementInteractionModeEnum.Normal &&
                _operationBeforeCopy != null)
            {
                _bpo = _operationBeforeCopy;
            }

            ResetPointerDrag();
            ResetCopySelectionDrag();
            _interactionMode = PlacementInteractionModeEnum.Normal;
            _operationBeforeCopy = null;
            _copyStatus = string.Empty;
        }

        _preview.enabled = _enable;
    }

    public bool TryCancelCopyMode()
    {
        if (_interactionMode == PlacementInteractionModeEnum.Normal)
            return false;

        if (_operationBeforeCopy != null)
            _bpo = _operationBeforeCopy;

        _operationBeforeCopy = null;
        _interactionMode = PlacementInteractionModeEnum.Normal;
        _copyStatus = string.Empty;
        ResetCopySelectionDrag();
        ResetPointerDrag();
        return true;
    }

    private void SetOperation(BuildingPlacementOperation operation)
    {
        _bpo = operation ?? CreateEmptyOperation();
        _operationBeforeCopy = null;
        _interactionMode = PlacementInteractionModeEnum.Normal;
        _copyStatus = string.Empty;
        ResetCopySelectionDrag();
        ResetPointerDrag();
    }

    private static BuildingPlacementOperation CreateEmptyOperation()
    {
        return new BuildingPlacementOperation(new List<BuildingPlacementCandidate>());
    }

    public void ClearOperation()
    {
        SetOperation(CreateEmptyOperation());
    }

    #region Properties
    public BuildingPlacementOperation Operation { get => _bpo; set => SetOperation(value); }
    public string CopyStatus => _copyStatus;
    #endregion
}
