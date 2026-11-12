using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public readonly struct BuildingInfoItemRow
{
    public readonly DroneTransportAreaEnum Area;
    public readonly ItemTypeEnum Type;
    public readonly int Count;
    public BuildingInfoItemRow(DroneTransportAreaEnum area, ItemTypeEnum type, int count)
    {
        Area = area;
        Type = type;
        Count = count;
    }
}

public readonly struct BuildingInfoTaskRow
{
    public readonly Entity Task;
    public readonly string Text;
    public BuildingInfoTaskRow(Entity task, string text)
    {
        Task = task;
        Text = text;
    }
}

public readonly struct BuildingInfoDroneRow
{
    public readonly Entity Drone;
    public readonly string Text;
    public readonly bool Stored;
    public readonly bool Docked;
    public BuildingInfoDroneRow(Entity drone, string text, bool stored, bool docked)
    {
        Drone = drone;
        Text = text;
        Stored = stored;
        Docked = docked;
    }
}

/// <summary>선택 원본의 화면 투영/편집 초안/요청 접수만 담당한다. 재고와 작업 상태는 ECS에 유지한다.</summary>
[RequireComponent(typeof(WorldSelectionController))]
public sealed class BuildingInfoController : MonoBehaviour
{
    private WorldSelectionController _selection;
    private World _world;
    private ulong _feedbackToken;
    private readonly HashSet<Entity> _pending = new HashSet<Entity>();
    private float _nextRefresh;
    private bool _noticeInitialized;
    private uint _observedResetRevision;
    private float _resetNoticeUntil;
    private ResearchCycleResetReasonEnum _resetReason;
    private readonly StringBuilder _text = new StringBuilder();
    public readonly List<BuildingInfoItemRow> Items = new List<BuildingInfoItemRow>();
    public readonly List<BuildingInfoTaskRow> Tasks = new List<BuildingInfoTaskRow>();
    public readonly List<BuildingInfoDroneRow> Drones = new List<BuildingInfoDroneRow>();
    public readonly List<ItemTypeEnum> ItemTypes = new List<ItemTypeEnum>();
    public readonly List<int> RecipeIds = new List<int>();
    public string Title { get; private set; }
    public string Details { get; private set; }
    public string Feedback { get; private set; }
    public BuildingTypeEnum Type { get; private set; }
    public bool HasSelection { get; private set; }
    public int SelectedRecipe { get; private set; }
    public StorageFilter Filter { get; private set; }

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

        Disconnect();
        HasSelection = false;
        Changed?.Invoke();
    }

    private void Disconnect()
    {
        WorldUiRequestFeedbackRelay.Unregister(_feedbackToken);
        _feedbackToken = 0;
        _pending.Clear();
        _world = null;
    }

    private void OnSelectionChanged()
    {
        _noticeInitialized = false;
        _resetNoticeUntil = 0f;
        _pending.Clear();
        Feedback = string.Empty;
        Refresh();
        SelectionChanged?.Invoke();
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime < _nextRefresh)
        {
            return;
        }

        _nextRefresh = Time.unscaledTime + 0.2f;
        Refresh();
    }

    public void Refresh()
    {
        if (!ReferenceEquals(_world, _selection.SelectedWorld))
        {
            Disconnect();
            _world = _selection.SelectedWorld;
            RegisterFeedback();
        }

        Items.Clear();
        Tasks.Clear();
        Drones.Clear();
        ItemTypes.Clear();
        RecipeIds.Clear();
        Type = BuildingTypeEnum.None;
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
        if (manager.HasComponent<BuildingType>(entity))
        {
            var building = manager.GetComponentData<BuildingType>(entity);
            Type = building.Type;
            Title = building.Type + " T" + building.Tier;
            using var configs = manager.CreateEntityQuery(typeof(BuildingConfig), typeof(BuildingConfigElement));
            if (configs.CalculateEntityCount() == 1 &&
                BuildingConfigLookupUtility.TryGetConfig(manager.GetBuffer<BuildingConfigElement>(configs.GetSingletonEntity(), true),
                    building.BuildingId, out var config))
            {
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
            _text.AppendLine(state.Status + " / " + (state.Progress * 100f).ToString("F0") + "%");
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
            _text.AppendLine("채굴 진행 " + (state.Progress * 100f).ToString("F0") + "%");
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
            _text.AppendLine("연료 잔여 에너지 " + manager.GetComponentData<FuelEnergyState>(entity).RemainingEnergy.ToString("F2"));
        }

        if (manager.HasComponent<StorageFilter>(entity))
        {
            Filter = manager.GetComponentData<StorageFilter>(entity);
        }

        ReadItems(manager, entity);
        ReadTasks(manager, entity);
        ReadCatalog(manager);
        ReadStation(manager, entity);
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
        if (manager.HasBuffer<StoredItemElement>(drone))
        {
            _text.AppendLine("실제 적재 " + manager.GetBuffer<StoredItemElement>(drone, true).Length + "개");
        }
    }

    private void RegisterFeedback()
    {
        if (_world == null)
        {
            return;
        }

        if (!_world.IsCreated)
        {
            return;
        }

        _feedbackToken = WorldUiRequestFeedbackRelay.Register(_world, ReceiveFeedback);
    }

    private void ReadItems(EntityManager manager, Entity entity)
    {
        var counts = new Dictionary<(DroneTransportAreaEnum, ItemTypeEnum), int>();
        if (manager.HasBuffer<StoredItemElement>(entity))
        {
            bool general = DroneSchedulingUtility.IsStorage(manager, entity);
            int generalSlots = manager.HasComponent<Storage>(entity) ? manager.GetComponentData<Storage>(entity).SlotCount - StationStorageUtility.ReadDedicatedCount(manager, entity) : int.MaxValue;
            foreach (var item in manager.GetBuffer<StoredItemElement>(entity, true))
            {
                if (general && item.SlotIndex >= generalSlots)
                {
                    continue;
                }

                var key = (general ? DroneTransportAreaEnum.GeneralStorage : DroneTransportAreaEnum.Input, item.ItemType);
                counts.TryGetValue(key, out int count);
                counts[key] = count + 1;
            }
        }

        if (manager.HasBuffer<ProductItemElement>(entity))
        {
            foreach (var item in manager.GetBuffer<ProductItemElement>(entity, true))
            {
                var key = (DroneTransportAreaEnum.Product, item.ItemType);
                counts.TryGetValue(key, out int count);
                counts[key] = count + 1;
            }
        }

        foreach (var entry in counts)
        {
            Items.Add(new BuildingInfoItemRow(entry.Key.Item1, entry.Key.Item2, entry.Value));
        }

        Items.Sort((a, b) => a.Area != b.Area ? a.Area.CompareTo(b.Area) : a.Type.CompareTo(b.Type));
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
        manager.GetComponentData<ItemSpatialIndexFence>(indexOwner).GetReaderDependency().Complete();
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

            if (!manager.HasComponent<ItemIdentity>(item) || !manager.HasComponent<ItemOwnership>(item))
            {
                continue;
            }

            if (!manager.GetComponentData<ItemOwnership>(item).IsWorldItem)
            {
                continue;
            }

            _text.AppendLine("벨트 셀 아이템 " + manager.GetComponentData<ItemIdentity>(item).Type);
        }
        while (index.TryGetNextItem(out item, ref iterator));
    }

    private void ReadTasks(EntityManager manager, Entity entity)
    {
        using var query = manager.CreateEntityQuery(typeof(DroneLogisticsTask), typeof(DroneTransportRequirement), typeof(DroneTransportProgress));
        using var tasks = query.ToEntityArray(Allocator.Temp);
        foreach (Entity root in tasks)
        {
            var task = manager.GetComponentData<DroneLogisticsTask>(root);
            if (task.Target != entity)
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

        RecipeIds.Add(0);
        using var recipes = manager.CreateEntityQuery(typeof(RecipeRegistry), typeof(RecipeConfigElement));
        if (recipes.CalculateEntityCount() != 1)
        {
            return;
        }

        foreach (var recipe in manager.GetBuffer<RecipeConfigElement>(recipes.GetSingletonEntity(), true))
        {
            if (recipe.IsUnlocked)
            {
                RecipeIds.Add(recipe.Id);
            }
        }
    }

    private void ReadStation(EntityManager manager, Entity entity)
    {
        if (!manager.HasComponent<DroneStation>(entity))
        {
            return;
        }

        int dedicated = StationStorageUtility.ReadDedicatedCount(manager, entity);
        if (manager.HasComponent<DroneStationNetwork>(entity))
        {
            _text.AppendLine("공개 네트워크 " + manager.GetComponentData<DroneStationNetwork>(entity).RepresentativeStation);
        }

        _text.AppendLine("일반 슬롯 " + (manager.GetComponentData<Storage>(entity).SlotCount - dedicated) + " / 드론 전용 " + dedicated);
        if (manager.HasBuffer<DockedDroneElement>(entity))
        {
            int charging = 0;
            int waiting = 0;
            int docked = 0;
            foreach (var entry in manager.GetBuffer<DockedDroneElement>(entity, true))
            {
                if (!manager.HasComponent<DroneActivityState>(entry.Drone))
                {
                    continue;
                }
                docked++;
                var activity = manager.GetComponentData<DroneActivityState>(entry.Drone).Activity;
                if (activity == DroneActivityKind.Charging)
                {
                    charging++;
                }
                if (activity == DroneActivityKind.WaitingForCharge)
                {
                    waiting++;
                }
            }
            int stored = 0;
            foreach (var entry in manager.GetBuffer<StoredItemElement>(entity, true))
            {
                if (entry.ItemType == ItemTypeEnum.Drone)
                {
                    stored++;
                }
            }
            _text.AppendLine("수납 " + stored + " / 정박 " + docked + " / 충전 " + charging + " / 충전 대기 " + waiting);
        }
        foreach (var item in manager.GetBuffer<StoredItemElement>(entity, true))
        {
            if (item.ItemType == ItemTypeEnum.Drone)
            {
                Drones.Add(new BuildingInfoDroneRow(item.ItemEntity, "슬롯 " + item.SlotIndex + " / 수납 드론", true, false));
            }
        }

        using var query = manager.CreateEntityQuery(typeof(DroneWorker), typeof(GridPosition));
        using var workers = query.ToEntityArray(Allocator.Temp);
        var origin = manager.GetComponentData<GridPosition>(entity).Value;
        var size = manager.GetComponentData<BuildingFootprint>(entity).GetEffectiveSize(manager.GetComponentData<Direction>(entity).dir);
        foreach (Entity worker in workers)
        {
            bool docked = manager.HasComponent<DroneDocked>(worker) && manager.GetComponentData<DroneDocked>(worker).Station == entity;
            var position = manager.GetComponentData<GridPosition>(worker).Value;
            bool inside = position.x >= origin.x && position.y >= origin.y && (long)position.x < (long)origin.x + size.x && (long)position.y < (long)origin.y + size.y;
            if (!docked && !inside)
            {
                continue;
            }

            string label = docked ? "정박 드론" : "정거장 범위 내 드론";
            Drones.Add(new BuildingInfoDroneRow(worker, label, false, docked));
        }
    }

    public void RequestTransport(DroneLogisticsTaskKindEnum kind, DroneTransportAreaEnum area, ItemTypeEnum type, int quantity)
    {
        if (!CanSubmit())
        {
            return;
        }

        Track(DroneTaskRequestUtility.SubmitTransport(_world.EntityManager, kind, _selection.Selected, type, quantity, area));
    }

    public void CancelTask(Entity task)
    {
        if (!CanSubmit())
        {
            return;
        }

        Track(DroneTaskRequestUtility.SubmitCancel(_world.EntityManager, task));
    }

    public void RequestStation(Entity drone, DroneStationOperation operation)
    {
        if (!CanSubmit())
        {
            return;
        }

        Track(DroneStationRequestUtility.Submit(_world.EntityManager, drone, _selection.Selected, operation));
    }

    public void SetRecipe(int recipe)
    {
        if (!CanSubmit())
        {
            return;
        }

        BuildingSettingsUtility.Submit(_world.EntityManager, _selection.Selected, new PlannedBuildingSettings { HasRecipe = true, RecipeId = recipe });
    }

    public void SetFilter(StorageFilter filter)
    {
        if (!CanSubmit())
        {
            return;
        }

        BuildingSettingsUtility.Submit(_world.EntityManager, _selection.Selected, new PlannedBuildingSettings { HasStorageFilter = true, Filter = filter });
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

    private void Track(Entity request)
    {
        _world.EntityManager.AddComponentData(request, new WorldUiRequestOwner { FeedbackToken = _feedbackToken });
        _pending.Add(request);
        Feedback = "요청 접수 대기";
        Changed?.Invoke();
    }

    private void ReceiveFeedback(WorldUiRequestFeedback result)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        if (!_pending.Remove(result.Request))
        {
            return;
        }

        Feedback = result.Message;
        Changed?.Invoke();
    }
}
