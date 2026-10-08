using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace PlanetMiner.Config
{
    /// <summary>레시피 JSON의 최상위 목록 DTO. 파싱용 managed 입력이며 ECS 레지스트리 원본이 아니다.</summary>
    [Serializable]
    public class RecipeConfigJsonData
    {
        public List<RecipeJsonEntry> recipes = new List<RecipeJsonEntry>();
    }

    /// <summary>레시피 ID·주생산품·조건·재료·부산물의 JSON 입력. 출력/재료는 게시 전 평탄한 목록 범위로 변환한다.</summary>
    [Serializable]
    public class RecipeJsonEntry
    {
        public int id;
        public string outputItemType;
        public int outputAmount = 1;
        public float craftTime = 1.0f;
        public List<string> conditions = new List<string>();
        public List<RecipeIngredientJsonEntry> ingredients = new List<RecipeIngredientJsonEntry>();
        public List<RecipeOutputJsonEntry> byproducts = new List<RecipeOutputJsonEntry>();
    }

    /// <summary>품목 문자열과 재료 요구량의 파싱 입력. long으로 읽은 뒤 런타임 int 범위를 검증한다.</summary>
    [Serializable]
    public class RecipeIngredientJsonEntry
    {
        public string itemType;
        public long amount = 1;
    }

    /// <summary>품목 문자열과 부산물 수량의 파싱 입력. 생산 결과/실물 생성 요청과 구분한다.</summary>
    [Serializable]
    public class RecipeOutputJsonEntry
    {
        public string itemType;
        public int amount = 1;
    }

    /// <summary>
    /// 게시 전 파싱 결과의 레시피/재료/출력 목록. RecipeConfigLoader가 만들고 PublishConfig가 ECS 버퍼로 복사한다.
    /// managed 입력이며 별도 unmanaged 메모리나 ECS 엔티티에 부착되는 영속 상태가 아니다.
    /// </summary>
    public sealed class RecipeConfigData
    {
        public readonly List<RecipeConfigElement> Recipes = new List<RecipeConfigElement>();
        public readonly List<RecipeIngredientElement> Ingredients = new List<RecipeIngredientElement>();
        public readonly List<RecipeOutputElement> Outputs = new List<RecipeOutputElement>();
    }

    /// <summary>
    /// 역할·목적: Resources 레시피 JSON을 해석해 재료/출력 범위를 만들고 World 소유 버퍼에 한 번 게시한다.
    /// conditions는 현재 DTO 입력 필드만 있으며 비트 변환은 연결하지 않고 ConditionFlags=0으로 게시한다.
    /// 입력·출력: 정식 품목명·실제 주생산품을 요구하고 작성된 부산물도 검증한다. 전체 초기화는 필수 ID 1~5를 요구하며 부분 게시 API는 격리 구성에 사용한다.
    /// 이용: RecipeInitSystem(Initialization)과 설정 테스트가 호출한다. 런타임 Reader는 RecipeRegistry와 ECS 버퍼를 사용한다.
    /// 수명·실패: 기존 레지스트리는 교체하지 않는다. 복사 중 실패하면 이번에 만든 미완성 엔티티만 회수하고 예외를 전달한다.
    /// </summary>
    public static class RecipeConfigLoader
    {
        public const string DefaultResourcePath = "Config/CrafterRecipeConfig";

        public static RecipeConfigData LoadFromResources(string resourcePath = DefaultResourcePath)
        {
            var textAsset = Resources.Load<TextAsset>(resourcePath);
            if (textAsset == null)
            {
                throw new ArgumentException($"Recipe config asset was not found at Resources/{resourcePath}.", nameof(resourcePath));
            }

            var config = ParseJson(textAsset.text);
            ValidateRequiredRecipes(config);
            return config;
        }

        public static RecipeConfigData ParseJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("Recipe JSON is null or empty.", nameof(json));
            }

            var jsonData = JsonUtility.FromJson<RecipeConfigJsonData>(json);
            if (jsonData == null)
            {
                throw new ArgumentException("Parsed recipe data is null.", nameof(json));
            }

            if (jsonData.recipes == null)
            {
                throw new ArgumentException("Recipe list is null.", nameof(json));
            }
            if (jsonData.recipes.Count == 0)
            {
                throw new ArgumentException("Recipe list is empty.", nameof(json));
            }

            var config = new RecipeConfigData();
            foreach (var entry in jsonData.recipes)
            {
                if (entry == null)
                {
                    throw new ArgumentException("Recipe list contains a null entry.", nameof(json));
                }
                var recipe = new RecipeConfigElement
                {
                    Id = entry.id,
                    CraftTime = entry.craftTime > 0 ? entry.craftTime : 1.0f,
                    ConditionFlags = 0,
                    IngredientStart = config.Ingredients.Count,
                    IngredientCount = entry.ingredients != null ? entry.ingredients.Count : 0,
                    OutputStart = config.Outputs.Count,
                    OutputCount = 1 + (entry.byproducts != null ? entry.byproducts.Count : 0)
                };

                if (entry.ingredients != null)
                {
                    foreach (var ingredient in entry.ingredients)
                    {
                        if (ingredient == null)
                        {
                            throw new ArgumentException($"Recipe {entry.id} contains a null ingredient.", nameof(json));
                        }
                        ItemTypeEnum itemType = ParseItemType(ingredient.itemType, entry.id, "ingredient");
                        if (ingredient.amount < int.MinValue || ingredient.amount > int.MaxValue)
                        {
                            throw new ArgumentException($"Recipe {entry.id} ingredient '{itemType}' amount {ingredient.amount} is outside the Int32 range.", nameof(json));
                        }

                        config.Ingredients.Add(new RecipeIngredientElement(itemType, ingredient.amount > 0 ? (int)ingredient.amount : 1));
                    }
                }

                // 각 레시피의 출력 슬롯 0은 주생산품이며, 이후 슬롯은 JSON 순서의 부산물이다.
                ItemTypeEnum primaryType = ParseItemType(entry.outputItemType, entry.id, "primary output");
                config.Outputs.Add(new RecipeOutputElement(primaryType, entry.outputAmount > 0 ? entry.outputAmount : 1));
                if (entry.byproducts != null)
                {
                    foreach (var output in entry.byproducts)
                    {
                        if (output == null)
                        {
                            throw new ArgumentException($"Recipe {entry.id} contains a null byproduct.", nameof(json));
                        }
                        ItemTypeEnum itemType = ParseItemType(output.itemType, entry.id, "byproduct");
                        config.Outputs.Add(new RecipeOutputElement(itemType, output.amount > 0 ? output.amount : 1, true));
                    }
                }

                config.Recipes.Add(recipe);
            }

            ValidateConfig(config);
            return config;
        }

        private static ItemTypeEnum ParseItemType(string value, int recipeId, string role)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"Recipe {recipeId} has an empty {role} item name.", "json");
            }
            if (!Enum.TryParse(value, true, out ItemTypeEnum itemType) ||
                !Enum.IsDefined(typeof(ItemTypeEnum), itemType) ||
                !string.Equals(value.Trim(), itemType.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Recipe {recipeId} has an unknown {role} item name '{value}'.", "json");
            }

            ValidateItemType(itemType, $"Recipe {recipeId} {role}");
            return itemType;
        }

        /// <summary>사전 등록된 원본 버퍼도 게시 입력과 같은 품목·출력 계약으로 검사한다. 수정하거나 복사하지 않는다.</summary>
        public static void ValidateRegisteredConfig(
            DynamicBuffer<RecipeConfigElement> recipes,
            DynamicBuffer<RecipeIngredientElement> ingredients,
            DynamicBuffer<RecipeOutputElement> outputs)
        {
            var recipeIds = new HashSet<int>();
            for (int i = 0; i < recipes.Length; i++)
            {
                var recipe = recipes[i];
                ValidateRecipeId(recipe.Id, recipeIds);
                ValidateRecipeRange(recipe, ingredients.Length, outputs.Length);
                var ingredientTypes = new HashSet<ItemTypeEnum>();
                for (int ingredientIndex = 0; ingredientIndex < recipe.IngredientCount; ingredientIndex++)
                {
                    ValidateIngredientUniqueness(recipe.Id,
                        ingredients[recipe.IngredientStart + ingredientIndex].ItemType, ingredientTypes);
                }
                for (int outputIndex = 0; outputIndex < recipe.OutputCount; outputIndex++)
                {
                    ValidateOutputRole(recipe.Id, outputs[recipe.OutputStart + outputIndex], outputIndex);
                }
            }

            for (int i = 0; i < ingredients.Length; i++)
            {
                ValidateItemType(ingredients[i].ItemType, $"Ingredient at index {i}");
            }
            for (int i = 0; i < outputs.Length; i++)
            {
                ValidateOutput(outputs[i], i);
            }
        }

        /// <summary>전체 제품 초기화에서 사용하는 필수 목록이다. 잠긴 레시피도 데이터 자체는 시작 시 존재해야 한다.</summary>
        public static void ValidateRequiredRecipes(RecipeConfigData config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            var ids = new HashSet<int>();
            foreach (var recipe in config.Recipes)
            {
                ids.Add(recipe.Id);
            }
            ValidateRequiredRecipeIds(ids);
        }

        public static void ValidateRequiredRecipes(in DynamicBuffer<RecipeConfigElement> recipes)
        {
            var ids = new HashSet<int>();
            foreach (var recipe in recipes)
            {
                ids.Add(recipe.Id);
            }
            ValidateRequiredRecipeIds(ids);
        }

        private static void ValidateRequiredRecipeIds(HashSet<int> ids)
        {
            var missing = new List<int>();
            for (int id = 1; id <= 5; id++)
            {
                if (!ids.Contains(id))
                {
                    missing.Add(id);
                }
            }
            if (missing.Count > 0)
            {
                throw new ArgumentException($"Missing required recipe IDs: {string.Join(", ", missing)}.");
            }
        }

        /// <summary>명시적인 기본 데이터 생성 API다. 파일 로드 실패의 대체 경로로는 사용하지 않는다.</summary>
        public static RecipeConfigData CreateDefaultConfig()
        {
            var config = new RecipeConfigData();
            AddDefaultRecipe(config, 1, 1.0f, ItemTypeEnum.Iron,
                new RecipeIngredientElement(ItemTypeEnum.Iron_Ore, 1));
            AddDefaultRecipe(config, 2, 1.0f, ItemTypeEnum.Copper,
                new RecipeIngredientElement(ItemTypeEnum.Copper_Ore, 1));
            AddDefaultRecipe(config, 3, 1.5f, ItemTypeEnum.Iron_Stick,
                new RecipeIngredientElement(ItemTypeEnum.Iron, 2));
            AddDefaultRecipe(config, 4, 1.5f, ItemTypeEnum.Copper_Stick,
                new RecipeIngredientElement(ItemTypeEnum.Copper, 2));
            AddDefaultRecipe(config, 5, 4.0f, ItemTypeEnum.Drone,
                new RecipeIngredientElement(ItemTypeEnum.Iron_Stick, 2),
                new RecipeIngredientElement(ItemTypeEnum.Copper_Stick, 2));
            return config;
        }

        private static void AddDefaultRecipe(
            RecipeConfigData config, int id, float craftTime, ItemTypeEnum outputType,
            params RecipeIngredientElement[] ingredients)
        {
            config.Recipes.Add(new RecipeConfigElement
            {
                Id = id,
                CraftTime = craftTime,
                IngredientStart = config.Ingredients.Count,
                IngredientCount = ingredients.Length,
                OutputStart = config.Outputs.Count,
                OutputCount = 1
            });
            config.Ingredients.AddRange(ingredients);
            config.Outputs.Add(new RecipeOutputElement(outputType, 1));
        }

        /// <summary>
        /// 읽는 시스템을 실행하기 전에 한 번 게시한다. 기존 설정을 덮어쓰거나 추가 게시하지 않는다.
        /// 파싱 결과를 버퍼로 복사하므로 호출자가 목록을 보관/해제할 필요가 없다.
        /// 반환 엔티티와 버퍼는 World 종료까지 유지하며 Init 시스템 제거 시에도 해제하지 않는다.
        /// 품목·필수 주생산품·부산물 및 중복 재료를 전체 검증한 뒤에만 레지스트리를 만든다.
        /// </summary>
        public static Entity PublishConfig(EntityManager entityManager, RecipeConfigData config)
        {
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<RecipeRegistry>());
            if (!query.IsEmptyIgnoreFilter)
            {
                throw new InvalidOperationException("RecipeRegistry is already registered. Runtime replacement is not supported.");
            }

            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            // 전체 입력을 검증한 뒤에만 World 수명 레지스트리를 만든다. 후반 부산물 오류도 부분 게시하지 않는다.
            ValidateConfig(config);
            Entity registryEntity = entityManager.CreateEntity(
                typeof(RecipeRegistry), typeof(RecipeConfigElement),
                typeof(RecipeIngredientElement), typeof(RecipeOutputElement));
            try
            {
                var recipes = entityManager.GetBuffer<RecipeConfigElement>(registryEntity);
                recipes.EnsureCapacity(config.Recipes.Count);
                foreach (var recipe in config.Recipes)
                {
                    recipes.Add(recipe);
                }

                var ingredients = entityManager.GetBuffer<RecipeIngredientElement>(registryEntity);
                ingredients.EnsureCapacity(config.Ingredients.Count);
                foreach (var ingredient in config.Ingredients)
                {
                    ingredients.Add(ingredient);
                }

                var outputs = entityManager.GetBuffer<RecipeOutputElement>(registryEntity);
                outputs.EnsureCapacity(config.Outputs.Count);
                foreach (var output in config.Outputs)
                {
                    outputs.Add(output);
                }

                return registryEntity;
            }
            catch
            {
                // 이번 게시에서 만든 미완성 엔티티만 회수한다.
                entityManager.DestroyEntity(registryEntity);
                throw;
            }
        }

        private static void ValidateConfig(RecipeConfigData config)
        {
            var recipeIds = new HashSet<int>();
            foreach (var recipe in config.Recipes)
            {
                ValidateRecipeId(recipe.Id, recipeIds);
                ValidateRecipeRange(recipe, config.Ingredients.Count, config.Outputs.Count);
                var ingredientTypes = new HashSet<ItemTypeEnum>();
                for (int i = 0; i < recipe.IngredientCount; i++)
                {
                    ValidateIngredientUniqueness(recipe.Id,
                        config.Ingredients[recipe.IngredientStart + i].ItemType, ingredientTypes);
                }
                for (int i = 0; i < recipe.OutputCount; i++)
                {
                    ValidateOutputRole(recipe.Id, config.Outputs[recipe.OutputStart + i], i);
                }
            }

            // 참조 범위 밖의 행도 같은 버퍼로 복사하므로 모든 행의 품목을 검증한다.
            for (int i = 0; i < config.Ingredients.Count; i++)
            {
                ValidateItemType(config.Ingredients[i].ItemType, $"Ingredient at index {i}");
            }
            for (int i = 0; i < config.Outputs.Count; i++)
            {
                ValidateOutput(config.Outputs[i], i);
            }
        }

        private static void ValidateRecipeId(int recipeId, HashSet<int> recipeIds)
        {
            if (recipeId <= 0)
            {
                throw new ArgumentException($"Recipe ID {recipeId} must be explicitly set to a positive integer.", "config");
            }
            if (!recipeIds.Add(recipeId))
            {
                throw new ArgumentException($"Duplicate recipe ID {recipeId}.", "config");
            }
        }

        private static void ValidateRecipeRange(RecipeConfigElement recipe, int ingredientCount, int outputCount)
        {
            if (recipe.IngredientStart < 0 || recipe.IngredientCount < 0 ||
                (long)recipe.IngredientStart + recipe.IngredientCount > ingredientCount)
            {
                throw new ArgumentException("Recipe ingredient range is outside the supplied buffer.", "config");
            }
            if (recipe.OutputStart < 0 || recipe.OutputCount < 0 ||
                (long)recipe.OutputStart + recipe.OutputCount > outputCount)
            {
                throw new ArgumentException("Recipe output range is outside the supplied buffer.", "config");
            }
            if (recipe.OutputCount == 0)
            {
                throw new ArgumentException($"Recipe {recipe.Id} requires a primary output.", "config");
            }
        }

        private static void ValidateIngredientUniqueness(int recipeId, ItemTypeEnum itemType, HashSet<ItemTypeEnum> ingredientTypes)
        {
            if (!ingredientTypes.Add(itemType))
            {
                throw new ArgumentException($"Recipe {recipeId} contains duplicate ingredient '{itemType}'.", "config");
            }
        }

        private static void ValidateItemType(ItemTypeEnum itemType, string role)
        {
            if (itemType == ItemTypeEnum.None)
            {
                throw new ArgumentException($"{role} cannot use None as an item.", "config");
            }
            if (!Enum.IsDefined(typeof(ItemTypeEnum), itemType))
            {
                throw new ArgumentException($"{role} has an undefined item type '{(int)itemType}'.", "config");
            }
        }

        private static void ValidateOutput(RecipeOutputElement output, int outputIndex)
        {
            ValidateItemType(output.ItemType, $"Output at index {outputIndex}");
            if (output.Amount <= 0)
            {
                throw new ArgumentException($"Output at index {outputIndex} requires a positive amount.", "config");
            }
        }

        private static void ValidateOutputRole(int recipeId, RecipeOutputElement output, int outputIndex)
        {
            // 슬롯 0은 주생산품, 후속 슬롯은 선택적인 부산물이다. None 행으로 빈 슬롯을 표현하지 않는다.
            if (output.IsByproduct != (outputIndex > 0))
            {
                throw new ArgumentException($"Recipe {recipeId} requires a primary output in slot 0 and byproducts in later slots.", "config");
            }
        }
    }
}
