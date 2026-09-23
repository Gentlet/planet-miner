using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Task 3.2.5: BuildingItemOutputDecisionSystem 단위 테스트.
/// 건물 내 보관 아이템의 외향 벨트 방출 의사결정(CanOutput, TargetBeltPosition, FIFO 0번)을 검증합니다.
/// </summary>
public class Phase3BuildingOutputDecisionTests : EcsWorldTestFixture
{
    private SystemHandle _beltSpatialSyncHandle;
    private SystemHandle _itemSpatialSyncHandle;
    private SystemHandle _outputDecisionHandle;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        _beltSpatialSyncHandle = _world.GetOrCreateSystem(typeof(BeltSpatialSyncSystem));
        _itemSpatialSyncHandle = _world.GetOrCreateSystem(typeof(ItemSpatialSyncSystem));
        _outputDecisionHandle = _world.GetOrCreateSystem(typeof(StorageItemOutputDecisionSystem));
    }

    private Entity CreateStorage(int2 position, int2 size, DirectionEnum direction = DirectionEnum.Up)
        => Entities.CreateStorage(position, size, direction, slotCount: 4);

    private Entity CreateStoredItem(Entity storageEntity, ItemTypeEnum itemType, int slotIndex)
        => Entities.CreateStoredItem(storageEntity, itemType, slotIndex);

    private Entity CreateBelt(int2 position, DirectionEnum direction)
        => Entities.CreateBelt(position, direction);

    private Entity CreateWorldBeltItem(int2 position, float progress, ItemTypeEnum itemType = ItemTypeEnum.Iron_Ore)
        => Entities.CreateBeltItem(position, DirectionEnum.Right, progress, 0.0f, itemType);

    private void SyncSpatialAndRunDecision()
    {
        _beltSpatialSyncHandle.Update(_world.Unmanaged);
        var beltFence = _world.EntityManager.CreateEntityQuery(typeof(BeltSpatialIndexFence)).GetSingletonRW<BeltSpatialIndexFence>();
        beltFence.ValueRW.Complete();

        _itemSpatialSyncHandle.Update(_world.Unmanaged);
        var itemFence = _world.EntityManager.CreateEntityQuery(typeof(ItemSpatialIndexFence)).GetSingletonRW<ItemSpatialIndexFence>();
        itemFence.ValueRW.Complete();

        _outputDecisionHandle.Update(_world.Unmanaged);
        ref var systemState = ref _world.Unmanaged.ResolveSystemStateRef(_outputDecisionHandle);
        systemState.Dependency.Complete();
    }

    [Test]
    public void Test01_BuildingWithItem_OutwardBeltEmpty_DecidesCanOutput()
    {
        // Arrange: 1x1 storage at (0, 0) with 1 stored item. Outward belt at (1, 0) pointing Right.
        var storage = CreateStorage(new int2(0, 0), new int2(1, 1));
        var storedItem = CreateStoredItem(storage, ItemTypeEnum.Iron_Ore, slotIndex: 0);
        CreateBelt(new int2(1, 0), DirectionEnum.Right);

        // Act
        SyncSpatialAndRunDecision();

        // Assert
        Assert.IsTrue(_entityManager.IsComponentEnabled<BuildingItemOutputDecision>(storage));
        var decision = _entityManager.GetComponentData<BuildingItemOutputDecision>(storage);
        Assert.IsTrue(decision.CanOutput);
        Assert.AreEqual(storedItem, decision.ItemToOutput);
        Assert.AreEqual(new int2(1, 0), decision.TargetBeltPosition);
    }

    [Test]
    public void Test02_BuildingEmpty_OutputDecisionRemainsDisabled()
    {
        // Arrange: 1x1 storage with no items
        var storage = CreateStorage(new int2(0, 0), new int2(1, 1));
        CreateBelt(new int2(1, 0), DirectionEnum.Right);

        // Act
        SyncSpatialAndRunDecision();

        // Assert
        Assert.IsFalse(_entityManager.IsComponentEnabled<BuildingItemOutputDecision>(storage));
    }

    [Test]
    public void Test03_OutwardBeltOccupiedAtEntrance_DecidesCannotOutput()
    {
        // Arrange: Storage at (0, 0) with item. Belt at (1, 0) has an item at progress 0.1f (< 0.25f ItemSpacing)
        var storage = CreateStorage(new int2(0, 0), new int2(1, 1));
        CreateStoredItem(storage, ItemTypeEnum.Iron_Ore, slotIndex: 0);
        CreateBelt(new int2(1, 0), DirectionEnum.Right);
        CreateWorldBeltItem(new int2(1, 0), progress: 0.1f);

        // Act
        SyncSpatialAndRunDecision();

        // Assert: Entrance blocked, decision remains disabled
        Assert.IsFalse(_entityManager.IsComponentEnabled<BuildingItemOutputDecision>(storage));
    }

    [Test]
    public void Test04_OutwardBeltHasItemFarAhead_DecidesCanOutput()
    {
        // Arrange: Item on belt has progressed to 0.5f (>= 0.25f ItemSpacing), entrance is free!
        var storage = CreateStorage(new int2(0, 0), new int2(1, 1));
        var storedItem = CreateStoredItem(storage, ItemTypeEnum.Iron_Ore, slotIndex: 0);
        CreateBelt(new int2(1, 0), DirectionEnum.Right);
        CreateWorldBeltItem(new int2(1, 0), progress: 0.5f);

        // Act
        SyncSpatialAndRunDecision();

        // Assert: Entrance free, can output
        Assert.IsTrue(_entityManager.IsComponentEnabled<BuildingItemOutputDecision>(storage));
        var decision = _entityManager.GetComponentData<BuildingItemOutputDecision>(storage);
        Assert.IsTrue(decision.CanOutput);
        Assert.AreEqual(storedItem, decision.ItemToOutput);
        Assert.AreEqual(new int2(1, 0), decision.TargetBeltPosition);
    }

    [Test]
    public void Test05_InwardBelt_IgnoredForOutput()
    {
        // Arrange: Belt at (1, 0) points Left (into the building). This is an INWARD belt, not outward.
        var storage = CreateStorage(new int2(0, 0), new int2(1, 1));
        CreateStoredItem(storage, ItemTypeEnum.Iron_Ore, slotIndex: 0);
        CreateBelt(new int2(1, 0), DirectionEnum.Left);

        // Act
        SyncSpatialAndRunDecision();

        // Assert: Inward belt cannot be used for output
        Assert.IsFalse(_entityManager.IsComponentEnabled<BuildingItemOutputDecision>(storage));
    }

    [Test]
    public void Test06_MultiTileBuilding_MultipleBelts_SelectsFirstAvailable()
    {
        // Arrange: 2x2 storage at (0, 0).
        // Occupied cells: (0, 0), (1, 0), (0, 1), (1, 1).
        // Outward belt 1: (2, 0) pointing Right. (Right side of storage)
        // Outward belt 2: (0, 2) pointing Up. (Top side of storage)
        var storage = CreateStorage(new int2(0, 0), new int2(2, 2));
        var storedItem = CreateStoredItem(storage, ItemTypeEnum.Iron_Ore, slotIndex: 0);
        CreateBelt(new int2(2, 0), DirectionEnum.Right);
        CreateBelt(new int2(0, 2), DirectionEnum.Up);

        // Act
        SyncSpatialAndRunDecision();

        // Assert: Selects first available outward belt (in perimeter traversal order: right side (2, 0) comes before top side (0, 2))
        Assert.IsTrue(_entityManager.IsComponentEnabled<BuildingItemOutputDecision>(storage));
        var decision = _entityManager.GetComponentData<BuildingItemOutputDecision>(storage);
        Assert.IsTrue(decision.CanOutput);
        Assert.AreEqual(storedItem, decision.ItemToOutput);
        Assert.AreEqual(new int2(2, 0), decision.TargetBeltPosition);
    }
}
