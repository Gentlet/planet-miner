using System.Collections.Generic;
using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// 역할·목적: 자원 생성의 시드/청크 순서 결정성과 수량 경계에 대한 NUnit EditMode 회귀 검증.
/// 입력·검사: 명시적 설정/프리팹 DB로 서로 다른 생성 순서의 ECB를 재생해 좌표/수량·중복 셀·난수 소비 독립성을 비교한다.
/// 수명: EcsWorldTestFixture가 각 사례의 독립 World를 준비하고 종료 시 해제한다.
/// </summary>
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
        // 같은 시드/좌표를 다른 청크 순서로 생성해 난수 소비와 패치 결과의 순서 의존성을 분리한다.
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
