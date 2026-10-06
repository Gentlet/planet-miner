using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>
/// 역할·목적: 현장 취소의 기존 자재 반환·무효/중복 소비에 대한 NUnit EditMode 회귀 검증.
/// 입력·검사: 도착 실물/수량과 요청을 직접 준비해 Command/EndCommand의 Owner/위치·현장 삭제를 검사한다. 자재 운송을 실행하지 않는다.
/// 수명: EcsWorldTestFixture가 각 사례의 독립 World를 준비하고 종료 시 해제한다.
/// </summary>
public class Phase7ConstructionCancelTests : EcsWorldTestFixture
{
    private SystemHandle _cancelCommandSystem;
    private SystemHandle _lifecycleApplySystem;
    private EndCommandEntityCommandBufferSystem _endCommandEcb;
    private EndStateApplyEntityCommandBufferSystem _endStateApplyEcb;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        CreateGameplayPrefabDatabases();
        _cancelCommandSystem = _world.GetOrCreateSystem<ConstructionCancelCommandSystem>();
        _lifecycleApplySystem = _world.GetOrCreateSystem<ConstructionLifecycleApplySystem>();
        _endCommandEcb = _world.GetOrCreateSystemManaged<EndCommandEntityCommandBufferSystem>();
        _endStateApplyEcb = _world.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>();
    }

    private void RunCancelPhase()
    {
        // 취소는 Command/EndCommand에서 반환·삭제한다. StateApply 완공을 기다리지 않는다.
        Simulation.UpdateAndComplete(_cancelCommandSystem);
        Simulation.Playback(_endCommandEcb);
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
        _entityManager.AddComponentData(item, new DisableRendering());
        _entityManager.AddComponentData(item, new GridPosition(int2.zero));
        _entityManager.AddComponentData(item, LocalTransform.Identity);

        var buffer = _entityManager.GetBuffer<StoredItemElement>(site);
        buffer.Add(new StoredItemElement(item, type, 0));
        return item;
    }

    private Entity RequestCancel(Entity site)
    {
        var request = _entityManager.CreateEntity();
        _entityManager.AddComponentData(request, new CancelConstructionRequest(site));
        return request;
    }

    [Test]
    public void Test03_CancelConstruction_FullySatisfiedSite_BeforeCompletion_ReturnsMaterialsAndCancels()
    {
        // 1. 자재가 100% 충족된 공사 현장
        int2 sitePos = new int2(5, 5);
        var site = CreateSite(BuildingTypeEnum.Storage, sitePos);
        var reqBuffer = _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site);
        reqBuffer.Add(new ConstructionMaterialRequirementElement(ItemTypeEnum.Iron, 2, deliveredQuantity: 2));

        var item1 = StoreMaterialItem(site, ItemTypeEnum.Iron);
        var item2 = StoreMaterialItem(site, ItemTypeEnum.Iron);

        // 2. Command에서 취소를 기록하고 EndCommand에서 반환과 삭제를 확정한다.
        Entity cancelRequest = RequestCancel(site);
        Simulation.UpdateAndComplete(_cancelCommandSystem);
        Assert.IsTrue(_entityManager.Exists(site));
        Assert.IsTrue(_entityManager.Exists(cancelRequest));
        Assert.AreEqual(ItemOwnership.Stored(site), _entityManager.GetComponentData<ItemOwnership>(item1));
        Simulation.Playback(_endCommandEcb);

        // 3. 완공되지 않고 취소 및 자재 반환 검증
        Assert.IsFalse(_entityManager.Exists(site), "공사 현장은 파괴되어야 함");
        Assert.IsFalse(_entityManager.Exists(cancelRequest));
        Assert.IsTrue(_entityManager.Exists(item1), "자재 1은 소비되지 않고 월드에 남아있어야 함");
        Assert.IsTrue(_entityManager.Exists(item2), "자재 2는 소비되지 않고 월드에 남아있어야 함");
        foreach (Entity item in new[] { item1, item2 })
        {
            Assert.AreEqual(ItemOwnership.WorldItem, _entityManager.GetComponentData<ItemOwnership>(item));
            Assert.AreEqual(sitePos, _entityManager.GetComponentData<GridPosition>(item).Value);
            Assert.AreEqual(new float3(sitePos.x, sitePos.y, 0f), _entityManager.GetComponentData<LocalTransform>(item).Position);
            Assert.IsFalse(_entityManager.HasComponent<DisableRendering>(item));
        }

        // StateApply 완공 검사까지 실행해도 취소 현장의 건물/자재 소비가 발생하지 않는다.
        Simulation.UpdateAndComplete(_lifecycleApplySystem);
        Simulation.Playback(_endStateApplyEcb);
        Assert.IsTrue(_entityManager.Exists(item1));
        Assert.IsTrue(_entityManager.Exists(item2));

        var storageQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<Storage>());
        Assert.AreEqual(0, storageQuery.CalculateEntityCount(), "완공 건물이 생성되어서는 안 됨");
        storageQuery.Dispose();
    }

    [Test]
    public void Test05_DuplicateCancelRequests_SameFrame_SafeAndIdempotent()
    {
        // 1. 공사 현장 1개에 대해 동일 프레임에 2개의 취소 요청 발행
        var site = CreateSite(BuildingTypeEnum.Miner, new int2(1, 1));
        var item = StoreMaterialItem(site, ItemTypeEnum.Iron);

        var req1 = RequestCancel(site);
        var req2 = RequestCancel(site);

        RunCancelPhase();

        // 2. 두 요청 모두 안전하게 소비되고 예외 없이 현장 파괴 및 자재 반환 검증
        Assert.IsFalse(_entityManager.Exists(req1));
        Assert.IsFalse(_entityManager.Exists(req2));
        Assert.IsFalse(_entityManager.Exists(site));
        Assert.IsTrue(_entityManager.Exists(item));
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
    }

    [Test]
    public void Test06_CancelNonExistentOrAlreadyCompletedBuilding_StrictRejection()
    {
        // 1. 완공된 건물 엔티티 (ConstructionSite 컴포넌트가 없음)
        var building = _entityManager.CreateEntity();
        _entityManager.AddComponentData(building, new BuildingType(BuildingTypeEnum.Miner));
        _entityManager.AddComponentData(building, new GridPosition(new int2(2, 2)));

        // 2. 완공 건물에 대해 취소 요청 발행
        var reqEntity = RequestCancel(building);

        RunCancelPhase();

        // 3. 완공 건물은 파괴되지 않고 보호되며, 취소 요청만 안전하게 소비(Idempotent Drop)됨 확인
        Assert.IsFalse(_entityManager.Exists(reqEntity));
        Assert.IsTrue(_entityManager.Exists(building), "완공된 건물은 취소 요청으로 파괴되어서는 안 됨");
    }

}
