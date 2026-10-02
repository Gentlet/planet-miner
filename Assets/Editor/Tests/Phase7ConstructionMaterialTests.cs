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

    private Entity RequestSupply(Entity site, Entity source, Entity item, ItemTypeEnum type)
    {
        Entity delivery = Entities.CreateConstructionMaterialDelivery(site, source, item, type);
        var request = _entityManager.CreateEntity();
        _entityManager.AddComponentData(request, new SupplyConstructionMaterialRequest(delivery, source));
        return request;
    }

    [Test]
    public void Test03_SupplyMaterial_WithExistingReservation_DecrementsReservedQuantity()
    {
        // 1. 운송 예약이 2개 걸려 있는 현장 준비 (요구 3, 조달 0, 예약 2)
        var siteEntity = CreateSite(BuildingTypeEnum.Storage, required: 3);

        // 2. Iron 아이템 전달
        Entity source = Entities.CreateConstructionMaterialSource();
        Entity itemEntity = Entities.CreateStoredConstructionMaterial(source, ItemTypeEnum.Iron);
        Entity reservedItem = Entities.CreateStoredConstructionMaterial(source, ItemTypeEnum.Iron);

        Entity delivery = Entities.CreateConstructionMaterialDelivery(siteEntity, source, itemEntity, ItemTypeEnum.Iron, true);
        Entities.CreateConstructionMaterialDelivery(siteEntity, source, reservedItem, ItemTypeEnum.Iron, true);
        Entity request = _entityManager.CreateEntity();
        _entityManager.AddComponentData(request, new SupplyConstructionMaterialRequest(
            delivery, source));

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
        Entity source = Entities.CreateConstructionMaterialSource();
        Entity itemEntity = Entities.CreateStoredConstructionMaterial(source, ItemTypeEnum.Copper);

        var reqEntity = RequestSupply(siteEntity, source, itemEntity, ItemTypeEnum.Copper);

        RunMaterialApplyPhase();

        // 3. 검증: 수령 거부 (집계 변화 없음, 공급원 소유권 유지, 요청 소비)
        Assert.IsFalse(_entityManager.Exists(reqEntity));
        var updatedReqBuffer = _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(siteEntity);
        Assert.AreEqual(0, updatedReqBuffer[0].DeliveredQuantity, "잘못된 품목은 수령되지 않아야 함");

        var ownership = _entityManager.GetComponentData<ItemOwnership>(itemEntity);
        Assert.AreEqual(source, ownership.Owner, "수령 거부된 아이템은 공급원의 소유권을 유지해야 함");
        Assert.IsTrue(_entityManager.HasComponent<DisableRendering>(itemEntity));

        var storedBuffer = _entityManager.GetBuffer<StoredItemElement>(siteEntity);
        Assert.AreEqual(0, storedBuffer.Length, "현장 보관 버퍼에 등록되지 않아야 함");
    }

    [Test]
    public void Test05_SupplyMaterial_OverDelivery_StrictRejection()
    {
        // 1. 이미 요구 수량이 100% 충족된 현장 (요구 1, 조달 1)
        var siteEntity = CreateSite(BuildingTypeEnum.Storage, required: 1, delivered: 1);

        // 2. 추가 Iron 전달 시도 (초과 전달)
        Entity source = Entities.CreateConstructionMaterialSource();
        Entity itemEntity = Entities.CreateStoredConstructionMaterial(source, ItemTypeEnum.Iron);

        RequestSupply(siteEntity, source, itemEntity, ItemTypeEnum.Iron);

        RunMaterialApplyPhase();

        // 3. 검증: 초과 전달 거부
        var updatedReqBuffer = _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(siteEntity);
        Assert.AreEqual(1, updatedReqBuffer[0].DeliveredQuantity, "초과 전달 시 조달량이 증가하지 않아야 함");

        var ownership = _entityManager.GetComponentData<ItemOwnership>(itemEntity);
        Assert.AreEqual(source, ownership.Owner, "거부된 아이템은 원래 소유권을 유지해야 함");
    }

    [Test]
    public void Test06_SupplyMaterial_DeadSite_StrictRejection()
    {
        // 1. 존재하지 않는 임의의 엔티티를 현장으로 지정
        var deadSite = _entityManager.CreateEntity();
        _entityManager.DestroyEntity(deadSite); // 파괴됨

        Entity source = Entities.CreateConstructionMaterialSource();
        Entity itemEntity = Entities.CreateStoredConstructionMaterial(source, ItemTypeEnum.Iron);

        var reqEntity = RequestSupply(deadSite, source, itemEntity, ItemTypeEnum.Iron);

        RunMaterialApplyPhase();

        // 2. 검증: 안전하게 거부 및 요청 엔티티 파괴
        Assert.IsFalse(_entityManager.Exists(reqEntity));
        Assert.AreEqual(source, _entityManager.GetComponentData<ItemOwnership>(itemEntity).Owner);
    }

    private Entity RequestDelivery(Entity delivery, Entity expectedOwner)
    {
        Entity request = _entityManager.CreateEntity();
        _entityManager.AddComponentData(request, new SupplyConstructionMaterialRequest(delivery, expectedOwner));
        return request;
    }

    private void CancelDelivery(Entity delivery)
    {
        Entity request = _entityManager.CreateEntity();
        _entityManager.AddComponentData(request, new CancelConstructionMaterialDeliveryRequest(delivery));
    }

    [TestCase(BuildingTypeEnum.Storage)]
    [TestCase(BuildingTypeEnum.DroneStation)]
    [TestCase(BuildingTypeEnum.MainFacility)]
    public void DuplicateRequestsAndCompetingSites_TransferOnePhysicalItemOnce(BuildingTypeEnum sourceType)
    {
        Entity firstSite = CreateSite(BuildingTypeEnum.Storage, 3);
        Entity secondSite = CreateSite(BuildingTypeEnum.Storage, 3);
        Entity config = _entityManager.CreateEntity(typeof(BuildingConfig));
        _entityManager.AddBuffer<BuildingConfigElement>(config).Add(new BuildingConfigElement(
            sourceType, 0, 0, true, default, new Unity.Mathematics.int2(1, 1)));
        Entity spawn = _entityManager.CreateEntity();
        _entityManager.AddComponentData(spawn, new SpawnBuildingRequest(
            sourceType, new Unity.Mathematics.int2(100, 100), DirectionEnum.Up, default, default));
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<BuildingLifecycleApplySystem>());
        Simulation.Playback(_ecbSystem);
        using var sourceQuery = _entityManager.CreateEntityQuery(typeof(BuildingType), typeof(Storage), typeof(GridPosition));
        Entity source = sourceQuery.GetSingletonEntity();
        Entity item = Entities.CreateStoredConstructionMaterial(source, ItemTypeEnum.Iron);
        Entity first = Entities.CreateConstructionMaterialDelivery(firstSite, source, item, ItemTypeEnum.Iron, true);
        Entity second = Entities.CreateConstructionMaterialDelivery(secondSite, source, item, ItemTypeEnum.Iron, true);
        Entity third = Entities.CreateConstructionMaterialDelivery(firstSite, source, item, ItemTypeEnum.Iron, true);
        RunMaterialApplyPhase();

        RequestDelivery(first, source);
        RequestDelivery(first, source);
        RequestDelivery(second, source);
        RequestDelivery(third, source);
        Simulation.UpdateAndComplete(_lifecycleApplySystem);
        Assert.AreEqual(source, _entityManager.GetComponentData<ItemOwnership>(item).Owner,
            "Owner 변경은 아직 ECB 반영 전이어야 한다.");
        Assert.AreEqual(ConstructionMaterialDeliveryOutcomeEnum.Registered,
            _entityManager.GetComponentData<ConstructionMaterialDeliveryResult>(first).Outcome,
            "외부 완료 결과를 Playback 전에 게시하지 않는다.");
        Simulation.Playback(_ecbSystem);

        Entity winner = _entityManager.GetComponentData<ItemOwnership>(item).Owner;
        Assert.That(winner == firstSite || winner == secondSite);
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(source).Length);
        Assert.AreEqual(1, _entityManager.GetBuffer<StoredItemElement>(firstSite).Length +
            _entityManager.GetBuffer<StoredItemElement>(secondSite).Length);
        Assert.AreEqual(1, _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(firstSite)[0].DeliveredQuantity +
            _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(secondSite)[0].DeliveredQuantity);
        Assert.AreEqual(0, _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(firstSite)[0].ReservedQuantity +
            _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(secondSite)[0].ReservedQuantity);
        Assert.IsTrue(_entityManager.HasComponent<DisableRendering>(item));

        RequestDelivery(first, source);
        RequestDelivery(second, source);
        RequestDelivery(third, source);
        RunMaterialApplyPhase();
        Assert.AreEqual(winner, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
        Assert.AreEqual(1, _entityManager.GetBuffer<StoredItemElement>(winner).Length);
    }

    [Test]
    public void UnreservedSupplyAndRejectedDelivery_DoNotSettleAnotherDeliveryReservation()
    {
        Entity site = CreateSite(BuildingTypeEnum.Storage, 3);
        Entity firstSource = Entities.CreateConstructionMaterialSource();
        Entity secondSource = Entities.CreateConstructionMaterialSource();
        Entity unreservedSource = Entities.CreateConstructionMaterialSource();
        Entity firstItem = Entities.CreateStoredConstructionMaterial(firstSource, ItemTypeEnum.Iron);
        Entity secondItem = Entities.CreateStoredConstructionMaterial(secondSource, ItemTypeEnum.Iron);
        Entity unreservedItem = Entities.CreateStoredConstructionMaterial(unreservedSource, ItemTypeEnum.Iron);
        Entity first = Entities.CreateConstructionMaterialDelivery(site, firstSource, firstItem, ItemTypeEnum.Iron, true);
        Entity second = Entities.CreateConstructionMaterialDelivery(site, secondSource, secondItem, ItemTypeEnum.Iron, true);
        Entity unreserved = Entities.CreateConstructionMaterialDelivery(site, unreservedSource, unreservedItem, ItemTypeEnum.Iron);
        RunMaterialApplyPhase();
        RequestDelivery(unreserved, unreservedSource);
        RunMaterialApplyPhase();
        Assert.AreEqual(2, _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site)[0].ReservedQuantity);

        RequestDelivery(first, Entity.Null); // 실제 Owner와 다른, 오래된 도착 정보.
        RequestDelivery(first, Entity.Null);
        RunMaterialApplyPhase();
        Assert.AreEqual(1, _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site)[0].ReservedQuantity);
        Assert.AreEqual(1, _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site)[0].DeliveredQuantity);
        Assert.AreEqual(firstSource, _entityManager.GetComponentData<ItemOwnership>(firstItem).Owner);
        Assert.AreEqual(1, _entityManager.GetBuffer<StoredItemElement>(firstSource).Length);
        Assert.IsTrue(_entityManager.GetComponentData<ConstructionMaterialDeliveryResult>(second).ReservationActive);

        CancelDelivery(second);
        CancelDelivery(second);
        RunMaterialApplyPhase();
        Assert.AreEqual(0, _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site)[0].ReservedQuantity);
        Assert.AreEqual(ConstructionMaterialDeliveryOutcomeEnum.Cancelled,
            _entityManager.GetComponentData<ConstructionMaterialDeliveryResult>(second).Outcome);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void F004Conflict_PreservesRegisteredReservationUntilExplicitCancellation(bool destroy)
    {
        Entity site = CreateSite(BuildingTypeEnum.Storage, 3);
        Entity source = Entities.CreateConstructionMaterialSource();
        Entity item = Entities.CreateStoredConstructionMaterial(source, ItemTypeEnum.Iron);
        Entity delivery = Entities.CreateConstructionMaterialDelivery(site, source, item, ItemTypeEnum.Iron, true);
        RunMaterialApplyPhase();
        if (destroy)
        {
            // 유효한 Destroy의 Producer 계약: 소유 버퍼를 먼저 제거한다.
            _entityManager.GetBuffer<StoredItemElement>(source).Clear();
            _entityManager.AddComponent<DestroyItemRequest>(item);
        }
        else
        {
            Entity demolition = _entityManager.CreateEntity();
            _entityManager.AddComponentData(demolition, new DemolishBuildingRequest(source));
        }
        Entity request = RequestDelivery(delivery, source);
        RunMaterialApplyPhase();
        Assert.IsFalse(_entityManager.Exists(request));
        Assert.AreEqual(0, _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site)[0].DeliveredQuantity);
        Assert.AreEqual(1, _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site)[0].ReservedQuantity);
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(site).Length);
        Assert.AreEqual(destroy ? 0 : 1, _entityManager.GetBuffer<StoredItemElement>(source).Length);
        Assert.AreEqual(source, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
        var result = _entityManager.GetComponentData<ConstructionMaterialDeliveryResult>(delivery);
        Assert.IsTrue(result.ReservationActive);
        Assert.AreEqual(destroy ? ConstructionMaterialDeliveryOutcomeEnum.DestroyConflict :
            ConstructionMaterialDeliveryOutcomeEnum.DemolitionConflict, result.Outcome);
        CancelDelivery(delivery);
        RunMaterialApplyPhase();
        Assert.AreEqual(0, _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site)[0].ReservedQuantity);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TransportedMaterial_UsesCurrentHolderAndKeepsOriginalSource(bool worldItem)
    {
        Entity site = CreateSite(BuildingTypeEnum.Storage, 3);
        Entity source = Entities.CreateConstructionMaterialSource();
        Entity item = Entities.CreateStoredConstructionMaterial(source, ItemTypeEnum.Iron);
        Entity delivery = Entities.CreateConstructionMaterialDelivery(site, source, item, ItemTypeEnum.Iron, true);
        RunMaterialApplyPhase();
        Entity carrier = Entity.Null;
        _entityManager.GetBuffer<StoredItemElement>(source).Clear();
        if (worldItem)
        {
            _entityManager.SetComponentData(item, ItemOwnership.WorldItem);
            _entityManager.AddComponent<BeltMovementState>(item);
            _entityManager.RemoveComponent<DisableRendering>(item);
        }
        else
        {
            carrier = _entityManager.CreateEntity();
            _entityManager.AddBuffer<StoredItemElement>(carrier).Add(new StoredItemElement(item, ItemTypeEnum.Iron, 0));
            _entityManager.SetComponentData(item, ItemOwnership.Stored(carrier));
        }
        RequestDelivery(delivery, carrier);
        RunMaterialApplyPhase();
        Assert.AreEqual(source, _entityManager.GetComponentData<ConstructionMaterialDelivery>(delivery).SourceBuilding);
        if (worldItem)
        {
            Assert.IsFalse(_entityManager.IsComponentEnabled<BeltMovementState>(item));
        }
        else
        {
            Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(carrier).Length);
        }
        Assert.AreEqual(site, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
        Assert.AreEqual(1, _entityManager.GetBuffer<StoredItemElement>(site).Length);
    }

    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public void ConcurrentWorldTransfer_RejectsSupplyWithoutDependingOnOwnershipSystemOrder(
        bool ownershipFirst, bool expectWorldOwner)
    {
        Entity site = CreateSite(BuildingTypeEnum.Storage, 3);
        Entity source = Entities.CreateConstructionMaterialSource();
        Entity item = Entities.CreateStoredConstructionMaterial(source, ItemTypeEnum.Iron);
        Entity delivery = Entities.CreateConstructionMaterialDelivery(site, source, item, ItemTypeEnum.Iron, true);
        RunMaterialApplyPhase();
        _entityManager.GetBuffer<StoredItemElement>(source).Clear();
        _entityManager.AddComponentData(item, new TransferOwnershipRequest(Entity.Null));
        RequestDelivery(delivery, expectWorldOwner ? Entity.Null : source);
        var ownership = _world.GetOrCreateSystem<ItemOwnershipApplySystem>();
        if (ownershipFirst)
        {
            Simulation.UpdateAndComplete(ownership);
            Assert.IsFalse(_entityManager.IsComponentEnabled<TransferOwnershipRequest>(item));
            Assert.IsTrue(_entityManager.GetComponentData<TransferOwnershipRequest>(item).ProcessedInStateApply);
        }
        Simulation.UpdateAndComplete(_lifecycleApplySystem);
        if (!ownershipFirst)
        {
            Simulation.UpdateAndComplete(ownership);
        }
        Simulation.Playback(_ecbSystem);
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
        Assert.IsFalse(_entityManager.HasComponent<DisableRendering>(item));
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(site).Length);
        Assert.AreEqual(0, _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site)[0].ReservedQuantity);
        Assert.IsFalse(_entityManager.IsComponentEnabled<TransferOwnershipRequest>(item));
        Assert.IsFalse(_entityManager.GetComponentData<TransferOwnershipRequest>(item).ProcessedInStateApply);
        Assert.AreEqual(ConstructionMaterialDeliveryOutcomeEnum.ConflictingTransfer,
            _entityManager.GetComponentData<ConstructionMaterialDeliveryResult>(delivery).Outcome);

        // 다음 틱에 공급원으로 정상 복귀한 실물은 이전 처리 표시 때문에 막히지 않는다.
        _entityManager.GetBuffer<StoredItemElement>(source).Add(new StoredItemElement(item, ItemTypeEnum.Iron, 0));
        _entityManager.SetComponentData(item, ItemOwnership.Stored(source));
        _entityManager.AddComponent<DisableRendering>(item);
        Entity nextDelivery = Entities.CreateConstructionMaterialDelivery(site, source, item, ItemTypeEnum.Iron, true);
        RequestDelivery(nextDelivery, source);
        RunMaterialApplyPhase();
        Assert.AreEqual(site, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
        Assert.AreEqual(1, _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site)[0].DeliveredQuantity);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DuplicateRegistration_DoesNotConsumeCapacityNeededByAnotherPhysicalItem(bool previouslyRegistered)
    {
        Entity site = CreateSite(BuildingTypeEnum.Storage, 2);
        Entity firstSource = Entities.CreateConstructionMaterialSource();
        Entity secondSource = Entities.CreateConstructionMaterialSource();
        Entity firstItem = Entities.CreateStoredConstructionMaterial(firstSource, ItemTypeEnum.Iron);
        Entity secondItem = Entities.CreateStoredConstructionMaterial(secondSource, ItemTypeEnum.Iron);
        Entity first = Entities.CreateConstructionMaterialDelivery(site, firstSource, firstItem, ItemTypeEnum.Iron, true);
        if (previouslyRegistered)
        {
            RunMaterialApplyPhase();
        }
        Entity duplicate = Entities.CreateConstructionMaterialDelivery(site, firstSource, firstItem, ItemTypeEnum.Iron, true);
        Entity second = Entities.CreateConstructionMaterialDelivery(site, secondSource, secondItem, ItemTypeEnum.Iron, true);
        RunMaterialApplyPhase();
        Assert.AreEqual(2, _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site)[0].ReservedQuantity);
        Assert.AreEqual(ConstructionMaterialDeliveryOutcomeEnum.ItemAlreadyClaimed,
            _entityManager.GetComponentData<ConstructionMaterialDeliveryResult>(duplicate).Outcome);
        Assert.IsFalse(_entityManager.GetComponentData<ConstructionMaterialDeliveryResult>(duplicate).ReservationActive);
        Assert.AreEqual(ConstructionMaterialDeliveryStateEnum.Ready,
            _entityManager.GetComponentData<ConstructionMaterialDeliveryResult>(second).State);

        RequestDelivery(first, firstSource);
        RequestDelivery(duplicate, firstSource);
        RequestDelivery(second, secondSource);
        RunMaterialApplyPhase();
        Assert.AreEqual(2, _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site)[0].DeliveredQuantity);
        Assert.AreEqual(0, _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site)[0].ReservedQuantity);
        Assert.AreEqual(2, _entityManager.GetBuffer<StoredItemElement>(site).Length);
        Assert.AreEqual(site, _entityManager.GetComponentData<ItemOwnership>(firstItem).Owner);
        Assert.AreEqual(site, _entityManager.GetComponentData<ItemOwnership>(secondItem).Owner);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CancelledDelivery_ReleasesPhysicalClaimBeforeReplacementRegistration(bool previouslyRegistered)
    {
        Entity firstSite = CreateSite(BuildingTypeEnum.Storage, 1);
        Entity nextSite = CreateSite(BuildingTypeEnum.Storage, 1);
        Entity source = Entities.CreateConstructionMaterialSource();
        Entity item = Entities.CreateStoredConstructionMaterial(source, ItemTypeEnum.Iron);
        Entity first = Entities.CreateConstructionMaterialDelivery(firstSite, source, item, ItemTypeEnum.Iron, true);
        if (previouslyRegistered)
        {
            RunMaterialApplyPhase();
        }
        CancelDelivery(first);
        Entity replacement = Entities.CreateConstructionMaterialDelivery(nextSite, source, item, ItemTypeEnum.Iron, true);
        RunMaterialApplyPhase();
        Assert.AreEqual(ConstructionMaterialDeliveryOutcomeEnum.Cancelled,
            _entityManager.GetComponentData<ConstructionMaterialDeliveryResult>(first).Outcome);
        Assert.AreEqual(0, _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(firstSite)[0].ReservedQuantity);
        Assert.AreEqual(1, _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(nextSite)[0].ReservedQuantity);
        Assert.AreEqual(ConstructionMaterialDeliveryStateEnum.Ready,
            _entityManager.GetComponentData<ConstructionMaterialDeliveryResult>(replacement).State);
    }

    [Test]
    public void ClosedSite_ReleasesPhysicalClaimBeforeReplacementRegistration()
    {
        Entity firstSite = CreateSite(BuildingTypeEnum.Storage, 1);
        Entity nextSite = CreateSite(BuildingTypeEnum.Storage, 1);
        Entity source = Entities.CreateConstructionMaterialSource();
        Entity item = Entities.CreateStoredConstructionMaterial(source, ItemTypeEnum.Iron);
        Entity first = Entities.CreateConstructionMaterialDelivery(firstSite, source, item, ItemTypeEnum.Iron, true);
        RunMaterialApplyPhase();
        Entity cancelSite = _entityManager.CreateEntity();
        _entityManager.AddComponentData(cancelSite, new CancelConstructionRequest(firstSite));
        Entity replacement = Entities.CreateConstructionMaterialDelivery(nextSite, source, item, ItemTypeEnum.Iron, true);
        RunMaterialApplyPhase();
        Assert.IsFalse(_entityManager.Exists(firstSite));
        Assert.IsFalse(_entityManager.GetComponentData<ConstructionMaterialDeliveryResult>(first).ReservationActive);
        Assert.AreEqual(ConstructionMaterialDeliveryOutcomeEnum.SiteClosed,
            _entityManager.GetComponentData<ConstructionMaterialDeliveryResult>(first).Outcome);
        Assert.AreEqual(ConstructionMaterialDeliveryStateEnum.Ready,
            _entityManager.GetComponentData<ConstructionMaterialDeliveryResult>(replacement).State);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void DestroyedTransfer_DoesNotRecordCleanupCommandsAgainstDeletedItem(bool ownershipFirst)
    {
        Entity site = CreateSite(BuildingTypeEnum.Storage, 2);
        Entity source = Entities.CreateConstructionMaterialSource();
        Entity item = Entities.CreateStoredConstructionMaterial(source, ItemTypeEnum.Iron);
        Entity delivery = Entities.CreateConstructionMaterialDelivery(site, source, item, ItemTypeEnum.Iron, true);
        RunMaterialApplyPhase();
        _entityManager.GetBuffer<StoredItemElement>(source).Clear();
        _entityManager.AddComponent<DestroyItemRequest>(item);
        _entityManager.AddComponentData(item, new TransferOwnershipRequest(Entity.Null));
        RequestDelivery(delivery, Entity.Null);
        var ownership = _world.GetOrCreateSystem<ItemOwnershipApplySystem>();
        var lifecycle = _world.GetOrCreateSystem<ItemLifecycleApplySystem>();
        if (ownershipFirst)
        {
            Simulation.UpdateAndComplete(ownership);
        }
        Simulation.UpdateAndComplete(lifecycle);
        if (!ownershipFirst)
        {
            Simulation.UpdateAndComplete(ownership);
        }
        Simulation.UpdateAndComplete(_lifecycleApplySystem);
        Simulation.Playback(_ecbSystem);
        Assert.IsFalse(_entityManager.Exists(item));
        Assert.AreEqual(1, _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site)[0].ReservedQuantity);
        Assert.AreEqual(ConstructionMaterialDeliveryOutcomeEnum.DestroyConflict,
            _entityManager.GetComponentData<ConstructionMaterialDeliveryResult>(delivery).Outcome);
    }

    [Test]
    public void MissingDestinationBuffer_DoesNotRemoveSourceMaterialOrCountDelivery()
    {
        Entity site = CreateSite(BuildingTypeEnum.Storage, 3);
        Entity source = Entities.CreateConstructionMaterialSource();
        Entity item = Entities.CreateStoredConstructionMaterial(source, ItemTypeEnum.Iron);
        Entity delivery = Entities.CreateConstructionMaterialDelivery(site, source, item, ItemTypeEnum.Iron, true);
        RunMaterialApplyPhase();
        _entityManager.RemoveComponent<StoredItemElement>(site);
        RequestDelivery(delivery, source);
        RunMaterialApplyPhase();
        Assert.AreEqual(0, _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site)[0].DeliveredQuantity);
        Assert.AreEqual(1, _entityManager.GetBuffer<StoredItemElement>(source).Length);
        Assert.AreEqual(source, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
    }

    [Test]
    public void Completion_ClosesOutstandingReservationAndRetainsDeliveredResult()
    {
        Entity site = CreateSite(BuildingTypeEnum.Belt, 1);
        _entityManager.AddComponentData(site, new GridPosition(Unity.Mathematics.int2.zero));
        _entityManager.AddComponentData(site, new Direction(DirectionEnum.Up));
        _entityManager.AddComponentData(site, new BuildingFootprint(new Unity.Mathematics.int2(1, 1)));
        Entity reservedSource = Entities.CreateConstructionMaterialSource();
        Entity suppliedSource = Entities.CreateConstructionMaterialSource();
        Entity reservedItem = Entities.CreateStoredConstructionMaterial(reservedSource, ItemTypeEnum.Iron);
        Entity suppliedItem = Entities.CreateStoredConstructionMaterial(suppliedSource, ItemTypeEnum.Iron);
        Entity pending = Entities.CreateConstructionMaterialDelivery(site, reservedSource, reservedItem, ItemTypeEnum.Iron, true);
        Entity supplied = Entities.CreateConstructionMaterialDelivery(site, suppliedSource, suppliedItem, ItemTypeEnum.Iron);
        RunMaterialApplyPhase();
        RequestDelivery(supplied, suppliedSource);
        RunMaterialApplyPhase();
        Assert.IsFalse(_entityManager.Exists(site));
        Assert.IsFalse(_entityManager.Exists(suppliedItem));
        Assert.IsTrue(_entityManager.Exists(reservedItem));
        var pendingResult = _entityManager.GetComponentData<ConstructionMaterialDeliveryResult>(pending);
        Assert.AreEqual(ConstructionMaterialDeliveryOutcomeEnum.SiteClosed, pendingResult.Outcome);
        Assert.IsFalse(pendingResult.ReservationActive);
        Assert.AreEqual(ConstructionMaterialDeliveryOutcomeEnum.Supplied,
            _entityManager.GetComponentData<ConstructionMaterialDeliveryResult>(supplied).Outcome);
        RequestDelivery(supplied, Entity.Null);
        RunMaterialApplyPhase();
        Assert.AreEqual(ConstructionMaterialDeliveryOutcomeEnum.Supplied,
            _entityManager.GetComponentData<ConstructionMaterialDeliveryResult>(supplied).Outcome);
    }

    [Test]
    public void Test08_SupplyMaterial_MultipleSupplies_ExceedingBatch_AcceptsUntilFullAndRejectsRemainder()
    {
        // 1. 현장 준비: Iron 2개만 요구
        var siteEntity = CreateSite(BuildingTypeEnum.Crafter, required: 2);

        // 2. 아이템 3개 동시 전달 시도 (1개 초과)
        Entity firstSource = Entities.CreateConstructionMaterialSource();
        Entity secondSource = Entities.CreateConstructionMaterialSource();
        Entity thirdSource = Entities.CreateConstructionMaterialSource();
        Entity item1 = Entities.CreateStoredConstructionMaterial(firstSource, ItemTypeEnum.Iron);
        Entity item2 = Entities.CreateStoredConstructionMaterial(secondSource, ItemTypeEnum.Iron);
        Entity item3 = Entities.CreateStoredConstructionMaterial(thirdSource, ItemTypeEnum.Iron);

        RequestSupply(siteEntity, firstSource, item1, ItemTypeEnum.Iron);
        RequestSupply(siteEntity, secondSource, item2, ItemTypeEnum.Iron);
        RequestSupply(siteEntity, thirdSource, item3, ItemTypeEnum.Iron);

        RunMaterialApplyPhase();

        // 3. 검증: 정확히 2개만 수령되고, 3번째는 초과 거부됨
        var updatedReqBuffer = _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(siteEntity);
        Assert.AreEqual(2, updatedReqBuffer[0].DeliveredQuantity, "최대 요구량인 2개까지만 수령되어야 함");

        var storedBuffer = _entityManager.GetBuffer<StoredItemElement>(siteEntity);
        Assert.AreEqual(2, storedBuffer.Length, "현장 보관함에는 2개만 등록되어야 함");

        // 앞선 두 실물은 현장 수납, 초과 실물은 공급원 유지.
        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(item1).IsStored);
        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(item2).IsStored);
        Assert.AreEqual(thirdSource, _entityManager.GetComponentData<ItemOwnership>(item3).Owner,
            "초과된 실물은 공급원에 유지되어야 함");
    }
}
