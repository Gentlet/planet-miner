using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>
/// 역할·목적: 철거 승인과 대기 생성/제작/채굴의 인계에 대한 NUnit EditMode 회귀 검증.
/// 입력·검사: 기존 실물/생산 결과/요청으로 앞단 생성 차단·반환/환급·선소비 전 재료 보존을 검사한다. 그룹 정렬과 Item/Building 상대 순서를 바꾼 직접 호출 사례를 구분한다.
/// 수명: EcsWorldTestFixture가 각 사례의 독립 World를 준비하고 종료 시 해제한다.
/// </summary>
public class Phase7ItemCreationDemolitionTests : EcsWorldTestFixture
{
    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        CreateGameplayPrefabDatabases();
    }

    private EndCommandEntityCommandBufferSystem _endCommand;

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void ApprovedDemolition_DropsPendingCreation_PreservesContentsRefundAndWorldSpawn(
        bool createItemSystemFirst, bool useCustomPrefabDb)
    {
        var pipeline = CreatePipeline(createItemSystemFirst);
        int2 position = new int2(7, 9);
        Entity building = CreateBufferedBuilding(BuildingTypeEnum.Crafter, position);
        Entity storedItem = Entities.CreateStoredItem(building, ItemTypeEnum.Iron_Ore);
        Entity productItem = Entities.CreateStoredItem(building, ItemTypeEnum.Copper_Ore);
        _entityManager.AddComponent<DisableRendering>(storedItem);
        _entityManager.AddComponent<DisableRendering>(productItem);
        _entityManager.GetBuffer<StoredItemElement>(building).RemoveAt(1);
        _entityManager.GetBuffer<ProductItemElement>(building).Add(
            new ProductItemElement(productItem, ItemTypeEnum.Copper_Ore, 0));
        AddProductionResults(building);
        AddRefundConfig(BuildingTypeEnum.Crafter, 2);
        if (useCustomPrefabDb)
        {
            AddItemPrefabDb();
        }

        var command = _endCommand.CreateCommandBuffer();
        RequestDemolition(building);
        RequestDemolition(building); // 중복 요청도 승인/환급은 한 번만 수행한다.
        RecordStoredSpawns(command, building);
        Entity worldRequest = command.CreateEntity();
        int2 worldPosition = new int2(20, 30);
        command.AddComponent(worldRequest, new SpawnItemRequest(ItemTypeEnum.Drone, worldPosition));

        Simulation.SetDeltaTime(0.1f);
        pipeline.Update();

        Assert.IsFalse(_entityManager.Exists(building));
        Assert.IsTrue(_entityManager.Exists(storedItem));
        Assert.IsTrue(_entityManager.Exists(productItem));
        Assert.AreEqual(position, _entityManager.GetComponentData<GridPosition>(storedItem).Value);
        Assert.AreEqual(position, _entityManager.GetComponentData<GridPosition>(productItem).Value);
        AssertRequestsConsumed();
        using var query = _entityManager.CreateEntityQuery(typeof(ItemIdentity), typeof(ItemOwnership));
        using var items = query.ToEntityArray(Allocator.Temp);
        Assert.AreEqual(5, items.Length, "기존 2 + 환급 2 + World Spawn 1만 남아야 한다.");
        int refundCount = 0;
        int worldSpawnCount = 0;
        foreach (Entity item in items)
        {
            AssertWorldItem(item);
            ItemTypeEnum type = _entityManager.GetComponentData<ItemIdentity>(item).Type;
            if (type == ItemTypeEnum.Iron)
            {
                refundCount++;
                Assert.AreEqual(position, _entityManager.GetComponentData<GridPosition>(item).Value);
            }
            if (type == ItemTypeEnum.Drone)
            {
                worldSpawnCount++;
                Assert.AreEqual(worldPosition, _entityManager.GetComponentData<GridPosition>(item).Value);
            }
        }
        Assert.AreEqual(2, refundCount);
        Assert.AreEqual(1, worldSpawnCount);
    }

    [Test]
    public void RejectedDemolition_AllowsNormalProductAndStoredSpawns()
    {
        var pipeline = CreatePipeline(true);
        Entity building = CreateBufferedBuilding(BuildingTypeEnum.Crafter, int2.zero);
        _entityManager.AddComponent<IndestructibleBuilding>(building);
        AddProductionResults(building);
        var command = _endCommand.CreateCommandBuffer();
        RequestDemolition(building);
        RecordStoredSpawns(command, building);

        pipeline.Update();

        Assert.IsTrue(_entityManager.Exists(building));
        Assert.AreEqual(0, _entityManager.GetBuffer<ProductResult>(building).Length);
        var stored = _entityManager.GetBuffer<StoredItemElement>(building);
        var products = _entityManager.GetBuffer<ProductItemElement>(building);
        Assert.AreEqual(1, stored.Length);
        Assert.AreEqual(6, products.Length, "주생산품 3 + 부산물 2 + Product Spawn 1");
        using var query = _entityManager.CreateEntityQuery(typeof(ItemIdentity), typeof(ItemOwnership));
        using var items = query.ToEntityArray(Allocator.Temp);
        Assert.AreEqual(7, items.Length);
        foreach (Entity item in items)
        {
            Assert.AreEqual(building, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
            Assert.IsTrue(_entityManager.HasComponent<DisableRendering>(item));
            int membership = 0;
            for (int i = 0; i < stored.Length; i++)
            {
                if (stored[i].ItemEntity == item) membership++;
            }
            for (int i = 0; i < products.Length; i++)
            {
                if (products[i].ItemEntity == item) membership++;
            }
            Assert.AreEqual(1, membership);
        }
        AssertRequestsConsumed();
    }

    [Test]
    public void NextTickWithoutBuildingRequests_CreatesNormalItems()
    {
        var pipeline = CreatePipeline(false);
        Entity demolished = CreateBufferedBuilding(BuildingTypeEnum.Storage, int2.zero);
        RequestDemolition(demolished);
        pipeline.Update();
        Assert.IsFalse(_entityManager.Exists(demolished));
        AssertRequestsConsumed();

        Entity survivor = CreateBufferedBuilding(BuildingTypeEnum.Crafter, new int2(3, 4));
        AddProductionResults(survivor);
        RecordStoredSpawns(_endCommand.CreateCommandBuffer(), survivor);
        pipeline.Update();

        Assert.AreEqual(6, _entityManager.GetBuffer<ProductItemElement>(survivor).Length);
        Assert.AreEqual(1, _entityManager.GetBuffer<StoredItemElement>(survivor).Length);
        AssertRequestsConsumed();
    }

    [Test]
    public void EndCommand_ConsumesAllRequests_RecordsOnlyApprovedBuildingState()
    {
        CreatePipeline(false);
        var buildingSystem = _world.GetExistingSystem<BuildingLifecycleApplySystem>();
        var endStateApply = _world.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>();
        Entity approved = CreateBufferedBuilding(BuildingTypeEnum.Storage, int2.zero);
        Entity protectedBuilding = CreateBufferedBuilding(BuildingTypeEnum.Storage, new int2(1, 0));
        _entityManager.AddComponent<IndestructibleBuilding>(protectedBuilding);
        Entity mainFacility = CreateBufferedBuilding(BuildingTypeEnum.MainFacility, new int2(2, 0));
        Entity constructionSite = CreateBufferedBuilding(BuildingTypeEnum.ConstructionSite, new int2(3, 0));
        Entity invalidType = CreateBufferedBuilding(BuildingTypeEnum.None, new int2(4, 0));
        Entity buildingPrefab = CreateBufferedBuilding(BuildingTypeEnum.Storage, new int2(5, 0));
        _entityManager.AddComponent<Prefab>(buildingPrefab);
        Entity nonBuilding = _entityManager.CreateEntity();
        Entity destroyed = _entityManager.CreateEntity();
        _entityManager.DestroyEntity(destroyed);
        var targets = new[]
        {
            approved, approved, protectedBuilding, mainFacility, constructionSite,
            invalidType, buildingPrefab, nonBuilding, destroyed, Entity.Null
        };
        var requests = new Entity[targets.Length];
        for (int i = 0; i < targets.Length; i++)
        {
            requests[i] = _entityManager.CreateEntity(typeof(DemolishBuildingRequest));
            _entityManager.SetComponentData(requests[i], new DemolishBuildingRequest(targets[i]));
        }

        _world.GetExistingSystemManaged<CommandGroup>().Update();

        // Command 종료 시 모든 요청을 소비하고 승인된 건물의 상태만 후속 단계로 전달한다.
        Assert.IsTrue(_entityManager.Exists(approved));
        Assert.AreEqual(BuildingTypeEnum.Storage, _entityManager.GetComponentData<BuildingType>(approved).Type);
        Assert.IsTrue(_entityManager.HasComponent<PendingBuildingDemolition>(approved));
        for (int i = 0; i < requests.Length; i++)
        {
            Assert.IsFalse(_entityManager.Exists(requests[i]), "유효/거부/중복 요청 모두 EndCommand에서 삭제되어야 한다.");
        }
        foreach (Entity rejectedTarget in new[]
                 { protectedBuilding, mainFacility, constructionSite, invalidType, buildingPrefab, nonBuilding })
        {
            Assert.IsFalse(_entityManager.HasComponent<PendingBuildingDemolition>(rejectedTarget));
        }

        Simulation.UpdateAndComplete(buildingSystem);
        Assert.IsTrue(_entityManager.Exists(approved), "실제 철거는 EndStateApply에서 확정된다.");
        Assert.IsTrue(_entityManager.HasComponent<PendingBuildingDemolition>(approved));
        Simulation.Playback(endStateApply);

        Assert.IsFalse(_entityManager.Exists(approved));
        Assert.IsTrue(_entityManager.Exists(protectedBuilding));
        Assert.IsTrue(_entityManager.Exists(mainFacility));
        Assert.IsTrue(_entityManager.Exists(constructionSite));
        Assert.IsTrue(_entityManager.Exists(invalidType));
        Assert.IsTrue(_entityManager.Exists(buildingPrefab));
        Assert.IsTrue(_entityManager.Exists(nonBuilding));
        AssertRequestsConsumed();
    }

    [TestCase(true, false)]
    [TestCase(true, true)]
    [TestCase(false, false)]
    public void ApprovedDemolition_StopsCrafting_PreservesUnconsumedIngredients(bool demolish, bool alreadyCrafting)
    {
        Entity recipeConfig = RecipeInitSystem.InitializeRecipeRegistry(_entityManager);
        ItemConfigInitSystem.InitializeItemRegistry(_entityManager);
        var pipeline = Simulation.CreateCrafterPipeline();
        _endCommand = _world.GetExistingSystemManaged<EndCommandEntityCommandBufferSystem>();
        var recipe = _entityManager.GetBuffer<RecipeConfigElement>(recipeConfig, true)[0];
        using var recipeIngredients = _entityManager.GetBuffer<RecipeIngredientElement>(recipeConfig, true)
            .ToNativeArray(Allocator.Temp);
        using var recipeOutputs = _entityManager.GetBuffer<RecipeOutputElement>(recipeConfig, true)
            .ToNativeArray(Allocator.Temp);
        Entity crafter = Entities.CreateCrafter(new int2(5, 5), recipe.Id,
            recipe.CraftTime / GameConstants.MaxSimulationDeltaTime * 2f);
        for (int i = 0; i < recipe.IngredientCount; i++)
        {
            var ingredient = recipeIngredients[recipe.IngredientStart + i];
            for (int count = 0; count < ingredient.Amount; count++)
            {
                Entities.CreateStoredItem(crafter, ingredient.ItemType, i);
            }
        }
        using var itemQuery = _entityManager.CreateEntityQuery(typeof(ItemIdentity));
        using var ingredients = itemQuery.ToEntityArray(Allocator.Temp);
        int expectedOutputs = 0;
        for (int i = 0; i < recipe.OutputCount; i++)
        {
            expectedOutputs += recipeOutputs[recipe.OutputStart + i].Amount;
        }

        // 정상 대조군과 진행 중 철거 사례만 먼저 착수한다. 승인 전 소비된 재료는 보상하지 않는다.
        if (!demolish || alreadyCrafting)
        {
            Simulation.SetDeltaTime(GameConstants.MaxSimulationDeltaTime);
            pipeline.Update();
            Assert.AreEqual(0, itemQuery.CalculateEntityCount());
            Assert.IsTrue(_entityManager.GetComponentData<CrafterState>(crafter).IsCraftingActive);
        }
        if (demolish)
        {
            RequestDemolition(crafter);
        }

        Simulation.SetDeltaTime(GameConstants.MaxSimulationDeltaTime, 0.2);
        pipeline.Update();

        foreach (Entity ingredient in ingredients)
        {
            bool shouldPreserve = demolish && !alreadyCrafting;
            Assert.AreEqual(shouldPreserve, _entityManager.Exists(ingredient));
            if (shouldPreserve)
            {
                AssertWorldItem(ingredient);
                Assert.AreEqual(new int2(5, 5), _entityManager.GetComponentData<GridPosition>(ingredient).Value);
            }
        }
        Assert.AreEqual(!demolish, _entityManager.Exists(crafter));
        int expectedItems = !demolish ? expectedOutputs : alreadyCrafting ? 0 : ingredients.Length;
        Assert.AreEqual(expectedItems, itemQuery.CalculateEntityCount());
        if (!demolish)
        {
            Assert.AreEqual(expectedOutputs, _entityManager.GetBuffer<ProductItemElement>(crafter).Length);
            Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(crafter).Length);
        }
        AssertRequestsConsumed();
    }

    [TestCase(true, 1)]
    [TestCase(true, 2)]
    [TestCase(false, 1)]
    [TestCase(false, 2)]
    public void ApprovedDemolition_StopsMining_LeavesResourceUnconsumed(bool demolish, int amount)
    {
        var pipeline = CreatePipeline(false);
        var decision = _world.GetExistingSystemManaged<DecisionGroup>();
        decision.AddSystemToUpdateList(_world.GetOrCreateSystem<MinerDecisionSystem>());
        decision.SortSystems();
        var execution = _world.GetExistingSystemManaged<ExecutionGroup>();
        execution.AddSystemToUpdateList(_world.GetOrCreateSystem<MinerExecutionSystem>());
        execution.SortSystems();
        Entity resource = Entities.CreateResourceNode(int2.zero, ItemTypeEnum.Iron_Ore, amount);
        Entity miner = Entities.CreateMiner(int2.zero, new int2(1, 1), DirectionEnum.Up, progress: 1f);
        _entityManager.SetComponentData(miner, new MinerDecision(true, resource));
        _entityManager.SetComponentEnabled<MinerDecision>(miner, true);
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<ResourceSpatialSyncSystem>());
        if (demolish)
        {
            RequestDemolition(miner);
        }

        Simulation.SetDeltaTime(0.1f);
        pipeline.Update();

        Assert.AreEqual(!demolish, _entityManager.Exists(miner));
        bool resourceRemains = demolish || amount > 1;
        Assert.AreEqual(resourceRemains, _entityManager.Exists(resource));
        if (resourceRemains)
        {
            Assert.AreEqual(demolish ? amount : amount - 1,
                _entityManager.GetComponentData<ResourceNode>(resource).Amount);
        }
        using var query = _entityManager.CreateEntityQuery(typeof(ItemIdentity));
        Assert.AreEqual(demolish ? 0 : 1, query.CalculateEntityCount());
        if (!demolish)
        {
            Assert.AreEqual(1, _entityManager.GetBuffer<ProductItemElement>(miner).Length);
        }
        AssertRequestsConsumed();
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ValidatedDemolition_BlocksCreation_InEitherApplyOrder(bool itemFirst)
    {
        CreatePipeline(itemFirst);
        Entity building = CreateBufferedBuilding(BuildingTypeEnum.Crafter, int2.zero);
        AddProductionResults(building);
        RequestDemolition(building);
        RecordStoredSpawns(_endCommand.CreateCommandBuffer(), building);
        _world.GetExistingSystemManaged<CommandGroup>().Update();

        Assert.IsTrue(_entityManager.HasComponent<PendingBuildingDemolition>(building));
        Assert.AreEqual(0, _entityManager.GetBuffer<ProductResult>(building).Length,
            "승인 시 오래된 생산 결과를 Command에서 소비한다.");
        using (var spawns = _entityManager.CreateEntityQuery(new EntityQueryDesc
               {
                   All = new[] { ComponentType.ReadOnly<SpawnItemRequest>() },
                   Options = EntityQueryOptions.IgnoreComponentEnabledState
               }))
        {
            Assert.AreEqual(2, spawns.CalculateEntityCount(), "EndCommand에서 생성된 요청도 Decision이 검사해야 한다.");
            _world.GetExistingSystemManaged<DecisionGroup>().Update();
            _entityManager.CompleteAllTrackedJobs();
            using var pendingSpawns = spawns.ToEntityArray(Allocator.Temp);
            foreach (Entity spawn in pendingSpawns)
            {
                Assert.IsFalse(_entityManager.IsComponentEnabled<SpawnItemRequest>(spawn));
            }
        }

        var itemSystem = _world.GetExistingSystem<ItemLifecycleApplySystem>();
        var buildingSystem = _world.GetExistingSystem<BuildingLifecycleApplySystem>();
        // 중간 Job 완료 대기 없이 두 예약 순서와 ECB 생성 순서 모두 검증한다.
        Simulation.Update(itemFirst ? itemSystem : buildingSystem);
        Simulation.Update(itemFirst ? buildingSystem : itemSystem);
        Simulation.Playback(_world.GetExistingSystemManaged<EndStateApplyEntityCommandBufferSystem>());

        Assert.IsFalse(_entityManager.Exists(building));
        using var items = _entityManager.CreateEntityQuery(typeof(ItemIdentity));
        Assert.AreEqual(0, items.CalculateEntityCount());
        AssertRequestsConsumed();
    }

    private GameSimulationGroup CreatePipeline(bool createItemSystemFirst)
    {
        // Apply 생성 순서를 바꾸어 상대 실행 순서와 관계없이 앞단 철거 승인 차단 계약을 검사한다.
        var simulation = _world.GetOrCreateSystemManaged<GameSimulationGroup>();
        var command = _world.GetOrCreateSystemManaged<CommandGroup>();
        var decision = _world.GetOrCreateSystemManaged<DecisionGroup>();
        var reservation = _world.GetOrCreateSystemManaged<ReservationGroup>();
        var execution = _world.GetOrCreateSystemManaged<ExecutionGroup>();
        var apply = _world.GetOrCreateSystemManaged<StateApplyGroup>();
        var sync = _world.GetOrCreateSystemManaged<SynchronizationGroup>();
        simulation.AddSystemToUpdateList(sync);
        simulation.AddSystemToUpdateList(apply);
        simulation.AddSystemToUpdateList(execution);
        simulation.AddSystemToUpdateList(reservation);
        simulation.AddSystemToUpdateList(decision);
        simulation.AddSystemToUpdateList(command);
        _endCommand = _world.GetOrCreateSystemManaged<EndCommandEntityCommandBufferSystem>();
        command.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingDemolitionCommandSystem>());
        command.AddSystemToUpdateList(_endCommand);
        decision.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemSpawnAdmissionDecisionSystem>());
        if (createItemSystemFirst)
        {
            apply.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemLifecycleApplySystem>());
            apply.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingLifecycleApplySystem>());
        }
        else
        {
            apply.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingLifecycleApplySystem>());
            apply.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemLifecycleApplySystem>());
        }
        apply.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>());
        command.SortSystems();
        decision.SortSystems();
        apply.SortSystems();
        simulation.SortSystems();
        return simulation;
    }

    private Entity CreateBufferedBuilding(BuildingTypeEnum type, int2 position)
    {
        Entity building = Entities.CreateBuilding(type, position, new int2(1, 1));
        _entityManager.AddBuffer<StoredItemElement>(building);
        _entityManager.AddBuffer<ProductItemElement>(building);
        _entityManager.AddBuffer<ProductResult>(building);
        return building;
    }

    private void AddProductionResults(Entity building)
    {
        var results = _entityManager.GetBuffer<ProductResult>(building);
        results.Add(new ProductResult(ItemTypeEnum.Copper, 3, 0));
        results.Add(new ProductResult(ItemTypeEnum.Stone, 2, 1));
    }

    private void RequestDemolition(Entity building)
    {
        Entity request = _entityManager.CreateEntity(typeof(DemolishBuildingRequest));
        _entityManager.SetComponentData(request, new DemolishBuildingRequest(building));
    }

    private static void RecordStoredSpawns(EntityCommandBuffer command, Entity building)
    {
        Entity storageRequest = command.CreateEntity();
        command.AddComponent(storageRequest, new SpawnItemRequest(ItemTypeEnum.Iron, building, ItemSpawnDestination.Storage));
        Entity productRequest = command.CreateEntity();
        command.AddComponent(productRequest, new SpawnItemRequest(ItemTypeEnum.Iron, building, ItemSpawnDestination.Product));
    }

    private void AddRefundConfig(BuildingTypeEnum type, int quantity)
    {
        Entity config = _entityManager.CreateEntity(typeof(BuildingConfig));
        _entityManager.AddBuffer<BuildingConfigElement>(config);
        _entityManager.AddBuffer<BuildingConstructionMaterialElement>(config).Add(
            new BuildingConstructionMaterialElement(type, ItemTypeEnum.Iron, quantity));
    }

    private void AddItemPrefabDb()
    {
        TestPrefabDatabaseFactory.RemoveDatabase<ItemPrefabDatabase>(_entityManager);
        Entity database = _entityManager.CreateEntity(typeof(ItemPrefabDatabase));
        _entityManager.AddBuffer<ItemPrefabElement>(database);
        foreach (var type in new[] { ItemTypeEnum.Iron, ItemTypeEnum.Copper, ItemTypeEnum.Stone, ItemTypeEnum.Drone })
        {
            Entity prefab = _entityManager.CreateEntity(typeof(Prefab), typeof(ItemIdentity), typeof(LocalTransform));
            _entityManager.SetComponentData(prefab, new ItemIdentity(type));
            _entityManager.GetBuffer<ItemPrefabElement>(database).Add(new ItemPrefabElement(type, prefab));
        }
    }

    private void AssertRequestsConsumed()
    {
        using var demolish = _entityManager.CreateEntityQuery(typeof(DemolishBuildingRequest));
        using var spawn = _entityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<SpawnItemRequest>() },
            Options = EntityQueryOptions.IgnoreComponentEnabledState
        });
        Assert.AreEqual(0, demolish.CalculateEntityCount());
        Assert.AreEqual(0, spawn.CalculateEntityCount());
    }

    private void AssertWorldItem(Entity item)
    {
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
        Assert.IsFalse(_entityManager.HasComponent<DisableRendering>(item));
        int2 position = _entityManager.GetComponentData<GridPosition>(item).Value;
        Assert.AreEqual(new float3(position.x, position.y, 0f), _entityManager.GetComponentData<LocalTransform>(item).Position);
    }
}
