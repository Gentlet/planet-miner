using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 라우터 연결 변경(소멸, 대체, 재연결) 및 잔류 아이템 처리 단위 테스트.
/// - Splitter 입력 벨트 소멸 시 남은 벨트로 기준선 전환 및 커서 0 리셋 검증
/// - Splitter 모든 입력 벨트 소멸 시 대기 및 재연결 시 라우팅 재개 검증
/// - Splitter 출력 포트 소멸 시 남은 포트로 우회 분배 검증
/// - Merger 출력 벨트 소멸 시 남은 벨트로 기준선 전환 및 커서 0 리셋 검증
/// - Merger 모든 출력 벨트 소멸 시 입력 아이템 종단 보존 및 재연결 시 합류 재개 검증
/// - 전달 결정 후 적용 전 벨트/아이템 소멸 시 안전 무효화(Fail-safe) 검증
/// </summary>
public class Phase6ConnectionChangeTests : EcsWorldTestFixture
{
    private SystemHandle _beltSpatialSyncHandle;
    private SystemHandle _itemSpatialSyncHandle;
    private SystemHandle _splitterDecisionHandle;
    private SystemHandle _mergerDecisionHandle;
    private SystemHandle _reservationHandle;
    private SystemHandle _routingApplyHandle;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();

        _beltSpatialSyncHandle = _world.GetOrCreateSystem(typeof(BeltSpatialSyncSystem));
        _itemSpatialSyncHandle = _world.GetOrCreateSystem(typeof(ItemSpatialSyncSystem));
        _splitterDecisionHandle = _world.GetOrCreateSystem(typeof(SplitterDecisionSystem));
        _mergerDecisionHandle = _world.GetOrCreateSystem(typeof(MergerDecisionSystem));
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
        _splitterDecisionHandle.Update(_world.Unmanaged);
        _mergerDecisionHandle.Update(_world.Unmanaged);
        _reservationHandle.Update(_world.Unmanaged);
        _routingApplyHandle.Update(_world.Unmanaged);
        _beltSpatialSyncHandle.Update(_world.Unmanaged);
        _itemSpatialSyncHandle.Update(_world.Unmanaged);
    }

    [Test]
    public void Test01_Splitter_InputBeltDestroyed_SwitchesToRemainingBeltAndResetsCursor()
    {
        // Arrange: (0,0) Splitter, 원래 기준 입력 벨트 A(-1, 0, Tick=10), 대체 입력 벨트 B(0, -1, Tick=20)
        var beltA = Entities.CreateBelt(new int2(-1, 0), DirectionEnum.Right);
        _entityManager.AddComponentData(beltA, new PlacementStamp(tick: 10UL, order: 0U));

        var beltB = Entities.CreateBelt(new int2(0, -1), DirectionEnum.Up);
        _entityManager.AddComponentData(beltB, new PlacementStamp(tick: 20UL, order: 0U));

        // Splitter 생성 (초기 기준선: beltA, Forward: Right, 커서: 1(Right))
        var splitter = CreateSplitter(new int2(0, 0), DirectionEnum.Right, beltA, outputCursor: 1);

        // 벨트 B(Up) 기준 Forward 출력 벨트 (0, 1) Up 배치
        var outputBelt = Entities.CreateBelt(new int2(0, 1), DirectionEnum.Up);

        // 벨트 A 파괴(철거)
        _entityManager.DestroyEntity(beltA);

        // 벨트 B에 아이템 도착
        var item = Entities.CreateBeltItem(new int2(0, -1), DirectionEnum.Up, progress: 1.0f);

        // Act
        RunPipeline();

        // Assert: 남은 벨트 B가 새 기준선으로 채택되고, 커서가 0으로 리셋되어 Forward(0, 1)로 정상 배출
        Assert.AreEqual(new int2(0, 1), _entityManager.GetComponentData<GridPosition>(item).Value);
        Assert.AreEqual(0.0f, _entityManager.GetComponentData<BeltMovementState>(item).Progress);

        var splitterState = _entityManager.GetComponentData<SplitterRoutingState>(splitter);
        Assert.AreEqual(beltB, splitterState.InputBelt);
        Assert.AreEqual(DirectionEnum.Up, splitterState.ForwardDirection);
        Assert.AreEqual(1, splitterState.OutputCursor); // 0(Forward) 배출 후 1(Right)로 전진
    }

    [Test]
    public void Test02_Splitter_AllInputBeltsDestroyed_IdlesAndResumesOnReconnection()
    {
        // Arrange: (0,0) Splitter, 입력 벨트 (-1, 0)
        var beltA = Entities.CreateBelt(new int2(-1, 0), DirectionEnum.Right);
        var splitter = CreateSplitter(new int2(0, 0), DirectionEnum.Right, beltA, outputCursor: 0);
        var outputBelt = Entities.CreateBelt(new int2(1, 0), DirectionEnum.Right);

        // 1. 모든 입력 벨트 파괴
        _entityManager.DestroyEntity(beltA);

        // Act 1: 입력 없는 상태에서 파이프라인 실행 -> 에러 없이 유휴(Idle)
        RunPipeline();
        Assert.IsFalse(_entityManager.IsComponentEnabled<RoutingTransferDecision>(splitter));

        // 2. 새로운 입력 벨트 C 설치 및 아이템 도착
        var beltC = Entities.CreateBelt(new int2(-1, 0), DirectionEnum.Right);
        var item = Entities.CreateBeltItem(new int2(-1, 0), DirectionEnum.Right, progress: 1.0f);

        // Act 2: 재실행
        RunPipeline();

        // Assert: 새 입력 벨트와 즉시 연결되어 정상 배출
        Assert.AreEqual(new int2(1, 0), _entityManager.GetComponentData<GridPosition>(item).Value);
        Assert.AreEqual(0.0f, _entityManager.GetComponentData<BeltMovementState>(item).Progress);
        Assert.AreEqual(beltC, _entityManager.GetComponentData<SplitterRoutingState>(splitter).InputBelt);
    }

    [Test]
    public void Test03_Splitter_OutputBeltDestroyed_BypassesToRemainingOutputPort()
    {
        // Arrange: (0,0) Splitter, Forward=(1,0), Right=(0,-1)
        var inputBelt = Entities.CreateBelt(new int2(-1, 0), DirectionEnum.Right);
        var splitter = CreateSplitter(new int2(0, 0), DirectionEnum.Right, inputBelt, outputCursor: 0);

        var beltForward = Entities.CreateBelt(new int2(1, 0), DirectionEnum.Right);
        var beltRight = Entities.CreateBelt(new int2(0, -1), DirectionEnum.Down);

        // Forward 벨트 파괴
        _entityManager.DestroyEntity(beltForward);

        var item = Entities.CreateBeltItem(new int2(-1, 0), DirectionEnum.Right, progress: 1.0f);

        // Act
        RunPipeline();

        // Assert: 파괴된 Forward 포트를 우회하고 남은 Right(0, -1) 포트로 정상 배출, 커서 2(Left)로 전진
        Assert.AreEqual(new int2(0, -1), _entityManager.GetComponentData<GridPosition>(item).Value);
        Assert.AreEqual(0.0f, _entityManager.GetComponentData<BeltMovementState>(item).Progress);
        Assert.AreEqual(2, _entityManager.GetComponentData<SplitterRoutingState>(splitter).OutputCursor);
    }

    [Test]
    public void Test04_Merger_OutputBeltDestroyed_SwitchesToRemainingBeltAndResetsCursor()
    {
        // Arrange: (0,0) Merger, 원래 출력 벨트 A(1, 0, Tick=10), 대체 출력 벨트 B(0, 1, Tick=20)
        var beltA = Entities.CreateBelt(new int2(1, 0), DirectionEnum.Right);
        _entityManager.AddComponentData(beltA, new PlacementStamp(tick: 10UL, order: 0U));

        var beltB = Entities.CreateBelt(new int2(0, 1), DirectionEnum.Up);
        _entityManager.AddComponentData(beltB, new PlacementStamp(tick: 20UL, order: 0U));

        // Merger 생성 (초기 기준선: beltA, Forward: Right, 커서: 1(Left))
        var merger = CreateMerger(new int2(0, 0), DirectionEnum.Right, beltA, inputCursor: 1);

        // 벨트 B(Up) 기준 Back 입력 벨트 (0, -1) Up 배치
        var inputBelt = Entities.CreateBelt(new int2(0, -1), DirectionEnum.Up);

        // 출력 벨트 A 파괴(철거)
        _entityManager.DestroyEntity(beltA);

        // 입력 벨트에 아이템 도착
        var item = Entities.CreateBeltItem(new int2(0, -1), DirectionEnum.Up, progress: 1.0f);

        // Act
        RunPipeline();

        // Assert: 남은 벨트 B가 새 기준선으로 채택되고, 커서가 0으로 리셋되어 Back(0, -1)에서 (0, 1)로 유입
        Assert.AreEqual(new int2(0, 1), _entityManager.GetComponentData<GridPosition>(item).Value);
        Assert.AreEqual(0.0f, _entityManager.GetComponentData<BeltMovementState>(item).Progress);

        var mergerState = _entityManager.GetComponentData<MergerRoutingState>(merger);
        Assert.AreEqual(beltB, mergerState.OutputBelt);
        Assert.AreEqual(DirectionEnum.Up, mergerState.ForwardDirection);
        Assert.AreEqual(1, mergerState.InputCursor); // 0(Back) 유입 후 1(Left)로 전진
    }

    [Test]
    public void Test05_Merger_AllOutputBeltsDestroyed_PreservesItemsAndResumesOnReconnection()
    {
        // Arrange: (0,0) Merger, 출력 벨트 A(1, 0), 입력 벨트(-1, 0)
        var beltA = Entities.CreateBelt(new int2(1, 0), DirectionEnum.Right);
        var inputBelt = Entities.CreateBelt(new int2(-1, 0), DirectionEnum.Right);
        var merger = CreateMerger(new int2(0, 0), DirectionEnum.Right, beltA, inputCursor: 0);

        var item = Entities.CreateBeltItem(new int2(-1, 0), DirectionEnum.Right, progress: 1.0f);

        // 1. 모든 출력 벨트 파괴
        _entityManager.DestroyEntity(beltA);

        // Act 1: 출력 없는 상태에서 파이프라인 실행
        RunPipeline();

        // Assert 1: 아이템은 입력 벨트(-1, 0) 종단에 안전하게 보존, 커서 동결(0), 결정 비활성화
        Assert.AreEqual(new int2(-1, 0), _entityManager.GetComponentData<GridPosition>(item).Value);
        Assert.AreEqual(1.0f, _entityManager.GetComponentData<BeltMovementState>(item).Progress);
        Assert.AreEqual(0, _entityManager.GetComponentData<MergerRoutingState>(merger).InputCursor);
        Assert.IsFalse(_entityManager.IsComponentEnabled<RoutingTransferDecision>(merger));

        // 2. 새 출력 벨트 C 설치
        var beltC = Entities.CreateBelt(new int2(1, 0), DirectionEnum.Right);

        // Act 2: 재실행
        RunPipeline();

        // Assert 2: 새 출력 벨트로 즉시 정상 합류 및 커서 전진
        Assert.AreEqual(new int2(1, 0), _entityManager.GetComponentData<GridPosition>(item).Value);
        Assert.AreEqual(0.0f, _entityManager.GetComponentData<BeltMovementState>(item).Progress);
        Assert.AreEqual(1, _entityManager.GetComponentData<MergerRoutingState>(merger).InputCursor);
        Assert.AreEqual(beltC, _entityManager.GetComponentData<MergerRoutingState>(merger).OutputBelt);
    }

    [Test]
    public void Test06_RoutingApply_TargetOrItemDestroyedBeforeApply_SafelyDropsDecision()
    {
        // Arrange: 수동으로 활성화된 RoutingTransferDecision
        var inputBelt = Entities.CreateBelt(new int2(-1, 0), DirectionEnum.Right);
        var outputBelt = Entities.CreateBelt(new int2(1, 0), DirectionEnum.Right);
        var splitter = CreateSplitter(new int2(0, 0), DirectionEnum.Right, inputBelt, outputCursor: 0);

        var item = Entities.CreateBeltItem(new int2(-1, 0), DirectionEnum.Right, progress: 1.0f);

        // 결정 수동 활성화
        _entityManager.SetComponentData(splitter, new RoutingTransferDecision(item, inputBelt, outputBelt));
        _entityManager.SetComponentEnabled<RoutingTransferDecision>(splitter, true);

        // 적용 직전 대상 출력 벨트 파괴
        _entityManager.DestroyEntity(outputBelt);

        // Act: StateApply 실행
        _routingApplyHandle.Update(_world.Unmanaged);

        // Assert: 예외 없이 결정을 안전하게 비활성화 및 드롭, 아이템은 원래 위치에 유지
        Assert.IsFalse(_entityManager.IsComponentEnabled<RoutingTransferDecision>(splitter));
        Assert.AreEqual(new int2(-1, 0), _entityManager.GetComponentData<GridPosition>(item).Value);
        Assert.AreEqual(1.0f, _entityManager.GetComponentData<BeltMovementState>(item).Progress);
    }
}
