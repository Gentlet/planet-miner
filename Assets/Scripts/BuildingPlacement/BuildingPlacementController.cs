using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;


public class BuildingPlacementController : MonoBehaviour
{
    private enum PointerDragMode
    {
        None,
        Placement,
        Destruction
    }

    private EntityManager _entityManager;
    private ChunkMapSystem _chunkMap;

    [SerializeField]
    private BuildingPlacementPreview _preview;
    private BuildingPlacementOperation _bpo;

    [SerializeField]
    private bool _enable = false;

    private PointerDragMode _pointerDragMode;
    private bool _hasLastPointerDragCell;
    private int2 _lastPointerDragCell;

    private void Start()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        _bpo = new BuildingPlacementOperation(new List<BuildingPlacementCandidate>());

        for (int i = 0; i < 1; i++)
        {
            for (int j = 0; j < 1; j++)
            {
                _bpo.Candidates.Add(
                    new BuildingPlacementCandidate(
                    BuildingTypeEnum.Belt,
                    new int2(i, j),
                    DirectionEnum.Up,
                    false)
                    );
            }
        }
    }

    private void Awake()
    {
        _chunkMap = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<ChunkMapSystem>();

        if (_chunkMap == null)
        {
            Debug.LogError("ChunkMapSystem not found.");
        }
    }

    void Update()
    {
        _preview.enabled = _enable;


        if (!_enable || _bpo == null)
        {
            ResetPointerDrag();
            return;
        }

        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            _bpo.Rotate();
        }

        if (Mouse.current != null && _chunkMap != null)
        {
            Vector3 pos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            int2 gridCell = pos.ToGridCell();

            if (_preview != null)
                _preview.ShowPreview(_bpo, gridCell);

            if (_bpo != null)
                _bpo.EvaluatePlacement(_chunkMap);

            HandlePointerInput(gridCell);
        }
    }

    private void HandlePointerInput(int2 gridCell)
    {
        if (_pointerDragMode == PointerDragMode.Placement && !Mouse.current.leftButton.isPressed ||
            _pointerDragMode == PointerDragMode.Destruction && !Mouse.current.rightButton.isPressed)
        {
            ResetPointerDrag();
        }

        bool isPointerOverUi = PointerUtility.IsPointerOverUi();

        if (_pointerDragMode == PointerDragMode.None && !isPointerOverUi)
        {
            if (Mouse.current.leftButton.wasPressedThisFrame)
                _pointerDragMode = PointerDragMode.Placement;
            else if (Mouse.current.rightButton.wasPressedThisFrame)
                _pointerDragMode = PointerDragMode.Destruction;
        }

        if (_pointerDragMode == PointerDragMode.None || isPointerOverUi)
            return;

        if (_hasLastPointerDragCell && math.all(_lastPointerDragCell == gridCell))
            return;

        _lastPointerDragCell = gridCell;
        _hasLastPointerDragCell = true;

        if (_pointerDragMode == PointerDragMode.Placement)
        {
            if (_bpo.GetCanPlace)
                CreateSpawnRequest();
        }
        else
        {
            CreateDestroyRequest(gridCell);
        }
    }

    private void ResetPointerDrag()
    {
        _pointerDragMode = PointerDragMode.None;
        _hasLastPointerDragCell = false;
    }

    private void CreateSpawnRequest()
    {
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
        _enable = enable;
        _bpo = bpo;

        _preview.enabled = _enable;
    }
    public void SetEnable(bool enable)
    {
        _enable = enable;

        _preview.enabled = _enable;
    }

    #region Properties
    public BuildingPlacementOperation Operation { get => _bpo; set => _bpo = value; }
    #endregion
}
