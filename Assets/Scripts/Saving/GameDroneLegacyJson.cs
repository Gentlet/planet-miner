#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Unity.Entities;

/// <summary>현행 슬롯의 형식 3 JSON과 타입 기반 드론 스냅샷 사이의 변환 경계다.</summary>
internal static class GameDroneLegacyJson
{
    private static readonly IContractResolver ComponentFields = new ComponentFieldResolver();

    public static JObject Write(SavedDroneWorld snapshot)
    {
        var serializer = CreateSerializer(new ReferenceConverter(reference => GameDroneSave.ReadReferenceId(snapshot, reference)));
        var rows = new JArray();
        foreach (var row in snapshot.Entities)
        {
            rows.Add(new JObject
            {
                ["id"] = row.Id,
                ["kind"] = row.Kind.ToString(),
                ["position"] = row.Position == null ? JValue.CreateNull() : JArray.FromObject(row.Position),
                ["disabled"] = row.Disabled,
                ["state"] = GameDroneSaveSchema.WriteLegacyState(row, serializer)
            });
        }
        return new JObject
        {
            ["entities"] = rows,
            ["nextActionReceipt"] = new JValue(snapshot.NextActionReceipt),
            ["nextStationRequest"] = new JValue(snapshot.NextStationRequest),
            ["nextTaskRequest"] = new JValue(snapshot.NextTaskRequest)
        };
    }

    public static SavedDroneWorld Read(JObject document)
    {
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var snapshot = new SavedDroneWorld();
        var serializer = CreateSerializer(new ReferenceConverter(id => GameDroneSave.IndexReference(snapshot, indices, id)));
        var wrapper = new JsonSerializer();
        wrapper.Converters.Add(new StringEnumConverter { AllowIntegerValues = false });
        var saved = document.ToObject<LegacyWorld>(wrapper);
        snapshot.NextActionReceipt = saved.NextActionReceipt;
        snapshot.NextStationRequest = saved.NextStationRequest;
        snapshot.NextTaskRequest = saved.NextTaskRequest;
        try
        {
            foreach (var entry in saved.Entities)
            {
                if (entry == null)
                {
                    throw new JsonSerializationException("드론 저장 기록이 없습니다.");
                }
                var row = new SavedDroneEntity { Id = entry.Id, Kind = entry.Kind, Position = entry.Position, Disabled = entry.Disabled };
                GameDroneSaveSchema.ReadLegacyState(row, entry.State, serializer);
                snapshot.Entities.Add(row);
            }
            GameDroneSave.ValidateFileStructure(snapshot);
        }
        catch (InvalidDataException error)
        {
            throw new JsonSerializationException(error.Message, error);
        }
        return snapshot;
    }

    [JsonObject(MemberSerialization.OptIn)]
    private sealed class LegacyWorld
    {
        [JsonProperty("entities", Required = Required.Always)] public List<LegacyEntity> Entities;
        [JsonProperty("nextActionReceipt", Required = Required.AllowNull)] public ulong? NextActionReceipt;
        [JsonProperty("nextStationRequest", Required = Required.AllowNull)] public ulong? NextStationRequest;
        [JsonProperty("nextTaskRequest", Required = Required.AllowNull)] public ulong? NextTaskRequest;
    }

    [JsonObject(MemberSerialization.OptIn)]
    private sealed class LegacyEntity
    {
        [JsonProperty("id", Required = Required.Always)] public string Id;
        [JsonProperty("kind", Required = Required.Always)] public SavedDroneKindEnum Kind;
        [JsonProperty("position", Required = Required.AllowNull)] public float[] Position;
        [JsonProperty("disabled", Required = Required.Always)] public bool Disabled;
        [JsonProperty("state", Required = Required.Always)] public JObject State;
    }

    private static JsonSerializer CreateSerializer(ReferenceConverter references)
    {
        var serializer = new JsonSerializer { ContractResolver = ComponentFields, MissingMemberHandling = MissingMemberHandling.Error };
        serializer.Converters.Add(references);
        serializer.Converters.Add(new StringEnumConverter { AllowIntegerValues = false });
        return serializer;
    }

    private sealed class ComponentFieldResolver : DefaultContractResolver
    {
        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
        {
            var fields = new List<JsonProperty>();
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var property = CreateProperty(field, MemberSerialization.Fields);
                property.Required = field.FieldType == typeof(Entity) ? Required.AllowNull : Required.Always;
                fields.Add(property);
            }
            return fields;
        }
    }

    private sealed class ReferenceConverter : JsonConverter<Entity>
    {
        private readonly Func<Entity, string> _write;
        private readonly Func<string, Entity> _read;

        public ReferenceConverter(Func<Entity, string> write)
        {
            _write = write;
        }

        public ReferenceConverter(Func<string, Entity> read)
        {
            _read = read;
        }

        public override void WriteJson(JsonWriter writer, Entity value, JsonSerializer serializer)
        {
            if (value == Entity.Null)
            {
                writer.WriteNull();
                return;
            }
            writer.WriteValue(_write(value));
        }

        public override Entity ReadJson(JsonReader reader, Type objectType, Entity existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return Entity.Null;
            }
            if (reader.TokenType != JsonToken.String || reader.Value is not string id)
            {
                throw new JsonSerializationException("드론 저장 참조에는 저장 ID가 필요합니다.");
            }
            return _read(id);
        }
    }
}
#endif
