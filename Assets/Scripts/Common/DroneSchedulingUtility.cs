using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 드론 후보/경로/배정의 현재성과 현장·공급원·보관처·실물 자격을 같은 규칙으로 조회한다.
/// 입력·출력: ECS 원본과 외부 관측/경로 결과를 읽고 수량·유효성·우선순위를 계산한다. 공급원 재고/보관 공간을 예약하거나 실물을 옮기지 않는다.
/// 이용: DroneTaskDecisionSystem, ConstructionSupplyReservationSystem, DroneTaskExecutionSystem 및 Publish/Lifecycle의 적용 검사가 공유한다.
/// 수명·소유권: GetOrCreateCandidates만 World 단일 의도/후보 버퍼 엔티티를 준비한다. 나머지 조회는 원본 쓰기 없이 적용 가능성을 반환한다.
/// 실제 위치 원본·경로 평가 Producer는 후속이다. 관측/평가 결과와 revision을 확인하는 것이 실제 비행 구현을 뜻하지 않는다.
/// </summary>
public static class DroneSchedulingUtility
{
    public static Entity GetOrCreateCandidates(EntityManager manager)
    {
        using var query = manager.CreateEntityQuery(ComponentType.ReadWrite<DroneTaskCandidateDecisionElement>());
        if (!query.IsEmptyIgnoreFilter)
        {
            Entity existing = query.GetSingletonEntity();
            if (!manager.HasBuffer<DroneTaskPendingPublicationElement>(existing))
            {
                manager.AddBuffer<DroneTaskPendingPublicationElement>(existing);
            }
            if (!manager.HasBuffer<DroneRouteDecisionElement>(existing))
            {
                manager.AddBuffer<DroneRouteDecisionElement>(existing);
            }
            if (!manager.HasBuffer<DroneTaskCreationDecisionElement>(existing))
            {
                manager.AddBuffer<DroneTaskCreationDecisionElement>(existing);
            }
            if (!manager.HasBuffer<DroneTaskInvalidationDecisionElement>(existing))
            {
                manager.AddBuffer<DroneTaskInvalidationDecisionElement>(existing);
            }
            return existing;
        }

        return manager.CreateEntity(typeof(DroneTaskCandidateDecisionElement), typeof(DroneTaskPendingPublicationElement), typeof(DroneRouteDecisionElement),
            typeof(DroneTaskCreationDecisionElement), typeof(DroneTaskInvalidationDecisionElement));
    }

    /// <summary>초기 능력/연구 Writer가 게시한 공통 값을 읽는다. 미게시 상태에 임의 기본값을 만들지 않는다.</summary>
    public static int ReadCarryingCapacity(in EntityQuery capacityQuery)
    {
        if (capacityQuery.IsEmptyIgnoreFilter)
        {
            return 0;
        }

        return math.max(0, capacityQuery.GetSingleton<DroneCapacityState>().CarryingCapacity);
    }

    public static bool IsActiveEntity(EntityManager manager, Entity entity)
    {
        if (entity == Entity.Null)
        {
            return false;
        }

        if (!manager.Exists(entity))
        {
            return false;
        }

        return !manager.HasComponent<Disabled>(entity) && !manager.HasComponent<Prefab>(entity);
    }

    public static bool IsStorage(EntityManager manager, Entity entity)
    {
        if (!IsActiveEntity(manager, entity))
        {
            return false;
        }

        if (manager.HasComponent<PendingBuildingDemolition>(entity) ||
            !manager.HasComponent<BuildingType>(entity) || !manager.HasComponent<GridPosition>(entity) ||
            !manager.HasComponent<Storage>(entity) || !manager.HasBuffer<StoredItemElement>(entity))
        {
            return false;
        }

        var type = manager.GetComponentData<BuildingType>(entity).Type;
        return type == BuildingTypeEnum.Storage || type == BuildingTypeEnum.MainFacility ||
               type == BuildingTypeEnum.DroneStation;
    }

    public static bool IsSite(EntityManager manager, Entity site)
    {
        if (!IsActiveEntity(manager, site))
        {
            return false;
        }

        if (!manager.HasComponent<ConstructionSite>(site) || !manager.HasComponent<GridPosition>(site) ||
            !manager.HasBuffer<ConstructionMaterialRequirementElement>(site))
        {
            return false;
        }

        return (manager.GetComponentData<ConstructionSite>(site).Flags & ConstructionSiteFlags.Cancelled) == 0;
    }

    public static bool IsWorldItem(EntityManager manager, Entity item, ItemTypeEnum type)
    {
        if (!IsActiveEntity(manager, item))
        {
            return false;
        }

        if (!manager.HasComponent<ItemIdentity>(item) || !manager.HasComponent<ItemOwnership>(item) ||
            !manager.HasComponent<GridPosition>(item) || HasDestroy(manager, item))
        {
            return false;
        }

        return type != ItemTypeEnum.None && manager.GetComponentData<ItemIdentity>(item).Type == type &&
               manager.GetComponentData<ItemOwnership>(item).IsWorldItem;
    }

    public static bool HasDestroy(EntityManager manager, Entity item)
    {
        return manager.HasComponent<DestroyItemRequest>(item) && manager.IsComponentEnabled<DestroyItemRequest>(item);
    }

    public static bool IsIdleWorker(EntityManager manager, Entity worker, int carryingCapacity)
    {
        if (!IsActiveEntity(manager, worker))
        {
            return false;
        }

        if (!manager.HasComponent<DroneWorker>(worker) || !manager.HasComponent<DroneWorkerObservation>(worker) ||
            !manager.HasComponent<DroneWorkerAssignment>(worker) || !manager.HasComponent<DroneCargoState>(worker) ||
            !manager.HasBuffer<StoredItemElement>(worker))
        {
            return false;
        }

        var observation = manager.GetComponentData<DroneWorkerObservation>(worker);
        return carryingCapacity > 0 && observation.Revision > 0 &&
               observation.CanAcceptTask && math.all(math.isfinite(observation.Position)) &&
               manager.GetComponentData<DroneWorkerAssignment>(worker).Assignment == Entity.Null &&
               manager.GetComponentData<DroneCargoState>(worker).Origin == DroneCargoOriginEnum.None &&
               manager.GetBuffer<StoredItemElement>(worker, true).IsEmpty;
    }

    public static int CountInventory(EntityManager manager, Entity storage, ItemTypeEnum type)
    {
        if (!IsStorage(manager, storage))
        {
            return 0;
        }

        int count = 0;
        var items = manager.GetBuffer<StoredItemElement>(storage, true);
        for (int i = 0; i < items.Length; i++)
        {
            var entry = items[i];
            if (entry.ItemType != type || !IsActiveEntity(manager, entry.ItemEntity))
            {
                continue;
            }

            if (!manager.HasComponent<ItemIdentity>(entry.ItemEntity) ||
                !manager.HasComponent<ItemOwnership>(entry.ItemEntity) || HasDestroy(manager, entry.ItemEntity))
            {
                continue;
            }

            if (manager.GetComponentData<ItemIdentity>(entry.ItemEntity).Type == type &&
                manager.GetComponentData<ItemOwnership>(entry.ItemEntity).Owner == storage)
            {
                count++;
            }
        }

        return count;
    }

    public static bool HasStorageSpace(EntityManager manager, Entity storage, ItemTypeEnum type, Entity registry)
    {
        return FindStorageSlot(manager, storage, type, registry) >= 0;
    }

    /// <summary>배정 후보와 실제 보관이 공유하는 필터·슬롯·MaxStack 계산. 보관 상태는 변경하지 않는다.</summary>
    public static int FindStorageSlot(EntityManager manager, Entity storage, ItemTypeEnum type, Entity registry)
    {
        if (!TryReadStorageLimit(manager, storage, type, registry, out int slots, out int maxStack)) return -1;
        for (int slot = 0; slot < slots; slot++)
        {
            if (SlotHasRoom(manager, storage, type, slot, maxStack)) return slot;
        }
        return -1;
    }

    public static bool CanStoreInSlot(EntityManager manager, Entity storage, ItemTypeEnum type, Entity registry, int slot)
    {
        if (!TryReadStorageLimit(manager, storage, type, registry, out int slots, out int maxStack)) return false;
        if (slot < 0 || slot >= slots) return false;
        return SlotHasRoom(manager, storage, type, slot, maxStack);
    }

    private static bool TryReadStorageLimit(EntityManager manager, Entity storage, ItemTypeEnum type, Entity registry,
        out int slots, out int maxStack)
    {
        slots = 0;
        maxStack = 0;
        if (!IsStorage(manager, storage))
        {
            return false;
        }

        if (registry == Entity.Null)
        {
            return false;
        }

        if (!manager.Exists(registry)) return false;
        if (!manager.HasComponent<ItemRegistry>(registry)) return false;
        if (!manager.HasBuffer<ItemConfigElement>(registry)) return false;

        if (manager.HasComponent<StorageFilter>(storage) &&
            !manager.GetComponentData<StorageFilter>(storage).IsItemAllowed(type))
        {
            return false;
        }

        maxStack = ItemRegistry.GetMaxStack(manager.GetBuffer<ItemConfigElement>(registry, true), type);
        if (maxStack <= 0)
        {
            return false;
        }
        slots = math.min(manager.GetComponentData<Storage>(storage).SlotCount, GameConstants.MaxStorageSlots);
        return slots > 0;
    }

    private static bool SlotHasRoom(EntityManager manager, Entity storage, ItemTypeEnum type, int slot, int maxStack)
    {
        var items = manager.GetBuffer<StoredItemElement>(storage, true);
        if (manager.HasBuffer<BuildingInputSlotElement>(storage))
        {
            var inputs = manager.GetBuffer<BuildingInputSlotElement>(storage, true);
            if (slot >= inputs.Length || inputs[slot].ItemType != type) return false;
        }
        int count = 0;
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i].SlotIndex != slot) continue;
            if (items[i].ItemType != type) return false;
            count++;
        }
        return count < maxStack;
    }

    public static bool IsUnderSite(EntityManager manager, Entity item, NativeArray<Entity> sites)
    {
        var position = manager.GetComponentData<GridPosition>(item).Value;
        for (int i = 0; i < sites.Length; i++)
        {
            Entity site = sites[i];
            if (!IsSite(manager, site) || !manager.HasComponent<BuildingFootprint>(site) ||
                !manager.HasComponent<Direction>(site))
            {
                continue;
            }

            var origin = manager.GetComponentData<GridPosition>(site).Value;
            var size = manager.GetComponentData<BuildingFootprint>(site)
                .GetEffectiveSize(manager.GetComponentData<Direction>(site).dir);
            if (math.all(position >= origin) && math.all(position < origin + size))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsTaskValid(EntityManager manager, in DroneLogisticsTask task, NativeArray<Entity> sites)
    {
        if (task.State != DroneLogisticsTaskStateEnum.Open || task.CreationSequence == 0 || task.ItemType == ItemTypeEnum.None)
        {
            return false;
        }

        if (task.Kind == DroneLogisticsTaskKindEnum.ConstructionSupply)
        {
            return IsSite(manager, task.Target);
        }

        if (task.Kind != DroneLogisticsTaskKindEnum.WorldItemRecovery || !IsWorldItem(manager, task.Target, task.ItemType))
        {
            return false;
        }

        return task.RecoveryReason == DroneRecoveryReasonEnum.DroneDrop ||
               (task.RecoveryReason == DroneRecoveryReasonEnum.SiteClearance && IsUnderSite(manager, task.Target, sites));
    }

    public static bool IsTerminal(DroneTaskAssignmentStateEnum state)
    {
        return state == DroneTaskAssignmentStateEnum.None || state == DroneTaskAssignmentStateEnum.Completed ||
               state == DroneTaskAssignmentStateEnum.Cancelled;
    }

    /// <summary>판단만 수행한다. 예약·작업·수행자 연결의 변경은 각 반영 단계가 소유한다.</summary>
    public static bool AssignmentNeedsCleanup(EntityManager manager, in DroneTaskAssignment assignment,
        NativeArray<Entity> sites)
    {
        if (IsTerminal(assignment.State) || !IsActiveEntity(manager, assignment.Worker)) return true;
        if (assignment.State == DroneTaskAssignmentStateEnum.Retargeting) return true;
        if (assignment.NextAction == DroneActionKindEnum.DropCargo)
            return ReadAssignmentCargoQuantity(manager, assignment) <= 0 ||
                   !ConstructionSiteWorldItemUtility.IsWorldPositionAllowed(manager, assignment.DropPosition);
        if (assignment.NextAction == DroneActionKindEnum.StoreCargo)
            return ReadAssignmentCargoQuantity(manager, assignment) <= 0 || !IsStorage(manager, assignment.Destination);
        if (assignment.Task == Entity.Null) return true;
        if (!manager.HasComponent<DroneLogisticsTask>(assignment.Task)) return true;
        var task = manager.GetComponentData<DroneLogisticsTask>(assignment.Task);
        if (!IsTaskValid(manager, task, sites)) return true;
        if (assignment.NextAction == DroneActionKindEnum.SupplyConstructionSite)
            return task.Kind != DroneLogisticsTaskKindEnum.ConstructionSupply ||
                   task.Target != assignment.Destination || !IsSite(manager, assignment.Destination);
        if (assignment.NextAction == DroneActionKindEnum.CollectFromStorage)
            return !IsStorage(manager, assignment.Source);
        if (assignment.NextAction == DroneActionKindEnum.RecoverWorldItem)
            return !IsWorldItem(manager, assignment.Source, assignment.ItemType) || !IsStorage(manager, assignment.Destination);
        return false;
    }

    public static bool IsRetargetingWorker(EntityManager manager, Entity assignmentEntity,
        out DroneTaskAssignment assignment, out int cargoQuantity)
    {
        assignment = default;
        cargoQuantity = 0;
        if (!IsActiveEntity(manager, assignmentEntity)) return false;
        if (!manager.HasComponent<DroneTaskAssignment>(assignmentEntity)) return false;
        assignment = manager.GetComponentData<DroneTaskAssignment>(assignmentEntity);
        if (assignment.State != DroneTaskAssignmentStateEnum.Retargeting || assignment.Revision == 0) return false;
        if (!IsActiveEntity(manager, assignment.Worker)) return false;
        if (!manager.HasComponent<DroneWorker>(assignment.Worker)) return false;
        if (!manager.HasComponent<DroneWorkerObservation>(assignment.Worker)) return false;
        if (!manager.HasComponent<DroneWorkerAssignment>(assignment.Worker)) return false;
        if (manager.GetComponentData<DroneWorkerAssignment>(assignment.Worker).Assignment != assignmentEntity) return false;
        var observation = manager.GetComponentData<DroneWorkerObservation>(assignment.Worker);
        if (observation.Revision == 0 || !math.all(math.isfinite(observation.Position))) return false;
        if (AssignmentCreationSequence(manager, assignment) == 0) return false;
        cargoQuantity = ReadAssignmentCargoQuantity(manager, assignment);
        return cargoQuantity > 0;
    }

    /// <summary>재배정 때 목적 작업을 바꿔도 최초 배정의 순서를 유지한다.</summary>
    public static ulong AssignmentCreationSequence(EntityManager manager, in DroneTaskAssignment assignment)
    {
        if (assignment.OriginalTaskCreationSequence > 0) return assignment.OriginalTaskCreationSequence;
        if (assignment.Task == Entity.Null) return 0;
        if (!manager.Exists(assignment.Task)) return 0;
        if (!manager.HasComponent<DroneLogisticsTask>(assignment.Task)) return 0;
        return manager.GetComponentData<DroneLogisticsTask>(assignment.Task).CreationSequence;
    }

    private static int ReadAssignmentCargoQuantity(EntityManager manager, in DroneTaskAssignment assignment)
    {
        if (!IsActiveEntity(manager, assignment.Worker)) return 0;
        if (!manager.HasComponent<DroneCargoState>(assignment.Worker)) return 0;
        if (!manager.HasBuffer<StoredItemElement>(assignment.Worker)) return 0;
        var origin = manager.GetComponentData<DroneCargoState>(assignment.Worker).Origin;
        if (origin != DroneCargoOriginEnum.Supply && origin != DroneCargoOriginEnum.Recovery) return 0;
        if (assignment.ItemType == ItemTypeEnum.None) return 0;
        var cargo = manager.GetBuffer<StoredItemElement>(assignment.Worker, true);
        for (int i = 0; i < cargo.Length; i++)
        {
            Entity item = cargo[i].ItemEntity;
            if (!IsActiveEntity(manager, item)) return 0;
            if (HasDestroy(manager, item)) return 0;
            if (cargo[i].ItemType != assignment.ItemType) return 0;
            if (!manager.HasComponent<ItemIdentity>(item)) return 0;
            if (manager.GetComponentData<ItemIdentity>(item).Type != assignment.ItemType) return 0;
            if (!manager.HasComponent<ItemOwnership>(item)) return 0;
            if (manager.GetComponentData<ItemOwnership>(item).Owner != assignment.Worker) return 0;
        }
        return cargo.Length;
    }

    /// <summary>
    /// 아직 해제되지 않은 무효 배정/미공개 예약의 수량을 제외해 다음 예약 가능량을 읽기 계산한다.
    /// Decision이 원본 합계를 선행 수정하지 않으며 실제 해제는 Reservation/Publish의 소유 경계가 담당한다.
    /// </summary>
    public static int ProjectedRemaining(EntityManager manager, Entity site, ItemTypeEnum type,
        NativeArray<Entity> assignments, NativeArray<Entity> sites, DynamicBuffer<DroneTaskPendingPublicationElement> pendingPublications)
    {
        int releasedQuantity = 0;
        for (int i = 0; i < assignments.Length; i++)
        {
            Entity entity = assignments[i];
            if (!manager.HasComponent<ConstructionSupplyReservation>(entity)) continue;
            var reservation = manager.GetComponentData<ConstructionSupplyReservation>(entity);
            if (reservation.Site != site || reservation.ItemType != type || reservation.RemainingQuantity <= 0) continue;
            if (AssignmentNeedsCleanup(manager, manager.GetComponentData<DroneTaskAssignment>(entity), sites))
                releasedQuantity += reservation.RemainingQuantity;
        }
        for (int i = 0; i < pendingPublications.Length; i++)
        {
            var pending = pendingPublications[i];
            if (!pending.PublicationQueued && pending.Candidate.Destination == site && pending.Candidate.ItemType == type)
                releasedQuantity += pending.CommittedQuantity;
        }
        return ConstructionSupplyReservationUtility.RemainingIncludingOwn(manager, site, type, releasedQuantity);
    }

    public static int ComparePlacement(EntityManager manager, Entity left, Entity right)
    {
        bool hasLeft = manager.HasComponent<PlacementStamp>(left);
        bool hasRight = manager.HasComponent<PlacementStamp>(right);
        var a = hasLeft ? manager.GetComponentData<PlacementStamp>(left) : default;
        var b = hasRight ? manager.GetComponentData<PlacementStamp>(right) : default;
        int2 leftPosition = manager.HasComponent<GridPosition>(left)
            ? manager.GetComponentData<GridPosition>(left).Value : default;
        int2 rightPosition = manager.HasComponent<GridPosition>(right)
            ? manager.GetComponentData<GridPosition>(right).Value : default;
        return PlacementStamp.Compare(hasLeft, a, leftPosition, hasRight, b, rightPosition);
    }

    /// <summary>공급끼리는 PlacementStamp를 우선하고 나머지 동률/종류 간 비교는 작업 CreationSequence를 사용한다.</summary>
    public static bool EarlierTask(EntityManager manager, Entity left, Entity right)
    {
        if (right == Entity.Null)
        {
            return true;
        }

        var a = manager.GetComponentData<DroneLogisticsTask>(left);
        var b = manager.GetComponentData<DroneLogisticsTask>(right);
        if (a.Kind == DroneLogisticsTaskKindEnum.ConstructionSupply && b.Kind == a.Kind)
        {
            int placement = ComparePlacement(manager, a.Target, b.Target);
            if (placement != 0) return placement < 0;
        }

        return a.CreationSequence < b.CreationSequence;
    }

    public static bool RouteIsCurrent(EntityManager manager, in DroneRouteEvaluationRequest request, int carryingCapacity)
    {
        if (request.IsDropPositionSearch && request.Kind != DroneRouteKindEnum.Direct) return false;
        if (request.Kind == DroneRouteKindEnum.Direct)
            return DirectRouteIsCurrent(manager, request);
        if (!IsIdleWorker(manager, request.Worker, carryingCapacity))
        {
            return false;
        }

        if (request.Assignment != Entity.Null || request.AssignmentRevision != 0 || request.EvaluationRevision == 0 ||
            request.Kind != DroneRouteKindEnum.ViaSource)
        {
            return false;
        }

        // TODO(SoT 연결): 아래 검사는 요청과 관측의 일치만 확인한다. 실제 드론 위치 원본과의 연결/검사는 아직 없다.
        // 이동/관측 Producer 연결 시 완성할 계약이며, 현재 실행에서 위치 불일치를 재현했다는 의미는 아니다.
        var observation = manager.GetComponentData<DroneWorkerObservation>(request.Worker);
        if (request.WorkerObservationRevision != observation.Revision ||
            !math.all(request.OriginPosition == observation.Position))
        {
            return false;
        }

        if (!IsActiveEntity(manager, request.Source) || !IsActiveEntity(manager, request.Destination))
        {
            return false;
        }

        return manager.HasComponent<GridPosition>(request.Source) && manager.HasComponent<GridPosition>(request.Destination) &&
               !manager.HasComponent<PendingBuildingDemolition>(request.Source) &&
               !manager.HasComponent<PendingBuildingDemolition>(request.Destination) &&
               math.all(manager.GetComponentData<GridPosition>(request.Source).Value == request.SourcePosition) &&
               math.all(manager.GetComponentData<GridPosition>(request.Destination).Value == request.DestinationPosition);
    }

    private static bool DirectRouteIsCurrent(EntityManager manager, in DroneRouteEvaluationRequest request)
    {
        if (!IsRetargetingWorker(manager, request.Assignment, out var assignment, out _)) return false;
        if (request.Source != Entity.Null || request.EvaluationRevision == 0) return false;
        if (assignment.Worker != request.Worker || assignment.Revision != request.AssignmentRevision) return false;
        var observation = manager.GetComponentData<DroneWorkerObservation>(request.Worker);
        // TODO(SoT 연결): 실제 위치 원본 연결 전에는 관측/요청 revision 일치까지만 검증한다.
        if (observation.Revision != request.WorkerObservationRevision ||
            !math.all(observation.Position == request.OriginPosition)) return false;
        if (request.IsDropPositionSearch) return request.Destination == Entity.Null;
        if (!IsActiveEntity(manager, request.Destination)) return false;
        if (manager.HasComponent<PendingBuildingDemolition>(request.Destination)) return false;
        if (!manager.HasComponent<GridPosition>(request.Destination)) return false;
        return math.all(manager.GetComponentData<GridPosition>(request.Destination).Value == request.DestinationPosition);
    }

    public static bool TryReadRoute(EntityManager manager, Entity requestEntity, int carryingCapacity,
        out bool reachable, out float distance)
    {
        reachable = false;
        distance = 0f;
        if (!manager.HasComponent<DroneRouteEvaluationRequest>(requestEntity) ||
            !manager.HasComponent<DroneRouteEvaluationResult>(requestEntity))
        {
            return false;
        }

        var request = manager.GetComponentData<DroneRouteEvaluationRequest>(requestEntity);
        var result = manager.GetComponentData<DroneRouteEvaluationResult>(requestEntity);
        if (!RouteIsCurrent(manager, request, carryingCapacity) || result.EvaluationRevision != request.EvaluationRevision)
        {
            return false;
        }

        if (result.Status == DroneRouteEvaluationStatusEnum.Unreachable)
        {
            return true;
        }

        if (result.Status != DroneRouteEvaluationStatusEnum.Reachable || !math.isfinite(result.TotalDistance) || result.TotalDistance < 0f)
        {
            return false;
        }

        if (request.IsDropPositionSearch && (!result.HasDropPosition ||
            !ConstructionSiteWorldItemUtility.IsWorldPositionAllowed(manager, result.DropPosition))) return false;

        reachable = true;
        distance = result.TotalDistance;
        return true;
    }

    public static int CandidateQuantity(EntityManager manager, in DroneTaskCandidateDecisionElement candidate, Entity registry,
        int carryingCapacity, int committedQuantity = 0)
    {
        if (candidate.Assignment != Entity.Null)
            return RetargetingCandidateQuantity(manager, candidate, registry, carryingCapacity, committedQuantity);
        if (!IsIdleWorker(manager, candidate.Worker, carryingCapacity) || !manager.HasComponent<DroneLogisticsTask>(candidate.Task))
        {
            return 0;
        }

        var observation = manager.GetComponentData<DroneWorkerObservation>(candidate.Worker);
        if (observation.Revision != candidate.WorkerObservationRevision ||
            !TryReadRoute(manager, candidate.RouteRequest, carryingCapacity, out bool reachable, out _) || !reachable)
        {
            return 0;
        }

        var task = manager.GetComponentData<DroneLogisticsTask>(candidate.Task);
        var route = manager.GetComponentData<DroneRouteEvaluationRequest>(candidate.RouteRequest);
        if (task.State != DroneLogisticsTaskStateEnum.Open || task.ItemType != candidate.ItemType ||
            route.Worker != candidate.Worker || route.Source != candidate.Source || route.Destination != candidate.Destination)
        {
            return 0;
        }

        if (task.Kind == DroneLogisticsTaskKindEnum.ConstructionSupply)
        {
            if (task.Target != candidate.Destination || !IsSite(manager, task.Target)) return 0;
            return math.min(carryingCapacity, math.min(candidate.Quantity,
                math.min(ConstructionSupplyReservationUtility.RemainingIncludingOwn(manager, task.Target, task.ItemType, committedQuantity),
                    CountInventory(manager, candidate.Source, task.ItemType))));
        }

        if (task.Kind != DroneLogisticsTaskKindEnum.WorldItemRecovery || task.Target != candidate.Source ||
            !IsWorldItem(manager, candidate.Source, task.ItemType) || !HasStorageSpace(manager, candidate.Destination, task.ItemType, registry))
        {
            return 0;
        }

        return 1;
    }

    private static int RetargetingCandidateQuantity(EntityManager manager,
        in DroneTaskCandidateDecisionElement candidate, Entity registry, int carryingCapacity, int committedQuantity)
    {
        if (!IsRetargetingWorker(manager, candidate.Assignment, out var assignment, out int cargoQuantity)) return 0;
        if (assignment.Worker != candidate.Worker || assignment.Revision != candidate.AssignmentRevision) return 0;
        if (assignment.ItemType != candidate.ItemType || candidate.Source != Entity.Null) return 0;
        var observation = manager.GetComponentData<DroneWorkerObservation>(candidate.Worker);
        if (observation.Revision != candidate.WorkerObservationRevision) return 0;
        int quantity = math.min(candidate.Quantity, cargoQuantity);
        if (quantity <= 0) return 0;
        if (candidate.NextAction == DroneActionKindEnum.DropCargo)
        {
            if (candidate.Destination != Entity.Null) return 0;
            if (!ConstructionSiteWorldItemUtility.IsWorldPositionAllowed(manager, candidate.DropPosition)) return 0;
            if (candidate.RouteRequest == Entity.Null)
                return math.all((int2)math.floor(observation.Position.xy) == candidate.DropPosition) ? quantity : 0;
            if (!TryReadRoute(manager, candidate.RouteRequest, carryingCapacity, out bool dropReachable, out _) ||
                !dropReachable) return 0;
            var dropRequest = manager.GetComponentData<DroneRouteEvaluationRequest>(candidate.RouteRequest);
            var dropResult = manager.GetComponentData<DroneRouteEvaluationResult>(candidate.RouteRequest);
            if (!dropRequest.IsDropPositionSearch || dropRequest.Assignment != candidate.Assignment) return 0;
            return math.all(dropResult.DropPosition == candidate.DropPosition) ? quantity : 0;
        }
        if (!TryReadRoute(manager, candidate.RouteRequest, carryingCapacity, out bool reachable, out _) || !reachable) return 0;
        var route = manager.GetComponentData<DroneRouteEvaluationRequest>(candidate.RouteRequest);
        if (route.Kind != DroneRouteKindEnum.Direct || route.Assignment != candidate.Assignment ||
            route.Destination != candidate.Destination) return 0;
        if (candidate.NextAction == DroneActionKindEnum.StoreCargo)
            return HasStorageSpace(manager, candidate.Destination, candidate.ItemType, registry) ? quantity : 0;
        if (candidate.NextAction != DroneActionKindEnum.SupplyConstructionSite) return 0;
        if (manager.GetComponentData<DroneCargoState>(candidate.Worker).Origin != DroneCargoOriginEnum.Supply) return 0;
        if (!IsActiveEntity(manager, candidate.Task)) return 0;
        if (!manager.HasComponent<DroneLogisticsTask>(candidate.Task)) return 0;
        var task = manager.GetComponentData<DroneLogisticsTask>(candidate.Task);
        if (task.Kind != DroneLogisticsTaskKindEnum.ConstructionSupply || task.State != DroneLogisticsTaskStateEnum.Open ||
            task.ItemType != candidate.ItemType || task.Target != candidate.Destination) return 0;
        if (!IsSite(manager, candidate.Destination)) return 0;
        return math.min(quantity, ConstructionSupplyReservationUtility.RemainingIncludingOwn(manager,
            candidate.Destination, candidate.ItemType, committedQuantity));
    }
}
