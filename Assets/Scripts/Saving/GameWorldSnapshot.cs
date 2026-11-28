using System;
using System.Collections.Generic;
#if UNITY_EDITOR
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
#endif
using Unity.Mathematics;

#if UNITY_EDITOR
[JsonObject(MemberSerialization.OptIn)]
#endif
internal sealed class GameWorldSnapshot
{
#if UNITY_EDITOR
    [JsonProperty("world", Required = Required.Always)]
#endif
    public SavedWorld World = new();
#if UNITY_EDITOR
    [JsonProperty("buildings", Required = Required.Always)]
#endif
    public List<SavedBuilding> Buildings = new();
#if UNITY_EDITOR
    [JsonProperty("worldItems", Required = Required.Always)]
#endif
    public List<SavedWorldItem> WorldItems = new();
#if UNITY_EDITOR
    [JsonProperty("research")]
#endif
    public SavedResearch Research;
#if UNITY_EDITOR
    [JsonProperty("constructionSites")]
#endif
    public List<SavedConstructionSite> ConstructionSites = new();
#if UNITY_EDITOR
    [JsonProperty("passiveDroneTasks", NullValueHandling = NullValueHandling.Ignore)]
#endif
    public List<SavedPassiveDroneTask> PassiveDroneTasks;
    public SavedDroneWorld DroneLogistics;

    public bool IsLegacyFormat { get; private set; }
    internal bool AllowsLegacyBeltSpacing;
    internal bool RecordsBeltEntryDirections;

#if UNITY_EDITOR
    public JObject ToJson()
    {
        var document = JObject.FromObject(this, CreateSerializer());
        if (DroneLogistics != null)
        {
            document["droneLogistics"] = GameDroneLegacyJson.Write(DroneLogistics);
        }
        return document;
    }
    public static GameWorldSnapshot FromJson(JObject document)
    {
        foreach (JProperty property in document.Properties())
        {
            if (property.Name is not ("world" or "buildings" or "worldItems" or "research" or "constructionSites" or "passiveDroneTasks" or "droneLogistics" or
                "formatVersion" or "contentVersion" or "worldGeneratorVersion" or "savedAtUtc"))
            {
                throw new NotSupportedException($"지원하지 않는 저장 영역입니다. {property.Name}");
            }
        }
        bool legacy = JToken.DeepEquals(document["formatVersion"], new JValue(LegacyGameSaveFile.InitialVersion));
        bool workOnly = JToken.DeepEquals(document["formatVersion"], new JValue(LegacyGameSaveFile.WorkVersion));
        if ((legacy || workOnly) && document.ContainsKey("droneLogistics"))
        {
            throw new NotSupportedException("해당 형식에서 지원하지 않는 드론 저장 영역이 있습니다.");
        }
        if (legacy && (document.ContainsKey("research") || document.ContainsKey("constructionSites") ||
            document.ContainsKey("passiveDroneTasks")))
        {
            throw new NotSupportedException("형식 1에서 지원하지 않는 작업 영역이 있습니다.");
        }
        if (!legacy)
        {
            if (document["research"] is not JObject || document["constructionSites"] is not JArray ||
                (workOnly ? document["passiveDroneTasks"] is not JArray : document["droneLogistics"] is not JObject))
            {
                throw new JsonSerializationException("연구, 공사 현장 또는 자동 작업의 저장 영역이 없습니다.");
            }
            if (!workOnly && document.ContainsKey("passiveDroneTasks"))
            {
                throw new JsonSerializationException("형식 3에서는 드론 작업을 한 영역에서 복원해야 합니다.");
            }
            if (document["buildings"] is JArray buildings)
            {
                foreach (JToken building in buildings)
                {
                    if (building is not JObject entry || !entry.ContainsKey("production"))
                    {
                        throw new JsonSerializationException("건물의 작업 상태 저장 영역이 없습니다.");
                    }
                }
            }
        }
        var snapshot = document.ToObject<GameWorldSnapshot>(CreateSerializer());
        snapshot.IsLegacyFormat = legacy;
        snapshot.AllowsLegacyBeltSpacing = true;
        if (!legacy && !workOnly)
        {
            snapshot.DroneLogistics = GameDroneLegacyJson.Read((JObject)document["droneLogistics"]);
        }
        // 지원 형식을 확인한 뒤 배열의 빈 원소를 손상으로 분류한다. 선택 속성의 null은 유효하다.
        foreach (JToken token in document.Descendants())
        {
            if (token.Parent is JArray && token.Type is JTokenType.Null or JTokenType.Undefined)
            {
                throw new JsonSerializationException($"저장 배열의 항목이 비어 있습니다. {token.Path}");
            }
        }
        return snapshot;
    }

#endif

    internal static void ValidateBeltSpacing(SavedBeltPathCells beltCells, List<SavedWorldItem> items,
        Dictionary<SavedWorldItem, float> restoredProgress = null, bool repairLegacySpacing = true,
        bool useSavedEntryDirections = false)
    {
        var belts = beltCells.Belts;
        var progresses = new Dictionary<int2, List<SavedWorldItem>>();
        foreach (var item in items)
        {
            if (item == null)
            {
                throw new System.IO.InvalidDataException("월드 아이템 기록이 비어 있습니다.");
            }
            if (!item.BeltProgress.HasValue)
            {
                continue;
            }
            int2 cell = ReadCell(item.Cell);
            float progress = item.BeltProgress.Value;
            if (!belts.TryGetValue(cell, out var exit) || !float.IsFinite(progress) || progress < 0f || progress > 1f)
            {
                throw new System.IO.InvalidDataException("벨트 아이템의 위치나 진행도가 유효하지 않습니다.");
            }
            if (useSavedEntryDirections && progress < 0.5f && item.Direction.HasValue &&
                BeltPathUtility.IsHeadOn(item.Direction.Value, exit))
            {
                throw new System.IO.InvalidDataException("벨트 아이템의 진입 방향이 유효하지 않습니다.");
            }
            if (!progresses.TryGetValue(cell, out var entries))
            {
                entries = new List<SavedWorldItem>(GameConstants.MaxItemsPerBeltTile);
                progresses.Add(cell, entries);
            }
            entries.Add(item);
        }
        float minimumGap = GameConstants.ItemSpacing - GameConstants.AlignmentEpsilon;
        foreach (var entry in progresses)
        {
            var sorted = entry.Value;
            sorted.Sort((a, b) => a.BeltProgress.Value.CompareTo(b.BeltProgress.Value));
            if (sorted.Count > GameConstants.MaxItemsPerBeltTile)
            {
                throw new System.IO.InvalidDataException("벨트 한 칸의 아이템 수가 한도를 넘었습니다.");
            }
            for (int index = 1; index < sorted.Count; index++)
            {
                if (sorted[index].BeltProgress.Value - sorted[index - 1].BeltProgress.Value < minimumGap)
                {
                    throw new System.IO.InvalidDataException("같은 벨트 칸의 아이템 간격이 유효하지 않습니다.");
                }
            }
        }
        var remaining = new Queue<int2>(progresses.Keys);
        while (remaining.Count > 0)
        {
            int2 origin = remaining.Dequeue();
            DirectionEnum direction = belts[origin];
            int2 next = origin + direction.ToInt2();
            if (!progresses.TryGetValue(next, out var ahead) ||
                BeltPathUtility.GetEntryDirection(next, belts[next], beltCells) != direction)
            {
                continue;
            }
            var behind = progresses[origin];
            float exit = Progress(behind[behind.Count - 1]);
            int firstInLane = ahead.FindIndex(item => !useSavedEntryDirections || !item.Direction.HasValue ||
                Progress(item) >= 0.5f || item.Direction.Value == direction);
            if (firstInLane < 0 || BeltEntryUtility.HasBoundarySpacing(exit, Progress(ahead[firstInLane])))
            {
                continue;
            }
            if (restoredProgress == null || !repairLegacySpacing)
            {
                throw new System.IO.InvalidDataException("인접 벨트 칸의 아이템 간격이 유효하지 않습니다.");
            }
            // 구형 방향 변경은 새 경계의 간격을 확인하지 않았다. 입구 한 칸분의 간격을 비우면
            // 이 셀은 다른 유입 경계에서도 재보정할 필요가 없다. 셀과 아이템 순서는 유지한다.
            for (int i = 0; i < ahead.Count; i++)
            {
                float target = (i + 1) * GameConstants.ItemSpacing;
                if (Progress(ahead[i]) < target)
                {
                    restoredProgress[ahead[i]] = target;
                }
            }
            remaining.Enqueue(next);
        }

        if (restoredProgress != null)
        {
            foreach (var (cell, itemsOnBelt) in progresses)
            {
                DirectionEnum direction = belts[cell];
                int2 opposite = cell + direction.ToInt2();
                if (!progresses.TryGetValue(opposite, out var opposingItems) ||
                    !BeltPathUtility.IsHeadOn(direction, belts[opposite]) ||
                    BeltPathUtility.HasHeadOnSpacing(Progress(itemsOnBelt[itemsOnBelt.Count - 1]),
                        Progress(opposingItems[opposingItems.Count - 1])))
                {
                    continue;
                }

                // 오래된 저장에서 양쪽 끝이 겹치면 두 선두를 정지선 뒤로 당긴다.
                // 상류의 실물도 셀과 순서를 유지하면서 좁아진 간격만 되돌린다.
                RetreatQueue(cell);
                RetreatQueue(opposite);
            }
        }

        void RetreatQueue(int2 current)
        {
            float limit = BeltPathUtility.HeadOnStopProgress;
            while (progresses.TryGetValue(current, out var queue) && Progress(queue[queue.Count - 1]) > limit)
            {
                for (int i = queue.Count - 1; i >= 0; i--)
                {
                    var item = queue[i];
                    float progress = Progress(item);
                    if (progress > limit)
                    {
                        restoredProgress[item] = limit;
                        progress = limit;
                    }
                    limit = progress - minimumGap;
                }
                DirectionEnum entry = BeltPathUtility.GetEntryDirection(current, belts[current], beltCells);
                current -= entry.ToInt2();
                if (!beltCells.PointsToward(current, entry))
                {
                    break;
                }
                limit = 1f - minimumGap + Progress(queue[0]);
            }
        }

        float Progress(SavedWorldItem item)
        {
            if (restoredProgress != null && restoredProgress.TryGetValue(item, out float corrected))
            {
                return corrected;
            }
            return item.BeltProgress.Value;
        }
    }

    public static int[] WriteCell(int2 cell) => new[] { cell.x, cell.y };

    public static int2 ReadCell(int[] coordinates)
    {
        if (coordinates == null)
        {
            throw new System.IO.InvalidDataException("저장 좌표가 없습니다.");
        }
        if (coordinates.Length != 2)
        {
            throw new System.IO.InvalidDataException("저장 좌표는 두 축이 필요합니다.");
        }
        return new int2(coordinates[0], coordinates[1]);
    }

#if UNITY_EDITOR
    private static JsonSerializer CreateSerializer()
    {
        var serializer = new JsonSerializer();
        serializer.Converters.Add(new StringEnumConverter { AllowIntegerValues = false });
        return serializer;
    }
#endif
}

#if UNITY_EDITOR
[JsonObject(MemberSerialization.OptIn)]
#endif
internal sealed class SavedWorld
{
#if UNITY_EDITOR
    [JsonProperty("seed", Required = Required.Always)]
#endif
    public uint Seed;
#if UNITY_EDITOR
    [JsonProperty("generatedChunks", Required = Required.Always)]
#endif
    public List<int[]> GeneratedChunks = new();
#if UNITY_EDITOR
    [JsonProperty("pendingChunks", Required = Required.Always)]
#endif
    public List<int[]> PendingChunks = new();
#if UNITY_EDITOR
    [JsonProperty("resourceNodes", Required = Required.Always)]
#endif
    public List<SavedResourceNode> ResourceNodes = new();
#if UNITY_EDITOR
    [JsonProperty("mainFacilityId", Required = Required.Always)]
#endif
    public string MainFacilityId;
#if UNITY_EDITOR
    [JsonProperty("placementTick", Required = Required.Always)]
#endif
    public ulong PlacementTick;
#if UNITY_EDITOR
    [JsonProperty("nextPlacementReceipt", Required = Required.Always)]
#endif
    public ulong NextPlacementReceipt;
#if UNITY_EDITOR
    [JsonProperty("nextDroneTaskSequence", Required = Required.Always)]
#endif
    public ulong NextDroneTaskSequence;
}

#if UNITY_EDITOR
[JsonObject(MemberSerialization.OptIn)]
#endif
internal sealed class SavedResourceNode
{
#if UNITY_EDITOR
    [JsonProperty("cell", Required = Required.Always)]
#endif
    public int[] Cell;
#if UNITY_EDITOR
    [JsonProperty("itemId", Required = Required.Always)]
#endif
    public int ItemId;
#if UNITY_EDITOR
    [JsonProperty("remainingAmount", Required = Required.Always)]
#endif
    public int RemainingAmount;
}

#if UNITY_EDITOR
[JsonObject(MemberSerialization.OptIn)]
#endif
internal sealed class SavedBuilding
{
#if UNITY_EDITOR
    [JsonProperty("id", Required = Required.Always)]
#endif
    public string Id;
#if UNITY_EDITOR
    [JsonProperty("buildingTypeId", Required = Required.Always)]
#endif
    public int BuildingTypeId;
#if UNITY_EDITOR
    [JsonProperty("origin", Required = Required.Always)]
#endif
    public int[] Origin;
#if UNITY_EDITOR
    [JsonProperty("direction", Required = Required.Always)]
#endif
    public DirectionEnum Direction;
#if UNITY_EDITOR
    [JsonProperty("placementStamp", Required = Required.Always)]
#endif
    public SavedPlacementStamp Stamp;
#if UNITY_EDITOR
    [JsonProperty("disabled", Required = Required.Always)]
#endif
    public bool Disabled;
#if UNITY_EDITOR
    [JsonProperty("storage", Required = Required.AllowNull)]
#endif
    public SavedStorage Storage;
#if UNITY_EDITOR
    [JsonProperty("routing", Required = Required.AllowNull)]
#endif
    public SavedRouting Routing;
#if UNITY_EDITOR
    [JsonProperty("production")]
#endif
    public SavedProduction Production;
}

#if UNITY_EDITOR
[JsonObject(MemberSerialization.OptIn)]
#endif
internal sealed class SavedPlacementStamp
{
#if UNITY_EDITOR
    [JsonProperty("tick", Required = Required.Always)]
#endif
    public ulong Tick;
#if UNITY_EDITOR
    [JsonProperty("receipt", Required = Required.Always)]
#endif
    public ulong Receipt;
#if UNITY_EDITOR
    [JsonProperty("order", Required = Required.Always)]
#endif
    public uint Order;
}

#if UNITY_EDITOR
[JsonObject(MemberSerialization.OptIn)]
#endif
internal class SavedStorageFilter
{
#if UNITY_EDITOR
    [JsonProperty("filterMode", Required = Required.Always)]
#endif
    public StorageFilterMode FilterMode;
#if UNITY_EDITOR
    [JsonProperty("filterItemIds", Required = Required.Always)]
#endif
    public List<int> FilterItemIds = new();
}

#if UNITY_EDITOR
[JsonObject(MemberSerialization.OptIn)]
#endif
internal sealed class SavedStorage : SavedStorageFilter
{
#if UNITY_EDITOR
    [JsonProperty("slots", Required = Required.Always)]
#endif
    public List<SavedStorageSlot> Slots = new();
}

#if UNITY_EDITOR
[JsonObject(MemberSerialization.OptIn)]
#endif
internal sealed class SavedStorageSlot
{
#if UNITY_EDITOR
    [JsonProperty("slot", Required = Required.Always)]
#endif
    public int Slot;
#if UNITY_EDITOR
    [JsonProperty("itemId", Required = Required.Always)]
#endif
    public int ItemId;
#if UNITY_EDITOR
    [JsonProperty("quantity", Required = Required.Always)]
#endif
    public int Quantity;
}

#if UNITY_EDITOR
[JsonObject(MemberSerialization.OptIn)]
#endif
internal sealed class SavedRouting
{
#if UNITY_EDITOR
    [JsonProperty("beltId", Required = Required.AllowNull)]
#endif
    public string BeltId;
#if UNITY_EDITOR
    [JsonProperty("forwardDirection", Required = Required.Always)]
#endif
    public DirectionEnum ForwardDirection;
#if UNITY_EDITOR
    [JsonProperty("cursor", Required = Required.Always)]
#endif
    public byte Cursor;
}

#if UNITY_EDITOR
[JsonObject(MemberSerialization.OptIn)]
#endif
internal sealed class SavedWorldItem
{
#if UNITY_EDITOR
    [JsonProperty("id", Required = Required.Always)]
#endif
    public string Id;
#if UNITY_EDITOR
    [JsonProperty("cell", Required = Required.Always)]
#endif
    public int[] Cell;
#if UNITY_EDITOR
    [JsonProperty("itemId", Required = Required.Always)]
#endif
    public int ItemId;
#if UNITY_EDITOR
    [JsonProperty("beltProgress", Required = Required.AllowNull)]
#endif
    public float? BeltProgress;
#if UNITY_EDITOR
    [JsonProperty("direction", Required = Required.AllowNull)]
#endif
    public DirectionEnum? Direction;
}
