using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public partial class BuildingPlacementPreview
{
    [SerializeField]
    private Color powerSupplyColor = new(0.1f, 0.55f, 1f, 0.18f);

    [SerializeField]
    private Color connectablePowerPoleColor = new(1f, 0.85f, 0.1f, 0.7f);

    private readonly List<GameObject> _powerSupplyPreviewObjects = new();
    private readonly List<GameObject> _connectablePowerPolePreviewObjects = new();
    private readonly List<int2> _powerSupplyCells = new();
    private readonly HashSet<int2> _uniquePowerSupplyCells = new();
    private readonly List<Entity> _connectablePowerPoles = new();
    private readonly HashSet<Entity> _uniqueConnectablePowerPoles = new();
    private EntityManager _entityManager;
    private PowerGridSystem _powerGrid;
    private EntityQuery _powerConfigQuery;
    private Sprite _rangeCellSprite;
    private Texture2D _rangeCellTexture;

    private void Awake()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        _entityManager = world.EntityManager;
        _powerGrid = world.GetExistingSystemManaged<PowerGridSystem>();
        _powerConfigQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<PowerConfig>(),
            ComponentType.ReadOnly<PowerPoleConfigElement>());
        CreateRangeCellSprite();
        InitializeConstructionSitePresentation();
    }

    private void OnDestroy()
    {
        if (_rangeCellSprite != null)
            Destroy(_rangeCellSprite);

        if (_rangeCellTexture != null)
            Destroy(_rangeCellTexture);
    }

    private void UpdatePowerPoleRangePreview(BuildingPlacementOperation operation)
    {
        HideObjects(_powerSupplyPreviewObjects);
        HideObjects(_connectablePowerPolePreviewObjects);

        if (!TryGetPowerPoleConfig(out PowerPoleConfigElement config))
            return;

        if (_powerGrid == null)
            _powerGrid = World.DefaultGameObjectInjectionWorld
                .GetExistingSystemManaged<PowerGridSystem>();

        if (_powerGrid == null)
            return;

        _uniquePowerSupplyCells.Clear();
        _uniqueConnectablePowerPoles.Clear();

        for (int i = 0; i < operation.Candidates.Count; i++)
        {
            BuildingPlacementCandidate candidate = operation.Candidates[i];

            if (candidate.type != BuildingTypeEnum.PowerPole)
                continue;

            GridBounds supplyBounds = PowerGridRangeUtility.GetBounds(
                candidate.position,
                config.supplyRange);
            supplyBounds.GetCells(_powerSupplyCells);

            for (int cellIndex = 0;
                 cellIndex < _powerSupplyCells.Count;
                 cellIndex++)
            {
                _uniquePowerSupplyCells.Add(_powerSupplyCells[cellIndex]);
            }

            _powerGrid.GetConnectablePowerPoles(
                candidate.position,
                config.connectionRange,
                _connectablePowerPoles);

            for (int poleIndex = 0;
                 poleIndex < _connectablePowerPoles.Count;
                 poleIndex++)
            {
                _uniqueConnectablePowerPoles.Add(
                    _connectablePowerPoles[poleIndex]);
            }
        }

        ShowSupplyCells();
        ShowConnectablePowerPoles();
    }

    private void ShowSupplyCells()
    {
        EnsureRangeObjectCount(
            _powerSupplyPreviewObjects,
            _uniquePowerSupplyCells.Count,
            8);
        int previewIndex = 0;

        foreach (int2 cell in _uniquePowerSupplyCells)
        {
            GameObject previewObject =
                _powerSupplyPreviewObjects[previewIndex];
            SetRangePreview(
                previewObject,
                cell,
                powerSupplyColor,
                Vector3.one);
            previewIndex++;
        }
    }

    private void ShowConnectablePowerPoles()
    {
        EnsureRangeObjectCount(
            _connectablePowerPolePreviewObjects,
            _uniqueConnectablePowerPoles.Count,
            12);
        int previewIndex = 0;

        foreach (Entity powerPoleEntity in _uniqueConnectablePowerPoles)
        {
            if (!_entityManager.Exists(powerPoleEntity))
                continue;

            if (!_entityManager.HasComponent<GridPosition>(powerPoleEntity))
                continue;

            int2 cell = _entityManager
                .GetComponentData<GridPosition>(powerPoleEntity)
                .gridPosition;
            SetRangePreview(
                _connectablePowerPolePreviewObjects[previewIndex],
                cell,
                connectablePowerPoleColor,
                new Vector3(1.15f, 1.15f, 1f));
            previewIndex++;
        }
    }

    private bool TryGetPowerPoleConfig(out PowerPoleConfigElement config)
    {
        if (_powerConfigQuery.IsEmptyIgnoreFilter)
        {
            config = default;
            return false;
        }

        DynamicBuffer<PowerPoleConfigElement> configs =
            _powerConfigQuery.GetSingletonBuffer<PowerPoleConfigElement>(true);
        return PowerGridRangeUtility.TryGetPowerPoleConfig(
            configs,
            out config);
    }

    private void CreateRangeCellSprite()
    {
        _rangeCellTexture = new Texture2D(1, 1)
        {
            name = "Power Range Preview Texture",
            filterMode = FilterMode.Point
        };
        _rangeCellTexture.SetPixel(0, 0, Color.white);
        _rangeCellTexture.Apply();
        _rangeCellSprite = Sprite.Create(
            _rangeCellTexture,
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f),
            1f);
        _rangeCellSprite.name = "Power Range Preview Sprite";
    }

    private void EnsureRangeObjectCount(
        List<GameObject> previewObjects,
        int count,
        int sortingOrder)
    {
        while (previewObjects.Count < count)
        {
            GameObject previewObject = Instantiate(previewPrefab, transform);
            SpriteRenderer renderer =
                previewObject.GetComponent<SpriteRenderer>();

            if (renderer != null)
            {
                renderer.sprite = _rangeCellSprite;
                renderer.sortingOrder = sortingOrder;
            }

            previewObject.SetActive(false);
            previewObjects.Add(previewObject);
        }
    }

    private static void SetRangePreview(
        GameObject previewObject,
        int2 cell,
        Color color,
        Vector3 scale)
    {
        previewObject.SetActive(true);
        previewObject.transform.position = cell.ToVector2();
        previewObject.transform.localScale = scale;
        SpriteRenderer renderer = previewObject.GetComponent<SpriteRenderer>();

        if (renderer != null)
            renderer.color = color;
    }

    private static void HideObjects(List<GameObject> previewObjects)
    {
        for (int i = 0; i < previewObjects.Count; i++)
            previewObjects[i].SetActive(false);
    }
}
