using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 자재 충족·현재 바닥 차단·완공 실패 보존에 대한 NUnit EditMode 회귀 검증.
/// 입력·검사: 도착량/실물/DB를 준비해 Construction Apply/ECB의 생성·소비·중단을 검사한다. 드론 이동/신호 Producer는 실행하지 않는다.
/// 수명: EcsWorldTestFixture가 각 사례의 독립 World를 준비하고 종료 시 해제한다.
/// </summary>
public class Phase7ConstructionCompletionTests : EcsWorldTestFixture
{
    private SystemHandle _lifecycleApplySystem;
    private EndBuildingEntityCommandBufferSystem _ecbSystem;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        CreateGameplayPrefabDatabases();
        _lifecycleApplySystem = _world.GetOrCreateSystem<ConstructionLifecycleApplySystem>();
        _ecbSystem = _world.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>();
    }

    private void RunCompletionPhase()
    {
        // fixture 도착량/실물에 완공 Apply를 실행한다. 생성 성공 뒤 같은 ECB에서 현장/자재가 삭제되어야 한다.
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
        _entityManager.AddComponentData(site, new ConstructionSite(targetType, flags));
        _entityManager.AddBuffer<ConstructionMaterialRequirementElement>(site);
        _entityManager.AddBuffer<StoredItemElement>(site);
        return site;
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

        // 현재 실물이 차단 원본이며 플래그는 완공 시스템이 매번 갱신한다.
        Entity floorItem = _entityManager.CreateEntity(typeof(ItemIdentity), typeof(ItemOwnership), typeof(GridPosition));
        _entityManager.SetComponentData(floorItem, new ItemIdentity(ItemTypeEnum.Iron));
        _entityManager.SetComponentData(floorItem, ItemOwnership.WorldItem);
        _entityManager.SetComponentData(floorItem, new GridPosition(new int2(3, 3)));

        // 2. 1차 실행: 바닥 실물이 남은 상태
        RunCompletionPhase();

        Assert.IsTrue(_entityManager.Exists(site), "바닥 청소 대기 중에는 완공으로 전환되지 않아야 함");
        Assert.IsTrue(_entityManager.Exists(item), "자재가 소비되지 않아야 함");

        // 3. 바닥 실물 제거. 플래그는 수동으로 해제하지 않는다.
        _entityManager.DestroyEntity(floorItem);

        // 4. 2차 실행: 플래그 해제 후 정상 완공
        RunCompletionPhase();

        Assert.IsFalse(_entityManager.Exists(site), "플래그 해제 후 공사 현장이 완공되어 파괴되어야 함");
        Assert.IsFalse(_entityManager.Exists(item), "자재가 정상 소비되어야 함");

        var storageQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<Storage>());
        Assert.AreEqual(1, storageQuery.CalculateEntityCount(), "완공된 Storage 건물이 생성되어야 함");
        storageQuery.Dispose();
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

        // 3. Copper까지 도착한 현장 상태를 직접 준비한 뒤 완공을 평가한다.
        Entity copperItem = StoreMaterialItem(site, ItemTypeEnum.Copper);
        var updatedRequirements = _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site);
        var copperRequirement = updatedRequirements[1];
        copperRequirement.DeliveredQuantity = 1;
        updatedRequirements[1] = copperRequirement;

        RunCompletionPhase();

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

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void SpawnRejected_PreservesSiteAndMaterials_AndStopsNextTick(int databaseFailure)
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
        Entity second = StoreMaterialItem(site, ItemTypeEnum.Iron);
        _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site).Add(
            new ConstructionMaterialRequirementElement(ItemTypeEnum.Iron, 2, deliveredQuantity: 2));

        var group = _world.GetOrCreateSystemManaged<GameSimulationGroup>();
        var building = _world.GetOrCreateSystemManaged<BuildingSimulationGroup>();
        var apply = _world.GetOrCreateSystemManaged<BuildingStateApplyGroup>();
        apply.AddSystemToUpdateList(_lifecycleApplySystem);
        apply.SortSystems();
        building.AddSystemToUpdateList(apply);
        building.AddSystemToUpdateList(_ecbSystem);
        building.SortSystems();
        group.AddSystemToUpdateList(building);
        group.SortSystems();

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
