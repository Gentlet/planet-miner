using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>
/// 역할·목적: 현재 월드 실물의 현장 완공 차단과 내부 World Spawn 거부에 대한 NUnit EditMode 회귀 검증.
/// 입력·검사: 회전 footprint·Owner/GridPosition·Destroy 입력으로 StateApply의 차단/완공/스폰 결과를 검사한다.
/// 수명: EcsWorldTestFixture가 각 사례의 독립 World를 준비하고 종료 시 해제한다.
/// </summary>
public class ConstructionClearanceTests : EcsWorldTestFixture
{
    private BuildingSimulationGroup _building;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        CreateGameplayPrefabDatabases();
        _building = _world.GetOrCreateSystemManaged<BuildingSimulationGroup>();
        var apply = _world.GetOrCreateSystemManaged<BuildingStateApplyGroup>();
        _building.AddSystemToUpdateList(apply);
        _building.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>());
        apply.AddSystemToUpdateList(_world.GetOrCreateSystem<ConstructionLifecycleApplySystem>());
        apply.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemOwnershipApplySystem>());
        apply.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingLifecycleApplySystem>());
        apply.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemLifecycleApplySystem>());
        apply.SortSystems();
        _building.SortSystems();
    }

    [TestCase(DirectionEnum.Up, true)]
    [TestCase(DirectionEnum.Right, false)]
    public void Clearance_UsesRotatedFootprintAndCurrentWorldOwnership(DirectionEnum direction, bool blocked)
    {
        Entity site = CreateSite(new int2(5, 5), new int2(2, 1), direction, required: 1);
        CreateItem(new int2(6, 5));
        Entities.PrepareConstructionConfiguration();
        _building.Update();
        Assert.AreEqual(blocked, IsBlocked(site));
        Assert.IsTrue(_entityManager.Exists(site), "자재 미충족 현장은 차단 여부와 무관하게 남는다.");
    }

    [Test]
    public void WorldItemMovingIntoAndOutOfSite_UpdatesClearanceWithoutWaitingForSpatialSync()
    {
        // 맵 재구축 없이 실물 좌표만 바꿔 차단 판단이 이전 틱 인덱스에 의존하지 않는지 분리한다.
        Entity site = CreateSite(int2.zero, new int2(1, 1), required: 1);
        Entity item = CreateItem(new int2(8, 8));
        Entities.PrepareConstructionConfiguration();
        _building.Update();
        Assert.IsFalse(IsBlocked(site));
        _entityManager.SetComponentData(item, new GridPosition(int2.zero));
        Entities.PrepareConstructionConfiguration();
        _building.Update();
        Assert.IsTrue(IsBlocked(site));
        _entityManager.SetComponentData(item, new GridPosition(new int2(8, 8)));
        Entities.PrepareConstructionConfiguration();
        _building.Update();
        Assert.IsFalse(IsBlocked(site));
    }

    [Test]
    public void LastWorldItemCollectedBeforeCompletion_ClearsTheFlagAndCompletesInTheSameTick()
    {
        Entity site = CreateSite(int2.zero, new int2(1, 1), flags: ConstructionSiteFlags.AwaitingItemClearance);
        Entity item = CreateItem(int2.zero);
        Entity holder = _entityManager.CreateEntity(typeof(StoredItemElement));
        using var ecb = new EntityCommandBuffer(Allocator.TempJob);
        Assert.IsTrue(ItemOwnershipApplySystem.TryTransferItem(_entityManager, item, ItemTypeEnum.Iron,
            Entity.Null, holder, 0, new int2(2, 2), ecb));
        ecb.Playback(_entityManager);
        Entities.PrepareConstructionConfiguration();
        _building.Update();
        Assert.IsFalse(_entityManager.Exists(site));
        Assert.IsTrue(_entityManager.Exists(item));
        Assert.AreEqual(holder, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
    }

    [Test]
    public void ExistingWorldItemMarkedForDestroy_DoesNotBlockCompletion()
    {
        Entity site = CreateSite(int2.zero, new int2(1, 1), flags: ConstructionSiteFlags.AwaitingItemClearance);
        Entity item = CreateItem(int2.zero);
        _entityManager.SetComponentEnabled<DestroyItemRequest>(item, true);

        Entities.PrepareConstructionConfiguration();
        _building.Update();

        Assert.IsFalse(_entityManager.Exists(item));
        Assert.IsFalse(_entityManager.Exists(site));
    }

    [Test]
    public void WorldSpawnInsideConstructionSite_IsRejectedAndDoesNotBlockCompletion()
    {
        Entity site = CreateSite(int2.zero, new int2(1, 1));
        Entity request = _entityManager.CreateEntity(typeof(SpawnItemRequest));
        _entityManager.SetComponentData(request, new SpawnItemRequest(ItemTypeEnum.Iron, int2.zero));
        Entities.PrepareConstructionConfiguration();
        _building.Update();
        Assert.IsFalse(_entityManager.Exists(site), "거부한 World Spawn은 완공을 막지 않는다.");
        Assert.IsFalse(_entityManager.Exists(request));
        using var items = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<ItemIdentity>(),
            ComponentType.ReadOnly<ItemOwnership>(), ComponentType.ReadOnly<GridPosition>());
        Assert.AreEqual(0, items.CalculateEntityCount());
    }

    [TestCase(DirectionEnum.Up, 6, 5, false)]
    [TestCase(DirectionEnum.Up, 7, 5, true)]
    [TestCase(DirectionEnum.Right, 5, 6, false)]
    [TestCase(DirectionEnum.Right, 6, 5, true)]
    [TestCase(DirectionEnum.Right, 5, 7, true)]
    public void WorldSpawn_UsesRotatedSiteFootprintAndAllowsOnlyOutsideCells(
        DirectionEnum direction, int x, int y, bool allowed)
    {
        Entity site = CreateSite(new int2(5, 5), new int2(2, 1), direction, required: 1);
        Entity request = _entityManager.CreateEntity(typeof(SpawnItemRequest));
        _entityManager.SetComponentData(request, new SpawnItemRequest(ItemTypeEnum.Iron, new int2(x, y)));

        Entities.PrepareConstructionConfiguration();
        _building.Update();

        Assert.IsFalse(_entityManager.Exists(request));
        Assert.IsTrue(_entityManager.Exists(site));
        Assert.IsFalse(IsBlocked(site));
        using var items = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<ItemIdentity>(),
            ComponentType.ReadOnly<ItemOwnership>(), ComponentType.ReadOnly<GridPosition>());
        Assert.AreEqual(allowed ? 1 : 0, items.CalculateEntityCount());
        if (allowed)
        {
            Entity item = items.GetSingletonEntity();
            Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(item).IsWorldItem);
            Assert.AreEqual(new int2(x, y), _entityManager.GetComponentData<GridPosition>(item).Value);
        }
    }

    [Test]
    public void DemolitionReturnAtANonOverlappingBuilding_DoesNotBlockAnotherSitesCompletion()
    {
        Entity site = CreateSite(int2.zero, new int2(1, 1));
        Entity building = _entityManager.CreateEntity(typeof(BuildingType), typeof(GridPosition),
            typeof(PendingBuildingDemolition), typeof(StoredItemElement));
        _entityManager.SetComponentData(building, new BuildingType(BuildingTypeEnum.Storage));
        _entityManager.SetComponentData(building, new GridPosition(new int2(3, 0)));
        Entity item = CreateItem(new int2(9, 9), building);
        _entityManager.GetBuffer<StoredItemElement>(building).Add(new StoredItemElement(item, ItemTypeEnum.Iron, 0));
        Entities.PrepareConstructionConfiguration();
        _building.Update();
        Assert.IsFalse(_entityManager.Exists(building));
        Assert.IsFalse(_entityManager.Exists(site));
        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(item).IsWorldItem);
        Assert.AreEqual(new int2(3, 0), _entityManager.GetComponentData<GridPosition>(item).Value);
        Assert.IsFalse(_entityManager.HasComponent<DisableRendering>(item));
    }

    [Test]
    public void OutsideWorldSpawn_DoesNotBlockLaterSiteCompletionElsewhere()
    {
        Entity request = _entityManager.CreateEntity(typeof(SpawnItemRequest));
        _entityManager.SetComponentData(request, new SpawnItemRequest(ItemTypeEnum.Iron, new int2(10, 10)));
        Entities.PrepareConstructionConfiguration();
        _building.Update();
        Entity site = CreateSite(int2.zero, new int2(1, 1), flags: ConstructionSiteFlags.AwaitingItemClearance);
        Entities.PrepareConstructionConfiguration();
        _building.Update();
        Assert.IsFalse(_entityManager.Exists(site));
        using var items = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<ItemIdentity>(),
            ComponentType.ReadOnly<ItemOwnership>(), ComponentType.ReadOnly<GridPosition>());
        Assert.AreEqual(1, items.CalculateEntityCount());
        Assert.AreEqual(new int2(10, 10), _entityManager.GetComponentData<GridPosition>(items.GetSingletonEntity()).Value);
    }

    private Entity CreateSite(int2 position, int2 size, DirectionEnum direction = DirectionEnum.Up,
        int required = 0, ConstructionSiteFlags flags = ConstructionSiteFlags.None)
    {
        Entity site = _entityManager.CreateEntity(typeof(ConstructionSite), typeof(BuildingType),
            typeof(GridPosition), typeof(BuildingFootprint), typeof(Direction), typeof(PlacementStamp));
        _entityManager.SetComponentData(site, new ConstructionSite(BuildingTypeEnum.Storage, flags));
        _entityManager.SetComponentData(site, new BuildingType(BuildingTypeEnum.ConstructionSite));
        _entityManager.SetComponentData(site, new GridPosition(position));
        _entityManager.SetComponentData(site, new BuildingFootprint(size));
        _entityManager.SetComponentData(site, new Direction(direction));
        _entityManager.AddBuffer<StoredItemElement>(site);
        var rows = _entityManager.AddBuffer<ConstructionMaterialRequirementElement>(site);
        if (required > 0) rows.Add(new ConstructionMaterialRequirementElement(ItemTypeEnum.Iron, required));
        return site;
    }

    private Entity CreateItem(int2 position, Entity owner = default)
    {
        Entity item = _entityManager.CreateEntity(typeof(ItemIdentity), typeof(ItemOwnership), typeof(GridPosition),
            typeof(LocalTransform), typeof(TransferOwnershipRequest), typeof(DestroyItemRequest));
        _entityManager.SetComponentData(item, new ItemIdentity(ItemTypeEnum.Iron));
        _entityManager.SetComponentData(item, new ItemOwnership(owner));
        _entityManager.SetComponentData(item, new GridPosition(position));
        _entityManager.SetComponentData(item, LocalTransform.FromPosition(new float3(position.x, position.y, 0)));
        _entityManager.SetComponentEnabled<TransferOwnershipRequest>(item, false);
        _entityManager.SetComponentEnabled<DestroyItemRequest>(item, false);
        if (owner != Entity.Null) _entityManager.AddComponent<DisableRendering>(item);
        return item;
    }

    private bool IsBlocked(Entity site)
    {
        return (_entityManager.GetComponentData<ConstructionSite>(site).Flags & ConstructionSiteFlags.AwaitingItemClearance) != 0;
    }

}
