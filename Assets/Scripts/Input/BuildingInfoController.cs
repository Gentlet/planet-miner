using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public readonly struct BuildingInfoTaskRow
{
    public readonly Entity Task;
    public readonly string Text;
    public readonly bool CanCancel;
    public BuildingInfoTaskRow(Entity task, string text, bool canCancel = true)
    {
        Task = task;
        Text = text;
        CanCancel = canCancel;
    }
}

/// <summary>선택 원본의 화면 투영과 요청 접수를 담당한다. 재고와 작업 상태는 ECS에 유지한다.</summary>
[RequireComponent(typeof(WorldSelectionController))]
public sealed partial class BuildingInfoController : PeriodicWorldView
{
    private WorldSelectionController _selection;
    private World _world;
    private ulong _feedbackToken;
    private bool _noticeInitialized;
    private uint _observedResetRevision;
    private float _resetNoticeUntil;
    private ResearchCycleResetReasonEnum _resetReason;
    private readonly StringBuilder _text = new StringBuilder();
    public readonly List<BuildingInfoTaskRow> Tasks = new List<BuildingInfoTaskRow>();
    public readonly List<ItemTypeEnum> ItemTypes = new List<ItemTypeEnum>();
    public string Title { get; private set; }
    public string Details { get; private set; }
    public string Feedback { get; private set; }
    public bool HasCrafting { get; private set; }
    public bool HasEditableFilter { get; private set; }
    public bool HasGeneralStorage { get; private set; }
    public bool HasInput { get; private set; }
    public bool HasProductOutput { get; private set; }
    public bool HasSelection { get; private set; }
    public int SelectedRecipe { get; private set; }
    public bool CanEditRecipe { get; private set; }
    public StorageFilter Filter { get; private set; }
    internal float? MiningProgress { get; private set; }
    internal float? CraftingProgress { get; private set; }
    internal string CraftingStatus { get; private set; }
    internal FuelEnergyState? FuelProgress { get; private set; }

    public event Action Changed;
    public event Action SelectionChanged;
    private void OnEnable()
    {
        _selection = GetComponent<WorldSelectionController>();
        _selection.Changed += OnSelectionChanged;
        OnSelectionChanged();
    }

    private void OnDisable()
    {
        if (_selection != null)
        {
            _selection.Changed -= OnSelectionChanged;
        }

        WorldUiRequestFeedbackRelay.Unregister(_feedbackToken);
        _feedbackToken = 0;
        _world = null;
        HasSelection = false;
        Changed?.Invoke();
    }

    private void OnSelectionChanged()
    {
        _noticeInitialized = false;
        _resetNoticeUntil = 0f;
        WorldUiRequestFeedbackRelay.ClearPending(_feedbackToken);
        Feedback = string.Empty;
        Refresh();
        SelectionChanged?.Invoke();
    }

    public override void Refresh()
    {
        if (!ReferenceEquals(_world, _selection.SelectedWorld))
        {
            WorldUiRequestFeedbackRelay.Unregister(_feedbackToken);
            _feedbackToken = 0;
            _world = _selection.SelectedWorld;
            _feedbackToken = WorldUiRequestFeedbackRelay.Register(_world, ReceiveFeedback);
        }

        ResetBuildingDisplay();
        Tasks.Clear();
        ItemTypes.Clear();
        HasCrafting = false;
        HasEditableFilter = false;
        HasGeneralStorage = false;
        HasInput = false;
        HasProductOutput = false;
        CanEditRecipe = false;
        MiningProgress = null;
        CraftingProgress = null;
        CraftingStatus = string.Empty;
        FuelProgress = null;
        HasSelection = CanSubmit();
        if (!HasSelection)
        {
            Title = string.Empty;
            Details = string.Empty;
            Changed?.Invoke();
            return;
        }

        var manager = _world.EntityManager;
        Entity entity = _selection.Selected;
        _text.Clear();
        HasCrafting = manager.HasComponent<CrafterState>(entity);
        HasEditableFilter = manager.HasComponent<EditableStorageFilter>(entity);
        HasGeneralStorage = DroneTransportEndpointUtility.IsArea(manager, entity, DroneTransportAreaEnum.GeneralStorage);
        HasInput = DroneTransportEndpointUtility.IsArea(manager, entity, DroneTransportAreaEnum.Input);
        HasProductOutput = DroneTransportEndpointUtility.IsArea(manager, entity, DroneTransportAreaEnum.Product);
        BuildingConfigElement buildingConfig = default;
        bool site = manager.HasComponent<ConstructionSite>(entity);
        if (site || manager.HasComponent<BuildingIdentity>(entity))
        {
            int buildingId = site ? manager.GetComponentData<ConstructionSite>(entity).TargetBuildingId
                : manager.GetComponentData<BuildingIdentity>(entity).BuildingId;
            Title = site ? "공사 현장" : "건물 " + buildingId;
            using var configs = manager.CreateEntityQuery(typeof(BuildingConfig), typeof(BuildingConfigElement));
            if (configs.CalculateEntityCount() == 1 &&
                BuildingConfigLookupUtility.TryGetConfig(manager.GetBuffer<BuildingConfigElement>(configs.GetSingletonEntity(), true),
                    buildingId, out var config))
            {
                buildingConfig = config;
                Title = config.DisplayName.ToString() + " T" + config.Tier;
            }
        }
        else
        {
            Title = manager.HasComponent<DroneWorker>(entity) ? "드론" :
                (manager.HasComponent<ItemIdentity>(entity) ? manager.GetComponentData<ItemIdentity>(entity).Type.ToString() : "선택 대상");
        }

        if (manager.HasComponent<GridPosition>(entity))
        {
            var position = manager.GetComponentData<GridPosition>(entity).Value;
            _text.AppendLine("위치 " + position.x + ", " + position.y);
        }

        if (manager.HasComponent<CrafterState>(entity))
        {
            var state = manager.GetComponentData<CrafterState>(entity);
            SelectedRecipe = state.SelectedRecipeId;
            CanEditRecipe = BuildingSettingsUtility.IsEditableCrafter(manager, entity);
            CraftingProgress = state.Progress;
            CraftingStatus = state.Status switch
            {
                CrafterStatusEnum.NoRecipe => "레시피 미선택",
                CrafterStatusEnum.WaitingForInput => "재료 대기",
                CrafterStatusEnum.WaitingForOutput => "출력 공간 대기",
                CrafterStatusEnum.WaitingForByproductOutput => "기존 생산품 배출 대기",
                _ => string.Empty
            };
        }

        if (manager.HasComponent<PlannedBuildingSettings>(entity))
        {
            var planned = manager.GetComponentData<PlannedBuildingSettings>(entity);
            if (planned.HasRecipe)
            {
                _text.AppendLine("완공 예정 레시피 " + planned.RecipeId);
            }

            if (planned.HasStorageFilter)
            {
                _text.AppendLine("완공 예정 필터 " + planned.Filter.Mode);
            }
        }

        ReadBelt(manager, entity);
        if (manager.HasComponent<ResearchCycleState>(entity))
        {
            ResearchStatusTextUtility.Append(manager, entity, _text);
            ReadResearchResetNotice(manager);
        }
        if (manager.HasComponent<DroneWorker>(entity))
        {
            ReadDrone(manager, entity);
        }
        if (manager.HasComponent<MinerState>(entity))
        {
            var state = manager.GetComponentData<MinerState>(entity);
            MiningProgress = state.Progress;
        }

        if (manager.HasComponent<PowerDemand>(entity))
        {
            bool prepared = manager.IsComponentEnabled<PowerDemand>(entity);
            _text.AppendLine(prepared ? "전력 요청 " + manager.GetComponentData<PowerDemand>(entity).RequestedEnergy.ToString("F2") : "전력 요청 미준비");
        }

        if (manager.HasComponent<PowerSupplyResult>(entity))
        {
            var supply = manager.GetComponentData<PowerSupplyResult>(entity);
            _text.AppendLine("배정 에너지 " + supply.AllocatedEnergy.ToString("F2") + " / 공급 " + (supply.SupplyRatio * 100f).ToString("F0") + "%");
        }

        if (manager.HasComponent<PowerGeneratorSupplyPlan>(entity))
        {
            bool prepared = manager.IsComponentEnabled<PowerGeneratorSupplyPlan>(entity);
            var plan = manager.GetComponentData<PowerGeneratorSupplyPlan>(entity);
            _text.AppendLine(prepared ? "발전 가용 " + plan.AvailableEnergy.ToString("F2") + " / 사용 " + plan.AllocatedEnergy.ToString("F2") : "발전 계획 미준비");
        }

        if (manager.HasComponent<FuelEnergyState>(entity))
        {
            FuelProgress = manager.GetComponentData<FuelEnergyState>(entity);
        }

        if (manager.HasComponent<StorageFilter>(entity))
        {
            Filter = manager.GetComponentData<StorageFilter>(entity);
        }

        ReadInventorySlots(manager, entity);
        ReadTasks(manager, entity);
        ReadCatalog(manager);
        HasPowerGridSection = manager.HasComponent<PowerPole>(entity);
        if (HasPowerGridSection)
        {
            GridDisplay = BuildingInfoPowerGrid.Read(_world, entity);
        }
        if (manager.HasBuffer<ConstructionMaterialRequirementElement>(entity))
        {
            foreach (var material in manager.GetBuffer<ConstructionMaterialRequirementElement>(entity, true))
            {
                _text.AppendLine(material.ItemType + " " + material.DeliveredQuantity + "/" + material.RequiredQuantity + " (예약 " + material.ReservedQuantity + ")");
            }
        }

        Details = _text.ToString();
        Changed?.Invoke();
    }

    private void ReadResearchResetNotice(EntityManager manager)
    {
        using var query = manager.CreateEntityQuery(typeof(ResearchRegistry), typeof(ResearchCycleResetResult));
        if (query.CalculateEntityCount() != 1)
        {
            return;
        }
        var notice = manager.GetComponentData<ResearchCycleResetResult>(query.GetSingletonEntity());
        if (_noticeInitialized && notice.Revision != _observedResetRevision)
        {
            _resetNoticeUntil = Time.unscaledTime + 2.5f;
            _resetReason = notice.Reason;
        }
        _noticeInitialized = true;
        _observedResetRevision = notice.Revision;
        if (Time.unscaledTime < _resetNoticeUntil)
        {
            _text.AppendLine(_resetReason == ResearchCycleResetReasonEnum.SelectionChanged
                ? "연구 변경으로 로컬 주기 초기화" : "전역 연구 완료로 로컬 주기 초기화");
        }
    }

    private void ReadDrone(EntityManager manager, Entity drone)
    {
        FindDroneStation(manager, drone);
        if (manager.HasComponent<DroneBatteryState>(drone))
        {
            using var maximum = manager.CreateEntityQuery(typeof(DroneBatteryConfig));
            float current = manager.GetComponentData<DroneBatteryState>(drone).CurrentCharge;
            string capacity = maximum.CalculateEntityCount() == 1
                ? manager.GetComponentData<DroneBatteryConfig>(maximum.GetSingletonEntity()).MaximumCharge.ToString("F2") : "미준비";
            _text.AppendLine("배터리 " + current.ToString("F2") + "/" + capacity);
        }
        if (manager.HasComponent<DroneActivityState>(drone))
        {
            _text.AppendLine("활동 " + manager.GetComponentData<DroneActivityState>(drone).Activity);
        }
        if (manager.HasComponent<DroneMovementDecision>(drone))
        {
            var movement = manager.GetComponentData<DroneMovementDecision>(drone);
            _text.AppendLine(movement.Ready ? "이동 목적 " + movement.Purpose + " / 목표 " + movement.Target : "이동 판단 준비 중");
        }
        if (manager.HasComponent<DroneDocked>(drone))
        {
            _text.AppendLine("정박 " + manager.GetComponentData<DroneDocked>(drone).Station);
        }
        if (manager.HasComponent<DroneWorkerAssignment>(drone))
        {
            Entity assignment = manager.GetComponentData<DroneWorkerAssignment>(drone).Assignment;
            if (manager.HasComponent<DroneTaskAssignment>(assignment))
            {
                var work = manager.GetComponentData<DroneTaskAssignment>(assignment);
                _text.AppendLine("작업 " + work.Task + " / " + work.State + " / " + work.ItemType + " " + work.AssignedQuantity);
            }
            else
            {
                _text.AppendLine("배정 작업 없음");
            }
        }
        if (manager.HasComponent<DroneCargoState>(drone))
        {
            _text.AppendLine("실제 적재 " + manager.GetComponentData<DroneCargoState>(drone).Quantity + "개");
        }
    }

    private void ReadBelt(EntityManager manager, Entity entity)
    {
        if (!manager.HasComponent<BeltComponent>(entity))
        {
            return;
        }

        _text.AppendLine("기본 벨트 속도 " + manager.GetComponentData<BeltComponent>(entity).Speed.ToString("F2"));
        using var query = manager.CreateEntityQuery(typeof(ItemSpatialIndex), typeof(ItemSpatialIndexFence));
        if (query.CalculateEntityCount() != 1)
        {
            return;
        }

        Entity indexOwner = query.GetSingletonEntity();
        // ECS 필드 의존성과 별개인 공유 Native 맵의 마지막 Writer만 완료한 뒤 동기적으로 읽는다.
        manager.GetComponentData<ItemSpatialIndexFence>(indexOwner).State.GetReaderDependency().Complete();
        var index = manager.GetComponentData<ItemSpatialIndex>(indexOwner);
        var position = manager.GetComponentData<GridPosition>(entity).Value;
        if (!index.TryGetFirstItem(position, out Entity item, out var iterator))
        {
            return;
        }

        do
        {
            if (!manager.Exists(item))
            {
                continue;
            }

            if (!manager.HasComponent<ItemIdentity>(item))
            {
                continue;
            }

            if (DroneSchedulingUtility.HasDestroy(manager, item))
            {
                continue;
            }

            _text.AppendLine("벨트 셀 아이템 " + manager.GetComponentData<ItemIdentity>(item).Type);
        }
        while (index.TryGetNextItem(out item, ref iterator));
    }

    private void ReadTasks(EntityManager manager, Entity entity)
    {
        Entity demolition = Entity.Null;
        if (manager.HasComponent<PendingBuildingDemolition>(entity))
            demolition = manager.GetComponentData<PendingBuildingDemolition>(entity).Task;
        else if (manager.HasComponent<PreparingStationDemolition>(entity))
            demolition = manager.GetComponentData<PreparingStationDemolition>(entity).Task;
        if (manager.HasComponent<DroneLogisticsTask>(demolition) && manager.HasComponent<DroneDemolitionProgress>(demolition))
        {
            var task = manager.GetComponentData<DroneLogisticsTask>(demolition);
            var status = manager.GetComponentData<DroneDemolitionProgress>(demolition);
            string text = task.State == DroneLogisticsTaskStateEnum.ExecutionConfirmed ? "철거 확정" :
                status.AwaitingRelocation ? "정거장 드론 이전 중" :
                status.AwaitingSpace && status.RemainingStock > 0 ? $"창고 공간 부족 · 재고 {status.RemainingStock}개 남음" :
                status.AwaitingSpace ? "창고 공간 부족" :
                status.RemainingStock > 0 ? $"재고 {status.RemainingStock}개 남음" :
                status.RequiredWorkers == 0 ? "재고 확인 중" :
                status.AssignedWorkers == 0 ? $"운반 가능한 드론 대기 · {status.RequiredWorkers}기 필요" :
                $"환급 드론 {status.ArrivedWorkers}/{status.AssignedWorkers}기 도착";
            if (task.State == DroneLogisticsTaskStateEnum.Open || task.State == DroneLogisticsTaskStateEnum.ExecutionConfirmed)
                Tasks.Add(new BuildingInfoTaskRow(demolition, text, task.State == DroneLogisticsTaskStateEnum.Open));
        }

        using var query = manager.CreateEntityQuery(typeof(DroneLogisticsTask), typeof(DroneTransportRequirement), typeof(DroneTransportProgress));
        using var tasks = query.ToEntityArray(Allocator.Temp);
        foreach (Entity root in tasks)
        {
            var task = manager.GetComponentData<DroneLogisticsTask>(root);
            if (task.Target != entity || task.DemolitionRoot != Entity.Null)
            {
                continue;
            }

            var goal = manager.GetComponentData<DroneTransportRequirement>(root);
            var progress = manager.GetComponentData<DroneTransportProgress>(root);
            Tasks.Add(new BuildingInfoTaskRow(root, task.Kind + " / " + task.Area + " / " + task.ItemType + " " + progress.DeliveredQuantity + "/" + goal.Quantity + " (확보 " + progress.ReservedQuantity + ") " + task.State));
        }
    }

    private void ReadCatalog(EntityManager manager)
    {
        using (var query = manager.CreateEntityQuery(typeof(ItemRegistry), typeof(ItemConfigElement)))
        {
            if (query.CalculateEntityCount() == 1)
            {
                foreach (var item in manager.GetBuffer<ItemConfigElement>(query.GetSingletonEntity(), true))
                {
                    if (item.ItemType != ItemTypeEnum.None && item.MaxStack > 0)
                    {
                        ItemTypes.Add(item.ItemType);
                    }
                }
            }
        }

        ReadRecipeChoices(manager);
    }

    public void RequestTransport(DroneLogisticsTaskKindEnum kind, DroneTransportAreaEnum area, ItemTypeEnum type, int quantity)
    {
        if (!CanSubmit())
        {
            return;
        }

        Entity request = DroneTaskRequestUtility.SubmitTransport(_world.EntityManager, kind, _selection.Selected, type, quantity, area);
        WorldUiRequestFeedbackRelay.Track(_world.EntityManager, _feedbackToken, request);
        Feedback = "요청 접수 대기";
        Changed?.Invoke();
    }

    public void CancelTask(Entity task)
    {
        if (!CanSubmit())
        {
            return;
        }

        Entity request = DroneTaskRequestUtility.SubmitCancel(_world.EntityManager, task);
        WorldUiRequestFeedbackRelay.Track(_world.EntityManager, _feedbackToken, request);
        Feedback = "요청 접수 대기";
        Changed?.Invoke();
    }

    internal void ActivateStoredDrone(int sourceSlotIndex)
    {
        if (!CanSubmit())
        {
            return;
        }
        if (!_world.EntityManager.HasComponent<DroneStation>(_selection.Selected))
        {
            return;
        }

        SubmitStationRequest(Entity.Null, _selection.Selected, DroneStationOperation.ActivateStored, sourceSlotIndex);
    }

    internal void RequestDroneStation(DroneStationOperation operation)
    {
        if (!CanSubmit())
        {
            return;
        }
        if (!TryGetDroneStation(_world.EntityManager, _selection.Selected, out var station, out _))
        {
            return;
        }

        SubmitStationRequest(_selection.Selected, station, operation);
    }

    private void SubmitStationRequest(Entity drone, Entity station, DroneStationOperation operation, int sourceSlotIndex = -1)
    {
        Entity request = DroneStationRequestUtility.Submit(_world.EntityManager, drone, station, operation, sourceSlotIndex);
        WorldUiRequestFeedbackRelay.Track(_world.EntityManager, _feedbackToken, request);
        Feedback = "요청 접수 대기";
        Changed?.Invoke();
    }

    public void SubmitSettings(in PlannedBuildingSettings settings)
    {
        if (!CanSubmit())
        {
            return;
        }

        BuildingSettingsUtility.Submit(_world.EntityManager, _selection.Selected, settings);
    }

    private bool CanSubmit()
    {
        if (!isActiveAndEnabled)
        {
            return false;
        }

        if (_world == null)
        {
            return false;
        }

        if (!_world.IsCreated)
        {
            return false;
        }

        if (!ReferenceEquals(_world, World.DefaultGameObjectInjectionWorld))
        {
            return false;
        }

        if (!ReferenceEquals(_world, _selection.SelectedWorld))
        {
            return false;
        }

        if (_selection.Selected == Entity.Null)
        {
            return false;
        }

        return _world.EntityManager.Exists(_selection.Selected);
    }

    private void ReceiveFeedback(WorldUiRequestFeedback result)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }


        // 선택 갱신보다 이전 World의 결과 소비가 먼저 실행될 수 있다.
        if (!ReferenceEquals(_world, World.DefaultGameObjectInjectionWorld))
        {
            return;
        }

        Feedback = result.Message;
        Changed?.Invoke();
    }
}
