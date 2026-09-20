using System;
using System.Collections.Generic;
using Unity.Collections;
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

    /// <summary>
    /// CrafterRecipeConfig JSON 파일 또는 프로그래밍 방식으로
    /// 불변 RecipeRegistryBlob을 생성하는 빌더 유틸리티.
    /// </summary>
    public static class RecipeConfigLoader
    {
        public const string DefaultResourcePath = "Config/CrafterRecipeConfig";

        /// <summary>
        /// Resources 경로에서 JSON을 로드하여 RecipeRegistryBlob을 빌드합니다.
        /// </summary>
        public static BlobAssetReference<RecipeRegistryBlob> LoadBlobAssetFromResources(string resourcePath = DefaultResourcePath)
        {
            var textAsset = Resources.Load<TextAsset>(resourcePath);
            if (textAsset != null && !string.IsNullOrEmpty(textAsset.text))
            {
                return BuildBlobAssetFromJson(textAsset.text);
            }

            Debug.LogWarning($"[RecipeConfigLoader] Failed to load recipe JSON at '{resourcePath}'. Falling back to default hardcoded recipes.");
            return BuildDefaultFallbackBlobAsset();
        }

        /// <summary>
        /// JSON 문자열을 파싱하여 BlobAssetReference<RecipeRegistryBlob>을 빌드합니다.
        /// </summary>
        public static BlobAssetReference<RecipeRegistryBlob> BuildBlobAssetFromJson(string json)
        {
            var jsonData = JsonUtility.FromJson<RecipeConfigJsonData>(json);
            if (jsonData == null || jsonData.recipes == null || jsonData.recipes.Count == 0)
            {
                return BuildDefaultFallbackBlobAsset();
            }

            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<RecipeRegistryBlob>();
                var recipesArray = builder.Allocate(ref root.Recipes, jsonData.recipes.Count);

                for (int i = 0; i < jsonData.recipes.Count; i++)
                {
                    var rEntry = jsonData.recipes[i];
                    recipesArray[i].Id = rEntry.id;
                    recipesArray[i].CraftTime = rEntry.craftTime > 0 ? rEntry.craftTime : 1.0f;
                    recipesArray[i].ConditionFlags = 0;

                    // 1. 재료 목록 빌드
                    int ingCount = rEntry.ingredients != null ? rEntry.ingredients.Count : 0;
                    var ingArray = builder.Allocate(ref recipesArray[i].Ingredients, ingCount);
                    for (int j = 0; j < ingCount; j++)
                    {
                        var ingEntry = rEntry.ingredients[j];
                        Enum.TryParse(ingEntry.itemType, true, out ItemTypeEnum ingType);
                        ingArray[j] = new RecipeIngredientBlob(ingType, ingEntry.amount > 0 ? ingEntry.amount : 1);
                    }

                    // 2. 출력물 목록 빌드 (주 생산품 1개 + 부산품 N개)
                    int byCount = rEntry.byproducts != null ? rEntry.byproducts.Count : 0;
                    int totalOutputs = 1 + byCount;
                    var outArray = builder.Allocate(ref recipesArray[i].Outputs, totalOutputs);

                    // 주 생산품 (Primary Output)
                    Enum.TryParse(rEntry.outputItemType, true, out ItemTypeEnum primaryType);
                    outArray[0] = new RecipeOutputBlob(primaryType, rEntry.outputAmount > 0 ? rEntry.outputAmount : 1, false);

                    // 부산품 목록 (Byproducts)
                    for (int k = 0; k < byCount; k++)
                    {
                        var byEntry = rEntry.byproducts[k];
                        Enum.TryParse(byEntry.itemType, true, out ItemTypeEnum byType);
                        outArray[1 + k] = new RecipeOutputBlob(byType, byEntry.amount > 0 ? byEntry.amount : 1, true);
                    }
                }

                return builder.CreateBlobAssetReference<RecipeRegistryBlob>(Allocator.Persistent);
            }
        }

        /// <summary>
        /// JSON 누락 시 안전하게 사용하는 기본 5종 레시피 BlobAsset.
        /// </summary>
        public static BlobAssetReference<RecipeRegistryBlob> BuildDefaultFallbackBlobAsset()
        {
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<RecipeRegistryBlob>();
                var recipesArray = builder.Allocate(ref root.Recipes, 5);

                // 레시피 1: Iron_Ore 1 -> Iron 1 (1.0s)
                recipesArray[0].Id = 1;
                recipesArray[0].CraftTime = 1.0f;
                var ing1 = builder.Allocate(ref recipesArray[0].Ingredients, 1);
                ing1[0] = new RecipeIngredientBlob(ItemTypeEnum.Iron_Ore, 1);
                var out1 = builder.Allocate(ref recipesArray[0].Outputs, 1);
                out1[0] = new RecipeOutputBlob(ItemTypeEnum.Iron, 1, false);

                // 레시피 2: Copper_Ore 1 -> Copper 1 (1.0s)
                recipesArray[1].Id = 2;
                recipesArray[1].CraftTime = 1.0f;
                var ing2 = builder.Allocate(ref recipesArray[1].Ingredients, 1);
                ing2[0] = new RecipeIngredientBlob(ItemTypeEnum.Copper_Ore, 1);
                var out2 = builder.Allocate(ref recipesArray[1].Outputs, 1);
                out2[0] = new RecipeOutputBlob(ItemTypeEnum.Copper, 1, false);

                // 레시피 3: Iron 2 -> Iron_Stick 1 (1.5s)
                recipesArray[2].Id = 3;
                recipesArray[2].CraftTime = 1.5f;
                var ing3 = builder.Allocate(ref recipesArray[2].Ingredients, 1);
                ing3[0] = new RecipeIngredientBlob(ItemTypeEnum.Iron, 2);
                var out3 = builder.Allocate(ref recipesArray[2].Outputs, 1);
                out3[0] = new RecipeOutputBlob(ItemTypeEnum.Iron_Stick, 1, false);

                // 레시피 4: Copper 2 -> Copper_Stick 1 (1.5s)
                recipesArray[3].Id = 4;
                recipesArray[3].CraftTime = 1.5f;
                var ing4 = builder.Allocate(ref recipesArray[3].Ingredients, 1);
                ing4[0] = new RecipeIngredientBlob(ItemTypeEnum.Copper, 2);
                var out4 = builder.Allocate(ref recipesArray[3].Outputs, 1);
                out4[0] = new RecipeOutputBlob(ItemTypeEnum.Copper_Stick, 1, false);

                // 레시피 5: Iron_Stick 2 + Copper_Stick 2 -> Drone 1 (4.0s)
                recipesArray[4].Id = 5;
                recipesArray[4].CraftTime = 4.0f;
                var ing5 = builder.Allocate(ref recipesArray[4].Ingredients, 2);
                ing5[0] = new RecipeIngredientBlob(ItemTypeEnum.Iron_Stick, 2);
                ing5[1] = new RecipeIngredientBlob(ItemTypeEnum.Copper_Stick, 2);
                var out5 = builder.Allocate(ref recipesArray[4].Outputs, 1);
                out5[0] = new RecipeOutputBlob(ItemTypeEnum.Drone, 1, false);

                return builder.CreateBlobAssetReference<RecipeRegistryBlob>(Allocator.Persistent);
            }
        }
    }
}
