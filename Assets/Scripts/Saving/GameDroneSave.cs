using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>발행된 드론 원본을 저장 ID로 연결하고, 비공개 복원 ECB에 같은 원본과 참조를 구성한다.</summary>
internal static class GameDroneSave
{
    private const string SchedulingId = "drone-scheduling";
    private const string RemovedPrefix = "removed-";

    public static SavedDroneWorld Capture(EntityManager manager, Dictionary<Entity, string> savedIds)
    {
        GameDroneSaveValidation.Validate(manager);
        var snapshot = new SavedDroneWorld
        {
            NextActionReceipt = ReadSequence<DroneActionReceiptSequence>(manager)?.NextValue,
            NextStationRequest = ReadSequence<DroneStationRequestSequence>(manager)?.NextValue,
            NextTaskRequest = ReadSequence<DroneTaskRequestSequence>(manager)?.NextValue
        };
        var rows = new List<(Entity Entity, SavedDroneEntity Row)>();
        foreach (var existing in savedIds)
        {
            if (manager.HasComponent<BuildingIdentity>(existing.Key) || manager.HasComponent<ConstructionSite>(existing.Key))
            {
                rows.Add((existing.Key, new SavedDroneEntity { Id = existing.Value, Kind = SavedDroneKindEnum.Building }));
            }
            else if (manager.HasComponent<DroneRecoveryPending>(existing.Key))
            {
                rows.Add((existing.Key, new SavedDroneEntity { Id = existing.Value, Kind = SavedDroneKindEnum.Item }));
            }
        }
        CaptureOwners(manager, rows, savedIds, SavedDroneKindEnum.Worker, typeof(DroneWorker));
        CaptureOwners(manager, rows, savedIds, SavedDroneKindEnum.Task, typeof(DroneLogisticsTask));
        CaptureOwners(manager, rows, savedIds, SavedDroneKindEnum.Assignment, typeof(DroneTaskAssignment));
        CaptureOwners(manager, rows, savedIds, SavedDroneKindEnum.Action, typeof(DroneActionReadyRequest), typeof(DroneItemTransferResult));
        CaptureOwners(manager, rows, savedIds, SavedDroneKindEnum.StationRequest, typeof(DroneStationRequest), typeof(DroneStationResult));
        CaptureOwners(manager, rows, savedIds, SavedDroneKindEnum.TaskRequest, typeof(DroneTaskRequest), typeof(DroneTaskRequestResult));
        CaptureOwners(manager, rows, savedIds, SavedDroneKindEnum.Route, typeof(DroneRouteEvaluationRequest));
        CaptureOwners(manager, rows, savedIds, SavedDroneKindEnum.DemolitionCommand, typeof(DemolishBuildingRequest));

        using var scheduling = manager.CreateEntityQuery(typeof(DroneTaskGenerationBlockElement));
        Entity owner = scheduling.GetSingletonEntity();
        savedIds.Add(owner, SchedulingId);
        rows.Add((owner, new SavedDroneEntity { Id = SchedulingId, Kind = SavedDroneKindEnum.Scheduling }));

        var removed = new Dictionary<Entity, string>();
        var referenceIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        var references = new GameSnapshotArchive(entity =>
        {
            if (entity == Entity.Null)
            {
                return Entity.Null;
            }
            if (!savedIds.TryGetValue(entity, out string id))
            {
                if (manager.Exists(entity))
                {
                    throw new InvalidOperationException("드론 상태가 저장 범위 밖의 살아 있는 엔티티를 참조합니다.");
                }
                if (!removed.TryGetValue(entity, out id))
                {
                    id = RemovedPrefix + removed.Count.ToString(CultureInfo.InvariantCulture);
                    removed.Add(entity, id);
                }
            }
            return IndexReference(snapshot, referenceIndices, id);
        });
        foreach (var entry in rows)
        {
            GameDroneSaveSchema.Capture(manager, entry.Entity, entry.Row, references);
            if (entry.Row.Fields.Count != 0)
            {
                snapshot.Entities.Add(entry.Row);
            }
        }
        return snapshot;
    }

    private static void CaptureOwners(EntityManager manager, List<(Entity, SavedDroneEntity)> rows,
        Dictionary<Entity, string> savedIds, SavedDroneKindEnum kind, params ComponentType[] types)
    {
        using var query = manager.CreateEntityQuery(new EntityQueryDesc
        {
            Any = types,
            Options = EntityQueryOptions.IncludeDisabledEntities
        });
        using var entities = query.ToEntityArray(Allocator.Temp);
        foreach (Entity entity in entities)
        {
            string id = "drone-owner-" + rows.Count.ToString(CultureInfo.InvariantCulture);
            var row = new SavedDroneEntity { Id = id, Kind = kind, Disabled = manager.HasComponent<Disabled>(entity) };
            if (kind == SavedDroneKindEnum.Worker)
            {
                float3 position = manager.GetComponentData<LocalTransform>(entity).Position;
                row.Position = new[] { position.x, position.y, position.z };
            }
            savedIds.Add(entity, id);
            rows.Add((entity, row));
        }
    }

    public static void ValidateFileStructure(SavedDroneWorld snapshot)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in snapshot.Entities)
        {
            if (row == null)
            {
                throw new InvalidDataException("드론 저장 기록이 없습니다.");
            }
            if (string.IsNullOrEmpty(row.Id))
            {
                throw new InvalidDataException("드론 저장 ID가 없습니다.");
            }
            if (!ids.Add(row.Id))
            {
                throw new InvalidDataException("드론 저장 ID가 중복됩니다.");
            }
            if (row.Id.StartsWith(RemovedPrefix, StringComparison.Ordinal))
            {
                throw new InvalidDataException("드론 저장 ID에 예약된 이름을 사용할 수 없습니다.");
            }
            if ((row.Kind == SavedDroneKindEnum.Scheduling) != (row.Id == SchedulingId))
            {
                throw new InvalidDataException("드론 정산 관리자의 저장 ID와 역할이 일치하지 않습니다.");
            }
            if (row.Kind == SavedDroneKindEnum.Worker)
            {
                if (row.Position == null)
                {
                    throw new InvalidDataException("드론 위치가 없습니다.");
                }
                if (row.Position.Length != 3)
                {
                    throw new InvalidDataException("드론 위치는 세 축이 필요합니다.");
                }
            }
            GameDroneSaveSchema.ValidateState(row);
        }
        if (!ids.Contains(SchedulingId))
        {
            throw new InvalidDataException("드론 정산 관리자의 저장 기록이 없습니다.");
        }
        ValidateDockReferences(snapshot);
    }

    // 정박의 양방향 참조와 유일성을 드론 ID와 정거장 ID로 검사한다.
    private static void ValidateDockReferences(SavedDroneWorld snapshot)
    {
        var occupants = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in snapshot.Entities)
        {
            if (row.Kind != SavedDroneKindEnum.Building || !row.TryGet<List<DockedDroneElement>>(out var docks))
            {
                continue;
            }
            foreach (var dock in docks)
            {
                if (dock.SlotIndex != -1)
                {
                    throw new InvalidDataException("정박 기록에는 수납 슬롯을 지정할 수 없습니다.");
                }
                string droneId = ReadReferenceId(snapshot, dock.Drone);
                if (string.IsNullOrEmpty(droneId))
                {
                    throw new InvalidDataException("정박 기록에 드론 참조가 없습니다.");
                }
                if (!occupants.TryAdd(droneId, row.Id))
                {
                    throw new InvalidDataException("정박한 드론이 중복 등록되어 있습니다.");
                }
            }
        }
        foreach (var row in snapshot.Entities)
        {
            if (row.Kind != SavedDroneKindEnum.Worker || !row.TryGet<DroneDocked>(out var dock))
            {
                continue;
            }
            if (dock.SlotIndex != -1 || !occupants.Remove(row.Id, out var station) ||
                station != ReadReferenceId(snapshot, dock.Station))
            {
                throw new InvalidDataException("드론의 정박 기록이 정거장의 정박 기록과 일치하지 않습니다.");
            }
        }
        if (occupants.Count != 0)
        {
            throw new InvalidDataException("정거장에 등록된 드론의 정박 기록이 없습니다.");
        }
    }

    internal static Entity IndexReference(SavedDroneWorld snapshot, Dictionary<string, int> indices, string id)
    {
        if (!indices.TryGetValue(id, out int index))
        {
            snapshot.References.Add(id);
            index = snapshot.References.Count;
            indices.Add(id, index);
        }
        return new Entity { Index = index };
    }

    internal static string ReadReferenceId(SavedDroneWorld snapshot, Entity reference)
    {
        if (reference == Entity.Null)
        {
            return null;
        }
        if (reference.Version != 0 || reference.Index <= 0 || reference.Index > snapshot.References.Count)
        {
            throw new InvalidDataException("드론 저장 참조 번호가 범위를 벗어났습니다.");
        }
        return snapshot.References[reference.Index - 1];
    }

    internal static bool IsRemovedReference(string id) =>
        id.StartsWith(RemovedPrefix, StringComparison.Ordinal) &&
        int.TryParse(id.Substring(RemovedPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out _);

    public static void Record(EntityManager manager, EntityCommandBuffer ecb, SavedDroneWorld snapshot,
        Dictionary<string, Entity> worldEntities)
    {
        var entities = new Dictionary<string, Entity>(StringComparer.Ordinal);
        foreach (var existing in worldEntities)
        {
            entities.Add(existing.Key, existing.Value);
        }
        foreach (var row in snapshot.Entities)
        {
            Entity entity = RecordOwner(manager, ref ecb, row, worldEntities);
            if (row.Kind is not (SavedDroneKindEnum.Building or SavedDroneKindEnum.Item))
            {
                if (!entities.TryAdd(row.Id, entity))
                {
                    throw new InvalidDataException("드론 저장 ID가 기존 월드 엔티티와 겹칩니다.");
                }
                if (row.Disabled)
                {
                    ecb.AddComponent<Disabled>(entity);
                }
            }
        }

        // 삭제된 참조도 다른 삭제 참조와의 동일성을 유지해야 한다. 지연 참조를 연결한 뒤 임시 대상을 실제로 삭제한다.
        var removed = new List<Entity>();
        EntityCommandBuffer playback = ecb;
        var references = new GameSnapshotArchive(reference =>
        {
            string id = ReadReferenceId(snapshot, reference);
            if (id == null)
            {
                return Entity.Null;
            }
            if (entities.TryGetValue(id, out var entity))
            {
                return entity;
            }
            if (!IsRemovedReference(id))
            {
                throw new InvalidDataException("드론 상태가 파일에 없는 대상을 참조합니다.");
            }
            entity = playback.CreateEntity();
            entities.Add(id, entity);
            removed.Add(entity);
            return entity;
        });
        foreach (var row in snapshot.Entities)
        {
            Entity entity = entities[row.Id];
            GameDroneSaveSchema.Record(ecb, entity, row, references);
#if UNITY_EDITOR
            if (row.Kind == SavedDroneKindEnum.Task && row.TryGet(out DroneLogisticsTask task))
            {
                RuntimeEntityDebugNames.SetDroneTask(ecb, entity, task.Kind);
            }
#endif
        }
        foreach (Entity tombstone in removed)
        {
            ecb.DestroyEntity(tombstone);
        }
        RecordSequences(ref ecb, snapshot);
    }

    private static Entity RecordOwner(EntityManager manager, ref EntityCommandBuffer ecb, SavedDroneEntity row,
        Dictionary<string, Entity> worldEntities)
    {
        switch (row.Kind)
        {
            case SavedDroneKindEnum.Worker:
                var position = new float3(row.Position[0], row.Position[1], row.Position[2]);
                using (var battery = manager.CreateEntityQuery(typeof(DroneBatteryConfig)))
                {
                    float maximum = battery.GetSingleton<DroneBatteryConfig>().MaximumCharge;
                    if (!DroneSpawnUtility.TrySpawnActiveDrone(manager, ref ecb, position, maximum, out Entity drone, out string error))
                    {
                        throw new InvalidDataException(error);
                    }
                    return drone;
                }
            case SavedDroneKindEnum.Building:
            case SavedDroneKindEnum.Item:
                if (!worldEntities.TryGetValue(row.Id, out var target) ||
                    (row.Kind == SavedDroneKindEnum.Item ? !manager.HasComponent<ItemIdentity>(target) :
                        !manager.HasComponent<BuildingIdentity>(target) && !manager.HasComponent<ConstructionSite>(target)))
                {
                    throw new InvalidDataException("드론 소유 상태의 대상과 월드 엔티티 역할이 일치하지 않습니다.");
                }
                return target;
            case SavedDroneKindEnum.Scheduling:
                using (var query = manager.CreateEntityQuery(typeof(DroneTaskGenerationBlockElement)))
                {
                    return query.GetSingletonEntity();
                }
            default:
                return ecb.CreateEntity();
        }
    }

    private static T? ReadSequence<T>(EntityManager manager) where T : unmanaged, IComponentData
    {
        using var query = manager.CreateEntityQuery(typeof(T));
        return query.IsEmptyIgnoreFilter ? null : query.GetSingleton<T>();
    }

    private static void RecordSequences(ref EntityCommandBuffer ecb, SavedDroneWorld saved)
    {
        if (saved.NextActionReceipt == 0 || saved.NextStationRequest == 0 || saved.NextTaskRequest == 0)
        {
            throw new InvalidDataException("드론 요청의 다음 접수 순번이 유효하지 않습니다.");
        }
        if (saved.NextActionReceipt.HasValue)
        {
            ecb.AddComponent(ecb.CreateEntity(), new DroneActionReceiptSequence { NextValue = saved.NextActionReceipt.Value });
        }
        if (saved.NextStationRequest.HasValue)
        {
            ecb.AddComponent(ecb.CreateEntity(), new DroneStationRequestSequence { NextValue = saved.NextStationRequest.Value });
        }
        if (saved.NextTaskRequest.HasValue)
        {
            ecb.AddComponent(ecb.CreateEntity(), new DroneTaskRequestSequence { NextValue = saved.NextTaskRequest.Value });
        }
    }

}
