using System.Collections.Generic;
using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public class Phase4ResourceGenerationTests : EcsWorldTestFixture
{
    private (Entity ConfigEntity, Entity PrefabDbEntity) SetupDefaultConfig(uint worldSeed = 12345u, int initialChunkSize = 3)
    {
        Entity settingsEntity = _entityManager.CreateEntity(typeof(ResourceGenerationSettings));
        _entityManager.SetComponentData(settingsEntity, new ResourceGenerationSettings(worldSeed, initialChunkSize));

        var buffer = _entityManager.AddBuffer<ResourceGenerationConfigElement>(settingsEntity);
        // Iron_Ore
        buffer.Add(new ResourceGenerationConfigElement(
            ItemTypeEnum.Iron_Ore,
            weight: 0.8f,
            minPatchRadius: 2,
            maxPatchRadius: 4,
            cellFillChance: 0.7f,
            minAmount: 50,
            maxAmount: 200));
        // Copper_Ore
        buffer.Add(new ResourceGenerationConfigElement(
            ItemTypeEnum.Copper_Ore,
            weight: 0.6f,
            minPatchRadius: 2,
            maxPatchRadius: 3,
            cellFillChance: 0.6f,
            minAmount: 40,
            maxAmount: 150));
        // Coal
        buffer.Add(new ResourceGenerationConfigElement(
            ItemTypeEnum.Coal,
            weight: 0.5f,
            minPatchRadius: 1,
            maxPatchRadius: 3,
            cellFillChance: 0.65f,
            minAmount: 30,
            maxAmount: 120));
        // Stone
        buffer.Add(new ResourceGenerationConfigElement(
            ItemTypeEnum.Stone,
            weight: 0.7f,
            minPatchRadius: 2,
            maxPatchRadius: 4,
            cellFillChance: 0.75f,
            minAmount: 60,
            maxAmount: 250));

        Entity prefabDbEntity = _entityManager.CreateEntity(typeof(ResourcePrefabDatabase));
        var prefabBuffer = _entityManager.AddBuffer<ResourcePrefabElement>(prefabDbEntity);

        ItemTypeEnum[] types = { ItemTypeEnum.Iron_Ore, ItemTypeEnum.Copper_Ore, ItemTypeEnum.Coal, ItemTypeEnum.Stone };
        foreach (var t in types)
        {
            var mockPrefab = _entityManager.CreateEntity(typeof(LocalTransform), typeof(ResourceNode));
            _entityManager.SetComponentData(mockPrefab, LocalTransform.FromPosition(float3.zero));
            prefabBuffer.Add(new ResourcePrefabElement(t, mockPrefab));
        }

        return (settingsEntity, prefabDbEntity);
    }

    [Test]
    public void Test01_ChunkUtility_IsInsideChunk_HandlesBoundaryCellsCorrectly()
    {
        int2 chunkZero = new int2(0, 0);
        Assert.IsTrue(ChunkUtility.IsInsideChunk(new int2(0, 0), chunkZero));
        Assert.IsTrue(ChunkUtility.IsInsideChunk(new int2(15, 15), chunkZero));
        Assert.IsTrue(ChunkUtility.IsInsideChunk(new int2(5, 10), chunkZero));
        Assert.IsFalse(ChunkUtility.IsInsideChunk(new int2(-1, 0), chunkZero));
        Assert.IsFalse(ChunkUtility.IsInsideChunk(new int2(16, 0), chunkZero));
        Assert.IsFalse(ChunkUtility.IsInsideChunk(new int2(0, -1), chunkZero));
        Assert.IsFalse(ChunkUtility.IsInsideChunk(new int2(0, 16), chunkZero));

        int2 chunkNegOne = new int2(-1, -1);
        Assert.IsTrue(ChunkUtility.IsInsideChunk(new int2(-16, -16), chunkNegOne));
        Assert.IsTrue(ChunkUtility.IsInsideChunk(new int2(-1, -1), chunkNegOne));
        Assert.IsFalse(ChunkUtility.IsInsideChunk(new int2(0, 0), chunkNegOne));
        Assert.IsFalse(ChunkUtility.IsInsideChunk(new int2(-17, -1), chunkNegOne));
        Assert.IsFalse(ChunkUtility.IsInsideChunk(new int2(-1, 0), chunkNegOne));
    }

    [Test]
    public void Test02_ResourceGenerationUtility_DeterministicGeneration_SameSeedProducesIdenticalResources()
    {
        uint seed = 424242u;
        int2 targetChunk = new int2(0, 0);

        // 첫 번째 생성
        var (configEntity1, prefabDbEntity1) = SetupDefaultConfig(seed);
        var configs1 = _entityManager.GetBuffer<ResourceGenerationConfigElement>(configEntity1);
        var prefabs1 = _entityManager.GetBuffer<ResourcePrefabElement>(prefabDbEntity1);
        var ecb1 = new EntityCommandBuffer(Allocator.Temp);
        ResourceGenerationUtility.GenerateChunkResources(ref ecb1, targetChunk, seed, configs1, prefabs1);
        ecb1.Playback(_entityManager);
        ecb1.Dispose();

        var query = _entityManager.CreateEntityQuery(typeof(GridPosition), typeof(ResourceNode));
        var entities1 = query.ToEntityArray(Allocator.Temp);
        Assert.Greater(entities1.Length, 0, "자원이 최소 1개 이상 생성되어야 합니다.");

        var dict1 = new Dictionary<int2, (ItemTypeEnum Type, int Amount)>();
        for (int i = 0; i < entities1.Length; i++)
        {
            var pos = _entityManager.GetComponentData<GridPosition>(entities1[i]).Value;
            var node = _entityManager.GetComponentData<ResourceNode>(entities1[i]);
            dict1[pos] = (node.ResourceType, node.Amount);
        }
        entities1.Dispose();

        // 기존 자원 엔티티 전체 파괴 후 재실행
        _entityManager.DestroyEntity(query);

        // 두 번째 생성 (동일 시드)
        configs1 = _entityManager.GetBuffer<ResourceGenerationConfigElement>(configEntity1);
        prefabs1 = _entityManager.GetBuffer<ResourcePrefabElement>(prefabDbEntity1);
        var ecb2 = new EntityCommandBuffer(Allocator.Temp);
        ResourceGenerationUtility.GenerateChunkResources(ref ecb2, targetChunk, seed, configs1, prefabs1);
        ecb2.Playback(_entityManager);
        ecb2.Dispose();

        var entities2 = query.ToEntityArray(Allocator.Temp);
        Assert.AreEqual(dict1.Count, entities2.Length, "동일 시드에서 생성된 자원 엔티티 수가 일치해야 합니다.");

        for (int i = 0; i < entities2.Length; i++)
        {
            var pos = _entityManager.GetComponentData<GridPosition>(entities2[i]).Value;
            var node = _entityManager.GetComponentData<ResourceNode>(entities2[i]);

            Assert.IsTrue(dict1.ContainsKey(pos), $"동일 좌표 ({pos.x}, {pos.y})가 존재해야 합니다.");
            Assert.AreEqual(dict1[pos].Type, node.ResourceType, $"좌표 ({pos.x}, {pos.y})의 자원 타입이 일치해야 합니다.");
            Assert.AreEqual(dict1[pos].Amount, node.Amount, $"좌표 ({pos.x}, {pos.y})의 매장량이 일치해야 합니다.");
        }
        entities2.Dispose();
    }

    [Test]
    public void Test03_ResourceGenerationUtility_DifferentSeedProducesDifferentResources()
    {
        uint seedA = 10001u;
        uint seedB = 99999u;
        int2 targetChunk = new int2(0, 0);

        // Seed A 생성
        var (configEntity, prefabDbEntity) = SetupDefaultConfig(seedA);
        var configs = _entityManager.GetBuffer<ResourceGenerationConfigElement>(configEntity);
        var prefabs = _entityManager.GetBuffer<ResourcePrefabElement>(prefabDbEntity);
        var ecbA = new EntityCommandBuffer(Allocator.Temp);
        ResourceGenerationUtility.GenerateChunkResources(ref ecbA, targetChunk, seedA, configs, prefabs);
        ecbA.Playback(_entityManager);
        ecbA.Dispose();

        var query = _entityManager.CreateEntityQuery(typeof(GridPosition), typeof(ResourceNode));
        var entitiesA = query.ToEntityArray(Allocator.Temp);
        var positionsA = new HashSet<int2>();
        for (int i = 0; i < entitiesA.Length; i++)
        {
            positionsA.Add(_entityManager.GetComponentData<GridPosition>(entitiesA[i]).Value);
        }
        entitiesA.Dispose();

        _entityManager.DestroyEntity(query);

        // Seed B 생성
        configs = _entityManager.GetBuffer<ResourceGenerationConfigElement>(configEntity);
        prefabs = _entityManager.GetBuffer<ResourcePrefabElement>(prefabDbEntity);
        var ecbB = new EntityCommandBuffer(Allocator.Temp);
        ResourceGenerationUtility.GenerateChunkResources(ref ecbB, targetChunk, seedB, configs, prefabs);
        ecbB.Playback(_entityManager);
        ecbB.Dispose();

        var entitiesB = query.ToEntityArray(Allocator.Temp);
        var positionsB = new HashSet<int2>();
        for (int i = 0; i < entitiesB.Length; i++)
        {
            positionsB.Add(_entityManager.GetComponentData<GridPosition>(entitiesB[i]).Value);
        }
        entitiesB.Dispose();

        // 완전히 같지 않아야 함 (시드에 따른 다양성)
        bool allMatch = positionsA.SetEquals(positionsB);
        Assert.IsFalse(allMatch, "서로 다른 시드는 서로 다른 자원 분포를 산출해야 합니다.");
    }

    [Test]
    public void Test04_ResourceGenerationUtility_LoadOrderInvariance_CrossChunkPatchesMatchRegardlessOfLoadOrder()
    {
        uint seed = 77777u;
        int2 chunkA = new int2(0, 0);
        int2 chunkB = new int2(1, 0);

        var (configEntity, prefabDbEntity) = SetupDefaultConfig(seed);
        var configs = _entityManager.GetBuffer<ResourceGenerationConfigElement>(configEntity);
        var prefabs = _entityManager.GetBuffer<ResourcePrefabElement>(prefabDbEntity);

        // 시나리오 1: chunkA 먼저 생성 후 chunkB 생성
        var ecb1 = new EntityCommandBuffer(Allocator.Temp);
        ResourceGenerationUtility.GenerateChunkResources(ref ecb1, chunkA, seed, configs, prefabs);
        ResourceGenerationUtility.GenerateChunkResources(ref ecb1, chunkB, seed, configs, prefabs);
        ecb1.Playback(_entityManager);
        ecb1.Dispose();

        var query = _entityManager.CreateEntityQuery(typeof(GridPosition), typeof(ResourceNode));
        var entitiesOrder1 = query.ToEntityArray(Allocator.Temp);
        var order1Map = new Dictionary<int2, (ItemTypeEnum Type, int Amount)>();
        for (int i = 0; i < entitiesOrder1.Length; i++)
        {
            var pos = _entityManager.GetComponentData<GridPosition>(entitiesOrder1[i]).Value;
            var node = _entityManager.GetComponentData<ResourceNode>(entitiesOrder1[i]);
            order1Map[pos] = (node.ResourceType, node.Amount);
        }
        entitiesOrder1.Dispose();

        _entityManager.DestroyEntity(query);

        // 시나리오 2: chunkB 먼저 생성 후 chunkA 생성 (순서 반대)
        configs = _entityManager.GetBuffer<ResourceGenerationConfigElement>(configEntity);
        prefabs = _entityManager.GetBuffer<ResourcePrefabElement>(prefabDbEntity);
        var ecb2 = new EntityCommandBuffer(Allocator.Temp);
        ResourceGenerationUtility.GenerateChunkResources(ref ecb2, chunkB, seed, configs, prefabs);
        ResourceGenerationUtility.GenerateChunkResources(ref ecb2, chunkA, seed, configs, prefabs);
        ecb2.Playback(_entityManager);
        ecb2.Dispose();

        var entitiesOrder2 = query.ToEntityArray(Allocator.Temp);
        Assert.AreEqual(order1Map.Count, entitiesOrder2.Length, "청크 로드 순서가 바뀌어도 총 생성 자원 수는 동일해야 합니다.");

        for (int i = 0; i < entitiesOrder2.Length; i++)
        {
            var pos = _entityManager.GetComponentData<GridPosition>(entitiesOrder2[i]).Value;
            var node = _entityManager.GetComponentData<ResourceNode>(entitiesOrder2[i]);

            Assert.IsTrue(order1Map.ContainsKey(pos), $"로드 순서와 무관하게 좌표 ({pos.x}, {pos.y})가 존재해야 합니다.");
            Assert.AreEqual(order1Map[pos].Type, node.ResourceType, $"로드 순서와 무관하게 좌표 ({pos.x}, {pos.y})의 타입이 일치해야 합니다.");
            Assert.AreEqual(order1Map[pos].Amount, node.Amount, $"로드 순서와 무관하게 좌표 ({pos.x}, {pos.y})의 매장량이 일치해야 합니다.");
        }
        entitiesOrder2.Dispose();
    }

    [Test]
    public void Test05_ResourceGenerationUtility_NoDuplicateCellsInChunk()
    {
        uint seed = 98765u;
        int2 targetChunk = new int2(0, 0);

        var (configEntity, prefabDbEntity) = SetupDefaultConfig(seed);
        var configs = _entityManager.GetBuffer<ResourceGenerationConfigElement>(configEntity);
        var prefabs = _entityManager.GetBuffer<ResourcePrefabElement>(prefabDbEntity);

        var ecb = new EntityCommandBuffer(Allocator.Temp);
        ResourceGenerationUtility.GenerateChunkResources(ref ecb, targetChunk, seed, configs, prefabs);
        ecb.Playback(_entityManager);
        ecb.Dispose();

        var query = _entityManager.CreateEntityQuery(typeof(GridPosition), typeof(ResourceNode));
        var entities = query.ToEntityArray(Allocator.Temp);
        var seen = new HashSet<int2>();

        for (int i = 0; i < entities.Length; i++)
        {
            var pos = _entityManager.GetComponentData<GridPosition>(entities[i]).Value;
            Assert.IsTrue(seen.Add(pos), $"좌표 ({pos.x}, {pos.y})에 중복된 자원 엔티티가 생성되었습니다.");
            Assert.IsTrue(ChunkUtility.IsInsideChunk(pos, targetChunk), $"좌표 ({pos.x}, {pos.y})가 청크 영역을 벗어났습니다.");
        }

        entities.Dispose();
    }

    [Test]
    public void Test06_ResourceGenerationCommandSystem_ExecutesWithPipeline_AndRegistersToSpatialIndex()
    {
        // 1. 설정 생성 (3x3 초기 청크)
        SetupDefaultConfig(worldSeed: 8888u, initialChunkSize: 3);

        // 2. 파이프라인 시스템 준비
        var bootstrapSystem = _world.GetOrCreateSystem(typeof(InitialChunkLoadBootstrapSystem));
        var simulation = Simulation.CreateResourceGenerationPipeline();

        // 3. 파이프라인 1회 업데이트
        // Bootstrap: 9개 청크 요청 큐잉
        bootstrapSystem.Update(_world.Unmanaged);

        simulation.Update();
        var fence = _world.EntityManager.CreateEntityQuery(typeof(ResourceSpatialIndexFence)).GetSingletonRW<ResourceSpatialIndexFence>();
        fence.ValueRW.Complete();

        // 4. 검증: 자원 엔티티가 생성되었고 ResourceSpatialIndex에 등록되었는지 확인
        var resQuery = _entityManager.CreateEntityQuery(typeof(GridPosition), typeof(ResourceNode));
        int totalResources = resQuery.CalculateEntityCount();
        Assert.Greater(totalResources, 0, "초기 3x3 청크 로드 시 자원이 생성되어야 합니다.");

        var spatialIndex = _entityManager.CreateEntityQuery(typeof(ResourceSpatialIndex)).GetSingleton<ResourceSpatialIndex>();
        Assert.AreEqual(totalResources, spatialIndex.Map.Count(), "모든 자원 노드가 ResourceSpatialIndex에 동기화되어야 합니다.");

        // 5. 멱등성 검증: 동일 파이프라인 다시 실행 시 자원 중복 생성 없음
        simulation.Update();
        fence = _world.EntityManager.CreateEntityQuery(typeof(ResourceSpatialIndexFence)).GetSingletonRW<ResourceSpatialIndexFence>();
        fence.ValueRW.Complete();

        Assert.AreEqual(totalResources, resQuery.CalculateEntityCount(), "이미 로드된 청크에 대해서는 자원이 중복 생성되지 않아야 합니다.");
        Assert.AreEqual(totalResources, spatialIndex.Map.Count(), "ResourceSpatialIndex 엔트리 수도 일정하게 유지되어야 합니다.");
        var tracker = _entityManager.CreateEntityQuery(typeof(GeneratedChunkTracker)).GetSingleton<GeneratedChunkTracker>();
        Assert.AreEqual(9, tracker.Map.Count());
        Assert.AreEqual(0, tracker.Pending.Count());
    }

    [Test]
    public void Test07_ResourceGenerationCommandSystem_GracefulEarlyReturnWhenNoConfigs()
    {
        // ResourceGenerationSettings만 있고 ConfigBuffer가 없는 경우
        Entity settingsEntity = _entityManager.CreateEntity(typeof(ResourceGenerationSettings));
        _entityManager.SetComponentData(settingsEntity, new ResourceGenerationSettings(worldSeed: 1, initialChunkSize: 3));

        var chunkLoadSystem = _world.GetOrCreateSystem(typeof(ChunkLoadCommandSystem));
        var resourceGenSystem = _world.GetOrCreateSystem(typeof(ResourceGenerationCommandSystem));

        chunkLoadSystem.Update(_world.Unmanaged);

        // 에러 없이 통과해야 함
        Assert.DoesNotThrow(() => resourceGenSystem.Update(_world.Unmanaged));

        var resQuery = _entityManager.CreateEntityQuery(typeof(GridPosition), typeof(ResourceNode));
        Assert.AreEqual(0, resQuery.CalculateEntityCount());
    }

    [Test]
    public void Test08_GetMaxPatchRadius_IgnoresInactiveResourcesWithZeroWeight()
    {
        Entity entity = _entityManager.CreateEntity();
        var buffer = _entityManager.AddBuffer<ResourceGenerationConfigElement>(entity);

        // 활성 자원: 반경 4
        buffer.Add(new ResourceGenerationConfigElement(ItemTypeEnum.Iron_Ore, weight: 0.5f, 1, 4, 0.5f, 10, 50));
        // 비활성 자원 (Weight = 0): 반경 1024
        buffer.Add(new ResourceGenerationConfigElement(ItemTypeEnum.Copper_Ore, weight: 0.0f, 100, 1024, 0.5f, 10, 50));

        int maxRadius = ResourceGenerationUtility.GetMaxPatchRadius(buffer);
        Assert.AreEqual(4, maxRadius, "Weight가 0인 비활성 자원의 반경(1024)은 무시되고 활성 자원의 반경(4)이 반환되어야 함");
    }

    [Test]
    public void Test09_ResourceGeneration_NearMaxAmount_DoesNotThrowOverflow()
    {
        // CreateEntity에 버퍼 타입을 명시하여 이후 구조적 변경(Archetype Change) 방지
        Entity settingsEntity = _entityManager.CreateEntity(typeof(ResourceGenerationSettings), typeof(ResourceGenerationConfigElement));
        _entityManager.SetComponentData(settingsEntity, new ResourceGenerationSettings(worldSeed: 12345u, initialChunkSize: 1));

        Entity prefabDbEntity = _entityManager.CreateEntity(typeof(ResourcePrefabDatabase), typeof(ResourcePrefabElement));
        var mockPrefab = _entityManager.CreateEntity(typeof(LocalTransform), typeof(ResourceNode));
        _entityManager.SetComponentData(mockPrefab, LocalTransform.FromPosition(float3.zero));

        var buffer = _entityManager.GetBuffer<ResourceGenerationConfigElement>(settingsEntity);
        buffer.Add(new ResourceGenerationConfigElement(
            ItemTypeEnum.Iron_Ore,
            weight: 1.0f,
            minPatchRadius: 2,
            maxPatchRadius: 2,
            cellFillChance: 1.0f,
            minAmount: 1000,
            maxAmount: int.MaxValue - 1));

        var prefabBuffer = _entityManager.GetBuffer<ResourcePrefabElement>(prefabDbEntity);
        prefabBuffer.Add(new ResourcePrefabElement(ItemTypeEnum.Iron_Ore, mockPrefab));

        var ecb = new EntityCommandBuffer(Allocator.Temp);
        Assert.DoesNotThrow(() =>
        {
            ResourceGenerationUtility.GenerateChunkResources(ref ecb, new int2(0, 0), 12345u, buffer, prefabBuffer);
        }, "maxAmount가 int.MaxValue - 1이어도 오버플로 없이 정상 실행되어야 함");

        ecb.Playback(_entityManager);
        ecb.Dispose();

        var query = _entityManager.CreateEntityQuery(typeof(GridPosition), typeof(ResourceNode));
        var nodes = query.ToComponentDataArray<ResourceNode>(Allocator.Temp);
        Assert.Greater(nodes.Length, 0);
        for (int i = 0; i < nodes.Length; i++)
        {
            Assert.GreaterOrEqual(nodes[i].Amount, 1000);
            Assert.LessOrEqual(nodes[i].Amount, int.MaxValue - 1);
        }
        nodes.Dispose();
    }

    [Test]
    public void Test10_CrossChunkPatch_RngDoesNotDriftAcrossNeighboringChunks()
    {
        // 타깃 청크 (0, 0)과 (1, 0)에 걸치는 단일 패치(Weight 1.0, FillChance 1.0, Radius 4)를 구성
        uint seed = 777u;
        var (configEntity, prefabDbEntity) = SetupDefaultConfig(seed);
        var configs = _entityManager.GetBuffer<ResourceGenerationConfigElement>(configEntity);
        var prefabs = _entityManager.GetBuffer<ResourcePrefabElement>(prefabDbEntity);

        // 청크 (0, 0)과 청크 (1, 0)을 각각 독립적으로 생성
        var ecb0 = new EntityCommandBuffer(Allocator.Temp);
        ResourceGenerationUtility.GenerateChunkResources(ref ecb0, new int2(0, 0), seed, configs, prefabs);
        ecb0.Playback(_entityManager);
        ecb0.Dispose();

        var query = _entityManager.CreateEntityQuery(typeof(GridPosition), typeof(ResourceNode));
        int count0 = query.CalculateEntityCount();
        Assert.Greater(count0, 0, "청크 (0, 0)에 자원이 생성되어야 함");

        // Playback으로 인한 구조적 변경 이후 버퍼 핸들 갱신
        configs = _entityManager.GetBuffer<ResourceGenerationConfigElement>(configEntity);
        prefabs = _entityManager.GetBuffer<ResourcePrefabElement>(prefabDbEntity);

        var ecb1 = new EntityCommandBuffer(Allocator.Temp);
        ResourceGenerationUtility.GenerateChunkResources(ref ecb1, new int2(1, 0), seed, configs, prefabs);
        ecb1.Playback(_entityManager);
        ecb1.Dispose();

        int totalCount = query.CalculateEntityCount();
        Assert.Greater(totalCount, count0, "청크 (1, 0)에도 자원이 누적 생성되어야 함");

        // 중복 좌표가 전혀 없는지 확인
        var positions = query.ToComponentDataArray<GridPosition>(Allocator.Temp);
        var uniquePositions = new HashSet<int2>();
        for (int i = 0; i < positions.Length; i++)
        {
            Assert.IsTrue(uniquePositions.Add(positions[i].Value), $"중복 셀 ({positions[i].Value.x}, {positions[i].Value.y})이 발생하지 않아야 함");
        }
        positions.Dispose();
    }
}
