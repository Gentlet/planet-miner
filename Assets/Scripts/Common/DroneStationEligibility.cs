using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Decision과 최종 Apply가 공유하는 출고/정박/수납 자격 검사다.
/// 역할 구성은 QueryMask로 확인하고 값은 필요한 RO Lookup에서 직접 읽는다. ECS 상태를 복제하지 않는다.
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
    [ReadOnly] public ComponentLookup<DestroyItemRequest> DestroyRequests;
    [ReadOnly] public ComponentLookup<Storage> Storages;
    [ReadOnly] public ComponentLookup<GridPosition> Positions;
    [ReadOnly] public ComponentLookup<LocalTransform> Transforms;
    [ReadOnly] public ComponentLookup<BuildingFootprint> Footprints;
    [ReadOnly] public ComponentLookup<Direction> Directions;
    [ReadOnly] public BufferLookup<StoredItemElement> Items;
    [ReadOnly] public BufferLookup<DockedDroneElement> Docked;

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
            DestroyRequests = state.GetComponentLookup<DestroyItemRequest>(true),
            Storages = state.GetComponentLookup<Storage>(true),
            Positions = state.GetComponentLookup<GridPosition>(true),
            Transforms = state.GetComponentLookup<LocalTransform>(true),
            Footprints = state.GetComponentLookup<BuildingFootprint>(true),
            Directions = state.GetComponentLookup<Direction>(true),
            Items = state.GetBufferLookup<StoredItemElement>(true),
            Docked = state.GetBufferLookup<DockedDroneElement>(true)
        };
    }

    public DroneStationRequestStatus Validate(in DroneStationRequest request, float maximum)
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
        if (request.Operation == DroneStationOperation.Activate && ItemTypes.HasComponent(drone))
        {
            if (!ItemMask.MatchesIgnoreFilter(drone) || ItemTypes[drone].Type != ItemTypeEnum.Drone ||
                Owners[drone].Owner != station || !ContainsItem(Items[station], drone))
            {
                return DroneStationRequestStatus.Rejected;
            }
            if (Batteries.HasComponent(drone))
            {
                float charge = Batteries[drone].CurrentCharge;
                if (!math.isfinite(charge) || charge < 0f || charge > maximum)
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
        if (Activities[drone].Activity != DroneActivityKind.Idle)
        {
            return DroneStationRequestStatus.Busy;
        }
        if (Docks.HasComponent(drone))
        {
            var dock = Docks[drone];
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
            return Docks.HasComponent(drone) ? DroneStationRequestStatus.Applied : DroneStationRequestStatus.Rejected;
        }
        if (!IsAtStation(drone, station))
        {
            return DroneStationRequestStatus.Rejected;
        }
        if (request.Operation == DroneStationOperation.Dock)
        {
            return Docks.HasComponent(drone) ? DroneStationRequestStatus.Rejected : DroneStationRequestStatus.Applied;
        }
        if (Batteries[drone].CurrentCharge != maximum || Assignments[drone].Assignment != Entity.Null ||
            !Items[drone].IsEmpty || Cargo[drone].Origin != DroneCargoOriginEnum.None)
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
        if (!Footprints.HasComponent(station) || !Directions.HasComponent(station) || !Positions.HasComponent(drone))
        {
            return false;
        }
        float3 position = Transforms[drone].Position;
        if (!math.all(math.isfinite(position)))
        {
            return false;
        }
        int2 cell = Positions[drone].Value;
        int2 anchor = Positions[station].Value;
        int2 size = Footprints[station].GetEffectiveSize(Directions[station].dir);
        return math.all(math.floor(position.xy) == cell) && math.all(cell >= anchor) &&
            (long)cell.x < (long)anchor.x + size.x && (long)cell.y < (long)anchor.y + size.y;
    }
}
