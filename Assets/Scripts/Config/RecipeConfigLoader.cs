using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace PlanetMiner.Config
{
    [Serializable]
    public class RecipeConfigJsonData
    {
        public List<RecipeJsonEntry> recipes = new List<RecipeJsonEntry>();
    }

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

    [Serializable]
    public class RecipeIngredientJsonEntry
    {
        public string itemType;
        public int amount = 1;
    }

    [Serializable]
    public class RecipeOutputJsonEntry
    {
        public string itemType;
        public int amount = 1;
    }

    /// <summary>게시 전 파싱 결과. 일반 C# 목록이며 별도의 unmanaged 메모리를 소유하지 않는다.</summary>
    public sealed class RecipeConfigData
    {
        public readonly List<RecipeConfigElement> Recipes = new List<RecipeConfigElement>();
        public readonly List<RecipeIngredientElement> Ingredients = new List<RecipeIngredientElement>();
        public readonly List<RecipeOutputElement> Outputs = new List<RecipeOutputElement>();
    }

    /// <summary>기존 JSON 해석과 기본값을 유지하며 레시피를 World 소유 버퍼로 한 번 게시한다.</summary>
    public static class RecipeConfigLoader
    {
        public const string DefaultResourcePath = "Config/CrafterRecipeConfig";

        public static RecipeConfigData LoadFromResources(string resourcePath = DefaultResourcePath)
        {
            var textAsset = Resources.Load<TextAsset>(resourcePath);
            if (textAsset != null)
            {
                if (!string.IsNullOrEmpty(textAsset.text))
                {
                    return ParseJson(textAsset.text);
                }
            }

            Debug.LogWarning($"[RecipeConfigLoader] Failed to load recipe JSON at '{resourcePath}'. Falling back to default hardcoded recipes.");
            return CreateDefaultConfig();
        }

        public static RecipeConfigData ParseJson(string json)
        {
            var jsonData = JsonUtility.FromJson<RecipeConfigJsonData>(json);
            if (jsonData == null)
            {
                return CreateDefaultConfig();
            }

            if (jsonData.recipes == null)
            {
                return CreateDefaultConfig();
            }

            if (jsonData.recipes.Count == 0)
            {
                return CreateDefaultConfig();
            }

            var config = new RecipeConfigData();
            foreach (var entry in jsonData.recipes)
            {
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
                        Enum.TryParse(ingredient.itemType, true, out ItemTypeEnum itemType);
                        config.Ingredients.Add(new RecipeIngredientElement(itemType, ingredient.amount > 0 ? ingredient.amount : 1));
                    }
                }

                // 각 레시피의 출력 슬롯 0은 주생산품이며, 이후 슬롯은 JSON 순서의 부산물이다.
                Enum.TryParse(entry.outputItemType, true, out ItemTypeEnum primaryType);
                config.Outputs.Add(new RecipeOutputElement(primaryType, entry.outputAmount > 0 ? entry.outputAmount : 1));
                if (entry.byproducts != null)
                {
                    foreach (var output in entry.byproducts)
                    {
                        Enum.TryParse(output.itemType, true, out ItemTypeEnum itemType);
                        config.Outputs.Add(new RecipeOutputElement(itemType, output.amount > 0 ? output.amount : 1, true));
                    }
                }

                config.Recipes.Add(recipe);
            }

            return config;
        }

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

            ValidateRanges(config);
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

        private static void ValidateRanges(RecipeConfigData config)
        {
            foreach (var recipe in config.Recipes)
            {
                if (recipe.IngredientStart < 0 || recipe.IngredientCount < 0 ||
                    (long)recipe.IngredientStart + recipe.IngredientCount > config.Ingredients.Count)
                {
                    throw new ArgumentException("Recipe ingredient range is outside the supplied buffer.", nameof(config));
                }

                if (recipe.OutputStart < 0 || recipe.OutputCount < 0 ||
                    (long)recipe.OutputStart + recipe.OutputCount > config.Outputs.Count)
                {
                    throw new ArgumentException("Recipe output range is outside the supplied buffer.", nameof(config));
                }
            }
        }
    }
}
