using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>
/// Task 7.6 공사 취소 및 도착 자재 반환 단위/통합 테스트.
/// </summary>
public class Phase7ConstructionCancelTests : EcsWorldTestFixture
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

    private void RunCancelPhase()
    {
        Simulation.UpdateAndComplete(_lifecycleApplySystem);
        Simulation.Playback(_ecbSystem);
    }

    private void RunMaterialApplyPhase()
    {
        Simulation.UpdateAndComplete(_lifecycleApplySystem);
        Simulation.Playback(_ecbSystem);
    }

    private void RunCancelAndCompletionPhase()
    {
        Simulation.UpdateAndComplete(_lifecycleApplySystem);
        Simulation.Playback(_ecbSystem);
    }

    private void RunFullPipelinePhase()
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
        _entityManager.AddComponentData(item, new GridPosition(int2.zero));
        _entityManager.AddComponentData(item, LocalTransform.Identity);
        return item;
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

    private Entity RequestSupply(Entity site, Entity item, ItemTypeEnum type)
    {
        var request = _entityManager.CreateEntity();
        _entityManager.AddComponentData(request, new SupplyConstructionMaterialRequest(site, item, type));
        return request;
    }

    [Test]
    public void Test01_CancelConstruction_EmptySite_DestroysSiteAndConsumesRequest()
    {
        // 1. 자재가 없는 빈 공사 현장 생성
        var site = CreateSite(BuildingTypeEnum.Miner, new int2(3, 4));
        var reqEntity = RequestCancel(site);

        Assert.IsTrue(_entityManager.Exists(site));
        Assert.IsTrue(_entityManager.Exists(reqEntity));

        // 2. 취소 Phase 실행
        RunCancelPhase();

        // 3. 현장 및 요청 엔티티 파괴 검증
        Assert.IsFalse(_entityManager.Exists(reqEntity), "취소 요청은 단일 프레임 내에 소비되어야 함");
        Assert.IsFalse(_entityManager.Exists(site), "취소된 공사 현장은 파괴되어야 함");
    }

    [Test]
    public void Test02_CancelConstruction_WithMaterials_EjectsMaterialsAsWorldItems()
    {
        // 1. 자재가 보관된 현장 생성
        int2 sitePos = new int2(10, 20);
        var site = CreateSite(BuildingTypeEnum.Crafter, sitePos, DirectionEnum.Right, new int2(2, 2));
        var item1 = StoreMaterialItem(site, ItemTypeEnum.Iron);
        var item2 = StoreMaterialItem(site, ItemTypeEnum.Copper);

        Assert.IsTrue(_entityManager.HasComponent<DisableRendering>(item1));
        Assert.IsTrue(_entityManager.HasComponent<DisableRendering>(item2));

        // 2. 취소 요청 발행 및 실행
        var reqEntity = RequestCancel(site);
        RunCancelPhase();

        // 3. 현장 파괴 확인
        Assert.IsFalse(_entityManager.Exists(reqEntity));
        Assert.IsFalse(_entityManager.Exists(site));

        // 4. 자재 소유권 및 렌더링/위치 복구 검증
        Assert.IsTrue(_entityManager.Exists(item1));
        Assert.IsTrue(_entityManager.Exists(item2));

        var ownership1 = _entityManager.GetComponentData<ItemOwnership>(item1);
        var ownership2 = _entityManager.GetComponentData<ItemOwnership>(item2);
        Assert.AreEqual(Entity.Null, ownership1.Owner, "아이템 1의 소유권은 WorldItem이어야 함");
        Assert.AreEqual(Entity.Null, ownership2.Owner, "아이템 2의 소유권은 WorldItem이어야 함");

        Assert.IsFalse(_entityManager.HasComponent<DisableRendering>(item1), "아이템 1의 DisableRendering은 제거되어야 함");
        Assert.IsFalse(_entityManager.HasComponent<DisableRendering>(item2), "아이템 2의 DisableRendering은 제거되어야 함");

        var pos1 = _entityManager.GetComponentData<GridPosition>(item1);
        var pos2 = _entityManager.GetComponentData<GridPosition>(item2);
        Assert.AreEqual(sitePos, pos1.Value, "아이템 1의 그리드 위치는 현장 좌표와 일치해야 함");
        Assert.AreEqual(sitePos, pos2.Value, "아이템 2의 그리드 위치는 현장 좌표와 일치해야 함");

        var transform1 = _entityManager.GetComponentData<LocalTransform>(item1);
        Assert.AreEqual(new float3(sitePos.x, sitePos.y, 0f), transform1.Position);
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

        // 2. 취소 요청 발행 및 [취소 + 완공] 동시 평가 파이프라인 실행
        RequestCancel(site);
        RunCancelAndCompletionPhase();

        // 3. 완공되지 않고 취소 및 자재 반환 검증
        Assert.IsFalse(_entityManager.Exists(site), "공사 현장은 파괴되어야 함");
        Assert.IsTrue(_entityManager.Exists(item1), "자재 1은 소비되지 않고 월드에 남아있어야 함");
        Assert.IsTrue(_entityManager.Exists(item2), "자재 2는 소비되지 않고 월드에 남아있어야 함");

        var storageQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<Storage>());
        Assert.AreEqual(0, storageQuery.CalculateEntityCount(), "완공 건물이 생성되어서는 안 됨");
        storageQuery.Dispose();
    }

    [Test]
    public void Test04_RaceCondition_SupplyAndCancelAndComplete_SameFrame_CancelWins()
    {
        // 1. Belt 현장: 자재 1개 요구, 0개 도착 상태
        int2 pos = new int2(8, 8);
        var site = CreateSite(BuildingTypeEnum.Belt, pos);
        var reqBuffer = _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site);
        reqBuffer.Add(new ConstructionMaterialRequirementElement(ItemTypeEnum.Iron, 1, deliveredQuantity: 0));

        // 2. 동일 프레임에 마지막 자재 공급 요청과 취소 요청 동시 발행
        var item = CreateWorldItem(ItemTypeEnum.Iron);
        var supplyReq = RequestSupply(site, item, ItemTypeEnum.Iron);
        var cancelReq = RequestCancel(site);

        // 3. Full Pipeline 1회 실행:
        // MaterialApply (자재 수령 버퍼 추가) -> CancelApply (취소 플래그 마킹, 자재 반환, 현장 파괴) -> CompletionApply (취소 감지하여 완공 스킵)
        RunFullPipelinePhase();

        // 4. 취소 우선(Cancel Wins) 검증
        Assert.IsFalse(_entityManager.Exists(supplyReq));
        Assert.IsFalse(_entityManager.Exists(cancelReq));
        Assert.IsFalse(_entityManager.Exists(site), "현장은 취소되어 파괴되어야 함");

        // 방금 도착했던 자재도 소비/파괴되지 않고 월드 아이템으로 반환되어야 함
        Assert.IsTrue(_entityManager.Exists(item), "공급된 자재는 파괴되지 않고 월드로 반환되어야 함");
        var ownership = _entityManager.GetComponentData<ItemOwnership>(item);
        Assert.AreEqual(Entity.Null, ownership.Owner, "자재 소유권은 WorldItem이어야 함");
        Assert.IsFalse(_entityManager.HasComponent<DisableRendering>(item), "렌더링이 활성화되어야 함");

        var beltQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<BeltComponent>());
        Assert.AreEqual(0, beltQuery.CalculateEntityCount(), "완공 건물이 생성되어서는 안 됨");
        beltQuery.Dispose();
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

    [Test]
    public void Test07_SpatialIndex_ReleaseOnCancel()
    {
        // 1. 공사 현장 생성 후 공간 인덱스 동기화
        int2 pos = new int2(15, 15);
        var site = CreateSite(BuildingTypeEnum.Storage, pos, DirectionEnum.Up, new int2(1, 1));
        Simulation.UpdateAndComplete(_spatialSyncSystem);

        var index = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<BuildingSpatialIndex>())
            .GetSingleton<BuildingSpatialIndex>();

        Assert.IsTrue(index.Map.TryGetValue(pos, out var siteInfo));
        Assert.AreEqual(BuildingTypeEnum.ConstructionSite, siteInfo.Type);

        // 2. 현장 취소 및 공간 인덱스 재동기화
        RequestCancel(site);
        RunCancelPhase();
        Simulation.UpdateAndComplete(_spatialSyncSystem);

        // 3. 공간 인덱스에서 점유가 완전히 해제되었는지 검증
        Assert.IsFalse(index.Map.TryGetValue(pos, out _), "취소된 공사 현장은 공간 인덱스에서 완전히 제거되어야 함");
    }
}
