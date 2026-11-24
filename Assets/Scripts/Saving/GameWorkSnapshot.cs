using System.Collections.Generic;
#if UNITY_EDITOR
using Newtonsoft.Json;
#endif

#if UNITY_EDITOR
[JsonObject(MemberSerialization.OptIn)]
#endif
internal sealed class SavedProduction
{
#if UNITY_EDITOR
    [JsonProperty("miningProgress", Required = Required.AllowNull)]
#endif
    public float? MiningProgress;
#if UNITY_EDITOR
    [JsonProperty("crafting", Required = Required.AllowNull)]
#endif
    public SavedCrafting Crafting;
#if UNITY_EDITOR
    [JsonProperty("remainingFuelEnergy", Required = Required.AllowNull)]
#endif
    public float? RemainingFuelEnergy;
#if UNITY_EDITOR
    [JsonProperty("activeFuelCapacity", NullValueHandling = NullValueHandling.Ignore)]
#endif
    public float? ActiveFuelCapacity;
#if UNITY_EDITOR
    [JsonProperty("researchCycle", Required = Required.AllowNull)]
#endif
    public SavedResearchCycle ResearchCycle;
#if UNITY_EDITOR
    [JsonProperty("products", Required = Required.AllowNull)]
#endif
    public List<SavedStorageSlot> Products;
}

#if UNITY_EDITOR
[JsonObject(MemberSerialization.OptIn)]
#endif
internal sealed class SavedCrafting
{
#if UNITY_EDITOR
    [JsonProperty("recipeId", Required = Required.Always)]
#endif
    public int RecipeId;
#if UNITY_EDITOR
    [JsonProperty("progress", Required = Required.Always)]
#endif
    public float Progress;
#if UNITY_EDITOR
    [JsonProperty("isActive", Required = Required.Always)]
#endif
    public bool IsActive;
#if UNITY_EDITOR
    [JsonProperty("status", Required = Required.Always)]
#endif
    public CrafterStatusEnum Status;
}

#if UNITY_EDITOR
[JsonObject(MemberSerialization.OptIn)]
#endif
internal sealed class SavedResearch
{
#if UNITY_EDITOR
    [JsonProperty("activeResearchId", Required = Required.Always)]
#endif
    public int ActiveResearchId;
#if UNITY_EDITOR
    [JsonProperty("progress", Required = Required.Always)]
#endif
    public List<SavedResearchProgress> Progress = new();
}

#if UNITY_EDITOR
[JsonObject(MemberSerialization.OptIn)]
#endif
internal sealed class SavedResearchProgress
{
#if UNITY_EDITOR
    [JsonProperty("researchId", Required = Required.Always)]
#endif
    public int ResearchId;
#if UNITY_EDITOR
    [JsonProperty("progress", Required = Required.Always)]
#endif
    public double Progress;
#if UNITY_EDITOR
    [JsonProperty("isCompleted", Required = Required.Always)]
#endif
    public bool IsCompleted;
}

#if UNITY_EDITOR
[JsonObject(MemberSerialization.OptIn)]
#endif
internal sealed class SavedResearchCycle
{
#if UNITY_EDITOR
    [JsonProperty("researchId", Required = Required.Always)]
#endif
    public int ResearchId;
#if UNITY_EDITOR
    [JsonProperty("elapsedTime", Required = Required.Always)]
#endif
    public float ElapsedTime;
#if UNITY_EDITOR
    [JsonProperty("inputConfigured", Required = Required.Always)]
#endif
    public bool InputConfigured;
#if UNITY_EDITOR
    [JsonProperty("recovery", Required = Required.Always)]
#endif
    public List<SavedStorageSlot> Recovery = new();
}

#if UNITY_EDITOR
[JsonObject(MemberSerialization.OptIn)]
#endif
internal sealed class SavedConstructionSite
{
#if UNITY_EDITOR
    [JsonProperty("id", Required = Required.Always)]
#endif
    public string Id;
#if UNITY_EDITOR
    [JsonProperty("targetBuildingId", Required = Required.Always)]
#endif
    public int TargetBuildingId;
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
    [JsonProperty("awaitingItemClearance", Required = Required.Always)]
#endif
    public bool AwaitingItemClearance;
#if UNITY_EDITOR
    [JsonProperty("plannedRecipeId", Required = Required.AllowNull)]
#endif
    public int? PlannedRecipeId;
#if UNITY_EDITOR
    [JsonProperty("plannedStorageFilter", Required = Required.AllowNull)]
#endif
    public SavedStorageFilter PlannedStorageFilter;
#if UNITY_EDITOR
    [JsonProperty("materials", Required = Required.Always)]
#endif
    public List<SavedConstructionMaterial> Materials = new();
}

#if UNITY_EDITOR
[JsonObject(MemberSerialization.OptIn)]
#endif
internal sealed class SavedConstructionMaterial
{
#if UNITY_EDITOR
    [JsonProperty("itemId", Required = Required.Always)]
#endif
    public int ItemId;
#if UNITY_EDITOR
    [JsonProperty("required", Required = Required.Always)]
#endif
    public int RequiredQuantity;
#if UNITY_EDITOR
    [JsonProperty("delivered", Required = Required.Always)]
#endif
    public int DeliveredQuantity;
#if UNITY_EDITOR
    [JsonProperty("reserved")]
#endif
    public int ReservedQuantity;
}

#if UNITY_EDITOR
[JsonObject(MemberSerialization.OptIn)]
#endif
internal sealed class SavedPassiveDroneTask
{
#if UNITY_EDITOR
    [JsonProperty("kind", Required = Required.Always)]
#endif
    public DroneLogisticsTaskKindEnum Kind;
#if UNITY_EDITOR
    [JsonProperty("targetId", Required = Required.Always)]
#endif
    public string TargetId;
#if UNITY_EDITOR
    [JsonProperty("itemId", Required = Required.Always)]
#endif
    public int ItemId;
#if UNITY_EDITOR
    [JsonProperty("creationSequence", Required = Required.Always)]
#endif
    public ulong CreationSequence;
#if UNITY_EDITOR
    [JsonProperty("quantity", Required = Required.Always)]
#endif
    public int Quantity;
}
