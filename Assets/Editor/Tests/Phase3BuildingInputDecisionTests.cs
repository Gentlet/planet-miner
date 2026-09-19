using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Task 3.2.4: BuildingItemInputDecisionSystem 단위 테스트.
/// 벨트 끝에 도달한 아이템의 건물 입고 의사결정(CanDeposit, TargetBuilding)을 순수하게 검증합니다.
/// </summary>
public class Phase3BuildingInputDecisionTests : EcsWorldTestFixture
{
    private SystemHandle _buildingSpatialSyncHandle;
    private SystemHandle _inputDecisionHandle;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        _buildingSpatialSyncHandle = _world.GetOrCreateSystem(typeof(BuildingSpatialSyncSystem));
        _inputDecisionHandle = _world.GetOrCreateSystem(typeof(BuildingItemInputDecisionSystem));
    }

    private Entity CreateStorage(int2 position, int2 size, DirectionEnum direction = DirectionEnum.Up, StorageFilter? filter = null)
    {
        var entity = _entityManager.CreateEntity(
            typeof(BuildingType),
            typeof(BuildingFootprint),
            typeof(GridPosition),
            typeof(Direction),
            typeof(Storage));

        _entityManager.SetComponentData(entity, new BuildingType(BuildingTypeEnum.Storage));
        _entityManager.SetComponentData(entity, new BuildingFootprint(size));
        _entityManager.SetComponentData(entity, new GridPosition(position));
        _entityManager.SetComponentData(entity, new Direction(direction));
        _entityManager.SetComponentData(entity, new Storage(slotCount: 4));
        _entityManager.AddBuffer<StoredItemElement>(entity);

        if (filter.HasValue)
        {
            _entityManager.AddComponentData(entity, filter.Value);
        }

        return entity;
    }

    private Entity CreateBeltItem(int2 position, DirectionEnum direction, float progress, ItemTypeEnum itemType = ItemTypeEnum.Iron_Ore)
    {
        var entity = _entityManager.CreateEntity(
            typeof(ItemIdentity),
            typeof(ItemOwnership),
            typeof(GridPosition),
            typeof(Direction),
            typeof(BeltMovementState),
            typeof(BuildingItemInputDecision));

        _entityManager.SetComponentData(entity, new ItemIdentity(itemType));
        _entityManager.SetComponentData(entity, ItemOwnership.WorldItem);
        _entityManager.SetComponentData(entity, new GridPosition(position));
        _entityManager.SetComponentData(entity, new Direction(direction));
        _entityManager.SetComponentData(entity, new BeltMovementState(progress));
        _entityManager.SetComponentData(entity, new BuildingItemInputDecision(Entity.Null, false, -1));

        // 초기 상태: 비활성화
        _entityManager.SetComponentEnabled<BuildingItemInputDecision>(entity, false);

        return entity;
    }

    private void SyncSpatialAndRunDecision()
    {
        _buildingSpatialSyncHandle.Update(_world.Unmanaged);
        var fence = _world.EntityManager.CreateEntityQuery(typeof(BuildingSpatialIndexFence)).GetSingletonRW<BuildingSpatialIndexFence>();
        fence.ValueRW.Complete();

        _inputDecisionHandle.Update(_world.Unmanaged);
        ref var systemState = ref _world.Unmanaged.ResolveSystemStateRef(_inputDecisionHandle);
        systemState.Dependency.Complete();
    }

    [Test]
    public void Test01_ItemAtBeltEnd_AdjacentStorage_DecidesCanDeposit()
    {
        // Arrange: (0, 0) belt pointing Right, item at progress 1.0f. Storage at (1, 0).
        var storage = CreateStorage(new int2(1, 0), new int2(1, 1));
        var item = CreateBeltItem(new int2(0, 0), DirectionEnum.Right, 1.0f, ItemTypeEnum.Iron_Ore);

        // Act
        SyncSpatialAndRunDecision();

        // Assert
        Assert.IsTrue(_entityManager.IsComponentEnabled<BuildingItemInputDecision>(item));
        var decision = _entityManager.GetComponentData<BuildingItemInputDecision>(item);
        Assert.AreEqual(storage, decision.TargetBuilding);
        Assert.IsTrue(decision.CanDeposit);
        Assert.AreEqual(-1, decision.TargetSlotIndex);
    }

    [Test]
    public void Test02_ItemAtBeltEnd_StorageFilterBlocks_DecidesBlocked()
    {
        // Arrange: Storage at (1, 0) blocks Iron_Ore via Whitelist (only Copper_Ore allowed)
        var filter = new StorageFilter(StorageFilterMode.Whitelist);
        filter.Mask.Set((byte)ItemTypeEnum.Copper_Ore, true);
        var storage = CreateStorage(new int2(1, 0), new int2(1, 1), filter: filter);

        var item = CreateBeltItem(new int2(0, 0), DirectionEnum.Right, 1.0f, ItemTypeEnum.Iron_Ore);

        // Act
        SyncSpatialAndRunDecision();

        // Assert: Decision is enabled with CanDeposit = false
        Assert.IsTrue(_entityManager.IsComponentEnabled<BuildingItemInputDecision>(item));
        var decision = _entityManager.GetComponentData<BuildingItemInputDecision>(item);
        Assert.AreEqual(storage, decision.TargetBuilding);
        Assert.IsFalse(decision.CanDeposit, "Item should not be allowed to deposit due to filter.");
    }

    [Test]
    public void Test03_ItemMovingMidBelt_DecisionRemainsDisabled()
    {
        // Arrange: Item at progress 0.5f
        CreateStorage(new int2(1, 0), new int2(1, 1));
        var item = CreateBeltItem(new int2(0, 0), DirectionEnum.Right, 0.5f, ItemTypeEnum.Iron_Ore);

        // Act
        SyncSpatialAndRunDecision();

        // Assert: Component remains disabled
        Assert.IsFalse(_entityManager.IsComponentEnabled<BuildingItemInputDecision>(item));
    }

    [Test]
    public void Test04_ItemAtBeltEnd_NoBuilding_DecisionDisabled()
    {
        // Arrange: Belt ends with no building in front
        var item = CreateBeltItem(new int2(0, 0), DirectionEnum.Right, 1.0f, ItemTypeEnum.Iron_Ore);

        // Act
        SyncSpatialAndRunDecision();

        // Assert
        Assert.IsFalse(_entityManager.IsComponentEnabled<BuildingItemInputDecision>(item));
    }

    [Test]
    public void Test05_ItemAtBeltEnd_NonStorageBuilding_DecidesBlocked()
    {
        // Arrange: Non-storage building (PowerPole) at (1, 0)
        var pole = _entityManager.CreateEntity(
            typeof(BuildingType),
            typeof(BuildingFootprint),
            typeof(GridPosition),
            typeof(Direction));
        _entityManager.SetComponentData(pole, new BuildingType(BuildingTypeEnum.PowerPole));
        _entityManager.SetComponentData(pole, new BuildingFootprint(1, 1));
        _entityManager.SetComponentData(pole, new GridPosition(new int2(1, 0)));
        _entityManager.SetComponentData(pole, new Direction(DirectionEnum.Up));

        var item = CreateBeltItem(new int2(0, 0), DirectionEnum.Right, 1.0f, ItemTypeEnum.Iron_Ore);

        // Act
        SyncSpatialAndRunDecision();

        // Assert
        Assert.IsTrue(_entityManager.IsComponentEnabled<BuildingItemInputDecision>(item));
        var decision = _entityManager.GetComponentData<BuildingItemInputDecision>(item);
        Assert.AreEqual(pole, decision.TargetBuilding);
        Assert.IsFalse(decision.CanDeposit);
    }

    [Test]
    public void Test06_MultiTileBuilding_DetectsTargetBuildingAtOccupiedCell()
    {
        // Arrange: 2x2 Storage at anchor (1, 0). Cells: (1, 0), (2, 0), (1, 1), (2, 1).
        var storage = CreateStorage(new int2(1, 0), new int2(2, 2));

        // Belt item at (0, 1) pointing Right -> next cell is (1, 1) which is part of the 2x2 storage
        var item = CreateBeltItem(new int2(0, 1), DirectionEnum.Right, 1.0f, ItemTypeEnum.Iron_Ore);

        // Act
        SyncSpatialAndRunDecision();

        // Assert
        Assert.IsTrue(_entityManager.IsComponentEnabled<BuildingItemInputDecision>(item));
        var decision = _entityManager.GetComponentData<BuildingItemInputDecision>(item);
        Assert.AreEqual(storage, decision.TargetBuilding);
        Assert.IsTrue(decision.CanDeposit);
    }
}
