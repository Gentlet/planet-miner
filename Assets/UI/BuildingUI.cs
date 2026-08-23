using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public partial class BuildingUI : MonoBehaviour
{
    private const float RefreshInterval = 0.05f;

    private readonly Dictionary<ItemTypeEnum, int> _storedCounts = new();
    private readonly Dictionary<ItemTypeEnum, int> _producedCounts = new();
    private readonly Dictionary<ItemTypeEnum, Button> _recipeButtons = new();
    private readonly HashSet<ItemTypeEnum> _listedInputTypes = new();
    private readonly List<ItemTypeEnum> _transferItemTypes = new();
    private readonly List<string> _transferItemNames = new();

    private EntityManager _entityManager;
    private ChunkMapSystem _chunkMap;
    private Entity _configEntity;
    private Entity _selectedBuilding;
    private bool _selectionEnabled;
    private float _nextRefreshTime;

    private VisualElement _root;
    private VisualElement _panel;
    private UIDocument _uiDocument;
    private Label _buildingTitleLabel;
    private Label _statusLabel;
    private VisualElement _powerContainer;
    private Label _powerGridLabel;
    private Label _powerPrimaryLabel;
    private Label _powerSecondaryLabel;
    private Label _powerTertiaryLabel;
    private Label _powerQuaternaryLabel;
    private Label _recipeTitleLabel;
    private Label _currentRecipeLabel;
    private VisualElement _recipeContainer;
    private Label _inputTitleLabel;
    private VisualElement _inputContainer;
    private Label _outputTitleLabel;
    private VisualElement _outputContainer;
    private Label _progressTitleLabel;
    private ProgressBar _progressBar;
    private Label _remainingTimeLabel;
    private Label _speedLabel;
    private Label _speedReasonLabel;
    private VisualElement _droneTransferContainer;
    private DropdownField _droneItemField;
    private IntegerField _droneQuantityField;
    private Button _droneInsertButton;
    private Button _droneRemoveButton;
    private Button _closeButton;

    private void Awake()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        _entityManager = world.EntityManager;
        _chunkMap = world.GetExistingSystemManaged<ChunkMapSystem>();
        _uiDocument = GetComponent<UIDocument>();
        _configEntity = Entity.Null;
        _selectedBuilding = Entity.Null;
    }

    private void OnEnable()
    {
        BindVisualTree();
        SetPanelVisible(false);
    }

    private void OnDisable()
    {
        UnbindVisualTree();
    }

    private void Update()
    {
        if (!_selectionEnabled)
            return;

        if (Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame &&
            IsOpen)
        {
            Close();
            return;
        }

        if (PointerUtility.WasLeftClickPressed())
            SelectBuildingUnderPointer();

        if (!IsOpen || Time.unscaledTime < _nextRefreshTime)
            return;

        _nextRefreshTime = Time.unscaledTime + RefreshInterval;
        Refresh();
    }

    public void SetSelectionEnabled(bool enabled)
    {
        _selectionEnabled = enabled;

        if (!_selectionEnabled)
            Close();
    }

    public void Close()
    {
        _selectedBuilding = Entity.Null;
        SetPanelVisible(false);
    }

    private void SelectBuildingUnderPointer()
    {
        Vector3 worldPosition = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        int2 gridCell = worldPosition.ToGridCell();

        if (!_chunkMap.TryGetBuilding(gridCell, out Entity buildingEntity) ||
            !_entityManager.Exists(buildingEntity) ||
            !IsSupportedBuilding(buildingEntity))
        {
            Close();
            return;
        }

        _selectedBuilding = buildingEntity;
        _nextRefreshTime = 0f;
        SetPanelVisible(true);
        Refresh();
    }

    private void Refresh()
    {
        if (!_entityManager.Exists(_selectedBuilding))
        {
            Close();
            return;
        }

        if (_entityManager.HasComponent<CoalGenerator>(_selectedBuilding))
        {
            RefreshCoalGenerator();
            return;
        }

        if (_entityManager.HasComponent<MainFacility>(_selectedBuilding))
        {
            RefreshMainFacility();
            return;
        }

        if (_entityManager.HasComponent<PowerPole>(_selectedBuilding))
        {
            RefreshPowerPole();
            return;
        }

        if (!TryGetConfigEntity(out Entity configEntity))
        {
            Close();
            return;
        }

        DynamicBuffer<ItemStorageLimitElement> storageLimits =
            _entityManager.GetBuffer<ItemStorageLimitElement>(configEntity, true);

        if (_entityManager.HasComponent<Storage>(_selectedBuilding))
        {
            RefreshStorage(storageLimits);
            return;
        }

        if (_entityManager.HasComponent<Crafter>(_selectedBuilding))
        {
            RefreshCrafter(configEntity, storageLimits);
            return;
        }

        if (_entityManager.HasComponent<Miner>(_selectedBuilding))
        {
            RefreshMiner(storageLimits);
            return;
        }

        Close();
    }

    private bool IsSupportedBuilding(Entity buildingEntity)
    {
        return _entityManager.HasComponent<Crafter>(buildingEntity) ||
               _entityManager.HasComponent<Miner>(buildingEntity) ||
               _entityManager.HasComponent<Storage>(buildingEntity) ||
               _entityManager.HasComponent<CoalGenerator>(buildingEntity) ||
               _entityManager.HasComponent<MainFacility>(buildingEntity) ||
               _entityManager.HasComponent<PowerPole>(buildingEntity);
    }

    private bool TryGetConfigEntity(out Entity configEntity)
    {
        if (_configEntity != Entity.Null && _entityManager.Exists(_configEntity))
        {
            configEntity = _configEntity;
            return true;
        }

        using EntityQuery configQuery =
            _entityManager.CreateEntityQuery(ComponentType.ReadOnly<CrafterConfig>());

        if (configQuery.IsEmptyIgnoreFilter)
        {
            configEntity = Entity.Null;
            return false;
        }

        _configEntity = configQuery.GetSingletonEntity();
        configEntity = _configEntity;
        return true;
    }

    private bool IsOpen => _selectedBuilding != Entity.Null;
}
