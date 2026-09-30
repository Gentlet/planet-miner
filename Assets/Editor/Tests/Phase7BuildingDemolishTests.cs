using System.Text.RegularExpressions;
using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Task 7.7 건물 철거 코어 및 내용물/자재 반환 단위/통합 테스트.
/// </summary>
public class Phase7BuildingDemolishTests : EcsWorldTestFixture
{
    private SystemHandle _demolitionCommandSystem;
    private EndCommandEntityCommandBufferSystem _endCommand;
    private SystemHandle _lifecycleApplySystem;
    private SystemHandle _spatialSyncSystem;
    private EndStateApplyEntityCommandBufferSystem _ecbSystem;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        CreateGameplayPrefabDatabases();
        _demolitionCommandSystem = _world.GetOrCreateSystem<BuildingDemolitionCommandSystem>();
        _endCommand = _world.GetOrCreateSystemManaged<EndCommandEntityCommandBufferSystem>();
        _lifecycleApplySystem = _world.GetOrCreateSystem<BuildingLifecycleApplySystem>();
        _spatialSyncSystem = _world.GetOrCreateSystem<BuildingSpatialSyncSystem>();
        _ecbSystem = _world.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>();
    }

    private void RunDemolishPhase()
    {
        RunDemolitionCommandPhase();
        Simulation.UpdateAndComplete(_lifecycleApplySystem);
        Simulation.Playback(_ecbSystem);
    }

    private void RunDemolitionCommandPhase()
    {
        Simulation.UpdateAndComplete(_demolitionCommandSystem);
        Simulation.Playback(_endCommand);
    }

    private Entity CreateBuilding(
        BuildingTypeEnum type,
        int2 pos,
        DirectionEnum dir = DirectionEnum.Up,
        int2 size = default,
        PlacementStamp stamp = default)
    {
        if (size.x <= 0 || size.y <= 0)
        {
            size = new int2(1, 1);
        }

        var building = _entityManager.CreateEntity();
        _entityManager.AddComponentData(building, new BuildingType(type));
        _entityManager.AddComponentData(building, new BuildingFootprint(size));
        _entityManager.AddComponentData(building, new GridPosition(pos));
        _entityManager.AddComponentData(building, new Direction(dir));
        _entityManager.AddComponentData(building, stamp);
        _entityManager.AddComponentData(building, LocalTransform.FromPosition(new float3(pos.x, pos.y, 0f)));

        return building;
    }

    private Entity RequestDemolish(Entity targetBuilding)
    {
        var req = _entityManager.CreateEntity();
        _entityManager.AddComponentData(req, new DemolishBuildingRequest(targetBuilding));
        return req;
    }

    private Entity CreateStoredItem(Entity owner, ItemTypeEnum type)
    {
        var item = _entityManager.CreateEntity();
        _entityManager.AddComponentData(item, new ItemIdentity(type));
        _entityManager.AddComponentData(item, ItemOwnership.Stored(owner));
        _entityManager.AddComponentData(item, new GridPosition(int2.zero));
        _entityManager.AddComponentData(item, LocalTransform.FromPosition(float3.zero));
        _entityManager.AddComponentData(item, new DisableRendering());

        if (!_entityManager.HasBuffer<StoredItemElement>(owner))
        {
            _entityManager.AddBuffer<StoredItemElement>(owner);
        }
        _entityManager.GetBuffer<StoredItemElement>(owner).Add(new StoredItemElement(item, type, 0));

        return item;
    }

    private Entity CreateProductItem(Entity owner, ItemTypeEnum type)
    {
        var item = _entityManager.CreateEntity();
        _entityManager.AddComponentData(item, new ItemIdentity(type));
        _entityManager.AddComponentData(item, ItemOwnership.Stored(owner));
        _entityManager.AddComponentData(item, new GridPosition(int2.zero));
        _entityManager.AddComponentData(item, LocalTransform.FromPosition(float3.zero));
        _entityManager.AddComponentData(item, new DisableRendering());

        if (!_entityManager.HasBuffer<ProductItemElement>(owner))
        {
            _entityManager.AddBuffer<ProductItemElement>(owner);
        }
        _entityManager.GetBuffer<ProductItemElement>(owner).Add(new ProductItemElement(item, type, 0));

        return item;
    }

    private Entity SetupBuildingConfigWithMaterials(BuildingTypeEnum type, ItemTypeEnum materialType, int quantity)
    {
        var configEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(configEntity, new BuildingConfig());
        _entityManager.AddBuffer<BuildingConfigElement>(configEntity);

        var materialBuffer = _entityManager.AddBuffer<BuildingConstructionMaterialElement>(configEntity);
        materialBuffer.Add(new BuildingConstructionMaterialElement(type, materialType, quantity));

        return configEntity;
    }

    [Test]
    public void Test02_DemolishBuilding_WithStoredAndProductItems_EjectsAllAsWorldItems()
    {
        // 1. Crafter 건물 및 내부 Stored/Product 아이템 생성
        int2 pos = new int2(10, 10);
        var building = CreateBuilding(BuildingTypeEnum.Crafter, pos, size: new int2(2, 2));
        var storedItem = CreateStoredItem(building, ItemTypeEnum.Iron);
        var productItem = CreateProductItem(building, ItemTypeEnum.Copper);

        // 2. 철거 요청 및 실행
        var req = RequestDemolish(building);
        RunDemolishPhase();

        // 3. 건물 파괴 확인
        Assert.IsFalse(_entityManager.Exists(building));
        Assert.IsFalse(_entityManager.Exists(req));

        // 4. 내용물 아이템 전수 방출 검증
        Assert.IsTrue(_entityManager.Exists(storedItem), "Stored 아이템은 보존되어야 함");
        Assert.IsTrue(_entityManager.Exists(productItem), "Product 아이템은 보존되어야 함");

        var storedOwnership = _entityManager.GetComponentData<ItemOwnership>(storedItem);
        Assert.AreEqual(ItemOwnership.WorldItem.Owner, storedOwnership.Owner, "Stored 아이템은 WorldItem으로 전환되어야 함");
        Assert.IsFalse(_entityManager.HasComponent<DisableRendering>(storedItem), "렌더링이 활성화되어야 함");
        Assert.AreEqual(pos, _entityManager.GetComponentData<GridPosition>(storedItem).Value, "건물 위치로 배치되어야 함");

        var productOwnership = _entityManager.GetComponentData<ItemOwnership>(productItem);
        Assert.AreEqual(ItemOwnership.WorldItem.Owner, productOwnership.Owner, "Product 아이템은 WorldItem으로 전환되어야 함");
        Assert.IsFalse(_entityManager.HasComponent<DisableRendering>(productItem), "렌더링이 활성화되어야 함");
        Assert.AreEqual(pos, _entityManager.GetComponentData<GridPosition>(productItem).Value, "건물 위치로 배치되어야 함");
    }

    [Test]
    public void Test03_DemolishBuilding_RefundsConstructionMaterials_100Percent()
    {
        // 1. Belt 건설 비용(Iron 3개) 설정 및 Belt 건물 생성
        int2 pos = new int2(3, 4);
        var configEntity = SetupBuildingConfigWithMaterials(BuildingTypeEnum.Belt, ItemTypeEnum.Iron, 3);
        var belt = CreateBuilding(BuildingTypeEnum.Belt, pos);

        // 2. 철거 요청 및 실행
        var req = RequestDemolish(belt);
        RunDemolishPhase();

        // 3. 건물 파괴 및 요청 소비 확인
        Assert.IsFalse(_entityManager.Exists(belt));
        Assert.IsFalse(_entityManager.Exists(req));

        // 4. 건설 자재 3개 전액 환급 스폰 확인
        var itemQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<ItemIdentity>(),
            ComponentType.ReadOnly<ItemOwnership>(),
            ComponentType.ReadOnly<GridPosition>()
        );

        Assert.AreEqual(3, itemQuery.CalculateEntityCount(), "건설 자재 3개가 100% 월드 아이템으로 환급 스폰되어야 함");

        using (var items = itemQuery.ToEntityArray(Allocator.Temp))
        {
            for (int i = 0; i < items.Length; i++)
            {
                var identity = _entityManager.GetComponentData<ItemIdentity>(items[i]);
                var ownership = _entityManager.GetComponentData<ItemOwnership>(items[i]);
                var gridPos = _entityManager.GetComponentData<GridPosition>(items[i]);

                Assert.AreEqual(ItemTypeEnum.Iron, identity.Type);
                Assert.AreEqual(ItemOwnership.WorldItem.Owner, ownership.Owner);
                Assert.AreEqual(pos, gridPos.Value);
                Assert.IsFalse(_entityManager.HasComponent<DisableRendering>(items[i]), "환급된 월드 아이템은 렌더링되어야 함");
            }
        }

        itemQuery.Dispose();
    }

    [Test]
    public void Test04_DemolishBuilding_Indestructible_StrictRejection()
    {
        // 1. IndestructibleBuilding 태그 보유 건물 생성
        var building = CreateBuilding(BuildingTypeEnum.MainFacility, new int2(0, 0), size: new int2(3, 3));
        _entityManager.AddComponentData(building, new IndestructibleBuilding());

        // 2. 철거 요청 및 실행
        var req = RequestDemolish(building);
        RunDemolishPhase();

        // 3. 철거 거부 확인 (건물 온전히 보존, 요청만 소비)
        Assert.IsTrue(_entityManager.Exists(building), "Indestructible 건물은 철거되지 않고 보존되어야 함");
        Assert.IsFalse(_entityManager.Exists(req), "거부된 철거 요청은 안전하게 소비되어야 함");
    }

    [Test]
    public void Test05_DemolishBuilding_InvalidOrConstructionSite_StrictRejection()
    {
        // 1. 공사 현장 생성 (공사 현장은 철거 대상이 아님 -> Cancel로만 취소 가능)
        var site = _entityManager.CreateEntity();
        _entityManager.AddComponentData(site, new BuildingType(BuildingTypeEnum.ConstructionSite));
        _entityManager.AddComponentData(site, new GridPosition(new int2(1, 1)));

        // 2. 공사 현장에 철거 요청 발행
        var req = RequestDemolish(site);
        RunDemolishPhase();

        // 3. 거부 확인 (현장 보존, 요청 소비)
        Assert.IsTrue(_entityManager.Exists(site), "공사 현장은 철거 대상이 아니므로 보존되어야 함");
        Assert.IsFalse(_entityManager.Exists(req), "무효한 요청은 소비되어야 함");
    }

    [Test]
    public void Test06_DemolishBuilding_DuplicateRequests_SameFrame_SafeAndIdempotent()
    {
        // 1. 건물 및 동일 건물 대상 복수 철거 요청 발행
        var building = CreateBuilding(BuildingTypeEnum.PowerPole, new int2(8, 8));
        var req1 = RequestDemolish(building);
        var req2 = RequestDemolish(building);

        // 2. 철거 Phase 실행
        RunDemolishPhase();

        // 3. 건물 파괴 및 두 요청 모두 안전하게 소비 확인
        Assert.IsFalse(_entityManager.Exists(building));
        Assert.IsFalse(_entityManager.Exists(req1));
        Assert.IsFalse(_entityManager.Exists(req2));
    }

    [Test]
    public void Test08_DemolishBelt_WithItemOnBelt_PreservesItemAsStaticWorldItem()
    {
        // 1. ItemSpatialIndex 초기화
        var spatialSync = _world.GetOrCreateSystem<ItemSpatialSyncSystem>();
        Simulation.UpdateAndComplete(spatialSync);

        // 2. 벨트 건물 및 벨트 위 아이템 생성
        int2 beltPos = new int2(20, 20);
        var belt = CreateBuilding(BuildingTypeEnum.Belt, beltPos);

        var item = _entityManager.CreateEntity();
        _entityManager.AddComponentData(item, new ItemIdentity(ItemTypeEnum.Iron));
        _entityManager.AddComponentData(item, ItemOwnership.WorldItem);
        _entityManager.AddComponentData(item, new GridPosition(beltPos));
        _entityManager.AddComponentData(item, LocalTransform.FromPosition(new float3(beltPos.x, beltPos.y, 0f)));
        _entityManager.AddComponentData(item, new BeltMovementState(0.5f));

        // 공간 인덱스 동기화 (아이템을 공간 맵에 등록)
        Simulation.UpdateAndComplete(spatialSync);

        // 3. 벨트 철거 요청 및 실행
        RequestDemolish(belt);
        RunDemolishPhase();

        // 4. 벨트 파괴 확인 및 아이템의 BeltMovementState 비활성화 확인
        Assert.IsFalse(_entityManager.Exists(belt), "벨트는 파괴되어야 함");
        Assert.IsTrue(_entityManager.Exists(item), "아이템은 바닥에 보존되어야 함");

        bool isBeltMoving = _entityManager.IsComponentEnabled<BeltMovementState>(item);
        Assert.IsFalse(isBeltMoving, "벨트가 철거되었으므로 BeltMovementState는 비활성화되어 정적 WorldItem으로 유지되어야 함");
        Assert.AreEqual(ItemOwnership.WorldItem.Owner, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Test10_DemolishBuilding_WithPrefabDb_MissingPrefab_StrictFailPolicy(bool missingDatabase)
    {
        // 1. 프리팹 DB는 등록되었으나 Iron_Ore만 있고 Iron은 누락된 상태
        LogAssert.Expect(LogType.Error, new Regex(".*Missing prefab for refund item type.*"));

        var mockPrefab = _entityManager.CreateEntity(typeof(Prefab), typeof(ItemIdentity), typeof(LocalTransform));
        _entityManager.SetComponentData(mockPrefab, new ItemIdentity(ItemTypeEnum.Iron_Ore));

        TestPrefabDatabaseFactory.RemoveDatabase<ItemPrefabDatabase>(_entityManager);

        if (!missingDatabase)
        {
            var dbEntity = _entityManager.CreateEntity(typeof(ItemPrefabDatabase));
            var prefabBuffer = _entityManager.AddBuffer<ItemPrefabElement>(dbEntity);
            prefabBuffer.Add(new ItemPrefabElement(ItemTypeEnum.Iron_Ore, mockPrefab));
        }

        // 2. Iron(누락된 프리팹)을 요구하는 건물 생성
        int2 pos = new int2(7, 7);
        SetupBuildingConfigWithMaterials(BuildingTypeEnum.Storage, ItemTypeEnum.Iron, 5);
        var storage = CreateBuilding(BuildingTypeEnum.Storage, pos);

        // 3. 철거 실행
        RequestDemolish(storage);
        RunDemolishPhase();

        // 4. 건물은 정상 파괴되되, Strict Fail에 의해 누락된 프리팹 아이템은 스폰되지 않아야 함 (고스트/투명 엔티티 방지)
        Assert.IsFalse(_entityManager.Exists(storage), "건물 자체는 파괴되어야 함");

        var query = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<ItemIdentity>(),
            ComponentType.ReadOnly<ItemOwnership>(),
            ComponentType.Exclude<Prefab>()
        );
        Assert.AreEqual(0, query.CalculateEntityCount(), "프리팹이 누락된 환급 아이템은 Strict Fail로 인해 스폰되지 않아야 함");
        query.Dispose();
    }

    [Test]
    public void Test11_DemolishBelt_SameTickMovement_TimingCorrectness()
    {
        // 시나리오: 틱 N의 Phase 4에서 아이템이 (10, 10) 벨트 A에서 (11, 10) 벨트 B로 이동한 상태.
        // 즉, 아이템의 실제 GridPosition은 이미 (11, 10)로 갱신됨.
        int2 posA = new int2(10, 10);
        int2 posB = new int2(11, 10);

        var beltA = CreateBuilding(BuildingTypeEnum.Belt, posA);
        var beltB = CreateBuilding(BuildingTypeEnum.Belt, posB);

        var item = _entityManager.CreateEntity();
        _entityManager.AddComponentData(item, new ItemIdentity(ItemTypeEnum.Copper));
        _entityManager.AddComponentData(item, ItemOwnership.WorldItem);
        // Phase 4가 끝난 직후라 아이템은 이미 posB에 위치함
        _entityManager.AddComponentData(item, new GridPosition(posB));
        _entityManager.AddComponentData(item, LocalTransform.FromPosition(new float3(posB.x, posB.y, 0f)));
        _entityManager.AddComponentData(item, new BeltMovementState(0.1f));

        // 1. 아이템이 이미 떠난 이전 벨트 A를 철거
        RequestDemolish(beltA);
        RunDemolishPhase();

        // 검증: 아이템은 posB에 있으므로, beltA가 철거되어도 BeltMovementState는 안전하게 활성화 유지되어야 함 (엉뚱한 아이템 차단 방지)
        Assert.IsFalse(_entityManager.Exists(beltA), "벨트 A는 파괴되어야 함");
        Assert.IsTrue(_entityManager.IsComponentEnabled<BeltMovementState>(item), "벨트 A 철거는 이미 떠난 아이템의 BeltMovementState에 영향을 주지 않아야 함");

        // 2. 이제 아이템이 실제로 위치하고 있는 벨트 B를 철거
        RequestDemolish(beltB);
        RunDemolishPhase();

        // 검증: 아이템이 위치한 벨트 B가 철거되었으므로, BeltMovementState가 정상적으로 비활성화되어야 함
        Assert.IsFalse(_entityManager.Exists(beltB), "벨트 B는 파괴되어야 함");
        Assert.IsFalse(_entityManager.IsComponentEnabled<BeltMovementState>(item), "벨트 B가 철거되었으므로 BeltMovementState가 비활성화되어야 함");
        Assert.AreEqual(posB, _entityManager.GetComponentData<GridPosition>(item).Value, "아이템 좌표는 posB로 유지되어야 함");
    }

    [Test]
    public void Test12_DemolishBelt_WithConcurrentRoutingAndOutput_OrderingGuaranteed()
    {
        // 시나리오: 동일 프레임 StateApplyGroup에서 Routing/Output으로 벨트 위로 이동된 아이템과 해당 벨트의 철거가 동시에 일어남.
        // UpdateAfter(RoutingApplySystem, BuildingItemStorageApplySystem) 덕분에 물류가 먼저 반영된 후 철거 Job이 실행되어야 함.
        var stateApplyGroup = _world.GetOrCreateSystemManaged<StateApplyGroup>();
        stateApplyGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<RoutingApplySystem>());
        stateApplyGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingItemStorageApplySystem>());
        stateApplyGroup.AddSystemToUpdateList(_lifecycleApplySystem);
        stateApplyGroup.AddSystemToUpdateList(_ecbSystem);
        stateApplyGroup.SortSystems();

        int2 targetPos = new int2(20, 20);
        var targetBelt = CreateBuilding(BuildingTypeEnum.Belt, targetPos);

        // 이전 벨트 또는 라우터에서 targetBelt로 진입하기로 확정된 아이템
        var item = _entityManager.CreateEntity();
        _entityManager.AddComponentData(item, new ItemIdentity(ItemTypeEnum.Iron));
        _entityManager.AddComponentData(item, ItemOwnership.WorldItem);
        _entityManager.AddComponentData(item, new GridPosition(new int2(19, 20)));
        _entityManager.AddComponentData(item, LocalTransform.FromPosition(new float3(19f, 20f, 0f)));
        _entityManager.AddComponentData(item, new BeltMovementState(1.0f));

        // RoutingTransferDecision 활성화 (아이템을 targetBelt로 이동시키도록)
        var routerEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(routerEntity, new RoutingTransferDecision(item, Entity.Null, targetBelt));
        _entityManager.AddComponentData(routerEntity, new SplitterRoutingState(Entity.Null, DirectionEnum.Right));

        // 동시에 targetBelt에 대한 철거 요청 인큐
        RequestDemolish(targetBelt);
        RunDemolitionCommandPhase();

        // StateApplyGroup 1회 업데이트 (RoutingApply -> BuildingLifecycleApply -> EndStateApply ECB 순차 실행)
        stateApplyGroup.Update();

        // 검증:
        // 1. targetBelt는 완전히 파괴됨
        Assert.IsFalse(_entityManager.Exists(targetBelt), "targetBelt는 파괴되어야 함");
        // 2. 아이템은 targetPos(20, 20)로 이동 반영됨
        Assert.AreEqual(targetPos, _entityManager.GetComponentData<GridPosition>(item).Value, "아이템은 목적지 벨트 좌표로 전송되어야 함");
        // 3. Routing이 먼저 전송한 후 BuildingLifecycle이 철거했으므로, 아이템의 BeltMovementState는 안전하게 꺼져 있어야 함!
        Assert.IsFalse(_entityManager.IsComponentEnabled<BeltMovementState>(item), "철거된 벨트에 도착한 아이템의 이동 상태는 안전하게 비활성화되어야 함");
    }

    [Test]
    public void Test13_EndCommandECB_Playback_ImmediateAvailabilityForDownstreamPhases()
    {
        // 시나리오: Phase 1 CommandGroup에서 배치 요청을 수행하면,
        // EndCommandEntityCommandBufferSystem에 의해 동일 프레임 Phase 2 진입 전에 물리적 ConstructionSite가 즉시 실체화됨.
        var commandGroup = _world.GetOrCreateSystemManaged<CommandGroup>();
        var placementCommand = _world.GetOrCreateSystem<BuildingPlacementCommandSystem>();
        var endCommandEcb = _world.GetOrCreateSystemManaged<EndCommandEntityCommandBufferSystem>();
        commandGroup.AddSystemToUpdateList(placementCommand);
        commandGroup.AddSystemToUpdateList(endCommandEcb);
        commandGroup.SortSystems();

        // 공간 인덱스 준비
        _world.GetOrCreateSystem<BuildingSpatialSyncSystem>();
        _world.GetOrCreateSystem<ResourceSpatialSyncSystem>();
        _world.GetOrCreateSystem<ItemSpatialSyncSystem>();

        int2 sitePos = new int2(30, 30);
        var reqEntity = _entityManager.CreateEntity(typeof(BuildingPlacementRequest));
        _entityManager.SetComponentData(reqEntity, new BuildingPlacementRequest(PlacementFlags.StrictAllOrNothing));
        var buffer = _entityManager.AddBuffer<PlacementRequestCandidateElement>(reqEntity);
        buffer.Add(new PlacementRequestCandidateElement(BuildingTypeEnum.Storage, new int2(1, 1), sitePos, DirectionEnum.Up));

        // CommandGroup 1회 실행 (BuildingPlacementCommandSystem -> EndCommandEntityCommandBufferSystem)
        commandGroup.Update();

        // 검증:
        // 1. 요청 엔티티는 Phase 1 끝에서 이미 파괴됨
        Assert.IsFalse(_entityManager.Exists(reqEntity), "배치 요청 엔티티는 CommandGroup 끝에서 파괴되어야 함");

        // 2. ConstructionSite 엔티티가 월드에 물리적으로 이미 실체화되어 존재함 (동일 프레임 Phase 2~4에서 즉시 접근 가능)
        var siteQuery = _entityManager.CreateEntityQuery(typeof(ConstructionSite), typeof(GridPosition));
        Assert.AreEqual(1, siteQuery.CalculateEntityCount(), "CommandGroup 종료 직후 동일 프레임에 ConstructionSite가 실체화되어야 함");
        var siteEntity = siteQuery.GetSingletonEntity();
        Assert.AreEqual(sitePos, _entityManager.GetComponentData<GridPosition>(siteEntity).Value);
        siteQuery.Dispose();
    }
}
