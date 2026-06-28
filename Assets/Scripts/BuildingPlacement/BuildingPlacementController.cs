using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;


public class BuildingPlacementController : MonoBehaviour
{
    private GridOccupancySystem _gos;

    [SerializeField]
    private BuildingPlacementPreview _preview;
    private BuildingPlacementOperation _bpo;

    [SerializeField]
    private bool _enable = false;

    private void Start()
    {
        _bpo = new BuildingPlacementOperation(new List<BuildingPlacementCandidate>
        {
            new BuildingPlacementCandidate(
                BuildingTypeEnum.Belt,
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
            Vector2 pos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue()) + (Vector3.one * 0.5f);
            pos = pos.Floor();

            _bpo.EvaluatePlacement(pos.ToInt2(), _gos);
            _preview.ShowPreview(_bpo, pos.ToInt2());

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                Debug.Log($"{pos} : Clicked!!!!!!");
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
