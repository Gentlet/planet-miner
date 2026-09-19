using System.IO;
using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Task 3.4: Storage Buffer Invariant 검증 테스트.
/// 창고 버퍼(DynamicBuffer<StoredItemElement>)와 ItemOwnership 간의 양방향 일관성,
/// 슬롯 범위, 슬롯 단일 품목 규칙, MaxStack 한도, StorageFilter 준수,
/// 그리고 미소비 확정 결정(Decision) 잔류 감시를 단위 및 통합 검증합니다.
/// </summary>
public class Phase3StorageInvariantTests : EcsWorldTestFixture
{
    private SystemHandle _beltSpatialSyncHandle;
    private SystemHandle _itemSpatialSyncHandle;
    private SystemHandle _buildingSpatialSyncHandle;

    private SystemHandle _buildingInputDecisionHandle;
    private SystemHandle _buildingOutputDecisionHandle;
    private SystemHandle _storageReservationHandle;
    private SystemHandle _storageApplyHandle;
    private SystemHandle _ownershipApplyHandle;

    private WorldInvariantValidationSystem _invariantValidationSystem;
    private string _logDirectory;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();

        _beltSpatialSyncHandle = _world.GetOrCreateSystem(typeof(BeltSpatialSyncSystem));
        _itemSpatialSyncHandle = _world.GetOrCreateSystem(typeof(ItemSpatialSyncSystem));
        _buildingSpatialSyncHandle = _world.GetOrCreateSystem(typeof(BuildingSpatialSyncSystem));

        _buildingInputDecisionHandle = _world.GetOrCreateSystem(typeof(BuildingItemInputDecisionSystem));
        _buildingOutputDecisionHandle = _world.GetOrCreateSystem(typeof(BuildingItemOutputDecisionSystem));
        _storageReservationHandle = _world.GetOrCreateSystem(typeof(BuildingStorageInputReservationSystem));
        _storageApplyHandle = _world.GetOrCreateSystem(typeof(BuildingItemStorageApplySystem));
        _ownershipApplyHandle = _world.GetOrCreateSystem(typeof(ItemOwnershipApplySystem));

        _invariantValidationSystem = _world.GetOrCreateSystemManaged<WorldInvariantValidationSystem>();
        _invariantValidationSystem.ResetViolationCount();

        _logDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "InvariantErrors"));
        CleanLogDirectory();
    }

    [TearDown]
    public override void TearDown()
    {
        CleanLogDirectory();
        base.TearDown();
    }

    private void CleanLogDirectory()
    {
        try
        {
            if (Directory.Exists(_logDirectory))
            {
                var files = Directory.GetFiles(_logDirectory, "invariant_error_*.txt");
                foreach (var file in files)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // 파일 잠금 예외 무시
        }
    }

    private Entity CreateStorage(int2 position, int2 size, int slotCount = 8, StorageFilter? filter = null)
    {
        var entity = _entityManager.CreateEntity(
            typeof(BuildingType),
            typeof(BuildingFootprint),
            typeof(GridPosition),
            typeof(Direction),
            typeof(Storage),
            typeof(BuildingItemOutputDecision));

        _entityManager.SetComponentData(entity, new BuildingType(BuildingTypeEnum.Storage));
        _entityManager.SetComponentData(entity, new BuildingFootprint(size));
        _entityManager.SetComponentData(entity, new GridPosition(position));
        _entityManager.SetComponentData(entity, new Direction(DirectionEnum.Up));
        _entityManager.SetComponentData(entity, new Storage(slotCount));
        _entityManager.SetComponentData(entity, new BuildingItemOutputDecision(false, Entity.Null, int2.zero));
        _entityManager.SetComponentEnabled<BuildingItemOutputDecision>(entity, false);

        _entityManager.AddBuffer<StoredItemElement>(entity);

        if (filter.HasValue)
        {
            _entityManager.AddComponentData(entity, filter.Value);
        }

        return entity;
    }

    private Entity CreateBelt(int2 position, DirectionEnum direction, float speed = 2.0f)
    {
        var entity = _entityManager.CreateEntity(
            typeof(GridPosition),
            typeof(Direction),
            typeof(BeltComponent));

        _entityManager.SetComponentData(entity, new GridPosition(position));
        _entityManager.SetComponentData(entity, new Direction(direction));
        _entityManager.SetComponentData(entity, new BeltComponent(speed));
        return entity;
    }

    private Entity CreateBeltItem(int2 position, DirectionEnum direction, float progress, ItemTypeEnum itemType = ItemTypeEnum.Iron_Ore)
    {
        var entity = _entityManager.CreateEntity(
            typeof(ItemIdentity),
            typeof(ItemOwnership),
            typeof(GridPosition),
            typeof(Direction),
            typeof(LocalTransform),
            typeof(BeltMovementState),
            typeof(BeltMovementDecision),
            typeof(BuildingItemInputDecision),
            typeof(TransferOwnershipRequest));

        _entityManager.SetComponentData(entity, new ItemIdentity(itemType));
        _entityManager.SetComponentData(entity, ItemOwnership.WorldItem);
        _entityManager.SetComponentData(entity, new GridPosition(position));
        _entityManager.SetComponentData(entity, new Direction(direction));
        _entityManager.SetComponentData(entity, LocalTransform.FromPosition(new float3(position.x, position.y, 0f)));
        _entityManager.SetComponentData(entity, new BeltMovementState(progress));
        _entityManager.SetComponentData(entity, new BeltMovementDecision(0f, false));
        _entityManager.SetComponentData(entity, new BuildingItemInputDecision(Entity.Null, false, -1));
        _entityManager.SetComponentData(entity, new TransferOwnershipRequest(Entity.Null));

        _entityManager.SetComponentEnabled<BeltMovementState>(entity, true);
        _entityManager.SetComponentEnabled<BeltMovementDecision>(entity, true);
        _entityManager.SetComponentEnabled<BuildingItemInputDecision>(entity, false);
        _entityManager.SetComponentEnabled<TransferOwnershipRequest>(entity, false);

        return entity;
    }

    private Entity CreateStoredItem(Entity storageEntity, ItemTypeEnum itemType, int slotIndex)
    {
        var entity = _entityManager.CreateEntity(
            typeof(ItemIdentity),
            typeof(ItemOwnership),
            typeof(GridPosition),
            typeof(Direction),
            typeof(LocalTransform),
            typeof(BeltMovementState),
            typeof(BeltMovementDecision),
            typeof(BuildingItemInputDecision),
            typeof(TransferOwnershipRequest));

        _entityManager.SetComponentData(entity, new ItemIdentity(itemType));
        _entityManager.SetComponentData(entity, ItemOwnership.Stored(storageEntity));
        _entityManager.SetComponentData(entity, new GridPosition(int2.zero));
        _entityManager.SetComponentData(entity, new Direction(DirectionEnum.Up));
        _entityManager.SetComponentData(entity, LocalTransform.Identity);
        _entityManager.SetComponentData(entity, new BeltMovementState(0f));
        _entityManager.SetComponentData(entity, new BeltMovementDecision(0f, false));
        _entityManager.SetComponentData(entity, new BuildingItemInputDecision(Entity.Null, false, -1));
        _entityManager.SetComponentData(entity, new TransferOwnershipRequest(Entity.Null));

        _entityManager.SetComponentEnabled<BeltMovementState>(entity, false);
        _entityManager.SetComponentEnabled<BeltMovementDecision>(entity, false);
        _entityManager.SetComponentEnabled<BuildingItemInputDecision>(entity, false);
        _entityManager.SetComponentEnabled<TransferOwnershipRequest>(entity, false);

        var buffer = _entityManager.GetBuffer<StoredItemElement>(storageEntity);
        buffer.Add(new StoredItemElement(entity, itemType, slotIndex));

        return entity;
    }

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

    [Test]
    public void Test01_ValidStoragePipeline_PassesAllStorageInvariants()
    {
        // Arrange: 정상 입고 파이프라인 구성
        CreateBelt(new int2(0, 0), DirectionEnum.Right);
        var storage = CreateStorage(new int2(1, 0), new int2(1, 1), slotCount: 4);
        var item = CreateBeltItem(new int2(0, 0), DirectionEnum.Right, 0.999f, ItemTypeEnum.Iron_Ore);

        SyncAllSpatialIndices();

        // Act: 입고 파이프라인 실행
        _buildingInputDecisionHandle.Update(_world.Unmanaged);
        _storageReservationHandle.Update(_world.Unmanaged);
        _storageApplyHandle.Update(_world.Unmanaged);
        _ownershipApplyHandle.Update(_world.Unmanaged);
        SyncAllSpatialIndices();

        // Invariant 검증 시스템 실행
        _invariantValidationSystem.Update();

        // Assert: 무결성 위반 0건 확인
        Assert.AreEqual(0, _invariantValidationSystem.TotalViolationCount, "Valid storage deposit should have 0 invariant violations.");
    }

    [Test]
    public void Test02_OrphanStoredItem_ViolatesInvariant()
    {
        // Arrange: 아이템은 창고를 Owner로 가리키지만, 창고 버퍼에는 아이템이 누락된 고아 상태 주입
        var storage = CreateStorage(new int2(1, 0), new int2(1, 1), slotCount: 4);
        var orphanItem = _entityManager.CreateEntity(typeof(ItemIdentity), typeof(ItemOwnership));
        _entityManager.SetComponentData(orphanItem, new ItemIdentity(ItemTypeEnum.Iron_Ore));
        _entityManager.SetComponentData(orphanItem, ItemOwnership.Stored(storage));

        SyncAllSpatialIndices();

        // Act
        _invariantValidationSystem.Update();

        // Assert: 고아 수납 아이템 위반 감지 확인
        Assert.GreaterOrEqual(_invariantValidationSystem.TotalViolationCount, 1, "Orphan stored item must be detected as an invariant violation.");
    }

    [Test]
    public void Test03_GhostBufferItem_ViolatesInvariant()
    {
        // Arrange: 창고 버퍼에는 등록되었으나, 파괴된 유령 엔티티를 가리키는 결함 주입
        var storage = CreateStorage(new int2(1, 0), new int2(1, 1), slotCount: 4);
        var tempItem = _entityManager.CreateEntity(typeof(ItemIdentity), typeof(ItemOwnership));
        _entityManager.SetComponentData(tempItem, new ItemIdentity(ItemTypeEnum.Iron_Ore));
        _entityManager.SetComponentData(tempItem, ItemOwnership.Stored(storage));

        var buffer = _entityManager.GetBuffer<StoredItemElement>(storage);
        buffer.Add(new StoredItemElement(tempItem, ItemTypeEnum.Iron_Ore, 0));

        // 아이템 엔티티 파괴 -> 유령 포인터 발생
        _entityManager.DestroyEntity(tempItem);

        SyncAllSpatialIndices();

        // Act
        _invariantValidationSystem.Update();

        // Assert: 유령 버퍼 아이템 위반 감지 확인
        Assert.GreaterOrEqual(_invariantValidationSystem.TotalViolationCount, 1, "Ghost buffer item must be detected as an invariant violation.");
    }

    [Test]
    public void Test04_SlotRangeAndPollution_ViolatesInvariant()
    {
        // Arrange: 슬롯 수용량(SlotCount=2)을 초과하는 SlotIndex(5)와 동일 슬롯 내 품목 혼합 주입
        var storage = CreateStorage(new int2(1, 0), new int2(1, 1), slotCount: 2);
        
        // 결함 1: SlotIndex 초과
        CreateStoredItem(storage, ItemTypeEnum.Iron_Ore, slotIndex: 5);

        // 결함 2: 슬롯 0에 서로 다른 품목 혼합
        CreateStoredItem(storage, ItemTypeEnum.Iron_Ore, slotIndex: 0);
        CreateStoredItem(storage, ItemTypeEnum.Copper_Ore, slotIndex: 0);

        SyncAllSpatialIndices();

        // Act
        _invariantValidationSystem.Update();

        // Assert: 슬롯 인덱스 초과 및 슬롯 품목 혼합 위반 감지 확인
        Assert.GreaterOrEqual(_invariantValidationSystem.TotalViolationCount, 2, "Invalid slot index and slot pollution must be detected as violations.");
    }

    [Test]
    public void Test05_SlotStackLimitExceeded_ViolatesInvariant()
    {
        // Arrange: 단일 슬롯에 MaxStack(50)을 초과하는 51개 아이템 주입
        var storage = CreateStorage(new int2(1, 0), new int2(1, 1), slotCount: 2);

        for (int i = 0; i < 51; i++)
        {
            CreateStoredItem(storage, ItemTypeEnum.Iron_Ore, slotIndex: 0);
        }

        SyncAllSpatialIndices();

        // Act
        _invariantValidationSystem.Update();

        // Assert: 슬롯 스택 상한 초과 위반 감지 확인
        Assert.GreaterOrEqual(_invariantValidationSystem.TotalViolationCount, 1, "Stack limit exceeded must be detected as an invariant violation.");
    }

    [Test]
    public void Test06_DisallowedFilterItem_ViolatesInvariant()
    {
        // Arrange: Iron_Ore만 허용하는 Whitelist 필터 창고에 Copper_Ore 보관 결함 주입
        var filter = new StorageFilter(StorageFilterMode.Whitelist);
        filter.Mask.Set((byte)ItemTypeEnum.Iron_Ore, true);

        var storage = CreateStorage(new int2(1, 0), new int2(1, 1), slotCount: 4, filter: filter);
        CreateStoredItem(storage, ItemTypeEnum.Copper_Ore, slotIndex: 0);

        SyncAllSpatialIndices();

        // Act
        _invariantValidationSystem.Update();

        // Assert: 필터 위반 아이템 감지 확인
        Assert.GreaterOrEqual(_invariantValidationSystem.TotalViolationCount, 1, "Disallowed filter item must be detected as an invariant violation.");
    }

    [Test]
    public void Test07_UnconsumedDecisions_ViolatesLifecycleInvariant()
    {
        // Arrange: 프레임 끝에 소비되지 않고 남은 확정 입출력 Decision 주입
        var storage = CreateStorage(new int2(1, 0), new int2(1, 1), slotCount: 4);
        var item = CreateBeltItem(new int2(0, 0), DirectionEnum.Right, 0.999f, ItemTypeEnum.Iron_Ore);

        // 결함 1: 확정된 입고 결정 미소비 잔류
        _entityManager.SetComponentData(item, new BuildingItemInputDecision(storage, true, 0));
        _entityManager.SetComponentEnabled<BuildingItemInputDecision>(item, true);

        // 결함 2: 확정된 출고 결정 미소비 잔류
        _entityManager.SetComponentData(storage, new BuildingItemOutputDecision(true, item, new int2(0, 0)));
        _entityManager.SetComponentEnabled<BuildingItemOutputDecision>(storage, true);

        SyncAllSpatialIndices();

        // Act: StateApplyGroup을 거치지 않고 바로 Invariant 검증 실행
        _invariantValidationSystem.Update();

        // Assert: 미소비 확정 입출력 Decision 감시 위반 확인 (최소 2건)
        Assert.GreaterOrEqual(_invariantValidationSystem.TotalViolationCount, 2, "Unconsumed confirmed decisions must be detected as invariant violations.");
    }
}
