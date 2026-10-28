using System;
using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>작업 명령/자동 생성 차단/기존 Drone 그룹 및 EndSimulation의 회귀. 실제 이동/충전은 실행하지 않는다.</summary>
public class DroneTaskRequestTests : EcsWorldTestFixture
{
    private GameSimulationGroup _game;
    private DroneSimulationGroup _drone;
    private DroneDecisionGroup _decision;
    private DroneReservationGroup _reservation;
    private DroneExecutionGroup _execution;
    private DroneStateApplyGroup _apply;
    private EndSimulationEntityCommandBufferSystem _end;
    private Entity _candidates;
    public override void SetUp()
    {
        base.SetUp();
        DroneTaskNetworkTestUtility.PrepareWideNetwork(_entityManager);
        CreateGameplayPrefabDatabases();
        Entities.PrepareSimulationConfiguration();
        _candidates = DroneSchedulingUtility.GetOrCreateCandidates(_entityManager);
        _game = _world.GetOrCreateSystemManaged<GameSimulationGroup>();
        _drone = _world.GetOrCreateSystemManaged<DroneSimulationGroup>();
        _decision = _world.GetOrCreateSystemManaged<DroneDecisionGroup>();
        _reservation = _world.GetOrCreateSystemManaged<DroneReservationGroup>();
        _execution = _world.GetOrCreateSystemManaged<DroneExecutionGroup>();
        _apply = _world.GetOrCreateSystemManaged<DroneStateApplyGroup>();
        _end = _world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
        var commit = _world.GetOrCreateSystemManaged<SimulationCommitGroup>();
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskDecisionSystem>());
        _reservation.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskReservationSystem>());
        _execution.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskExecutionSystem>());
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskLifecycleApplySystem>());
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskAssignmentPublishSystem>());
        _drone.AddSystemToUpdateList(_decision);
        _drone.AddSystemToUpdateList(_reservation);
        _drone.AddSystemToUpdateList(_execution);
        _drone.AddSystemToUpdateList(_apply);
        commit.AddSystemToUpdateList(_end);
        _game.AddSystemToUpdateList(_drone);
        _game.AddSystemToUpdateList(commit);
        _decision.SortSystems();
        _reservation.SortSystems();
        _execution.SortSystems();
        _apply.SortSystems();
        _drone.SortSystems();
        commit.SortSystems();
        _game.SortSystems();
    }

    private Entity Site()
    {
        Entity site = _entityManager.CreateEntity();
        _entityManager.AddComponentData(site, new ConstructionSite(BuildingTypeEnum.Storage, 1));
        _entityManager.AddComponentData(site, new BuildingType(BuildingTypeEnum.ConstructionSite, 0));
        _entityManager.AddComponentData(site, new GridPosition(int2.zero));
        _entityManager.AddComponentData(site, new BuildingFootprint(new int2(1, 1)));
        _entityManager.AddComponentData(site, new Direction(DirectionEnum.Up));
        _entityManager.AddComponentData(site, new PlacementStamp(1, 0));
        _entityManager.AddBuffer<StoredItemElement>(site);
        _entityManager.AddBuffer<ConstructionMaterialRequirementElement>(site).Add(new ConstructionMaterialRequirementElement(ItemTypeEnum.Iron, 10));
        return site;
    }

    private Entity Create(Entity target, DroneLogisticsTaskKindEnum kind = DroneLogisticsTaskKindEnum.ConstructionSupply) => DroneTaskRequestUtility.SubmitCreate(_entityManager, kind, target, ItemTypeEnum.Iron);
    private Entity WorldItem(int2 position)
    {
        Entity item = _entityManager.CreateEntity(typeof(ItemIdentity), typeof(ItemOwnership), typeof(GridPosition), typeof(LocalTransform));
        _entityManager.SetComponentData(item, new ItemIdentity(ItemTypeEnum.Iron));
        _entityManager.SetComponentData(item, ItemOwnership.WorldItem);
        _entityManager.SetComponentData(item, new GridPosition(position));
        _entityManager.SetComponentData(item, LocalTransform.FromPosition(new float3(position.x, position.y, 0)));
        return item;
    }

    private DroneTaskRequestResult Result(Entity request)
    {
        Assert.IsTrue(DroneTaskRequestUtility.TryConsumeResult(_entityManager, request, out var result));
        Assert.IsFalse(DroneTaskRequestUtility.TryConsumeResult(_entityManager, request, out _));
        return result;
    }

    private int TaskCount()
    {
        using var query = _entityManager.CreateEntityQuery(typeof(DroneLogisticsTask));
        return query.CalculateEntityCount();
    }

    private Entity Supply(Entity site)
    {
        Entity request = Create(site);
        _game.Update();
        var result = Result(request);
        Assert.AreEqual(DroneTaskRequestStatusEnum.Created, result.Status);
        return result.Task;
    }

    [Test]
    public void CreateAndDuplicates_PublishOneRootAndRemappedResultsAtEndSimulation()
    {
        Entity site = Site();
        Entity first = Create(site);
        Entity second = Create(site);
        _drone.Update();
        Assert.AreEqual(0, TaskCount());
        Assert.IsFalse(DroneTaskRequestUtility.TryConsumeResult(_entityManager, first, out _));
        _end.Update();
        var created = Result(first);
        var duplicate = Result(second);
        Assert.AreEqual(DroneTaskRequestStatusEnum.Created, created.Status);
        Assert.AreEqual(DroneTaskRequestStatusEnum.Existing, duplicate.Status);
        Assert.AreEqual(created.Task, duplicate.Task);
        Assert.IsTrue(_entityManager.Exists(created.Task));
        Assert.AreEqual(1, TaskCount());
        Entity third = Create(site);
        _game.Update();
        Assert.AreEqual(created.Task, Result(third).Task);
        Assert.AreEqual(1, TaskCount());
        using var sequence = _entityManager.CreateEntityQuery(typeof(DroneTaskSequence));
        Assert.AreEqual(2UL, sequence.GetSingleton<DroneTaskSequence>().NextValue);
    }

    [Test]
    public void Cancellation_BlocksAutomaticGenerationUntilExplicitCreate_WithoutChangingSite()
    {
        Entity site = Site();
        Entity task = Supply(site);
        Entity cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, task);
        _drone.Update();
        Assert.IsTrue(_entityManager.Exists(task));
        Assert.AreEqual(0, _entityManager.GetBuffer<DroneTaskGenerationBlockElement>(_candidates).Length);
        _end.Update();
        Assert.AreEqual(DroneTaskRequestStatusEnum.Cancelled, Result(cancel).Status);
        Assert.IsFalse(_entityManager.Exists(task));
        Assert.AreEqual(0, TaskCount());
        Assert.AreEqual(10, _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site)[0].RequiredQuantity);
        _game.Update();
        _game.Update();
        Assert.AreEqual(0, TaskCount());
        Entity recreate = Create(site);
        _game.Update();
        Assert.AreEqual(DroneTaskRequestStatusEnum.Created, Result(recreate).Status);
        Assert.AreEqual(1, TaskCount());
        Assert.AreEqual(0, _entityManager.GetBuffer<DroneTaskGenerationBlockElement>(_candidates).Length);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void CancelCreateOrdering_AndLaterDuplicateCancel_AreDeterministic(bool cancelFirst)
    {
        Entity site = Site();
        Entity task = Supply(site);
        Entity create, cancel;
        if (cancelFirst)
        {
            cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, task);
            create = Create(site);
        }
        else
        {
            create = Create(site);
            cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, task);
        }

        Entity duplicateCancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, task);
        // Query order differs from Sequence order.
        _entityManager.AddComponentData(cancel, new GridPosition(int2.zero));
        _game.Update();
        var createResult = Result(create);
        Assert.AreEqual(cancelFirst ? DroneTaskRequestStatusEnum.Created : DroneTaskRequestStatusEnum.Existing, createResult.Status);
        Assert.AreEqual(DroneTaskRequestStatusEnum.Cancelled, Result(cancel).Status);
        Assert.AreEqual(DroneTaskRequestStatusEnum.Rejected, Result(duplicateCancel).Status);
        Assert.AreEqual(cancelFirst ? 1 : 0, TaskCount());
        _game.Update();
        Assert.AreEqual(cancelFirst ? 1 : 0, TaskCount());
    }

    [Test]
    public void AcceptedCancellation_ExcludesNewPlanningBeforeRootDeletionIsPublished()
    {
        Entity site = Site();
        Entity task = Supply(site);
        Entity storage = Entities.CreateStorage(new int2(2, 0), new int2(1, 1), slotCount: 1);
        Entities.CreateStoredItem(storage, ItemTypeEnum.Iron, 0);
        Entity capacity = _entityManager.CreateEntity(typeof(DroneCapacityState));
        _entityManager.SetComponentData(capacity, new DroneCapacityState { CarryingCapacity = 3 });
        Entity worker = _entityManager.CreateEntity(typeof(DroneWorker), typeof(DroneWorkerObservation), typeof(DroneWorkerAssignment), typeof(StoredItemElement));
        _entityManager.SetComponentData(worker, new DroneWorkerObservation { Position = new float3(2, 0, 0), Revision = 1, CanAcceptTask = true });
        Entity cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, task);
        _decision.Update();
        Assert.IsTrue(_entityManager.Exists(task));
        Assert.AreEqual(0, _entityManager.GetBuffer<DroneRouteDecisionElement>(_candidates).Length);
        Assert.AreEqual(0, _entityManager.GetBuffer<DroneTaskCandidateDecisionElement>(_candidates).Length);
        _reservation.Update();
        _execution.Update();
        _apply.Update();
        _end.Update();
        Assert.AreEqual(DroneTaskRequestStatusEnum.Cancelled, Result(cancel).Status);
        Assert.AreEqual(0, TaskCount());
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CancelledSupplyDestination_DoesNotStallCargoRetargetingToStorage(bool committed)
    {
        Entity site = Site();
        Entity task = Supply(site);
        Entity cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, task);
        if (committed)
        {
            _game.Update();
            Assert.AreEqual(DroneTaskRequestStatusEnum.Cancelled, Result(cancel).Status);
        }
        Entity storage = Entities.CreateStorage(new int2(3, 0), new int2(1, 1), slotCount: 1);
        Entity capacity = _entityManager.CreateEntity(typeof(DroneCapacityState));
        _entityManager.SetComponentData(capacity, new DroneCapacityState { CarryingCapacity = 3 });
        Entity worker = _entityManager.CreateEntity(typeof(DroneWorker), typeof(DroneWorkerObservation),
            typeof(DroneWorkerAssignment), typeof(DroneCargoState), typeof(StoredItemElement));
        _entityManager.SetComponentData(worker, new DroneWorkerObservation { Position = new float3(2, 0, 0), Revision = 1 });
        _entityManager.SetComponentData(worker, new DroneCargoState { Origin = DroneCargoOriginEnum.Supply });
        Entities.CreateStoredItem(worker, ItemTypeEnum.Iron, 0);
        Entity assignment = _entityManager.CreateEntity(typeof(DroneTaskAssignment));
        _entityManager.SetComponentData(assignment, new DroneTaskAssignment
        {
            Worker = worker, ItemType = ItemTypeEnum.Iron, Revision = 1,
            OriginalTaskCreationSequence = 1, State = DroneTaskAssignmentStateEnum.Retargeting
        });
        _entityManager.SetComponentData(worker, new DroneWorkerAssignment { Assignment = assignment });
        _decision.Update();
        var routes = _entityManager.GetBuffer<DroneRouteDecisionElement>(_candidates, true);
        Assert.AreEqual(1, routes.Length);
        Assert.AreEqual(storage, routes[0].Request.Destination);
        Assert.AreEqual(1, _entityManager.GetBuffer<StoredItemElement>(worker).Length);
    }

    [Test]
    public void RejectedLateCreation_DoesNotRemoveCommittedGenerationBlock()
    {
        Entity site = Site();
        Entity task = Supply(site);
        Entity cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, task);
        _game.Update();
        Result(cancel);
        Entity create = Create(site);
        _decision.Update();
        _entityManager.SetComponentData(site, new ConstructionSite(BuildingTypeEnum.Storage, 1, ConstructionSiteFlags.Cancelled));
        _execution.Update();
        _apply.Update();
        _end.Update();
        Assert.AreEqual(DroneTaskRequestStatusEnum.Rejected, Result(create).Status);
        Assert.AreEqual(1, _entityManager.GetBuffer<DroneTaskGenerationBlockElement>(_candidates).Length);
        Assert.AreEqual(0, TaskCount());
    }

    [Test]
    public void ActiveAssignment_RejectsCancellationWithoutChangingAssignmentOrBlockingSource()
    {
        Entity site = Site();
        Entity task = Supply(site);
        Entity worker = _entityManager.CreateEntity(typeof(DroneWorker), typeof(DroneWorkerObservation), typeof(DroneWorkerAssignment), typeof(StoredItemElement));
        Entity source = Entities.CreateStorage(new int2(2, 0), new int2(1, 1), slotCount: 1);
        Entity assignment = _entityManager.CreateEntity(typeof(DroneTaskAssignment));
        _entityManager.SetComponentData(assignment, new DroneTaskAssignment { Task = task, Worker = worker, Source = source, Destination = site, ItemType = ItemTypeEnum.Iron, Revision = 1, State = DroneTaskAssignmentStateEnum.MovingToSource, NextAction = DroneActionKindEnum.CollectFromStorage });
        _entityManager.SetComponentData(worker, new DroneWorkerAssignment { Assignment = assignment });
        Entity cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, task);
        _game.Update();
        Assert.AreEqual(DroneTaskRequestStatusEnum.Busy, Result(cancel).Status);
        Assert.IsTrue(_entityManager.Exists(task));
        Assert.AreEqual(DroneTaskAssignmentStateEnum.MovingToSource, _entityManager.GetComponentData<DroneTaskAssignment>(assignment).State);
        Assert.AreEqual(assignment, _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment);
        Assert.AreEqual(0, _entityManager.GetBuffer<DroneTaskGenerationBlockElement>(_candidates).Length);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnsettledSiteQuantityOrPublication_RejectsCancellation(bool publication)
    {
        Entity site = Site();
        Entity task = Supply(site);
        if (publication)
        {
            _entityManager.GetBuffer<DroneTaskPendingPublicationElement>(_candidates).Add(new DroneTaskPendingPublicationElement { Candidate = new DroneTaskCandidateDecisionElement { Task = task }, Quantity = 1, PublicationQueued = true });
        }
        else
        {
            var buffer = _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site);
            var value = buffer[0];
            value.ReservedQuantity = 1;
            buffer[0] = value;
        }

        Entity cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, task);
        _game.Update();
        Assert.AreEqual(DroneTaskRequestStatusEnum.Busy, Result(cancel).Status);
        Assert.IsTrue(_entityManager.Exists(task));
        Assert.AreEqual(0, _entityManager.GetBuffer<DroneTaskGenerationBlockElement>(_candidates).Length);
    }

    [Test]
    public void LatePublication_RechecksBusyAtLifecycleBeforeAnyCancellationCommit()
    {
        Entity site = Site();
        Entity task = Supply(site);
        Entity cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, task);
        _decision.Update();
        _execution.Update();
        _entityManager.GetBuffer<DroneTaskPendingPublicationElement>(_candidates).Add(new DroneTaskPendingPublicationElement { Candidate = new DroneTaskCandidateDecisionElement { Task = task }, PublicationQueued = true });
        _apply.Update();
        _end.Update();
        Assert.AreEqual(DroneTaskRequestStatusEnum.Busy, Result(cancel).Status);
        Assert.IsTrue(_entityManager.Exists(task));
        Assert.AreEqual(0, _entityManager.GetBuffer<DroneTaskGenerationBlockElement>(_candidates).Length);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Recovery_UsesOnlyObstructionOrDroneDrop_AndCancellationPersists(bool dropped)
    {
        if (!dropped)
        {
            Site();
        }

        Entity item = WorldItem(int2.zero);
        if (dropped)
        {
            _entityManager.AddComponent<DroneRecoveryPending>(item);
        }

        Entity create = Create(item, DroneLogisticsTaskKindEnum.WorldItemRecovery);
        _game.Update();
        Entity task = Result(create).Task;
        Assert.AreEqual(dropped ? DroneRecoveryReasonEnum.DroneDrop : DroneRecoveryReasonEnum.SiteClearance, _entityManager.GetComponentData<DroneLogisticsTask>(task).RecoveryReason);
        Entity cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, task);
        _game.Update();
        Assert.AreEqual(DroneTaskRequestStatusEnum.Cancelled, Result(cancel).Status);
        _game.Update();
        using var tasks = _entityManager.CreateEntityQuery(typeof(DroneLogisticsTask));
        using var values = tasks.ToComponentDataArray<DroneLogisticsTask>(Allocator.Temp);
        foreach (var value in values)
        {
            Assert.AreNotEqual(item, value.Target);
        }

        Assert.IsTrue(_entityManager.Exists(item));
    }

    [Test]
    public void RejectedAndUnsupportedRequests_AreConsumedAndDoNotCreateWork()
    {
        Entity site = Site();
        Entity satisfied = Create(site);
        var buffer = _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site);
        var requirement = buffer[0];
        requirement.DeliveredQuantity = requirement.RequiredQuantity;
        buffer[0] = requirement;
        Entity floorItem = WorldItem(new int2(10, 10));
        Entity ordinaryRecovery = Create(floorItem, DroneLogisticsTaskKindEnum.WorldItemRecovery);
        Entity unsupportedKind = Create(site, (DroneLogisticsTaskKindEnum)200);
        Entity unsupportedOperation = DroneTaskRequestUtility.Submit(_entityManager, new DroneTaskRequest { Operation = (DroneTaskRequestOperationEnum)200 });
        Entity missing = DroneTaskRequestUtility.SubmitCancel(_entityManager, Entity.Null);
        _game.Update();
        Assert.AreEqual(DroneTaskRequestStatusEnum.Rejected, Result(satisfied).Status);
        Assert.AreEqual(DroneTaskRequestStatusEnum.Rejected, Result(ordinaryRecovery).Status);
        Assert.AreEqual(DroneTaskRequestStatusEnum.Unsupported, Result(unsupportedKind).Status);
        Assert.AreEqual(DroneTaskRequestStatusEnum.Unsupported, Result(unsupportedOperation).Status);
        Assert.AreEqual(DroneTaskRequestStatusEnum.Rejected, Result(missing).Status);
        Assert.AreEqual(0, TaskCount());
    }

    [Test]
    public void SuppressionIsPerKey_AndRemovedWhenItsSourceEntityIsDestroyed()
    {
        Entity site = Site();
        Entity task = Supply(site);
        Entity cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, task);
        _game.Update();
        Result(cancel);
        _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site).Add(new ConstructionMaterialRequirementElement(ItemTypeEnum.Copper, 2));
        _game.Update();
        Assert.AreEqual(1, TaskCount());
        _entityManager.DestroyEntity(site);
        _game.Update();
        Assert.AreEqual(0, _entityManager.GetBuffer<DroneTaskGenerationBlockElement>(_candidates).Length);
    }

    [TestCase(0UL)]
    [TestCase(ulong.MaxValue)]
    public void InvalidReceiptSequence_FailsBeforeCreatingRequest(ulong next)
    {
        Entity sequence = _entityManager.CreateEntity(typeof(DroneTaskRequestSequence));
        _entityManager.SetComponentData(sequence, new DroneTaskRequestSequence { NextValue = next });
        Assert.Throws<InvalidOperationException>(() => DroneTaskRequestUtility.SubmitCancel(_entityManager, Entity.Null));
        using var requests = _entityManager.CreateEntityQuery(typeof(DroneTaskRequest));
        Assert.AreEqual(0, requests.CalculateEntityCount());
    }
}
