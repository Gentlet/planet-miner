using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Belt Execution System 통합 및 단위 테스트.
/// BeltMovementExecutionSystem에 의한 아이템 진행도(Progress), 타일 횡단(GridPosition),
/// 실제 렌더링 위치(LocalTransform), 의사결정 소비(Consume)를 검증.
/// </summary>
public class Phase2BeltExecutionTests : EcsWorldTestFixture
{
    private SystemHandle _beltSpatialSyncHandle;
    private SystemHandle _itemSpatialSyncHandle;
    private SystemHandle _beltDecisionHandle;
    private SystemHandle _beltExecutionHandle;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();

        _beltSpatialSyncHandle = _world.GetOrCreateSystem(typeof(BeltSpatialSyncSystem));
        _itemSpatialSyncHandle = _world.GetOrCreateSystem(typeof(ItemSpatialSyncSystem));
        _beltDecisionHandle = _world.GetOrCreateSystem(typeof(BeltMovementDecisionSystem));
        _beltExecutionHandle = _world.GetOrCreateSystem(typeof(BeltMovementExecutionSystem));
    }

    private Entity CreateBelt(int2 position, DirectionEnum direction, float speed = 2.0f)
        => Entities.CreateBelt(position, direction, speed);

    private Entity CreateBeltItem(int2 position, float progress, float plannedProgress = 0.0f, ItemTypeEnum itemType = ItemTypeEnum.Iron_Ore)
        => Entities.CreateBeltItem(position, DirectionEnum.Right, progress, plannedProgress, itemType);

    private void SyncSpatialIndices()
    {
        _beltSpatialSyncHandle.Update(_world.Unmanaged);
        _itemSpatialSyncHandle.Update(_world.Unmanaged);
    }

    private void RunDecisionPhase(float deltaTime)
        => Simulation.Update(_beltDecisionHandle, deltaTime);

    private void RunExecutionPhase()
        => Simulation.Update(_beltExecutionHandle);

    [Test]
    public void Test04_CornerBelt_UpdatesTransformDirectionOnCrossing()
    {
        // Arrange: (0,0) [Right] -> (1,0) [Up] 코너 벨트
        CreateBelt(new int2(0, 0), DirectionEnum.Right, speed: 2.0f);
        CreateBelt(new int2(1, 0), DirectionEnum.Up, speed: 2.0f);

        var item = CreateBeltItem(new int2(0, 0), progress: 0.9f, plannedProgress: 0.2f); // 0.9 + 0.2 = 1.1f -> 새 타일 0.1f

        SyncSpatialIndices();

        // Act
        RunExecutionPhase();

        // Assert
        var state = _entityManager.GetComponentData<BeltMovementState>(item);
        var pos = _entityManager.GetComponentData<GridPosition>(item);
        var transform = _entityManager.GetComponentData<LocalTransform>(item);

        Assert.AreEqual(new int2(1, 0), pos.Value);
        Assert.AreEqual(0.1f, state.Progress, 0.0001f);
        // 새 타일 (1,0)의 벨트 방향이 Up(0, 1)이므로:
        // x = 1.0f, y = 0.0f + 1.0 * (0.1 - 0.5) = -0.4f
        Assert.AreEqual(1.0f, transform.Position.x, 0.0001f);
        Assert.AreEqual(-0.4f, transform.Position.y, 0.0001f);
    }

    [Test]
    public void Test06_MultiTileHops_ExecutionWhileLoop_TraversesMultipleTilesCorrectly()
    {
        // Arrange: 3칸 연속 벨트 (0,0) -> (1,0) -> (2,0)
        CreateBelt(new int2(0, 0), DirectionEnum.Right, speed: 2.0f);
        CreateBelt(new int2(1, 0), DirectionEnum.Right, speed: 2.0f);
        CreateBelt(new int2(2, 0), DirectionEnum.Right, speed: 2.0f);

        // Progress = 0.8f, PlannedProgress = 1.4f (총 2.2f -> 2타일 횡단 후 (2,0) 타일에서 0.2f 잔류)
        var item = CreateBeltItem(new int2(0, 0), progress: 0.8f, plannedProgress: 1.4f);

        SyncSpatialIndices();

        // Act
        RunExecutionPhase();

        // Assert
        var state = _entityManager.GetComponentData<BeltMovementState>(item);
        var pos = _entityManager.GetComponentData<GridPosition>(item);
        var transform = _entityManager.GetComponentData<LocalTransform>(item);
        var decision = _entityManager.GetComponentData<BeltMovementDecision>(item);

        // 1. GridPosition이 2칸 전진하여 (2, 0)
        Assert.AreEqual(new int2(2, 0), pos.Value, "GridPosition must advance 2 tiles to (2,0).");
        // 2. Progress가 2.2 - 2.0 = 0.2f로 정확히 이월
        Assert.AreEqual(0.2f, state.Progress, 0.0001f, "Residual progress (0.2f) must be carried over.");
        // 3. LocalTransform: center(2,0) + dir(1,0) * (0.2 - 0.5) = (2.0 - 0.3, 0) = (1.7f, 0f, 0f)
        Assert.AreEqual(1.7f, transform.Position.x, 0.0001f);
        Assert.AreEqual(0.0f, transform.Position.y, 0.0001f);
        // 4. 계획 소비 확인
        Assert.AreEqual(0.0f, decision.PlannedProgress, 0.0001f);
    }

    [Test]
    public void Test07_LargeMovement_EndOfBelt_ClampsToTerminalBoundary()
    {
        // Arrange: 2칸 벨트 (0,0) -> (1,0), 종단은 (1,0)
        CreateBelt(new int2(0, 0), DirectionEnum.Right, speed: 2.0f);
        CreateBelt(new int2(1, 0), DirectionEnum.Right, speed: 2.0f);

        // Progress = 0.8f, PlannedProgress = 2.0f (총 2.8f로 (1,0) 종단을 한참 초과하는 이동량)
        var item = CreateBeltItem(new int2(0, 0), progress: 0.8f, plannedProgress: 2.0f);

        SyncSpatialIndices();

        // Act
        RunExecutionPhase();

        // Assert
        var state = _entityManager.GetComponentData<BeltMovementState>(item);
        var pos = _entityManager.GetComponentData<GridPosition>(item);
        var transform = _entityManager.GetComponentData<LocalTransform>(item);

        // 종단 타일 (1,0)에서 출구 경계(1.0f)에 정확히 정지 (초과 탈출 방지)
        Assert.AreEqual(new int2(1, 0), pos.Value, "Item must stop at the last existing belt tile (1,0).");
        Assert.AreEqual(1.0f, state.Progress, 0.0001f, "Progress must clamp at 1.0f at end of belt.");
        // center(1,0) + dir(1,0) * (1.0 - 0.5) = (1.5f, 0f, 0f)
        Assert.AreEqual(1.5f, transform.Position.x, 0.0001f);
    }

    [Test]
    public void Test08_DecisionSystem_LargeDeltaTime_ClampsDtAndPlannedProgress()
    {
        // Arrange: 벨트 (0,0) -> (1,0), 속도 100.0f의 초고속 벨트
        CreateBelt(new int2(0, 0), DirectionEnum.Right, speed: 100.0f);
        CreateBelt(new int2(1, 0), DirectionEnum.Right, speed: 100.0f);

        var item = CreateBeltItem(new int2(0, 0), progress: 0.0f, plannedProgress: 0.0f);

        SyncSpatialIndices();

        // Act: 1.0초라는 비정상 대형 DeltaTime 입력 (MaxSimulationDeltaTime=0.1f 클램핑되어 0.1 * 100 = 10.0f 시도)
        RunDecisionPhase(deltaTime: 1.0f);

        // Assert: 1회 의사결정 승인 상한은 1.0f로 클램핑되어야 함 (미검사 다다음 타일 터널링 방지)
        var decision = _entityManager.GetComponentData<BeltMovementDecision>(item);
        Assert.LessOrEqual(decision.PlannedProgress, 1.0f, "PlannedProgress must not exceed 1.0f per decision step.");
        Assert.AreEqual(1.0f, decision.PlannedProgress, 0.0001f);
    }
}
