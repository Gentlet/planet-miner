using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Task 2.2: Belt Decision System 통합 및 단위 테스트.
/// 벨트 위 아이템의 PlannedMovement 및 IsBlocked 의사결정 규칙을 검증합니다.
/// </summary>
public class Phase2BeltDecisionTests : EcsWorldTestFixture
{
    private SystemHandle _beltSpatialSyncHandle;
    private SystemHandle _itemSpatialSyncHandle;
    private SystemHandle _beltDecisionHandle;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();

        _beltSpatialSyncHandle = _world.GetOrCreateSystem(typeof(BeltSpatialSyncSystem));
        _itemSpatialSyncHandle = _world.GetOrCreateSystem(typeof(ItemSpatialSyncSystem));
        _beltDecisionHandle = _world.GetOrCreateSystem(typeof(BeltMovementDecisionSystem));
    }

    private Entity CreateBelt(int2 position, DirectionEnum direction, float speed = 2.0f)
    {
        var entity = _entityManager.CreateEntity(typeof(GridPosition), typeof(Direction), typeof(BeltComponent));
        _entityManager.SetComponentData(entity, new GridPosition(position));
        _entityManager.SetComponentData(entity, new Direction(direction));
        _entityManager.SetComponentData(entity, new BeltComponent(speed));
        return entity;
    }

    private Entity CreateBeltItem(int2 position, float progress, ItemTypeEnum itemType = ItemTypeEnum.Iron_Ore)
    {
        var entity = _entityManager.CreateEntity(
            typeof(ItemIdentity),
            typeof(GridPosition),
            typeof(ItemOwnership),
            typeof(BeltMovementState),
            typeof(BeltMovementDecision));

        _entityManager.SetComponentData(entity, new ItemIdentity(itemType));
        _entityManager.SetComponentData(entity, new GridPosition(position));
        _entityManager.SetComponentData(entity, ItemOwnership.WorldItem);
        _entityManager.SetComponentData(entity, new BeltMovementState(progress));
        _entityManager.SetComponentData(entity, new BeltMovementDecision());
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

    [Test]
    public void Test01_FreeMovement_CalculatesFullSpeedDistance()
    {
        // Arrange: 연속된 벨트 2개와 타일 입구(0.0f)에 위치한 아이템 1개
        CreateBelt(new int2(0, 0), DirectionEnum.Right, speed: 2.0f);
        CreateBelt(new int2(1, 0), DirectionEnum.Right, speed: 2.0f);

        var item = CreateBeltItem(new int2(0, 0), progress: 0.0f);

        SyncSpatialIndices();

        // Act: dt = 0.05s (속도 2.0 * 0.05 = 0.1f 거리 이동 예상)
        RunDecisionPhase(0.05f);

        // Assert
        var decision = _entityManager.GetComponentData<BeltMovementDecision>(item);
        Assert.AreEqual(0.1f, decision.PlannedProgress, 0.0001f, "Free item should plan to move full distance.");
        Assert.IsFalse(decision.IsBlocked, "Item should not be blocked.");
    }

    [Test]
    public void Test02_EndOfBelt_BlocksAtTileEnd()
    {
        // Arrange: (0,0)에만 벨트가 있고 (1,0)에는 벨트가 없는 종단 상황
        CreateBelt(new int2(0, 0), DirectionEnum.Right, speed: 2.0f);

        // 타일 출구 직전 (Progress = 0.95f)에 위치
        var item = CreateBeltItem(new int2(0, 0), progress: 0.95f);

        SyncSpatialIndices();

        // Act: dt = 0.05s (목표 이동거리 0.1f > 남은 타일거리 0.05f)
        RunDecisionPhase(0.05f);

        // Assert: 타일 끝(1.0f)까지만 이동 계획이 잡히고 막힘 플래그가 활성화되어야 함
        var decision = _entityManager.GetComponentData<BeltMovementDecision>(item);
        Assert.AreEqual(0.05f, decision.PlannedProgress, 0.0001f, "Item at belt end should only move up to tile boundary (1.0f).");
        Assert.IsTrue(decision.IsBlocked, "Item at belt end must be marked as blocked.");
    }

    [Test]
    public void Test03_SameTileAheadItem_EnforcesItemSpacing()
    {
        // Arrange: 같은 타일 (0,0)에 두 아이템 A, B 배치
        CreateBelt(new int2(0, 0), DirectionEnum.Right, speed: 2.0f);

        var itemA = CreateBeltItem(new int2(0, 0), progress: 0.2f);
        var itemB = CreateBeltItem(new int2(0, 0), progress: 0.35f); // 간격 0.15f < ItemSpacing(0.25f)

        SyncSpatialIndices();

        // Act
        RunDecisionPhase(0.05f);

        // Assert:
        // A는 B와의 최소 간격(0.25f) 부족으로 인해 전진할 수 없음 (0.0f, Blocked)
        var decisionA = _entityManager.GetComponentData<BeltMovementDecision>(itemA);
        Assert.AreEqual(0.0f, decisionA.PlannedProgress, 0.0001f, "Item A should be blocked by leading item B on the same tile.");
        Assert.IsTrue(decisionA.IsBlocked, "Item A must be blocked.");

        // B는 전방이 비어있으므로 0.1f 온전히 이동 가능
        var decisionB = _entityManager.GetComponentData<BeltMovementDecision>(itemB);
        Assert.AreEqual(0.1f, decisionB.PlannedProgress, 0.0001f, "Item B is clear ahead and should move normally.");
        Assert.IsFalse(decisionB.IsBlocked, "Item B should not be blocked.");
    }

    [Test]
    public void Test04_NextTileItem_EnforcesItemSpacingAcrossBoundary()
    {
        // Arrange: (0,0) 타일 끝의 A와 (1,0) 타일 입구의 B
        CreateBelt(new int2(0, 0), DirectionEnum.Right, speed: 2.0f);
        CreateBelt(new int2(1, 0), DirectionEnum.Right, speed: 2.0f);

        var itemA = CreateBeltItem(new int2(0, 0), progress: 0.9f);
        var itemB = CreateBeltItem(new int2(1, 0), progress: 0.1f); // 둘 사이 실제 거리 = (1.0 - 0.9) + 0.1 = 0.2f < 0.25f

        SyncSpatialIndices();

        // Act
        RunDecisionPhase(0.05f);

        // Assert: A는 다음 타일 입구에 있는 B로 인해 타일 경계를 넘어설 수 없음
        var decisionA = _entityManager.GetComponentData<BeltMovementDecision>(itemA);
        Assert.AreEqual(0.0f, decisionA.PlannedProgress, 0.0001f, "Item A should be blocked by item B on the next tile.");
        Assert.IsTrue(decisionA.IsBlocked, "Item A must be blocked.");
    }

    [Test]
    public void Test05_DecisionSystem_NeverMutatesProgressOrPosition()
    {
        // Arrange
        CreateBelt(new int2(0, 0), DirectionEnum.Right, speed: 2.0f);
        CreateBelt(new int2(1, 0), DirectionEnum.Right, speed: 2.0f);

        var item = CreateBeltItem(new int2(0, 0), progress: 0.3f);

        SyncSpatialIndices();

        // Act
        RunDecisionPhase(0.05f);

        // Assert: DecisionGroup은 오직 PlannedMovement와 IsBlocked만 계산하며,
        // Progress와 GridPosition은 절대 변경하지 않는 불변 원칙을 지켜야 함
        var state = _entityManager.GetComponentData<BeltMovementState>(item);
        var decision = _entityManager.GetComponentData<BeltMovementDecision>(item);
        var pos = _entityManager.GetComponentData<GridPosition>(item);

        Assert.AreEqual(0.3f, state.Progress, 0.0001f, "Progress must NOT be modified in Decision phase.");
        Assert.AreEqual(new int2(0, 0), pos.Value, "GridPosition must NOT be modified in Decision phase.");
        Assert.AreEqual(0.1f, decision.PlannedProgress, 0.0001f, "PlannedMovement must be recorded in BeltMovementDecision.");
    }
}
