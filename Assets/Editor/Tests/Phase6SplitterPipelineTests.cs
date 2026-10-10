using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: Splitter 출력 순환·우회·정체/복구·기준 벨트에 대한 NUnit EditMode 회귀 검증.
/// 입력·검사: Forward/Right/Left 출구와 입력으로 Decision→Reservation→RoutingApply의 실물/커서를 검사한다.
/// 수명: EcsWorldTestFixture가 각 사례의 독립 World를 준비하고 종료 시 해제한다.
/// </summary>
public class Phase6SplitterPipelineTests : EcsWorldTestFixture
{
    private SystemHandle _beltSpatialSyncHandle;
    private SystemHandle _itemSpatialSyncHandle;
    private SystemHandle _splitterDecisionHandle;
    private SystemHandle _reservationHandle;
    private SystemHandle _routingApplyHandle;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();

        _beltSpatialSyncHandle = _world.GetOrCreateSystem(typeof(BeltSpatialSyncSystem));
        _itemSpatialSyncHandle = _world.GetOrCreateSystem(typeof(ItemSpatialSyncSystem));
        _splitterDecisionHandle = _world.GetOrCreateSystem(typeof(SplitterDecisionSystem));
        _reservationHandle = _world.GetOrCreateSystem(typeof(BeltDestinationReservationSystem));
        _routingApplyHandle = _world.GetOrCreateSystem(typeof(RoutingApplySystem));
    }

    private Entity CreateSplitter(
        int2 position,
        DirectionEnum forward = DirectionEnum.Right,
        Entity inputBelt = default,
        byte outputCursor = 0,
        ulong tick = 0,
        uint order = 0)
    {
        var entity = Entities.CreateBuilding(BuildingTypeEnum.Splitter, position, new int2(1, 1), forward);
        _entityManager.AddComponentData(entity, new SplitterRoutingState(inputBelt, forward, outputCursor));
        _entityManager.AddComponentData(entity, new RoutingTransferDecision(Entity.Null, Entity.Null, Entity.Null));
        _entityManager.SetComponentEnabled<RoutingTransferDecision>(entity, false);
        _entityManager.AddComponentData(entity, new PlacementStamp(tick, order));
        return entity;
    }

    private void RunPipeline()
    {
        // 맵→판단→예약→전달→재등록으로 막힌 출구 우회와 커서 전진을 검사한다.
        _beltSpatialSyncHandle.Update(_world.Unmanaged);
        _itemSpatialSyncHandle.Update(_world.Unmanaged);
        _splitterDecisionHandle.Update(_world.Unmanaged);
        _reservationHandle.Update(_world.Unmanaged);
        _routingApplyHandle.Update(_world.Unmanaged);
        _beltSpatialSyncHandle.Update(_world.Unmanaged);
        _itemSpatialSyncHandle.Update(_world.Unmanaged);
    }

    [Test]
    public void Test01_Splitter_AlternatesOutputs_InForwardRightLeftOrder()
    {
        // Arrange: (0,0) Splitter (Forward = Right), (-1,0) 입력 벨트
        var inputBelt = Entities.CreateBelt(new int2(-1, 0), DirectionEnum.Right);
        var splitter = CreateSplitter(new int2(0, 0), DirectionEnum.Right, inputBelt, outputCursor: 0);

        // 출력 벨트 3개 배치: Forward(Right)=(1,0), Right(Down)=(0,-1), Left(Up)=(0,1)
        var beltForward = Entities.CreateBelt(new int2(1, 0), DirectionEnum.Right);
        var beltRight = Entities.CreateBelt(new int2(0, -1), DirectionEnum.Down);
        var beltLeft = Entities.CreateBelt(new int2(0, 1), DirectionEnum.Up);

        // 1. 첫 번째 아이템 -> Forward(1, 0) 배출 및 커서 1(Right) 전진
        var item1 = Entities.CreateBeltItem(new int2(-1, 0), DirectionEnum.Right, progress: 1.0f);
        RunPipeline();

        Assert.AreEqual(new int2(1, 0), _entityManager.GetComponentData<GridPosition>(item1).Value);
        Assert.AreEqual(0.0f, _entityManager.GetComponentData<BeltMovementState>(item1).Progress);
        Assert.AreEqual(1, _entityManager.GetComponentData<SplitterRoutingState>(splitter).OutputCursor);

        // 2. 두 번째 아이템 -> Right(0, -1) 배출 및 커서 2(Left) 전진
        var item2 = Entities.CreateBeltItem(new int2(-1, 0), DirectionEnum.Right, progress: 1.0f);
        RunPipeline();

        Assert.AreEqual(new int2(0, -1), _entityManager.GetComponentData<GridPosition>(item2).Value);
        Assert.AreEqual(0.0f, _entityManager.GetComponentData<BeltMovementState>(item2).Progress);
        Assert.AreEqual(2, _entityManager.GetComponentData<SplitterRoutingState>(splitter).OutputCursor);

        // 3. 세 번째 아이템 -> Left(0, 1) 배출 및 커서 0(Forward) 순환
        var item3 = Entities.CreateBeltItem(new int2(-1, 0), DirectionEnum.Right, progress: 1.0f);
        RunPipeline();

        Assert.AreEqual(new int2(0, 1), _entityManager.GetComponentData<GridPosition>(item3).Value);
        Assert.AreEqual(0.0f, _entityManager.GetComponentData<BeltMovementState>(item3).Progress);
        Assert.AreEqual(0, _entityManager.GetComponentData<SplitterRoutingState>(splitter).OutputCursor);
    }

    [Test]
    public void Test02_Splitter_WorkConserving_BypassesBlockedOrMissingPort()
    {
        // Arrange: (0,0) Splitter (Forward = Right, Cursor = 0)
        var inputBelt = Entities.CreateBelt(new int2(-1, 0), DirectionEnum.Right);
        var splitter = CreateSplitter(new int2(0, 0), DirectionEnum.Right, inputBelt, outputCursor: 0);

        // Forward(1, 0) 벨트는 이미 아이템이 있어 차단 (진입 공간 부족)
        var beltForward = Entities.CreateBelt(new int2(1, 0), DirectionEnum.Right);
        var blockingItem = Entities.CreateBeltItem(new int2(1, 0), DirectionEnum.Right, progress: 0.1f);

        // Right(0, -1) 벨트는 비어있음
        var beltRight = Entities.CreateBelt(new int2(0, -1), DirectionEnum.Down);

        // 입력 아이템 도착
        var inputItem = Entities.CreateBeltItem(new int2(-1, 0), DirectionEnum.Right, progress: 1.0f);

        // Act
        RunPipeline();

        // Assert: Forward를 건너뛰고 Right(0, -1)로 우회 배출, 커서는 배출된 포트 1(Right) 다음인 2(Left)로 전진
        Assert.AreEqual(new int2(0, -1), _entityManager.GetComponentData<GridPosition>(inputItem).Value);
        Assert.AreEqual(0.0f, _entityManager.GetComponentData<BeltMovementState>(inputItem).Progress);
        Assert.AreEqual(2, _entityManager.GetComponentData<SplitterRoutingState>(splitter).OutputCursor);
    }

    [Test]
    public void Test04_Splitter_BlockedOutputCleared_ResumesRouting()
    {
        // Arrange: Forward 벨트만 연결된 Splitter에서 출구가 차단된 상태
        var inputBelt = Entities.CreateBelt(new int2(-1, 0), DirectionEnum.Right);
        var splitter = CreateSplitter(new int2(0, 0), DirectionEnum.Right, inputBelt, outputCursor: 0);
        var beltForward = Entities.CreateBelt(new int2(1, 0), DirectionEnum.Right);
        var obstacleItem = Entities.CreateBeltItem(new int2(1, 0), DirectionEnum.Right, progress: 0.1f);

        var inputItem = Entities.CreateBeltItem(new int2(-1, 0), DirectionEnum.Right, progress: 1.0f);

        // 1. 차단 상태 확인
        RunPipeline();
        Assert.AreEqual(new int2(-1, 0), _entityManager.GetComponentData<GridPosition>(inputItem).Value);

        // 2. 장애물 아이템 전진(공간 확보)
        Assert.AreEqual(0, _entityManager.GetComponentData<SplitterRoutingState>(splitter).OutputCursor);
        _entityManager.SetComponentData(obstacleItem, new BeltMovementState(0.9f));

        // Act: 재실행
        RunPipeline();

        // Assert: 차단 해제 후 즉시 Forward로 정상 배출 및 커서 전진
        Assert.AreEqual(new int2(1, 0), _entityManager.GetComponentData<GridPosition>(inputItem).Value);
        Assert.AreEqual(0.0f, _entityManager.GetComponentData<BeltMovementState>(inputItem).Progress);
        Assert.AreEqual(1, _entityManager.GetComponentData<SplitterRoutingState>(splitter).OutputCursor);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Test05_Splitter_PlacementStampPriority_DeterminesInputBelt(bool sameStamp)
    {
        // Arrange: InputBelt 미지정 상태의 Splitter
        var splitter = CreateSplitter(new int2(0, 0), DirectionEnum.Right, inputBelt: Entity.Null, outputCursor: 0);

        // 2개 입력 벨트 배치
        // 벨트 A: (-1, 0) Right (Tick = 20)
        var beltA = Entities.CreateBelt(new int2(-1, 0), DirectionEnum.Right);
        _entityManager.AddComponentData(beltA, new PlacementStamp(tick: sameStamp ? 10UL : 20UL, order: 0U));

        // 벨트 B: (0, -1) Up (Tick = 10) -> 더 일찍 설치됨!
        var beltB = Entities.CreateBelt(new int2(0, -1), DirectionEnum.Up);
        _entityManager.AddComponentData(beltB, new PlacementStamp(tick: 10UL, order: 0U));

        // 벨트 B의 진행 방향(Up) 기준 Forward 출구: (0, 1) Up
        int2 outputPosition = sameStamp ? new int2(1, 0) : new int2(0, 1);
        DirectionEnum selectedDirection = sameStamp ? DirectionEnum.Right : DirectionEnum.Up;
        var outputBelt = Entities.CreateBelt(outputPosition, selectedDirection);

        // 벨트 B에 아이템 도착
        var itemB = Entities.CreateBeltItem(sameStamp ? new int2(-1, 0) : new int2(0, -1), selectedDirection, progress: 1.0f);

        // Act
        RunPipeline();

        // Assert: PlacementStamp가 더 앞선 벨트 B가 기준선으로 채택되어 Forward(0, 1)로 배출
        Assert.AreEqual(outputPosition, _entityManager.GetComponentData<GridPosition>(itemB).Value);
        var splitterState = _entityManager.GetComponentData<SplitterRoutingState>(splitter);
        Assert.AreEqual(sameStamp ? beltA : beltB, splitterState.InputBelt);
        Assert.AreEqual(selectedDirection, splitterState.ForwardDirection);
    }
}
