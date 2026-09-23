using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Task 3.2.6: Task 3.2 Storage/Building 입출력 의사결정 파이프라인 통합 테스트.
/// 벨트 이동 -> 건물 입고 연계, 다중 외향 벨트 교차 방출, 다중 타일 복합 필터링, 정체 해소 후 방출 재개를 통합 검증합니다.
/// </summary>
public class Phase3StorageDecisionTests : EcsWorldTestFixture
{
    private SystemHandle _beltSpatialSyncHandle;
    private SystemHandle _itemSpatialSyncHandle;
    private SystemHandle _buildingSpatialSyncHandle;
    private SystemHandle _beltMovementDecisionHandle;
    private SystemHandle _buildingInputDecisionHandle;
    private SystemHandle _buildingOutputDecisionHandle;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();

        _beltSpatialSyncHandle = _world.GetOrCreateSystem(typeof(BeltSpatialSyncSystem));
        _itemSpatialSyncHandle = _world.GetOrCreateSystem(typeof(ItemSpatialSyncSystem));
        _buildingSpatialSyncHandle = _world.GetOrCreateSystem(typeof(BuildingSpatialSyncSystem));

        _beltMovementDecisionHandle = _world.GetOrCreateSystem(typeof(BeltMovementDecisionSystem));
        _buildingInputDecisionHandle = _world.GetOrCreateSystem(typeof(BuildingItemInputDecisionSystem));
        _buildingOutputDecisionHandle = _world.GetOrCreateSystem(typeof(StorageItemOutputDecisionSystem));
    }

    private Entity CreateStorage(int2 position, int2 size, DirectionEnum direction = DirectionEnum.Up, StorageFilter? filter = null)
        => Entities.CreateStorage(position, size, direction, slotCount: 8, filter: filter);

    private Entity CreateStoredItem(Entity storageEntity, ItemTypeEnum itemType, int slotIndex)
        => Entities.CreateStoredItem(storageEntity, itemType, slotIndex);

    private Entity CreateBelt(int2 position, DirectionEnum direction, float speed = 2.0f)
        => Entities.CreateBelt(position, direction, speed);

    private Entity CreateBeltItem(int2 position, DirectionEnum direction, float progress, ItemTypeEnum itemType = ItemTypeEnum.Iron_Ore)
        => Entities.CreateBeltItem(position, direction, progress, 0.0f, itemType);

    private void SyncAllSpatialIndices()
    {
        _beltSpatialSyncHandle.Update(_world.Unmanaged);
        var beltFence = _world.EntityManager.CreateEntityQuery(typeof(BeltSpatialIndexFence)).GetSingletonRW<BeltSpatialIndexFence>();
        beltFence.ValueRW.Complete();

        _itemSpatialSyncHandle.Update(_world.Unmanaged);
        var itemFence = _world.EntityManager.CreateEntityQuery(typeof(ItemSpatialIndexFence)).GetSingletonRW<ItemSpatialIndexFence>();
        itemFence.ValueRW.Complete();

        _buildingSpatialSyncHandle.Update(_world.Unmanaged);
        var buildingFence = _world.EntityManager.CreateEntityQuery(typeof(BuildingSpatialIndexFence)).GetSingletonRW<BuildingSpatialIndexFence>();
        buildingFence.ValueRW.Complete();
    }

    private void RunDecisionPhase(float deltaTime = 0.05f)
    {
        _world.SetTime(new Unity.Core.TimeData(0.1, deltaTime));
        SyncAllSpatialIndices();

        _beltMovementDecisionHandle.Update(_world.Unmanaged);
        ref var beltState = ref _world.Unmanaged.ResolveSystemStateRef(_beltMovementDecisionHandle);
        beltState.Dependency.Complete();

        _buildingInputDecisionHandle.Update(_world.Unmanaged);
        ref var inputState = ref _world.Unmanaged.ResolveSystemStateRef(_buildingInputDecisionHandle);
        inputState.Dependency.Complete();

        _buildingOutputDecisionHandle.Update(_world.Unmanaged);
        ref var outputState = ref _world.Unmanaged.ResolveSystemStateRef(_buildingOutputDecisionHandle);
        outputState.Dependency.Complete();
    }

    [Test]
    public void Test01_BeltMovement_To_BuildingInput_PipelineIntegration()
    {
        // 1. Arrange: Belt at (0, 0) pointing Right towards Storage at (1, 0).
        CreateBelt(new int2(0, 0), DirectionEnum.Right);
        var storage = CreateStorage(new int2(1, 0), new int2(1, 1));

        // Item moving mid-belt at progress 0.6f
        var item = CreateBeltItem(new int2(0, 0), DirectionEnum.Right, 0.6f, ItemTypeEnum.Iron_Ore);

        // 2. Act: Step 1 - mid-belt movement decision
        RunDecisionPhase();

        // Assert Step 1: Planned progress to reach tile end (1.0 - 0.6 = 0.4), but InputDecision is NOT active yet
        var beltDecision = _entityManager.GetComponentData<BeltMovementDecision>(item);
        Assert.Greater(beltDecision.PlannedProgress, 0.0f);
        Assert.IsFalse(_entityManager.IsComponentEnabled<BuildingItemInputDecision>(item), "InputDecision should remain disabled while moving mid-belt.");

        // 3. Act: Step 2 - Item reaches end of belt (progress = 1.0f)
        _entityManager.SetComponentData(item, new BeltMovementState(1.0f));
        RunDecisionPhase();

        // Assert Step 2: Belt is blocked at tile end, and BuildingInputDecision activates with CanDeposit = true!
        beltDecision = _entityManager.GetComponentData<BeltMovementDecision>(item);
        Assert.IsTrue(beltDecision.IsBlocked, "Item should be blocked at belt end.");

        Assert.IsTrue(_entityManager.IsComponentEnabled<BuildingItemInputDecision>(item), "InputDecision should activate at belt end.");
        var inputDecision = _entityManager.GetComponentData<BuildingItemInputDecision>(item);
        Assert.AreEqual(storage, inputDecision.TargetBuilding);
        Assert.IsTrue(inputDecision.CanDeposit);
        Assert.AreEqual(-1, inputDecision.TargetSlotIndex);
    }

    [Test]
    public void Test02_MultipleOutwardBelts_AlternatingOutput_Pipeline()
    {
        // 1. Arrange: 2x2 Storage at (0, 0) with 2 stored items.
        // Belt 1: (2, 0) pointing Right (East output)
        // Belt 2: (0, 2) pointing Up (North output)
        var storage = CreateStorage(new int2(0, 0), new int2(2, 2));
        var item1 = CreateStoredItem(storage, ItemTypeEnum.Iron_Ore, slotIndex: 0);
        var item2 = CreateStoredItem(storage, ItemTypeEnum.Copper_Ore, slotIndex: 1);

        CreateBelt(new int2(2, 0), DirectionEnum.Right);
        CreateBelt(new int2(0, 2), DirectionEnum.Up);

        // 2. Act: Tick 1 - First output decision
        RunDecisionPhase();

        // Assert Tick 1: Belt 1 (2, 0) is chosen first for FIFO item1
        Assert.IsTrue(_entityManager.IsComponentEnabled<BuildingItemOutputDecision>(storage));
        var outputDecision = _entityManager.GetComponentData<BuildingItemOutputDecision>(storage);
        Assert.IsTrue(outputDecision.CanOutput);
        Assert.AreEqual(item1, outputDecision.ItemToOutput);
        Assert.AreEqual(new int2(2, 0), outputDecision.TargetBeltPosition);

        // 3. Simulate Tick 2: Item 1 has been placed at entrance of Belt 1 (progress = 0.0f)
        // And item 1 is removed from storage buffer (simulating ownership transfer)
        var buffer = _entityManager.GetBuffer<StoredItemElement>(storage);
        buffer.RemoveAt(0); // Only item2 remains in storage

        var worldItem1 = CreateBeltItem(new int2(2, 0), DirectionEnum.Right, 0.0f, ItemTypeEnum.Iron_Ore);

        // Act: Tick 2 - Output decision for item 2
        RunDecisionPhase();

        // Assert Tick 2: Belt 1 is blocked at entrance (progress 0.0f < 0.25f), so system automatically alternates to Belt 2 (0, 2)!
        Assert.IsTrue(_entityManager.IsComponentEnabled<BuildingItemOutputDecision>(storage));
        outputDecision = _entityManager.GetComponentData<BuildingItemOutputDecision>(storage);
        Assert.IsTrue(outputDecision.CanOutput);
        Assert.AreEqual(item2, outputDecision.ItemToOutput);
        Assert.AreEqual(new int2(0, 2), outputDecision.TargetBeltPosition, "Should naturally alternate to the second available belt.");
    }

    [Test]
    public void Test03_MultiTileBuilding_MultipleArrivals_FilteredDecision()
    {
        // 1. Arrange: 2x2 Storage at (1, 1).
        // Occupied cells: (1, 1), (2, 1), (1, 2), (2, 2).
        // Filter: Whitelist, only Iron_Ore allowed.
        var filter = new StorageFilter(StorageFilterMode.Whitelist);
        filter.Mask.Set((byte)ItemTypeEnum.Iron_Ore, true);
        var storage = CreateStorage(new int2(1, 1), new int2(2, 2), filter: filter);

        // Inward belt 1: at (0, 1) pointing Right -> enters storage cell (1, 1) with Iron_Ore (Allowed)
        CreateBelt(new int2(0, 1), DirectionEnum.Right);
        var allowedItem = CreateBeltItem(new int2(0, 1), DirectionEnum.Right, 1.0f, ItemTypeEnum.Iron_Ore);

        // Inward belt 2: at (1, 3) pointing Down -> enters storage cell (1, 2) with Coal (Blocked by filter)
        CreateBelt(new int2(1, 3), DirectionEnum.Down);
        var blockedItem = CreateBeltItem(new int2(1, 3), DirectionEnum.Down, 1.0f, ItemTypeEnum.Coal);

        // 2. Act
        RunDecisionPhase();

        // 3. Assert: Both items detect the same 2x2 storage, but decision branches based on filter!
        Assert.IsTrue(_entityManager.IsComponentEnabled<BuildingItemInputDecision>(allowedItem));
        var decisionAllowed = _entityManager.GetComponentData<BuildingItemInputDecision>(allowedItem);
        Assert.AreEqual(storage, decisionAllowed.TargetBuilding);
        Assert.IsTrue(decisionAllowed.CanDeposit, "Iron_Ore must be allowed by whitelist filter.");

        Assert.IsTrue(_entityManager.IsComponentEnabled<BuildingItemInputDecision>(blockedItem));
        var decisionBlocked = _entityManager.GetComponentData<BuildingItemInputDecision>(blockedItem);
        Assert.AreEqual(storage, decisionBlocked.TargetBuilding);
        Assert.IsFalse(decisionBlocked.CanDeposit, "Coal must be blocked by whitelist filter.");
    }

    [Test]
    public void Test04_OutwardBeltCongestion_ResolvesAndResumesOutput()
    {
        // 1. Arrange: Storage at (0, 0) with 1 item. Outward belt at (1, 0) pointing Right.
        var storage = CreateStorage(new int2(0, 0), new int2(1, 1));
        var storedItem = CreateStoredItem(storage, ItemTypeEnum.Iron_Ore, slotIndex: 0);
        CreateBelt(new int2(1, 0), DirectionEnum.Right);

        // Another world item on the belt is jamming the entrance (progress = 0.05f < 0.25f ItemSpacing)
        var aheadItem = CreateBeltItem(new int2(1, 0), DirectionEnum.Right, 0.05f);

        // 2. Act: Step 1 - Jammed entrance
        RunDecisionPhase();

        // Assert Step 1: Cannot output due to congestion
        Assert.IsFalse(_entityManager.IsComponentEnabled<BuildingItemOutputDecision>(storage), "Output should be disabled when entrance is congested.");

        // 3. Act: Step 2 - Ahead item advances down the belt (progress = 0.4f >= 0.25f ItemSpacing)
        _entityManager.SetComponentData(aheadItem, new BeltMovementState(0.4f));
        RunDecisionPhase();

        // Assert Step 2: Entrance cleared! Output decision automatically activates and targets belt
        Assert.IsTrue(_entityManager.IsComponentEnabled<BuildingItemOutputDecision>(storage), "Output should resume when entrance is cleared.");
        var outputDecision = _entityManager.GetComponentData<BuildingItemOutputDecision>(storage);
        Assert.IsTrue(outputDecision.CanOutput);
        Assert.AreEqual(storedItem, outputDecision.ItemToOutput);
        Assert.AreEqual(new int2(1, 0), outputDecision.TargetBeltPosition);
    }
}