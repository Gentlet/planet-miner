using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

/// <summary>게시 전 검증용 관리형 자료다. 게시 이후 Reader는 이 객체 대신 ECS 버퍼를 사용한다.</summary>
public sealed class ResearchConfigData
{
    public readonly List<ResearchDefinitionElement> Definitions = new List<ResearchDefinitionElement>();
    public readonly List<ResearchPrerequisiteElement> Prerequisites = new List<ResearchPrerequisiteElement>();
    public readonly List<ResearchIngredientElement> Ingredients = new List<ResearchIngredientElement>();
    public readonly List<ResearchRewardElement> Rewards = new List<ResearchRewardElement>();
    public readonly List<ResearchInitialRecipeUnlockElement> InitialRecipes = new List<ResearchInitialRecipeUnlockElement>();
}

public static class ResearchConfigLoader
{
    public const string DefaultResourcePath = "Config/ResearchConfig";

    public static ResearchConfigData LoadFromResources(string resourcePath = DefaultResourcePath)
    {
        var asset = Resources.Load<TextAsset>(resourcePath);
        if (asset == null)
        {
            throw new ArgumentException($"Research configuration resource '{resourcePath}' was not found.");
        }

        return ParseJson(asset.text);
    }

    public static ResearchConfigData ParseJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("Research JSON must not be empty.", nameof(json));
        }

        var root = JObject.Parse(json);
        if (root.Property("initiallyUnlockedBuildings") != null)
        {
            throw new ArgumentException("Building initial unlocks belong to BuildingConfig, not initiallyUnlockedBuildings.");
        }

        var data = new ResearchConfigData();
        foreach (var token in RequireArray(root, "initiallyUnlockedRecipeIds"))
        {
            data.InitialRecipes.Add(new ResearchInitialRecipeUnlockElement { RecipeId = ReadInteger(token, "initial recipe ID") });
        }

        foreach (var token in RequireArray(root, "researches"))
        {
            var entry = RequireObject(token, "research");
            var definition = new ResearchDefinitionElement
            {
                StableId = ReadId(entry, "stableId"),
                DisplayName = new FixedString128Bytes(ReadText(entry, "displayName", default(FixedString128Bytes).Capacity, false)),
                Description = new FixedString512Bytes(ReadText(entry, "description", default(FixedString512Bytes).Capacity, true)),
                CycleDuration = ReadNumber(entry, "cycleDuration"),
                ProgressPerCycle = ReadNumber(entry, "progressPerCycle"),
                RequiredProgress = ReadNumber(entry, "requiredProgress"),
                PrerequisiteStart = data.Prerequisites.Count,
                IngredientStart = data.Ingredients.Count,
                RewardStart = data.Rewards.Count
            };

            foreach (var prerequisite in RequireArray(entry, "prerequisiteIds"))
            {
                data.Prerequisites.Add(new ResearchPrerequisiteElement { ResearchId = ReadId(prerequisite, "prerequisite ID") });
            }
            foreach (var ingredient in RequireArray(entry, "ingredients"))
            {
                var ingredientEntry = RequireObject(ingredient, "ingredient");
                data.Ingredients.Add(new ResearchIngredientElement
                {
                    ItemType = ReadEnum<ItemTypeEnum>(ingredientEntry, "itemType"),
                    Amount = ReadInteger(ingredientEntry["amount"], "ingredient amount")
                });
            }
            foreach (var reward in RequireArray(entry, "rewards"))
            {
                data.Rewards.Add(ReadReward(RequireObject(reward, "reward")));
            }

            definition.PrerequisiteCount = data.Prerequisites.Count - definition.PrerequisiteStart;
            definition.IngredientCount = data.Ingredients.Count - definition.IngredientStart;
            definition.RewardCount = data.Rewards.Count - definition.RewardStart;
            data.Definitions.Add(definition);
        }

        ValidateDefinitions(data);
        return data;
    }

    public static Entity PublishConfig(EntityManager manager, ResearchConfigData data)
    {
        using var existing = manager.CreateEntityQuery(typeof(ResearchRegistry));
        if (!existing.IsEmptyIgnoreFilter)
        {
            throw new InvalidOperationException("Research configuration is already registered. Runtime replacement is not supported.");
        }

        // 공개 API의 입력은 호출자가 변경할 수 있으므로 참조까지 전체 검증한 뒤에만 구조를 변경한다.
        ValidateDefinitions(data);
        ValidateReferences(manager, data);
        Entity created = manager.CreateEntity(typeof(ResearchRegistry), typeof(ResearchState));
        try
        {
            AddBuffer(manager, created, data.Definitions);
            AddBuffer(manager, created, data.Prerequisites);
            AddBuffer(manager, created, data.Ingredients);
            AddBuffer(manager, created, data.Rewards);
            AddBuffer(manager, created, data.InitialRecipes);
            var progress = manager.AddBuffer<ResearchProgressElement>(created);
            foreach (var definition in data.Definitions)
            {
                progress.Add(new ResearchProgressElement { ResearchId = definition.StableId });
            }
            ResearchRewardUtility.InitializeFromProgress(manager, created);
            return created;
        }
        catch
        {
            // 기존 원본은 보존하고 이 호출에서 만든 미완성 게시만 회수한다.
            manager.DestroyEntity(created);
            throw;
        }
    }

    public static void ValidatePublishedConfig(EntityManager manager, Entity registered)
    {
        if (!manager.HasComponent<ResearchState>(registered))
        {
            throw new ArgumentException("ResearchRegistry requires ResearchState.");
        }

        var data = new ResearchConfigData();
        ReadBuffer(manager, registered, data.Definitions);
        ReadBuffer(manager, registered, data.Prerequisites);
        ReadBuffer(manager, registered, data.Ingredients);
        ReadBuffer(manager, registered, data.Rewards);
        ReadBuffer(manager, registered, data.InitialRecipes);
        ValidateDefinitions(data);
        ValidateReferences(manager, data);

        if (!manager.HasBuffer<ResearchProgressElement>(registered))
        {
            throw new ArgumentException("ResearchRegistry requires its progress buffer.");
        }
        var progress = manager.GetBuffer<ResearchProgressElement>(registered, true);
        ValidateRegisteredProgress(data, manager.GetComponentData<ResearchState>(registered), progress);
    }

    private static void ValidateRegisteredProgress(ResearchConfigData data, ResearchState state,
        DynamicBuffer<ResearchProgressElement> progress)
    {
        var definitions = new Dictionary<FixedString32Bytes, ResearchDefinitionElement>();
        foreach (var definition in data.Definitions)
        {
            definitions.Add(definition.StableId, definition);
        }
        var progressById = new Dictionary<FixedString32Bytes, ResearchProgressElement>();
        foreach (var entry in progress)
        {
            if (!definitions.TryGetValue(entry.ResearchId, out var definition) ||
                !progressById.TryAdd(entry.ResearchId, entry) || !IsFinite(entry.Progress) || entry.Progress < 0)
            {
                throw new ArgumentException("Research progress requires unique registered IDs and finite nonnegative progress.");
            }
            if (entry.IsCompleted != (entry.Progress >= definition.RequiredProgress))
            {
                throw new ArgumentException("Research completion must match its required progress threshold.");
            }
        }
        if (progressById.Count != definitions.Count)
        {
            throw new ArgumentException("Research progress must cover every definition.");
        }

        // 완료는 선행 완료 후에만 도달할 수 있다. 활성 연구가 없어도 이 원본 관계를 검사하며 값을 보정하지 않는다.
        foreach (var entry in progress)
        {
            if (entry.IsCompleted)
            {
                ValidateCompletedPrerequisites(data, definitions[entry.ResearchId], progressById);
            }
        }
        if (state.ActiveResearchId.IsEmpty)
        {
            return;
        }
        if (!definitions.TryGetValue(state.ActiveResearchId, out var active) || progressById[state.ActiveResearchId].IsCompleted)
        {
            throw new ArgumentException("Active research must be registered and incomplete.");
        }
        ValidateCompletedPrerequisites(data, active, progressById);
    }

    private static void ValidateCompletedPrerequisites(ResearchConfigData data, ResearchDefinitionElement definition,
        Dictionary<FixedString32Bytes, ResearchProgressElement> progressById)
    {
        for (int i = 0; i < definition.PrerequisiteCount; i++)
        {
            var prerequisite = data.Prerequisites[definition.PrerequisiteStart + i].ResearchId;
            if (!progressById[prerequisite].IsCompleted)
            {
                throw new ArgumentException($"Research '{definition.StableId}' requires every prerequisite to be completed.");
            }
        }
    }

    private static void ValidateDefinitions(ResearchConfigData data)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        var indices = new Dictionary<FixedString32Bytes, int>();
        int prerequisiteEnd = 0;
        int ingredientEnd = 0;
        int rewardEnd = 0;
        for (int i = 0; i < data.Definitions.Count; i++)
        {
            var definition = data.Definitions[i];
            string stableId = definition.StableId.ToString();
            if (string.IsNullOrWhiteSpace(stableId) || stableId != stableId.Trim() ||
                !indices.TryAdd(definition.StableId, i))
            {
                throw new ArgumentException("Research stable IDs must be nonempty and unique.");
            }
            if (string.IsNullOrWhiteSpace(definition.DisplayName.ToString()))
            {
                throw new ArgumentException($"Research '{definition.StableId}' requires a display name.");
            }
            RequirePositive(definition.CycleDuration, "cycleDuration");
            RequirePositive(definition.ProgressPerCycle, "progressPerCycle");
            RequirePositive(definition.RequiredProgress, "requiredProgress");
            prerequisiteEnd = ValidateRange(definition.PrerequisiteStart, definition.PrerequisiteCount, prerequisiteEnd, data.Prerequisites.Count);
            ingredientEnd = ValidateRange(definition.IngredientStart, definition.IngredientCount, ingredientEnd, data.Ingredients.Count);
            rewardEnd = ValidateRange(definition.RewardStart, definition.RewardCount, rewardEnd, data.Rewards.Count);
            ValidateIngredients(data, definition);
            ValidateRewards(data, definition);
        }
        if (prerequisiteEnd != data.Prerequisites.Count || ingredientEnd != data.Ingredients.Count || rewardEnd != data.Rewards.Count)
        {
            throw new ArgumentException("Research definition buffers must not contain orphaned entries.");
        }

        var recipeIds = new HashSet<int>();
        foreach (var initial in data.InitialRecipes)
        {
            if (initial.RecipeId <= 0 || !recipeIds.Add(initial.RecipeId))
            {
                throw new ArgumentException("Initial recipe IDs must be positive and unique.");
            }
        }
        ValidatePrerequisites(data, indices);
    }

    private static void ValidatePrerequisites(ResearchConfigData data, Dictionary<FixedString32Bytes, int> indices)
    {
        // 깊은 선행 관계도 관리형 호출 스택에 의존하지 않도록 진입 차수로 전체 순환을 검사한다.
        var indegrees = new int[data.Definitions.Count];
        var dependents = new List<int>[data.Definitions.Count];
        for (int i = 0; i < data.Definitions.Count; i++)
        {
            dependents[i] = new List<int>();
        }
        for (int i = 0; i < data.Definitions.Count; i++)
        {
            var definition = data.Definitions[i];
            var prerequisites = new HashSet<FixedString32Bytes>();
            for (int j = 0; j < definition.PrerequisiteCount; j++)
            {
                var prerequisite = data.Prerequisites[definition.PrerequisiteStart + j].ResearchId;
                if (!indices.TryGetValue(prerequisite, out int parent) || parent == i || !prerequisites.Add(prerequisite))
                {
                    throw new ArgumentException($"Research '{definition.StableId}' has an unknown, self or duplicate prerequisite.");
                }
                indegrees[i]++;
                dependents[parent].Add(i);
            }
        }
        var ready = new Queue<int>();
        for (int i = 0; i < indegrees.Length; i++)
        {
            if (indegrees[i] == 0)
            {
                ready.Enqueue(i);
            }
        }
        int visited = 0;
        while (ready.Count != 0)
        {
            int current = ready.Dequeue();
            visited++;
            foreach (int dependent in dependents[current])
            {
                indegrees[dependent]--;
                if (indegrees[dependent] == 0)
                {
                    ready.Enqueue(dependent);
                }
            }
        }
        if (visited != data.Definitions.Count)
        {
            throw new ArgumentException("Research prerequisites contain a cycle.");
        }
    }

    private static void ValidateIngredients(ResearchConfigData data, ResearchDefinitionElement definition)
    {
        var itemTypes = new HashSet<ItemTypeEnum>();
        for (int i = 0; i < definition.IngredientCount; i++)
        {
            var ingredient = data.Ingredients[definition.IngredientStart + i];
            if (!Enum.IsDefined(typeof(ItemTypeEnum), ingredient.ItemType) || ingredient.ItemType == ItemTypeEnum.None ||
                ingredient.Amount <= 0 || ingredient.Amount > int.MaxValue / 2 || !itemTypes.Add(ingredient.ItemType))
            {
                throw new ArgumentException($"Research '{definition.StableId}' has an invalid or duplicate ingredient or an unrepresentable two-cycle amount.");
            }
        }
    }

    private static void ValidateRewards(ResearchConfigData data, ResearchDefinitionElement definition)
    {
        var buildings = new HashSet<BuildingTypeEnum>();
        var recipes = new HashSet<int>();
        for (int i = 0; i < definition.RewardCount; i++)
        {
            var reward = data.Rewards[definition.RewardStart + i];
            switch (reward.Type)
            {
                case ResearchRewardTypeEnum.BuildingUnlock:
                    if (reward.BuildingType == BuildingTypeEnum.None || reward.BuildingType == BuildingTypeEnum.Count ||
                        reward.BuildingType == BuildingTypeEnum.ConstructionSite ||
                        !Enum.IsDefined(typeof(BuildingTypeEnum), reward.BuildingType) || !buildings.Add(reward.BuildingType) ||
                        reward.RecipeId != 0 || reward.StatKey != ResearchStatKeyEnum.None || reward.Bonus != 0)
                    {
                        throw new ArgumentException("BuildingUnlock requires one valid building target and no recipe/stat payload.");
                    }
                    break;
                case ResearchRewardTypeEnum.RecipeUnlock:
                    if (reward.RecipeId <= 0 || !recipes.Add(reward.RecipeId) || reward.BuildingType != BuildingTypeEnum.None ||
                        reward.StatKey != ResearchStatKeyEnum.None || reward.Bonus != 0)
                    {
                        throw new ArgumentException("RecipeUnlock requires one positive recipe ID and no building/stat payload.");
                    }
                    break;
                case ResearchRewardTypeEnum.StatModifier:
                    if (reward.StatKey == ResearchStatKeyEnum.None || !Enum.IsDefined(typeof(ResearchStatKeyEnum), reward.StatKey) ||
                        reward.BuildingType != BuildingTypeEnum.None || reward.RecipeId != 0)
                    {
                        throw new ArgumentException("StatModifier requires one supported stat key and no building/recipe payload.");
                    }
                    RequirePositive(reward.Bonus, "stat bonus");
                    break;
                default:
                    throw new ArgumentException("Unsupported research reward type.");
            }
        }
    }

    private static void ValidateReferences(EntityManager manager, ResearchConfigData data)
    {
        var buildings = GetRegistryBuffer<BuildingConfig, BuildingConfigElement>(manager);
        var recipes = GetRegistryBuffer<RecipeRegistry, RecipeConfigElement>(manager);
        var items = GetRegistryBuffer<ItemRegistry, ItemConfigElement>(manager);
        using var buildingState = manager.CreateEntityQuery(typeof(BuildingConfig), typeof(ResearchStatBonuses));
        if (buildingState.CalculateEntityCount() != 1)
        {
            throw new ArgumentException("Building configuration requires its research bonus state before research publication.");
        }
        var recipeIds = new HashSet<int>();
        foreach (var recipe in recipes)
        {
            recipeIds.Add(recipe.Id);
        }
        var itemTypes = new HashSet<ItemTypeEnum>();
        foreach (var item in items)
        {
            itemTypes.Add(item.ItemType);
        }
        foreach (var initial in data.InitialRecipes)
        {
            if (!recipeIds.Contains(initial.RecipeId))
            {
                throw new ArgumentException($"Initial recipe '{initial.RecipeId}' is not configured.");
            }
        }
        foreach (var ingredient in data.Ingredients)
        {
            if (!itemTypes.Contains(ingredient.ItemType))
            {
                throw new ArgumentException($"Research ingredient '{ingredient.ItemType}' is not configured.");
            }
        }

        long researchSlots = 0;
        foreach (var item in items)
        {
            int maximum = 0;
            foreach (var ingredient in data.Ingredients)
            {
                if (ingredient.ItemType == item.ItemType && ingredient.Amount > maximum)
                {
                    maximum = ingredient.Amount;
                }
            }
            if (maximum == 0)
            {
                continue;
            }
            if (item.MaxStack <= 0)
            {
                throw new ArgumentException("Research input layout requires positive configured item stack sizes.");
            }
            researchSlots += ResearchMaterialUtility.SlotCountForTwoCycles(maximum, item.MaxStack);
        }
        if (researchSlots > GameConstants.MaxStorageSlots)
        {
            throw new ArgumentException("Research input layout exceeds the supported storage slot limit.");
        }

        // 양수 보너스만 지원하므로 전체 완료 합계가 모든 중간 완료 조합의 상한이다.
        ResearchStatBonuses maximumBonuses = default;
        foreach (var reward in data.Rewards)
        {
            if (reward.Type == ResearchRewardTypeEnum.StatModifier)
            {
                ResearchRewardUtility.AddBonus(ref maximumBonuses, reward);
            }
        }
        foreach (var building in buildings)
        {
            double bonus;
            float fallback;
            switch (building.BuildingType)
            {
                case BuildingTypeEnum.Miner: bonus = maximumBonuses.MiningSpeed; fallback = 1f; break;
                case BuildingTypeEnum.Crafter: bonus = maximumBonuses.CraftingSpeed; fallback = 1f; break;
                case BuildingTypeEnum.Belt: bonus = maximumBonuses.BeltSpeed; fallback = 2f; break;
                case BuildingTypeEnum.ResearchBuilding: bonus = maximumBonuses.ResearchSpeed; fallback = 1f; break;
                default: continue;
            }
            double maximumEffectiveSpeed = (building.Speed > 0f ? building.Speed : fallback) * (1d + bonus);
            if (!IsFinite(maximumEffectiveSpeed) || maximumEffectiveSpeed > float.MaxValue)
            {
                throw new ArgumentException("Research speed bonuses exceed the supported finite speed range.");
            }
            // 실행이 사용하는 float 최종 속도로 비교하여 안전한 상한 경계의 소수 보너스를 오거부하지 않는다.
            if (building.BuildingType == BuildingTypeEnum.Belt && (float)maximumEffectiveSpeed > GameConstants.MaxBeltSpeed)
            {
                throw new ArgumentException("Research belt speed bonuses exceed the safe belt speed limit.");
            }
        }

        var definitions = new Dictionary<FixedString32Bytes, ResearchDefinitionElement>();
        foreach (var definition in data.Definitions)
        {
            definitions.Add(definition.StableId, definition);
            for (int i = 0; i < definition.RewardCount; i++)
            {
                var reward = data.Rewards[definition.RewardStart + i];
                if (reward.Type == ResearchRewardTypeEnum.RecipeUnlock && !recipeIds.Contains(reward.RecipeId))
                {
                    throw new ArgumentException($"Reward recipe '{reward.RecipeId}' is not configured.");
                }
                if (reward.Type == ResearchRewardTypeEnum.BuildingUnlock)
                {
                    ValidateBuildingReward(buildings, definition.StableId, reward.BuildingType);
                }
            }
        }
        foreach (var building in buildings)
        {
            if (building.BuildingType == BuildingTypeEnum.ResearchBuilding &&
                (!IsFinite(building.Speed) || building.Speed <= 0f))
            {
                throw new ArgumentException("Research building speed must be finite and positive.");
            }
            if (building.RequiredResearch.IsEmpty)
            {
                continue;
            }
            if (!definitions.TryGetValue(building.RequiredResearch, out var definition) || !HasBuildingReward(data, definition, building.BuildingType))
            {
                throw new ArgumentException($"Building '{building.BuildingType}' refers to missing research or a mismatched unlock reward.");
            }
        }
    }

    private static void ValidateBuildingReward(DynamicBuffer<BuildingConfigElement> buildings, FixedString32Bytes researchId, BuildingTypeEnum buildingType)
    {
        // 같은 종류라도 티어별 필요 연구는 독립적이다. 각 행의 참조는 별도의 역방향 검사에서 확인한다.
        foreach (var building in buildings)
        {
            if (building.BuildingType == buildingType && building.RequiredResearch == researchId)
            {
                return;
            }
        }
        throw new ArgumentException($"Building reward '{buildingType}' has no matching BuildingConfig.RequiredResearch.");
    }

    private static bool HasBuildingReward(ResearchConfigData data, ResearchDefinitionElement definition, BuildingTypeEnum buildingType)
    {
        for (int i = 0; i < definition.RewardCount; i++)
        {
            var reward = data.Rewards[definition.RewardStart + i];
            if (reward.Type == ResearchRewardTypeEnum.BuildingUnlock && reward.BuildingType == buildingType)
            {
                return true;
            }
        }
        return false;
    }

    private static DynamicBuffer<TElement> GetRegistryBuffer<TRegistry, TElement>(EntityManager manager)
        where TRegistry : unmanaged, IComponentData where TElement : unmanaged, IBufferElementData
    {
        using var query = manager.CreateEntityQuery(typeof(TRegistry));
        if (query.CalculateEntityCount() != 1)
        {
            throw new ArgumentException($"Research configuration requires exactly one {typeof(TRegistry).Name}.");
        }
        Entity entity = query.GetSingletonEntity();
        if (!manager.HasBuffer<TElement>(entity))
        {
            throw new ArgumentException($"{typeof(TRegistry).Name} requires {typeof(TElement).Name}.");
        }
        return manager.GetBuffer<TElement>(entity, true);
    }

    private static void AddBuffer<T>(EntityManager manager, Entity entity, List<T> entries) where T : unmanaged, IBufferElementData
    {
        var buffer = manager.AddBuffer<T>(entity);
        foreach (var entry in entries)
        {
            buffer.Add(entry);
        }
    }

    private static void ReadBuffer<T>(EntityManager manager, Entity entity, List<T> entries) where T : unmanaged, IBufferElementData
    {
        if (!manager.HasBuffer<T>(entity))
        {
            throw new ArgumentException($"ResearchRegistry requires {typeof(T).Name}.");
        }
        foreach (var entry in manager.GetBuffer<T>(entity, true))
        {
            entries.Add(entry);
        }
    }

    private static int ValidateRange(int start, int count, int expectedStart, int length)
    {
        if (start != expectedStart || count < 0 || start < 0 || (long)start + count > length)
        {
            throw new ArgumentException("Research definition ranges must be contiguous and within their buffers.");
        }
        return start + count;
    }

    private static ResearchRewardElement ReadReward(JObject entry)
    {
        var reward = new ResearchRewardElement { Type = ReadEnum<ResearchRewardTypeEnum>(entry, "type") };
        switch (reward.Type)
        {
            case ResearchRewardTypeEnum.BuildingUnlock:
                reward.BuildingType = ReadEnum<BuildingTypeEnum>(entry, "buildingType");
                break;
            case ResearchRewardTypeEnum.RecipeUnlock:
                reward.RecipeId = ReadInteger(entry["recipeId"], "reward recipe ID");
                break;
            case ResearchRewardTypeEnum.StatModifier:
                reward.StatKey = ReadEnum<ResearchStatKeyEnum>(entry, "statKey");
                reward.Bonus = ReadNumber(entry, "bonus");
                break;
            default:
                throw new ArgumentException("Unsupported research reward type.");
        }
        foreach (var property in entry.Properties())
        {
            bool allowed = property.Name == "type" ||
                (reward.Type == ResearchRewardTypeEnum.BuildingUnlock && property.Name == "buildingType") ||
                (reward.Type == ResearchRewardTypeEnum.RecipeUnlock && property.Name == "recipeId") ||
                (reward.Type == ResearchRewardTypeEnum.StatModifier && (property.Name == "statKey" || property.Name == "bonus"));
            if (!allowed)
            {
                throw new ArgumentException($"Reward field '{property.Name}' does not belong to '{reward.Type}'.");
            }
        }
        return reward;
    }

    private static JObject RequireObject(JToken token, string name)
    {
        if (!(token is JObject entry))
        {
            throw new ArgumentException($"Research {name} must be an object.");
        }
        return entry;
    }

    private static JArray RequireArray(JObject entry, string name)
    {
        if (!(entry[name] is JArray array))
        {
            throw new ArgumentException($"Research '{name}' must be an explicit array.");
        }
        return array;
    }

    private static FixedString32Bytes ReadId(JObject entry, string name)
    {
        return ReadId(entry[name], name);
    }

    private static FixedString32Bytes ReadId(JToken token, string name)
    {
        if (token == null)
        {
            throw new ArgumentException($"Research '{name}' is missing.");
        }
        if (token.Type != JTokenType.String)
        {
            throw new ArgumentException($"Research '{name}' must be a string ID.");
        }
        string value = token.Value<string>();
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || Encoding.UTF8.GetByteCount(value) > default(FixedString32Bytes).Capacity)
        {
            throw new ArgumentException($"Research '{name}' must be nonempty, unpadded and fit FixedString32Bytes.");
        }
        return new FixedString32Bytes(value);
    }

    private static string ReadText(JObject entry, string name, int capacity, bool allowEmpty)
    {
        var token = entry[name];
        if (token == null)
        {
            throw new ArgumentException($"Research '{name}' is missing.");
        }
        if (token.Type != JTokenType.String)
        {
            throw new ArgumentException($"Research '{name}' must be a string.");
        }
        string value = token.Value<string>();
        if ((!allowEmpty && string.IsNullOrWhiteSpace(value)) || Encoding.UTF8.GetByteCount(value) > capacity)
        {
            throw new ArgumentException($"Research '{name}' is empty or exceeds its UTF8 capacity.");
        }
        return value;
    }

    private static T ReadEnum<T>(JObject entry, string name) where T : struct, Enum
    {
        string text = ReadText(entry, name, 128, false).Trim();
        if (!Enum.TryParse(text, true, out T value) || !Enum.IsDefined(typeof(T), value) ||
            !string.Equals(text, value.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unsupported research '{name}': '{text}'.");
        }
        return value;
    }

    private static int ReadInteger(JToken token, string name)
    {
        if (token == null)
        {
            throw new ArgumentException($"Research '{name}' is missing.");
        }
        if (token.Type != JTokenType.Integer || !int.TryParse(token.ToString(Formatting.None), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            throw new ArgumentException($"Research '{name}' must be an Int32 integer token.");
        }
        return value;
    }

    private static float ReadNumber(JObject entry, string name)
    {
        var token = entry[name];
        if (token == null)
        {
            throw new ArgumentException($"Research '{name}' is missing.");
        }
        if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
        {
            throw new ArgumentException($"Research '{name}' must be a numeric token.");
        }
        return token.Value<float>();
    }

    private static void RequirePositive(float value, string name)
    {
        if (!IsFinite(value) || value <= 0)
        {
            throw new ArgumentException($"Research '{name}' must be finite and positive.");
        }
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
