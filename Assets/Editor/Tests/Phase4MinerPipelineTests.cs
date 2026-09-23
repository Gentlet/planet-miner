using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Task 4.2: Miner Decision & Execution System 통합 파이프라인 검증 테스트 (내부 버퍼 모델).
/// - MinerDecisionSystem: 하부 자원 감지, 내부 버퍼 여유 검사, 다중 자원 우선순위
/// - MinerExecutionSystem: 진행도 누적, 채굴 완료 시 ProductResult 기록, 자원 차감 및 고갈 파괴
/// - BuildingItemOutput 파이프라인 연계: 채굴기 버퍼의 아이템이 외향 벨트로 정상 방출되는 전체 2단계 파이프라인 검증
/// </summary>
public class Phase4MinerPipelineTests : EcsWorldTestFixture
{
    private SystemHandle _resourceSpatialSyncHandle;
    private SystemHandle _beltSpatialSyncHandle;
    private SystemHandle _itemSpatialSyncHandle;
    private SystemHandle _minerDecisionHandle;
    private SystemHandle _minerExecutionHandle;
    private SystemHandle _outputDecisionHandle;
    private SystemHandle _storageApplyHandle;
    private SystemHandle _lifecycleHandle;
    private SystemHandle _ownershipHandle;
    private EndStateApplyEntityCommandBufferSystem _endStateApplyEcb;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();

        _resourceSpatialSyncHandle = _world.GetOrCreateSystem(typeof(ResourceSpatialSyncSystem));
        _beltSpatialSyncHandle = _world.GetOrCreateSystem(typeof(BeltSpatialSyncSystem));
        _itemSpatialSyncHandle = _world.GetOrCreateSystem(typeof(ItemSpatialSyncSystem));
        _minerDecisionHandle = _world.GetOrCreateSystem(typeof(MinerDecisionSystem));
        _minerExecutionHandle = _world.GetOrCreateSystem(typeof(MinerExecutionSystem));
        _outputDecisionHandle = _world.GetOrCreateSystem(typeof(ProductItemOutputDecisionSystem));
        _storageApplyHandle = _world.GetOrCreateSystem(typeof(BuildingItemStorageApplySystem));
        _lifecycleHandle = _world.GetOrCreateSystem(typeof(ItemLifecycleApplySystem));
        _ownershipHandle = _world.GetOrCreateSystem(typeof(ItemOwnershipApplySystem));
        _endStateApplyEcb = _world.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>();
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
    public void Test01_MinerDecision_DetectsUnderlyingResourceAndHasSpace()
    {
        // 1x1 채굴기 (10, 10), 바로 아래 철광석, 버퍼 비어있음
        var resEntity = CreateResourceNode(new int2(10, 10), ItemTypeEnum.Iron_Ore, 100);
        var minerEntity = CreateMiner(new int2(10, 10), new int2(1, 1), DirectionEnum.Up, 1.0f);

        SyncAllSpatialIndices();
        _minerDecisionHandle.Update(_world.Unmanaged);

        Assert.IsTrue(_entityManager.IsComponentEnabled<MinerDecision>(minerEntity), "MinerDecision should be enabled when resource exists and buffer has space.");
        var decision = _entityManager.GetComponentData<MinerDecision>(minerEntity);
        Assert.IsTrue(decision.CanMine, "CanMine should be true.");
        Assert.AreEqual(resEntity, decision.TargetResource, "TargetResource should match underlying resource.");
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
    public void Test03_MinerExecution_SpawnsItemToMinerBufferAndDecrementsAmount()
    {
        // 채굴기 (10, 10), 철광석 Amount = 10, Progress = 0.95f
        var resEntity = CreateResourceNode(new int2(10, 10), ItemTypeEnum.Iron_Ore, 10);
        var minerEntity = CreateMiner(new int2(10, 10), new int2(1, 1), DirectionEnum.Up, miningSpeed: 1.0f, progress: 0.95f);

        SyncAllSpatialIndices();
        _minerDecisionHandle.Update(_world.Unmanaged);

        // Phase 4: Execution 실행 (deltaTime = 0.1f -> 0.95 + 0.1 = 1.05f -> 채굴 완료 및 0.05f 리셋)
        _world.SetTime(new Unity.Core.TimeData(0.1, 0.1f));
        _minerExecutionHandle.Update(_world.Unmanaged);

        // 1. 진행도 확인: 초과 진행도 0.05f 보존
        var minerState = _entityManager.GetComponentData<MinerState>(minerEntity);
        Assert.AreEqual(0.05f, minerState.Progress, 0.001f, "Progress should be reset preserving excess progress.");

        // 2. 자원 매장량 확인: 10에서 9로 차감
        var resNode = _entityManager.GetComponentData<ResourceNode>(resEntity);
        Assert.AreEqual(9, resNode.Amount, "Resource amount should be decremented by 1.");

        // 3. Execution 결과는 SpawnItemRequest Entity가 아니라 ProductResult Buffer에 즉시 기록되어야 함
        var productResults = _entityManager.GetBuffer<ProductResult>(minerEntity);
        Assert.AreEqual(1, productResults.Length, "Miner should record exactly one production result before StateApply.");
        Assert.AreEqual(ItemTypeEnum.Iron_Ore, productResults[0].ItemType);
        Assert.AreEqual(1, productResults[0].Count);
        Assert.AreEqual(0, productResults[0].SlotIndex);

        // 4. Phase 5 StateApply 단계 연계: ProductResult가 실제 Item + ProductItemElement로 변환되는지 검증
        RunStateApplyPhase();

        productResults = _entityManager.GetBuffer<ProductResult>(minerEntity);
        Assert.AreEqual(0, productResults.Length, "ProductResult should be consumed in the same StateApply phase.");

        var buffer = _entityManager.GetBuffer<ProductItemElement>(minerEntity);
        Assert.AreEqual(1, buffer.Length, "Miner buffer should contain 1 product item.");
        Assert.AreEqual(ItemTypeEnum.Iron_Ore, buffer[0].ItemType);

        var storedItem = buffer[0].ItemEntity;
        Assert.IsTrue(_entityManager.Exists(storedItem), "Stored item entity should exist.");
        var ownership = _entityManager.GetComponentData<ItemOwnership>(storedItem);
        Assert.IsTrue(ownership.IsStored);
        Assert.AreEqual(minerEntity, ownership.Owner);
    }

    [Test]
    public void Test04_FullMiningAndOutputToBeltPipeline()
    {
        // 채굴기 (10, 10), 철광석 Amount = 10, 외향 벨트 (10, 11) (Dir = Up)
        var resEntity = CreateResourceNode(new int2(10, 10), ItemTypeEnum.Iron_Ore, 10);
        var minerEntity = CreateMiner(new int2(10, 10), new int2(1, 1), DirectionEnum.Up, miningSpeed: 1.0f, progress: 0.95f);
        CreateBelt(new int2(10, 11), DirectionEnum.Up);

        // [프레임 1]: 채굴 및 버퍼 적재
        SyncAllSpatialIndices();
        _minerDecisionHandle.Update(_world.Unmanaged);

        _world.SetTime(new Unity.Core.TimeData(0.1, 0.1f));
        _minerExecutionHandle.Update(_world.Unmanaged);

        RunStateApplyPhase();

        var buffer = _entityManager.GetBuffer<ProductItemElement>(minerEntity);
        Assert.AreEqual(1, buffer.Length, "Item should be loaded in miner buffer.");
        Entity minedItem = buffer[0].ItemEntity;

        // [프레임 2]: 외향 벨트 감지 및 벨트로 방출
        SyncAllSpatialIndices();
        _outputDecisionHandle.Update(_world.Unmanaged);

        Assert.IsTrue(_entityManager.IsComponentEnabled<BuildingItemOutputDecision>(minerEntity), "BuildingItemOutputDecision should be enabled.");
        var outputDecision = _entityManager.GetComponentData<BuildingItemOutputDecision>(minerEntity);
        Assert.IsTrue(outputDecision.CanOutput);
        Assert.AreEqual(minedItem, outputDecision.ItemToOutput);
        Assert.AreEqual(new int2(10, 11), outputDecision.TargetBeltPosition);

        // 방출 실행 (StateApply)
        RunStateApplyPhase();

        // 검증: 버퍼에서 제거되었고 월드 벨트 아이템으로 전환됨
        Assert.AreEqual(0, buffer.Length, "Miner buffer should now be empty after output.");
        var itemOwnership = _entityManager.GetComponentData<ItemOwnership>(minedItem);
        Assert.IsTrue(itemOwnership.IsWorldItem, "Item should now be a WorldItem on belt.");
        var itemPos = _entityManager.GetComponentData<GridPosition>(minedItem);
        Assert.AreEqual(new int2(10, 11), itemPos.Value, "Item should be at target belt position.");
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
    public void Test06_ResourceDepletion_DestroysResourceEntity()
    {
        var resEntity = CreateResourceNode(new int2(10, 10), ItemTypeEnum.Iron_Ore, 1);
        var minerEntity = CreateMiner(new int2(10, 10), new int2(1, 1), DirectionEnum.Up, miningSpeed: 1.0f, progress: 0.95f);

        SyncAllSpatialIndices();
        _minerDecisionHandle.Update(_world.Unmanaged);

        _world.SetTime(new Unity.Core.TimeData(0.1, 0.1f));
        _minerExecutionHandle.Update(_world.Unmanaged);
        RunStateApplyPhase();

        Assert.IsFalse(_entityManager.Exists(resEntity), "Depleted resource entity should be destroyed.");

        SyncAllSpatialIndices();
        _minerDecisionHandle.Update(_world.Unmanaged);

        Assert.IsFalse(_entityManager.IsComponentEnabled<MinerDecision>(minerEntity), "MinerDecision should be disabled after resource depletion.");
    }

    [Test]
    public void Test07_MultiTileMiner_PicksFirstResource()
    {
        var copperEntity = CreateResourceNode(new int2(10, 10), ItemTypeEnum.Copper_Ore, 50);
        var ironEntity = CreateResourceNode(new int2(11, 11), ItemTypeEnum.Iron_Ore, 50);
        var minerEntity = CreateMiner(new int2(10, 10), new int2(2, 2), DirectionEnum.Up, 1.0f);

        SyncAllSpatialIndices();
        _minerDecisionHandle.Update(_world.Unmanaged);

        Assert.IsTrue(_entityManager.IsComponentEnabled<MinerDecision>(minerEntity));
        var decision = _entityManager.GetComponentData<MinerDecision>(minerEntity);
        Assert.IsTrue(decision.CanMine);
        Assert.AreEqual(copperEntity, decision.TargetResource, "Multi-tile miner should pick the first resource (anchor corner).");
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

        // 소비된 ProductResult를 다시 처리해 중복 아이템을 생성하면 안 됩니다.
        RunStateApplyPhase();
        productBuffer = _entityManager.GetBuffer<ProductItemElement>(minerEntity);
        Assert.AreEqual(50, productBuffer.Length, "A consumed ProductResult must never create a duplicate item.");

        SyncAllSpatialIndices();
        _minerDecisionHandle.Update(_world.Unmanaged);

        Assert.IsFalse(
            _entityManager.IsComponentEnabled<MinerDecision>(minerEntity),
            "Miner must stop after the single ProductBuffer stack reaches MaxStack.");
    }
}
