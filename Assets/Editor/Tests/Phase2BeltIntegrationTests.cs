using System.IO;
using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Task 2.4: Belt Domain 전체 파이프라인 통합 및 Invariant 검증 테스트.
/// SpatialSync -> Decision -> Execution -> SpatialSync -> InvariantValidation 전체 흐름의
/// 위치 정합성, 공간 인덱스 동기화, 간격 보존, 무결성 위반 감지 기능을 검증합니다.
/// </summary>
public class Phase2BeltIntegrationTests : EcsWorldTestFixture
{
    private SystemHandle _beltSpatialSyncHandle;
    private SystemHandle _itemSpatialSyncHandle;
    private SystemHandle _beltDecisionHandle;
    private SystemHandle _beltExecutionHandle;
    private WorldInvariantValidationSystem _invariantValidationSystem;

    private string _logDirectory;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();

        _beltSpatialSyncHandle = _world.GetOrCreateSystem(typeof(BeltSpatialSyncSystem));
        _itemSpatialSyncHandle = _world.GetOrCreateSystem(typeof(ItemSpatialSyncSystem));
        _beltDecisionHandle = _world.GetOrCreateSystem(typeof(BeltMovementDecisionSystem));
        _beltExecutionHandle = _world.GetOrCreateSystem(typeof(BeltMovementExecutionSystem));
        _invariantValidationSystem = _world.GetOrCreateSystemManaged<WorldInvariantValidationSystem>();

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
            // 테스트 정리 중 파일 잠금 예외 무시
        }
    }

    private Entity CreateBelt(int2 position, DirectionEnum direction, float speed = 2.0f)
        => Entities.CreateBelt(position, direction, speed);

    private Entity CreateBeltItem(int2 position, float progress, float plannedProgress = 0.0f, ItemTypeEnum itemType = ItemTypeEnum.Iron_Ore)
        => Entities.CreateBeltItem(position, DirectionEnum.Right, progress, plannedProgress, itemType);

    /// <summary>
    /// Phase 2 ~ Phase 6 전체 시뮬레이션 프레임을 실행합니다:
    /// Synchronization(사전 동기화) -> Decision -> Execution -> Synchronization(사후 동기화 & Invariant 검증)
    /// </summary>
    private void StepFullBeltSimulation(float deltaTime)
    {
        _world.SetTime(new Unity.Core.TimeData(0.1, deltaTime));

        // 1. 사전 공간 동기화 (Decision 이전 단계 최신 상태 반영)
        _beltSpatialSyncHandle.Update(_world.Unmanaged);
        _itemSpatialSyncHandle.Update(_world.Unmanaged);

        // 2. DecisionGroup (Phase 2)
        _beltDecisionHandle.Update(_world.Unmanaged);

        // 3. ExecutionGroup (Phase 4)
        _beltExecutionHandle.Update(_world.Unmanaged);

        // 4. SynchronizationGroup (Phase 6: 이동된 위치 재동기화 및 Invariant 검증)
        _beltSpatialSyncHandle.Update(_world.Unmanaged);
        _itemSpatialSyncHandle.Update(_world.Unmanaged);
        _invariantValidationSystem.Update();
    }

    [Test]
    public void Test01_MultiTileFlow_AdvancesItemsAndSyncsItemSpatialIndexAccurately()
    {
        // Arrange: (0,0) -> (1,0) -> (2,0) 3개 타일 직선 벨트
        CreateBelt(new int2(0, 0), DirectionEnum.Right, speed: 2.0f);
        CreateBelt(new int2(1, 0), DirectionEnum.Right, speed: 2.0f);
        CreateBelt(new int2(2, 0), DirectionEnum.Right, speed: 2.0f);

        var item = CreateBeltItem(new int2(0, 0), progress: 0.0f);

        // Act: dt = 0.05s (speed 2.0 * 0.05 = 0.1f/frame)로 10프레임 시뮬레이션
        // 총 이동거리 = 1.0f -> 타일 (1,0)의 입구(0.0f)에 도달해야 함
        for (int i = 0; i < 10; i++)
        {
            StepFullBeltSimulation(0.05f);
        }

        // Assert
        var state = _entityManager.GetComponentData<BeltMovementState>(item);
        var pos = _entityManager.GetComponentData<GridPosition>(item);
        var spatialIndex = _world.EntityManager.CreateEntityQuery(typeof(ItemSpatialIndex)).GetSingleton<ItemSpatialIndex>();

        // 1. 새 위치 (1,0) 및 Progress 0.0f 검증
        Assert.AreEqual(new int2(1, 0), pos.Value);
        Assert.AreEqual(0.0f, state.Progress, 0.001f);

        // 2. ItemSpatialIndex 동기화 무결성 검증: (0,0)에는 없고 (1,0)에 존재
        Assert.IsFalse(spatialIndex.HasItemAt(new int2(0, 0)), "Old tile (0,0) must not contain item.");
        Assert.IsTrue(spatialIndex.HasItemAt(new int2(1, 0)), "New tile (1,0) must contain item.");
        Assert.AreEqual(1, spatialIndex.CountItemsAt(new int2(1, 0)));

        // 3. Invariant 위반 없음 검증 (로그 파일 미생성)
        int logCount = Directory.Exists(_logDirectory) ? Directory.GetFiles(_logDirectory, "invariant_error_*.txt").Length : 0;
        Assert.AreEqual(0, logCount, "No invariant violation should occur during normal multi-tile flow.");
    }

    [Test]
    public void Test02_BackpressureWave_PropagatesToFollowingItemsAndPreservesSpacing()
    {
        // Arrange: (0,0) -> (1,0) 2개 타일 벨트, (2,0)은 벨트 없음(종단)
        CreateBelt(new int2(0, 0), DirectionEnum.Right, speed: 2.0f);
        CreateBelt(new int2(1, 0), DirectionEnum.Right, speed: 2.0f);

        // 선두 아이템 A (종단 직전 0.9f), 후행 아이템 B (0.5f)
        var itemA = CreateBeltItem(new int2(1, 0), progress: 0.9f);
        var itemB = CreateBeltItem(new int2(1, 0), progress: 0.5f);

        // Act: 5프레임 실행 (dt = 0.05s)
        // A는 1프레임 만에 1.0f(종단)에 도달하여 정체
        // B는 A가 1.0f에 멈춘 후 0.75f까지만 전진하여 정체 (간격 0.25f 유지)
        for (int i = 0; i < 5; i++)
        {
            StepFullBeltSimulation(0.05f);
        }

        // Assert
        var stateA = _entityManager.GetComponentData<BeltMovementState>(itemA);
        var stateB = _entityManager.GetComponentData<BeltMovementState>(itemB);
        var decA = _entityManager.GetComponentData<BeltMovementDecision>(itemA);
        var decB = _entityManager.GetComponentData<BeltMovementDecision>(itemB);

        // A는 종단(1.0f)에 도달하여 막힘
        Assert.AreEqual(1.0f, stateA.Progress, 0.001f);
        Assert.IsTrue(decA.IsBlocked);

        // B는 0.75f에 멈춰 A와의 0.25f 간격을 엄격히 보존
        Assert.AreEqual(0.75f, stateB.Progress, 0.001f);
        Assert.IsTrue(decB.IsBlocked);

        float gap = stateA.Progress - stateB.Progress;
        Assert.AreEqual(0.25f, gap, 0.001f, "Gap between items must be exactly ItemSpacing (0.25f).");

        int logCount = Directory.Exists(_logDirectory) ? Directory.GetFiles(_logDirectory, "invariant_error_*.txt").Length : 0;
        Assert.AreEqual(0, logCount, "Backpressure wave must not cause invariant violations.");
    }

    [Test]
    public void Test03_SingleTile_MaximumFourItems_FlowsWithoutViolation()
    {
        // Arrange: 1타일 최대 수용량인 4개 아이템을 0.25f 간격으로 배치
        CreateBelt(new int2(0, 0), DirectionEnum.Right, speed: 2.0f);
        CreateBelt(new int2(1, 0), DirectionEnum.Right, speed: 2.0f);

        CreateBeltItem(new int2(0, 0), progress: 0.0f);
        CreateBeltItem(new int2(0, 0), progress: 0.25f);
        CreateBeltItem(new int2(0, 0), progress: 0.5f);
        CreateBeltItem(new int2(0, 0), progress: 0.75f);

        // Act: 1프레임 전체 파이프라인 시뮬레이션
        StepFullBeltSimulation(0.05f);

        // Assert: 4개 아이템이 정상 전진하고 Invariant 위반이 발생하지 않아야 함
        int logCount = Directory.Exists(_logDirectory) ? Directory.GetFiles(_logDirectory, "invariant_error_*.txt").Length : 0;
        Assert.AreEqual(0, logCount, "4 items on a single tile with 0.25f spacing must be completely valid.");
    }

    [Test]
    public void Test04_SpacingViolation_DetectedByInvariantValidationSystem()
    {
        // Arrange: (0,0) 타일에 불법적으로 너무 좁은 간격(0.1f)으로 두 아이템을 강제 배치
        CreateBelt(new int2(0, 0), DirectionEnum.Right, speed: 2.0f);

        CreateBeltItem(new int2(0, 0), progress: 0.2f);
        CreateBeltItem(new int2(0, 0), progress: 0.3f); // 간격 0.1f < 0.25f (명백한 침범)

        // Act: 공간 동기화 및 Invariant 검증 강제 실행
        _beltSpatialSyncHandle.Update(_world.Unmanaged);
        _itemSpatialSyncHandle.Update(_world.Unmanaged);
        _invariantValidationSystem.Update();

        // Assert: Invariant 위반 리포트 파일이 정상적으로 생성되었는지 확인
        Assert.IsTrue(Directory.Exists(_logDirectory), "Log directory must exist.");
        var files = Directory.GetFiles(_logDirectory, "invariant_error_*.txt");
        Assert.GreaterOrEqual(files.Length, 1, "Invariant error report file must be generated for spacing violation.");

        string reportContent = File.ReadAllText(files[0]);
        StringAssert.Contains("BeltInvariant", reportContent);
        StringAssert.Contains("Belt item spacing violated", reportContent);
    }
}
