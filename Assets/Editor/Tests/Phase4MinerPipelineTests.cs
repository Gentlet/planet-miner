using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 채굴 판단/진행·자원 소비·생산 결과의 실물 생성에 대한 NUnit EditMode 회귀 검증.
/// 입력·검사: 자원/채굴기/버퍼/시간 입력으로 종류 충돌·무한 자원·스택 경계·delta time 상한을 검사한다.
/// 수명: EcsWorldTestFixture가 각 사례의 독립 World를 준비하고 종료 시 해제한다.
/// </summary>
public class Phase4MinerPipelineTests : EcsWorldTestFixture
{
    private SystemHandle _resourceSpatialSyncHandle;
    private SystemHandle _beltSpatialSyncHandle;
    private SystemHandle _itemSpatialSyncHandle;
    private SystemHandle _minerDecisionHandle;
    private SystemHandle _minerExecutionHandle;
    private SystemHandle _storageApplyHandle;
    private SystemHandle _lifecycleHandle;
    private SystemHandle _ownershipHandle;
    private EndBuildingEntityCommandBufferSystem _endStateApplyEcb;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        CreateGameplayPrefabDatabases();

        _resourceSpatialSyncHandle = _world.GetOrCreateSystem(typeof(ResourceSpatialSyncSystem));
        _beltSpatialSyncHandle = _world.GetOrCreateSystem(typeof(BeltSpatialSyncSystem));
        _itemSpatialSyncHandle = _world.GetOrCreateSystem(typeof(ItemSpatialSyncSystem));
        _minerDecisionHandle = _world.GetOrCreateSystem(typeof(MinerDecisionSystem));
        _minerExecutionHandle = _world.GetOrCreateSystem(typeof(MinerExecutionSystem));
        _storageApplyHandle = _world.GetOrCreateSystem(typeof(BuildingItemStorageApplySystem));
        _lifecycleHandle = _world.GetOrCreateSystem(typeof(ItemLifecycleApplySystem));
        _ownershipHandle = _world.GetOrCreateSystem(typeof(ItemOwnershipApplySystem));
        _endStateApplyEcb = _world.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>();
    }

    private Entity CreateResourceNode(int2 position, ItemTypeEnum resourceType, int amount)
        => Entities.CreateResourceNode(position, resourceType, amount);

    private Entity CreateMiner(int2 position, int2 size, DirectionEnum direction, float miningSpeed = 1.0f, float progress = 0.0f)
        => Entities.CreateMiner(position, size, direction, miningSpeed, progress);

    private Entity CreateBelt(int2 position, DirectionEnum direction, float speed = 2.0f)
        => Entities.CreateBelt(position, direction, speed);

    private void SyncAllSpatialIndices()
    {
        _resourceSpatialSyncHandle.Update(_world.Unmanaged);
        var resFence = _world.EntityManager.CreateEntityQuery(typeof(ResourceSpatialIndexFence)).GetSingletonRW<ResourceSpatialIndexFence>();
        resFence.ValueRW.Complete();

        _beltSpatialSyncHandle.Update(_world.Unmanaged);
        var beltFence = _world.EntityManager.CreateEntityQuery(typeof(BeltSpatialIndexFence)).GetSingletonRW<BeltSpatialIndexFence>();
        beltFence.ValueRW.Complete();

        _itemSpatialSyncHandle.Update(_world.Unmanaged);
        var itemFence = _world.EntityManager.CreateEntityQuery(typeof(ItemSpatialIndexFence)).GetSingletonRW<ItemSpatialIndexFence>();
        itemFence.ValueRW.Complete();
    }

    private void RunStateApplyPhase()
    {
        // 생산 결과 소비·실물 생성·소유권의 ECB 경계를 진행한다. Execution만으로 실체화를 검사하지 않는다.
        // 1. 직전 Phase에서 기록된 구조적 변경 반영
        _endStateApplyEcb.Update();

        // 2. 창고/건물 출고 및 입고 적용
        _storageApplyHandle.Update(_world.Unmanaged);

        // 3. ProductResult / SpawnItemRequest 소비 및 소유권 적용
        _lifecycleHandle.Update(_world.Unmanaged);
        _ownershipHandle.Update(_world.Unmanaged);

        // 4. 아이템 엔티티 실체화 및 버퍼 적재 완료
        _endStateApplyEcb.Update();

        ref var storageState = ref _world.Unmanaged.ResolveSystemStateRef(_storageApplyHandle);
        storageState.Dependency.Complete();
    }

    [Test]
    public void Test02_MinerDecision_NoResourceOrFullBuffer_DisablesDecision()
    {
        // Case A: 자원이 없음
        var minerNoRes = CreateMiner(new int2(10, 10), new int2(1, 1), DirectionEnum.Up, 1.0f);
        SyncAllSpatialIndices();
        _minerDecisionHandle.Update(_world.Unmanaged);

        Assert.IsFalse(_entityManager.IsComponentEnabled<MinerDecision>(minerNoRes));
        var decisionNoRes = _entityManager.GetComponentData<MinerDecision>(minerNoRes);
        Assert.IsFalse(decisionNoRes.CanMine);

        // Case B: 자원은 있으나 내부 버퍼가 이미 꽉 참 (1스택 한도 = 50개)
        var resEntity = CreateResourceNode(new int2(20, 20), ItemTypeEnum.Iron_Ore, 50);
        var minerFull = CreateMiner(new int2(20, 20), new int2(1, 1), DirectionEnum.Up, 1.0f);
        var prodBuffer = _entityManager.GetBuffer<ProductItemElement>(minerFull);
        for (int i = 0; i < 50; i++)
        {
            var dummyItem = _entityManager.CreateEntity(typeof(ItemIdentity), typeof(ItemOwnership));
            prodBuffer.Add(new ProductItemElement(dummyItem, ItemTypeEnum.Iron_Ore, 0));
        }

        SyncAllSpatialIndices();
        _minerDecisionHandle.Update(_world.Unmanaged);

        Assert.IsFalse(_entityManager.IsComponentEnabled<MinerDecision>(minerFull), "MinerDecision should be disabled when buffer is full.");
        var decisionFull = _entityManager.GetComponentData<MinerDecision>(minerFull);
        Assert.IsFalse(decisionFull.CanMine);
    }

    [Test]
    public void Test05_InfiniteResourceMode_DoesNotDecrementAmount()
    {
        var configEntity = _entityManager.CreateEntity(typeof(ResourceConfig));
        _entityManager.SetComponentData(configEntity, new ResourceConfig(isResourceInfinite: true));

        var resEntity = CreateResourceNode(new int2(10, 10), ItemTypeEnum.Copper_Ore, 5);
        var minerEntity = CreateMiner(new int2(10, 10), new int2(1, 1), DirectionEnum.Up, miningSpeed: 1.0f, progress: 0.95f);

        SyncAllSpatialIndices();
        _minerDecisionHandle.Update(_world.Unmanaged);

        _world.SetTime(new Unity.Core.TimeData(0.1, 0.1f));
        _minerExecutionHandle.Update(_world.Unmanaged);
        RunStateApplyPhase();

        var resNode = _entityManager.GetComponentData<ResourceNode>(resEntity);
        Assert.AreEqual(5, resNode.Amount, "Resource amount should NOT be decremented in infinite mode.");
    }

    [Test]
    public void Test08_MinerDecision_DifferentProductType_BlocksUntilBufferIsEmpty()
    {
        CreateResourceNode(new int2(10, 10), ItemTypeEnum.Copper_Ore, 50);
        var minerEntity = CreateMiner(new int2(10, 10), new int2(1, 1), DirectionEnum.Up, 1.0f);

        var productBuffer = _entityManager.GetBuffer<ProductItemElement>(minerEntity);
        var dummyIron = _entityManager.CreateEntity(typeof(ItemIdentity), typeof(ItemOwnership));
        productBuffer.Add(new ProductItemElement(dummyIron, ItemTypeEnum.Iron_Ore, 0));

        SyncAllSpatialIndices();
        _minerDecisionHandle.Update(_world.Unmanaged);

        Assert.IsFalse(
            _entityManager.IsComponentEnabled<MinerDecision>(minerEntity),
            "Miner should not mine Copper while Iron remains in its single-stack ProductBuffer.");

        productBuffer.Clear();
        _minerDecisionHandle.Update(_world.Unmanaged);

        Assert.IsTrue(
            _entityManager.IsComponentEnabled<MinerDecision>(minerEntity),
            "Miner should resume mining after the previous product type is fully drained.");
    }

    [Test]
    public void Test09_ProductResult_StateApplyFillsLastStackSlotWithoutOverflowOrDuplicateConsumption()
    {
        CreateResourceNode(new int2(10, 10), ItemTypeEnum.Iron_Ore, 10);
        var minerEntity = CreateMiner(
            new int2(10, 10),
            new int2(1, 1),
            DirectionEnum.Up,
            miningSpeed: 1.0f,
            progress: 0.95f);

        var productBuffer = _entityManager.GetBuffer<ProductItemElement>(minerEntity);
        for (int i = 0; i < 49; i++)
        {
            var dummyItem = _entityManager.CreateEntity(typeof(ItemIdentity), typeof(ItemOwnership));
            productBuffer.Add(new ProductItemElement(dummyItem, ItemTypeEnum.Iron_Ore, 0));
        }

        SyncAllSpatialIndices();
        _minerDecisionHandle.Update(_world.Unmanaged);
        Assert.IsTrue(_entityManager.IsComponentEnabled<MinerDecision>(minerEntity));

        _world.SetTime(new Unity.Core.TimeData(0.1, 0.1f));
        _minerExecutionHandle.Update(_world.Unmanaged);

        var productResults = _entityManager.GetBuffer<ProductResult>(minerEntity);
        Assert.AreEqual(1, productResults.Length, "Exactly one production result should be pending before StateApply.");
        Assert.AreEqual(49, productBuffer.Length, "ProductBuffer must not change before StateApply.");

        RunStateApplyPhase();

        productResults = _entityManager.GetBuffer<ProductResult>(minerEntity);
        productBuffer = _entityManager.GetBuffer<ProductItemElement>(minerEntity);
        Assert.AreEqual(0, productResults.Length, "ProductResult must be consumed during StateApply.");
        Assert.AreEqual(50, productBuffer.Length, "StateApply should fill exactly the final stack slot.");

        // 소비된 ProductResult를 다시 처리해 중복 아이템을 생성금지.
        RunStateApplyPhase();
        productBuffer = _entityManager.GetBuffer<ProductItemElement>(minerEntity);
        Assert.AreEqual(50, productBuffer.Length, "A consumed ProductResult must never create a duplicate item.");

        SyncAllSpatialIndices();
        _minerDecisionHandle.Update(_world.Unmanaged);

        Assert.IsFalse(
            _entityManager.IsComponentEnabled<MinerDecision>(minerEntity),
            "Miner must stop after the single ProductBuffer stack reaches MaxStack.");
    }

    [Test]
    public void Test10_MinerExecution_LargeDeltaTime_ClampsToMaxSimulationDeltaTime()
    {
        // Arrange: 채굴 속도 1.0f, 초기 진행도 0.0f
        CreateResourceNode(new int2(5, 5), ItemTypeEnum.Iron_Ore, 100);
        var minerEntity = CreateMiner(
            new int2(5, 5),
            new int2(1, 1),
            DirectionEnum.Up,
            miningSpeed: 1.0f,
            progress: 0.0f);

        SyncAllSpatialIndices();
        _minerDecisionHandle.Update(_world.Unmanaged);
        Assert.IsTrue(_entityManager.IsComponentEnabled<MinerDecision>(minerEntity));

        // Act: 1.0초 대형 DeltaTime 입력 (MaxSimulationDeltaTime = 0.1f 클램핑 동작)
        _world.SetTime(new Unity.Core.TimeData(1.0, 1.0f));
        _minerExecutionHandle.Update(_world.Unmanaged);

        // Assert: 진행도가 MaxSimulationDeltaTime(0.1f) * MiningSpeed(1.0f) = 0.1f로 제한 검증
        var state = _entityManager.GetComponentData<MinerState>(minerEntity);
        Assert.AreEqual(GameConstants.MaxSimulationDeltaTime * 1.0f, state.Progress, 0.0001f,
            "MinerExecutionSystem must clamp DeltaTime to MaxSimulationDeltaTime.");
    }
}
