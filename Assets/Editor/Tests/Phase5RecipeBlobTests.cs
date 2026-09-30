using NUnit.Framework;
using PlanetMiner.Config;
using Unity.Entities;

/// <summary>
/// Recipe 데이터 구조 (BlobAsset / Unmanaged Struct) 단위 테스트 슈트.
/// BlobAsset 기반 불변 레시피 레지스트리와 Crafter 컴포넌트들의 정합성을 검증.
/// </summary>
public class Phase5RecipeBlobTests
{
    private BlobAssetReference<RecipeRegistryBlob> _blobRef;

    [TearDown]
    public void TearDown()
    {
        if (_blobRef.IsCreated)
        {
            _blobRef.Dispose();
        }
    }

    [Test]
    public void Test02_SingleIngredientRecipe_IronAndCopper_CorrectMapping()
    {
        _blobRef = RecipeConfigLoader.LoadBlobAssetFromResources();
        Assert.IsTrue(_blobRef.IsCreated);
        ref var registry = ref _blobRef.Value;
        Assert.AreEqual(5, registry.Recipes.Length, "CrafterRecipeConfig.json must contain exactly 5 recipes.");

        // Recipe 1: Iron_Ore 1 -> Iron 1 (1.0s)
        bool found = registry.TryGetRecipeIndex(1, out int ironIdx);
        Assert.IsTrue(found);
        ref var ironRecipe = ref registry.Recipes[ironIdx];
        Assert.AreEqual(1.0f, ironRecipe.CraftTime);
        Assert.AreEqual(1, ironRecipe.Ingredients.Length);
        Assert.AreEqual(ItemTypeEnum.Iron_Ore, ironRecipe.Ingredients[0].ItemType);
        Assert.AreEqual(1, ironRecipe.Ingredients[0].Amount);

        Assert.AreEqual(1, ironRecipe.Outputs.Length);
        Assert.AreEqual(ItemTypeEnum.Iron, ironRecipe.Outputs[0].ItemType);
        Assert.AreEqual(1, ironRecipe.Outputs[0].Amount);
        Assert.IsFalse(ironRecipe.Outputs[0].IsByproduct);

        // Recipe 2: Copper_Ore 1 -> Copper 1 (1.0s)
        bool foundCopper = registry.TryGetRecipeIndex(2, out int copperIdx);
        Assert.IsTrue(foundCopper);
        ref var copperRecipe = ref registry.Recipes[copperIdx];
        Assert.AreEqual(ItemTypeEnum.Copper_Ore, copperRecipe.Ingredients[0].ItemType);
        Assert.AreEqual(ItemTypeEnum.Copper, copperRecipe.Outputs[0].ItemType);
    }

    [TestCase(1)]
    [TestCase(2)]
    public void Test05_CustomJsonWithByproducts_CorrectlyBuildsOutputs(int byproductCount)
    {
        // 두 번째 부산물 유무만 바꾸어 공통 파싱·출력 계약을 검증한다.
        string secondByproduct = byproductCount == 2
            ? ", { \"itemType\": \"Copper\", \"amount\": 1 }"
            : string.Empty;
        string customJson = @"
        {
          ""recipes"": [
            {
              ""id"": 10,
              ""outputItemType"": ""Iron"",
              ""outputAmount"": 2,
              ""craftTime"": 2.5,
              ""ingredients"": [
                { ""itemType"": ""Iron_Ore"", ""amount"": 3 }
              ],
              ""byproducts"": [
                { ""itemType"": ""Stone"", ""amount"": 1 }" + secondByproduct + @"
              ]
            }
          ]
        }";

        // Act
        _blobRef = RecipeConfigLoader.BuildBlobAssetFromJson(customJson);
        ref var registry = ref _blobRef.Value;

        // Assert
        Assert.AreEqual(1, registry.Recipes.Length);
        ref var recipe = ref registry.Recipes[0];
        Assert.AreEqual(10, recipe.Id);
        Assert.AreEqual(2.5f, recipe.CraftTime);

        Assert.AreEqual(1 + byproductCount, recipe.Outputs.Length);

        // 주생산품
        Assert.AreEqual(ItemTypeEnum.Iron, recipe.Outputs[0].ItemType);
        Assert.AreEqual(2, recipe.Outputs[0].Amount);
        Assert.IsFalse(recipe.Outputs[0].IsByproduct);

        var expectedByproducts = new[] { ItemTypeEnum.Stone, ItemTypeEnum.Copper };
        for (int i = 0; i < byproductCount; i++)
        {
            Assert.AreEqual(expectedByproducts[i], recipe.Outputs[i + 1].ItemType);
            Assert.AreEqual(1, recipe.Outputs[i + 1].Amount);
            Assert.IsTrue(recipe.Outputs[i + 1].IsByproduct);
        }

        // Primary 헬퍼 검증
        Assert.IsTrue(recipe.TryGetPrimaryOutput(out var primary));
        Assert.AreEqual(ItemTypeEnum.Iron, primary.ItemType);
        Assert.AreEqual(2, primary.Amount);
    }

}
