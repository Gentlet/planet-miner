using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Rendering;

/// <summary>
/// Task 7.4 공사 자재 요구량 및 수령 계약 단위/통합 테스트.
/// </summary>
public class Phase7ConstructionMaterialTests : EcsWorldTestFixture
{
    private SystemHandle _lifecycleApplySystem;
    private EndStateApplyEntityCommandBufferSystem _ecbSystem;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        CreateGameplayPrefabDatabases();
        _lifecycleApplySystem = _world.GetOrCreateSystem<ConstructionLifecycleApplySystem>();
        _ecbSystem = _world.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>();
    }

    private void RunMaterialApplyPhase()
    {
        Simulation.UpdateAndComplete(_lifecycleApplySystem);
        Simulation.Playback(_ecbSystem);
    }

    private Entity CreateSite(BuildingTypeEnum type, int required, int delivered = 0, int reserved = 0)
    {
        var site = _entityManager.CreateEntity();
        _entityManager.AddComponentData(site, new ConstructionSite(type, required == delivered ? 1.0f : 0.0f));
        var requirements = _entityManager.AddBuffer<ConstructionMaterialRequirementElement>(site);
        requirements.Add(new ConstructionMaterialRequirementElement(ItemTypeEnum.Iron, required, delivered, reserved));
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

    private Entity RequestSupply(Entity site, Entity item, ItemTypeEnum type)
    {
        var request = _entityManager.CreateEntity();
        _entityManager.AddComponentData(request, new SupplyConstructionMaterialRequest(site, item, type));
        return request;
    }

    [Test]
    public void Test03_SupplyMaterial_WithExistingReservation_DecrementsReservedQuantity()
    {
        // 1. 운송 예약이 2개 걸려 있는 현장 준비 (요구 3, 조달 0, 예약 2)
        var siteEntity = CreateSite(BuildingTypeEnum.Storage, required: 3, reserved: 2);

        // 2. Iron 아이템 전달
        var itemEntity = CreateWorldItem(ItemTypeEnum.Iron);

        RequestSupply(siteEntity, itemEntity, ItemTypeEnum.Iron);

        RunMaterialApplyPhase();

        // 3. 검증: 조달량 1 증가, 예약량 1 감소 (2 -> 1)
        var updatedReqBuffer = _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(siteEntity);
        Assert.AreEqual(1, updatedReqBuffer[0].DeliveredQuantity);
        Assert.AreEqual(1, updatedReqBuffer[0].ReservedQuantity, "도착 완료된 자재는 예약량에서 차감되어야 함");
        Assert.AreEqual(1, updatedReqBuffer[0].RemainingToReserve, "추가 예약 가능량은 3 - (1 + 1) = 1이어야 함");
    }

    [Test]
    public void Test04_SupplyMaterial_WrongItemType_StrictRejection()
    {
        // 1. 현장은 Iron만 요구함
        var siteEntity = CreateSite(BuildingTypeEnum.Miner, required: 2);

        // 2. Copper 아이템을 전달 시도
        var itemEntity = CreateWorldItem(ItemTypeEnum.Copper);

        var reqEntity = RequestSupply(siteEntity, itemEntity, ItemTypeEnum.Copper);

        RunMaterialApplyPhase();

        // 3. 검증: 수령 거부 (현장 조달량 변화 없음, 아이템 소유권 월드 유지, 요청 엔티티는 파괴)
        Assert.IsFalse(_entityManager.Exists(reqEntity));
        var updatedReqBuffer = _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(siteEntity);
        Assert.AreEqual(0, updatedReqBuffer[0].DeliveredQuantity, "잘못된 품목은 수령되지 않아야 함");

        var ownership = _entityManager.GetComponentData<ItemOwnership>(itemEntity);
        Assert.IsTrue(ownership.IsWorldItem, "수령 거부된 아이템은 원래 소유권을 유지해야 함");
        Assert.IsFalse(_entityManager.HasComponent<DisableRendering>(itemEntity));

        var storedBuffer = _entityManager.GetBuffer<StoredItemElement>(siteEntity);
        Assert.AreEqual(0, storedBuffer.Length, "현장 보관 버퍼에 등록되지 않아야 함");
    }

    [Test]
    public void Test05_SupplyMaterial_OverDelivery_StrictRejection()
    {
        // 1. 이미 요구 수량이 100% 충족된 현장 (요구 1, 조달 1)
        var siteEntity = CreateSite(BuildingTypeEnum.Storage, required: 1, delivered: 1);

        // 2. 추가 Iron 전달 시도 (초과 전달)
        var itemEntity = CreateWorldItem(ItemTypeEnum.Iron);

        RequestSupply(siteEntity, itemEntity, ItemTypeEnum.Iron);

        RunMaterialApplyPhase();

        // 3. 검증: 초과 전달 거부
        var updatedReqBuffer = _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(siteEntity);
        Assert.AreEqual(1, updatedReqBuffer[0].DeliveredQuantity, "초과 전달 시 조달량이 증가하지 않아야 함");

        var ownership = _entityManager.GetComponentData<ItemOwnership>(itemEntity);
        Assert.IsTrue(ownership.IsWorldItem, "거부된 아이템은 원래 소유권을 유지해야 함");
    }

    [Test]
    public void Test06_SupplyMaterial_DeadSite_StrictRejection()
    {
        // 1. 존재하지 않는 임의의 엔티티를 현장으로 지정
        var deadSite = _entityManager.CreateEntity();
        _entityManager.DestroyEntity(deadSite); // 파괴됨

        var itemEntity = CreateWorldItem(ItemTypeEnum.Iron);

        var reqEntity = RequestSupply(deadSite, itemEntity, ItemTypeEnum.Iron);

        RunMaterialApplyPhase();

        // 2. 검증: 안전하게 거부 및 요청 엔티티 파괴
        Assert.IsFalse(_entityManager.Exists(reqEntity));
        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(itemEntity).IsWorldItem);
    }

    [Test]
    public void Test08_SupplyMaterial_MultipleSupplies_ExceedingBatch_AcceptsUntilFullAndRejectsRemainder()
    {
        // 1. 현장 준비: Iron 2개만 요구
        var siteEntity = CreateSite(BuildingTypeEnum.Crafter, required: 2);

        // 2. 아이템 3개 동시 전달 시도 (1개 초과)
        var item1 = CreateWorldItem(ItemTypeEnum.Iron);
        var item2 = CreateWorldItem(ItemTypeEnum.Iron);
        var item3 = CreateWorldItem(ItemTypeEnum.Iron);

        RequestSupply(siteEntity, item1, ItemTypeEnum.Iron);
        RequestSupply(siteEntity, item2, ItemTypeEnum.Iron);
        RequestSupply(siteEntity, item3, ItemTypeEnum.Iron);

        RunMaterialApplyPhase();

        // 3. 검증: 정확히 2개만 수령되고, 3번째는 초과 거부됨
        var updatedReqBuffer = _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(siteEntity);
        Assert.AreEqual(2, updatedReqBuffer[0].DeliveredQuantity, "최대 요구량인 2개까지만 수령되어야 함");

        var storedBuffer = _entityManager.GetBuffer<StoredItemElement>(siteEntity);
        Assert.AreEqual(2, storedBuffer.Length, "현장 보관함에는 2개만 등록되어야 함");

        // item1, item2는 수납, item3은 월드 유지
        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(item1).IsStored);
        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(item2).IsStored);
        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(item3).IsWorldItem, "초과된 3번째 아이템은 월드에 유지되어야 함");
    }
}
