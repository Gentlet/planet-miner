using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Entities;

/// <summary>저장 캡처 전과 복원 World 공개 전에 배정/화물, 정박 소유권과 예약 수량을 확인한다.</summary>
internal static class GameDroneSaveValidation
{
    public static void Validate(EntityManager manager)
    {
        ValidateWorkers(manager);
        ValidateDockOwnership(manager);
        ValidateReservations(manager);
        ValidateTaskSequences(manager);
    }

    private static void ValidateWorkers(EntityManager manager)
    {
        using var workers = manager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<DroneWorker>() },
            Options = EntityQueryOptions.IncludeDisabledEntities
        });
        using var entities = workers.ToEntityArray(Allocator.Temp);
        using var batteries = manager.CreateEntityQuery(typeof(DroneBatteryConfig));
        using var capacities = manager.CreateEntityQuery(typeof(DroneCapacityState));
        float maximum = batteries.GetSingleton<DroneBatteryConfig>().MaximumCharge;
        int capacity = capacities.GetSingleton<DroneCapacityState>().CarryingCapacity;
        foreach (Entity entity in entities)
        {
            var battery = manager.GetComponentData<DroneBatteryState>(entity);
            var cargo = manager.GetComponentData<DroneCargoState>(entity);
            if (!DroneBatteryUtility.IsValid(battery, maximum) ||
                cargo.Quantity < 0 || cargo.Quantity > capacity ||
                (cargo.Quantity > 0 && (cargo.ItemType == ItemTypeEnum.None || cargo.Origin == DroneCargoOriginEnum.None)) ||
                (cargo.Quantity == 0 && cargo.ItemType != ItemTypeEnum.None))
            {
                throw new InvalidDataException("드론의 배터리 또는 화물 수량이 유효하지 않습니다.");
            }
            Entity assignment = manager.GetComponentData<DroneWorkerAssignment>(entity).Assignment;
            if (assignment != Entity.Null &&
                (!manager.HasComponent<DroneTaskAssignment>(assignment) ||
                 manager.GetComponentData<DroneTaskAssignment>(assignment).Worker != entity))
            {
                throw new InvalidDataException("드론과 활성 배정의 연결이 일치하지 않습니다.");
            }
        }
    }

    private static void ValidateDockOwnership(EntityManager manager)
    {
        using var stations = manager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<DockedDroneElement>() },
            Options = EntityQueryOptions.IncludeDisabledEntities
        });
        using var stationEntities = stations.ToEntityArray(Allocator.Temp);
        var registeredWorkers = new HashSet<Entity>();
        foreach (Entity station in stationEntities)
        {
            var docks = manager.GetBuffer<DockedDroneElement>(station, true);
            if (docks.IsEmpty)
            {
                continue;
            }
            if (!manager.HasComponent<DroneStation>(station) || !manager.HasComponent<DroneStationStorage>(station) ||
                !manager.HasComponent<Storage>(station) || !manager.HasBuffer<StorageSlotElement>(station))
            {
                throw new InvalidDataException("정박 기록에 유효한 정거장 보관 슬롯이 없습니다.");
            }
            foreach (var entry in docks)
            {
                if (!registeredWorkers.Add(entry.Drone))
                {
                    throw new InvalidDataException("드론이 둘 이상의 정박 관계에 등록되어 있습니다.");
                }
                if (!StationStorageUtility.MatchesDockedWorker(manager, station, entry.Drone))
                {
                    throw new InvalidDataException("정거장과 드론의 정박 관계가 일치하지 않습니다.");
                }
                if (entry.SlotIndex != -1 || manager.GetComponentData<DroneDocked>(entry.Drone).SlotIndex != -1)
                {
                    throw new InvalidDataException("정박 기록에는 수납 슬롯을 지정할 수 없습니다.");
                }
            }
        }
        using var dockedWorkers = manager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<DroneDocked>() },
            Options = EntityQueryOptions.IncludeDisabledEntities
        });
        using var workers = dockedWorkers.ToEntityArray(Allocator.Temp);
        foreach (Entity worker in workers)
        {
            if (!registeredWorkers.Contains(worker))
            {
                throw new InvalidDataException("정박한 드론이 정거장에 등록되어 있지 않습니다.");
            }
        }
    }

    private static void ValidateReservations(EntityManager manager)
    {
        var siteTotals = new Dictionary<(Entity, ItemTypeEnum), long>();
        var transportTotals = new Dictionary<Entity, long>();
        using (var query = manager.CreateEntityQuery(typeof(ConstructionSupplyReservation)))
        using (var assigned = query.ToComponentDataArray<ConstructionSupplyReservation>(Allocator.Temp))
        {
            foreach (var reservation in assigned)
            {
                AddQuantity(siteTotals, (reservation.Site, reservation.ItemType), reservation.RemainingQuantity);
            }
        }
        using (var query = manager.CreateEntityQuery(typeof(DroneTransportReservation)))
        using (var assigned = query.ToComponentDataArray<DroneTransportReservation>(Allocator.Temp))
        {
            foreach (var reservation in assigned)
            {
                AddQuantity(transportTotals, reservation.Task, reservation.RemainingQuantity);
            }
        }
        using (var scheduler = manager.CreateEntityQuery(typeof(DroneTaskPendingPublicationElement), typeof(DroneTransportPendingReservationElement)))
        {
            Entity owner = scheduler.GetSingletonEntity();
            foreach (var pending in manager.GetBuffer<DroneTaskPendingPublicationElement>(owner, true))
            {
                if (!pending.PublicationQueued)
                {
                    AddQuantity(siteTotals, (pending.Candidate.Destination, pending.Candidate.ItemType), pending.CommittedQuantity);
                }
            }
            foreach (var pending in manager.GetBuffer<DroneTransportPendingReservationElement>(owner, true))
            {
                AddQuantity(transportTotals, pending.Reservation.Task, pending.Reservation.RemainingQuantity);
            }
        }
        ValidateConstructionReservations(manager, siteTotals);
        ValidateTransportReservations(manager, transportTotals);
    }

    private static void ValidateConstructionReservations(EntityManager manager, Dictionary<(Entity, ItemTypeEnum), long> totals)
    {
        using var query = manager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<ConstructionMaterialRequirementElement>() },
            Options = EntityQueryOptions.IncludeDisabledEntities
        });
        using var sites = query.ToEntityArray(Allocator.Temp);
        foreach (Entity site in sites)
        {
            foreach (var material in manager.GetBuffer<ConstructionMaterialRequirementElement>(site, true))
            {
                var key = (site, material.ItemType);
                totals.TryGetValue(key, out long total);
                if (material.ReservedQuantity != total)
                {
                    throw new InvalidDataException("공사 현장의 공급 예약 합계가 개별 확보량과 일치하지 않습니다.");
                }
                totals.Remove(key);
            }
        }
        foreach (var remainder in totals)
        {
            if (remainder.Value > 0 && manager.Exists(remainder.Key.Item1))
            {
                throw new InvalidDataException("공사 자재 예약의 살아 있는 현장 또는 품목이 올바르지 않습니다.");
            }
        }
    }

    private static void ValidateTransportReservations(EntityManager manager, Dictionary<Entity, long> totals)
    {
        using var query = manager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<DroneLogisticsTask>() },
            Options = EntityQueryOptions.IncludeDisabledEntities
        });
        using var tasks = query.ToEntityArray(Allocator.Temp);
        foreach (Entity task in tasks)
        {
            if (!DroneSchedulingUtility.IsTransportKind(manager.GetComponentData<DroneLogisticsTask>(task).Kind))
            {
                continue;
            }
            if (!manager.HasComponent<DroneTransportRequirement>(task) || !manager.HasComponent<DroneTransportProgress>(task))
            {
                throw new InvalidDataException("반입/반출 작업에 운반 요구량 또는 진행량이 없습니다.");
            }
            var progress = manager.GetComponentData<DroneTransportProgress>(task);
            totals.TryGetValue(task, out long total);
            if (progress.DeliveredQuantity < 0 || progress.ReservedQuantity != total ||
                (long)progress.DeliveredQuantity + total > manager.GetComponentData<DroneTransportRequirement>(task).Quantity)
            {
                throw new InvalidDataException("운반 작업의 완료량 또는 예약 합계가 개별 확보량과 일치하지 않습니다.");
            }
            totals.Remove(task);
        }
        foreach (var remainder in totals)
        {
            if (remainder.Value > 0 && manager.Exists(remainder.Key))
            {
                throw new InvalidDataException("운반 예약의 살아 있는 대상에 수량 기록이 없습니다.");
            }
        }
    }

    private static void AddQuantity<TKey>(Dictionary<TKey, long> totals, TKey owner, int quantity)
    {
        if (quantity < 0)
        {
            throw new InvalidDataException("드론 예약의 남은 수량이 음수입니다.");
        }
        if (quantity != 0)
        {
            totals.TryGetValue(owner, out long total);
            totals[owner] = total + quantity;
        }
    }

    private static void ValidateTaskSequences(EntityManager manager)
    {
        using var sequence = manager.CreateEntityQuery(typeof(DroneTaskSequence));
        ulong next = sequence.GetSingleton<DroneTaskSequence>().NextValue;
        using var query = manager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<DroneLogisticsTask>() },
            Options = EntityQueryOptions.IncludeDisabledEntities
        });
        using var tasks = query.ToComponentDataArray<DroneLogisticsTask>(Allocator.Temp);
        var seen = new HashSet<ulong>();
        foreach (var task in tasks)
        {
            if (task.CreationSequence == 0 || task.CreationSequence >= next)
            {
                throw new InvalidDataException("드론 작업 생성 순번이 유효하지 않습니다.");
            }
            if (!seen.Add(task.CreationSequence))
            {
                throw new InvalidDataException("드론 작업 생성 순번이 중복됩니다.");
            }
        }
    }
}
