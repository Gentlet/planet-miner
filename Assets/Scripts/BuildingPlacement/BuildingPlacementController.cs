using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;


public class BuildingPlacementController : MonoBehaviour
{
    private EntityManager _entityManager;
    private GridOccupancySystem _gos;

    [SerializeField]
    private BuildingPlacementPreview _preview;
    private BuildingPlacementOperation _bpo;

    [SerializeField]
    private bool _enable = false;

    private void Start()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        _bpo = new BuildingPlacementOperation(new List<BuildingPlacementCandidate>
        {
            new BuildingPlacementCandidate(
                BuildingTypeEnum.Belt,
                int2.zero,
                int2.zero,
                DirectionEnum.Up,
                false)
        });
    }

    private void Awake()
    {
        _gos = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<GridOccupancySystem>();

        if (_gos == null)
        {
            Debug.LogError("GridOccupancySystem not found.");
        }
    }

    void Update()
    {
        if (!_enable || _bpo == null)
            return;

        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            _bpo.Rotate();
        }

        if (Mouse.current != null && _gos != null && _preview != null)
        {
            Vector3 pos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());

            _bpo.EvaluatePlacement(_gos);
            _preview.ShowPreview(_bpo, pos.ToGridCell());

            if (Mouse.current.leftButton.wasPressedThisFrame && _bpo.GetCanPlace)
            {
                foreach (var candidate in _bpo.Candidates)
                {
                    Entity request = _entityManager.CreateEntity();
                    _entityManager.AddComponentData(request, 
                        new BuildingSpawnRequest {
                            type = candidate.type,
                            gridPosition = candidate.position,
                            dir = candidate.dir.NextDirection(_bpo.GetDirection)
                        });
                }
            }
        }
    }

    public void SetEnable(bool enable, BuildingPlacementOperation bpo)
    {
        _enable = enable;
        _bpo = bpo;
    }
    public void SetEnable(bool enable)
    {
        _enable = enable;
    }

    public BuildingPlacementOperation Operation { get => _bpo; set => _bpo = value; }
}
