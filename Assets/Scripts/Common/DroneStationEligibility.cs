using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

internal readonly struct DroneStationDockFootprint
{
    public readonly Entity Station;
    private readonly int2 _anchor;
    private readonly int2 _size;

    public DroneStationDockFootprint(Entity station, int2 anchor, int2 size)
    {
        Station = station;
        _anchor = anchor;
        _size = size;
    }

    public bool Contains(int2 cell) => math.all(cell >= _anchor) &&
        (long)cell.x < (long)_anchor.x + _size.x && (long)cell.y < (long)_anchor.y + _size.y;
}

/// <summary>
/// Decision과 최종 Apply가 공유하는 출고/정박/수납 자격 검사다.
/// 역할 구성은 QueryMask로 확인하고 값은 필요한 RO Lookup에서 직접 읽는다. ECS 상태를 복제하지 않는다.
/// 소유 시스템의 OnCreate에서 생성하고, 사용할 때마다 Update로 Lookup의 접근 정보를 갱신한다.
/// </summary>
internal struct DroneStationEligibility
{
    public EntityQueryMask StationMask;
    public EntityQueryMask WorkerMask;
    [ReadOnly] public EntityStorageInfoLookup Entities;
    [ReadOnly] public ComponentLookup<DroneStation> StationRoles;
    [ReadOnly] public ComponentLookup<DroneBatteryState> Batteries;
    [ReadOnly] public ComponentLookup<DroneActivityState> Activities;
    [ReadOnly] public ComponentLookup<DroneWorkerAssignment> Assignments;
    [ReadOnly] public ComponentLookup<DroneCargoState> Cargo;
    [ReadOnly] public ComponentLookup<DroneDocked> Docks;
    [ReadOnly] public ComponentLookup<DroneStationStorage> Dedicated;
    [ReadOnly] public ComponentLookup<DroneMovementState> Movements;
    private int NonFuelSlotCount(Entity station)
    {
        return FuelSlots.TryGetComponent(station, out var fuel) ? fuel.Start : Storages[station].SlotCount;
    }

    [ReadOnly] public ComponentLookup<Storage> Storages;
    [ReadOnly] public ComponentLookup<FuelStorageSlots> FuelSlots;
    [ReadOnly] public ComponentLookup<GridPosition> Positions;
    [ReadOnly] public ComponentLookup<LocalTransform> Transforms;
    [ReadOnly] public ComponentLookup<BuildingFootprint> Footprints;
    [ReadOnly] public ComponentLookup<Direction> Directions;
    [ReadOnly] public BufferLookup<DockedDroneElement> Docked;
    [ReadOnly] public ComponentLookup<PreparingStationDemolition> Preparing;
    [ReadOnly] public ComponentLookup<DroneStationRelocation> Relocations;
    [ReadOnly] public BufferLookup<DroneStationRelocationMemberElement> Members;
    [ReadOnly] public BufferLookup<DroneStationReservedSlotElement> Reserved;

    public static void CreateMasks(ref SystemState state, out EntityQueryMask station, out EntityQueryMask worker)
    {
        using var stations = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<DroneStation, DroneStationStorage, Storage, GridPosition, StorageSlotElement, DockedDroneElement>()
            .WithNone<Disabled, Prefab, PendingBuildingDemolition>();
        station = stations.Build(ref state).GetEntityQueryMask();
        using var workers = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<DroneWorker, DroneWorkerObservation, DroneWorkerAssignment, DroneCargoState>()
            .WithAll<DroneActivityState, DroneBatteryState, LocalTransform>()
            .WithAll<DroneMovementState, DroneMovementDecision, DroneMovementResult>()
            .WithNone<Disabled, Prefab, ItemIdentity>();
        worker = workers.Build(ref state).GetEntityQueryMask();
    }

    public static DroneStationEligibility Create(ref SystemState state, EntityQueryMask station, EntityQueryMask worker)
    {
        return new DroneStationEligibility
        {
            StationMask = station, WorkerMask = worker,
            Entities = state.GetEntityStorageInfoLookup(),
            StationRoles = state.GetComponentLookup<DroneStation>(true),
            Batteries = state.GetComponentLookup<DroneBatteryState>(true),
            Activities = state.GetComponentLookup<DroneActivityState>(true),
            Assignments = state.GetComponentLookup<DroneWorkerAssignment>(true),
            Cargo = state.GetComponentLookup<DroneCargoState>(true),
            Docks = state.GetComponentLookup<DroneDocked>(true),
            Dedicated = state.GetComponentLookup<DroneStationStorage>(true),
            Movements = state.GetComponentLookup<DroneMovementState>(true),
            Storages = state.GetComponentLookup<Storage>(true),
            FuelSlots = state.GetComponentLookup<FuelStorageSlots>(true),
            Positions = state.GetComponentLookup<GridPosition>(true),
            Transforms = state.GetComponentLookup<LocalTransform>(true),
            Footprints = state.GetComponentLookup<BuildingFootprint>(true),
            Directions = state.GetComponentLookup<Direction>(true),
            Docked = state.GetBufferLookup<DockedDroneElement>(true),
            Preparing = state.GetComponentLookup<PreparingStationDemolition>(true),
            Relocations = state.GetComponentLookup<DroneStationRelocation>(true),
            Members = state.GetBufferLookup<DroneStationRelocationMemberElement>(true),
            Reserved = state.GetBufferLookup<DroneStationReservedSlotElement>(true)
        };
    }

    public void Update(ref SystemState state)
    {
        Entities.Update(ref state);
        StationRoles.Update(ref state);
        Batteries.Update(ref state);
        Activities.Update(ref state);
        Assignments.Update(ref state);
        Cargo.Update(ref state);
        Docks.Update(ref state);
        Dedicated.Update(ref state);
        Movements.Update(ref state);
        Storages.Update(ref state);
        FuelSlots.Update(ref state);
        Positions.Update(ref state);
        Transforms.Update(ref state);
        Footprints.Update(ref state);
        Directions.Update(ref state);
        Docked.Update(ref state);
        Preparing.Update(ref state);
        Relocations.Update(ref state);
        Members.Update(ref state);
        Reserved.Update(ref state);
    }

    public DroneStationRequestStatus Validate(in DroneStationRequest request, float maximum, bool automatic = false,
        bool relocationRequest = false)
    {
        Entity drone = request.Drone;
        Entity station = request.Station;
        if (!Entities.Exists(station) || !StationMask.MatchesIgnoreFilter(station) ||
            !StationRoles.HasComponent(station)) return DroneStationRequestStatus.Rejected;
        if (request.Operation == DroneStationOperation.ActivateStored)
        {
            if (!StationStorageUtility.IsDroneStorageSlot(request.SourceSlotIndex, NonFuelSlotCount(station),
                Dedicated[station].DedicatedSlotCount)) return DroneStationRequestStatus.Rejected;
            if (relocationRequest)
            {
                if (!Preparing.TryGetComponent(station, out var preparation) || !preparation.Started ||
                    preparation.Task != request.RelocationTask ||
                    !Members.TryGetBuffer(request.RelocationTask, out var roster) ||
                    (uint)request.RelocationMemberIndex >= (uint)roster.Length) return DroneStationRequestStatus.Rejected;
                var member = roster[request.RelocationMemberIndex];
                if (member.Finished || member.Drone != Entity.Null || member.SourceSlotIndex != request.SourceSlotIndex ||
                    DroneStationRelocationUtility.ReservedSlot(Reserved, member.Destination, request.RelocationTask,
                        request.RelocationMemberIndex) != member.SlotIndex) return DroneStationRequestStatus.Rejected;
            }
            else if (Preparing.HasComponent(station)) return DroneStationRequestStatus.Busy;
            return DroneStationRequestStatus.Applied;
        }
        if (!Entities.Exists(drone) || !WorkerMask.MatchesIgnoreFilter(drone))
            return DroneStationRequestStatus.Rejected;
        bool relocating = Relocations.TryGetComponent(drone, out var link);
        DroneStationRelocationMemberElement relocation = default;
        if (relocating)
        {
            if (link.Task != request.RelocationTask || link.MemberIndex != request.RelocationMemberIndex ||
                !Members.TryGetBuffer(link.Task, out var roster) || (uint)link.MemberIndex >= (uint)roster.Length)
                return DroneStationRequestStatus.Busy;
            relocation = roster[link.MemberIndex];
            if (relocation.Finished || relocation.Drone != drone) return DroneStationRequestStatus.Rejected;
        }
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
                reservedSlot = DroneStationRelocationUtility.ReservedSlot(Reserved, station, request.RelocationTask, request.RelocationMemberIndex);
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
        if (Preparing.HasComponent(station))
        {
            bool ownsDock = hasDock && dock.Station == station;
            bool promisedArrival = relocating && relocation.Source == station;
            if (relocating && !promisedArrival)
            {
                if (!hasReadReservedSlot)
                {
                    reservedSlot = DroneStationRelocationUtility.ReservedSlot(Reserved, station, request.RelocationTask, request.RelocationMemberIndex);
                }
                promisedArrival = reservedSlot >= 0;
            }
            if (!promisedArrival && !ownsDock) return DroneStationRequestStatus.Busy;
            // 준비 중 기존 정박은 충전/기존 작업 이탈만 계속하며 새 수납으로 계획 입력을 바꾸지 않는다.
            if (!promisedArrival && request.Operation == DroneStationOperation.Store) return DroneStationRequestStatus.Busy;
            if (ownsDock)
            {
                if (!Assignments.TryGetComponent(drone, out workerAssignment))
                {
                    return DroneStationRequestStatus.Rejected;
                }
                hasReadWorkerCargo = true;
                if (!promisedArrival && !relocating && request.Operation == DroneStationOperation.Activate &&
                    workerAssignment.Assignment == Entity.Null && Cargo[drone].Quantity == 0)
                {
                    return DroneStationRequestStatus.Busy;
                }
            }
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
                if (entry.Drone == drone)
                {
                    matches++;
                }
            }
            if (dock.Station != station || matches != 1)
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
        if (Cargo[drone].Quantity != 0 || Cargo[drone].Origin != DroneCargoOriginEnum.None)
        {
            return DroneStationRequestStatus.Busy;
        }
        return DroneStationRequestStatus.Applied;
    }

    private bool IsAtStation(Entity drone, Entity station)
    {
        return TryReadDockFootprint(station, out var footprint) && TryReadWorkerCell(drone, out var cell) &&
            footprint.Contains(cell);
    }

    public bool TryReadDockFootprint(Entity station, out DroneStationDockFootprint bounds)
    {
        bounds = default;
        if (!Footprints.TryGetComponent(station, out var footprint) ||
            !Directions.TryGetComponent(station, out var direction))
        {
            return false;
        }
        bounds = new DroneStationDockFootprint(station, Positions[station].Value, footprint.GetEffectiveSize(direction.dir));
        return true;
    }

    public bool TryReadWorkerCell(Entity drone, out int2 cell)
    {
        cell = default;
        if (!Positions.TryGetComponent(drone, out var dronePosition)) return false;
        float3 position = Transforms[drone].Position;
        cell = dronePosition.Value;
        return math.all(math.isfinite(position)) && math.all(math.floor(position.xy) == cell);
    }
}
