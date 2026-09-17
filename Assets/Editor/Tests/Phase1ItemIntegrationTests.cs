using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;

public class Phase1ItemIntegrationTests : EcsWorldTestFixture
{
    private SystemHandle _lifecycleHandle;
    private SystemHandle _ownershipHandle;
    private SystemHandle _spatialSyncHandle;
    private EndStateApplyEntityCommandBufferSystem _endStateApplyEcb;
    private WorldInvariantValidationSystem _invariantValidationSystem;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();

        _lifecycleHandle = _world.GetOrCreateSystem(typeof(ItemLifecycleApplySystem));
        _ownershipHandle = _world.GetOrCreateSystem(typeof(ItemOwnershipApplySystem));
        _spatialSyncHandle = _world.GetOrCreateSystem(typeof(ItemSpatialSyncSystem));
        _endStateApplyEcb = _world.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>();
        _invariantValidationSystem = _world.GetOrCreateSystemManaged<WorldInvariantValidationSystem>();
    }

    private void UpdateStateApplyPhase()
    {
        _lifecycleHandle.Update(_world.Unmanaged);
        _ownershipHandle.Update(_world.Unmanaged);
        _endStateApplyEcb.Update();
    }

    private void UpdateSynchronizationPhase()
    {
        _spatialSyncHandle.Update(_world.Unmanaged);
        _invariantValidationSystem.Update();
    }

    [Test]
    public void Test01_SpawnItem_CreatesEntityAndInitializesComponents()
    {
        // Arrange
        var reqEntity = _entityManager.CreateEntity(typeof(SpawnItemRequest));
        _entityManager.SetComponentData(reqEntity, new SpawnItemRequest
        {
            ItemType = ItemTypeEnum.Iron_Ore,
            Position = new int2(10, 20),
            TargetOwner = Entity.Null
        });

        // Act
        UpdateStateApplyPhase();
        UpdateSynchronizationPhase();

        // Assert: 요청 엔티티 소멸 및 아이템 엔티티 생성 확인
        Assert.IsFalse(_entityManager.Exists(reqEntity), "SpawnItemRequest entity should be consumed and destroyed.");

        var query = _entityManager.CreateEntityQuery(typeof(ItemIdentity), typeof(GridPosition), typeof(ItemOwnership));
        Assert.AreEqual(1, query.CalculateEntityCount(), "Exactly 1 item entity should be created.");

        var item = query.GetSingletonEntity();
        Assert.AreEqual(ItemTypeEnum.Iron_Ore, _entityManager.GetComponentData<ItemIdentity>(item).Type);
        Assert.AreEqual(new int2(10, 20), _entityManager.GetComponentData<GridPosition>(item).Value);
        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(item).IsWorldItem);
        Assert.IsFalse(_entityManager.IsComponentEnabled<DestroyItemRequest>(item));
        Assert.IsFalse(_entityManager.IsComponentEnabled<TransferOwnershipRequest>(item));
    }

    [Test]
    public void Test02_SpatialSync_IndexesWorldItemAtGridPosition()
    {
        // Arrange: 스폰
        var reqEntity = _entityManager.CreateEntity(typeof(SpawnItemRequest));
        _entityManager.SetComponentData(reqEntity, new SpawnItemRequest
        {
            ItemType = ItemTypeEnum.Copper_Ore,
            Position = new int2(5, 7),
            TargetOwner = Entity.Null
        });
        UpdateStateApplyPhase();

        // Act: 공간 동기화
        UpdateSynchronizationPhase();

        // Assert: ItemSpatialIndex에서 O(1) 조회 확인
        var spatialIndex = _entityManager.CreateEntityQuery(typeof(ItemSpatialIndex)).GetSingleton<ItemSpatialIndex>();
        Assert.IsTrue(spatialIndex.HasItemAt(new int2(5, 7)));
        Assert.AreEqual(1, spatialIndex.CountItemsAt(new int2(5, 7)));

        Assert.IsTrue(spatialIndex.TryGetFirstItem(new int2(5, 7), out Entity indexedItem, out var it));
        var item = _entityManager.CreateEntityQuery(typeof(ItemIdentity)).GetSingletonEntity();
        Assert.AreEqual(item, indexedItem);
    }

    [Test]
    public void Test03_ItemMovement_UpdatesSpatialIndex()
    {
        // Arrange
        var reqEntity = _entityManager.CreateEntity(typeof(SpawnItemRequest));
        _entityManager.SetComponentData(reqEntity, new SpawnItemRequest
        {
            ItemType = ItemTypeEnum.Coal,
            Position = new int2(1, 1),
            TargetOwner = Entity.Null
        });
        UpdateStateApplyPhase();
        UpdateSynchronizationPhase();

        var item = _entityManager.CreateEntityQuery(typeof(ItemIdentity)).GetSingletonEntity();

        // Act: 이동 (GridPosition 변경 후 SynchronizationPhase 재실행)
        _entityManager.SetComponentData(item, new GridPosition(new int2(2, 3)));
        UpdateSynchronizationPhase();

        // Assert: 이전 위치에서 제거되고 새 위치로 인덱싱 확인
        var spatialIndex = _entityManager.CreateEntityQuery(typeof(ItemSpatialIndex)).GetSingleton<ItemSpatialIndex>();
        Assert.IsFalse(spatialIndex.HasItemAt(new int2(1, 1)), "Old position should be cleared.");
        Assert.IsTrue(spatialIndex.HasItemAt(new int2(2, 3)), "New position should be indexed.");
        Assert.AreEqual(1, spatialIndex.CountItemsAt(new int2(2, 3)));
    }

    [Test]
    public void Test04_TransferOwnership_UpdatesOwnershipAndExcludesFromSpatialIndex()
    {
        // Arrange: 월드 아이템 및 가상 창고 생성
        var reqEntity = _entityManager.CreateEntity(typeof(SpawnItemRequest));
        _entityManager.SetComponentData(reqEntity, new SpawnItemRequest
        {
            ItemType = ItemTypeEnum.Stone,
            Position = new int2(8, 8),
            TargetOwner = Entity.Null
        });
        UpdateStateApplyPhase();
        UpdateSynchronizationPhase();

        var item = _entityManager.CreateEntityQuery(typeof(ItemIdentity)).GetSingletonEntity();
        var mockStorage = _entityManager.CreateEntity();

        // Act: 소유권 이전 요청 발행 및 실행
        _entityManager.SetComponentEnabled<TransferOwnershipRequest>(item, true);
        _entityManager.SetComponentData(item, new TransferOwnershipRequest { TargetOwner = mockStorage });

        UpdateStateApplyPhase();
        UpdateSynchronizationPhase();

        // Assert: 소유권이 창고로 이전되고, 요청 비활성화 확인
        var ownership = _entityManager.GetComponentData<ItemOwnership>(item);
        Assert.IsFalse(ownership.IsWorldItem);
        Assert.AreEqual(mockStorage, ownership.Owner);
        Assert.IsFalse(_entityManager.IsComponentEnabled<TransferOwnershipRequest>(item), "Request must be disabled (Consume-on-Apply).");

        // Assert: 보관 아이템은 공간 인덱스에서 자동 배제 확인
        var spatialIndex = _entityManager.CreateEntityQuery(typeof(ItemSpatialIndex)).GetSingleton<ItemSpatialIndex>();
        Assert.IsFalse(spatialIndex.HasItemAt(new int2(8, 8)), "Stored item must not appear in spatial index.");
    }

    [Test]
    public void Test05_ReleaseToWorld_RestoresSpatialIndex()
    {
        // Arrange: 창고에 보관된 아이템 생성
        var mockStorage = _entityManager.CreateEntity();
        var reqEntity = _entityManager.CreateEntity(typeof(SpawnItemRequest));
        _entityManager.SetComponentData(reqEntity, new SpawnItemRequest
        {
            ItemType = ItemTypeEnum.Iron,
            Position = new int2(15, 25),
            TargetOwner = mockStorage
        });
        UpdateStateApplyPhase();
        UpdateSynchronizationPhase();

        var item = _entityManager.CreateEntityQuery(typeof(ItemIdentity)).GetSingletonEntity();
        var spatialIndex = _entityManager.CreateEntityQuery(typeof(ItemSpatialIndex)).GetSingleton<ItemSpatialIndex>();
        Assert.IsFalse(spatialIndex.HasItemAt(new int2(15, 25)), "Stored item must not be indexed initially.");

        // Act: 월드로 방출 (TargetOwner = Entity.Null)
        _entityManager.SetComponentEnabled<TransferOwnershipRequest>(item, true);
        _entityManager.SetComponentData(item, new TransferOwnershipRequest { TargetOwner = Entity.Null });

        UpdateStateApplyPhase();
        UpdateSynchronizationPhase();

        // Assert: 월드 아이템으로 복귀 및 공간 인덱스 재등록 확인
        var ownership = _entityManager.GetComponentData<ItemOwnership>(item);
        Assert.IsTrue(ownership.IsWorldItem);
        Assert.IsTrue(spatialIndex.HasItemAt(new int2(15, 25)), "Released world item must be restored in spatial index.");
    }

    [Test]
    public void Test06_DestroyItem_RemovesEntityAndCleansSpatialIndex()
    {
        // Arrange
        var reqEntity = _entityManager.CreateEntity(typeof(SpawnItemRequest));
        _entityManager.SetComponentData(reqEntity, new SpawnItemRequest
        {
            ItemType = ItemTypeEnum.Copper,
            Position = new int2(30, 30),
            TargetOwner = Entity.Null
        });
        UpdateStateApplyPhase();
        UpdateSynchronizationPhase();

        var item = _entityManager.CreateEntityQuery(typeof(ItemIdentity)).GetSingletonEntity();

        // Act: 파괴 요청 발행 및 파이프라인 실행
        _entityManager.SetComponentEnabled<DestroyItemRequest>(item, true);
        UpdateStateApplyPhase();
        UpdateSynchronizationPhase();

        // Assert: 아이템 엔티티 파괴 및 공간 인덱스 정리 확인
        Assert.IsFalse(_entityManager.Exists(item), "Item entity must be destroyed.");
        var spatialIndex = _entityManager.CreateEntityQuery(typeof(ItemSpatialIndex)).GetSingleton<ItemSpatialIndex>();
        Assert.IsFalse(spatialIndex.HasItemAt(new int2(30, 30)), "Destroyed item must be removed from spatial index.");
    }

    [Test]
    public void Test07_GhostOwner_TransferRequestIsIgnoredAndDroppedSafely()
    {
        // Arrange
        var reqEntity = _entityManager.CreateEntity(typeof(SpawnItemRequest));
        _entityManager.SetComponentData(reqEntity, new SpawnItemRequest
        {
            ItemType = ItemTypeEnum.Drone,
            Position = new int2(50, 50),
            TargetOwner = Entity.Null
        });
        UpdateStateApplyPhase();
        UpdateSynchronizationPhase();

        var item = _entityManager.CreateEntityQuery(typeof(ItemIdentity)).GetSingletonEntity();
        var ghostEntity = new Entity { Index = 99999, Version = 1 };

        // Act: 존재하지 않는 유령 엔티티로의 이전 요청
        _entityManager.SetComponentEnabled<TransferOwnershipRequest>(item, true);
        _entityManager.SetComponentData(item, new TransferOwnershipRequest { TargetOwner = ghostEntity });

        UpdateStateApplyPhase();
        UpdateSynchronizationPhase();

        // Assert: 기존 월드 아이템 유지 및 요청은 안전하게 비활성화
        var ownership = _entityManager.GetComponentData<ItemOwnership>(item);
        Assert.IsTrue(ownership.IsWorldItem, "Ghost owner must be rejected; item remains WorldItem.");
        Assert.IsFalse(_entityManager.IsComponentEnabled<TransferOwnershipRequest>(item), "Request must be consumed (disabled).");
    }
}
