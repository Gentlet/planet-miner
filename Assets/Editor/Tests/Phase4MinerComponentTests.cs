using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Resource 및 Miner 컴포넌트 정의 및 공간 색인 인프라 검증 테스트.
/// ResourceNode, ResourceConfig, ResourceSpatialIndex, MinerState, MinerDecision, ProductResult의
/// 데이터 구조와 공간 색인 수명주기를 검증.
/// </summary>
public class Phase4MinerComponentTests : EcsWorldTestFixture
{
    private SystemHandle _resourceSpatialSyncHandle;
    private WorldInvariantValidationSystem _invariantValidationSystem;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();

        _resourceSpatialSyncHandle = _world.GetOrCreateSystem(typeof(ResourceSpatialSyncSystem));
        _invariantValidationSystem = _world.GetOrCreateSystemManaged<WorldInvariantValidationSystem>();
        _invariantValidationSystem.ResetViolationCount();
    }

    private Entity CreateResourceNode(int2 position, ItemTypeEnum resourceType, int amount)
        => Entities.CreateResourceNode(position, resourceType, amount);

    private void SyncResourceSpatialIndex()
    {
        _resourceSpatialSyncHandle.Update(_world.Unmanaged);
        var fence = _world.EntityManager.CreateEntityQuery(typeof(ResourceSpatialIndexFence)).GetSingletonRW<ResourceSpatialIndexFence>();
        fence.ValueRW.Complete();
    }

    [Test]
    public void Test01_ResourceNode_InitializationAndConfig()
    {
        // Arrange & Act: 자원 노드 엔티티 및 전역 ResourceConfig 싱글톤 생성
        var resourceEntity = CreateResourceNode(new int2(5, 10), ItemTypeEnum.Iron_Ore, 500);

        var configEntity = _entityManager.CreateEntity(typeof(ResourceConfig));
        _entityManager.SetComponentData(configEntity, new ResourceConfig(isResourceInfinite: true));

        // Assert
        var node = _entityManager.GetComponentData<ResourceNode>(resourceEntity);
        Assert.AreEqual(ItemTypeEnum.Iron_Ore, node.ResourceType, "Resource type should be Iron_Ore.");
        Assert.AreEqual(500, node.Amount, "Initial amount should be 500.");

        var config = _entityManager.GetComponentData<ResourceConfig>(configEntity);
        Assert.IsTrue(config.IsResourceInfinite, "ResourceConfig should indicate infinite resources.");
    }

    [Test]
    public void Test02_ResourceSpatialIndex_SyncAndLookup()
    {
        // Arrange: 2개의 자원 노드 배치
        var ironNode = CreateResourceNode(new int2(2, 3), ItemTypeEnum.Iron_Ore, 100);
        var copperNode = CreateResourceNode(new int2(4, 7), ItemTypeEnum.Copper_Ore, 200);

        // Act: Phase 6 공간 색인 동기화 실행
        SyncResourceSpatialIndex();

        // Invariant 시스템 실행 (검증)
        _invariantValidationSystem.Update();

        // Assert: $O(1)$ 공간 색인 정상 조회 및 Invariant 통과 확인
        var spatialIndex = _world.EntityManager.CreateEntityQuery(typeof(ResourceSpatialIndex)).GetSingleton<ResourceSpatialIndex>();
        Assert.IsTrue(spatialIndex.HasResourceAt(new int2(2, 3)), "Resource should exist at (2, 3).");
        Assert.IsTrue(spatialIndex.TryGetResource(new int2(2, 3), out Entity foundIron));
        Assert.AreEqual(ironNode, foundIron, "Found entity should match ironNode.");

        Assert.IsTrue(spatialIndex.HasResourceAt(new int2(4, 7)), "Resource should exist at (4, 7).");
        Assert.IsTrue(spatialIndex.TryGetResource(new int2(4, 7), out Entity foundCopper));
        Assert.AreEqual(copperNode, foundCopper, "Found entity should match copperNode.");

        Assert.IsFalse(spatialIndex.HasResourceAt(new int2(0, 0)), "No resource should exist at (0, 0).");
        Assert.AreEqual(0, _invariantValidationSystem.TotalViolationCount, "There should be 0 invariant violations.");
    }

    [Test]
    public void Test04_ClearAndRebuild_ReflectsResourceDepletion()
    {
        // Arrange: 자원 노드 생성 후 동기화
        var resourceEntity = CreateResourceNode(new int2(1, 1), ItemTypeEnum.Coal, 50);
        SyncResourceSpatialIndex();

        var spatialIndex = _world.EntityManager.CreateEntityQuery(typeof(ResourceSpatialIndex)).GetSingleton<ResourceSpatialIndex>();
        Assert.IsTrue(spatialIndex.HasResourceAt(new int2(1, 1)), "Resource should exist initially.");

        // Act: 자원 고갈 시뮬레이션 (엔티티 파괴 후 다음 프레임 동기화)
        _entityManager.DestroyEntity(resourceEntity);
        SyncResourceSpatialIndex();

        // Invariant 검증
        _invariantValidationSystem.Update();

        // Assert: 공간 인덱스에서 정상 제거되었는지 확인
        spatialIndex = _world.EntityManager.CreateEntityQuery(typeof(ResourceSpatialIndex)).GetSingleton<ResourceSpatialIndex>();
        Assert.IsFalse(spatialIndex.HasResourceAt(new int2(1, 1)), "Resource should no longer exist after destruction.");
        Assert.AreEqual(0, _invariantValidationSystem.TotalViolationCount, "No invariant violations after resource removal.");
    }
}
