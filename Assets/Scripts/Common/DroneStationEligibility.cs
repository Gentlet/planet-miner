using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Decision과 최종 Apply가 공유하는 출고/정박/수납 자격 검사다.
/// 역할 구성은 QueryMask로 확인하고 값은 필요한 RO Lookup에서 직접 읽는다. ECS 상태를 복제하지 않는다.
/// 소유 시스템의 OnCreate에서 생성하고, 사용할 때마다 Update로 Lookup의 접근 정보를 갱신한다.
/// </summary>
internal struct DroneStationEligibility
{
    public EntityQueryMask StationMask;
    public EntityQueryMask ItemMask;
    public EntityQueryMask WorkerMask;
    [ReadOnly] public EntityStorageInfoLookup Entities;
    [ReadOnly] public ComponentLookup<BuildingType> BuildingTypes;
    [ReadOnly] public ComponentLookup<ItemIdentity> ItemTypes;
    [ReadOnly] public ComponentLookup<ItemOwnership> Owners;
    [ReadOnly] public ComponentLookup<DroneBatteryState> Batteries;
    [ReadOnly] public ComponentLookup<DroneActivityState> Activities;
    [ReadOnly] public ComponentLookup<DroneWorkerAssignment> Assignments;
    [ReadOnly] public ComponentLookup<DroneCargoState> Cargo;
    [ReadOnly] public ComponentLookup<DroneDocked> Docks;
    [ReadOnly] public ComponentLookup<DroneMovementState> Movements;
    [ReadOnly] public ComponentLookup<DestroyItemRequest> DestroyRequests;
    [ReadOnly] public ComponentLookup<Storage> Storages;
    [ReadOnly] public ComponentLookup<GridPosition> Positions;
    [ReadOnly] public ComponentLookup<LocalTransform> Transforms;
    [ReadOnly] public ComponentLookup<BuildingFootprint> Footprints;
    [ReadOnly] public ComponentLookup<Direction> Directions;
    [ReadOnly] public BufferLookup<StoredItemElement> Items;
    [ReadOnly] public BufferLookup<DockedDroneElement> Docked;
    [ReadOnly] public ComponentLookup<PreparingStationDemolition> Preparing;
    [ReadOnly] public ComponentLookup<DroneStationRelocation> Relocations;
    [ReadOnly] public BufferLookup<DroneStationReservedSlotElement> Reserved;

    public static void CreateMasks(ref SystemState state, out EntityQueryMask station, out EntityQueryMask item, out EntityQueryMask worker)
    {
        using var stations = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<BuildingType, DroneStationStorage, Storage, GridPosition, StoredItemElement, DockedDroneElement>()
            .WithNone<Disabled, Prefab, PendingBuildingDemolition>();
        station = stations.Build(ref state).GetEntityQueryMask();
        using var items = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<ItemIdentity, ItemOwnership, LocalTransform>()
            .WithNone<Disabled, Prefab, DroneWorker, DroneWorkerObservation, DroneWorkerAssignment>()
            .WithNone<DroneCargoState, DroneActivityState, DroneDocked, StoredItemElement>()
            .WithNone<DroneMovementState, DroneMovementDecision, DroneMovementResult>();
        item = items.Build(ref state).GetEntityQueryMask();
        using var workers = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<DroneWorker, DroneWorkerObservation, DroneWorkerAssignment, DroneCargoState>()
            .WithAll<DroneActivityState, DroneBatteryState, StoredItemElement, LocalTransform>()
            .WithAll<DroneMovementState, DroneMovementDecision, DroneMovementResult>()
            .WithNone<Disabled, Prefab, ItemIdentity>();
        worker = workers.Build(ref state).GetEntityQueryMask();
    }

    public static DroneStationEligibility Create(ref SystemState state, EntityQueryMask station, EntityQueryMask item, EntityQueryMask worker)
    {
        return new DroneStationEligibility
        {
            StationMask = station, ItemMask = item, WorkerMask = worker,
            Entities = state.GetEntityStorageInfoLookup(),
            BuildingTypes = state.GetComponentLookup<BuildingType>(true),
            ItemTypes = state.GetComponentLookup<ItemIdentity>(true),
            Owners = state.GetComponentLookup<ItemOwnership>(true),
            Batteries = state.GetComponentLookup<DroneBatteryState>(true),
            Activities = state.GetComponentLookup<DroneActivityState>(true),
            Assignments = state.GetComponentLookup<DroneWorkerAssignment>(true),
            Cargo = state.GetComponentLookup<DroneCargoState>(true),
            Docks = state.GetComponentLookup<DroneDocked>(true),
            Movements = state.GetComponentLookup<DroneMovementState>(true),
            DestroyRequests = state.GetComponentLookup<DestroyItemRequest>(true),
            Storages = state.GetComponentLookup<Storage>(true),
            Positions = state.GetComponentLookup<GridPosition>(true),
            Transforms = state.GetComponentLookup<LocalTransform>(true),
            Footprints = state.GetComponentLookup<BuildingFootprint>(true),
            Directions = state.GetComponentLookup<Direction>(true),
            Items = state.GetBufferLookup<StoredItemElement>(true),
            Docked = state.GetBufferLookup<DockedDroneElement>(true),
            Preparing = state.GetComponentLookup<PreparingStationDemolition>(true),
            Relocations = state.GetComponentLookup<DroneStationRelocation>(true),
            Reserved = state.GetBufferLookup<DroneStationReservedSlotElement>(true)
        };
    }

    public void Update(ref SystemState state)
    {
        Entities.Update(ref state);
        BuildingTypes.Update(ref state);
        ItemTypes.Update(ref state);
        Owners.Update(ref state);
        Batteries.Update(ref state);
        Activities.Update(ref state);
        Assignments.Update(ref state);
        Cargo.Update(ref state);
        Docks.Update(ref state);
        Movements.Update(ref state);
        DestroyRequests.Update(ref state);
        Storages.Update(ref state);
        Positions.Update(ref state);
        Transforms.Update(ref state);
        Footprints.Update(ref state);
        Directions.Update(ref state);
        Items.Update(ref state);
        Docked.Update(ref state);
        Preparing.Update(ref state);
        Relocations.Update(ref state);
        Reserved.Update(ref state);
    }

    public DroneStationRequestStatus Validate(in DroneStationRequest request, float maximum, bool automatic = false,
        bool relocationRequest = false)
    {
        Entity drone = request.Drone;
        Entity station = request.Station;
        if (drone == Entity.Null || station == Entity.Null)
        {
            return DroneStationRequestStatus.Rejected;
        }
        if (!Entities.Exists(drone) || !Entities.Exists(station))
        {
            return DroneStationRequestStatus.Rejected;
        }
        if (!StationMask.MatchesIgnoreFilter(station) || !DroneStationRangeUtility.IsStationType(BuildingTypes[station].Type))
        {
            return DroneStationRequestStatus.Rejected;
        }
        if (DestroyRequests.HasComponent(drone) && DestroyRequests.IsComponentEnabled(drone))
        {
            return DroneStationRequestStatus.Rejected;
        }
        bool relocating = Relocations.TryGetComponent(drone, out var relocation);
        if (relocating && !relocationRequest) return DroneStationRequestStatus.Busy;
        if (relocationRequest && !relocating) return DroneStationRequestStatus.Rejected;
        bool hasReadReservedSlot = false;
        int reservedSlot = -1;
        if (relocating)
        {
            if (request.Operation == DroneStationOperation.Store && station != relocation.Destination)
                return DroneStationRequestStatus.Rejected;
            if (request.Operation != DroneStationOperation.Activate && station == relocation.Destination)
            {
                reservedSlot = DroneStationRelocationUtility.ReservedSlot(Reserved, station, drone);
                hasReadReservedSlot = true;
                if (reservedSlot != relocation.SlotIndex)
                {
                    return DroneStationRequestStatus.NoSpace;
                }
            }
        }
        bool hasDock = Docks.TryGetComponent(drone, out var dock);
        bool hasReadWorkerCargo = false;
        DroneWorkerAssignment workerAssignment = default;
        DynamicBuffer<StoredItemElement> workerItems = default;
        if (Preparing.HasComponent(station))
        {
            bool ownsDock = hasDock && dock.Station == station;
            bool promisedArrival = relocating && relocation.Source == station;
            if (relocating && !promisedArrival)
            {
                if (!hasReadReservedSlot)
                {
                    reservedSlot = DroneStationRelocationUtility.ReservedSlot(Reserved, station, drone);
                }
                promisedArrival = reservedSlot >= 0;
            }
            if (!promisedArrival && !ownsDock) return DroneStationRequestStatus.Busy;
            // 준비 중 기존 정박은 충전/기존 작업 이탈만 계속하며 새 수납으로 계획 입력을 바꾸지 않는다.
            if (!promisedArrival && request.Operation == DroneStationOperation.Store) return DroneStationRequestStatus.Busy;
            if (ownsDock)
            {
                if (!Assignments.TryGetComponent(drone, out workerAssignment) ||
                    !Items.TryGetBuffer(drone, out workerItems))
                {
                    return DroneStationRequestStatus.Rejected;
                }
                hasReadWorkerCargo = true;
                if (!promisedArrival && !relocating && request.Operation == DroneStationOperation.Activate &&
                    workerAssignment.Assignment == Entity.Null && workerItems.IsEmpty)
                {
                    return DroneStationRequestStatus.Busy;
                }
            }
        }
        if (request.Operation == DroneStationOperation.Activate && ItemTypes.TryGetComponent(drone, out var itemType))
        {
            if (!ItemMask.MatchesIgnoreFilter(drone) || itemType.Type != ItemTypeEnum.Drone ||
                Owners[drone].Owner != station || !ContainsItem(Items[station], drone))
            {
                return DroneStationRequestStatus.Rejected;
            }
            if (Batteries.TryGetComponent(drone, out var battery))
            {
                if (!DroneBatteryUtility.IsValid(battery, maximum))
                {
                    return DroneStationRequestStatus.Rejected;
                }
            }
            return DroneStationRequestStatus.Applied;
        }
        if (request.Operation > DroneStationOperation.Store || !WorkerMask.MatchesIgnoreFilter(drone))
        {
            return DroneStationRequestStatus.Rejected;
        }
        var activity = Activities[drone].Activity;
        bool chargingActivity = activity == DroneActivityKind.WaitingForCharge ||
            activity == DroneActivityKind.Charging;
        // 명시적 Dock은 이동 목적지가 없어도 유효하다. 정박 관계가 없을 때만 도착 목적지를 사용한다.
        Entity chargingStation = hasDock ? dock.Station : Movements[drone].ChargingStation;
        bool chargingAtStation = automatic && chargingActivity && chargingStation == station && IsAtStation(drone, station);
        if (activity != DroneActivityKind.Idle && !chargingAtStation)
        {
            return DroneStationRequestStatus.Busy;
        }
        if (hasDock)
        {
            int matches = 0;
            foreach (var entry in Docked[station])
            {
                if (entry.Drone == drone && entry.SlotIndex == dock.SlotIndex)
                {
                    matches++;
                }
            }
            if (dock.Station != station || dock.SlotIndex < 0 || dock.SlotIndex >= Storages[station].SlotCount || matches != 1)
            {
                return DroneStationRequestStatus.Rejected;
            }
        }
        if (request.Operation == DroneStationOperation.Activate)
        {
            return hasDock ? DroneStationRequestStatus.Applied : DroneStationRequestStatus.Rejected;
        }
        if (!chargingAtStation && !IsAtStation(drone, station))
        {
            return DroneStationRequestStatus.Rejected;
        }
        if (request.Operation == DroneStationOperation.Dock)
        {
            return hasDock ? DroneStationRequestStatus.Rejected : DroneStationRequestStatus.Applied;
        }
        if (Batteries[drone].CurrentCharge != maximum)
        {
            return DroneStationRequestStatus.Busy;
        }
        Entity assignment = hasReadWorkerCargo ? workerAssignment.Assignment : Assignments[drone].Assignment;
        if (assignment != Entity.Null)
        {
            return DroneStationRequestStatus.Busy;
        }
        var carriedItems = hasReadWorkerCargo ? workerItems : Items[drone];
        if (!carriedItems.IsEmpty || Cargo[drone].Origin != DroneCargoOriginEnum.None)
        {
            return DroneStationRequestStatus.Busy;
        }
        return DroneStationRequestStatus.Applied;
    }

    private static bool ContainsItem(DynamicBuffer<StoredItemElement> items, Entity drone)
    {
        int count = 0;
        foreach (var item in items)
        {
            if (item.ItemEntity == drone && item.ItemType == ItemTypeEnum.Drone)
            {
                count++;
            }
        }
        return count == 1;
    }

    private bool IsAtStation(Entity drone, Entity station)
    {
        if (!Footprints.TryGetComponent(station, out var footprint) ||
            !Directions.TryGetComponent(station, out var direction) ||
            !Positions.TryGetComponent(drone, out var dronePosition))
        {
            return false;
        }
        float3 position = Transforms[drone].Position;
        if (!math.all(math.isfinite(position)))
        {
            return false;
        }
        int2 cell = dronePosition.Value;
        int2 anchor = Positions[station].Value;
        int2 size = footprint.GetEffectiveSize(direction.dir);
        return math.all(math.floor(position.xy) == cell) && math.all(cell >= anchor) &&
            (long)cell.x < (long)anchor.x + size.x && (long)cell.y < (long)anchor.y + size.y;
    }
}
