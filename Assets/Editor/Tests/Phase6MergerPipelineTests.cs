using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Merger 합류 파이프라인 및 라우팅 순환 단위 테스트.
/// - Back -> Left -> Right 3방향 라운드로빈 순환 및 커서 전진 검증
/// - 비어있는 포트 우회 합류(Work-conserving) 및 실제 유입 포트 기준 커서 갱신 검증
/// - 출력 정체 시 아이템 보존 및 커서 동결 검증
/// - 정체 해제 시 합류 재개 검증
/// - 다중 출력 벨트 중 PlacementStamp 우선순위 기준선 자동 선택 검증
/// </summary>
public class Phase6MergerPipelineTests : EcsWorldTestFixture
{
    private SystemHandle _beltSpatialSyncHandle;
    private SystemHandle _itemSpatialSyncHandle;
    private SystemHandle _mergerDecisionHandle;
    private SystemHandle _reservationHandle;
    private SystemHandle _routingApplyHandle;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();

        _beltSpatialSyncHandle = _world.GetOrCreateSystem(typeof(BeltSpatialSyncSystem));
        _itemSpatialSyncHandle = _world.GetOrCreateSystem(typeof(ItemSpatialSyncSystem));
        _mergerDecisionHandle = _world.GetOrCreateSystem(typeof(MergerDecisionSystem));
        _reservationHandle = _world.GetOrCreateSystem(typeof(BeltDestinationReservationSystem));
        _routingApplyHandle = _world.GetOrCreateSystem(typeof(RoutingApplySystem));
    }

    private Entity CreateMerger(
        int2 position,
        DirectionEnum forward = DirectionEnum.Right,
        Entity outputBelt = default,
        byte inputCursor = 0,
        ulong tick = 0,
        uint order = 0)
    {
        var entity = Entities.CreateBuilding(BuildingTypeEnum.Merger, position, new int2(1, 1), forward);
        _entityManager.AddComponentData(entity, new MergerRoutingState(outputBelt, forward, inputCursor));
        _entityManager.AddComponentData(entity, new RoutingTransferDecision(Entity.Null, Entity.Null, Entity.Null));
        _entityManager.SetComponentEnabled<RoutingTransferDecision>(entity, false);
        _entityManager.AddComponentData(entity, new PlacementStamp(tick, order));
        return entity;
    }

    private void RunPipeline()
    {
        _beltSpatialSyncHandle.Update(_world.Unmanaged);
        _itemSpatialSyncHandle.Update(_world.Unmanaged);
        _mergerDecisionHandle.Update(_world.Unmanaged);
        _reservationHandle.Update(_world.Unmanaged);
        _routingApplyHandle.Update(_world.Unmanaged);
        _beltSpatialSyncHandle.Update(_world.Unmanaged);
        _itemSpatialSyncHandle.Update(_world.Unmanaged);
    }

    [Test]
    public void Test01_Merger_AlternatesInputs_InBackLeftRightOrder()
    {
        // Arrange: (0,0) Merger (Forward = Right), (1,0) 출력 벨트
        var outputBelt = Entities.CreateBelt(new int2(1, 0), DirectionEnum.Right);
        var merger = CreateMerger(new int2(0, 0), DirectionEnum.Right, outputBelt, inputCursor: 0);

        // 입력 벨트 3개 배치: Back(Left)=(-1,0) Right, Left(Up)=(0,1) Down, Right(Down)=(0,-1) Up
        var beltBack = Entities.CreateBelt(new int2(-1, 0), DirectionEnum.Right);
        var beltLeft = Entities.CreateBelt(new int2(0, 1), DirectionEnum.Down);
        var beltRight = Entities.CreateBelt(new int2(0, -1), DirectionEnum.Up);

        var itemBack = Entities.CreateBeltItem(new int2(-1, 0), DirectionEnum.Right, progress: 1.0f);
        var itemLeft = Entities.CreateBeltItem(new int2(0, 1), DirectionEnum.Down, progress: 1.0f);
        var itemRight = Entities.CreateBeltItem(new int2(0, -1), DirectionEnum.Up, progress: 1.0f);

        // 1. 첫 번째 유입: Back(-1, 0) -> Output(1, 0) 및 커서 1(Left) 전진
        RunPipeline();

        Assert.AreEqual(new int2(1, 0), _entityManager.GetComponentData<GridPosition>(itemBack).Value);
        Assert.AreEqual(0.0f, _entityManager.GetComponentData<BeltMovementState>(itemBack).Progress);
        Assert.AreEqual(1, _entityManager.GetComponentData<MergerRoutingState>(merger).InputCursor);

        // 출력 벨트 공간 확보를 위해 첫 번째 아이템 전진
        _entityManager.SetComponentData(itemBack, new BeltMovementState(0.9f));

        // 2. 두 번째 유입: Left(0, 1) -> Output(1, 0) 및 커서 2(Right) 전진
        RunPipeline();

        Assert.AreEqual(new int2(1, 0), _entityManager.GetComponentData<GridPosition>(itemLeft).Value);
        Assert.AreEqual(0.0f, _entityManager.GetComponentData<BeltMovementState>(itemLeft).Progress);
        Assert.AreEqual(2, _entityManager.GetComponentData<MergerRoutingState>(merger).InputCursor);

        // 출력 벨트 공간 확보를 위해 두 번째 아이템 전진
        _entityManager.SetComponentData(itemLeft, new BeltMovementState(0.9f));

        // 3. 세 번째 유입: Right(0, -1) -> Output(1, 0) 및 커서 0(Back) 순환
        RunPipeline();

        Assert.AreEqual(new int2(1, 0), _entityManager.GetComponentData<GridPosition>(itemRight).Value);
        Assert.AreEqual(0.0f, _entityManager.GetComponentData<BeltMovementState>(itemRight).Progress);
        Assert.AreEqual(0, _entityManager.GetComponentData<MergerRoutingState>(merger).InputCursor);
    }

    [Test]
    public void Test02_Merger_WorkConserving_BypassesEmptyOrMissingPort()
    {
        // Arrange: (0,0) Merger (Forward = Right, Cursor = 0)
        var outputBelt = Entities.CreateBelt(new int2(1, 0), DirectionEnum.Right);
        var merger = CreateMerger(new int2(0, 0), DirectionEnum.Right, outputBelt, inputCursor: 0);

        // Back(-1, 0) 벨트는 비어있음
        var beltBack = Entities.CreateBelt(new int2(-1, 0), DirectionEnum.Right);

        // Left(0, 1) 벨트에만 아이템 도착
        var beltLeft = Entities.CreateBelt(new int2(0, 1), DirectionEnum.Down);
        var inputItem = Entities.CreateBeltItem(new int2(0, 1), DirectionEnum.Down, progress: 1.0f);

        // Act
        RunPipeline();

        // Assert: Back을 건너뛰고 Left(0, 1)에서 우회 합류, 커서는 유입된 포트 1(Left) 다음인 2(Right)로 전진
        Assert.AreEqual(new int2(1, 0), _entityManager.GetComponentData<GridPosition>(inputItem).Value);
        Assert.AreEqual(0.0f, _entityManager.GetComponentData<BeltMovementState>(inputItem).Progress);
        Assert.AreEqual(2, _entityManager.GetComponentData<MergerRoutingState>(merger).InputCursor);
    }

    [Test]
    public void Test03_Merger_OutputBlocked_PreservesItemsAndFreezesCursor()
    {
        // Arrange: (0,0) Merger (Forward = Right, Cursor = 0)
        var outputBelt = Entities.CreateBelt(new int2(1, 0), DirectionEnum.Right);
        var merger = CreateMerger(new int2(0, 0), DirectionEnum.Right, outputBelt, inputCursor: 0);

        // 출력 벨트가 이미 차단된 상태 (진입 공간 부족: minProgress < 0.25f)
        var blockingItem = Entities.CreateBeltItem(new int2(1, 0), DirectionEnum.Right, progress: 0.1f);

        // Back, Left, Right 3개 입력 벨트에 아이템 도착
        var beltBack = Entities.CreateBelt(new int2(-1, 0), DirectionEnum.Right);
        var itemBack = Entities.CreateBeltItem(new int2(-1, 0), DirectionEnum.Right, progress: 1.0f);

        var beltLeft = Entities.CreateBelt(new int2(0, 1), DirectionEnum.Down);
        var itemLeft = Entities.CreateBeltItem(new int2(0, 1), DirectionEnum.Down, progress: 1.0f);

        var beltRight = Entities.CreateBelt(new int2(0, -1), DirectionEnum.Up);
        var itemRight = Entities.CreateBeltItem(new int2(0, -1), DirectionEnum.Up, progress: 1.0f);

        // Act
        RunPipeline();

        // Assert: 아이템들은 입력 벨트에 그대로 대기 상태 유지, 커서 동결(0), 결정 비활성화
        Assert.AreEqual(new int2(-1, 0), _entityManager.GetComponentData<GridPosition>(itemBack).Value);
        Assert.AreEqual(1.0f, _entityManager.GetComponentData<BeltMovementState>(itemBack).Progress);

        Assert.AreEqual(new int2(0, 1), _entityManager.GetComponentData<GridPosition>(itemLeft).Value);
        Assert.AreEqual(1.0f, _entityManager.GetComponentData<BeltMovementState>(itemLeft).Progress);

        Assert.AreEqual(new int2(0, -1), _entityManager.GetComponentData<GridPosition>(itemRight).Value);
        Assert.AreEqual(1.0f, _entityManager.GetComponentData<BeltMovementState>(itemRight).Progress);

        Assert.AreEqual(0, _entityManager.GetComponentData<MergerRoutingState>(merger).InputCursor);
        Assert.IsFalse(_entityManager.IsComponentEnabled<RoutingTransferDecision>(merger));
    }

    [Test]
    public void Test04_Merger_BlockedOutputCleared_ResumesMerging()
    {
        // Arrange: (0,0) Merger에서 출력 벨트가 차단된 상태
        var outputBelt = Entities.CreateBelt(new int2(1, 0), DirectionEnum.Right);
        var merger = CreateMerger(new int2(0, 0), DirectionEnum.Right, outputBelt, inputCursor: 0);
        var obstacleItem = Entities.CreateBeltItem(new int2(1, 0), DirectionEnum.Right, progress: 0.1f);

        var beltBack = Entities.CreateBelt(new int2(-1, 0), DirectionEnum.Right);
        var inputItem = Entities.CreateBeltItem(new int2(-1, 0), DirectionEnum.Right, progress: 1.0f);

        // 1. 차단 상태 확인
        RunPipeline();
        Assert.AreEqual(new int2(-1, 0), _entityManager.GetComponentData<GridPosition>(inputItem).Value);

        // 2. 장애물 아이템 전진(공간 확보)
        _entityManager.SetComponentData(obstacleItem, new BeltMovementState(0.9f));

        // Act: 재실행
        RunPipeline();

        // Assert: 차단 해제 후 즉시 정상 유입 및 커서 전진(1)
        Assert.AreEqual(new int2(1, 0), _entityManager.GetComponentData<GridPosition>(inputItem).Value);
        Assert.AreEqual(0.0f, _entityManager.GetComponentData<BeltMovementState>(inputItem).Progress);
        Assert.AreEqual(1, _entityManager.GetComponentData<MergerRoutingState>(merger).InputCursor);
    }

    [Test]
    public void Test05_Merger_PlacementStampPriority_DeterminesOutputBelt()
    {
        // Arrange: OutputBelt 미지정 상태의 Merger
        var merger = CreateMerger(new int2(0, 0), DirectionEnum.Right, outputBelt: Entity.Null, inputCursor: 0);

        // 2개 나가는 벨트 배치
        // 벨트 A: (1, 0) Right (Tick = 20)
        var beltA = Entities.CreateBelt(new int2(1, 0), DirectionEnum.Right);
        _entityManager.AddComponentData(beltA, new PlacementStamp(tick: 20UL, order: 0U));

        // 벨트 B: (0, 1) Up (Tick = 10) -> 더 일찍 설치됨!
        var beltB = Entities.CreateBelt(new int2(0, 1), DirectionEnum.Up);
        _entityManager.AddComponentData(beltB, new PlacementStamp(tick: 10UL, order: 0U));

        // 벨트 B의 방향(Up) 기준 Back 입력 벨트: (0, -1) Up
        var inputBelt = Entities.CreateBelt(new int2(0, -1), DirectionEnum.Up);
        var inputItem = Entities.CreateBeltItem(new int2(0, -1), DirectionEnum.Up, progress: 1.0f);

        // Act
        RunPipeline();

        // Assert: PlacementStamp가 더 앞선 벨트 B가 기준선으로 채택되어 (0, 1)로 합류
        Assert.AreEqual(new int2(0, 1), _entityManager.GetComponentData<GridPosition>(inputItem).Value);
        var mergerState = _entityManager.GetComponentData<MergerRoutingState>(merger);
        Assert.AreEqual(beltB, mergerState.OutputBelt);
        Assert.AreEqual(DirectionEnum.Up, mergerState.ForwardDirection);
    }
}
