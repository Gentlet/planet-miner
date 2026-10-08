using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 배치 묶음 정책·동일 틱 경합·현장 초기화에 대한 NUnit EditMode 회귀 검증.
/// 입력·검사: 설정/Sync 소유 공간 맵/후보 버퍼로 Command/EndCommand의 요구량·차단/PlacementStamp를 검사한다. 승인과 자재 운송/완공은 구분한다.
/// 수명: EcsWorldTestFixture가 각 사례의 독립 World를 준비하고 종료 시 해제한다.
/// </summary>
public class Phase7PlacementCommandTests : EcsWorldTestFixture
{
    private SystemHandle _commandHandle;
    private SystemHandle _spatialSyncHandle;
    private EndCommandEntityCommandBufferSystem _endCommandEcb;

    private NativeParallelHashMap<int2, BuildingInfo> _buildingMap;
    private NativeParallelHashMap<int2, Entity> _resourceMap;
    private NativeParallelMultiHashMap<int2, Entity> _itemMap;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();

        _commandHandle = _world.GetOrCreateSystem(typeof(BuildingPlacementCommandSystem));
        _spatialSyncHandle = _world.GetOrCreateSystem(typeof(BuildingSpatialSyncSystem));
        _world.GetOrCreateSystem(typeof(ResourceSpatialSyncSystem));
        _world.GetOrCreateSystem(typeof(ItemSpatialSyncSystem));
        _endCommandEcb = _world.GetOrCreateSystemManaged<EndCommandEntityCommandBufferSystem>();

        // 시스템들이 OnCreate에서 생성한 공간 인덱스 맵 참조 가져오기
        _buildingMap = _entityManager.CreateEntityQuery(typeof(BuildingSpatialIndex)).GetSingleton<BuildingSpatialIndex>().Map;
        _resourceMap = _entityManager.CreateEntityQuery(typeof(ResourceSpatialIndex)).GetSingleton<ResourceSpatialIndex>().Map;
        _itemMap = _entityManager.CreateEntityQuery(typeof(ItemSpatialIndex)).GetSingleton<ItemSpatialIndex>().Map;
    }

    [TearDown]
    public override void TearDown()
    {
        base.TearDown();
    }

    private void UpdateCommandPhase()
    {
        using var configQuery = _entityManager.CreateEntityQuery(typeof(BuildingConfig));
        if (configQuery.IsEmptyIgnoreFilter)
        {
            Entities.PrepareBuildingConfiguration(BuildingTypeEnum.Belt, BuildingTypeEnum.Miner, BuildingTypeEnum.Storage);
        }
        // EndCommand 실체화 뒤 생성된 현장/요구 버퍼를 검사해 승인 판단과 가시화 시점을 구분한다.
        _commandHandle.Update(_world.Unmanaged);
        _endCommandEcb.Update();
    }

    private Entity CreatePlacementRequest(PlacementFlags flags, params PlacementRequestCandidateElement[] candidates)
    {
        return BuildingPlacementRequestUtility.Submit(_entityManager, new BuildingPlacementRequest(flags), candidates);
    }

    private void SetupBuildingConfig(params (BuildingTypeEnum type, float speed, int storage, bool unlocked, (ItemTypeEnum item, int qty)[] mats)[] configs)
    {
        var entity = _entityManager.CreateEntity(
            typeof(BuildingConfig),
            typeof(BuildingRuntimeConfig),
            typeof(BuildingConfigElement),
            typeof(BuildingRuntimeConfigElement),
            typeof(BuildingConstructionMaterialElement));

        var configBuffer = _entityManager.GetBuffer<BuildingConfigElement>(entity);
        var runtimeBuffer = _entityManager.GetBuffer<BuildingRuntimeConfigElement>(entity);
        var matBuffer = _entityManager.GetBuffer<BuildingConstructionMaterialElement>(entity);

        foreach (var c in configs)
        {
            configBuffer.Add(new BuildingConfigElement(c.type, c.speed, c.storage, c.unlocked));
            runtimeBuffer.Add(new BuildingRuntimeConfigElement(c.type, c.speed, c.storage));
            if (c.mats != null)
            {
                foreach (var m in c.mats)
                {
                    matBuffer.Add(new BuildingConstructionMaterialElement(c.type, m.item, m.qty));
                }
            }
        }
    }

    [Test]
    public void Test01_SinglePlacementRequest_CreatesConstructionSiteWithMaterialsAndStamp()
    {
        // Arrange
        SetupBuildingConfig((
            BuildingTypeEnum.Miner,
            0.1f,
            0,
            true,
            new[] { (ItemTypeEnum.Iron, 3) }));

        // 자원 노드 등록 (Miner 필수 요건)
        _resourceMap.Add(new int2(0, 0), new Entity { Index = 99 });

        var req = CreatePlacementRequest(
            PlacementFlags.StrictAllOrNothing,
            new PlacementRequestCandidateElement(BuildingTypeEnum.Miner, new int2(2, 2), new int2(0, 0), DirectionEnum.Up));

        // Act
        UpdateCommandPhase();

        // Assert: 1. ConstructionSite 엔티티 생성 검증
        var query = _entityManager.CreateEntityQuery(typeof(ConstructionSite), typeof(BuildingFootprint), typeof(GridPosition), typeof(PlacementStamp));
        Assert.AreEqual(1, query.CalculateEntityCount(), "Exactly 1 ConstructionSite must be spawned.");

        var siteEntity = query.GetSingletonEntity();
        var site = _entityManager.GetComponentData<ConstructionSite>(siteEntity);
        var footprint = _entityManager.GetComponentData<BuildingFootprint>(siteEntity);
        var pos = _entityManager.GetComponentData<GridPosition>(siteEntity);
        var stamp = _entityManager.GetComponentData<PlacementStamp>(siteEntity);

        Assert.AreEqual(BuildingTypeEnum.Miner, site.TargetBuildingType);
        Assert.AreEqual(new int2(2, 2), footprint.Size);
        Assert.AreEqual(new int2(0, 0), pos.Value);
        Assert.GreaterOrEqual(stamp.Tick, 1UL);
        Assert.AreEqual(0U, stamp.Order);

        // 2. 자재 요구량 버퍼 검증 (Iron 3개 요구, 0개 조달)
        var matBuffer = _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(siteEntity);
        Assert.AreEqual(1, matBuffer.Length);
        Assert.AreEqual(ItemTypeEnum.Iron, matBuffer[0].ItemType);
        Assert.AreEqual(3, matBuffer[0].RequiredQuantity);
        Assert.AreEqual(0, matBuffer[0].DeliveredQuantity);
        Assert.IsFalse(matBuffer[0].IsSatisfied);

        // 3. 요청 엔티티 소비(Consume-on-Apply) 검증
        Assert.IsFalse(_entityManager.Exists(req), "Placement request entity must be destroyed after processing.");
    }

    [Test]
    public void Test02_StrictAllOrNothing_Batch_RollsBackAllOnConflict()
    {
        // (1, 0) 타일에 이미 장애물(건물) 존재
        _buildingMap.Add(new int2(1, 0), new BuildingInfo(new Entity { Index = 10 }, BuildingTypeEnum.Storage, DirectionEnum.Up));

        // 3개 벨트 배치 요청
        var req = CreatePlacementRequest(
            PlacementFlags.StrictAllOrNothing,
            new PlacementRequestCandidateElement(BuildingTypeEnum.Belt, new int2(1, 1), new int2(0, 0)),
            new PlacementRequestCandidateElement(BuildingTypeEnum.Belt, new int2(1, 1), new int2(1, 0)), // 충돌!
            new PlacementRequestCandidateElement(BuildingTypeEnum.Belt, new int2(1, 1), new int2(2, 0)));

        // Act
        UpdateCommandPhase();

        // Assert: 1개 충돌로 인해 전체 묶음이 롤백되어 공사 현장 엔티티가 0개여야 함
        var query = _entityManager.CreateEntityQuery(typeof(ConstructionSite));
        Assert.AreEqual(0, query.CalculateEntityCount(), "StrictAllOrNothing must rollback entire batch on single conflict.");

        // 요청 엔티티는 정상 소비되어야 함
        Assert.IsFalse(_entityManager.Exists(req));
    }

    [Test]
    public void Test03_AllowPartialPlacement_Batch_CreatesOnlyValidSites()
    {
        // (1, 0) 타일에 이미 장애물(건물) 존재
        _buildingMap.Add(new int2(1, 0), new BuildingInfo(new Entity { Index = 10 }, BuildingTypeEnum.Storage, DirectionEnum.Up));

        // 3개 벨트 배치 요청 (AllowPartialPlacement 활성화)
        var req = CreatePlacementRequest(
            PlacementFlags.AllowPartialPlacement,
            new PlacementRequestCandidateElement(BuildingTypeEnum.Belt, new int2(1, 1), new int2(0, 0)),
            new PlacementRequestCandidateElement(BuildingTypeEnum.Belt, new int2(1, 1), new int2(1, 0)), // 충돌!
            new PlacementRequestCandidateElement(BuildingTypeEnum.Belt, new int2(1, 1), new int2(2, 0)));

        // Act
        UpdateCommandPhase();

        // Assert: 충돌한 후보(1)를 제외하고 유효한 2개의 공사 현장이 생성되어야 함
        var query = _entityManager.CreateEntityQuery(typeof(ConstructionSite), typeof(GridPosition));
        Assert.AreEqual(2, query.CalculateEntityCount());

        var positions = query.ToComponentDataArray<GridPosition>(Allocator.Temp);
        Assert.IsTrue((positions[0].Value.Equals(new int2(0, 0)) && positions[1].Value.Equals(new int2(2, 0))) ||
                      (positions[0].Value.Equals(new int2(2, 0)) && positions[1].Value.Equals(new int2(0, 0))));
        positions.Dispose();

        Assert.IsFalse(_entityManager.Exists(req));
    }

    [Test]
    public void Test05_BuildingConfig_ResearchLocked_RejectsPlacement()
    {
        // Arrange: ResearchBuilding이 IsUnlocked = false로 등록됨
        SetupBuildingConfig((
            BuildingTypeEnum.ResearchBuilding,
            1.0f,
            0,
            false,
            new[] { (ItemTypeEnum.Iron, 3) }));

        var req = CreatePlacementRequest(
            PlacementFlags.StrictAllOrNothing,
            new PlacementRequestCandidateElement(BuildingTypeEnum.ResearchBuilding, new int2(2, 2), new int2(0, 0)));

        // Act
        UpdateCommandPhase();

        // Assert: 잠긴 건물이므로 공사 현장이 생성되지 않고 거부되어야 함
        var query = _entityManager.CreateEntityQuery(typeof(ConstructionSite));
        Assert.AreEqual(0, query.CalculateEntityCount(), "Research locked building must be rejected.");

        Assert.IsFalse(_entityManager.Exists(req));
    }

    [Test]
    public void Test06_GroundItems_TagsAwaitingItemClearance()
    {
        // Arrange: (2, 2) 타일에 바닥 아이템 등록
        _itemMap.Add(new int2(2, 2), new Entity { Index = 55 });

        var req = CreatePlacementRequest(
            PlacementFlags.StrictAllOrNothing,
            new PlacementRequestCandidateElement(BuildingTypeEnum.Storage, new int2(1, 1), new int2(2, 2)));

        // Act
        UpdateCommandPhase();

        // Assert: 공사 현장은 생성되되, AwaitingItemClearance 플래그가 붙어있어야 함
        var query = _entityManager.CreateEntityQuery(typeof(ConstructionSite));
        Assert.AreEqual(1, query.CalculateEntityCount());

        var siteEntity = query.GetSingletonEntity();
        var site = _entityManager.GetComponentData<ConstructionSite>(siteEntity);
        Assert.IsTrue(site.Flags.HasFlag(ConstructionSiteFlags.AwaitingItemClearance), "Site must have AwaitingItemClearance flag when ground items exist.");

        Assert.IsFalse(_entityManager.Exists(req));
    }

    [Test]
    public void Test07_BatchInternalContention_ArbitratesDeterministically()
    {
        // Arrange: 동일 묶음 내에서 2개의 후보가 동일한 (0, 0)을 동시 요청
        var req = CreatePlacementRequest(
            PlacementFlags.AllowPartialPlacement,
            new PlacementRequestCandidateElement(BuildingTypeEnum.Storage, new int2(1, 1), new int2(0, 0)),
            new PlacementRequestCandidateElement(BuildingTypeEnum.Storage, new int2(1, 1), new int2(0, 0)));

        // Act
        UpdateCommandPhase();

        // Assert: 앞선 후보 0만 승인되어 총 1개 생성, 뒤 후보 1은 선점 충돌로 거부
        var query = _entityManager.CreateEntityQuery(typeof(ConstructionSite), typeof(PlacementStamp));
        Assert.AreEqual(1, query.CalculateEntityCount(), "Only first candidate must be approved in internal contention.");

        var stamp = query.GetSingleton<PlacementStamp>();
        Assert.AreEqual(0U, stamp.Order);

        Assert.IsFalse(_entityManager.Exists(req));
    }

    [TestCase(PlacementFlags.StrictAllOrNothing, PlacementFlags.StrictAllOrNothing, true)]
    [TestCase(PlacementFlags.StrictAllOrNothing, PlacementFlags.AllowPartialPlacement, true)]
    [TestCase(PlacementFlags.AllowPartialPlacement, PlacementFlags.StrictAllOrNothing, true)]
    [TestCase(PlacementFlags.AllowPartialPlacement, PlacementFlags.AllowPartialPlacement, true)]
    [TestCase(PlacementFlags.StrictAllOrNothing, PlacementFlags.StrictAllOrNothing, false)]
    [TestCase(PlacementFlags.StrictAllOrNothing, PlacementFlags.AllowPartialPlacement, false)]
    [TestCase(PlacementFlags.AllowPartialPlacement, PlacementFlags.StrictAllOrNothing, false)]
    [TestCase(PlacementFlags.AllowPartialPlacement, PlacementFlags.AllowPartialPlacement, false)]
    public void Test10_SeparateRequests_ReceiptOrderPreventsDuplicateFootprints(
        PlacementFlags firstFlags, PlacementFlags laterFlags, bool sameOrigin)
    {
        SetupBuildingConfig((BuildingTypeEnum.Storage, 1f, 4, true, null));
        Entity first = CreatePlacementRequest(firstFlags,
            new PlacementRequestCandidateElement(BuildingTypeEnum.Storage, new int2(2, 3), int2.zero, DirectionEnum.Right, 500UL));
        int2 overlappingOrigin = sameOrigin ? int2.zero : new int2(2, 1);
        Entity later = CreatePlacementRequest(laterFlags,
            new PlacementRequestCandidateElement(BuildingTypeEnum.Storage, new int2(2, 2), overlappingOrigin, DirectionEnum.Up, 1UL),
            new PlacementRequestCandidateElement(BuildingTypeEnum.Storage, new int2(1, 1), new int2(8, 8)));
        // 먼저 접수한 요청을 다른 archetype으로 옮겨 Query 배열 순서를 접수 순서의 근거로 삼지 않는다.
        _entityManager.AddComponentData(first, new Direction(DirectionEnum.Right));

        UpdateCommandPhase();

        bool allowPartial = laterFlags == PlacementFlags.AllowPartialPlacement;
        using var sites = _entityManager.CreateEntityQuery(typeof(ConstructionSite));
        Assert.AreEqual(allowPartial ? 2 : 1, sites.CalculateEntityCount());
        Assert.IsFalse(_entityManager.Exists(first));
        Assert.IsFalse(_entityManager.Exists(later));
        UpdateBuildingSpatialIndex();

        Entity winner = _buildingMap[int2.zero].Entity;
        Assert.AreEqual(500UL, _entityManager.GetComponentData<PlacementStamp>(winner).Tick);
        Assert.AreEqual(allowPartial ? 7 : 6, _buildingMap.Count());
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                Assert.AreEqual(winner, _buildingMap[new int2(x, y)].Entity);
            }
        }

        Assert.IsFalse(_buildingMap.ContainsKey(new int2(3, 1)));
        Assert.AreEqual(allowPartial, _buildingMap.ContainsKey(new int2(8, 8)));
    }

    [Test]
    public void Test11_StrictFailure_DoesNotKeepTentativeCellsOrReleaseEarlierApproval()
    {
        CreatePlacementRequest(PlacementFlags.StrictAllOrNothing,
            new PlacementRequestCandidateElement(BuildingTypeEnum.Storage, new int2(1, 1), int2.zero));
        CreatePlacementRequest(PlacementFlags.StrictAllOrNothing,
            new PlacementRequestCandidateElement(BuildingTypeEnum.Storage, new int2(1, 1), new int2(2, 0)),
            new PlacementRequestCandidateElement(BuildingTypeEnum.Storage, new int2(1, 1), int2.zero));
        CreatePlacementRequest(PlacementFlags.StrictAllOrNothing,
            new PlacementRequestCandidateElement(BuildingTypeEnum.Storage, new int2(1, 1), new int2(2, 0)),
            new PlacementRequestCandidateElement(BuildingTypeEnum.Storage, new int2(1, 1), int2.zero));
        // 세 번째도 앞 승인과 충돌해 전체 거부한다. 실패 묶음의 빈 셀은 네 번째가 사용할 수 있다.
        CreatePlacementRequest(PlacementFlags.StrictAllOrNothing,
            new PlacementRequestCandidateElement(BuildingTypeEnum.Storage, new int2(1, 1), new int2(2, 0)));

        UpdateCommandPhase();

        using var sites = _entityManager.CreateEntityQuery(typeof(ConstructionSite));
        Assert.AreEqual(2, sites.CalculateEntityCount());
        UpdateBuildingSpatialIndex();
        Assert.AreEqual(2, _buildingMap.Count());
        Assert.AreEqual(0U, _entityManager.GetComponentData<PlacementStamp>(_buildingMap[int2.zero].Entity).Order);
        Assert.IsTrue(_buildingMap.ContainsKey(new int2(2, 0)));
        Assert.AreNotEqual(_buildingMap[int2.zero].Entity, _buildingMap[new int2(2, 0)].Entity);
    }

    [TestCase(PlacementFlags.StrictAllOrNothing)]
    [TestCase(PlacementFlags.AllowPartialPlacement)]
    public void Test12_SeparateBeltDirectionRequests_FirstApprovalWins(PlacementFlags laterFlags)
    {
        Entity belt = _entityManager.CreateEntity(typeof(BuildingType), typeof(BuildingFootprint), typeof(GridPosition), typeof(Direction));
        _entityManager.SetComponentData(belt, new BuildingType(BuildingTypeEnum.Belt));
        _entityManager.SetComponentData(belt, new BuildingFootprint(new int2(1, 1)));
        _entityManager.SetComponentData(belt, new GridPosition(int2.zero));
        _entityManager.SetComponentData(belt, new Direction(DirectionEnum.Up));
        _buildingMap.Add(int2.zero, new BuildingInfo(belt, BuildingTypeEnum.Belt, DirectionEnum.Up));
        CreatePlacementRequest(PlacementFlags.StrictAllOrNothing,
            new PlacementRequestCandidateElement(BuildingTypeEnum.Belt, new int2(1, 1), int2.zero, DirectionEnum.Right));
        CreatePlacementRequest(laterFlags,
            new PlacementRequestCandidateElement(BuildingTypeEnum.Belt, new int2(1, 1), int2.zero, DirectionEnum.Down),
            new PlacementRequestCandidateElement(BuildingTypeEnum.Storage, new int2(1, 1), new int2(8, 8)));

        UpdateCommandPhase();

        bool allowPartial = laterFlags == PlacementFlags.AllowPartialPlacement;
        Assert.AreEqual(DirectionEnum.Right, _entityManager.GetComponentData<Direction>(belt).dir);
        using var sites = _entityManager.CreateEntityQuery(typeof(ConstructionSite));
        Assert.AreEqual(allowPartial ? 1 : 0, sites.CalculateEntityCount());
        UpdateBuildingSpatialIndex();
        Assert.AreEqual(belt, _buildingMap[int2.zero].Entity);
        Assert.AreEqual(DirectionEnum.Right, _buildingMap[int2.zero].Direction);
        Assert.AreEqual(allowPartial ? 2 : 1, _buildingMap.Count());
    }

    private void UpdateBuildingSpatialIndex()
    {
        _spatialSyncHandle.Update(_world.Unmanaged);
        using var fenceQuery = _entityManager.CreateEntityQuery(typeof(BuildingSpatialIndexFence));
        fenceQuery.GetSingleton<BuildingSpatialIndexFence>().Complete();
    }

    [Test]
    public void Test09_CandidateSpecificRequestTick_AssignsIndividualTicks()
    {
        // 동일 묶음 내에서 2개의 후보가 각각 다른 RequestTick(100UL, 200UL)을 지정
        var req = CreatePlacementRequest(
            PlacementFlags.StrictAllOrNothing,
            new PlacementRequestCandidateElement(BuildingTypeEnum.Storage, new int2(1, 1), new int2(0, 0), DirectionEnum.Up, 100UL),
            new PlacementRequestCandidateElement(BuildingTypeEnum.Storage, new int2(1, 1), new int2(2, 0), DirectionEnum.Up, 200UL));

        UpdateCommandPhase();

        var query = _entityManager.CreateEntityQuery(typeof(ConstructionSite), typeof(GridPosition), typeof(PlacementStamp));
        Assert.AreEqual(2, query.CalculateEntityCount());

        using var entities = query.ToEntityArray(Allocator.Temp);
        var stamp0 = _entityManager.GetComponentData<PlacementStamp>(entities[0]);
        var stamp1 = _entityManager.GetComponentData<PlacementStamp>(entities[1]);
        var pos0 = _entityManager.GetComponentData<GridPosition>(entities[0]);

        if (pos0.Value.x == 0)
        {
            Assert.AreEqual(100UL, stamp0.Tick);
            Assert.AreEqual(200UL, stamp1.Tick);
        }
        else
        {
            Assert.AreEqual(200UL, stamp0.Tick);
            Assert.AreEqual(100UL, stamp1.Tick);
        }
    }
}
