using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;

/// <summary>승인 중단/실제 드론 도착/확인 공개/다음 건물 결과와 취소를 실제 소유 단계로 연결한다.</summary>
public class DroneDemolitionTests : EcsWorldTestFixture
{
    private SystemHandle _command;
    private SystemHandle _building;
    private EndCommandEntityCommandBufferSystem _endCommand;
    private EndBuildingEntityCommandBufferSystem _endBuilding;
    private EndSimulationEntityCommandBufferSystem _end;
    private SimulationPreparationGroup _preparation;
    private DroneDecisionGroup _decision;
    private DroneReservationGroup _reservation;
    private DroneExecutionGroup _execution;
    private DroneStateApplyGroup _apply;
    private Entity _station;
    private double _time;

    public override void SetUp()
    {
        base.SetUp();
        Entities.PrepareSimulationConfiguration();
        CreateGameplayPrefabDatabases();
        TestPrefabDatabaseFactory.CreateDrones(_entityManager);
        Assert.IsTrue(DroneConfigInitializationSystem.Initialize(_entityManager));
        _command = _world.GetOrCreateSystem<BuildingDemolitionCommandSystem>();
        _building = _world.GetOrCreateSystem<BuildingLifecycleApplySystem>();
        _endCommand = _world.GetOrCreateSystemManaged<EndCommandEntityCommandBufferSystem>();
        _endBuilding = _world.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>();
        _end = _world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
        _preparation = _world.GetOrCreateSystemManaged<SimulationPreparationGroup>();
        _decision = _world.GetOrCreateSystemManaged<DroneDecisionGroup>();
        _reservation = _world.GetOrCreateSystemManaged<DroneReservationGroup>();
        _execution = _world.GetOrCreateSystemManaged<DroneExecutionGroup>();
        _apply = _world.GetOrCreateSystemManaged<DroneStateApplyGroup>();
        _preparation.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneWorkerObservationPreparationSystem>());
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneFlightDecisionSystem>());
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskDecisionSystem>());
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneActionRequestDecisionSystem>());
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneItemTransferDecisionSystem>());
        _reservation.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskReservationSystem>());
        _reservation.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneReturnReservationSystem>());
        _execution.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneItemTransferExecutionSystem>());
        _execution.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskExecutionSystem>());
        _execution.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneMovementExecutionSystem>());
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskLifecycleApplySystem>());
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneMovementApplySystem>());
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskAssignmentPublishSystem>());
        _preparation.SortSystems(); _decision.SortSystems(); _reservation.SortSystems();
        _execution.SortSystems(); _apply.SortSystems();
        _station = Entities.CreateStorage(new int2(-10, 0), new int2(1, 1), slotCount: 4);
        _entityManager.SetComponentData(_station, new BuildingType(BuildingTypeEnum.DroneStation, 1));
        _entityManager.AddComponent<DroneStation>(_station);
        _entityManager.AddBuffer<DockedDroneElement>(_station);
        _entityManager.AddComponentData(_station, new DroneStationStorage { DedicatedSlotCount = 2 });
        _entityManager.AddComponentData(_station, new DroneStationNetworkDecision
        {
            RepresentativeStation = _station,
            ActivityBounds = new DroneStationActivityBounds { MinX = -100, MinY = -100, MaxX = 100, MaxY = 100 }
        });
    }

    [Test]
    public void LegacyRequest_StopsBuildingAndWaitsWithoutADrone_AndConsumesOnlyItsOwnTaskResult()
    {
        Entity target = Building(new int2(4, 0));
        Entity item = Entities.CreateStoredItem(target, ItemTypeEnum.Copper);
        Entity request = _entityManager.CreateEntity(typeof(DemolishBuildingRequest));
        _entityManager.SetComponentData(request, new DemolishBuildingRequest(target));
        Tick();
        Assert.IsFalse(_entityManager.Exists(request));
        Assert.IsTrue(_entityManager.Exists(target));
        Entity root = _entityManager.GetComponentData<PendingBuildingDemolition>(target).Task;
        Assert.AreNotEqual(Entity.Null, root);
        for (int i = 0; i < 3; i++) Tick();
        Assert.IsTrue(_entityManager.Exists(target));
        Assert.AreEqual(DroneLogisticsTaskStateEnum.Open, _entityManager.GetComponentData<DroneLogisticsTask>(root).State);
        Assert.AreEqual(target, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
        using var owned = _entityManager.CreateEntityQuery(typeof(BuildingDemolitionAutomaticTaskRequest));
        Assert.AreEqual(0, owned.CalculateEntityCount());
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ActualArrival_ConfirmsAtEndSimulation_AndOnlyTheFollowingEndBuildingRemoves(bool exhaustBattery)
    {
        Entity target = Building(new int2(4, 0));
        Entity worker = Worker(exhaustBattery ? 4f : 100f);
        Entity task = Create(target);
        Entity assignment = Reach(worker);
        if (exhaustBattery) Assert.AreEqual(0f, _entityManager.GetComponentData<DroneBatteryState>(worker).CurrentCharge);
        Tick(endSimulation: false);
        Assert.IsTrue(_entityManager.Exists(target));
        Assert.IsFalse(_entityManager.HasComponent<BuildingDemolitionExecutionRequest>(task));
        Assert.AreEqual(DroneLogisticsTaskStateEnum.Open, _entityManager.GetComponentData<DroneLogisticsTask>(task).State);
        _end.Update();
        Assert.IsTrue(_entityManager.HasComponent<BuildingDemolitionExecutionRequest>(task));
        Assert.AreEqual(DroneLogisticsTaskStateEnum.ExecutionConfirmed, _entityManager.GetComponentData<DroneLogisticsTask>(task).State);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Completed, _entityManager.GetComponentData<DroneTaskAssignment>(assignment).State);
        Assert.IsTrue(_entityManager.Exists(target));
        Tick();
        Assert.IsFalse(_entityManager.Exists(target));
        Assert.AreEqual(BuildingDemolitionResultStatus.Completed, _entityManager.GetComponentData<BuildingDemolitionResult>(task).Status);
        Assert.AreEqual(DroneLogisticsTaskStateEnum.Closed, _entityManager.GetComponentData<DroneLogisticsTask>(task).State);
    }

    [Test]
    public void CancelBeforeConfirmation_StopsActionAndResumesBuildingOnNextCommand()
    {
        Entity target = Building(new int2(4, 0));
        Entity worker = Worker(100f);
        Entity task = Create(target);
        Reach(worker);
        Entity cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, task);
        Tick();
        Assert.AreEqual(DroneTaskRequestStatusEnum.Cancelled, Result(cancel).Status);
        Assert.IsFalse(_entityManager.HasComponent<BuildingDemolitionExecutionRequest>(task));
        Assert.IsTrue(_entityManager.HasComponent<PendingBuildingDemolition>(target));
        Tick();
        Assert.IsTrue(_entityManager.Exists(target));
        Assert.IsFalse(_entityManager.HasComponent<PendingBuildingDemolition>(target));
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment);
    }

    [Test]
    public void CancelAfterPublishedConfirmation_IsRejectedAndDoesNotUndoBuildingRemoval()
    {
        Entity target = Building(new int2(4, 0));
        Entity worker = Worker(100f);
        Entity task = Create(target);
        Reach(worker); Tick();
        Entity cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, task);
        Tick();
        Assert.AreEqual(DroneTaskRequestStatusEnum.Rejected, Result(cancel).Status);
        Assert.IsFalse(_entityManager.Exists(target));
        Assert.AreEqual(BuildingDemolitionResultStatus.Completed, _entityManager.GetComponentData<BuildingDemolitionResult>(task).Status);
    }

    [Test]
    public void DuplicateRequestsAndWorkers_PublishOneRootAndOneAssignment()
    {
        Entity target = Building(new int2(4, 0));
        Worker(100f); Worker(100f);
        Entity first = DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.BuildingDemolition, target, ItemTypeEnum.None);
        Entity second = DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.BuildingDemolition, target, ItemTypeEnum.None);
        Tick();
        var created = Result(first); var existing = Result(second);
        Assert.AreEqual(DroneTaskRequestStatusEnum.Created, created.Status);
        Assert.AreEqual(DroneTaskRequestStatusEnum.Existing, existing.Status);
        Assert.AreEqual(created.Task, existing.Task);
        for (int i = 0; i < 8; i++) Tick();
        using var query = _entityManager.CreateEntityQuery(typeof(DroneTaskAssignment));
        using var assignments = query.ToComponentDataArray<DroneTaskAssignment>(Allocator.Temp);
        int count = 0;
        foreach (var assignment in assignments) if (assignment.Task == created.Task && !DroneSchedulingUtility.IsTerminal(assignment.State)) count++;
        Assert.AreEqual(1, count);
    }

    [TestCase(BuildingTypeEnum.MainFacility)]
    [TestCase(BuildingTypeEnum.DroneStation)]
    [TestCase(BuildingTypeEnum.ConstructionSite)]
    [TestCase(BuildingTypeEnum.None)]
    public void ProtectedOrDeferredBuildingTypes_AreRejectedWithoutStopping(BuildingTypeEnum type)
    {
        Entity target = Building(new int2(4, 0));
        _entityManager.SetComponentData(target, new BuildingType(type, 1));
        Entity request = DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.BuildingDemolition, target, ItemTypeEnum.None);
        Tick();
        Assert.AreEqual(DroneTaskRequestStatusEnum.Rejected, Result(request).Status);
        Assert.IsFalse(_entityManager.HasComponent<PendingBuildingDemolition>(target));
    }

    [Test]
    public void CancelThenRecreate_RelinksTheNewRootAndDoesNotResumeItsPendingBuilding()
    {
        Entity target = Building(new int2(4, 0));
        Entity old = Create(target);
        Entity cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, old);
        Entity create = DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.BuildingDemolition, target, ItemTypeEnum.None);
        Tick();
        Assert.AreEqual(DroneTaskRequestStatusEnum.Cancelled, Result(cancel).Status);
        Entity next = Result(create).Task;
        Assert.AreNotEqual(old, next);
        Tick();
        Assert.AreEqual(next, _entityManager.GetComponentData<PendingBuildingDemolition>(target).Task);
        Assert.IsTrue(_entityManager.Exists(target));
    }

    [Test]
    public void TargetLostAfterConfirmation_ReportsFailureInsteadOfWaitingOrClaimingSuccess()
    {
        Entity target = Building(new int2(4, 0));
        Entity worker = Worker(100f);
        Entity task = Create(target);
        Reach(worker); Tick();
        _entityManager.DestroyEntity(target);
        Tick();
        Assert.AreEqual(BuildingDemolitionResultStatus.Failed, _entityManager.GetComponentData<BuildingDemolitionResult>(task).Status);
        Assert.AreEqual(DroneLogisticsTaskStateEnum.Closed, _entityManager.GetComponentData<DroneLogisticsTask>(task).State);
    }

    [Test]
    public void ReturnedContentsAndRefunds_AreMarkedAndRecoveredWithoutReplacingTheOriginalItem()
    {
        Entity target = Building(new int2(4, 0));
        Entity original = Entities.CreateStoredItem(target, ItemTypeEnum.Copper);
        using (var query = _entityManager.CreateEntityQuery(typeof(BuildingConstructionMaterialElement)))
        {
            var costs = _entityManager.GetBuffer<BuildingConstructionMaterialElement>(query.GetSingletonEntity());
            for (int i = costs.Length - 1; i >= 0; i--)
                if (costs[i].BuildingType == BuildingTypeEnum.Storage && costs[i].Tier == 1) costs.RemoveAt(i);
            costs.Add(new BuildingConstructionMaterialElement
            { BuildingType = BuildingTypeEnum.Storage, Tier = 1, ItemType = ItemTypeEnum.Iron, Quantity = 2 });
        }
        Entity worker = Worker(100f);
        Entity task = Create(target);
        Reach(worker); Tick(); Tick();
        Assert.IsFalse(_entityManager.Exists(target));
        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(original).IsWorldItem);
        Assert.AreEqual(DroneRecoveryReasonEnum.BuildingDemolition, _entityManager.GetComponentData<DroneRecoveryPending>(original).Reason);
        using (var marked = _entityManager.CreateEntityQuery(typeof(DroneRecoveryPending)))
            Assert.AreEqual(3, marked.CalculateEntityCount());
        Assert.AreEqual(BuildingDemolitionResultStatus.Completed, _entityManager.GetComponentData<BuildingDemolitionResult>(task).Status);
        for (int i = 0; i < 40 && _entityManager.GetBuffer<StoredItemElement>(_station).Length < 3; i++) Tick(100f);
        Assert.AreEqual(_station, _entityManager.GetComponentData<ItemOwnership>(original).Owner);
        Assert.AreEqual(1, DroneSchedulingUtility.CountInventory(_entityManager, _station, ItemTypeEnum.Copper));
        Assert.AreEqual(2, DroneSchedulingUtility.CountInventory(_entityManager, _station, ItemTypeEnum.Iron));
        Assert.IsFalse(_entityManager.HasComponent<DroneRecoveryPending>(original));
    }

    [Test]
    public void ExplicitLegacyRequestAfterCancellation_ReapprovesWithoutLosingItsNewRoot()
    {
        Entity target = Building(new int2(4, 0));
        Entity old = Create(target);
        Entity cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, old);
        Tick();
        Assert.AreEqual(DroneTaskRequestStatusEnum.Cancelled, Result(cancel).Status);
        Entity request = _entityManager.CreateEntity(typeof(DemolishBuildingRequest));
        _entityManager.SetComponentData(request, new DemolishBuildingRequest(target));
        Tick();
        Entity next = _entityManager.GetComponentData<PendingBuildingDemolition>(target).Task;
        Assert.AreNotEqual(old, next);
        Assert.AreNotEqual(Entity.Null, next);
        Tick();
        Assert.AreEqual(next, _entityManager.GetComponentData<PendingBuildingDemolition>(target).Task);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void IndestructibleOrPrefab_IsRejectedWithoutPendingState(bool prefab)
    {
        Entity target = Building(new int2(4, 0));
        if (prefab) _entityManager.AddComponent<Prefab>(target);
        else _entityManager.AddComponent<IndestructibleBuilding>(target);
        Entity request = DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.BuildingDemolition, target, ItemTypeEnum.None);
        Tick();
        Assert.AreEqual(DroneTaskRequestStatusEnum.Rejected, Result(request).Status);
        Assert.IsFalse(_entityManager.HasComponent<PendingBuildingDemolition>(target));
    }

    [Test]
    public void LostTargetBeforeArrival_DoesNotConfirmOrReportSuccessfulDemolition()
    {
        Entity target = Building(new int2(4, 0));
        Entity worker = Worker(100f);
        Entity task = Create(target);
        for (int i = 0; i < 12 && _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment == Entity.Null; i++) Tick();
        Assert.AreNotEqual(Entity.Null, _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment);
        _entityManager.DestroyEntity(target);
        Tick();
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment);
        Assert.IsFalse(_entityManager.HasComponent<BuildingDemolitionExecutionRequest>(task));
        Assert.IsFalse(_entityManager.HasComponent<BuildingDemolitionResult>(task));
    }

    [Test]
    public void InvalidDemolitionPayload_DoesNotStopAnOtherwiseValidBuilding()
    {
        Entity target = Building(new int2(4, 0));
        Entity request = DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.BuildingDemolition, target, ItemTypeEnum.Iron, 1);
        Tick();
        Assert.AreEqual(DroneTaskRequestStatusEnum.Rejected, Result(request).Status);
        Assert.IsFalse(_entityManager.HasComponent<PendingBuildingDemolition>(target));
    }

    [Test]
    public void StoredDrone_ActivatesForDemolitionDemandAndKeepsItsIdentity()
    {
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneStationAutomationDecisionSystem>());
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneStationLifecycleDecisionSystem>());
        _reservation.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneStationActivationReservationSystem>());
        _reservation.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneStationLifecycleReservationSystem>());
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneStationLifecycleApplySystem>());
        _decision.SortSystems(); _reservation.SortSystems(); _apply.SortSystems();
        Entity drone = Entities.CreateStoredItem(_station, ItemTypeEnum.Drone);
        Entity target = Building(new int2(4, 0));
        Entity task = Create(target);
        for (int i = 0; i < 16 && !_entityManager.HasComponent<DroneWorker>(drone); i++) Tick();
        Assert.IsTrue(_entityManager.HasComponent<DroneWorker>(drone));
        Entity assignment = Reach(drone);
        Assert.AreEqual(task, _entityManager.GetComponentData<DroneTaskAssignment>(assignment).Task);
        Tick(); Tick();
        Assert.IsFalse(_entityManager.Exists(target));
        Assert.IsTrue(_entityManager.Exists(drone));
    }

    [Test]
    public void RefundPrefabFailure_IsReportedAsFailedAndPreservesTheExistingFatalPolicy()
    {
        Entity target = Building(new int2(4, 0));
        using (var query = _entityManager.CreateEntityQuery(typeof(BuildingConstructionMaterialElement)))
        {
            var costs = _entityManager.GetBuffer<BuildingConstructionMaterialElement>(query.GetSingletonEntity());
            for (int i = costs.Length - 1; i >= 0; i--)
                if (costs[i].BuildingType == BuildingTypeEnum.Storage && costs[i].Tier == 1) costs.RemoveAt(i);
            costs.Add(new BuildingConstructionMaterialElement
            { BuildingType = BuildingTypeEnum.Storage, Tier = 1, ItemType = ItemTypeEnum.Iron, Quantity = 1 });
        }
        Entity worker = Worker(100f);
        Entity task = Create(target);
        Reach(worker); Tick();
        using (var query = _entityManager.CreateEntityQuery(typeof(ItemPrefabDatabase)))
        {
            var prefabs = _entityManager.GetBuffer<ItemPrefabElement>(query.GetSingletonEntity());
            for (int i = prefabs.Length - 1; i >= 0; i--)
                if (prefabs[i].Type == ItemTypeEnum.Iron) prefabs.RemoveAt(i);
        }
        LogAssert.Expect(LogType.Error, new Regex(".*Missing prefab for refund item type.*"));
        Tick();
        Assert.IsFalse(_entityManager.Exists(target));
        Assert.AreEqual(BuildingDemolitionResultStatus.Failed, _entityManager.GetComponentData<BuildingDemolitionResult>(task).Status);
        using var fatal = _entityManager.CreateEntityQuery(typeof(SimulationFatalError));
        Assert.AreEqual(1, fatal.CalculateEntityCount());
    }

    [Test]
    public void BuildingWithoutMovementGeometry_IsRejectedWithoutLeavingAnUnserviceablePendingState()
    {
        Entity target = _entityManager.CreateEntity(typeof(BuildingType));
        _entityManager.SetComponentData(target, new BuildingType(BuildingTypeEnum.Storage, 1));
        Entity request = DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.BuildingDemolition, target, ItemTypeEnum.None);
        Tick();
        Assert.AreEqual(DroneTaskRequestStatusEnum.Rejected, Result(request).Status);
        Assert.IsFalse(_entityManager.HasComponent<PendingBuildingDemolition>(target));
    }

    [TestCase(0UL)]
    [TestCase(ulong.MaxValue)]
    public void InvalidRequestSequence_RejectsLegacyDemolitionBeforeApprovalEffects(ulong next)
    {
        Entity target = Building(new int2(4, 0));
        Entity item = Entities.CreateStoredItem(target, ItemTypeEnum.Iron);
        _entityManager.AddComponent<TransferOwnershipRequest>(item);
        _entityManager.SetComponentEnabled<TransferOwnershipRequest>(item, true);
        _entityManager.AddBuffer<ProductResult>(target).Add(new ProductResult(ItemTypeEnum.Iron));
        SetRequestSequence(next);
        Entity request = LegacyRequest(target);
        Simulation.UpdateAndComplete(_command);
        _endCommand.Update();
        Assert.IsFalse(_entityManager.Exists(request));
        Assert.IsFalse(_entityManager.HasComponent<PendingBuildingDemolition>(target));
        Assert.IsTrue(_entityManager.IsComponentEnabled<TransferOwnershipRequest>(item));
        Assert.AreEqual(1, _entityManager.GetBuffer<ProductResult>(target).Length);
        Assert.AreEqual(target, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
    }

    [Test]
    public void LastAvailableSequence_ApprovesOnlyOneLegacyTargetWithoutStoppingTheRejectedTarget()
    {
        Entity first = Building(new int2(4, 0));
        Entity second = Building(new int2(6, 0));
        SetRequestSequence(ulong.MaxValue - 1);
        LegacyRequest(first);
        LegacyRequest(second);
        Tick();
        int stopped = (_entityManager.HasComponent<PendingBuildingDemolition>(first) ? 1 : 0) +
            (_entityManager.HasComponent<PendingBuildingDemolition>(second) ? 1 : 0);
        Assert.AreEqual(1, stopped);
        using var counter = _entityManager.CreateEntityQuery(typeof(DroneTaskRequestSequence));
        Assert.AreEqual(ulong.MaxValue, counter.GetSingleton<DroneTaskRequestSequence>().NextValue);
    }

    [Test]
    public void PreviouslySubmittedGenericRequest_StillApprovesWhenNoLegacyRequestSequencesRemain()
    {
        Entity legacyTarget = Building(new int2(4, 0));
        Entity genericTarget = Building(new int2(6, 0));
        Entity generic = DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.BuildingDemolition,
            genericTarget, ItemTypeEnum.None);
        SetRequestSequence(ulong.MaxValue);
        LegacyRequest(legacyTarget);
        Tick();
        Assert.AreEqual(DroneTaskRequestStatusEnum.Created, Result(generic).Status);
        Assert.IsTrue(_entityManager.HasComponent<PendingBuildingDemolition>(genericTarget));
        Assert.IsFalse(_entityManager.HasComponent<PendingBuildingDemolition>(legacyTarget));
    }

    [Test]
    public void LegacyAndGenericDuplicate_KeepOneRootAndPreserveTheExternalResult()
    {
        Entity target = Building(new int2(4, 0));
        LegacyRequest(target);
        Entity generic = DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.BuildingDemolition,
            target, ItemTypeEnum.None);
        Tick();
        var result = Result(generic);
        Assert.AreEqual(DroneTaskRequestStatusEnum.Created, result.Status);
        Assert.AreEqual(result.Task, _entityManager.GetComponentData<PendingBuildingDemolition>(target).Task);
        Tick();
        using var tasks = _entityManager.CreateEntityQuery(typeof(DroneLogisticsTask));
        Assert.AreEqual(1, tasks.CalculateEntityCount());
        using var owned = _entityManager.CreateEntityQuery(typeof(BuildingDemolitionAutomaticTaskRequest));
        Assert.AreEqual(0, owned.CalculateEntityCount());
    }

    private void SetRequestSequence(ulong next)
    {
        using var query = _entityManager.CreateEntityQuery(typeof(DroneTaskRequestSequence));
        Entity counter = query.IsEmptyIgnoreFilter ? _entityManager.CreateEntity(typeof(DroneTaskRequestSequence)) : query.GetSingletonEntity();
        _entityManager.SetComponentData(counter, new DroneTaskRequestSequence { NextValue = next });
    }

    private Entity LegacyRequest(Entity target)
    {
        Entity request = _entityManager.CreateEntity(typeof(DemolishBuildingRequest));
        _entityManager.SetComponentData(request, new DemolishBuildingRequest(target));
        return request;
    }

    private Entity Create(Entity target)
    {
        Entity request = DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.BuildingDemolition, target, ItemTypeEnum.None);
        Tick();
        var result = Result(request);
        Assert.AreEqual(DroneTaskRequestStatusEnum.Created, result.Status);
        return result.Task;
    }

    private DroneTaskRequestResult Result(Entity request)
    {
        Assert.IsTrue(DroneTaskRequestUtility.TryConsumeResult(_entityManager, request, out var result));
        return result;
    }

    private Entity Reach(Entity worker)
    {
        Entity assignment = Entity.Null;
        for (int i = 0; i < 16 && assignment == Entity.Null; i++)
        {
            Tick(); assignment = _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment;
        }
        Assert.AreNotEqual(Entity.Null, assignment);
        Tick(100f);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.AwaitingDelivery, _entityManager.GetComponentData<DroneTaskAssignment>(assignment).State);
        return assignment;
    }

    private Entity Building(int2 position) => Entities.CreateStorage(position, new int2(1, 1), slotCount: 2);
    private Entity Worker(float charge)
    {
        Entity worker = _entityManager.CreateEntity(typeof(LocalTransform), typeof(LocalToWorld), typeof(DroneBatteryState));
        var position = new float3(0.5f, 0.5f, 0);
        _entityManager.SetComponentData(worker, LocalTransform.FromPosition(position));
        _entityManager.SetComponentData(worker, new DroneBatteryState { CurrentCharge = charge });
        var ecb = _end.CreateCommandBuffer();
        Assert.IsTrue(DroneSpawnUtility.TryInitializeWorkerRole(_entityManager, ref ecb, worker, position, 100, out var error), error);
        _end.Update();
        return worker;
    }

    private void Tick(float dt = 0f, bool endSimulation = true)
    {
        _time += dt;
        Simulation.SetDeltaTime(dt, _time);
        Simulation.UpdateAndComplete(_command);
        _endCommand.Update();
        _preparation.Update();
        Simulation.UpdateAndComplete(_building);
        _endBuilding.Update();
        _decision.Update();
        _reservation.Update();
        _execution.Update();
        _apply.Update();
        if (endSimulation) _end.Update();
    }
}
