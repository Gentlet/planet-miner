using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Task 2.3: Belt Execution System 통합 및 단위 테스트.
/// BeltMovementExecutionSystem에 의한 아이템 진행도(Progress), 타일 횡단(GridPosition),
/// 실제 렌더링 위치(LocalTransform), 의사결정 소비(Consume)를 검증합니다.
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
    {
        var entity = _entityManager.CreateEntity(typeof(GridPosition), typeof(Direction), typeof(BeltComponent));
        _entityManager.SetComponentData(entity, new GridPosition(position));
        _entityManager.SetComponentData(entity, new Direction(direction));
        _entityManager.SetComponentData(entity, new BeltComponent(speed));
        return entity;
    }

    private Entity CreateBeltItem(int2 position, float progress, float plannedProgress = 0.0f, ItemTypeEnum itemType = ItemTypeEnum.Iron_Ore)
    {
        var entity = _entityManager.CreateEntity(
            typeof(ItemIdentity),
            typeof(GridPosition),
            typeof(LocalTransform),
            typeof(ItemOwnership),
            typeof(BeltMovementState),
            typeof(BeltMovementDecision));

        _entityManager.SetComponentData(entity, new ItemIdentity(itemType));
        _entityManager.SetComponentData(entity, new GridPosition(position));
        _entityManager.SetComponentData(entity, LocalTransform.FromPosition(new float3(position.x, position.y, 0f)));
        _entityManager.SetComponentData(entity, ItemOwnership.WorldItem);
        _entityManager.SetComponentData(entity, new BeltMovementState(progress));
        _entityManager.SetComponentData(entity, new BeltMovementDecision(plannedProgress));
        _entityManager.SetComponentEnabled<BeltMovementState>(entity, true);
        _entityManager.SetComponentEnabled<BeltMovementDecision>(entity, true);
        return entity;
    }

    private void SyncSpatialIndices()
    {
        _beltSpatialSyncHandle.Update(_world.Unmanaged);
        _itemSpatialSyncHandle.Update(_world.Unmanaged);
    }

    private void RunDecisionPhase(float deltaTime)
    {
        _world.SetTime(new Unity.Core.TimeData(0.1, deltaTime));
        _beltDecisionHandle.Update(_world.Unmanaged);
    }

    private void RunExecutionPhase()
    {
        _beltExecutionHandle.Update(_world.Unmanaged);
    }

    [Test]
    public void Test01_InsideTileMovement_AdvancesProgressAndUpdatesTransform()
    {
        // Arrange: (0,0) 방향 Right 벨트, Progress = 0.2f, 이번 프레임 이동 계획 = 0.1f
        CreateBelt(new int2(0, 0), DirectionEnum.Right, speed: 2.0f);
        var item = CreateBeltItem(new int2(0, 0), progress: 0.2f, plannedProgress: 0.1f);

        SyncSpatialIndices();

        // Act: ExecutionGroup 실행
        RunExecutionPhase();

        // Assert
        var state = _entityManager.GetComponentData<BeltMovementState>(item);
        var pos = _entityManager.GetComponentData<GridPosition>(item);
        var transform = _entityManager.GetComponentData<LocalTransform>(item);
        var decision = _entityManager.GetComponentData<BeltMovementDecision>(item);

        // 1. Progress가 0.2 + 0.1 = 0.3f로 전진
        Assert.AreEqual(0.3f, state.Progress, 0.0001f);
        // 2. 타일 내 이동이므로 GridPosition 유지
        Assert.AreEqual(new int2(0, 0), pos.Value);
        // 3. LocalTransform.Position = center(0,0) + dir(1,0) * (0.3 - 0.5) = (-0.2f, 0f, 0f)
        Assert.AreEqual(-0.2f, transform.Position.x, 0.0001f);
        Assert.AreEqual(0.0f, transform.Position.y, 0.0001f);
        // 4. Consume-on-Execution: PlannedProgress 소비 확인
        Assert.AreEqual(0.0f, decision.PlannedProgress, 0.0001f, "PlannedProgress must be consumed to 0.0f.");
    }

    [Test]
    public void Test02_BoundaryCrossing_AdvancesGridPositionAndWrapsProgress()
    {
        // Arrange: 연속된 벨트 (0,0) -> (1,0), Progress = 0.95f, 이동 계획 = 0.1f (합산 1.05f >= 1.0f)
        CreateBelt(new int2(0, 0), DirectionEnum.Right, speed: 2.0f);
        CreateBelt(new int2(1, 0), DirectionEnum.Right, speed: 2.0f);
        var item = CreateBeltItem(new int2(0, 0), progress: 0.95f, plannedProgress: 0.1f);

        SyncSpatialIndices();

        // Act
        RunExecutionPhase();

        // Assert
        var state = _entityManager.GetComponentData<BeltMovementState>(item);
        var pos = _entityManager.GetComponentData<GridPosition>(item);
        var transform = _entityManager.GetComponentData<LocalTransform>(item);
        var decision = _entityManager.GetComponentData<BeltMovementDecision>(item);

        // 1. 타일 경계를 횡단하여 GridPosition이 (1, 0)으로 전진
        Assert.AreEqual(new int2(1, 0), pos.Value, "GridPosition must advance to next tile.");
        // 2. Progress가 1.05 - 1.0 = 0.05f로 보정
        Assert.AreEqual(0.05f, state.Progress, 0.0001f);
        // 3. 새 타일 기준 Transform: center(1,0) + dir(1,0) * (0.05 - 0.5) = (1.0 - 0.45, 0) = (0.55f, 0f, 0f)
        Assert.AreEqual(0.55f, transform.Position.x, 0.0001f);
        Assert.AreEqual(0.0f, transform.Position.y, 0.0001f);
        // 4. 계획 소비 확인
        Assert.AreEqual(0.0f, decision.PlannedProgress, 0.0001f);
    }

    [Test]
    public void Test03_EndOfBelt_MaintainsTilePositionAtBoundary()
    {
        // Arrange: (0,0)에만 벨트가 있고 (1,0)에는 벨트가 없는 종단 상황
        // Progress = 0.95f, Decision 단계에 의해 PlannedProgress = 0.05f로 계획됨 (종단 캡)
        CreateBelt(new int2(0, 0), DirectionEnum.Right, speed: 2.0f);
        var item = CreateBeltItem(new int2(0, 0), progress: 0.95f, plannedProgress: 0.05f);

        SyncSpatialIndices();

        // Act
        RunExecutionPhase();

        // Assert
        var state = _entityManager.GetComponentData<BeltMovementState>(item);
        var pos = _entityManager.GetComponentData<GridPosition>(item);
        var transform = _entityManager.GetComponentData<LocalTransform>(item);

        // 타일 경계 끝(1.0f)에 도달하였으나 다음 타일 벨트가 없으므로 (0,0) 타일에 머무름
        Assert.AreEqual(new int2(0, 0), pos.Value);
        Assert.AreEqual(1.0f, state.Progress, 0.0001f);
        // Transform은 타일 출구 끝: center(0,0) + dir(1,0) * (1.0 - 0.5) = (0.5f, 0f, 0f)
        Assert.AreEqual(0.5f, transform.Position.x, 0.0001f);
    }

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
    public void Test05_PipelineIntegration_DecisionThroughExecution_AdvancesCorrectly()
    {
        // Arrange: (0,0) -> (1,0) 벨트, 아이템 입구(0.0f)
        CreateBelt(new int2(0, 0), DirectionEnum.Right, speed: 2.0f);
        CreateBelt(new int2(1, 0), DirectionEnum.Right, speed: 2.0f);

        var item = CreateBeltItem(new int2(0, 0), progress: 0.0f, plannedProgress: 0.0f);

        SyncSpatialIndices();

        // Phase 2: Decision (dt = 0.05s -> speed 2.0 * 0.05 = 0.1f PlannedProgress 결정)
        RunDecisionPhase(0.05f);

        var decisionMid = _entityManager.GetComponentData<BeltMovementDecision>(item);
        Assert.AreEqual(0.1f, decisionMid.PlannedProgress, 0.0001f, "Decision must calculate 0.1f.");

        // Phase 4: Execution (이동 적용 및 소비)
        RunExecutionPhase();

        var stateEnd = _entityManager.GetComponentData<BeltMovementState>(item);
        var decisionEnd = _entityManager.GetComponentData<BeltMovementDecision>(item);
        var transformEnd = _entityManager.GetComponentData<LocalTransform>(item);

        Assert.AreEqual(0.1f, stateEnd.Progress, 0.0001f, "Progress must advance by 0.1f.");
        Assert.AreEqual(0.0f, decisionEnd.PlannedProgress, 0.0001f, "PlannedProgress must be consumed.");
        // 입구(-0.5)에서 0.1f 전진 = -0.4f
        Assert.AreEqual(-0.4f, transformEnd.Position.x, 0.0001f);
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
