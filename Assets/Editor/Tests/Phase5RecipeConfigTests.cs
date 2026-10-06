using NUnit.Framework;
using PlanetMiner.Config;
using PlanetMiner.Tests;
using Unity.Entities;

/// <summary>
/// 역할·목적: 레시피 JSON의 재료/주생산품/부산물 매핑에 대한 NUnit EditMode 회귀 검증.
/// 입력·검사: 기본/사용자 레시피를 ECS 버퍼로 게시해 구간·순서·개수·품목을 검사한다. 실제 제작 진행/생산은 별도 파이프라인 범위다.
/// 수명: EcsWorldTestFixture가 각 사례의 독립 World를 준비하고 종료 시 해제한다.
/// </summary>
public class Phase5RecipeConfigTests : EcsWorldTestFixture
{
    [Test]
    public void Test02_SingleIngredientRecipe_IronAndCopper_CorrectMapping()
    {
        Entity configEntity = RecipeConfigLoader.PublishConfig(
            _entityManager, RecipeConfigLoader.LoadFromResources());
        Assert.IsTrue(_entityManager.Exists(configEntity));
        var recipes = _entityManager.GetBuffer<RecipeConfigElement>(configEntity, true);
        var ingredients = _entityManager.GetBuffer<RecipeIngredientElement>(configEntity, true);
        var outputs = _entityManager.GetBuffer<RecipeOutputElement>(configEntity, true);
        Assert.AreEqual(5, recipes.Length, "CrafterRecipeConfig.json must contain exactly 5 recipes.");

        // Recipe 1: Iron_Ore 1 -> Iron 1 (1.0s)
        bool found = RecipeConfigLookupUtility.TryGetRecipeIndex(recipes, 1, out int ironIdx);
        Assert.IsTrue(found);
        var ironRecipe = recipes[ironIdx];
        Assert.AreEqual(1.0f, ironRecipe.CraftTime);
        Assert.AreEqual(1, ironRecipe.IngredientCount);
        Assert.AreEqual(ItemTypeEnum.Iron_Ore, ingredients[ironRecipe.IngredientStart].ItemType);
        Assert.AreEqual(1, ingredients[ironRecipe.IngredientStart].Amount);

        Assert.AreEqual(1, ironRecipe.OutputCount);
        Assert.AreEqual(ItemTypeEnum.Iron, outputs[ironRecipe.OutputStart].ItemType);
        Assert.AreEqual(1, outputs[ironRecipe.OutputStart].Amount);
        Assert.IsFalse(outputs[ironRecipe.OutputStart].IsByproduct);

        // Recipe 2: Copper_Ore 1 -> Copper 1 (1.0s)
        bool foundCopper = RecipeConfigLookupUtility.TryGetRecipeIndex(recipes, 2, out int copperIdx);
        Assert.IsTrue(foundCopper);
        var copperRecipe = recipes[copperIdx];
        Assert.AreEqual(ItemTypeEnum.Copper_Ore, ingredients[copperRecipe.IngredientStart].ItemType);
        Assert.AreEqual(ItemTypeEnum.Copper, outputs[copperRecipe.OutputStart].ItemType);
    }

    [TestCase(1)]
    [TestCase(2)]
    public void Test05_CustomJsonWithByproducts_CorrectlyBuildsOutputs(int byproductCount)
    {
        // 부산물 개수를 바꿔 출력 구간/순서 매핑을 검사한다. 실제 제작을 진행하는 사례는 아니다.
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
        Entity configEntity = RecipeConfigLoader.PublishConfig(
            _entityManager, RecipeConfigLoader.ParseJson(customJson));
        var recipes = _entityManager.GetBuffer<RecipeConfigElement>(configEntity, true);
        var outputs = _entityManager.GetBuffer<RecipeOutputElement>(configEntity, true);

        // Assert
        Assert.AreEqual(1, recipes.Length);
        var recipe = recipes[0];
        Assert.AreEqual(10, recipe.Id);
        Assert.AreEqual(2.5f, recipe.CraftTime);

        Assert.AreEqual(1 + byproductCount, recipe.OutputCount);

        // 주생산품
        Assert.AreEqual(ItemTypeEnum.Iron, outputs[recipe.OutputStart].ItemType);
        Assert.AreEqual(2, outputs[recipe.OutputStart].Amount);
        Assert.IsFalse(outputs[recipe.OutputStart].IsByproduct);

        var expectedByproducts = new[] { ItemTypeEnum.Stone, ItemTypeEnum.Copper };
        for (int i = 0; i < byproductCount; i++)
        {
            Assert.AreEqual(expectedByproducts[i], outputs[recipe.OutputStart + i + 1].ItemType);
            Assert.AreEqual(1, outputs[recipe.OutputStart + i + 1].Amount);
            Assert.IsTrue(outputs[recipe.OutputStart + i + 1].IsByproduct);
        }

        // Primary 헬퍼 검증
        Assert.IsTrue(recipe.TryGetPrimaryOutput(outputs, out var primary));
        Assert.AreEqual(ItemTypeEnum.Iron, primary.ItemType);
        Assert.AreEqual(2, primary.Amount);
    }

}
