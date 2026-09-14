using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public partial class BuildingUI : MonoBehaviour
{
    private const float RefreshInterval = 0.05f;
    private const float DroneSelectionRadius = 0.45f;

    private readonly Dictionary<ItemTypeEnum, int> _storedCounts = new();
    private readonly Dictionary<ItemTypeEnum, int> _producedCounts = new();
    private readonly Dictionary<ItemTypeEnum, Button> _recipeButtons = new();
    private readonly HashSet<ItemTypeEnum> _listedInputTypes = new();
    private readonly List<ItemTypeEnum> _transferItemTypes = new();
    private readonly List<string> _transferItemNames = new();

    private EntityManager _entityManager;
    private ChunkMapSystem _chunkMap;
    private EntityQuery _activeDroneQuery;
    private EntityQuery _droneBuildingTaskQuery;
    private Entity _configEntity;
    private Entity _researchConfigEntity;
    private Entity _selectedBuilding;
    private Entity _selectedDrone;
    private Entity _selectedConstructionSite;
    private int2 _selectedConstructionSiteCell;
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
    private VisualElement _droneContainer;
    private ProgressBar _droneBatteryProgress;
    private Label _droneBatteryLabel;
    private Label _droneCargoLabel;
    private Label _droneCapabilityLabel;
    private Label _droneTaskLabel;
    private Label _droneAssignmentLabel;
    private VisualElement _beltContainer;
    private Label _beltMaximumSpeedLabel;
    private VisualElement _beltItemContainer;
    private Label _recipeTitleLabel;
    private Label _currentRecipeLabel;
    private VisualElement _recipeContainer;
    private Label _inputTitleLabel;
    private VisualElement _inputContainer;
    private Label _dedicatedDroneStorageTitleLabel;
    private VisualElement _dedicatedDroneStorageContainer;
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

    public bool IsBound { get; private set; }

    private void Awake()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world != null && world.IsCreated)
        {
            _entityManager = world.EntityManager;
            _chunkMap = world.GetExistingSystemManaged<ChunkMapSystem>();
            _activeDroneQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ActiveDrone>(),
                ComponentType.ReadOnly<DroneBattery>(),
                ComponentType.ReadOnly<DroneState>(),
                ComponentType.ReadOnly<LocalTransform>());
            _droneBuildingTaskQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<DroneBuildingItemTaskData>());
        }
        _uiDocument = GetComponent<UIDocument>();
        _configEntity = Entity.Null;
        _researchConfigEntity = Entity.Null;
        _selectedBuilding = Entity.Null;
        _selectedDrone = Entity.Null;
        _selectedConstructionSite = Entity.Null;
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
        if (!IsBound)
            return;

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
            SelectEntityUnderPointer();

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
        _selectedDrone = Entity.Null;
        _selectedConstructionSite = Entity.Null;
        SetPanelVisible(false);
    }

    private void SelectEntityUnderPointer()
    {
        Vector3 worldPosition = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());

        if (TrySelectDrone(worldPosition))
            return;

        int2 gridCell = worldPosition.ToGridCell();

        if (_chunkMap.TryGetConstructionSite(
                gridCell,
                out Entity constructionSiteEntity) &&
            _entityManager.Exists(constructionSiteEntity) &&
            _entityManager.HasComponent<ConstructionSite>(constructionSiteEntity))
        {
            _selectedBuilding = Entity.Null;
            _selectedDrone = Entity.Null;
            _selectedConstructionSite = constructionSiteEntity;
            _selectedConstructionSiteCell = gridCell;
            _nextRefreshTime = 0f;
            SetPanelVisible(true);
            Refresh();
            return;
        }

        if (!_chunkMap.TryGetBuilding(gridCell, out Entity buildingEntity) ||
            !_entityManager.Exists(buildingEntity) ||
            !IsSupportedBuilding(buildingEntity))
        {
            Close();
            return;
        }

        _selectedBuilding = buildingEntity;
        _selectedDrone = Entity.Null;
        _selectedConstructionSite = Entity.Null;
        _nextRefreshTime = 0f;
        SetPanelVisible(true);
        Refresh();
    }

    private bool TrySelectDrone(Vector3 worldPosition)
    {
        NativeArray<Entity> droneEntities = _activeDroneQuery.ToEntityArray(Allocator.Temp);
        NativeArray<LocalTransform> droneTransforms =
            _activeDroneQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
        NativeArray<DroneState> droneStates =
            _activeDroneQuery.ToComponentDataArray<DroneState>(Allocator.Temp);

        float2 pointerPosition = new(worldPosition.x, worldPosition.y);
        float maximumDistanceSquared = DroneSelectionRadius * DroneSelectionRadius;
        float nearestDistanceSquared = maximumDistanceSquared;
        Entity nearestDrone = Entity.Null;

        for (int index = 0; index < droneEntities.Length; index++)
        {
            if (droneStates[index].value == DroneStateEnum.Stored)
                continue;

            float2 dronePosition = droneTransforms[index].Position.xy;
            float distanceSquared = math.distancesq(pointerPosition, dronePosition);

            if (distanceSquared > nearestDistanceSquared)
                continue;

            nearestDistanceSquared = distanceSquared;
            nearestDrone = droneEntities[index];
        }

        droneStates.Dispose();
        droneTransforms.Dispose();
        droneEntities.Dispose();

        if (nearestDrone == Entity.Null)
            return false;

        _selectedBuilding = Entity.Null;
        _selectedDrone = nearestDrone;
        _selectedConstructionSite = Entity.Null;
        _nextRefreshTime = 0f;
        SetPanelVisible(true);
        Refresh();
        return true;
    }

    private void Refresh()
    {
        if (_selectedDrone != Entity.Null)
        {
            RefreshDrone();
            return;
        }

        if (_selectedConstructionSite != Entity.Null)
        {
            RefreshConstructionSite();
            return;
        }

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

        if (_entityManager.HasComponent<Belt>(_selectedBuilding))
        {
            RefreshBelt();
            return;
        }

        if (_entityManager.HasComponent<ResearchBuilding>(_selectedBuilding))
        {
            RefreshResearchBuilding();
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

        if (_entityManager.HasComponent<BuildingType>(_selectedBuilding))
        {
            RefreshStaticBuilding();
            return;
        }

        Close();
    }

    private bool IsSupportedBuilding(Entity buildingEntity)
    {
        return _entityManager.HasComponent<BuildingType>(buildingEntity);
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

    private bool TryGetResearchConfigEntity(out Entity configEntity)
    {
        if (_researchConfigEntity != Entity.Null &&
            _entityManager.Exists(_researchConfigEntity))
        {
            configEntity = _researchConfigEntity;
            return true;
        }

        using EntityQuery configQuery =
            _entityManager.CreateEntityQuery(ComponentType.ReadOnly<ResearchConfig>());

        if (configQuery.IsEmptyIgnoreFilter)
        {
            configEntity = Entity.Null;
            return false;
        }

        _researchConfigEntity = configQuery.GetSingletonEntity();
        configEntity = _researchConfigEntity;
        return true;
    }

    private bool IsOpen =>
        _selectedBuilding != Entity.Null ||
        _selectedDrone != Entity.Null ||
        _selectedConstructionSite != Entity.Null;
    private const int ResearchInputBufferCycleCount = 2;

    private void RefreshResearchBuilding()
    {
        if (!_entityManager.HasBuffer<StoredItemElement>(_selectedBuilding) ||
            !TryGetResearchConfigEntity(out Entity configEntity))
        {
            Close();
            return;
        }

        SetResearchBuildingLayout();
        ResearchBuilding researchBuilding = _entityManager
            .GetComponentData<ResearchBuilding>(_selectedBuilding);
        ResearchState researchState = _entityManager
            .GetComponentData<ResearchState>(configEntity);
        DynamicBuffer<ResearchDefinitionElement> definitions = _entityManager
            .GetBuffer<ResearchDefinitionElement>(configEntity, true);
        DynamicBuffer<ResearchIngredientElement> ingredients = _entityManager
            .GetBuffer<ResearchIngredientElement>(configEntity, true);
        DynamicBuffer<ResearchProgressElement> globalProgress = _entityManager
            .GetBuffer<ResearchProgressElement>(configEntity, true);
        DynamicBuffer<ResearchStatModifierElement> modifiers = _entityManager
            .GetBuffer<ResearchStatModifierElement>(configEntity, true);
        DynamicBuffer<StoredItemElement> storedItems = _entityManager
            .GetBuffer<StoredItemElement>(_selectedBuilding, true);

        CountItems(storedItems, _storedCounts, static item => item.type);

        ResearchDefinitionElement activeResearch = default;
        bool hasActiveResearch = researchState.activeResearchId.Length > 0 &&
            definitions.TryGetDefinition(
                researchState.activeResearchId,
                out activeResearch);

        UpdateResearchBuildingSummary(
            researchBuilding,
            hasActiveResearch,
            activeResearch,
            globalProgress);
        UpdateResearchBuildingInventory(
            hasActiveResearch,
            activeResearch,
            ingredients);
        UpdateResearchBuildingProgress(
            researchBuilding,
            hasActiveResearch,
            activeResearch,
            modifiers);
        RefreshPowerConsumer();
        UpdateResearchBuildingStatus(researchBuilding, hasActiveResearch);
    }

    private void UpdateResearchBuildingSummary(
        ResearchBuilding researchBuilding,
        bool hasActiveResearch,
        ResearchDefinitionElement activeResearch,
        DynamicBuffer<ResearchProgressElement> globalProgress)
    {
        if (!hasActiveResearch)
        {
            _currentRecipeLabel.text = "활성 연구: 없음";
            return;
        }

        int progressIndex = globalProgress.FindProgressIndex(activeResearch.stableId);
        float progress = progressIndex >= 0
            ? globalProgress[progressIndex].progress
            : 0f;
        _currentRecipeLabel.text =
            $"{activeResearch.displayName} · 전체 {progress:0.#} / {activeResearch.requiredProgress:0.#}";
    }

    private void UpdateResearchBuildingInventory(
        bool hasActiveResearch,
        ResearchDefinitionElement activeResearch,
        DynamicBuffer<ResearchIngredientElement> ingredients)
    {
        _listedInputTypes.Clear();
        int rowIndex = 0;

        if (hasActiveResearch)
        {
            for (int i = 0; i < ingredients.Length; i++)
            {
                ResearchIngredientElement ingredient = ingredients[i];

                if (!ingredient.researchId.Equals(activeResearch.stableId))
                    continue;

                _storedCounts.TryGetValue(ingredient.itemType, out int count);
                int capacity = ingredient.amount * ResearchInputBufferCycleCount;
                SetItemRow(
                    _inputContainer,
                    rowIndex,
                    ingredient.itemType,
                    count,
                    capacity,
                    $"주기당 {ingredient.amount}개",
                    false);
                _listedInputTypes.Add(ingredient.itemType);
                rowIndex++;
            }
        }

        for (ItemTypeEnum itemType = ItemTypeEnum.Iron_Ore;
             itemType < ItemTypeEnum.Count;
             itemType++)
        {
            if (!_storedCounts.TryGetValue(itemType, out int count) ||
                _listedInputTypes.Contains(itemType))
                continue;

            SetItemRow(
                _inputContainer,
                rowIndex,
                itemType,
                count,
                count,
                hasActiveResearch ? "현재 연구에서 사용 불가" : "배출 대기",
                true);
            rowIndex++;
        }

        TrimItemRowsAndSetEmptyState(_inputContainer, rowIndex);
    }

    private void UpdateResearchBuildingStatus(
        ResearchBuilding researchBuilding,
        bool hasActiveResearch)
    {
        if (researchBuilding.resetNoticeRemaining > 0f)
        {
            string message = researchBuilding.resetReason ==
                             ResearchCycleResetReasonEnum.ResearchChanged
                ? "연구 변경으로 진행 중이던 주기가 초기화되었습니다"
                : "연구 완료로 진행 중이던 다른 주기가 초기화되었습니다";
            SetStatus(message, "status-waiting");
            return;
        }

        if (!hasActiveResearch)
        {
            SetStatus("연구 화면에서 연구를 선택하세요", "status-waiting");
            return;
        }

        switch (researchBuilding.state)
        {
            case ResearchBuildingStateEnum.Researching:
                if (_entityManager.HasComponent<PowerConsumer>(_selectedBuilding) &&
                    _entityManager.GetComponentData<PowerConsumer>(
                        _selectedBuilding).supplyRatio < 0.9999f)
                    SetStatus("전력 부족으로 감속 중", "status-waiting");
                else
                    SetStatus("연구 중", "status-normal");
                break;
            case ResearchBuildingStateEnum.NoPower:
                SetStatus("전력 없음", "status-error");
                break;
            default:
                SetStatus("재료 대기 중", "status-waiting");
                break;
        }
    }

    private void UpdateResearchBuildingProgress(
        ResearchBuilding researchBuilding,
        bool hasActiveResearch,
        ResearchDefinitionElement activeResearch,
        DynamicBuffer<ResearchStatModifierElement> modifiers)
    {
        float duration = hasActiveResearch ? activeResearch.cycleDuration : 0f;
        float ratio = duration > 0f && researchBuilding.cycleActive
            ? math.saturate(researchBuilding.progress / duration)
            : 0f;
        float percent = ratio * 100f;
        _progressBar.value = percent;
        _progressBar.title = $"{percent:0}%";

        float powerMultiplier = GetProductionSpeedMultiplier(
            out string powerReason);
        float researchMultiplier = modifiers.GetStatMultiplier(
            ResearchStatModifierTypeEnum.ResearchSpeed);
        float effectiveSpeed = researchBuilding.speed *
                               powerMultiplier *
                               researchMultiplier;
        _speedLabel.text = effectiveSpeed > 0f
            ? $"연구 속도: ×{effectiveSpeed:0.##}"
            : "연구 속도: 정지";
        _speedReasonLabel.text = researchMultiplier > 1f
            ? $"{powerReason} · 연구 보너스 ×{researchMultiplier:0.##}"
            : powerReason;

        if (!researchBuilding.cycleActive)
        {
            _remainingTimeLabel.text = "주기 시작 대기";
            return;
        }

        _remainingTimeLabel.text = effectiveSpeed > 0f
            ? $"주기 남은 시간: {math.max(0f, duration - researchBuilding.progress) / effectiveSpeed:0.0}초"
            : "주기 남은 시간: 전력 공급 대기";
    }
}
