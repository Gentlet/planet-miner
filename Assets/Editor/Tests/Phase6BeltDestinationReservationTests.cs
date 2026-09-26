using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Task 6.2: 벨트 목적지 예약 시스템(BeltDestinationReservationSystem) 단위 및 파이프라인 테스트.
/// - 외부 건물 출고 및 분배기/합류기 라우팅 후보들의 공유 대상 벨트 경합 조율 검증
/// - PlacementStamp(Tick, Order) 기반 1개 후보 승인 및 탈락 후보 비활성화 검증
/// - 대상 벨트 여유 공간(ItemSpacing 0.25f) 및 수용량에 따른 승인/거부 검증
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
    public void Test01_SingleBuildingOutput_ApprovedWhenBeltEmpty()
    {
        // Arrange: 빈 벨트 (0,0)를 향한 창고 출고 요청
        var belt = Entities.CreateBelt(new int2(0, 0), DirectionEnum.Right);

        var storage = Entities.CreateStorage(new int2(0, 1), new int2(1, 1));
        var item = Entities.CreateBeltItem(new int2(0, 1), DirectionEnum.Down, progress: 0.0f);
        _entityManager.SetComponentData(storage, new BuildingItemOutputDecision(canOutput: true, item, new int2(0, 0)));
        _entityManager.SetComponentEnabled<BuildingItemOutputDecision>(storage, true);

        SyncSpatialIndices();

        // Act
        RunReservation();

        // Assert: 대상 벨트가 비어있으므로 정상 승인
        var outputDec = _entityManager.GetComponentData<BuildingItemOutputDecision>(storage);
        Assert.IsTrue(outputDec.CanOutput);
        Assert.IsTrue(_entityManager.IsComponentEnabled<BuildingItemOutputDecision>(storage));
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

    [Test]
    public void Test03_BuildingOutput_Vs_RoutingTransfer_PlacementStampArbitration()
    {
        // Arrange: 대상 벨트 (0,0)로 Splitter와 창고가 동시 진입 시도
        var targetBelt = Entities.CreateBelt(new int2(0, 0), DirectionEnum.Right);

        // 라우팅 엔티티 (Tick=10, Splitter)
        var routerEntity = _entityManager.CreateEntity(
            typeof(BuildingType),
            typeof(GridPosition),
            typeof(RoutingTransferDecision),
            typeof(PlacementStamp));
        _entityManager.SetComponentData(routerEntity, new BuildingType(BuildingTypeEnum.Splitter));
        _entityManager.SetComponentData(routerEntity, new GridPosition(new int2(-1, 0)));
        _entityManager.SetComponentData(routerEntity, new PlacementStamp(tick: 10, order: 0));

        var routeItem = Entities.CreateBeltItem(new int2(-1, 0), DirectionEnum.Right, progress: 1.0f);
        _entityManager.SetComponentData(routerEntity, new RoutingTransferDecision(routeItem, Entity.Null, targetBelt));
        _entityManager.SetComponentEnabled<RoutingTransferDecision>(routerEntity, true);

        // 창고 엔티티 (Tick=20)
        var storage = Entities.CreateStorage(new int2(0, 1), new int2(1, 1));
        _entityManager.AddComponentData(storage, new PlacementStamp(tick: 20, order: 0));
        var storageItem = Entities.CreateBeltItem(new int2(0, 1), DirectionEnum.Down, progress: 0.0f);
        _entityManager.SetComponentData(storage, new BuildingItemOutputDecision(canOutput: true, storageItem, new int2(0, 0)));
        _entityManager.SetComponentEnabled<BuildingItemOutputDecision>(storage, true);

        SyncSpatialIndices();

        // Act
        RunReservation();

        // Assert: 라우팅 엔티티(Tick 10) 승인, 창고(Tick 20) 탈락
        Assert.IsTrue(_entityManager.IsComponentEnabled<RoutingTransferDecision>(routerEntity));

        var storageOut = _entityManager.GetComponentData<BuildingItemOutputDecision>(storage);
        Assert.IsFalse(storageOut.CanOutput);
        Assert.IsFalse(_entityManager.IsComponentEnabled<BuildingItemOutputDecision>(storage));
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
}
