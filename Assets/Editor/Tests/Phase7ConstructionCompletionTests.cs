using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Task 7.5 공사 완료 및 건물 전환 단위/통합 테스트.
/// </summary>
public class Phase7ConstructionCompletionTests : EcsWorldTestFixture
{
    private SystemHandle _lifecycleApplySystem;
    private SystemHandle _spatialSyncSystem;
    private EndStateApplyEntityCommandBufferSystem _ecbSystem;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        CreateGameplayPrefabDatabases();
        _lifecycleApplySystem = _world.GetOrCreateSystem<ConstructionLifecycleApplySystem>();
        _spatialSyncSystem = _world.GetOrCreateSystem<BuildingSpatialSyncSystem>();
        _ecbSystem = _world.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>();
    }

    private void RunCompletionPhase()
    {
        Simulation.UpdateAndComplete(_lifecycleApplySystem);
        Simulation.Playback(_ecbSystem);
    }

    private void RunMaterialApplyPhase()
    {
        Simulation.UpdateAndComplete(_lifecycleApplySystem);
        Simulation.Playback(_ecbSystem);
    }

    private Entity CreateSite(
        BuildingTypeEnum targetType,
        int2 pos,
        DirectionEnum dir = DirectionEnum.Up,
        int2 size = default,
        PlacementStamp stamp = default,
        ConstructionSiteFlags flags = ConstructionSiteFlags.None)
    {
        if (size.x <= 0 || size.y <= 0)
        {
            size = new int2(1, 1);
        }

        var site = _entityManager.CreateEntity();
        _entityManager.AddComponentData(site, new BuildingType(BuildingTypeEnum.ConstructionSite));
        _entityManager.AddComponentData(site, new BuildingFootprint(size));
        _entityManager.AddComponentData(site, new GridPosition(pos));
        _entityManager.AddComponentData(site, new Direction(dir));
        _entityManager.AddComponentData(site, stamp);
        _entityManager.AddComponentData(site, new ConstructionSite(targetType, 0f, flags));
        _entityManager.AddBuffer<ConstructionMaterialRequirementElement>(site);
        _entityManager.AddBuffer<StoredItemElement>(site);
        return site;
    }

    private Entity CreateWorldItem(ItemTypeEnum type)
    {
        var item = _entityManager.CreateEntity();
        _entityManager.AddComponentData(item, new ItemIdentity(type));
        _entityManager.AddComponentData(item, ItemOwnership.WorldItem);
        return item;
    }

    private Entity StoreMaterialItem(Entity site, ItemTypeEnum type)
    {
        var item = _entityManager.CreateEntity();
        _entityManager.AddComponentData(item, new ItemIdentity(type));
        _entityManager.AddComponentData(item, ItemOwnership.Stored(site));
        var buffer = _entityManager.GetBuffer<StoredItemElement>(site);
        buffer.Add(new StoredItemElement(item, type, 0));
        return item;
    }

    private Entity RequestSupply(Entity site, Entity item, ItemTypeEnum type)
    {
        var request = _entityManager.CreateEntity();
        _entityManager.AddComponentData(request, new SupplyConstructionMaterialRequest(site, item, type));
        return request;
    }

    [Test]
    public void Test01_CompleteConstruction_WhenSatisfied_DestroysSiteAndSpawnsBuilding()
    {
        // 1. Miner 공사 현장 생성 (자재 Iron 2개 요구 및 충족)
        int2 pos = new int2(5, 10);
        var stamp = new PlacementStamp(100, 1);
        var site = CreateSite(BuildingTypeEnum.Miner, pos, DirectionEnum.Right, new int2(2, 2), stamp);

        var reqBuffer = _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site);
        reqBuffer.Add(new ConstructionMaterialRequirementElement(ItemTypeEnum.Iron, 2, deliveredQuantity: 2));

        // 현장에 보관된 자재 2개
        var item1 = StoreMaterialItem(site, ItemTypeEnum.Iron);
        var item2 = StoreMaterialItem(site, ItemTypeEnum.Iron);

        Assert.IsTrue(_entityManager.Exists(site));
        Assert.IsTrue(_entityManager.Exists(item1));
        Assert.IsTrue(_entityManager.Exists(item2));

        // 2. 완공 파이프라인 실행
        RunCompletionPhase();

        // 3. 공사 현장 및 보관 자재 파괴 확인 (자재 소비 완료)
        Assert.IsFalse(_entityManager.Exists(site), "완공 시 공사 현장 엔티티는 파괴되어야 함");
        Assert.IsFalse(_entityManager.Exists(item1), "소비된 자재 엔티티 1은 파괴되어야 함");
        Assert.IsFalse(_entityManager.Exists(item2), "소비된 자재 엔티티 2는 파괴되어야 함");

        // 4. 완공 건물 엔티티 생성 및 속성 승계 검증
        var query = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<BuildingType>(),
            ComponentType.ReadOnly<GridPosition>(),
            ComponentType.ReadOnly<Direction>(),
            ComponentType.ReadOnly<PlacementStamp>(),
            ComponentType.ReadOnly<MinerState>()
        );

        Assert.AreEqual(1, query.CalculateEntityCount(), "완공된 Miner 건물이 정확히 1개 생성되어야 함");
        var buildingEntity = query.GetSingletonEntity();
        Assert.AreEqual(BuildingTypeEnum.Miner, _entityManager.GetComponentData<BuildingType>(buildingEntity).Type);
        Assert.AreEqual(pos, _entityManager.GetComponentData<GridPosition>(buildingEntity).Value);
        Assert.AreEqual(DirectionEnum.Right, _entityManager.GetComponentData<Direction>(buildingEntity).dir);
        Assert.AreEqual(stamp.Tick, _entityManager.GetComponentData<PlacementStamp>(buildingEntity).Tick);
        Assert.AreEqual(stamp.Order, _entityManager.GetComponentData<PlacementStamp>(buildingEntity).Order);

        query.Dispose();
    }

    [Test]
    public void Test02_IncompleteConstruction_AwaitingMaterials_DoesNotTransition()
    {
        // 1. Crafter 공사 현장: Iron 2개 요구 중 1개만 도착
        var site = CreateSite(BuildingTypeEnum.Crafter, new int2(0, 0), DirectionEnum.Up, new int2(2, 2));
        var reqBuffer = _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site);
        reqBuffer.Add(new ConstructionMaterialRequirementElement(ItemTypeEnum.Iron, 2, deliveredQuantity: 1));

        var item = StoreMaterialItem(site, ItemTypeEnum.Iron);

        // 2. 파이프라인 실행
        RunCompletionPhase();

        // 3. 완공되지 않고 현장 및 자재 유지 확인
        Assert.IsTrue(_entityManager.Exists(site), "자재가 부족한 현장은 파괴되지 않고 유지되어야 함");
        Assert.IsTrue(_entityManager.Exists(item), "현장 자재는 소비되지 않아야 함");

        var buildingQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<CrafterState>());
        Assert.AreEqual(0, buildingQuery.CalculateEntityCount(), "완공 건물이 생성되어서는 안 됨");
        buildingQuery.Dispose();
    }

    [Test]
    public void Test03_Construction_BlockedByItemClearanceFlag_DoesNotTransition()
    {
        // 1. 자재는 100% 충족되었으나 바닥 아이템 청소 대기 플래그가 활성화된 현장
        var site = CreateSite(
            BuildingTypeEnum.Storage,
            new int2(3, 3),
            DirectionEnum.Up,
            new int2(1, 1),
            flags: ConstructionSiteFlags.AwaitingItemClearance);

        var reqBuffer = _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site);
        reqBuffer.Add(new ConstructionMaterialRequirementElement(ItemTypeEnum.Iron, 1, deliveredQuantity: 1));
        var item = StoreMaterialItem(site, ItemTypeEnum.Iron);

        // 2. 1차 실행: 플래그 활성 상태
        RunCompletionPhase();

        Assert.IsTrue(_entityManager.Exists(site), "바닥 청소 대기 중에는 완공으로 전환되지 않아야 함");
        Assert.IsTrue(_entityManager.Exists(item), "자재가 소비되지 않아야 함");

        // 3. 바닥 청소 완료로 플래그 해제
        var siteData = _entityManager.GetComponentData<ConstructionSite>(site);
        siteData.Flags = ConstructionSiteFlags.None;
        _entityManager.SetComponentData(site, siteData);

        // 4. 2차 실행: 플래그 해제 후 정상 완공
        RunCompletionPhase();

        Assert.IsFalse(_entityManager.Exists(site), "플래그 해제 후 공사 현장이 완공되어 파괴되어야 함");
        Assert.IsFalse(_entityManager.Exists(item), "자재가 정상 소비되어야 함");

        var storageQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<Storage>());
        Assert.AreEqual(1, storageQuery.CalculateEntityCount(), "완공된 Storage 건물이 생성되어야 함");
        storageQuery.Dispose();
    }

    [Test]
    public void Test04_FullPipeline_SupplyLastMaterial_ImmediatelyTransitionsToBuilding()
    {
        // 1. Belt 현장: 자재 1개 요구, 미도착(0/1) 상태
        int2 pos = new int2(7, 7);
        var stamp = new PlacementStamp(50, 2);
        var site = CreateSite(BuildingTypeEnum.Belt, pos, DirectionEnum.Left, new int2(1, 1), stamp);
        var reqBuffer = _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site);
        reqBuffer.Add(new ConstructionMaterialRequirementElement(ItemTypeEnum.Iron, 1, deliveredQuantity: 0));

        // 2. 마지막 자재 공급 요청 발행
        var item = CreateWorldItem(ItemTypeEnum.Iron);
        var reqEntity = RequestSupply(site, item, ItemTypeEnum.Iron);

        // 3. 자재 수령 및 완공 통합 Phase 실행 (공급과 완공이 단일 틱에 즉시 원자적으로 완료됨)
        RunMaterialApplyPhase();

        Assert.IsFalse(_entityManager.Exists(reqEntity), "자재 공급 요청은 소비되어야 함");

        // 4. 완공 상태 검증
        Assert.IsFalse(_entityManager.Exists(site), "공사 현장은 완공되어 파괴되어야 함");
        Assert.IsFalse(_entityManager.Exists(item), "공급된 자재는 완공 시 소비되어 파괴되어야 함");

        var beltQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<BeltComponent>(),
            ComponentType.ReadOnly<GridPosition>(),
            ComponentType.ReadOnly<Direction>()
        );
        Assert.AreEqual(1, beltQuery.CalculateEntityCount(), "완공된 Belt 건물이 생성되어야 함");
        var beltEntity = beltQuery.GetSingletonEntity();
        Assert.AreEqual(pos, _entityManager.GetComponentData<GridPosition>(beltEntity).Value);
        Assert.AreEqual(DirectionEnum.Left, _entityManager.GetComponentData<Direction>(beltEntity).dir);
        beltQuery.Dispose();
    }

    [Test]
    public void Test05_MultipleMaterials_AllMustBeSatisfied()
    {
        // 1. Crafter: Iron 2개, Copper 1개 요구
        var site = CreateSite(BuildingTypeEnum.Crafter, new int2(2, 2), DirectionEnum.Down, new int2(2, 2));
        var reqBuffer = _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site);
        reqBuffer.Add(new ConstructionMaterialRequirementElement(ItemTypeEnum.Iron, 2, deliveredQuantity: 2));
        reqBuffer.Add(new ConstructionMaterialRequirementElement(ItemTypeEnum.Copper, 1, deliveredQuantity: 0));

        var iron1 = StoreMaterialItem(site, ItemTypeEnum.Iron);
        var iron2 = StoreMaterialItem(site, ItemTypeEnum.Iron);

        // 2. Iron만 충족된 상태에서 실행 -> 완공 대기
        RunCompletionPhase();
        Assert.IsTrue(_entityManager.Exists(site), "Copper가 미충족이므로 완공되지 않아야 함");
        Assert.IsTrue(_entityManager.Exists(iron1));
        Assert.IsTrue(_entityManager.Exists(iron2));

        // 3. Copper 자재 공급 및 수령 (모든 자재가 충족되므로 즉시 완공 전환)
        var copperItem = CreateWorldItem(ItemTypeEnum.Copper);
        RequestSupply(site, copperItem, ItemTypeEnum.Copper);

        RunMaterialApplyPhase();

        // 4. 모든 자재 충족 후 완공 및 모든 자재 파괴 확인
        Assert.IsFalse(_entityManager.Exists(site));
        Assert.IsFalse(_entityManager.Exists(iron1));
        Assert.IsFalse(_entityManager.Exists(iron2));
        Assert.IsFalse(_entityManager.Exists(copperItem));

        var crafterQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<CrafterState>());
        Assert.AreEqual(1, crafterQuery.CalculateEntityCount(), "완공된 Crafter 건물이 생성되어야 함");
        crafterQuery.Dispose();
    }

    [Test]
    public void Test06_ZeroMaterialRequirement_InstantlyCompletes()
    {
        // 1. 요구 자재가 0개인(버퍼가 빈) 현장
        var site = CreateSite(BuildingTypeEnum.Splitter, new int2(4, 4), DirectionEnum.Up, new int2(1, 1));

        // 2. 실행 즉시 완공 전환
        RunCompletionPhase();

        Assert.IsFalse(_entityManager.Exists(site), "자재 요구가 없는 현장은 즉시 완공되어야 함");
        var splitterQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<SplitterRoutingState>());
        Assert.AreEqual(1, splitterQuery.CalculateEntityCount(), "완공된 Splitter 건물이 생성되어야 함");
        splitterQuery.Dispose();
    }

    [Test]
    public void Test07_SpatialIndex_ContinuityVerification()
    {
        // 1. 공간 인덱스 동기화 확인
        int2 pos = new int2(12, 15);
        int2 size = new int2(2, 2);
        var site = CreateSite(BuildingTypeEnum.Miner, pos, DirectionEnum.Up, size);
        var reqBuffer = _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site);
        reqBuffer.Add(new ConstructionMaterialRequirementElement(ItemTypeEnum.Iron, 1, deliveredQuantity: 1));
        var item = StoreMaterialItem(site, ItemTypeEnum.Iron);

        // 공사 현장 상태에서 공간 인덱스 동기화
        Simulation.UpdateAndComplete(_spatialSyncSystem);

        var index = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<BuildingSpatialIndex>())
            .GetSingleton<BuildingSpatialIndex>();

        Assert.IsTrue(index.Map.TryGetValue(pos, out var siteInfo), "공사 중에는 공간 인덱스에 등록되어 있어야 함");
        Assert.AreEqual(BuildingTypeEnum.ConstructionSite, siteInfo.Type);

        // 2. 완공 전환 및 공간 인덱스 재동기화
        RunCompletionPhase();
        Simulation.UpdateAndComplete(_spatialSyncSystem);

        // 3. 완공 건물로 공백 없이 갱신 등록되었는지 검증
        Assert.IsTrue(index.Map.TryGetValue(pos, out var buildingInfo), "완공 후에도 동일 좌표가 점유되어 있어야 함");
        Assert.AreEqual(BuildingTypeEnum.Miner, buildingInfo.Type, "건물 타입이 Miner로 갱신되어야 함");
        Assert.AreNotEqual(site, buildingInfo.Entity, "엔티티 참조가 완공 건물 엔티티로 교체되어야 함");
    }

    [Test]
    public void Test08_InvalidTargetBuildingType_DoesNotTransition()
    {
        // 1. None 타입 목표를 가진 비정상 현장
        var siteNone = CreateSite(BuildingTypeEnum.None, new int2(1, 1));
        // 2. ConstructionSite를 목표로 하는 비정상 현장
        var siteLoop = CreateSite(BuildingTypeEnum.ConstructionSite, new int2(2, 2));

        RunCompletionPhase();

        Assert.IsTrue(_entityManager.Exists(siteNone), "유효하지 않은 목표 타입은 완공 전환되지 않아야 함");
        Assert.IsTrue(_entityManager.Exists(siteLoop), "ConstructionSite로의 순환 전환은 차단되어야 함");
    }

    [TestCase(0, false)]
    [TestCase(1, false)]
    [TestCase(2, false)]
    [TestCase(2, true)]
    public void SpawnRejected_PreservesSiteAndMaterials_AndStopsNextTick(int databaseFailure, bool supplyLastMaterial)
    {
        Entity database = _entityManager.CreateEntityQuery(typeof(BuildingPrefabDatabase)).GetSingletonEntity();
        if (databaseFailure == 0)
        {
            _entityManager.DestroyEntity(database);
        }
        else
        {
            var entries = _entityManager.GetBuffer<BuildingPrefabElement>(database);
            entries.Clear();
            if (databaseFailure == 2)
            {
                entries.Add(new BuildingPrefabElement(BuildingTypeEnum.Miner, Entity.Null, new int2(2, 2)));
            }
        }

        Entity site = CreateSite(BuildingTypeEnum.Miner, new int2(4, 6));
        Entity first = StoreMaterialItem(site, ItemTypeEnum.Iron);
        Entity second = supplyLastMaterial ? CreateWorldItem(ItemTypeEnum.Iron) : StoreMaterialItem(site, ItemTypeEnum.Iron);
        _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site).Add(
            new ConstructionMaterialRequirementElement(ItemTypeEnum.Iron, 2, supplyLastMaterial ? 1 : 2));
        if (supplyLastMaterial)
        {
            RequestSupply(site, second, ItemTypeEnum.Iron);
        }

        var group = _world.GetOrCreateSystemManaged<GameSimulationGroup>();
        var apply = _world.GetOrCreateSystemManaged<StateApplyGroup>();
        apply.AddSystemToUpdateList(_lifecycleApplySystem);
        apply.AddSystemToUpdateList(_ecbSystem);
        apply.SortSystems();
        group.AddSystemToUpdateList(apply);

        UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error,
            new System.Text.RegularExpressions.Regex(".*Missing prefab for building type.*"));
        group.Update();
        group.Update(); // 오류 게시 후에는 다시 Spawn을 시도하지 않아야 한다.

        Assert.IsTrue(_entityManager.Exists(site));
        Assert.IsTrue(_entityManager.Exists(first));
        Assert.IsTrue(_entityManager.Exists(second));
        Assert.AreEqual(site, _entityManager.GetComponentData<ItemOwnership>(first).Owner);
        Assert.AreEqual(site, _entityManager.GetComponentData<ItemOwnership>(second).Owner);
        var stored = _entityManager.GetBuffer<StoredItemElement>(site);
        Assert.AreEqual(2, stored.Length);
        Assert.AreEqual(first, stored[0].ItemEntity);
        Assert.AreEqual(second, stored[1].ItemEntity);
        Assert.AreEqual(2, _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site)[0].DeliveredQuantity);
        using var buildings = _entityManager.CreateEntityQuery(typeof(MinerState));
        using var errors = _entityManager.CreateEntityQuery(typeof(SimulationFatalError));
        Assert.AreEqual(0, buildings.CalculateEntityCount());
        Assert.AreEqual(1, errors.CalculateEntityCount());
    }
}
