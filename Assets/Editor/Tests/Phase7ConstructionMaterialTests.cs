using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Rendering;

/// <summary>
/// Task 7.4 공사 자재 요구량 및 수령 계약 단위/통합 테스트.
/// </summary>
public class Phase7ConstructionMaterialTests : EcsWorldTestFixture
{
    private SystemHandle _materialApplySystem;
    private EndStateApplyEntityCommandBufferSystem _ecbSystem;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        _materialApplySystem = _world.GetOrCreateSystem<ConstructionMaterialApplySystem>();
        _ecbSystem = _world.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>();
    }

    private void RunMaterialApplyPhase()
    {
        Simulation.UpdateAndComplete(_materialApplySystem);
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
    public void Test01_ConstructionMaterialRequirement_PureProperties()
    {
        // 1. 초기 상태: 요구 5, 조달 2, 예약 1
        var elem = new ConstructionMaterialRequirementElement(ItemTypeEnum.Iron, 5, 2, 1);
        Assert.AreEqual(3, elem.RemainingRequired, "잔여 요구량은 5 - 2 = 3이어야 함");
        Assert.AreEqual(2, elem.RemainingToReserve, "추가 예약 가능량은 5 - (2 + 1) = 2여야 함");
        Assert.IsFalse(elem.IsSatisfied, "아직 미충족이어야 함");
        Assert.IsFalse(elem.IsFullyReserved, "아직 예약 미완료여야 함");

        // 2. 예약 충족: 요구 5, 조달 2, 예약 3
        elem.ReservedQuantity = 3;
        Assert.AreEqual(0, elem.RemainingToReserve);
        Assert.IsTrue(elem.IsFullyReserved, "조달+예약이 요구량과 같으면 IsFullyReserved == true");
        Assert.IsFalse(elem.IsSatisfied);

        // 3. 완공 충족: 요구 5, 조달 5
        elem.DeliveredQuantity = 5;
        elem.ReservedQuantity = 0;
        Assert.AreEqual(0, elem.RemainingRequired);
        Assert.AreEqual(0, elem.RemainingToReserve);
        Assert.IsTrue(elem.IsSatisfied, "조달량이 요구량 이상이면 IsSatisfied == true");
        Assert.IsTrue(elem.IsFullyReserved);
    }

    [Test]
    public void Test02_SupplyMaterial_SingleValid_IncreasesDeliveredAndTransfersOwnership()
    {
        // 1. 공사 현장 생성: Miner, Iron 2개 요구
        var siteEntity = CreateSite(BuildingTypeEnum.Miner, required: 2);

        // 2. 월드 아이템 생성: Iron
        var itemEntity = CreateWorldItem(ItemTypeEnum.Iron);

        // 3. 자재 전달 요청 발행
        var reqEntity = RequestSupply(siteEntity, itemEntity, ItemTypeEnum.Iron);

        RunMaterialApplyPhase();

        // 4. 요청 소비 확인 (Consume-on-Apply)
        Assert.IsFalse(_entityManager.Exists(reqEntity), "자재 공급 요청 엔티티는 단일 프레임 내에 파괴되어야 함");

        // 5. 현장 자재 수량 및 진행도 검증
        var updatedReqBuffer = _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(siteEntity);
        Assert.AreEqual(1, updatedReqBuffer[0].DeliveredQuantity, "DeliveredQuantity가 1 증가해야 함");
        Assert.AreEqual(1, updatedReqBuffer[0].RemainingRequired, "잔여 요구량은 1이어야 함");

        var site = _entityManager.GetComponentData<ConstructionSite>(siteEntity);
        Assert.AreEqual(0.5f, site.Progress, 0.001f, "진행도는 1 / 2 = 0.5f가 되어야 함");

        // 6. 아이템 엔티티 소유권 이전 및 렌더링 제외 검증
        var ownership = _entityManager.GetComponentData<ItemOwnership>(itemEntity);
        Assert.IsTrue(ownership.IsStored);
        Assert.AreEqual(siteEntity, ownership.Owner, "아이템 소유자가 현장 엔티티로 이전되어야 함");
        Assert.IsTrue(_entityManager.HasComponent<DisableRendering>(itemEntity), "수납된 자재는 DisableRendering이 부착되어야 함");

        // 7. 현장 StoredItemElement 버퍼 등록 검증
        var storedBuffer = _entityManager.GetBuffer<StoredItemElement>(siteEntity);
        Assert.AreEqual(1, storedBuffer.Length, "현장 보관 버퍼에 자재 아이템이 보관되어야 함");
        Assert.AreEqual(itemEntity, storedBuffer[0].ItemEntity);
        Assert.AreEqual(ItemTypeEnum.Iron, storedBuffer[0].ItemType);
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
    public void Test07_SupplyMaterial_MultipleSupplies_SameFrame_AccumulatesCorrectly()
    {
        // 1. 현장 준비: Iron 3개 요구
        var siteEntity = CreateSite(BuildingTypeEnum.Crafter, required: 3);

        // 2. 같은 프레임에 아이템 3개 동시 전달 요청 발행
        var item1 = CreateWorldItem(ItemTypeEnum.Iron);
        var item2 = CreateWorldItem(ItemTypeEnum.Iron);
        var item3 = CreateWorldItem(ItemTypeEnum.Iron);

        var req1 = RequestSupply(siteEntity, item1, ItemTypeEnum.Iron);
        var req2 = RequestSupply(siteEntity, item2, ItemTypeEnum.Iron);
        var req3 = RequestSupply(siteEntity, item3, ItemTypeEnum.Iron);

        RunMaterialApplyPhase();

        // 3. 검증: 동일 프레임 다중 자재가 1개씩 안전하게 누적되어 3개 모두 수령됨
        Assert.IsFalse(_entityManager.Exists(req1));
        Assert.IsFalse(_entityManager.Exists(req2));
        Assert.IsFalse(_entityManager.Exists(req3));

        var updatedReqBuffer = _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(siteEntity);
        Assert.AreEqual(3, updatedReqBuffer[0].DeliveredQuantity);
        Assert.IsTrue(updatedReqBuffer[0].IsSatisfied);

        var site = _entityManager.GetComponentData<ConstructionSite>(siteEntity);
        Assert.AreEqual(1.0f, site.Progress, 0.001f);

        var storedBuffer = _entityManager.GetBuffer<StoredItemElement>(siteEntity);
        Assert.AreEqual(3, storedBuffer.Length, "3개 아이템 모두 현장에 보관되어야 함");
        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(item1).IsStored);
        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(item2).IsStored);
        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(item3).IsStored);
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
