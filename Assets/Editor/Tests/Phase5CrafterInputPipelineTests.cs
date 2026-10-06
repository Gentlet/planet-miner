using NUnit.Framework;
using PlanetMiner.Config;
using PlanetMiner.Tests;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// 역할·목적: 제작기 생성→레시피/슬롯→입고/생산→레시피 변경에 대한 NUnit EditMode 회귀 검증.
/// 입력·검사: ECS 프리팹/요청으로 생성한 제작기의 슬롯/필터·Owner·실패 보존을 검사한다. 개별 호출과 정렬 그룹 사례 및 실제 장면 베이킹을 구분한다.
/// 수명: EcsWorldTestFixture가 각 사례의 독립 World를 준비하고 종료 시 해제한다.
/// </summary>
public class Phase5CrafterInputPipelineTests : EcsWorldTestFixture
{
    private Entity _recipes;
    private Entity _items;
    private EndCommandEntityCommandBufferSystem _commandEcb;
    private EndBuildingEntityCommandBufferSystem _applyEcb;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        CreateGameplayPrefabDatabases();
        _recipes = Entity.Null;
        _items = ItemConfigInitSystem.InitializeItemRegistry(_entityManager);
        _commandEcb = _world.GetOrCreateSystemManaged<EndCommandEntityCommandBufferSystem>();
        _applyEcb = _world.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>();
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void ActualCreation_InitializesEmptyInputAndDisabledDecisions(bool withCustomPrefab, bool fromConstruction)
    {
        Entity crafter = SpawnCrafter(withCustomPrefab, fromConstruction);

        Assert.AreEqual(CrafterStatusEnum.NoRecipe, _entityManager.GetComponentData<CrafterState>(crafter).Status);
        Assert.AreEqual(0, _entityManager.GetComponentData<CrafterState>(crafter).SelectedRecipeId);
        Assert.AreEqual(0, _entityManager.GetComponentData<Storage>(crafter).SlotCount);
        Assert.AreEqual(0, _entityManager.GetBuffer<BuildingInputSlotElement>(crafter).Length);
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(crafter).Length);
        Assert.AreEqual(0, _entityManager.GetBuffer<ProductItemElement>(crafter).Length);
        Assert.AreEqual(0, _entityManager.GetBuffer<ProductResult>(crafter).Length);
        Assert.IsFalse(_entityManager.IsComponentEnabled<CrafterDecision>(crafter));
        Assert.IsFalse(_entityManager.IsComponentEnabled<CrafterStateDecision>(crafter));
        Assert.IsFalse(_entityManager.IsComponentEnabled<BuildingItemOutputDecision>(crafter));
        Assert.IsFalse(_entityManager.GetComponentData<StorageFilter>(crafter).IsItemAllowed(ItemTypeEnum.Iron_Ore));

        Entity item = CreateBeltInput(ItemTypeEnum.Iron_Ore);
        RunInputDecision();
        Assert.IsFalse(_entityManager.IsComponentEnabled<BuildingItemInputDecision>(item));
    }

    [Test]
    public void RecipeSelection_CreatesDedicatedSlotsAndWhitelist()
    {
        Entity crafter = SpawnCrafter();
        Entity request = ChangeRecipe(crafter, 5);

        Assert.IsFalse(_entityManager.Exists(request));
        Assert.AreEqual(2, _entityManager.GetComponentData<Storage>(crafter).SlotCount);
        var slots = _entityManager.GetBuffer<BuildingInputSlotElement>(crafter);
        Assert.AreEqual(ItemTypeEnum.Iron_Stick, slots[0].ItemType);
        Assert.AreEqual(ItemTypeEnum.Copper_Stick, slots[1].ItemType);
        var filter = _entityManager.GetComponentData<StorageFilter>(crafter);
        Assert.IsTrue(filter.IsItemAllowed(ItemTypeEnum.Iron_Stick));
        Assert.IsTrue(filter.IsItemAllowed(ItemTypeEnum.Copper_Stick));
        Assert.IsFalse(filter.IsItemAllowed(ItemTypeEnum.Iron_Ore));
    }

    [Test]
    public void Reservation_PreservesOtherIngredientsSpace_AndMergesPendingItems()
    {
        Entity crafter = SpawnCrafter();
        ChangeRecipe(crafter, 5);
        int maxStack = GetMaxStack(ItemTypeEnum.Iron_Stick);
        for (int i = 0; i < maxStack; i++)
        {
            Entities.CreateStoredItem(crafter, ItemTypeEnum.Iron_Stick, 0);
        }

        Entity excessIron = CreateReservationCandidate(crafter, ItemTypeEnum.Iron_Stick);
        Entity copperA = CreateReservationCandidate(crafter, ItemTypeEnum.Copper_Stick);
        Entity copperB = CreateReservationCandidate(crafter, ItemTypeEnum.Copper_Stick);
        Run<BuildingStorageInputReservationSystem>();

        Assert.IsFalse(_entityManager.IsComponentEnabled<BuildingItemInputDecision>(excessIron));
        Assert.AreEqual(1, _entityManager.GetComponentData<BuildingItemInputDecision>(copperA).TargetSlotIndex);
        Assert.AreEqual(1, _entityManager.GetComponentData<BuildingItemInputDecision>(copperB).TargetSlotIndex);
        Run<BeltSpatialSyncSystem>();
        ApplyInput();
        Assert.AreEqual(crafter, _entityManager.GetComponentData<ItemOwnership>(copperA).Owner);
        Assert.AreEqual(crafter, _entityManager.GetComponentData<ItemOwnership>(copperB).Owner);
        Assert.AreEqual(maxStack + 2, _entityManager.GetBuffer<StoredItemElement>(crafter).Length);
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<ItemOwnership>(excessIron).Owner);
    }

    [Test]
    public void Reservation_RejectsUnassignedMaterialAndMismatchedLayout()
    {
        Entity crafter = SpawnCrafter();
        ChangeRecipe(crafter, 5);
        Entity unassigned = CreateReservationCandidate(crafter, ItemTypeEnum.Iron_Ore);
        Run<BuildingStorageInputReservationSystem>();
        Assert.IsFalse(_entityManager.IsComponentEnabled<BuildingItemInputDecision>(unassigned));

        _entityManager.SetComponentData(crafter, new Storage(3));
        Entity assigned = CreateReservationCandidate(crafter, ItemTypeEnum.Iron_Stick);
        Run<BuildingStorageInputReservationSystem>();
        Assert.IsFalse(_entityManager.IsComponentEnabled<BuildingItemInputDecision>(assigned));
    }

    [Test]
    public void RecipeChange_ShrinksLayout_MovesRemainingInputs_AndBlocksInputUntilDrained()
    {
        Entity crafter = SpawnCrafter();
        ChangeRecipe(crafter, 5);
        Entity remaining = Entities.CreateStoredItem(crafter, ItemTypeEnum.Copper_Stick, 1);
        var state = _entityManager.GetComponentData<CrafterState>(crafter);
        state.Progress = 0.5f;
        state.IsCraftingActive = true;
        _entityManager.SetComponentData(crafter, state);
        var previousDecision = new CrafterDecision(true, 5, 4);
        var previousStateDecision = new CrafterStateDecision(CrafterStatusEnum.Crafting);
        _entityManager.SetComponentData(crafter, previousDecision);
        _entityManager.SetComponentData(crafter, previousStateDecision);
        _entityManager.SetComponentEnabled<CrafterDecision>(crafter, true);
        _entityManager.SetComponentEnabled<CrafterStateDecision>(crafter, true);

        ChangeRecipe(crafter, 1);

        state = _entityManager.GetComponentData<CrafterState>(crafter);
        Assert.AreEqual(0f, state.Progress);
        Assert.IsFalse(state.IsCraftingActive);
        Assert.AreEqual(CrafterStatusEnum.WaitingForByproductOutput, state.Status);
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(crafter).Length);
        Assert.AreEqual(1, _entityManager.GetComponentData<Storage>(crafter).SlotCount);
        Assert.AreEqual(ItemTypeEnum.Iron_Ore, _entityManager.GetBuffer<BuildingInputSlotElement>(crafter)[0].ItemType);
        Assert.AreEqual(remaining, _entityManager.GetBuffer<ProductItemElement>(crafter)[0].ItemEntity);
        Assert.GreaterOrEqual(_entityManager.GetBuffer<ProductItemElement>(crafter)[0].SlotIndex, 1);
        Assert.AreEqual(crafter, _entityManager.GetComponentData<ItemOwnership>(remaining).Owner);
        // Command는 Decision 데이터와 활성 상태를 소유하지 않는다.
        Assert.AreEqual(previousDecision, _entityManager.GetComponentData<CrafterDecision>(crafter));
        Assert.AreEqual(previousStateDecision, _entityManager.GetComponentData<CrafterStateDecision>(crafter));
        Assert.IsTrue(_entityManager.IsComponentEnabled<CrafterDecision>(crafter));
        Assert.IsTrue(_entityManager.IsComponentEnabled<CrafterStateDecision>(crafter));

        // 다음 Decision 단계가 변경된 레시피와 잔여물 대기를 기준으로 다시 판단한다.
        Run<CrafterDecisionSystem>();
        Assert.AreEqual(1, _entityManager.GetComponentData<CrafterDecision>(crafter).RecipeId);
        Assert.IsFalse(_entityManager.IsComponentEnabled<CrafterDecision>(crafter));
        Assert.AreEqual(CrafterStatusEnum.WaitingForByproductOutput,
            _entityManager.GetComponentData<CrafterStateDecision>(crafter).NextStatus);
        Assert.IsTrue(_entityManager.IsComponentEnabled<CrafterStateDecision>(crafter));

        Entity incoming = CreateBeltInput(ItemTypeEnum.Iron_Ore);
        RunInputDecision();
        Assert.IsFalse(_entityManager.IsComponentEnabled<BuildingItemInputDecision>(incoming));
        // 기존 배출 동작의 완료 상태를 구성한다. 살아 있는 아이템 참조를 남기지 않는다.
        _entityManager.GetBuffer<ProductItemElement>(crafter).Clear();
        _entityManager.DestroyEntity(remaining);
        Run<CrafterDecisionSystem>();
        Run<CrafterStateApplySystem>();
        RunInputDecision();
        Assert.IsTrue(_entityManager.IsComponentEnabled<BuildingItemInputDecision>(incoming));
    }

    [Test]
    public void RecipeChange_PreservesMultipleStacks_WhenMovingRemainingInputs()
    {
        int maxStack = GetMaxStack(ItemTypeEnum.Iron_Ore);
        _recipes = RecipeInitSystem.InitializeRecipeRegistry(_entityManager,
            "{\"recipes\":[{\"id\":99,\"outputItemType\":\"Iron\",\"craftTime\":1," +
            "\"ingredients\":[{\"itemType\":\"Iron_Ore\",\"amount\":" + (maxStack + 1) + "}]}]}");

        Entity crafter = SpawnCrafter();
        ChangeRecipe(crafter, 99);
        Assert.AreEqual(2, _entityManager.GetComponentData<Storage>(crafter).SlotCount);
        for (int i = 0; i < maxStack; i++)
        {
            Entities.CreateStoredItem(crafter, ItemTypeEnum.Iron_Ore, 0);
        }

        Entity pending = CreateReservationCandidate(crafter, ItemTypeEnum.Iron_Ore);
        Run<BuildingStorageInputReservationSystem>();
        Assert.AreEqual(1, _entityManager.GetComponentData<BuildingItemInputDecision>(pending).TargetSlotIndex);
        Run<BeltSpatialSyncSystem>();
        ApplyInput();
        Assert.AreEqual(crafter, _entityManager.GetComponentData<ItemOwnership>(pending).Owner);

        ChangeRecipe(crafter, 0);

        var products = _entityManager.GetBuffer<ProductItemElement>(crafter);
        Assert.AreEqual(maxStack + 1, products.Length);
        int firstStackCount = 0;
        int secondStackCount = 0;
        foreach (var product in products)
        {
            Assert.AreEqual(crafter, _entityManager.GetComponentData<ItemOwnership>(product.ItemEntity).Owner);
            if (product.SlotIndex == 1) firstStackCount++;
            if (product.SlotIndex == 2) secondStackCount++;
        }

        Assert.AreEqual(maxStack, firstStackCount);
        Assert.AreEqual(1, secondStackCount);
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(crafter).Length);
        Assert.AreEqual(0, _entityManager.GetComponentData<Storage>(crafter).SlotCount);
    }

    [Test]
    public void ClearRecipe_WorksWithoutRegistries_AndRetainsRemainingItemOwnership()
    {
        Entity crafter = SpawnCrafter();
        ChangeRecipe(crafter, 1);
        Entity remaining = Entities.CreateStoredItem(crafter, ItemTypeEnum.Iron_Ore, 0);
        DestroyRegistryEntities();

        Entity request = ChangeRecipe(crafter, 0);

        Assert.IsFalse(_entityManager.Exists(request));
        Assert.AreEqual(0, _entityManager.GetComponentData<CrafterState>(crafter).SelectedRecipeId);
        Assert.AreEqual(0, _entityManager.GetComponentData<Storage>(crafter).SlotCount);
        Assert.AreEqual(0, _entityManager.GetBuffer<BuildingInputSlotElement>(crafter).Length);
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(crafter).Length);
        Assert.AreEqual(remaining, _entityManager.GetBuffer<ProductItemElement>(crafter)[0].ItemEntity);
        Assert.AreEqual(crafter, _entityManager.GetComponentData<ItemOwnership>(remaining).Owner);
        Assert.IsFalse(_entityManager.GetComponentData<StorageFilter>(crafter).IsItemAllowed(ItemTypeEnum.Iron_Ore));
        Entity incoming = CreateBeltInput(ItemTypeEnum.Iron_Ore);
        RunInputDecision();
        Assert.IsFalse(_entityManager.IsComponentEnabled<BuildingItemInputDecision>(incoming));
    }

    [Test]
    public void Selection_WaitsForItemRegistry_ThenAppliesTheSameRequest()
    {
        Entity crafter = SpawnCrafter();
        // The missing-registry state is injected only to exercise request deferral.
        _entityManager.CompleteAllTrackedJobs();
        using (var query = _entityManager.CreateEntityQuery(typeof(ItemRegistry)))
        {
            _entityManager.DestroyEntity(query.GetSingletonEntity());
        }

        Entity request = ChangeRecipe(crafter, 5);
        Assert.IsTrue(_entityManager.Exists(request));
        Assert.AreEqual(0, _entityManager.GetComponentData<Storage>(crafter).SlotCount);

        _items = ItemConfigInitSystem.InitializeItemRegistry(_entityManager);
        Run<CrafterRecipeCommandSystem>();
        Simulation.Playback(_commandEcb);
        Assert.IsFalse(_entityManager.Exists(request));
        Assert.AreEqual(2, _entityManager.GetComponentData<Storage>(crafter).SlotCount);
    }

    [Test]
    public void InvalidSlotCalculation_LeavesRecipeProgressBuffersAndFilterUnchanged()
    {
        // Publish the invalid-slot fixture alongside the normal recipes before gameplay starts.
        var config = RecipeConfigLoader.LoadFromResources();
        var invalidConfig = RecipeConfigLoader.ParseJson(
            "{\"recipes\":[{\"id\":99,\"outputItemType\":\"Iron\",\"craftTime\":1," +
            "\"ingredients\":[{\"itemType\":\"Iron_Ore\",\"amount\":100000}]}]}");
        var invalidRecipe = invalidConfig.Recipes[0];
        invalidRecipe.IngredientStart += config.Ingredients.Count;
        invalidRecipe.OutputStart += config.Outputs.Count;
        config.Recipes.Add(invalidRecipe);
        config.Ingredients.AddRange(invalidConfig.Ingredients);
        config.Outputs.AddRange(invalidConfig.Outputs);
        _recipes = RecipeConfigLoader.PublishConfig(_entityManager, config);

        Entity crafter = SpawnCrafter();
        ChangeRecipe(crafter, 1);
        Entity item = Entities.CreateStoredItem(crafter, ItemTypeEnum.Iron_Ore, 0);
        var state = _entityManager.GetComponentData<CrafterState>(crafter);
        state.Progress = 0.5f;
        state.IsCraftingActive = true;
        _entityManager.SetComponentData(crafter, state);
        LogAssert.Expect(LogType.Error,
            "[CrafterRecipeCommandSystem] Invalid input slots for recipe 99: SlotLimitExceeded. Recipe change rejected.");

        Entity request = ChangeRecipe(crafter, 99);

        Assert.IsFalse(_entityManager.Exists(request));
        Assert.AreEqual(1, _entityManager.GetComponentData<CrafterState>(crafter).SelectedRecipeId);
        Assert.AreEqual(0.5f, _entityManager.GetComponentData<CrafterState>(crafter).Progress);
        Assert.IsTrue(_entityManager.GetComponentData<CrafterState>(crafter).IsCraftingActive);
        Assert.AreEqual(1, _entityManager.GetComponentData<Storage>(crafter).SlotCount);
        Assert.AreEqual(item, _entityManager.GetBuffer<StoredItemElement>(crafter)[0].ItemEntity);
        Assert.AreEqual(crafter, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
        Assert.AreEqual(0, _entityManager.GetBuffer<ProductItemElement>(crafter).Length);
        Assert.IsTrue(_entityManager.GetComponentData<StorageFilter>(crafter).IsItemAllowed(ItemTypeEnum.Iron_Ore));
    }

    [Test]
    public void ActualSpawn_RecipeSelection_BeltInput_Consumption_AndProductionConnect()
    {
        Entity crafter = SpawnCrafter();
        ChangeRecipe(crafter, 1);
        Entity ore = CreateBeltInput(ItemTypeEnum.Iron_Ore);
        RunInputDecision();
        Run<BuildingStorageInputReservationSystem>();
        ApplyInput();
        Assert.AreEqual(crafter, _entityManager.GetComponentData<ItemOwnership>(ore).Owner);
        Assert.AreEqual(ore, _entityManager.GetBuffer<StoredItemElement>(crafter)[0].ItemEntity);

        Simulation.SetDeltaTime(GameConstants.MaxSimulationDeltaTime);
        Run<CrafterDecisionSystem>();
        Run<CrafterExecutionSystem>();
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(crafter).Length);
        Assert.IsTrue(_entityManager.IsComponentEnabled<DestroyItemRequest>(ore));
        Run<CrafterStateApplySystem>();
        Run<ItemLifecycleApplySystem>();
        Simulation.Playback(_applyEcb);
        Assert.IsFalse(_entityManager.Exists(ore));

        float craftTime = GetCraftTime(1);
        int ticks = (int)math.ceil(craftTime / GameConstants.MaxSimulationDeltaTime) + 2;
        for (int i = 0; i < ticks && _entityManager.GetBuffer<ProductItemElement>(crafter).Length == 0; i++)
        {
            Run<CrafterDecisionSystem>();
            Run<CrafterExecutionSystem>();
            Run<CrafterStateApplySystem>();
            Run<ItemLifecycleApplySystem>();
            Simulation.Playback(_applyEcb);
        }

        Assert.AreEqual(1, _entityManager.GetBuffer<ProductItemElement>(crafter).Length);
        var product = _entityManager.GetBuffer<ProductItemElement>(crafter)[0];
        Assert.AreEqual(ItemTypeEnum.Iron, product.ItemType);
        Assert.AreEqual(0, product.SlotIndex);
        Assert.AreEqual(crafter, _entityManager.GetComponentData<ItemOwnership>(product.ItemEntity).Owner);
    }

    [Test]
    public void Invariant_AllowsEmptyUnselectedCrafter_ButRejectsZeroCapacityStorage()
    {
        SpawnCrafter();
        var validation = _world.GetOrCreateSystemManaged<WorldInvariantValidationSystem>();
        validation.Update();
        Assert.AreEqual(0, validation.TotalViolationCount);

        Entities.CreateStorage(new int2(10, 10), new int2(1, 1), DirectionEnum.Up, slotCount: 0);
        validation.Update();
        Assert.Greater(validation.TotalViolationCount, 0);
    }

    [Test]
    public void Invariant_RejectsWrongMaterialInDedicatedSlot()
    {
        Entity crafter = SpawnCrafter();
        ChangeRecipe(crafter, 5);
        Entities.CreateStoredItem(crafter, ItemTypeEnum.Iron_Stick, 1);
        var validation = _world.GetOrCreateSystemManaged<WorldInvariantValidationSystem>();
        validation.Update();
        Assert.Greater(validation.TotalViolationCount, 0);
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void SortedGroups_ActualCreation_Transport_Production_RecipeChange_AndClear(
        bool withCustomPrefab, bool fromConstruction)
    {
        var pipeline = Simulation.CreateCrafterPipeline();
        var validation = _world.GetExistingSystemManaged<WorldInvariantValidationSystem>();
        double elapsed = 0;
        System.Action step = () =>
        {
            elapsed += GameConstants.MaxSimulationDeltaTime;
            Simulation.SetDeltaTime(GameConstants.MaxSimulationDeltaTime, elapsed);
            pipeline.Update();
            _entityManager.CompleteAllTrackedJobs();
            Assert.AreEqual(0, validation.TotalViolationCount, "Every completed frame must preserve world invariants.");
        };

        Entity source = CreateCrafterSource(withCustomPrefab, fromConstruction);
        Entity feeder = Entities.CreateStorage(new int2(-1, 0), new int2(1, 1));
        Entity receiver = Entities.CreateStorage(new int2(4, 0), new int2(1, 1));
        Entity inputBelt = Entities.CreateBelt(int2.zero, DirectionEnum.Right);
        Entities.CreateBelt(new int2(3, 0), DirectionEnum.Right);
        step();
        Assert.IsFalse(_entityManager.Exists(source));
        using var crafterQuery = _entityManager.CreateEntityQuery(typeof(CrafterState));
        Entity crafter = crafterQuery.GetSingletonEntity();
        Assert.AreEqual(0, _entityManager.GetComponentData<Storage>(crafter).SlotCount);

        Entity spawn = _entityManager.CreateEntity(typeof(SpawnItemRequest));
        _entityManager.SetComponentData(spawn,
            new SpawnItemRequest(ItemTypeEnum.Iron_Ore, feeder, ItemSpawnDestination.Storage));
        step();
        Assert.IsFalse(_entityManager.Exists(spawn));
        Entity ore = _entityManager.GetBuffer<StoredItemElement>(feeder)[0].ItemEntity;
        float beltSpeed = _entityManager.GetComponentData<BeltComponent>(inputBelt).Speed;
        int transportTicks = (int)math.ceil(1f / (beltSpeed * GameConstants.MaxSimulationDeltaTime)) + 5;
        StepUntil(step, () => _entityManager.GetComponentData<ItemOwnership>(ore).IsWorldItem &&
                            _entityManager.GetComponentData<BeltMovementState>(ore).Progress >= 1f,
            transportTicks);
        step();
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<ItemOwnership>(ore).Owner);
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(crafter).Length);

        Entity recipeRequest = QueueRecipe(crafter, 1);
        step();
        Assert.IsFalse(_entityManager.Exists(recipeRequest));
        Assert.AreEqual(crafter, _entityManager.GetComponentData<ItemOwnership>(ore).Owner);
        Assert.AreEqual(ore, _entityManager.GetBuffer<StoredItemElement>(crafter)[0].ItemEntity);
        Assert.IsFalse(_entityManager.GetComponentData<CrafterState>(crafter).IsCraftingActive);
        step();
        Assert.IsFalse(_entityManager.Exists(ore), "Consumption must finish at the normal EndStateApply boundary.");
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(crafter).Length);
        Assert.IsTrue(_entityManager.GetComponentData<CrafterState>(crafter).IsCraftingActive);

        float craftTime = GetCraftTime(1);
        float speed = _entityManager.GetComponentData<CrafterState>(crafter).Speed;
        int craftingTicks = (int)math.ceil(craftTime /
                                         (speed * GameConstants.MaxSimulationDeltaTime)) + 3;
        StepUntil(step, () => _entityManager.GetBuffer<ProductItemElement>(crafter).Length == 1, craftingTicks);
        var product = _entityManager.GetBuffer<ProductItemElement>(crafter)[0];
        Assert.AreEqual(ItemTypeEnum.Iron, product.ItemType);
        Assert.AreEqual(0, product.SlotIndex);
        Assert.AreEqual(crafter, _entityManager.GetComponentData<ItemOwnership>(product.ItemEntity).Owner);
        step();
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<ItemOwnership>(product.ItemEntity).Owner);
        Assert.AreEqual(0, _entityManager.GetBuffer<ProductItemElement>(crafter).Length);
        StepUntil(step, () => _entityManager.GetComponentData<ItemOwnership>(product.ItemEntity).Owner == receiver,
            transportTicks);
        Assert.AreEqual(product.ItemEntity, _entityManager.GetBuffer<StoredItemElement>(receiver)[0].ItemEntity);

        // 다음 입고가 끝난 직후 레시피를 바꿔, 선소비 전 잔여물이 정상 출력 경로로 배출되는지 확인한다.
        Entity secondSpawn = _entityManager.CreateEntity(typeof(SpawnItemRequest));
        _entityManager.SetComponentData(secondSpawn,
            new SpawnItemRequest(ItemTypeEnum.Iron_Ore, feeder, ItemSpawnDestination.Storage));
        step();
        Entity remaining = _entityManager.GetBuffer<StoredItemElement>(feeder)[0].ItemEntity;
        StepUntil(step, () => _entityManager.GetComponentData<ItemOwnership>(remaining).Owner == crafter,
            transportTicks);
        Entity change = QueueRecipe(crafter, 2);
        step();
        Assert.IsFalse(_entityManager.Exists(change));
        Assert.AreEqual(CrafterStatusEnum.WaitingForByproductOutput,
            _entityManager.GetComponentData<CrafterState>(crafter).Status);
        Assert.IsFalse(_entityManager.GetComponentData<CrafterState>(crafter).IsCraftingActive);
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(crafter).Length);
        Assert.AreEqual(ItemTypeEnum.Copper_Ore, _entityManager.GetBuffer<BuildingInputSlotElement>(crafter)[0].ItemType);
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<ItemOwnership>(remaining).Owner);
        StepUntil(step, () => _entityManager.GetComponentData<ItemOwnership>(remaining).Owner == receiver,
            transportTicks);
        Assert.AreEqual(CrafterStatusEnum.WaitingForInput, _entityManager.GetComponentData<CrafterState>(crafter).Status);
        Assert.AreEqual(2, _entityManager.GetBuffer<StoredItemElement>(receiver).Length);

        Entity clear = QueueRecipe(crafter, 0);
        step();
        Assert.IsFalse(_entityManager.Exists(clear));
        Assert.AreEqual(0, _entityManager.GetComponentData<Storage>(crafter).SlotCount);
        Assert.AreEqual(0, _entityManager.GetBuffer<BuildingInputSlotElement>(crafter).Length);
        Assert.AreEqual(CrafterStatusEnum.NoRecipe, _entityManager.GetComponentData<CrafterState>(crafter).Status);
        Assert.IsFalse(_entityManager.IsComponentEnabled<CrafterDecision>(crafter));
        Assert.IsFalse(_entityManager.IsComponentEnabled<CrafterStateDecision>(crafter));
    }

    private static void StepUntil(System.Action step, System.Func<bool> condition, int maximumTicks)
    {
        for (int tick = 0; tick < maximumTicks && !condition(); tick++)
        {
            step();
        }

        Assert.IsTrue(condition(), $"Pipeline did not reach the expected state within {maximumTicks} ticks.");
    }

    private Entity QueueRecipe(Entity crafter, int recipeId)
    {
        Entity request = _entityManager.CreateEntity(typeof(ChangeCrafterRecipeRequest));
        _entityManager.SetComponentData(request, new ChangeCrafterRecipeRequest(crafter, recipeId));
        return request;
    }

    private Entity SpawnCrafter(bool withCustomPrefab = false, bool fromConstruction = false)
    {
        // 직접 스폰과 현장 완공 경로를 분리 호출하여 ECB 이후 생성물의 런타임 초기화를 확인한다.
        Entity source = CreateCrafterSource(withCustomPrefab, fromConstruction);
        if (fromConstruction)
        {
            Run<ConstructionLifecycleApplySystem>();
        }
        else
        {
            Run<BuildingLifecycleApplySystem>();
        }

        Simulation.Playback(_applyEcb);
        Assert.IsFalse(_entityManager.Exists(source));
        using var query = _entityManager.CreateEntityQuery(typeof(CrafterState));
        return query.GetSingletonEntity();
    }

    private Entity CreateCrafterSource(bool withCustomPrefab, bool fromConstruction)
    {
        if (_recipes == Entity.Null)
        {
            _recipes = RecipeInitSystem.InitializeRecipeRegistry(_entityManager);
        }

        if (withCustomPrefab)
        {
            // ECS 모의 prefab: 기존 상태를 스폰 시 올바르게 초기화하는지도 확인한다.
            Entity prefab = _entityManager.CreateEntity(typeof(Prefab), typeof(LocalTransform), typeof(CrafterState),
                typeof(CrafterDecision), typeof(CrafterStateDecision), typeof(BuildingItemOutputDecision),
                typeof(Storage), typeof(StorageFilter));
            _entityManager.SetComponentData(prefab, new CrafterState(5));
            _entityManager.SetComponentData(prefab, new Storage(9));
            _entityManager.AddBuffer<BuildingInputSlotElement>(prefab).Add(new BuildingInputSlotElement(ItemTypeEnum.Coal));
            TestPrefabDatabaseFactory.RemoveDatabase<BuildingPrefabDatabase>(_entityManager);
            Entity database = _entityManager.CreateEntity(typeof(BuildingPrefabDatabase));
            _entityManager.AddBuffer<BuildingPrefabElement>(database)
                .Add(new BuildingPrefabElement(BuildingTypeEnum.Crafter, prefab, new int2(2, 2)));
        }

        Entity source = _entityManager.CreateEntity();
        if (fromConstruction)
        {
            _entityManager.AddComponentData(source, new BuildingType(BuildingTypeEnum.ConstructionSite));
            _entityManager.AddComponentData(source, new ConstructionSite(BuildingTypeEnum.Crafter));
            _entityManager.AddComponentData(source, new GridPosition(new int2(1, 0)));
            _entityManager.AddComponentData(source, new Direction(DirectionEnum.Up));
            _entityManager.AddComponentData(source, new BuildingFootprint(new int2(2, 2)));
            _entityManager.AddComponentData(source, default(PlacementStamp));
            _entityManager.AddBuffer<ConstructionMaterialRequirementElement>(source);
            _entityManager.AddBuffer<StoredItemElement>(source);
        }
        else
        {
            _entityManager.AddComponentData(source, new SpawnBuildingRequest(BuildingTypeEnum.Crafter, new int2(1, 0)));
        }

        return source;
    }

    private Entity ChangeRecipe(Entity crafter, int recipeId)
    {
        Entity request = _entityManager.CreateEntity(typeof(ChangeCrafterRecipeRequest));
        _entityManager.SetComponentData(request, new ChangeCrafterRecipeRequest(crafter, recipeId));
        Run<CrafterRecipeCommandSystem>();
        Simulation.Playback(_commandEcb);
        return request;
    }

    private Entity CreateBeltInput(ItemTypeEnum type)
    {
        Entities.CreateBelt(int2.zero, DirectionEnum.Right);
        Entity request = _entityManager.CreateEntity(typeof(SpawnItemRequest));
        _entityManager.SetComponentData(request, new SpawnItemRequest(type, int2.zero));
        Run<ItemLifecycleApplySystem>();
        Simulation.Playback(_applyEcb);

        using var query = _entityManager.CreateEntityQuery(typeof(ItemIdentity), typeof(ItemOwnership));
        using var items = query.ToEntityArray(Allocator.Temp);
        foreach (Entity item in items)
        {
            if (_entityManager.GetComponentData<ItemOwnership>(item).Owner != Entity.Null)
            {
                continue;
            }

            _entityManager.SetComponentData(item, new BeltMovementState(1f));
            _entityManager.SetComponentEnabled<BeltMovementState>(item, true);
            return item;
        }

        Assert.Fail("World item spawn did not produce a belt input candidate.");
        return Entity.Null;
    }

    private Entity CreateReservationCandidate(Entity crafter, ItemTypeEnum type)
    {
        Entity item = Entities.CreateBeltItem(int2.zero, DirectionEnum.Right, 1f, 0f, type);
        _entityManager.SetComponentData(item, new BuildingItemInputDecision
        {
            CanDeposit = true,
            TargetBuilding = crafter,
            TargetSlotIndex = -1
        });
        _entityManager.SetComponentEnabled<BuildingItemInputDecision>(item, true);
        return item;
    }

    private void RunInputDecision()
    {
        Run<BuildingSpatialSyncSystem>();
        Run<BeltSpatialSyncSystem>();
        Run<BuildingItemInputDecisionSystem>();
    }

    private void ApplyInput()
    {
        Run<BuildingItemStorageApplySystem>();
        Run<ItemOwnershipApplySystem>();
        Simulation.Playback(_applyEcb);
    }

    private void Run<T>() where T : unmanaged, ISystem
    {
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<T>());
    }

    private void DestroyRegistryEntities()
    {
        _entityManager.CompleteAllTrackedJobs();
        using var recipes = _entityManager.CreateEntityQuery(typeof(RecipeRegistry));
        using var items = _entityManager.CreateEntityQuery(typeof(ItemRegistry));
        _entityManager.DestroyEntity(recipes.GetSingletonEntity());
        _entityManager.DestroyEntity(items.GetSingletonEntity());
    }

    private int GetMaxStack(ItemTypeEnum itemType)
    {
        var registry = _entityManager.GetComponentData<ItemRegistry>(_items);
        return registry.GetMaxStack(_entityManager.GetBuffer<ItemConfigElement>(_items, true), itemType);
    }

    private float GetCraftTime(int recipeId)
    {
        var recipes = _entityManager.GetBuffer<RecipeConfigElement>(_recipes, true);
        Assert.IsTrue(RecipeConfigLookupUtility.TryGetRecipeIndex(recipes, recipeId, out int recipeIndex));
        return recipes[recipeIndex].CraftTime;
    }
}
