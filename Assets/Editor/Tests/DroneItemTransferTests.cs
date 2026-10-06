using System;
using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>
/// 역할·목적: 행동 신호의 실물 보존·예약/결과 정산에 대한 NUnit EditMode 회귀 검증.
/// 입력·검사: 수행자/배정/관측은 fixture로 준비한다. 이전 틱 실물/공간 상한·접수 순서·무효 행동을 검사하며 실제 이동/경로/배터리는 실행하지 않는다.
/// 수명: EcsWorldTestFixture가 각 사례의 독립 World를 준비하고 종료 시 해제한다.
/// </summary>
public class DroneItemTransferTests : EcsWorldTestFixture
{
    private StateApplyGroup _apply;
    private DecisionGroup _decision;
    private ExecutionGroup _execution;
    private ReservationGroup _reservation;
    private Entity _capacity;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        CreateGameplayPrefabDatabases();
        Entity registry = _entityManager.CreateEntity(typeof(ItemRegistry));
        _entityManager.SetComponentData(registry, new ItemRegistry { DefaultMaxStack = 2 });
        var config = _entityManager.AddBuffer<ItemConfigElement>(registry);
        foreach (ItemTypeEnum type in Enum.GetValues(typeof(ItemTypeEnum)))
        {
            config.Add(new ItemConfigElement(type, 2));
        }
        _capacity = _entityManager.CreateEntity(typeof(DroneCapacityState));
        _entityManager.SetComponentData(_capacity, new DroneCapacityState { CarryingCapacity = 3 });
        _apply = _world.GetOrCreateSystemManaged<StateApplyGroup>();
        // 등록 순서에 기대지 않고 실제 시스템의 순서 계약으로 정렬한다.
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>());
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskLifecycleApplySystem>());
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemOwnershipApplySystem>());
        _apply.SortSystems();
        _decision = _world.GetOrCreateSystemManaged<DecisionGroup>();
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneItemTransferDecisionSystem>());
        _decision.SortSystems();
        _execution = _world.GetOrCreateSystemManaged<ExecutionGroup>();
        _execution.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneItemTransferExecutionSystem>());
        _execution.SortSystems();
        _reservation = _world.GetOrCreateSystemManaged<ReservationGroup>();
        _reservation.AddSystemToUpdateList(_world.GetOrCreateSystem<ConstructionSupplyReservationSystem>());
        _reservation.SortSystems();
    }

    [Test]
    public void CommonItemOwnerTransfer_UpdatesContainersImmediately_AndDoesNotReplayTheTransfer()
    {
        Entity firstStorage = CreateStorage(0);
        Entity secondStorage = CreateStorage(0);
        Entity item = CreateItem(Entity.Null);
        var transform = LocalTransform.FromPositionRotationScale(
            new float3(0f, 0f, 7f), quaternion.RotateZ(0.7f), 2f);
        _entityManager.SetComponentData(item, transform);
        _entityManager.SetComponentEnabled<BeltMovementState>(item, true);
        using var ecb = new EntityCommandBuffer(Allocator.TempJob);

        Assert.IsTrue(ItemOwnershipApplySystem.TryTransferItem(_entityManager, item, ItemTypeEnum.Iron,
            Entity.Null, firstStorage, 1, new int2(3, 4), ecb));

        Assert.AreEqual(firstStorage, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
        CollectionAssert.AreEqual(new[] { item }, Stored(firstStorage));
        Assert.AreEqual(1, _entityManager.GetBuffer<StoredItemElement>(firstStorage)[0].SlotIndex);
        Assert.AreEqual(new int2(3, 4), _entityManager.GetComponentData<GridPosition>(item).Value);
        Assert.IsFalse(_entityManager.IsComponentEnabled<BeltMovementState>(item));
        Assert.IsFalse(_entityManager.HasComponent<DisableRendering>(item), "렌더 태그는 ECB 재생에 적용한다.");
        ecb.Playback(_entityManager);
        AssertStored(firstStorage, new[] { item });

        using var storeEcb = new EntityCommandBuffer(Allocator.TempJob);
        Assert.IsFalse(ItemOwnershipApplySystem.TryTransferItem(_entityManager, item, ItemTypeEnum.Iron,
            Entity.Null, firstStorage, 1, new int2(3, 4), storeEcb));
        AssertStored(firstStorage, new[] { item });
        _entityManager.SetComponentData(item, new TransferOwnershipRequest(firstStorage));
        _entityManager.SetComponentEnabled<TransferOwnershipRequest>(item, true);
        Assert.IsTrue(ItemOwnershipApplySystem.TryTransferItem(_entityManager, item, ItemTypeEnum.Iron,
            firstStorage, secondStorage, 0, new int2(8, 9), storeEcb));
        Assert.AreEqual(0, Stored(firstStorage).Length);
        AssertStored(secondStorage, new[] { item });
        Assert.IsFalse(_entityManager.IsComponentEnabled<TransferOwnershipRequest>(item));
        storeEcb.Playback(_entityManager);
        _apply.Update();
        AssertStored(secondStorage, new[] { item });

        using var dropEcb = new EntityCommandBuffer(Allocator.TempJob);
        Assert.IsTrue(ItemOwnershipApplySystem.TryTransferItem(_entityManager, item, ItemTypeEnum.Iron,
            secondStorage, Entity.Null, 0, new int2(-2, 6), dropEcb));
        Assert.AreEqual(0, Stored(secondStorage).Length);
        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(item).IsWorldItem);
        Assert.IsTrue(_entityManager.HasComponent<DisableRendering>(item));
        dropEcb.Playback(_entityManager);
        _apply.Update();

        Assert.IsFalse(_entityManager.HasComponent<DisableRendering>(item));
        Assert.AreEqual(new int2(-2, 6), _entityManager.GetComponentData<GridPosition>(item).Value);
        var finalTransform = _entityManager.GetComponentData<LocalTransform>(item);
        Assert.AreEqual(new float3(-2f, 6f, 7f), finalTransform.Position);
        Assert.AreEqual(transform.Rotation, finalTransform.Rotation);
        Assert.AreEqual(transform.Scale, finalTransform.Scale);
        Assert.IsFalse(_entityManager.IsComponentEnabled<TransferOwnershipRequest>(item));
        Assert.AreEqual(0, Stored(firstStorage).Length);
        Assert.AreEqual(0, Stored(secondStorage).Length);
    }

    [Test]
    public void CollectThenSupply_PreservesItemEntities_AndSettlesDeliveredAndReservedQuantities()
    {
        Entity source = CreateStorage(3);
        Entity[] originalItems = Stored(source);
        Entity site = CreateSite(3);
        Entity worker = CreateWorker();
        Entity assignment = CreateAssignment(worker, source, site, 3, DroneActionKindEnum.CollectFromStorage);
        Entity collect = Submit(assignment);

        TransferTick();

        AssertResult(collect, DroneItemTransferStatusEnum.Completed, 3);
        AssertStored(worker, originalItems);
        Assert.AreEqual(0, Stored(source).Length);
        Assert.AreEqual(3, Requirement(site).ReservedQuantity);
        Assert.AreEqual(0, Requirement(site).DeliveredQuantity);
        Assert.AreEqual(DroneCargoOriginEnum.Supply, _entityManager.GetComponentData<DroneCargoState>(worker).Origin);
        Assert.AreEqual(DroneActionKindEnum.SupplyConstructionSite,
            _entityManager.GetComponentData<DroneTaskAssignment>(assignment).NextAction);

        Entity supply = Submit(assignment, 2);
        TransferTick();

        AssertResult(supply, DroneItemTransferStatusEnum.Completed, 3);
        AssertStored(site, originalItems);
        Assert.AreEqual(0, Stored(worker).Length);
        Assert.AreEqual(3, Requirement(site).DeliveredQuantity);
        Assert.AreEqual(0, Requirement(site).ReservedQuantity);
        Assert.AreEqual(0, Reservation(assignment).RemainingQuantity);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Completed, Assignment(assignment).State);
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment);
        Assert.AreEqual(DroneCargoOriginEnum.None, _entityManager.GetComponentData<DroneCargoState>(worker).Origin);
    }

    [Test]
    public void LastClearanceItemRecovered_CompletesTheSiteInTheSameApply()
    {
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<ConstructionLifecycleApplySystem>());
        _apply.SortSystems();
        Entity site = CreateSite(0);
        var siteData = _entityManager.GetComponentData<ConstructionSite>(site);
        siteData.Flags |= ConstructionSiteFlags.AwaitingItemClearance;
        _entityManager.SetComponentData(site, siteData);
        Entity item = CreateItem(Entity.Null);
        _entityManager.SetComponentData(item, _entityManager.GetComponentData<GridPosition>(site));
        Entity destination = CreateStorage(0);
        Entity worker = CreateWorker();
        Entity assignment = CreateAssignment(worker, item, destination, 1, DroneActionKindEnum.RecoverWorldItem);
        Entity taskEntity = Assignment(assignment).Task;
        var task = _entityManager.GetComponentData<DroneLogisticsTask>(taskEntity);
        task.RecoveryReason = DroneRecoveryReasonEnum.SiteClearance;
        _entityManager.SetComponentData(taskEntity, task);
        Entity request = Submit(assignment);

        TransferTick();

        AssertResult(request, DroneItemTransferStatusEnum.Completed, 1);
        Assert.IsFalse(_entityManager.Exists(site));
        Assert.IsTrue(_entityManager.Exists(item));
        AssertStored(worker, new[] { item });
        Assert.AreEqual(DroneCargoOriginEnum.Recovery, _entityManager.GetComponentData<DroneCargoState>(worker).Origin);
    }

    [TestCase(0)]
    [TestCase(1)]
    public void ShortCollection_ReleasesOnlyUncollectedSiteReservation(int available)
    {
        Entity source = CreateStorage(available);
        Entity[] originals = Stored(source);
        Entity site = CreateSite(3);
        Entity worker = CreateWorker();
        Entity assignment = CreateAssignment(worker, source, site, 3, DroneActionKindEnum.CollectFromStorage);
        Entity request = Submit(assignment);

        TransferTick();

        AssertResult(request, available == 0 ? DroneItemTransferStatusEnum.Unavailable : DroneItemTransferStatusEnum.Partial, available);
        AssertStored(worker, originals);
        Assert.AreEqual(available, Requirement(site).ReservedQuantity);
        Assert.AreEqual(available, Reservation(assignment).RemainingQuantity);
        Assert.AreEqual(0, Requirement(site).DeliveredQuantity);
        if (available == 0)
        {
            Assert.AreEqual(DroneTaskAssignmentStateEnum.Cancelled, Assignment(assignment).State);
            Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment);
        }
        else
        {
            Assert.AreEqual(available, Assignment(assignment).AssignedQuantity);
            Assert.AreEqual(DroneTaskAssignmentStateEnum.MovingToDestination, Assignment(assignment).State);
        }
    }

    [Test]
    public void CompetingCollections_FirstSubmittedRequestGetsTheOnlyItem()
    {
        Entity source = CreateStorage(1);
        Entity original = Stored(source)[0];
        Entity firstWorker = CreateWorker();
        Entity secondWorker = CreateWorker();
        Entity firstSite = CreateSite(1);
        Entity secondSite = CreateSite(1);
        Entity firstAssignment = CreateAssignment(firstWorker, source, firstSite, 1, DroneActionKindEnum.CollectFromStorage);
        Entity secondAssignment = CreateAssignment(secondWorker, source, secondSite, 1, DroneActionKindEnum.CollectFromStorage);
        Entity earlier = Submit(secondAssignment);
        Entity later = Submit(firstAssignment);

        TransferTick();

        AssertResult(earlier, DroneItemTransferStatusEnum.Completed, 1);
        AssertResult(later, DroneItemTransferStatusEnum.Unavailable, 0);
        AssertStored(secondWorker, new[] { original });
        Assert.AreEqual(0, Stored(firstWorker).Length);
        Assert.AreEqual(0, Requirement(firstSite).ReservedQuantity);
        Assert.AreEqual(1, Requirement(secondSite).ReservedQuantity);
    }

    [Test]
    public void SupplyAcceptsOnlyRemainingNeed_AndRetargetsCargoWithoutKeepingTheOldReservation()
    {
        Entity site = CreateSite(3);
        var requirement = Requirement(site);
        requirement.DeliveredQuantity = 2;
        var requirements = _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site);
        requirements[0] = requirement;
        AddItems(site, 2);
        Entity worker = CreateWorker(3, DroneCargoOriginEnum.Supply);
        Entity[] cargo = Stored(worker);
        Entity assignment = CreateAssignment(worker, Entity.Null, site, 3, DroneActionKindEnum.SupplyConstructionSite);
        Entity request = Submit(assignment);

        TransferTick();

        AssertResult(request, DroneItemTransferStatusEnum.Partial, 1);
        Assert.AreEqual(3, Requirement(site).DeliveredQuantity);
        Assert.AreEqual(0, Requirement(site).ReservedQuantity);
        Assert.AreEqual(0, Reservation(assignment).RemainingQuantity);
        Assert.AreEqual(3, Stored(site).Length);
        Assert.AreEqual(2, Stored(worker).Length);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Retargeting, Assignment(assignment).State);
        AssertAllItemsStillExist(cargo);
        foreach (Entity item in Stored(worker)) Assert.AreEqual(worker, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
    }

    [Test]
    public void RecoveryThenStorage_PreservesTheWorldItem_AndClearsRecoveryCargoOnlyAfterDeposit()
    {
        Entity item = CreateItem(Entity.Null);
        _entityManager.AddComponent<DroneRecoveryPending>(item);
        Entity storage = CreateStorage(0, 1);
        Entity worker = CreateWorker();
        Entity assignment = CreateAssignment(worker, item, storage, 1, DroneActionKindEnum.RecoverWorldItem);
        Entity recover = Submit(assignment);

        TransferTick();

        AssertResult(recover, DroneItemTransferStatusEnum.Completed, 1);
        AssertStored(worker, new[] { item });
        Assert.IsFalse(_entityManager.HasComponent<DroneRecoveryPending>(item));
        Assert.AreEqual(DroneCargoOriginEnum.Recovery, _entityManager.GetComponentData<DroneCargoState>(worker).Origin);
        Assert.AreEqual(DroneActionKindEnum.StoreCargo, Assignment(assignment).NextAction);
        CleanupManagement();
        Assert.AreEqual(DroneTaskAssignmentStateEnum.MovingToDestination, Assignment(assignment).State,
            "회수 실물이 수행자 소유로 바뀌어도 유효한 보관 목적지로 이동하는 배정을 무효화하지 않는다.");
        AssertStored(worker, new[] { item });
        Entity store = Submit(assignment, 2);
        TransferTick();

        AssertResult(store, DroneItemTransferStatusEnum.Completed, 1);
        AssertStored(storage, new[] { item });
        Assert.AreEqual(0, Stored(worker).Length);
        Assert.AreEqual(DroneCargoOriginEnum.None, _entityManager.GetComponentData<DroneCargoState>(worker).Origin);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Completed, Assignment(assignment).State);
    }

    [Test]
    public void LastSupplyInSortedStateApply_CompletesBuildingAndConsumesSuppliedItemsAtTheSamePlayback()
    {
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<ConstructionLifecycleApplySystem>());
        _apply.SortSystems();
        Entity site = CreateSite(1);
        Entity worker = CreateWorker(1, DroneCargoOriginEnum.Supply);
        Entity item = Stored(worker)[0];
        Entity assignment = CreateAssignment(worker, Entity.Null, site, 1, DroneActionKindEnum.SupplyConstructionSite);
        Entity request = Submit(assignment);

        TransferTick();

        AssertResult(request, DroneItemTransferStatusEnum.Completed, 1);
        Assert.IsFalse(_entityManager.Exists(site));
        Assert.IsFalse(_entityManager.Exists(item));
        Assert.AreEqual(0, Stored(worker).Length);
        Assert.AreEqual(0, Reservation(assignment).RemainingQuantity);
        using var completedBuildings = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<BuildingType>(), ComponentType.ReadOnly<Storage>());
        using var buildings = completedBuildings.ToEntityArray(Allocator.Temp);
        Assert.AreEqual(1, buildings.Length);
        Assert.AreEqual(BuildingTypeEnum.Storage, _entityManager.GetComponentData<BuildingType>(buildings[0]).Type);
        Assert.AreEqual(int2.zero, _entityManager.GetComponentData<GridPosition>(buildings[0]).Value);
    }

    [Test]
    public void RecoveredCargo_CannotBeSuppliedDirectlyToAConstructionSite()
    {
        Entity worker = CreateWorker(1, DroneCargoOriginEnum.Recovery);
        Entity[] cargo = Stored(worker);
        Entity site = CreateSite(1);
        Entity assignment = CreateAssignment(worker, Entity.Null, site, 1, DroneActionKindEnum.SupplyConstructionSite);
        Entity request = Submit(assignment);

        TransferTick();

        AssertResult(request, DroneItemTransferStatusEnum.Rejected, 0);
        AssertStored(worker, cargo);
        Assert.AreEqual(0, Requirement(site).DeliveredQuantity);
        Assert.AreEqual(0, Stored(site).Length);
        Assert.AreEqual(DroneCargoOriginEnum.Recovery, _entityManager.GetComponentData<DroneCargoState>(worker).Origin);
    }

    [Test]
    public void PartialStorage_FillsActualSpace_AndPreservesRemainingCargo()
    {
        Entity storage = CreateStorage(1, 1);
        Entity worker = CreateWorker(3, DroneCargoOriginEnum.Recovery);
        Entity[] cargo = Stored(worker);
        Entity assignment = CreateAssignment(worker, cargo[0], storage, 3, DroneActionKindEnum.StoreCargo);
        Entity request = Submit(assignment);

        TransferTick();

        AssertResult(request, DroneItemTransferStatusEnum.Partial, 1);
        Assert.AreEqual(2, Stored(storage).Length);
        Assert.AreEqual(2, Stored(worker).Length);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Retargeting, Assignment(assignment).State);
        Assert.AreEqual(DroneCargoOriginEnum.Recovery, _entityManager.GetComponentData<DroneCargoState>(worker).Origin);
        AssertAllItemsStillExist(cargo);
        foreach (Entity item in Stored(storage)) Assert.AreEqual(storage, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
        foreach (Entity item in Stored(worker)) Assert.AreEqual(worker, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
    }

    [Test]
    public void CompetingDeposits_FirstSubmittedRequestGetsTheOnlySpace()
    {
        Entity storage = CreateStorage(1, 1);
        Entity firstWorker = CreateWorker(1, DroneCargoOriginEnum.Supply);
        Entity secondWorker = CreateWorker(1, DroneCargoOriginEnum.Supply);
        Entity firstItem = Stored(firstWorker)[0];
        Entity secondItem = Stored(secondWorker)[0];
        Entity firstAssignment = CreateAssignment(firstWorker, firstItem, storage, 1, DroneActionKindEnum.StoreCargo);
        Entity secondAssignment = CreateAssignment(secondWorker, secondItem, storage, 1, DroneActionKindEnum.StoreCargo);
        Entity earlier = Submit(secondAssignment);
        Entity later = Submit(firstAssignment);

        TransferTick();

        AssertResult(earlier, DroneItemTransferStatusEnum.Completed, 1);
        AssertResult(later, DroneItemTransferStatusEnum.Unavailable, 0);
        Assert.AreEqual(storage, _entityManager.GetComponentData<ItemOwnership>(secondItem).Owner);
        AssertStored(firstWorker, new[] { firstItem });
        Assert.AreEqual(2, Stored(storage).Length);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Retargeting, Assignment(firstAssignment).State);
    }

    [Test]
    public void DropCargo_ReturnsExistingItemsAtObservedWorldCell_AndMarksRecovery()
    {
        Entity worker = CreateWorker(2, DroneCargoOriginEnum.Supply);
        Entity[] cargo = Stored(worker);
        var observation = _entityManager.GetComponentData<DroneWorkerObservation>(worker);
        observation.Position = new float3(-0.25f, 2.75f, 0f);
        _entityManager.SetComponentData(worker, observation);
        Entity assignment = CreateAssignment(worker, cargo[0], Entity.Null, 2, DroneActionKindEnum.DropCargo);
        Entity request = Submit(assignment);

        TransferTick();

        AssertResult(request, DroneItemTransferStatusEnum.Completed, 2);
        Assert.AreEqual(0, Stored(worker).Length);
        foreach (Entity item in cargo)
        {
            Assert.IsTrue(_entityManager.Exists(item));
            Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(item).IsWorldItem);
            Assert.AreEqual(new int2(-1, 2), _entityManager.GetComponentData<GridPosition>(item).Value);
            Assert.IsTrue(_entityManager.HasComponent<DroneRecoveryPending>(item));
            Assert.IsFalse(_entityManager.HasComponent<DisableRendering>(item));
        }
        Assert.AreEqual(DroneCargoOriginEnum.None, _entityManager.GetComponentData<DroneCargoState>(worker).Origin);
    }

    [TestCase("revision")]
    [TestCase("sequence-zero")]
    [TestCase("target")]
    [TestCase("worker-link")]
    [TestCase("receipt-zero")]
    [TestCase("observation")]
    [TestCase("action-state")]
    public void InvalidActionIdentity_DoesNotTransferItemsOrChangeSiteAccounting(string invalidPart)
    {
        Entity source = CreateStorage(1);
        Entity[] originals = Stored(source);
        Entity site = CreateSite(1);
        Entity worker = CreateWorker();
        Entity assignment = CreateAssignment(worker, source, site, 1, DroneActionKindEnum.CollectFromStorage);
        DroneActionReadyRequest action = Ready(assignment);
        if (invalidPart == "revision") action.Action.AssignmentRevision++;
        if (invalidPart == "sequence-zero") action.Action.Sequence = 0;
        if (invalidPart == "target") action.Action.Target = site;
        if (invalidPart == "observation") action.WorkerObservationRevision++;
        if (invalidPart == "worker-link") _entityManager.SetComponentData(worker, new DroneWorkerAssignment());
        if (invalidPart == "action-state")
        {
            var state = Assignment(assignment);
            state.State = DroneTaskAssignmentStateEnum.Completed;
            _entityManager.SetComponentData(assignment, state);
        }
        Entity request;
        if (invalidPart == "receipt-zero")
        {
            request = _entityManager.CreateEntity(typeof(DroneActionReadyRequest), typeof(DroneItemTransferDecision));
            _entityManager.SetComponentData(request, action);
            _entityManager.AddBuffer<DroneItemTransferItemDecisionElement>(request);
            _entityManager.AddBuffer<DroneItemTransferSlotDecisionElement>(request);
        }
        else request = DroneActionRequestUtility.Submit(_entityManager, action);

        TransferTick();

        AssertResult(request, DroneItemTransferStatusEnum.Rejected, 0);
        AssertStored(source, originals);
        Assert.AreEqual(0, Stored(worker).Length);
        Assert.AreEqual(1, Requirement(site).ReservedQuantity);
        Assert.AreEqual(1, Reservation(assignment).RemainingQuantity);
        Assert.AreEqual(0, Requirement(site).DeliveredQuantity);
    }

    [Test]
    public void DuplicateAndReplayedAction_DoesNotMoveTheSameItemTwice()
    {
        Entity source = CreateStorage(2);
        Entity site = CreateSite(1);
        Entity worker = CreateWorker();
        Entity assignment = CreateAssignment(worker, source, site, 1, DroneActionKindEnum.CollectFromStorage);
        DroneActionReadyRequest ready = Ready(assignment);
        Entity first = DroneActionRequestUtility.Submit(_entityManager, ready);
        Entity duplicate = DroneActionRequestUtility.Submit(_entityManager, ready);
        TransferTick();
        AssertResult(first, DroneItemTransferStatusEnum.Completed, 1);
        AssertResult(duplicate, DroneItemTransferStatusEnum.Rejected, 0);
        Entity[] onceTransferred = Stored(worker);

        TransferTick();
        Entity replay = DroneActionRequestUtility.Submit(_entityManager, ready);
        TransferTick();

        AssertResult(replay, DroneItemTransferStatusEnum.Rejected, 0);
        AssertStored(worker, onceTransferred);
        Assert.AreEqual(1, Stored(source).Length);
        Assert.AreEqual(1, Requirement(site).ReservedQuantity);
        Assert.AreEqual(1UL, Assignment(assignment).LastAppliedActionSequence);
    }

    [Test]
    public void ActiveDestroyItem_IsNotCollected()
    {
        Entity source = CreateStorage(1);
        Entity item = Stored(source)[0];
        _entityManager.SetComponentEnabled<DestroyItemRequest>(item, true);
        Entity site = CreateSite(1);
        Entity worker = CreateWorker();
        Entity assignment = CreateAssignment(worker, source, site, 1, DroneActionKindEnum.CollectFromStorage);
        Entity request = Submit(assignment);

        TransferTick();

        AssertResult(request, DroneItemTransferStatusEnum.Unavailable, 0);
        Assert.AreEqual(0, Stored(worker).Length);
        Assert.AreEqual(source, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
        Assert.AreEqual(0, Requirement(site).ReservedQuantity);
        // 삭제 시스템은 이 격리 그룹에 없으며, 활성 Destroy 실물의 인계 제외만 검증한다.
        Assert.IsTrue(_entityManager.IsComponentEnabled<DestroyItemRequest>(item));
    }

    [Test]
    public void PendingTransferToAnotherOwner_IsNotCollectedFromTheOldOwner()
    {
        Entity source = CreateStorage(1);
        Entity item = Stored(source)[0];
        Entity newOwner = CreateStorage(0);
        _entityManager.GetBuffer<StoredItemElement>(newOwner).Add(new StoredItemElement(item, ItemTypeEnum.Iron, 0));
        _entityManager.SetComponentData(item, new TransferOwnershipRequest(newOwner));
        _entityManager.SetComponentEnabled<TransferOwnershipRequest>(item, true);
        Entity site = CreateSite(1);
        Entity worker = CreateWorker();
        Entity assignment = CreateAssignment(worker, source, site, 1, DroneActionKindEnum.CollectFromStorage);
        Entity request = Submit(assignment);

        TransferTick();

        AssertResult(request, DroneItemTransferStatusEnum.Unavailable, 0);
        Assert.AreEqual(0, Stored(worker).Length);
        Assert.AreEqual(newOwner, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
        Assert.AreEqual(0, Requirement(site).ReservedQuantity);
    }

    [Test]
    public void SameTickBeltDeposit_BecomesCollectibleOnlyOnTheNextTick()
    {
        _world.GetOrCreateSystem<BeltSpatialSyncSystem>();
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingItemStorageApplySystem>());
        _apply.SortSystems();
        Entity source = CreateStorage(0);
        Entity item = CreateItem(Entity.Null);
        _entityManager.AddComponentData(item, new BuildingItemInputDecision(source, true, 0));
        _entityManager.SetComponentEnabled<BuildingItemInputDecision>(item, true);
        _entityManager.SetComponentEnabled<BeltMovementState>(item, true);
        Entity site = CreateSite(1);
        Entity worker = CreateWorker();
        Entity assignment = CreateAssignment(worker, source, site, 1, DroneActionKindEnum.CollectFromStorage);
        Entity request = Submit(assignment);

        TransferTick();

        AssertResult(request, DroneItemTransferStatusEnum.Unavailable, 0);
        AssertStored(source, new[] { item });
        Assert.AreEqual(0, Stored(worker).Length);
        Assert.AreEqual(0, Requirement(site).ReservedQuantity);
        Assert.IsFalse(_entityManager.IsComponentEnabled<BuildingItemInputDecision>(item));
        Assert.IsFalse(_entityManager.IsComponentEnabled<BeltMovementState>(item));
        Assert.IsFalse(_entityManager.IsComponentEnabled<TransferOwnershipRequest>(item));

        Entity retryAssignment = CreateAssignment(worker, source, site, 1, DroneActionKindEnum.CollectFromStorage);
        Entity retry = Submit(retryAssignment);
        TransferTick();

        AssertResult(retry, DroneItemTransferStatusEnum.Completed, 1);
        AssertStored(worker, new[] { item });
        Assert.AreEqual(0, Stored(source).Length);
    }

    [Test]
    public void NewlyDepositedItemAndFreshlyCollectedCargo_AreUsedOnlyByLaterTickActions()
    {
        _world.GetOrCreateSystem<BeltSpatialSyncSystem>();
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingItemStorageApplySystem>());
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<ConstructionLifecycleApplySystem>());
        _apply.SortSystems();
        Entity source = CreateStorage(0);
        Entity item = CreateItem(Entity.Null);
        _entityManager.AddComponentData(item, new BuildingItemInputDecision(source, true, 0));
        _entityManager.SetComponentEnabled<BuildingItemInputDecision>(item, true);
        _entityManager.SetComponentEnabled<BeltMovementState>(item, true);
        Entity site = CreateSite(1);
        Entity worker = CreateWorker();
        Entity assignment = CreateAssignment(worker, source, site, 1, DroneActionKindEnum.CollectFromStorage);
        Entity collection = Submit(assignment);
        // 두 신호를 먼저 접수해도 Decision 시점의 재고와 현재 행동만 유효하다.
        DroneActionReadyRequest readyToSupply = Ready(assignment, 2);
        readyToSupply.Action.Kind = DroneActionKindEnum.SupplyConstructionSite;
        readyToSupply.Action.Target = site;
        Entity supply = DroneActionRequestUtility.Submit(_entityManager, readyToSupply);
        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(item).IsWorldItem);
        Assert.IsFalse(_entityManager.HasComponent<DisableRendering>(item));

        TransferTick();

        AssertResult(collection, DroneItemTransferStatusEnum.Unavailable, 0);
        AssertResult(supply, DroneItemTransferStatusEnum.Rejected, 0);
        AssertStored(source, new[] { item });
        Assert.IsTrue(_entityManager.Exists(site));
        Assert.AreEqual(0, Stored(worker).Length);
        Assert.AreEqual(0, Reservation(assignment).RemainingQuantity);

        Entity retryAssignment = CreateAssignment(worker, source, site, 1, DroneActionKindEnum.CollectFromStorage);
        Entity retryCollect = Submit(retryAssignment);
        DroneActionReadyRequest prematureSupply = Ready(retryAssignment, 2);
        prematureSupply.Action.Kind = DroneActionKindEnum.SupplyConstructionSite;
        prematureSupply.Action.Target = site;
        Entity retrySupply = DroneActionRequestUtility.Submit(_entityManager, prematureSupply);
        TransferTick();

        AssertResult(retryCollect, DroneItemTransferStatusEnum.Completed, 1);
        AssertResult(retrySupply, DroneItemTransferStatusEnum.Rejected, 0);
        AssertStored(worker, new[] { item });
        Assert.AreEqual(0, Stored(source).Length);
        Assert.AreEqual(0, Requirement(site).DeliveredQuantity);
        Assert.AreEqual(1UL, Assignment(retryAssignment).LastAppliedActionSequence);

        Entity finalSupply = Submit(retryAssignment, 2);
        TransferTick();

        AssertResult(finalSupply, DroneItemTransferStatusEnum.Completed, 1);
        Assert.IsFalse(_entityManager.Exists(item));
        Assert.IsFalse(_entityManager.Exists(site));
        Assert.AreEqual(0, Stored(worker).Length);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Completed, Assignment(retryAssignment).State);
        Assert.AreEqual(2UL, Assignment(retryAssignment).LastAppliedActionSequence);
        using var buildings = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<BuildingType>(), ComponentType.ReadOnly<Storage>());
        Assert.AreEqual(2, buildings.CalculateEntityCount(), "기존 공급원과 새 완공 창고가 존재한다.");
    }

    [Test]
    public void CollectionPlan_DoesNotSubstituteNewStockWhenItsOriginalItemLeavesThroughTheBelt()
    {
        Entity source = CreateStorage(1);
        Entity plannedItem = Stored(source)[0];
        PrepareBeltOutput(source, plannedItem);
        Entity site = CreateSite(1);
        Entity worker = CreateWorker();
        Entity assignment = CreateAssignment(worker, source, site, 1, DroneActionKindEnum.CollectFromStorage);
        Entity request = Submit(assignment);
        _decision.Update();
        _execution.Update();

        // Execution에서 읽지 못한 새 입고를 준비한다. 기존 실물은 실제 벨트 출고가 반영한다.
        Entity newStock = CreateItem(source);
        _entityManager.GetBuffer<StoredItemElement>(source).Add(new StoredItemElement(newStock, ItemTypeEnum.Iron, 0));
        _apply.Update();

        AssertResult(request, DroneItemTransferStatusEnum.Unavailable, 0);
        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(plannedItem).IsWorldItem);
        Assert.IsTrue(_entityManager.IsComponentEnabled<BeltMovementState>(plannedItem));
        AssertStored(source, new[] { newStock });
        Assert.AreEqual(0, Stored(worker).Length);
        Assert.AreEqual(0, Requirement(site).ReservedQuantity);
    }

    [Test]
    public void SpaceFreedBySameTickBeltOutput_IsAvailableForStorageOnlyOnTheNextTick()
    {
        Entity storage = CreateStorage(2, 1);
        Entity outgoing = Stored(storage)[0];
        PrepareBeltOutput(storage, outgoing);
        Entity worker = CreateWorker(1, DroneCargoOriginEnum.Recovery);
        Entity cargo = Stored(worker)[0];
        Entity assignment = CreateAssignment(worker, cargo, storage, 1, DroneActionKindEnum.StoreCargo);
        Entity request = Submit(assignment);

        TransferTick();

        AssertResult(request, DroneItemTransferStatusEnum.Unavailable, 0);
        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(outgoing).IsWorldItem);
        Assert.AreEqual(1, Stored(storage).Length);
        AssertStored(worker, new[] { cargo });
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Retargeting, Assignment(assignment).State);

        Entity retryAssignment = CreateAssignment(worker, cargo, storage, 1, DroneActionKindEnum.StoreCargo);
        Entity retry = Submit(retryAssignment);
        TransferTick();

        AssertResult(retry, DroneItemTransferStatusEnum.Completed, 1);
        Assert.AreEqual(2, Stored(storage).Length);
        Assert.AreEqual(storage, _entityManager.GetComponentData<ItemOwnership>(cargo).Owner);
        Assert.AreEqual(0, Stored(worker).Length);
    }

    [Test]
    public void CompetingDeposits_ShareTheOriginalFreeSpaceBudget_DespiteSameTickBeltOutput()
    {
        Entity storage = CreateStorage(1, 1);
        Entity outgoing = Stored(storage)[0];
        PrepareBeltOutput(storage, outgoing);
        Entity firstWorker = CreateWorker(1, DroneCargoOriginEnum.Supply);
        Entity secondWorker = CreateWorker(1, DroneCargoOriginEnum.Supply);
        Entity firstItem = Stored(firstWorker)[0];
        Entity secondItem = Stored(secondWorker)[0];
        Entity firstAssignment = CreateAssignment(firstWorker, firstItem, storage, 1, DroneActionKindEnum.StoreCargo);
        Entity secondAssignment = CreateAssignment(secondWorker, secondItem, storage, 1, DroneActionKindEnum.StoreCargo);
        Entity earlier = Submit(secondAssignment);
        Entity later = Submit(firstAssignment);

        TransferTick();

        AssertResult(earlier, DroneItemTransferStatusEnum.Completed, 1);
        AssertResult(later, DroneItemTransferStatusEnum.Unavailable, 0);
        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(outgoing).IsWorldItem);
        AssertStored(storage, new[] { secondItem });
        AssertStored(firstWorker, new[] { firstItem });
        Assert.AreEqual(0, Stored(secondWorker).Length);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Retargeting, Assignment(firstAssignment).State);
    }

    [Test]
    public void UnavailableItemTypeDeposit_DoesNotConsumeTheOldSpaceBudgetOfTheFollowingCompatibleType()
    {
        Entity storage = CreateStorage(1, 1);
        Entity existingIron = Stored(storage)[0];
        Entity copperWorker = CreateWorker(1, DroneCargoOriginEnum.Supply, ItemTypeEnum.Copper);
        Entity ironWorker = CreateWorker(1, DroneCargoOriginEnum.Supply);
        Entity copper = Stored(copperWorker)[0];
        Entity iron = Stored(ironWorker)[0];
        Entity copperAssignment = CreateAssignment(copperWorker, copper, storage, 1,
            DroneActionKindEnum.StoreCargo, ItemTypeEnum.Copper);
        Entity ironAssignment = CreateAssignment(ironWorker, iron, storage, 1, DroneActionKindEnum.StoreCargo);
        Entity earlier = Submit(copperAssignment);
        Entity later = Submit(ironAssignment);

        TransferTick();

        AssertResult(earlier, DroneItemTransferStatusEnum.Unavailable, 0);
        AssertResult(later, DroneItemTransferStatusEnum.Completed, 1);
        AssertStored(copperWorker, new[] { copper });
        Assert.AreEqual(ItemTypeEnum.Copper, _entityManager.GetComponentData<ItemIdentity>(copper).Type);
        Assert.AreEqual(ItemTypeEnum.Copper, _entityManager.GetBuffer<StoredItemElement>(copperWorker)[0].ItemType);
        AssertStored(storage, new[] { existingIron, iron });
        Assert.AreEqual(0, Stored(ironWorker).Length);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Retargeting, Assignment(copperAssignment).State);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Completed, Assignment(ironAssignment).State);
    }

    [Test]
    public void DecisionAndExecution_LeaveItemsAssignmentAndReservationsUnchangedUntilStateApply()
    {
        Entity source = CreateStorage(3);
        Entity[] originals = Stored(source);
        Entity site = CreateSite(3);
        Entity worker = CreateWorker();
        Entity assignment = CreateAssignment(worker, source, site, 3, DroneActionKindEnum.CollectFromStorage);
        Entity request = Submit(assignment);

        _decision.Update();
        AssertCollectionNotApplied(source, worker, site, assignment, request, originals);
        _execution.Update();
        // 앞선 단계가 원본 변경을 ECB로 우회 기록하지 않았는지도 확인한다.
        _world.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>().Update();
        AssertCollectionNotApplied(source, worker, site, assignment, request, originals);

        _apply.Update();

        AssertResult(request, DroneItemTransferStatusEnum.Completed, 3);
        AssertStored(worker, originals);
        Assert.AreEqual(0, Stored(source).Length);
        Assert.AreEqual(1UL, Assignment(assignment).LastAppliedActionSequence);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.MovingToDestination, Assignment(assignment).State);
    }

    [Test]
    public void ClearanceItemMovedOutsideSiteBeforeRecovery_RemainsWorldItemAndDoesNotEnterCargo()
    {
        CreateSite(1);
        Entity item = CreateItem(Entity.Null);
        Entity storage = CreateStorage(0);
        Entity worker = CreateWorker();
        Entity assignment = CreateAssignment(worker, item, storage, 1, DroneActionKindEnum.RecoverWorldItem);
        Entity taskEntity = Assignment(assignment).Task;
        var task = _entityManager.GetComponentData<DroneLogisticsTask>(taskEntity);
        task.RecoveryReason = DroneRecoveryReasonEnum.SiteClearance;
        _entityManager.SetComponentData(taskEntity, task);
        Entity request = Submit(assignment);
        // 요청 접수 이후 바닥 실물이 현장 점유 범위 밖으로 이동한 현재 상태를 준비한다.
        _entityManager.SetComponentData(item, new GridPosition(new int2(40, 40)));
        _entityManager.SetComponentData(item, LocalTransform.FromPosition(new float3(40f, 40f, 0f)));

        TransferTick();

        AssertResult(request, DroneItemTransferStatusEnum.Unavailable, 0);
        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(item).IsWorldItem);
        Assert.AreEqual(new int2(40, 40), _entityManager.GetComponentData<GridPosition>(item).Value);
        Assert.IsFalse(_entityManager.HasComponent<DisableRendering>(item));
        Assert.AreEqual(0, Stored(worker).Length);
        Assert.AreEqual(0, Stored(storage).Length);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Cancelled, Assignment(assignment).State);
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment);
    }

    [Test]
    public void IncreasedCommonCapacity_DoesNotIncreasePreviouslyAssignedCollectionQuantity()
    {
        Entity source = CreateStorage(3);
        Entity site = CreateSite(3);
        Entity worker = CreateWorker();
        Entity assignment = CreateAssignment(worker, source, site, 1, DroneActionKindEnum.CollectFromStorage);
        _entityManager.SetComponentData(_capacity, new DroneCapacityState { CarryingCapacity = 10 });
        Entity request = Submit(assignment);

        TransferTick();

        AssertResult(request, DroneItemTransferStatusEnum.Completed, 1);
        Assert.AreEqual(1, Stored(worker).Length);
        Assert.AreEqual(2, Stored(source).Length);
        Assert.AreEqual(1, Assignment(assignment).AssignedQuantity);
        Assert.AreEqual(1, Reservation(assignment).RemainingQuantity);
    }

    [Test]
    public void UnconsumedResult_KeepsCancelledAssignmentAlive_UntilResultConsumption()
    {
        Entity source = CreateStorage(0);
        Entity site = CreateSite(1);
        Entity worker = CreateWorker();
        Entity assignment = CreateAssignment(worker, source, site, 1, DroneActionKindEnum.CollectFromStorage);
        Entity request = Submit(assignment);
        TransferTick();
        AssertResult(request, DroneItemTransferStatusEnum.Unavailable, 0);
        CleanupManagement();
        Assert.IsTrue(_entityManager.Exists(assignment), "수행부가 읽지 않은 결과가 있으면 배정을 삭제하지 않는다.");
        Assert.IsTrue(DroneActionRequestUtility.TryConsumeResult(_entityManager, request, out var consumed));
        Assert.AreEqual(DroneItemTransferStatusEnum.Unavailable, consumed.Status);
        Assert.IsFalse(_entityManager.Exists(request));
        Assert.IsFalse(DroneActionRequestUtility.TryConsumeResult(_entityManager, request, out _));

        CleanupManagement();

        Assert.IsFalse(_entityManager.Exists(assignment));
        Assert.AreEqual(0, Requirement(site).ReservedQuantity);
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment);
    }

    private void CleanupManagement()
    {
        // 작업 무효화/정리는 이 경계를 검증하는 테스트에서만 실행한다.
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<DroneTaskDecisionSystem>());
        _reservation.Update();
        TransferTick();
    }

    private void TransferTick()
    {
        // 계획을 준비한 뒤 Ownership/Lifecycle 경계에 인계한다. 추가 Playback으로 같은 틱 새 실물의 가시화를 앞당기지 않는다.
        _decision.Update();
        _execution.Update();
        _apply.Update();
    }

    private void PrepareBeltOutput(Entity storage, Entity item)
    {
        var sync = _world.GetOrCreateSystem<BeltSpatialSyncSystem>();
        int2 outputPosition = new int2(1, 0);
        Entities.CreateBelt(outputPosition, DirectionEnum.Right);
        Simulation.UpdateAndComplete(sync);
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingItemStorageApplySystem>());
        _apply.SortSystems();
        _entityManager.AddComponentData(storage, new BuildingItemOutputDecision(true, item, outputPosition));
        _entityManager.SetComponentEnabled<BuildingItemOutputDecision>(storage, true);
    }

    private void AssertCollectionNotApplied(Entity source, Entity worker, Entity site, Entity assignment,
        Entity request, Entity[] originals)
    {
        AssertStored(source, originals);
        Assert.AreEqual(0, Stored(worker).Length);
        Assert.AreEqual(3, Assignment(assignment).AssignedQuantity);
        Assert.AreEqual(0UL, Assignment(assignment).LastAppliedActionSequence);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.AwaitingCollection, Assignment(assignment).State);
        Assert.AreEqual(assignment, _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment);
        Assert.AreEqual(3, Reservation(assignment).RemainingQuantity);
        Assert.AreEqual(3, Requirement(site).ReservedQuantity);
        Assert.AreEqual(0, Requirement(site).DeliveredQuantity);
        Assert.IsTrue(_entityManager.HasComponent<DroneActionReadyRequest>(request));
        Assert.IsFalse(_entityManager.HasComponent<DroneItemTransferResult>(request));
    }

    private Entity CreateStorage(int count, int slots = 16)
    {
        Entity storage = _entityManager.CreateEntity();
        _entityManager.AddComponentData(storage, new BuildingType(BuildingTypeEnum.Storage));
        _entityManager.AddComponentData(storage, new GridPosition(int2.zero));
        _entityManager.AddComponentData(storage, new Storage(slots));
        _entityManager.AddComponentData(storage, new StorageFilter(StorageFilterMode.AllowAll));
        _entityManager.AddComponentData(storage, new PlacementStamp(1, 0));
        _entityManager.AddBuffer<StoredItemElement>(storage);
        AddItems(storage, count);
        return storage;
    }

    private Entity CreateSite(int required)
    {
        Entity site = _entityManager.CreateEntity();
        _entityManager.AddComponentData(site, new BuildingType(BuildingTypeEnum.ConstructionSite));
        _entityManager.AddComponentData(site, new ConstructionSite(BuildingTypeEnum.Storage));
        _entityManager.AddComponentData(site, new GridPosition(int2.zero));
        _entityManager.AddComponentData(site, new BuildingFootprint(new int2(1, 1)));
        _entityManager.AddComponentData(site, new Direction(DirectionEnum.Up));
        _entityManager.AddComponentData(site, new PlacementStamp(1, 0));
        _entityManager.AddBuffer<StoredItemElement>(site);
        _entityManager.AddBuffer<ConstructionMaterialRequirementElement>(site).Add(
            new ConstructionMaterialRequirementElement(ItemTypeEnum.Iron, required));
        return site;
    }

    private Entity CreateWorker(int cargoCount = 0, DroneCargoOriginEnum origin = DroneCargoOriginEnum.None,
        ItemTypeEnum itemType = ItemTypeEnum.Iron)
    {
        Entity worker = _entityManager.CreateEntity(typeof(DroneWorker));
        _entityManager.AddComponentData(worker, new DroneWorkerObservation
        {
            Position = float3.zero,
            Revision = 1,
            CanAcceptTask = true
        });
        _entityManager.AddComponentData(worker, new DroneWorkerAssignment());
        _entityManager.AddComponentData(worker, new DroneCargoState { Origin = origin });
        _entityManager.AddBuffer<StoredItemElement>(worker);
        AddItems(worker, cargoCount, itemType);
        return worker;
    }

    private void AddItems(Entity owner, int count, ItemTypeEnum itemType = ItemTypeEnum.Iron)
    {
        for (int i = 0; i < count; i++)
        {
            Entity item = CreateItem(owner, itemType);
            _entityManager.GetBuffer<StoredItemElement>(owner).Add(new StoredItemElement(item, itemType, i / 2));
        }
    }

    private Entity CreateItem(Entity owner, ItemTypeEnum itemType = ItemTypeEnum.Iron)
    {
        Entity item = _entityManager.CreateEntity();
        _entityManager.AddComponentData(item, new ItemIdentity(itemType));
        _entityManager.AddComponentData(item, new ItemOwnership(owner));
        _entityManager.AddComponentData(item, new GridPosition(int2.zero));
        _entityManager.AddComponentData(item, new Direction(DirectionEnum.Up));
        _entityManager.AddComponentData(item, LocalTransform.Identity);
        _entityManager.AddComponentData(item, new TransferOwnershipRequest());
        _entityManager.SetComponentEnabled<TransferOwnershipRequest>(item, false);
        _entityManager.AddComponentData(item, new DestroyItemRequest());
        _entityManager.SetComponentEnabled<DestroyItemRequest>(item, false);
        _entityManager.AddComponentData(item, new BeltMovementState(0f));
        _entityManager.SetComponentEnabled<BeltMovementState>(item, false);
        if (owner != Entity.Null) _entityManager.AddComponent<DisableRendering>(item);
        return item;
    }

    private Entity CreateAssignment(Entity worker, Entity source, Entity destination, int quantity, DroneActionKindEnum action,
        ItemTypeEnum itemType = ItemTypeEnum.Iron)
    {
        bool supply = action == DroneActionKindEnum.CollectFromStorage || action == DroneActionKindEnum.SupplyConstructionSite;
        Entity task = _entityManager.CreateEntity(typeof(DroneLogisticsTask));
        _entityManager.SetComponentData(task, new DroneLogisticsTask
        {
            Kind = supply ? DroneLogisticsTaskKindEnum.ConstructionSupply : DroneLogisticsTaskKindEnum.WorldItemRecovery,
            State = DroneLogisticsTaskStateEnum.Open,
            Target = supply ? destination : source,
            ItemType = itemType,
            CreationSequence = 1,
            RecoveryReason = supply ? DroneRecoveryReasonEnum.None : DroneRecoveryReasonEnum.DroneDrop
        });
        Entity assignment = _entityManager.CreateEntity(typeof(DroneTaskAssignment), typeof(ConstructionSupplyReservation));
        _entityManager.SetComponentData(assignment, new DroneTaskAssignment
        {
            Worker = worker,
            Task = task,
            Source = source,
            Destination = destination,
            ItemType = itemType,
            AssignedQuantity = quantity,
            Revision = 1,
            State = action == DroneActionKindEnum.CollectFromStorage || action == DroneActionKindEnum.RecoverWorldItem
                ? DroneTaskAssignmentStateEnum.AwaitingCollection
                : action == DroneActionKindEnum.DropCargo ? DroneTaskAssignmentStateEnum.Retargeting : DroneTaskAssignmentStateEnum.AwaitingDelivery,
            NextAction = action,
            DropPosition = (int2)math.floor(_entityManager.GetComponentData<DroneWorkerObservation>(worker).Position.xy)
        });
        _entityManager.SetComponentData(worker, new DroneWorkerAssignment { Assignment = assignment });
        _entityManager.SetComponentData(assignment, new ConstructionSupplyReservation
        {
            Site = supply ? destination : Entity.Null,
            ItemType = itemType,
            AssignmentRevision = 1,
            RemainingQuantity = supply ? quantity : 0
        });
        if (supply)
        {
            var requirement = Requirement(destination);
            requirement.ReservedQuantity += quantity;
            var requirements = _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(destination);
            requirements[0] = requirement;
        }
        return assignment;
    }

    private Entity Submit(Entity assignment, ulong sequence = 1)
    {
        return DroneActionRequestUtility.Submit(_entityManager, Ready(assignment, sequence));
    }

    private DroneActionReadyRequest Ready(Entity entity, ulong sequence = 1)
    {
        var assignment = Assignment(entity);
        var observation = _entityManager.GetComponentData<DroneWorkerObservation>(assignment.Worker);
        bool collect = assignment.NextAction == DroneActionKindEnum.CollectFromStorage || assignment.NextAction == DroneActionKindEnum.RecoverWorldItem;
        return new DroneActionReadyRequest
        {
            Action = new DroneActionIdentity
            {
                Assignment = entity,
                AssignmentRevision = assignment.Revision,
                Sequence = sequence,
                Worker = assignment.Worker,
                Kind = assignment.NextAction,
                Target = assignment.NextAction == DroneActionKindEnum.DropCargo ? Entity.Null : collect ? assignment.Source : assignment.Destination
            },
            WorkerObservationRevision = observation.Revision,
            WorldPosition = (int2)math.floor(observation.Position.xy)
        };
    }

    private void AssertResult(Entity request, DroneItemTransferStatusEnum status, int count)
    {
        Assert.IsTrue(_entityManager.Exists(request));
        Assert.IsFalse(_entityManager.HasComponent<DroneActionReadyRequest>(request));
        Assert.IsTrue(_entityManager.HasComponent<DroneItemTransferResult>(request));
        var result = _entityManager.GetComponentData<DroneItemTransferResult>(request);
        Assert.AreEqual(status, result.Status);
        Assert.AreEqual(count, result.MovedQuantity);
    }

    private void AssertStored(Entity owner, Entity[] expected)
    {
        CollectionAssert.AreEquivalent(expected, Stored(owner));
        foreach (Entity item in expected)
        {
            Assert.IsTrue(_entityManager.Exists(item));
            Assert.AreEqual(owner, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
            Assert.IsTrue(_entityManager.HasComponent<DisableRendering>(item));
        }
    }

    private void AssertAllItemsStillExist(Entity[] items)
    {
        foreach (Entity item in items) Assert.IsTrue(_entityManager.Exists(item));
    }

    private Entity[] Stored(Entity owner)
    {
        var buffer = _entityManager.GetBuffer<StoredItemElement>(owner, true);
        var result = new Entity[buffer.Length];
        for (int i = 0; i < result.Length; i++) result[i] = buffer[i].ItemEntity;
        return result;
    }

    private ConstructionMaterialRequirementElement Requirement(Entity site) =>
        _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site, true)[0];

    private ConstructionSupplyReservation Reservation(Entity assignment) =>
        _entityManager.GetComponentData<ConstructionSupplyReservation>(assignment);

    private DroneTaskAssignment Assignment(Entity entity) => _entityManager.GetComponentData<DroneTaskAssignment>(entity);
}
