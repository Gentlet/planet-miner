using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public class Phase4ResourceAuthoringAndSpawnTests : EcsWorldTestFixture
{
    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        CreateGameplayPrefabDatabases();
    }

    private (Entity ConfigEntity, Entity PrefabDbEntity) SetupWorldWithPrefabs(
        uint worldSeed = 12345u,
        bool includeIron = true,
        bool includeCopper = true,
        bool includeCoal = true,
        bool includeStone = true)
    {
        Entity settingsEntity = _entityManager.CreateEntity(typeof(ResourceGenerationSettings));
        _entityManager.SetComponentData(settingsEntity, new ResourceGenerationSettings(worldSeed, 3));

        var buffer = _entityManager.AddBuffer<ResourceGenerationConfigElement>(settingsEntity);
        buffer.Add(new ResourceGenerationConfigElement(ItemTypeEnum.Iron_Ore, 0.8f, 2, 4, 0.7f, 50, 200));
        buffer.Add(new ResourceGenerationConfigElement(ItemTypeEnum.Copper_Ore, 0.6f, 2, 3, 0.6f, 40, 150));
        buffer.Add(new ResourceGenerationConfigElement(ItemTypeEnum.Coal, 0.5f, 1, 3, 0.65f, 30, 120));
        buffer.Add(new ResourceGenerationConfigElement(ItemTypeEnum.Stone, 0.7f, 2, 4, 0.75f, 60, 250));

        Entity prefabDbEntity = _entityManager.CreateEntity(typeof(ResourcePrefabDatabase));
        var prefabBuffer = _entityManager.AddBuffer<ResourcePrefabElement>(prefabDbEntity);

        if (includeIron)
        {
            var ironPrefab = _entityManager.CreateEntity(typeof(LocalTransform), typeof(ResourceNode));
            _entityManager.SetComponentData(ironPrefab, LocalTransform.FromPosition(float3.zero));
            prefabBuffer.Add(new ResourcePrefabElement(ItemTypeEnum.Iron_Ore, ironPrefab));
        }

        if (includeCopper)
        {
            var copperPrefab = _entityManager.CreateEntity(typeof(LocalTransform), typeof(ResourceNode));
            _entityManager.SetComponentData(copperPrefab, LocalTransform.FromPosition(float3.zero));
            prefabBuffer.Add(new ResourcePrefabElement(ItemTypeEnum.Copper_Ore, copperPrefab));
        }

        if (includeCoal)
        {
            var coalPrefab = _entityManager.CreateEntity(typeof(LocalTransform), typeof(ResourceNode));
            _entityManager.SetComponentData(coalPrefab, LocalTransform.FromPosition(float3.zero));
            prefabBuffer.Add(new ResourcePrefabElement(ItemTypeEnum.Coal, coalPrefab));
        }

        if (includeStone)
        {
            var stonePrefab = _entityManager.CreateEntity(typeof(LocalTransform), typeof(ResourceNode));
            _entityManager.SetComponentData(stonePrefab, LocalTransform.FromPosition(float3.zero));
            prefabBuffer.Add(new ResourcePrefabElement(ItemTypeEnum.Stone, stonePrefab));
        }

        return (settingsEntity, prefabDbEntity);
    }

    [Test]
    public void Test02_ResourceGeneration_InstantiatesPrefabWithTransformAndComponents()
    {
        var (configEntity, prefabDbEntity) = SetupWorldWithPrefabs(worldSeed: 55555u);
        var configs = _entityManager.GetBuffer<ResourceGenerationConfigElement>(configEntity);
        var prefabs = _entityManager.GetBuffer<ResourcePrefabElement>(prefabDbEntity);

        int2 targetChunk = new int2(0, 0);
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        ResourceGenerationUtility.GenerateChunkResources(ref ecb, targetChunk, 55555u, configs, prefabs);
        ecb.Playback(_entityManager);
        ecb.Dispose();

        var query = _entityManager.CreateEntityQuery(typeof(GridPosition), typeof(ResourceNode), typeof(LocalTransform));
        var entities = query.ToEntityArray(Allocator.Temp);
        Assert.Greater(entities.Length, 0, "자원 엔티티가 최소 1개 이상 인스턴스화되어야 합니다.");

        for (int i = 0; i < entities.Length; i++)
        {
            Entity resEntity = entities[i];
            var gridPos = _entityManager.GetComponentData<GridPosition>(resEntity).Value;
            var transform = _entityManager.GetComponentData<LocalTransform>(resEntity);
            var resNode = _entityManager.GetComponentData<ResourceNode>(resEntity);

            // LocalTransform의 X, Y 좌표가 GridPosition과 일치하고 Z는 0.0f인지 검증
            Assert.AreEqual((float)gridPos.x, transform.Position.x, 0.001f);
            Assert.AreEqual((float)gridPos.y, transform.Position.y, 0.001f);
            Assert.AreEqual(0.0f, transform.Position.z, 0.001f);

            // 유효한 광석 타입 및 매장량 검증
            Assert.IsTrue(resNode.ResourceType >= ItemTypeEnum.Iron_Ore && resNode.ResourceType <= ItemTypeEnum.Stone);
            Assert.Greater(resNode.Amount, 0);
        }

        entities.Dispose();
    }

    [Test]
    public void Test03_ResourceGeneration_MissingPrefab_BlocksEntireChunk()
    {
        // Stone 프리팹만 제외하고 DB 구성 (includeStone: false)
        var (configEntity, prefabDbEntity) = SetupWorldWithPrefabs(worldSeed: 77777u, includeStone: false);
        var configs = _entityManager.GetBuffer<ResourceGenerationConfigElement>(configEntity);
        var prefabs = _entityManager.GetBuffer<ResourcePrefabElement>(prefabDbEntity);

        int2 targetChunk = new int2(0, 0);
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        Assert.IsFalse(ResourceGenerationUtility.GenerateChunkResources(ref ecb, targetChunk, 77777u, configs, prefabs));
        ecb.Playback(_entityManager);
        ecb.Dispose();

        var query = _entityManager.CreateEntityQuery(typeof(GridPosition), typeof(ResourceNode));
        var entities = query.ToEntityArray(Allocator.Temp);
        Assert.AreEqual(0, entities.Length, "일부 광석을 먼저 생성하면 재시도 시 중복될 수 있으므로 전체를 대기한다.");

        entities.Dispose();
    }

    [Test]
    public void Test06_EndToEnd_ResourceSpawnToMiningAndDepletionPipeline()
    {
        // 1. 전체 월드 및 프리팹 셋업
        SetupWorldWithPrefabs(worldSeed: 12345u);

        var bootstrapSystem = _world.GetOrCreateSystem(typeof(InitialChunkLoadBootstrapSystem));
        var simulation = Simulation.CreateResourceGenerationPipeline(includeMining: true);

        // 2. 청크 로드 및 자원 스폰 실행
        bootstrapSystem.Update(_world.Unmanaged);
        simulation.Update();

        var fence = _world.EntityManager.CreateEntityQuery(typeof(ResourceSpatialIndexFence)).GetSingletonRW<ResourceSpatialIndexFence>();
        fence.ValueRW.Complete();

        // 3. 스폰된 자원 노드 하나를 선택
        var resQuery = _entityManager.CreateEntityQuery(typeof(GridPosition), typeof(ResourceNode));
        var resEntities = resQuery.ToEntityArray(Allocator.Temp);
        Assert.Greater(resEntities.Length, 0, "자원이 최소 1개 이상 스폰되어야 합니다.");

        Entity targetResource = resEntities[0];
        int2 targetPos = _entityManager.GetComponentData<GridPosition>(targetResource).Value;
        resEntities.Dispose();

        // 매장량을 1로 강제 설정하여 1회 채굴 후 즉시 고갈되도록 구성
        _entityManager.SetComponentData(targetResource, new ResourceNode(ItemTypeEnum.Iron_Ore, 1));

        // 전역 ResourceConfig (유한 매장량 모드)
        var resConfigEntity = _entityManager.CreateEntity(typeof(ResourceConfig));
        _entityManager.SetComponentData(resConfigEntity, new ResourceConfig(isResourceInfinite: false));

        // 4. 채굴기 설치 (자원 노드 위치 위에 1x1 마이너 설치)
        Entity minerEntity = Entities.CreateMiner(targetPos, new int2(1, 1), DirectionEnum.Up, miningSpeed: 100.0f);

        // 5. 다음 실제 시뮬레이션 프레임에서 결정 -> 채굴 -> 생산물 생성 -> 고갈 반영.
        Simulation.SetDeltaTime(0.1f);
        simulation.Update();
        var decision = _entityManager.GetComponentData<MinerDecision>(minerEntity);
        Assert.IsTrue(decision.CanMine, "채굴기가 자원 노드를 정상 인식해야 합니다.");
        Assert.AreEqual(targetResource, decision.TargetResource, "채굴 대상이 스폰된 자원 엔티티여야 합니다.");

        // 6. 고갈 검증: 자원 엔티티가 월드에서 제거되었는지 확인
        Assert.IsFalse(_entityManager.Exists(targetResource), "매장량이 고갈된 자원 엔티티는 파괴되어야 합니다.");
        Assert.AreEqual(1, _entityManager.GetBuffer<ProductItemElement>(minerEntity).Length);
    }
    [TestCase(false)]
    [TestCase(true)]
    public void DelayedDatabase_RetainsRequestsAndSpawnsExactlyOnce(bool createEmptyDatabase)
    {
        CreateSingleChunkSettings();
        Entity database = Entity.Null;
        if (createEmptyDatabase)
        {
            database = _entityManager.CreateEntity(typeof(ResourcePrefabDatabase));
            _entityManager.AddBuffer<ResourcePrefabElement>(database);
        }
        var simulation = Simulation.CreateResourceGenerationPipeline();
        _world.GetOrCreateSystem<InitialChunkLoadBootstrapSystem>().Update(_world.Unmanaged);

        for (int frame = 0; frame < 3; frame++)
        {
            EnqueueOrigin();
            simulation.Update();
            AssertWaitingForOrigin();
        }

        if (database == Entity.Null)
        {
            database = _entityManager.CreateEntity(typeof(ResourcePrefabDatabase));
            _entityManager.AddBuffer<ResourcePrefabElement>(database);
        }
        AddPrefab(database, ItemTypeEnum.Iron_Ore);
        AssertCompletesOnce(simulation);
    }

    [Test]
    public void MissingResourcePrefab_RetainsWholeChunkUntilAllTypesAreReady()
    {
        Entity settings = CreateSingleChunkSettings();
        _entityManager.GetBuffer<ResourceGenerationConfigElement>(settings).Add(
            new ResourceGenerationConfigElement(ItemTypeEnum.Copper_Ore, 1f, 2, 2, 1f, 100, 100));
        Entity database = _entityManager.CreateEntity(typeof(ResourcePrefabDatabase));
        _entityManager.AddBuffer<ResourcePrefabElement>(database);
        AddPrefab(database, ItemTypeEnum.Iron_Ore);
        var simulation = Simulation.CreateResourceGenerationPipeline();
        _world.GetOrCreateSystem<InitialChunkLoadBootstrapSystem>().Update(_world.Unmanaged);
        simulation.Update();
        simulation.Update();
        AssertWaitingForOrigin();

        AddPrefab(database, ItemTypeEnum.Copper_Ore);
        AssertCompletesOnce(simulation);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void InvalidPrefab_RetainsRequestUntilReferenceIsRepaired(bool destroyed)
    {
        CreateSingleChunkSettings();
        Entity invalidPrefab = _entityManager.CreateEntity();
        if (destroyed)
        {
            _entityManager.DestroyEntity(invalidPrefab);
        }
        Entity database = _entityManager.CreateEntity(typeof(ResourcePrefabDatabase));
        _entityManager.AddBuffer<ResourcePrefabElement>(database).Add(
            new ResourcePrefabElement(ItemTypeEnum.Iron_Ore, invalidPrefab));
        var simulation = Simulation.CreateResourceGenerationPipeline();
        _world.GetOrCreateSystem<InitialChunkLoadBootstrapSystem>().Update(_world.Unmanaged);
        simulation.Update();
        AssertWaitingForOrigin();
        _entityManager.GetBuffer<ResourcePrefabElement>(database).Clear();
        AddPrefab(database, ItemTypeEnum.Iron_Ore);
        AssertCompletesOnce(simulation);
    }

    [Test]
    public void EmptyChunk_IsCompletedOnlyAfterStateApplyPlayback()
    {
        CreateSingleChunkSettings(weight: 0f);
        Entity database = _entityManager.CreateEntity(typeof(ResourcePrefabDatabase));
        _entityManager.AddBuffer<ResourcePrefabElement>(database);
        var simulation = Simulation.CreateResourceGenerationPipeline();
        var probe = _world.GetOrCreateSystemManaged<ResourceGenerationBeforePlaybackProbe>();
        var command = _world.GetOrCreateSystemManaged<CommandGroup>();
        command.AddSystemToUpdateList(probe);
        command.SortSystems();
        _world.GetOrCreateSystem<InitialChunkLoadBootstrapSystem>().Update(_world.Unmanaged);
        simulation.Update();

        Assert.AreEqual(0, probe.CompletionCount, "EndCommand 전에는 완료 알림이 없어야 한다.");
        Assert.AreEqual(0, probe.ResourceCount);
        Entity trackerEntity = GetTrackerEntity();
        Assert.AreEqual(1, _entityManager.GetBuffer<GeneratedChunkCompletedElement>(trackerEntity).Length);
        Assert.AreEqual(0, _entityManager.GetComponentData<GeneratedChunkTracker>(trackerEntity).Map.Count());
        simulation.Update();
        var tracker = _entityManager.GetComponentData<GeneratedChunkTracker>(trackerEntity);
        Assert.AreEqual(1, tracker.Map.Count());
        Assert.AreEqual(0, tracker.Pending.Count());
        Assert.AreEqual(0, ResourceCount());
        Assert.AreEqual(0, _entityManager.GetBuffer<GeneratedChunkCompletedElement>(trackerEntity).Length);
    }

    [Test]
    public void MissingPlaybackSystem_DoesNotSpawnImmediatelyOrConsumeRequest()
    {
        SetupWorldWithPrefabs();
        _world.GetOrCreateSystem<InitialChunkLoadBootstrapSystem>().Update(_world.Unmanaged);
        _world.GetOrCreateSystem<ChunkLoadCommandSystem>().Update(_world.Unmanaged);
        _world.GetOrCreateSystem<ResourceGenerationCommandSystem>().Update(_world.Unmanaged);
        Assert.AreEqual(0, ResourceCount());
        Assert.AreEqual(9, _entityManager.GetBuffer<GeneratedChunkReadyElement>(GetTrackerEntity()).Length);
        Assert.AreEqual(0, _entityManager.GetBuffer<GeneratedChunkCompletedElement>(GetTrackerEntity()).Length);
    }

    private Entity CreateSingleChunkSettings(float weight = 1f)
    {
        Entity settings = _entityManager.CreateEntity(typeof(ResourceGenerationSettings));
        _entityManager.SetComponentData(settings, new ResourceGenerationSettings(12345u, 1));
        _entityManager.AddBuffer<ResourceGenerationConfigElement>(settings).Add(
            new ResourceGenerationConfigElement(ItemTypeEnum.Iron_Ore, weight, 2, 2, 1f, 100, 100));
        return settings;
    }

    private void AddPrefab(Entity database, ItemTypeEnum type)
    {
        Entity prefab = _entityManager.CreateEntity(typeof(Prefab), typeof(LocalTransform));
        _entityManager.SetComponentData(prefab, LocalTransform.Identity);
        _entityManager.GetBuffer<ResourcePrefabElement>(database).Add(new ResourcePrefabElement(type, prefab));
    }

    private Entity GetTrackerEntity()
        => _entityManager.CreateEntityQuery(typeof(GeneratedChunkTracker)).GetSingletonEntity();

    private int ResourceCount()
        => _entityManager.CreateEntityQuery(typeof(GridPosition), typeof(ResourceNode)).CalculateEntityCount();

    private void EnqueueOrigin()
    {
        Entity queue = _entityManager.CreateEntityQuery(typeof(ChunkLoadRequestQueue)).GetSingletonEntity();
        _entityManager.GetBuffer<ChunkLoadRequestElement>(queue).Add(new ChunkLoadRequestElement(int2.zero));
    }

    private void AssertWaitingForOrigin()
    {
        Entity entity = GetTrackerEntity();
        var tracker = _entityManager.GetComponentData<GeneratedChunkTracker>(entity);
        Assert.IsTrue(tracker.Pending.Contains(int2.zero));
        Assert.AreEqual(0, tracker.Map.Count());
        Assert.AreEqual(1, _entityManager.GetBuffer<GeneratedChunkReadyElement>(entity).Length);
        Assert.AreEqual(0, _entityManager.GetBuffer<GeneratedChunkCompletedElement>(entity).Length);
        Assert.AreEqual(0, ResourceCount());
    }

    private void AssertCompletesOnce(GameSimulationGroup simulation)
    {
        var probe = _world.GetOrCreateSystemManaged<ResourceGenerationBeforePlaybackProbe>();
        var command = _world.GetOrCreateSystemManaged<CommandGroup>();
        command.AddSystemToUpdateList(probe);
        command.SortSystems();
        EnqueueOrigin();
        simulation.Update();
        Assert.AreEqual(0, probe.ResourceCount, "실제 스폰은 Command 직후가 아니라 EndCommand Playback에서 반영되어야 한다.");
        Assert.AreEqual(0, probe.CompletionCount);
        int spawned = ResourceCount();
        Assert.Greater(spawned, 0);
        Entity trackerEntity = GetTrackerEntity();
        Assert.AreEqual(1, _entityManager.GetBuffer<GeneratedChunkCompletedElement>(trackerEntity).Length);
        Assert.AreEqual(0, _entityManager.GetBuffer<GeneratedChunkReadyElement>(trackerEntity).Length);
        Assert.AreEqual(0, _entityManager.GetComponentData<GeneratedChunkTracker>(trackerEntity).Map.Count());

        // 완료 알림 반영 전후에 재요청해도 이미 기록한 스폰을 반복하지 않는다.
        for (int frame = 0; frame < 2; frame++)
        {
            EnqueueOrigin();
            simulation.Update();
        }
        var tracker = _entityManager.GetComponentData<GeneratedChunkTracker>(trackerEntity);
        Assert.AreEqual(1, tracker.Map.Count());
        Assert.AreEqual(0, tracker.Pending.Count());
        Assert.AreEqual(spawned, ResourceCount());
        var fence = _entityManager.CreateEntityQuery(typeof(ResourceSpatialIndexFence)).GetSingletonRW<ResourceSpatialIndexFence>();
        fence.ValueRW.Complete();
        var index = _entityManager.CreateEntityQuery(typeof(ResourceSpatialIndex)).GetSingleton<ResourceSpatialIndex>();
        Assert.AreEqual(spawned, index.Map.Count(), "셀 중복이나 공간 등록 누락이 없어야 한다.");
    }
}

[DisableAutoCreation]
[UpdateInGroup(typeof(CommandGroup))]
[UpdateAfter(typeof(ResourceGenerationCommandSystem))]
[UpdateBefore(typeof(EndCommandEntityCommandBufferSystem))]
public partial class ResourceGenerationBeforePlaybackProbe : SystemBase
{
    public int ResourceCount { get; private set; }
    public int CompletionCount { get; private set; }

    protected override void OnUpdate()
    {
        ResourceCount = GetEntityQuery(typeof(GridPosition), typeof(ResourceNode)).CalculateEntityCount();
        Entity tracker = GetEntityQuery(typeof(GeneratedChunkTracker)).GetSingletonEntity();
        CompletionCount = EntityManager.GetBuffer<GeneratedChunkCompletedElement>(tracker).Length;
    }
}
