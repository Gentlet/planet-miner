using NUnit.Framework;
using PlanetMiner.Config;
using PlanetMiner.Tests;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

/// <summary>
/// Task 5.1: Recipe 데이터 구조 (BlobAsset / Unmanaged Struct) 단위 테스트 슈트.
/// BlobAsset 기반 불변 레시피 레지스트리와 Crafter 컴포넌트들의 정합성을 검증합니다.
/// </summary>
public class Phase5RecipeBlobTests : EcsWorldTestFixture
{
    private BlobAssetReference<RecipeRegistryBlob> _blobRef;

    [TearDown]
    public override void TearDown()
    {
        if (_blobRef.IsCreated)
        {
            _blobRef.Dispose();
        }
        base.TearDown();
    }

    [Test]
    public void Test01_RecipeConfigLoader_LoadsDefaultRecipesSuccessfully()
    {
        // Act: Resources/Config/CrafterRecipeConfig.json 로드
        _blobRef = RecipeConfigLoader.LoadBlobAssetFromResources();

        // Assert: 5종 레시피가 정상 로드되어야 함
        Assert.IsTrue(_blobRef.IsCreated);
        ref var registry = ref _blobRef.Value;
        Assert.AreEqual(5, registry.Recipes.Length, "CrafterRecipeConfig.json must contain exactly 5 recipes.");
    }

    [Test]
    public void Test02_SingleIngredientRecipe_IronAndCopper_CorrectMapping()
    {
        _blobRef = RecipeConfigLoader.LoadBlobAssetFromResources();
        ref var registry = ref _blobRef.Value;

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

    [Test]
    public void Test03_MultiIngredientRecipe_Drone_CorrectMapping()
    {
        _blobRef = RecipeConfigLoader.LoadBlobAssetFromResources();
        ref var registry = ref _blobRef.Value;

        // Recipe 5: Iron_Stick 2 + Copper_Stick 2 -> Drone 1 (4.0s)
        bool found = registry.TryGetRecipeIndex(5, out int droneIdx);
        Assert.IsTrue(found);
        ref var droneRecipe = ref registry.Recipes[droneIdx];
        Assert.AreEqual(4.0f, droneRecipe.CraftTime);
        Assert.AreEqual(2, droneRecipe.Ingredients.Length);

        Assert.IsTrue(droneRecipe.TryFindIngredient(ItemTypeEnum.Iron_Stick, out int ironStickAmount));
        Assert.AreEqual(2, ironStickAmount);

        Assert.IsTrue(droneRecipe.TryFindIngredient(ItemTypeEnum.Copper_Stick, out int copperStickAmount));
        Assert.AreEqual(2, copperStickAmount);

        Assert.AreEqual(1, droneRecipe.Outputs.Length);
        Assert.AreEqual(ItemTypeEnum.Drone, droneRecipe.Outputs[0].ItemType);
        Assert.AreEqual(1, droneRecipe.Outputs[0].Amount);
    }

    [Test]
    public void Test04_RecipeRegistryBlob_QueryHelpers_FastLookup()
    {
        _blobRef = RecipeConfigLoader.LoadBlobAssetFromResources();
        ref var registry = ref _blobRef.Value;

        // 1. Primary Output 검색: Iron_Stick (Recipe 3)
        bool foundStick = registry.TryFindRecipeIndexByPrimaryOutput(ItemTypeEnum.Iron_Stick, out int stickIdx);
        Assert.IsTrue(foundStick);
        ref var stickRecipe = ref registry.Recipes[stickIdx];
        Assert.AreEqual(3, stickRecipe.Id);

        // 2. 재료 요구량 검색
        Assert.IsTrue(stickRecipe.TryFindIngredient(ItemTypeEnum.Iron, out int ironReq));
        Assert.AreEqual(2, ironReq);
        Assert.IsFalse(stickRecipe.TryFindIngredient(ItemTypeEnum.Coal, out _));

        // 3. 존재하지 않는 레시피 검색
        Assert.IsFalse(registry.TryGetRecipeIndex(999, out _));
        Assert.IsFalse(registry.TryFindRecipeIndexByPrimaryOutput(ItemTypeEnum.None, out _));
    }

    [Test]
    public void Test05_CustomJsonWithByproducts_CorrectlyBuildsOutputs()
    {
        // Arrange: 주 완성품(Iron 1) + 부산품(Stone 1)이 포함된 커스텀 JSON
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
                { ""itemType"": ""Stone"", ""amount"": 1 }
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

        // 출력물 검증: 총 2개 (주생산품 1 + 부산품 1)
        Assert.AreEqual(2, recipe.Outputs.Length);

        // 주생산품
        Assert.AreEqual(ItemTypeEnum.Iron, recipe.Outputs[0].ItemType);
        Assert.AreEqual(2, recipe.Outputs[0].Amount);
        Assert.IsFalse(recipe.Outputs[0].IsByproduct);

        // 부산품
        Assert.AreEqual(ItemTypeEnum.Stone, recipe.Outputs[1].ItemType);
        Assert.AreEqual(1, recipe.Outputs[1].Amount);
        Assert.IsTrue(recipe.Outputs[1].IsByproduct);

        // Primary 헬퍼 검증
        Assert.IsTrue(recipe.TryGetPrimaryOutput(out var primary));
        Assert.AreEqual(ItemTypeEnum.Iron, primary.ItemType);
        Assert.AreEqual(2, primary.Amount);
    }

    [Test]
    public void Test06_CrafterComponents_StateAndDecision_Lifecycle()
    {
        // 1. Crafter 엔티티 생성
        var crafterEntity = _entityManager.CreateEntity(
            typeof(CrafterState),
            typeof(CrafterDecision),
            typeof(Storage));

        _entityManager.SetComponentData(crafterEntity, new CrafterState(selectedRecipeId: 1, speed: 1.5f));
        _entityManager.SetComponentData(crafterEntity, new CrafterDecision(canCraft: false, recipeId: 1));
        _entityManager.SetComponentEnabled<CrafterDecision>(crafterEntity, false);
        _entityManager.SetComponentData(crafterEntity, new Storage(slotCount: 4));
        _entityManager.AddBuffer<ProductResult>(crafterEntity);

        // 2. 컴포넌트 값 및 상태 검증
        var state = _entityManager.GetComponentData<CrafterState>(crafterEntity);
        Assert.AreEqual(1, state.SelectedRecipeId);
        Assert.AreEqual(1.5f, state.Speed);
        Assert.AreEqual(CrafterStatusEnum.Idle, state.Status);

        Assert.IsFalse(_entityManager.IsComponentEnabled<CrafterDecision>(crafterEntity));
        Assert.AreEqual(0, _entityManager.GetBuffer<ProductResult>(crafterEntity).Length);
    }

    [Test]
    public void Test07_BurstJob_CanReadRecipeRegistryBlob_WithoutSafetyErrors()
    {
        // 1. 전역 싱글톤 초기화
        _blobRef = RecipeInitSystem.InitializeRecipeRegistry(_entityManager);

        var registry = _entityManager.CreateEntityQuery(typeof(RecipeRegistry)).GetSingleton<RecipeRegistry>();
        var resultCraftTime = new NativeReference<float>(Allocator.TempJob);

        try
        {
            // 2. Burst Job 실행 (Recipe 5: Drone -> 4.0s)
            var job = new BurstRecipeReadTestJob
            {
                Registry = registry,
                SelectedRecipeId = 5,
                ResultCraftTime = resultCraftTime
            };

            var handle = IJobExtensions.Schedule(job);
            handle.Complete();

            // 3. Job 실행 결과 확인 (Drone 레시피 제작시간 4.0f가 정상 판독되었는지)
            Assert.AreEqual(4.0f, resultCraftTime.Value, "Burst job should read Recipe 5 CraftTime (4.0f) successfully.");
        }
        finally
        {
            resultCraftTime.Dispose();
        }
    }

    [Test]
    public void Test08_CustomJsonWithMultipleByproducts_BuildsAllOutputsCorrectly()
    {
        // Arrange: 주 완성품(Iron 2) + 부산품 1(Stone 1) + 부산품 2(Copper 1)
        string customJson = @"
        {
          ""recipes"": [
            {
              ""id"": 20,
              ""outputItemType"": ""Iron"",
              ""outputAmount"": 2,
              ""craftTime"": 3.0,
              ""ingredients"": [
                { ""itemType"": ""Iron_Ore"", ""amount"": 4 }
              ],
              ""byproducts"": [
                { ""itemType"": ""Stone"", ""amount"": 1 },
                { ""itemType"": ""Copper"", ""amount"": 1 }
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
        Assert.AreEqual(20, recipe.Id);
        Assert.AreEqual(3.0f, recipe.CraftTime);

        // 총 출력물: 3개 (주생산품 1 + 부산품 2)
        Assert.AreEqual(3, recipe.Outputs.Length);

        // Output 0: 주생산품
        Assert.AreEqual(ItemTypeEnum.Iron, recipe.Outputs[0].ItemType);
        Assert.AreEqual(2, recipe.Outputs[0].Amount);
        Assert.IsFalse(recipe.Outputs[0].IsByproduct);

        // Output 1: 부산품 1
        Assert.AreEqual(ItemTypeEnum.Stone, recipe.Outputs[1].ItemType);
        Assert.AreEqual(1, recipe.Outputs[1].Amount);
        Assert.IsTrue(recipe.Outputs[1].IsByproduct);

        // Output 2: 부산품 2
        Assert.AreEqual(ItemTypeEnum.Copper, recipe.Outputs[2].ItemType);
        Assert.AreEqual(1, recipe.Outputs[2].Amount);
        Assert.IsTrue(recipe.Outputs[2].IsByproduct);

        // TryGetPrimaryOutput 검증
        Assert.IsTrue(recipe.TryGetPrimaryOutput(out var primary));
        Assert.AreEqual(ItemTypeEnum.Iron, primary.ItemType);
        Assert.AreEqual(2, primary.Amount);
    }
}

/// <summary>
/// Burst Job 내부에서 RecipeRegistryBlob 포인터 안전 접근성을 검증하는 테스트용 Job.
/// </summary>
[BurstCompile]
public struct BurstRecipeReadTestJob : Unity.Jobs.IJob
{
    [ReadOnly]
    public RecipeRegistry Registry;

    public int SelectedRecipeId;

    public NativeReference<float> ResultCraftTime;

    public void Execute()
    {
        ref var registryBlob = ref Registry.Value.Value;
        if (registryBlob.TryGetRecipeIndex(SelectedRecipeId, out int recipeIdx))
        {
            ResultCraftTime.Value = registryBlob.Recipes[recipeIdx].CraftTime;
        }
    }
}
