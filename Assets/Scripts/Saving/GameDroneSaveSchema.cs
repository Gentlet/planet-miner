using System.IO;
using System;
using System.Collections.Generic;
#if UNITY_EDITOR
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
#endif
using Unity.Entities;

/// <summary>파일의 이름과 게임 컴포넌트를 명시적으로 맵핑한다. 틱마다 다시 만드는 Decision과 공간 인덱스는 제외한다.</summary>
internal static class GameDroneSaveSchema
{
    private static readonly IReadOnlyDictionary<SavedDroneKindEnum, Field[]> Fields =
        new Dictionary<SavedDroneKindEnum, Field[]>
        {
            [SavedDroneKindEnum.Worker] = new Field[]
            {
                Component<DroneBatteryState>("battery", GameDroneBinaryLayout.Fields, required: true),
                Component<DroneWorkerObservation>("observation", GameDroneBinaryLayout.Fields, required: true),
                Component<DroneWorkerAssignment>("assignment", GameDroneBinaryLayout.Fields, required: true),
                Component<DroneCargoState>("cargo", GameDroneBinaryLayout.Fields, required: true),
                Component<DroneActivityState>("activity", GameDroneBinaryLayout.Fields, required: true),
                Component<DroneMovementState>("movement", GameDroneBinaryLayout.Fields, required: true),
                Component<DroneMovementResult>("movementResult", GameDroneBinaryLayout.Fields, required: true),
                Component<DroneDocked>("docked", GameDroneBinaryLayout.Fields),
                Component<DroneStationRelocation>("relocation", GameDroneBinaryLayout.Fields)
            },
            [SavedDroneKindEnum.Task] = new Field[]
            {
                Component<DroneLogisticsTask>("task", GameDroneBinaryLayout.Fields, required: true),
                Component<DroneTransportRequirement>("transportRequirement", GameDroneBinaryLayout.Fields),
                Component<DroneTransportProgress>("transportProgress", GameDroneBinaryLayout.Fields),
                Component<DroneDemolitionProgress>("demolitionProgress", GameDroneBinaryLayout.Fields),
                Component<BuildingDemolitionExecutionRequest>("demolitionExecution", GameDroneBinaryLayout.Fields),
                Component<BuildingDemolitionResult>("demolitionResult", GameDroneBinaryLayout.Fields),
                Buffer<DroneDemolitionRefundElement>("refund", GameDroneBinaryLayout.Fields),
                Buffer<DroneStationRelocationMemberElement>("relocationMembers", GameDroneBinaryLayout.Fields),
                Component<GridPosition>("refundPosition", GameDroneBinaryLayout.Fields),
                Component<BuildingFootprint>("refundFootprint", GameDroneBinaryLayout.Fields),
                Component<Direction>("refundDirection", GameDroneBinaryLayout.Fields)
            },
            [SavedDroneKindEnum.Assignment] = new Field[]
            {
                Component<DroneTaskAssignment>("assignment", GameDroneBinaryLayout.Fields, required: true),
                Component<ConstructionSupplyReservation>("constructionReservation", GameDroneBinaryLayout.Fields),
                Component<DroneTransportReservation>("transportReservation", GameDroneBinaryLayout.Fields)
            },
            [SavedDroneKindEnum.Action] = new Field[]
            {
                Component<DroneActionReadyRequest>("request", GameDroneBinaryLayout.Fields),
                Component<DroneItemTransferResult>("result", GameDroneBinaryLayout.Fields),
                Component<DroneAutomaticActionRequest>("automatic", GameDroneBinaryLayout.Fields)
            },
            [SavedDroneKindEnum.StationRequest] = new Field[]
            {
                Component<DroneStationRequest>("request", GameDroneBinaryLayout.Fields),
                Component<DroneStationResult>("result", GameDroneBinaryLayout.Fields),
                Component<DroneAutomaticStationRequest>("automatic", GameDroneBinaryLayout.Fields),
                Component<DroneStationRelocationRequest>("relocation", GameDroneBinaryLayout.Fields)
            },
            [SavedDroneKindEnum.TaskRequest] = new Field[]
            {
                Component<DroneTaskRequest>("request", GameDroneBinaryLayout.Fields),
                Component<DroneTaskRequestResult>("result", GameDroneBinaryLayout.Fields),
                Component<BuildingDemolitionAutomaticTaskRequest>("demolitionOwner", GameDroneBinaryLayout.Fields)
            },
            [SavedDroneKindEnum.Route] = new Field[]
            {
                Component<DroneRouteEvaluationRequest>("request", GameDroneBinaryLayout.Fields, required: true),
                Component<DroneRouteEvaluationResult>("result", GameDroneBinaryLayout.Fields)
            },
            [SavedDroneKindEnum.DemolitionCommand] = new Field[]
            {
                Component<DemolishBuildingRequest>("request", GameDroneBinaryLayout.Fields, required: true)
            },
            [SavedDroneKindEnum.Building] = new Field[]
            {
                Component<PendingBuildingDemolition>("pendingDemolition", GameDroneBinaryLayout.Fields),
                Component<PreparingStationDemolition>("preparingDemolition", GameDroneBinaryLayout.Fields),
                Buffer<DockedDroneElement>("docked", GameDroneBinaryLayout.Fields),
                Buffer<DroneStationReservedSlotElement>("reservedSlots", GameDroneBinaryLayout.Fields)
            },
            [SavedDroneKindEnum.Item] = new Field[]
            {
                Component<DroneRecoveryPending>("recovery", GameDroneBinaryLayout.Fields)
            },
            [SavedDroneKindEnum.Scheduling] = new Field[]
            {
                Buffer<DroneTaskGenerationBlockElement>("generationBlocks", GameDroneBinaryLayout.Fields, required: true),
                Buffer<DroneTaskPendingPublicationElement>("pendingAssignments", GameDroneBinaryLayout.Fields, required: true),
                Buffer<DroneTransportPendingReservationElement>("pendingTransport", GameDroneBinaryLayout.Fields, required: true)
            }
        };


    public static void Capture(EntityManager manager, Entity entity, SavedDroneEntity row, GameSnapshotArchive references)
    {
        foreach (Field field in Fields[row.Kind])
        {
            object value = field.Capture(manager, entity, references);
            if (value != null)
            {
                row.Fields.Add(field.Type, value);
            }
        }
        ValidateState(row);
    }

    public static void Transfer(GameSnapshotArchive archive, SavedDroneEntity row)
    {
        foreach (Field field in RequireFields(row.Kind))
        {
            row.Fields.TryGetValue(field.Type, out object value);
            bool present = value != null;
            archive.Value(ref present);
            if (present)
            {
                value = field.Transfer(archive, value);
                if (archive.Reading)
                {
                    row.Fields.Add(field.Type, value);
                }
            }
        }
        ValidateState(row);
    }

    public static void ValidateState(SavedDroneEntity row)
    {
        int knownFields = 0;
        foreach (Field field in RequireFields(row.Kind))
        {
            if (row.Fields.ContainsKey(field.Type))
            {
                knownFields++;
            }
            else if (field.Required)
            {
                throw new InvalidDataException($"필수 드론 상태가 없습니다. {field.Name}");
            }
        }
        if (knownFields != row.Fields.Count)
        {
            throw new InvalidDataException("드론 역할에 지원하지 않는 상태가 있습니다.");
        }
        if (row.TryGet<DroneLogisticsTask>(out var task))
        {
            // 철거 Writer는 환급이 아직 0이어도 버퍼를 생성하고, EndBuilding은 그 버퍼를 교체한다.
            bool isDemolition = task.Kind == DroneLogisticsTaskKindEnum.BuildingDemolition;
            if (row.Has<DroneDemolitionProgress>() != isDemolition || row.Has<List<DroneDemolitionRefundElement>>() != isDemolition)
            {
                throw new InvalidDataException("철거 작업에는 진행 기록과 환급 버퍼가 함께 있어야 합니다.");
            }
            bool isTransport = DroneSchedulingUtility.IsTransportKind(task.Kind);
            if (row.Has<DroneTransportRequirement>() != isTransport || row.Has<DroneTransportProgress>() != isTransport)
            {
                throw new InvalidDataException("반입/반출 작업에는 운반 요구량과 진행량이 함께 있어야 합니다.");
            }
        }

        bool validRequest = row.Kind switch
        {
            SavedDroneKindEnum.Action => row.Has<DroneActionReadyRequest>() != row.Has<DroneItemTransferResult>(),
            SavedDroneKindEnum.StationRequest => row.Has<DroneStationRequest>() != row.Has<DroneStationResult>(),
            SavedDroneKindEnum.TaskRequest => row.Has<DroneTaskRequest>() != row.Has<DroneTaskRequestResult>(),
            _ => true
        };
        if (!validRequest)
        {
            throw new InvalidDataException("드론 요청에는 접수 상태와 완료 결과 중 하나만 있어야 합니다.");
        }
    }

    public static void ValidateReferences(SavedDroneEntity row, GameSnapshotArchive references)
    {
        foreach (Field field in Fields[row.Kind])
        {
            if (row.Fields.TryGetValue(field.Type, out object value))
            {
                field.Transfer(references, value);
            }
        }
    }

    private static Field[] RequireFields(SavedDroneKindEnum kind)
    {
        if (!Fields.TryGetValue(kind, out Field[] fields))
        {
            throw new InvalidDataException("저장된 드론 엔티티의 역할을 읽을 수 없습니다.");
        }
        return fields;
    }

    public static void Record(EntityCommandBuffer ecb, Entity entity, SavedDroneEntity row, GameSnapshotArchive references)
    {
        foreach (Field field in Fields[row.Kind])
        {
            if (row.Fields.TryGetValue(field.Type, out object value))
            {
                field.Record(ecb, entity, value, references);
            }
        }
        RecordRequestRuntime(ecb, entity, row);
    }

#if UNITY_EDITOR
    // 기존 슬롯의 형식 3 입출력은 동일한 필드 목록에서 이름만 사용한다.
    public static void ReadLegacyState(SavedDroneEntity row, JObject state, JsonSerializer serializer)
    {
        int knownFields = 0;
        foreach (Field field in RequireFields(row.Kind))
        {
            if (state.TryGetValue(field.Name, out JToken token))
            {
                row.Fields.Add(field.Type, field.ReadLegacy(token, serializer));
                knownFields++;
            }
        }
        if (knownFields != state.Count)
        {
            throw new NotSupportedException("지원하지 않는 드론 상태가 포함되어 있습니다.");
        }
    }

    public static JObject WriteLegacyState(SavedDroneEntity row, JsonSerializer serializer)
    {
        var state = new JObject();
        foreach (Field field in Fields[row.Kind])
        {
            if (row.Fields.TryGetValue(field.Type, out object value))
            {
                state.Add(field.Name, JToken.FromObject(value, serializer));
            }
        }
        return state;
    }

#endif

    private static void RecordRequestRuntime(EntityCommandBuffer ecb, Entity entity, SavedDroneEntity row)
    {
        switch (row.Kind)
        {
            case SavedDroneKindEnum.Action:
                if (!row.Has<DroneAutomaticActionRequest>())
                {
                    ecb.AddComponent<RestoredDroneActionResultOwner>(entity);
                }
                if (row.Has<DroneActionReadyRequest>())
                {
                    ecb.AddComponent<DroneItemTransferDecision>(entity);
                    ecb.AddBuffer<DroneItemTransferItemDecisionElement>(entity);
                    ecb.AddBuffer<DroneItemTransferSlotDecisionElement>(entity);
                }
                break;
            case SavedDroneKindEnum.StationRequest:
                if (!row.Has<DroneAutomaticStationRequest>() && !row.Has<DroneStationRelocationRequest>())
                {
                    ecb.AddComponent<WorldUiRequestOwner>(entity);
                }
                if (row.Has<DroneStationRequest>())
                {
                    ecb.AddComponent<DroneStationDecision>(entity);
                }
                break;
            case SavedDroneKindEnum.TaskRequest:
                if (!row.Has<BuildingDemolitionAutomaticTaskRequest>())
                {
                    ecb.AddComponent<WorldUiRequestOwner>(entity);
                }
                if (row.Has<DroneTaskRequest>())
                {
                    ecb.AddComponent<DroneTaskRequestDecision>(entity);
                    ecb.AddComponent<DroneTaskRequestExecution>(entity);
                }
                break;
            case SavedDroneKindEnum.Route:
                ecb.AddComponent<DroneRouteEvaluationDecision>(entity);
                break;
        }
    }


    private static Field Component<T>(string name, GameSnapshotArchive.Layout<T> layout, bool required = false)
        where T : unmanaged, IComponentData => new ComponentField<T>(name, layout, required);

    private static Field Buffer<T>(string name, GameSnapshotArchive.Layout<T> layout, bool required = false)
        where T : unmanaged, IBufferElementData => new BufferField<T>(name, layout, required);

    private abstract class Field
    {
        public readonly Type Type;
        public readonly string Name;
        public readonly bool Required;

        protected Field(Type type, string name, bool required)
        {
            Type = type;
            Name = name;
            Required = required;
        }

        public abstract object Capture(EntityManager manager, Entity entity, GameSnapshotArchive references);
        public abstract object Transfer(GameSnapshotArchive archive, object value);
        public abstract void Record(EntityCommandBuffer ecb, Entity entity, object value, GameSnapshotArchive references);
#if UNITY_EDITOR
        public abstract object ReadLegacy(JToken token, JsonSerializer serializer);

        protected T ReadLegacyObject<T>(JToken token, JsonSerializer serializer)
        {
            if (token is not JObject value)
            {
                throw new JsonSerializationException($"드론 상태에 객체가 필요합니다. {Name}");
            }
            return value.ToObject<T>(serializer);
        }
#endif
    }

    private sealed class ComponentField<T> : Field where T : unmanaged, IComponentData
    {
        private readonly GameSnapshotArchive.Layout<T> _layout;

        public ComponentField(string name, GameSnapshotArchive.Layout<T> layout, bool required)
            : base(typeof(T), name, required) => _layout = layout;

        public override object Capture(EntityManager manager, Entity entity, GameSnapshotArchive references)
        {
            if (!manager.HasComponent<T>(entity))
            {
                return null;
            }
            T value = ComponentType.ReadOnly<T>().IsZeroSized ? default : manager.GetComponentData<T>(entity);
            _layout(references, ref value);
            return value;
        }

        public override object Transfer(GameSnapshotArchive archive, object value)
        {
            T entry = archive.Reading ? default : (T)value;
            _layout(archive, ref entry);
            return entry;
        }

        public override void Record(EntityCommandBuffer ecb, Entity entity, object value, GameSnapshotArchive references)
        {
            if (ComponentType.ReadOnly<T>().IsZeroSized)
            {
                ecb.AddComponent<T>(entity);
                return;
            }
            T entry = (T)value;
            _layout(references, ref entry);
            ecb.AddComponent(entity, entry);
        }

#if UNITY_EDITOR
        public override object ReadLegacy(JToken token, JsonSerializer serializer) => ReadLegacyObject<T>(token, serializer);
#endif
    }

    private sealed class BufferField<T> : Field where T : unmanaged, IBufferElementData
    {
        private readonly GameSnapshotArchive.Layout<T> _layout;

        public BufferField(string name, GameSnapshotArchive.Layout<T> layout, bool required)
            : base(typeof(List<T>), name, required) => _layout = layout;

        public override object Capture(EntityManager manager, Entity entity, GameSnapshotArchive references)
        {
            if (!manager.HasBuffer<T>(entity))
            {
                return null;
            }
            var buffer = manager.GetBuffer<T>(entity, true);
            var entries = new List<T>(buffer.Length);
            foreach (T item in buffer)
            {
                T entry = item;
                _layout(references, ref entry);
                entries.Add(entry);
            }
            return entries;
        }

        public override object Transfer(GameSnapshotArchive archive, object value)
        {
            var entries = (List<T>)value;
            archive.List(ref entries, _layout);
            return entries;
        }

        public override void Record(EntityCommandBuffer ecb, Entity entity, object value, GameSnapshotArchive references)
        {
            var buffer = ecb.AddBuffer<T>(entity);
            foreach (T item in (List<T>)value)
            {
                T entry = item;
                _layout(references, ref entry);
                buffer.Add(entry);
            }
        }

#if UNITY_EDITOR
        public override object ReadLegacy(JToken token, JsonSerializer serializer)
        {
            if (token is not JArray entries)
            {
                throw new JsonSerializationException($"드론 버퍼에는 배열이 필요합니다. {Name}");
            }
            var values = new List<T>(entries.Count);
            foreach (JToken row in entries)
            {
                values.Add(ReadLegacyObject<T>(row, serializer));
            }
            return values;
        }
#endif
    }
}
