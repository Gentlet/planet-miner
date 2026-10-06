using System.Collections.Generic;
using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>
/// 역할·목적: 건물/라우터 벨트 입구 경합과 공통 진입 정책에 대한 NUnit EditMode 회귀 검증.
/// 입력·검사: PlacementStamp·점유/간격·후보를 준비해 Decision/Reservation/Apply의 승인과 실물 위치가 일치하는지 검사한다.
/// 수명: EcsWorldTestFixture가 각 사례의 독립 World를 준비하고 종료 시 해제한다.
/// </summary>
public class Phase6BeltDestinationReservationTests : EcsWorldTestFixture
{
    private SystemHandle _beltSpatialSyncHandle;
    private SystemHandle _itemSpatialSyncHandle;
    private SystemHandle _reservationHandle;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();

        _beltSpatialSyncHandle = _world.GetOrCreateSystem(typeof(BeltSpatialSyncSystem));
        _itemSpatialSyncHandle = _world.GetOrCreateSystem(typeof(ItemSpatialSyncSystem));
        _reservationHandle = _world.GetOrCreateSystem(typeof(BeltDestinationReservationSystem));
    }

    private void SyncSpatialIndices()
    {
        _beltSpatialSyncHandle.Update(_world.Unmanaged);
        _itemSpatialSyncHandle.Update(_world.Unmanaged);
    }

    private void RunReservation()
    {
        _reservationHandle.Update(_world.Unmanaged);
    }

    [Test]
    public void Test02_MultipleBuildingOutputs_PlacementStampArbitration()
    {
        // Arrange: 대상 벨트 (0,0)로 두 창고가 동시 출고 시도
        var belt = Entities.CreateBelt(new int2(0, 0), DirectionEnum.Right);

        // 창고 1 (Tick=10)
        var storage1 = Entities.CreateStorage(new int2(0, 1), new int2(1, 1));
        _entityManager.AddComponentData(storage1, new PlacementStamp(tick: 10, order: 0));
        var item1 = Entities.CreateBeltItem(new int2(0, 1), DirectionEnum.Down, progress: 0.0f);
        _entityManager.SetComponentData(storage1, new BuildingItemOutputDecision(canOutput: true, item1, new int2(0, 0)));
        _entityManager.SetComponentEnabled<BuildingItemOutputDecision>(storage1, true);

        // 창고 2 (Tick=20)
        var storage2 = Entities.CreateStorage(new int2(0, -1), new int2(1, 1));
        _entityManager.AddComponentData(storage2, new PlacementStamp(tick: 20, order: 0));
        var item2 = Entities.CreateBeltItem(new int2(0, -1), DirectionEnum.Up, progress: 0.0f);
        _entityManager.SetComponentData(storage2, new BuildingItemOutputDecision(canOutput: true, item2, new int2(0, 0)));
        _entityManager.SetComponentEnabled<BuildingItemOutputDecision>(storage2, true);

        SyncSpatialIndices();

        // Act
        RunReservation();

        // Assert: 창고 1(Tick 10) 승인, 창고 2(Tick 20) 탈락
        var out1 = _entityManager.GetComponentData<BuildingItemOutputDecision>(storage1);
        Assert.IsTrue(out1.CanOutput);
        Assert.IsTrue(_entityManager.IsComponentEnabled<BuildingItemOutputDecision>(storage1));

        var out2 = _entityManager.GetComponentData<BuildingItemOutputDecision>(storage2);
        Assert.IsFalse(out2.CanOutput);
        Assert.IsFalse(_entityManager.IsComponentEnabled<BuildingItemOutputDecision>(storage2));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Test03_BuildingOutput_Vs_RoutingTransfer_PlacementStampArbitration(bool routingWins)
    {
        // Arrange: 대상 벨트 (0,0)로 Splitter와 창고가 동시 진입 시도
        var targetBelt = Entities.CreateBelt(new int2(0, 0), DirectionEnum.Right);
        var sourceBelt = Entities.CreateBelt(new int2(-2, 0), DirectionEnum.Right);

        // 출고와 Routing의 승패를 각각 검증한다.
        var routerEntity = _entityManager.CreateEntity(
            typeof(BuildingType),
            typeof(GridPosition),
            typeof(SplitterRoutingState),
            typeof(RoutingTransferDecision),
            typeof(PlacementStamp));
        _entityManager.SetComponentData(routerEntity, new BuildingType(BuildingTypeEnum.Splitter));
        _entityManager.SetComponentData(routerEntity, new GridPosition(new int2(-1, 0)));
        _entityManager.SetComponentData(routerEntity, new PlacementStamp(tick: routingWins ? 10UL : 20UL, order: 0));
        _entityManager.SetComponentData(routerEntity, new SplitterRoutingState(sourceBelt, DirectionEnum.Right));

        var routeItem = Entities.CreateBeltItem(new int2(-2, 0), DirectionEnum.Right, progress: 1.0f);
        _entityManager.SetComponentData(routerEntity, new RoutingTransferDecision(routeItem, sourceBelt, targetBelt));
        _entityManager.SetComponentEnabled<RoutingTransferDecision>(routerEntity, true);

        // 실제 소유 버퍼를 구성해 거절 후 원본 보존과 승인 후 소유권 전환을 확인한다.
        var storage = Entities.CreateStorage(new int2(0, 1), new int2(1, 1));
        _entityManager.AddComponentData(storage, new PlacementStamp(tick: routingWins ? 20UL : 10UL, order: 0));
        var storageItem = Entities.CreateStoredItem(storage, ItemTypeEnum.Iron_Ore);
        _entityManager.SetComponentData(storageItem, new GridPosition(new int2(0, 1)));
        _entityManager.AddComponent<DisableRendering>(storageItem);
        _entityManager.SetComponentData(storage, new BuildingItemOutputDecision(canOutput: true, storageItem, new int2(0, 0)));
        _entityManager.SetComponentEnabled<BuildingItemOutputDecision>(storage, true);

        SyncSpatialIndices();

        // Act
        RunReservation();

        // 후보를 직접 주입하여 예약/반영 계약을 검증한다. 배치·포트 탐색 검증은 Test07에서 수행한다.
        Assert.AreEqual(routingWins, _entityManager.IsComponentEnabled<RoutingTransferDecision>(routerEntity));

        var storageOut = _entityManager.GetComponentData<BuildingItemOutputDecision>(storage);
        Assert.AreEqual(!routingWins, storageOut.CanOutput);
        Assert.AreEqual(!routingWins, _entityManager.IsComponentEnabled<BuildingItemOutputDecision>(storage));

        ApplyEntriesAndSync();

        Assert.AreEqual(routingWins, IsItemAtTarget(routeItem, int2.zero));
        Assert.AreEqual(!routingWins, IsItemAtTarget(storageItem, int2.zero));
        Assert.AreEqual(routingWins ? int2.zero : new int2(-2, 0),
            _entityManager.GetComponentData<GridPosition>(routeItem).Value);
        Assert.AreEqual(routingWins ? new int2(0, 1) : int2.zero,
            _entityManager.GetComponentData<GridPosition>(storageItem).Value);
        Assert.AreEqual(routingWins ? 0f : 1f,
            _entityManager.GetComponentData<BeltMovementState>(routeItem).Progress);
        Assert.AreEqual(0f, _entityManager.GetComponentData<BeltMovementState>(storageItem).Progress);
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<ItemOwnership>(routeItem).Owner);
        Assert.AreEqual(routingWins ? storage : Entity.Null,
            _entityManager.GetComponentData<ItemOwnership>(storageItem).Owner);
        Assert.AreEqual(routingWins ? 1 : 0, _entityManager.GetBuffer<StoredItemElement>(storage).Length);
        Assert.AreEqual(routingWins ? 1 : 0,
            _entityManager.GetComponentData<SplitterRoutingState>(routerEntity).OutputCursor);
        Assert.AreEqual(routingWins, _entityManager.HasComponent<DisableRendering>(storageItem));
        Assert.IsFalse(IsEntryDecisionEnabled(storage));
        Assert.IsFalse(IsEntryDecisionEnabled(routerEntity));
        Assert.IsFalse(_entityManager.IsComponentEnabled<TransferOwnershipRequest>(storageItem));
    }

    [Test]
    public void Test04_TargetSpaceNotAvailable_RejectsOutput()
    {
        // Arrange: 대상 벨트에 이미 아이템이 있고 progress=0.1f (여유 간격 0.25f 미만)
        var belt = Entities.CreateBelt(new int2(0, 0), DirectionEnum.Right);
        var blocker = Entities.CreateBeltItem(new int2(0, 0), DirectionEnum.Right, progress: 0.1f);

        // 창고 출고 시도
        var storage = Entities.CreateStorage(new int2(0, 1), new int2(1, 1));
        var item = Entities.CreateBeltItem(new int2(0, 1), DirectionEnum.Down, progress: 0.0f);
        _entityManager.SetComponentData(storage, new BuildingItemOutputDecision(canOutput: true, item, new int2(0, 0)));
        _entityManager.SetComponentEnabled<BuildingItemOutputDecision>(storage, true);

        SyncSpatialIndices();

        // Act
        RunReservation();

        // Assert: 공간 부족으로 출고 거부
        var outputDec = _entityManager.GetComponentData<BuildingItemOutputDecision>(storage);
        Assert.IsFalse(outputDec.CanOutput);
        Assert.IsFalse(_entityManager.IsComponentEnabled<BuildingItemOutputDecision>(storage));
    }

    [Test]
    public void Test05_TargetSpaceAvailable_AllowsOutput()
    {
        // Arrange: 대상 벨트에 기존 아이템이 있지만 이미 progress=0.5f로 멀리 전진해 있음 (여유 간격 >= 0.25f)
        var belt = Entities.CreateBelt(new int2(0, 0), DirectionEnum.Right);
        var existingItem = Entities.CreateBeltItem(new int2(0, 0), DirectionEnum.Right, progress: 0.5f);

        // 창고 출고 시도
        var storage = Entities.CreateStorage(new int2(0, 1), new int2(1, 1));
        var item = Entities.CreateBeltItem(new int2(0, 1), DirectionEnum.Down, progress: 0.0f);
        _entityManager.SetComponentData(storage, new BuildingItemOutputDecision(canOutput: true, item, new int2(0, 0)));
        _entityManager.SetComponentEnabled<BuildingItemOutputDecision>(storage, true);

        SyncSpatialIndices();

        // Act
        RunReservation();

        // Assert: 공간이 충분하므로 출고 승인
        var outputDec = _entityManager.GetComponentData<BuildingItemOutputDecision>(storage);
        Assert.IsTrue(outputDec.CanOutput);
        Assert.IsTrue(_entityManager.IsComponentEnabled<BuildingItemOutputDecision>(storage));
    }

    [Test]
    public void Test06_NoRequests_EarlyExitWithoutError()
    {
        // Arrange: 출고/전달 요청이 전혀 없는 상태
        var belt = Entities.CreateBelt(new int2(0, 0), DirectionEnum.Right);
        SyncSpatialIndices();

        // Act & Assert: 에러 없이 정상 조기 반환
        Assert.DoesNotThrow(() => RunReservation());
    }

    /// <summary>
    /// 역할·목적: 벨트 진입 회귀의 목표 셀 점유 fixture를 분류한다.
    /// 테스트 입력을 만드는 분기 값이며 제품 도메인 상태/엔티티로 게시하지 않는다.
    /// </summary>
    public enum TargetOccupancy
    {
        EnabledWorld,
        DisabledWorld,
        WorldWithoutMovement,
        Stored
    }

    private static IEnumerable<TestCaseData> EntryPolicyCases()
    {
        var sourceTypes = new[]
        {
            BuildingTypeEnum.Storage, BuildingTypeEnum.Miner,
            BuildingTypeEnum.Splitter, BuildingTypeEnum.Merger
        };

        float spacing = GameConstants.ItemSpacing;
        float epsilon = GameConstants.AlignmentEpsilon;
        int capacity = GameConstants.MaxItemsPerBeltTile;

        foreach (var sourceType in sourceTypes)
        {
            yield return new TestCaseData(sourceType, TargetOccupancy.EnabledWorld, 0f, 1, false);
            yield return new TestCaseData(sourceType, TargetOccupancy.EnabledWorld, spacing - 2f * epsilon, 1, false);
            yield return new TestCaseData(sourceType, TargetOccupancy.EnabledWorld, spacing - epsilon, 1, true);
            yield return new TestCaseData(sourceType, TargetOccupancy.EnabledWorld, spacing - 0.5f * epsilon, 1, true);
            yield return new TestCaseData(sourceType, TargetOccupancy.EnabledWorld, spacing, 1, true);
            yield return new TestCaseData(sourceType, TargetOccupancy.EnabledWorld, spacing, capacity - 1, true);
            yield return new TestCaseData(sourceType, TargetOccupancy.EnabledWorld, spacing, capacity, false);
            yield return new TestCaseData(sourceType, TargetOccupancy.DisabledWorld, 0f, capacity, true);
            yield return new TestCaseData(sourceType, TargetOccupancy.WorldWithoutMovement, 0f, capacity, true);
            yield return new TestCaseData(sourceType, TargetOccupancy.Stored, 0f, capacity, true);
        }
    }

    [TestCaseSource(nameof(EntryPolicyCases))]
    public void Test07_EntryPolicy_DecisionReservationAndApplyAgree(
        BuildingTypeEnum sourceType,
        TargetOccupancy occupancy,
        float firstProgress,
        int occupyingItemCount,
        bool shouldEnter)
    {
        int2 targetPosition = new int2(1, 0);
        Entity targetBelt = Entities.CreateBelt(targetPosition, DirectionEnum.Right);
        var source = CreateEntrySource(sourceType, targetBelt);
        CreateTargetOccupants(occupancy, targetPosition, firstProgress, occupyingItemCount);

        int2 originalPosition = _entityManager.GetComponentData<GridPosition>(source.Item).Value;
        float originalProgress = _entityManager.GetComponentData<BeltMovementState>(source.Item).Progress;
        Entity originalOwner = _entityManager.GetComponentData<ItemOwnership>(source.Item).Owner;
        float3 originalVisualPosition = _entityManager.GetComponentData<LocalTransform>(source.Item).Position;

        SyncSpatialIndices();
        Simulation.UpdateAndComplete(source.DecisionSystem);
        Assert.AreEqual(shouldEnter, IsEntryDecisionEnabled(source.Source), "Decision must use the shared entry policy.");

        Simulation.UpdateAndComplete(_reservationHandle);
        Assert.AreEqual(shouldEnter, IsEntryDecisionEnabled(source.Source), "Reservation must agree with the single candidate's precheck.");

        ApplyEntriesAndSync();

        Assert.IsFalse(IsEntryDecisionEnabled(source.Source), "A rejected or applied frame decision must be disabled.");
        Assert.IsFalse(_entityManager.IsComponentEnabled<TransferOwnershipRequest>(source.Item));
        Assert.AreEqual(0f, _entityManager.GetComponentData<BeltMovementDecision>(source.Item).PlannedProgress);
        Assert.AreEqual(shouldEnter ? targetPosition : originalPosition,
            _entityManager.GetComponentData<GridPosition>(source.Item).Value);
        Assert.AreEqual(shouldEnter ? 0f : originalProgress,
            _entityManager.GetComponentData<BeltMovementState>(source.Item).Progress);
        Assert.AreEqual(shouldEnter ? Entity.Null : originalOwner,
            _entityManager.GetComponentData<ItemOwnership>(source.Item).Owner);
        Assert.AreEqual(shouldEnter, IsItemAtTarget(source.Item, targetPosition));

        bool isRouter = sourceType == BuildingTypeEnum.Splitter || sourceType == BuildingTypeEnum.Merger;
        Assert.AreEqual(shouldEnter || isRouter, _entityManager.IsComponentEnabled<BeltMovementState>(source.Item));
        if (!shouldEnter)
        {
            Assert.AreEqual(originalVisualPosition, _entityManager.GetComponentData<LocalTransform>(source.Item).Position);
        }

        if (sourceType == BuildingTypeEnum.Splitter)
        {
            Assert.AreEqual(shouldEnter ? 1 : 0, _entityManager.GetComponentData<SplitterRoutingState>(source.Source).OutputCursor);
        }
        else if (sourceType == BuildingTypeEnum.Merger)
        {
            Assert.AreEqual(shouldEnter ? 1 : 0, _entityManager.GetComponentData<MergerRoutingState>(source.Source).InputCursor);
        }
        else
        {
            int remainingCount = sourceType == BuildingTypeEnum.Storage
                ? _entityManager.GetBuffer<StoredItemElement>(source.Source).Length
                : _entityManager.GetBuffer<ProductItemElement>(source.Source).Length;
            Assert.AreEqual(shouldEnter ? 0 : 1, remainingCount);
            Assert.AreEqual(!shouldEnter, _entityManager.HasComponent<DisableRendering>(source.Item));
        }
    }

    private (Entity Source, Entity Item, SystemHandle DecisionSystem) CreateEntrySource(
        BuildingTypeEnum sourceType, Entity targetBelt)
    {
        if (sourceType == BuildingTypeEnum.Storage || sourceType == BuildingTypeEnum.Miner)
        {
            Entity source = Entities.CreateStorage(int2.zero, new int2(1, 1), DirectionEnum.Right);
            Entity item = Entities.CreateStoredItem(source, ItemTypeEnum.Iron_Ore);
            _entityManager.AddComponent<DisableRendering>(item);

            if (sourceType == BuildingTypeEnum.Storage)
            {
                return (source, item, _world.GetOrCreateSystem<StorageItemOutputDecisionSystem>());
            }

            _entityManager.SetComponentData(source, new BuildingType(sourceType));
            _entityManager.GetBuffer<StoredItemElement>(source).Clear();
            _entityManager.AddBuffer<ProductItemElement>(source)
                .Add(new ProductItemElement(item, ItemTypeEnum.Iron_Ore));
            return (source, item, _world.GetOrCreateSystem<ProductItemOutputDecisionSystem>());
        }

        Entity inputBelt = Entities.CreateBelt(new int2(-1, 0), DirectionEnum.Right);
        Entity router = Entities.CreateBuilding(sourceType, int2.zero, new int2(1, 1), DirectionEnum.Right);
        Entity routedItem = Entities.CreateBeltItem(new int2(-1, 0), DirectionEnum.Right, 1f);
        _entityManager.AddComponentData(router, new RoutingTransferDecision(Entity.Null, Entity.Null, Entity.Null));
        _entityManager.SetComponentEnabled<RoutingTransferDecision>(router, false);

        if (sourceType == BuildingTypeEnum.Splitter)
        {
            _entityManager.AddComponentData(router, new SplitterRoutingState(inputBelt, DirectionEnum.Right));
            return (router, routedItem, _world.GetOrCreateSystem<SplitterDecisionSystem>());
        }

        _entityManager.AddComponentData(router, new MergerRoutingState(targetBelt, DirectionEnum.Right));
        return (router, routedItem, _world.GetOrCreateSystem<MergerDecisionSystem>());
    }

    private void CreateTargetOccupants(TargetOccupancy occupancy, int2 position, float firstProgress, int count)
    {
        Entity storedOwner = Entity.Null;
        if (occupancy == TargetOccupancy.Stored)
        {
            storedOwner = Entities.CreateStorage(new int2(10, 10), new int2(1, 1));
        }

        for (int i = 0; i < count; i++)
        {
            if (occupancy == TargetOccupancy.Stored)
            {
                Entity storedItem = Entities.CreateStoredItem(storedOwner, ItemTypeEnum.Iron_Ore);
                _entityManager.SetComponentData(storedItem, new GridPosition(position));
                _entityManager.SetComponentData(storedItem, new BeltMovementState(firstProgress));
                _entityManager.SetComponentEnabled<BeltMovementState>(storedItem, true);
                continue;
            }

            Entity item = Entities.CreateBeltItem(position, DirectionEnum.Right,
                firstProgress + i * GameConstants.ItemSpacing);
            if (occupancy == TargetOccupancy.DisabledWorld)
            {
                _entityManager.SetComponentEnabled<BeltMovementState>(item, false);
            }
            else if (occupancy == TargetOccupancy.WorldWithoutMovement)
            {
                _entityManager.RemoveComponent<BeltMovementState>(item);
            }
        }
    }

    private bool IsEntryDecisionEnabled(Entity source)
    {
        if (_entityManager.HasComponent<RoutingTransferDecision>(source))
        {
            return _entityManager.IsComponentEnabled<RoutingTransferDecision>(source);
        }

        return _entityManager.IsComponentEnabled<BuildingItemOutputDecision>(source);
    }

    private void ApplyEntriesAndSync()
    {
        // 승인 뒤 저장/라우팅/Owner를 반영하고 ECB→ItemSync 이후 실제 목표 셀을 검사한다.
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<BuildingItemStorageApplySystem>());
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<RoutingApplySystem>());
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<ItemOwnershipApplySystem>());
        Simulation.Playback(_world.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>());
        Simulation.UpdateAndComplete(_itemSpatialSyncHandle);
    }

    private bool IsItemAtTarget(Entity expectedItem, int2 targetPosition)
    {
        using var query = _entityManager.CreateEntityQuery(typeof(ItemSpatialIndex));
        var index = query.GetSingleton<ItemSpatialIndex>();
        if (!index.TryGetFirstItem(targetPosition, out Entity item, out var iterator))
        {
            return false;
        }

        do
        {
            if (item == expectedItem)
            {
                return true;
            }
        }
        while (index.TryGetNextItem(out item, ref iterator));

        return false;
    }
}
