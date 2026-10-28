using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 기존 배정 fixture가 다루지 않는 일반 운반 요청/계획 전용 경계와 이번 틱 네트워크 필터를 검증한다.
/// 실제 경로 평가/이동 대신 명시적인 결과를 제공하며 목표량 확보와 실물/완료량 변경을 구분한다.
/// </summary>
public class DroneTaskPlanningTests : EcsWorldTestFixture
{
    private SystemHandle _decision;
    private SystemHandle _reservation;
    private SystemHandle _transportReservation;
    private SystemHandle _execution;
    private SystemHandle _lifecycle;
    private SystemHandle _publish;
    private EndSimulationEntityCommandBufferSystem _end;
    private Entity _candidates;
    private Entity _station;

    public override void SetUp()
    {
        base.SetUp();
        Entities.PrepareSimulationConfiguration();
        using var registry = _entityManager.CreateEntityQuery(typeof(ItemConfigElement));
        var configs = _entityManager.GetBuffer<ItemConfigElement>(registry.GetSingletonEntity());
        for (int i = 0; i < configs.Length; i++)
        {
            if (configs[i].ItemType == ItemTypeEnum.Iron)
            {
                configs[i] = new ItemConfigElement(ItemTypeEnum.Iron, 3);
            }
        }
        Entity capacity = _entityManager.CreateEntity(typeof(DroneCapacityState));
        _entityManager.SetComponentData(capacity, new DroneCapacityState { CarryingCapacity = 3 });
        _station = PreparedStation(-20, -20, 20, 20);
        _decision = _world.GetOrCreateSystem<DroneTaskDecisionSystem>();
        _reservation = _world.GetOrCreateSystem<ConstructionSupplyReservationSystem>();
        _transportReservation = _world.GetOrCreateSystem<DroneTransportReservationSystem>();
        _execution = _world.GetOrCreateSystem<DroneTaskExecutionSystem>();
        _lifecycle = _world.GetOrCreateSystem<DroneTaskLifecycleApplySystem>();
        _publish = _world.GetOrCreateSystem<DroneTaskAssignmentPublishSystem>();
        _end = _world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
        _candidates = DroneSchedulingUtility.GetOrCreateCandidates(_entityManager);
    }

    [TestCase(DroneLogisticsTaskKindEnum.StorageImport)]
    [TestCase(DroneLogisticsTaskKindEnum.StorageExport)]
    public void OneShotRequest_PublishesOneRoot_AndPlansOnlyAvailableQuantityWithoutAssignment(DroneLogisticsTaskKindEnum kind)
    {
        Entity source = Storage(new int2(-2, 0), 4);
        Entity destination = Storage(new int2(2, 0), 2);
        Entity worker = Worker(float3.zero);
        Entity target = kind == DroneLogisticsTaskKindEnum.StorageImport ? destination : source;
        Entity request = DroneTaskRequestUtility.SubmitCreate(_entityManager, kind, target, ItemTypeEnum.Iron, 5);
        Entity duplicate = DroneTaskRequestUtility.SubmitCreate(_entityManager, kind, target, ItemTypeEnum.Iron, 9);
        Simulation.UpdateAndComplete(_decision);
        Simulation.UpdateAndComplete(_execution);
        Simulation.UpdateAndComplete(_lifecycle);
        Assert.IsFalse(DroneTaskRequestUtility.TryConsumeResult(_entityManager, request, out _));
        using (var before = _entityManager.CreateEntityQuery(typeof(DroneTransportRequirement)))
        {
            Assert.AreEqual(0, before.CalculateEntityCount());
        }
        _end.Update();
        Entity task = Result(request).Task;
        var duplicateResult = Result(duplicate);
        Assert.AreEqual(DroneTaskRequestStatusEnum.Existing, duplicateResult.Status);
        Assert.AreEqual(task, duplicateResult.Task);
        Assert.AreEqual(5, _entityManager.GetComponentData<DroneTransportRequirement>(task).Quantity);
        Tick();
        Assert.AreEqual(0, Plans().Length, "미평가 경로는 거리/도달 가능성을 만들지 않는다.");
        EvaluateRoutes();
        Tick();
        Assert.AreEqual(1, Plans().Length);
        var plan = Plans()[0].Candidate;
        Assert.AreEqual(worker, plan.Worker);
        Assert.AreEqual(source, plan.Source);
        Assert.AreEqual(destination, plan.Destination);
        Assert.AreEqual(1, plan.Quantity);
        Assert.AreEqual(0, _entityManager.GetBuffer<DroneTaskCandidateDecisionElement>(_candidates).Length);
        Assert.AreEqual(0, _entityManager.GetBuffer<DroneTaskPendingPublicationElement>(_candidates).Length);
        var transportPending = _entityManager.GetBuffer<DroneTransportPendingReservationElement>(_candidates, true);
        Assert.AreEqual(1, transportPending.Length);
        Assert.AreEqual(1, transportPending[0].Reservation.RemainingQuantity);
        var progress = _entityManager.GetComponentData<DroneTransportProgress>(task);
        Assert.AreEqual(0, progress.DeliveredQuantity);
        Assert.AreEqual(1, progress.ReservedQuantity);
        using (var assignments = _entityManager.CreateEntityQuery(typeof(DroneTaskAssignment)))
        {
            Assert.AreEqual(0, assignments.CalculateEntityCount());
        }
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment);
        Assert.AreEqual(4, DroneSchedulingUtility.CountInventory(_entityManager, source, ItemTypeEnum.Iron));
        Assert.AreEqual(2, DroneSchedulingUtility.CountInventory(_entityManager, destination, ItemTypeEnum.Iron));
        Entity filling = Entities.CreateStoredItem(destination, ItemTypeEnum.Iron);
        Tick();
        Assert.AreEqual(0, Plans().Length, "목적지 공간 변경을 다음 판단에 반영한다.");
        _entityManager.GetBuffer<StoredItemElement>(destination).RemoveAt(2);
        _entityManager.DestroyEntity(filling);
        Tick();
        EvaluateRoutes();
        Tick();
        Assert.AreEqual(1, Plans().Length);
        using (var sourceItems = _entityManager.GetBuffer<StoredItemElement>(source).ToNativeArray(Allocator.Temp))
        {
            _entityManager.GetBuffer<StoredItemElement>(source).Clear();
            foreach (var item in sourceItems)
            {
                _entityManager.DestroyEntity(item.ItemEntity);
            }
        }
        Tick();
        Assert.AreEqual(0, Plans().Length, "재고 부족은 완료나 목표량 감소로 처리하지 않는다.");
        Assert.AreEqual(5, _entityManager.GetComponentData<DroneTransportRequirement>(task).Quantity);
        Assert.AreEqual(DroneLogisticsTaskStateEnum.Open, _entityManager.GetComponentData<DroneLogisticsTask>(task).State);
        Entity cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, task);
        Tick();
        Assert.AreEqual(DroneTaskRequestStatusEnum.Cancelled, Result(cancel).Status);
        Assert.IsFalse(_entityManager.Exists(task));
        Assert.AreEqual(0, Plans().Length);
        Assert.AreEqual(0, DroneSchedulingUtility.CountInventory(_entityManager, source, ItemTypeEnum.Iron));
    }

    [Test]
    public void RotatedFootprintPartialOverlap_IsAllowed_AndMovingOutsideWaitsWithoutClosingTask()
    {
        Entity source = Storage(new int2(0, 0), 2);
        Entity destination = Storage(new int2(-22, 0), 0);
        _entityManager.SetComponentData(destination, new BuildingFootprint(new int2(1, 3)));
        _entityManager.SetComponentData(destination, new Direction(DirectionEnum.Right));
        Worker(float3.zero);
        Entity request = DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.StorageImport,
            destination, ItemTypeEnum.Iron, 4);
        Tick();
        Entity task = Result(request).Task;
        Tick();
        EvaluateRoutes();
        Tick();
        Assert.AreEqual(1, Plans().Length, "기준점 밖이어도 회전 footprint 한 셀이 겹치면 포함한다.");
        _entityManager.SetComponentData(destination, new GridPosition(new int2(-23, 0)));
        Tick();
        Assert.AreEqual(0, Plans().Length);
        Assert.AreEqual(DroneLogisticsTaskStateEnum.Open, _entityManager.GetComponentData<DroneLogisticsTask>(task).State);
        Assert.AreEqual(2, DroneSchedulingUtility.CountInventory(_entityManager, source, ItemTypeEnum.Iron));
        _entityManager.DestroyEntity(destination);
        Tick();
        Assert.IsFalse(_entityManager.Exists(task), "대상 소멸은 기존 무효화/수명주기 경계로 정리한다.");
    }

    [Test]
    public void WorkerUsesCurrentNetwork_AndSharedTargetDoesNotBridgeDisconnectedNetworks()
    {
        SetBounds(_station, -4, -4, 4, 4);
        PreparedStation(6, -4, 14, 4);
        Entity leftSource = Storage(new int2(-2, 0), 2);
        Entity rightSource = Storage(new int2(12, 0), 2);
        Entity destination = Storage(new int2(4, 0), 0);
        _entityManager.SetComponentData(destination, new BuildingFootprint(new int2(3, 1)));
        Entity worker = Worker(float3.zero);
        DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.StorageImport, destination, ItemTypeEnum.Iron, 2);
        Tick(); Tick(); EvaluateRoutes(); Tick();
        Assert.AreEqual(leftSource, Plans()[0].Candidate.Source);
        _entityManager.SetComponentData(worker, new DroneWorkerObservation
        {
            Position = new float3(10, 0, 0), Revision = 2, CanAcceptTask = true
        });
        Tick(); EvaluateRoutes(); Tick();
        Assert.AreEqual(1, Plans().Length);
        Assert.AreEqual(rightSource, Plans()[0].Candidate.Source);
        _entityManager.SetComponentData(worker, new DroneWorkerObservation
        {
            Position = new float3(5, 0, 0), Revision = 3, CanAcceptTask = true
        });
        Tick();
        Assert.AreEqual(0, Plans().Length, "네트워크 사이 빈 공간은 활동 영역이 아니다.");
    }

    [Test]
    public void RequestKeyKeepsDirectionAndItem_AndRejectsInvalidQuantityOrUnsupportedBuilding()
    {
        Entity storage = Storage(int2.zero, 0);
        Entity importIron = DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.StorageImport, storage, ItemTypeEnum.Iron, 2);
        Entity importCopper = DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.StorageImport, storage, ItemTypeEnum.Copper, 3);
        Entity exportIron = DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.StorageExport, storage, ItemTypeEnum.Iron, 2);
        Entity zero = DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.StorageImport, storage, ItemTypeEnum.Iron, 0);
        Entity negative = DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.StorageImport, storage, ItemTypeEnum.Iron, -1);
        Entity crafter = Storage(new int2(2, 0), 0);
        _entityManager.SetComponentData(crafter, new BuildingType(BuildingTypeEnum.Crafter, 1));
        Entity unsupportedTarget = DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.StorageImport, crafter, ItemTypeEnum.Iron, 2);
        Tick();
        Assert.AreNotEqual(Result(importIron).Task, Result(importCopper).Task);
        Assert.AreEqual(DroneTaskRequestStatusEnum.Created, Result(exportIron).Status);
        Assert.AreEqual(DroneTaskRequestStatusEnum.Rejected, Result(zero).Status);
        Assert.AreEqual(DroneTaskRequestStatusEnum.Rejected, Result(negative).Status);
        Assert.AreEqual(DroneTaskRequestStatusEnum.Rejected, Result(unsupportedTarget).Status);
        using var tasks = _entityManager.CreateEntityQuery(typeof(DroneTransportRequirement));
        Assert.AreEqual(3, tasks.CalculateEntityCount());
    }

    [Test]
    public void CurrentStationDecision_AdmitsNewBridgeBeforePublicNetworkIsUpdated()
    {
        _entityManager.DestroyEntity(_station);
        Entities.PrepareBuildingConfiguration(BuildingTypeEnum.DroneStation);
        using var configQuery = _entityManager.CreateEntityQuery(typeof(BuildingConfigElement));
        var configs = _entityManager.GetBuffer<BuildingConfigElement>(configQuery.GetSingletonEntity());
        for (int i = 0; i < configs.Length; i++)
        {
            if (configs[i].BuildingType == BuildingTypeEnum.DroneStation)
            {
                var config = configs[i];
                config.DroneStationRangeInChunks = 0;
                configs[i] = config;
            }
        }
        Entity first = ActualStation(int2.zero, 1);
        Entity last = ActualStation(new int2(4, 0), 2);
        Entity source = Storage(new int2(0, 1), 2);
        Entity destination = Storage(new int2(5, 1), 0);
        Worker(new float3(1, 1, 0));
        var networkSystem = _world.GetOrCreateSystem<DroneStationNetworkDecisionSystem>();
        Simulation.UpdateAndComplete(networkSystem);
        DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.StorageImport, destination, ItemTypeEnum.Iron, 2);
        Tick(); Tick();
        Assert.AreEqual(0, Plans().Length);
        ActualStation(new int2(2, 0), 3);
        Simulation.UpdateAndComplete(networkSystem);
        Assert.AreEqual(first, _entityManager.GetComponentData<DroneStationNetworkDecision>(last).RepresentativeStation);
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<DroneStationNetwork>(last).RepresentativeStation);
        Tick(); EvaluateRoutes(); Tick();
        Assert.AreEqual(1, Plans().Length);
        Assert.AreEqual(source, Plans()[0].Candidate.Source);
        _entityManager.AddComponent<Disabled>(first);
        Simulation.UpdateAndComplete(networkSystem);
        Tick();
        Assert.AreEqual(0, Plans().Length);
    }

    [TestCase(DroneLogisticsTaskKindEnum.StorageImport)]
    [TestCase(DroneLogisticsTaskKindEnum.StorageExport)]
    public void MultipleWorkers_ShareOneGoal_AndUnpublishedAmountsAreReturnedBeforeReplanning(DroneLogisticsTaskKindEnum kind)
    {
        Entity source = Storage(new int2(-2, 0), 6);
        Entity destination = Storage(new int2(2, 0), 0);
        Worker(float3.zero);
        Worker(new float3(0, 1, 0));
        Entity target = kind == DroneLogisticsTaskKindEnum.StorageImport ? destination : source;
        Entity request = DroneTaskRequestUtility.SubmitCreate(_entityManager, kind, target, ItemTypeEnum.Iron, 5);
        Tick();
        Entity task = Result(request).Task;
        Tick();
        EvaluateRoutes();
        Tick();
        var pending = _entityManager.GetBuffer<DroneTransportPendingReservationElement>(_candidates);
        Assert.AreEqual(2, pending.Length);
        Assert.AreEqual(3, pending[0].Reservation.RemainingQuantity);
        Assert.AreEqual(2, pending[1].Reservation.RemainingQuantity);
        Assert.AreNotEqual(pending[0].Candidate.Worker, pending[1].Candidate.Worker);
        Assert.AreEqual(5, _entityManager.GetComponentData<DroneTransportProgress>(task).ReservedQuantity);
        Assert.AreEqual(6, DroneSchedulingUtility.CountInventory(_entityManager, source, ItemTypeEnum.Iron));

        // 후속 배차 소비자가 승인 한 건을 인계받는 경계만 대역으로 호출한다. 실제 드론 배정은 생성하지 않는다.
        var first = pending[0];
        var activeReservation = DroneTransportReservationUtility.Take(ref first);
        pending[0] = first;
        Entity active = _entityManager.CreateEntity(typeof(DroneTransportReservation));
        _entityManager.SetComponentData(active, activeReservation);
        Tick();
        pending = _entityManager.GetBuffer<DroneTransportPendingReservationElement>(_candidates);
        Assert.AreEqual(1, pending.Length);
        Assert.AreEqual(2, pending[0].Reservation.RemainingQuantity);
        Assert.AreEqual(5, _entityManager.GetComponentData<DroneTransportProgress>(task).ReservedQuantity);

        var progress = _entityManager.GetComponentData<DroneTransportProgress>(task);
        activeReservation = _entityManager.GetComponentData<DroneTransportReservation>(active);
        Assert.IsTrue(DroneTransportReservationUtility.TryReduce(ref progress, ref activeReservation, 1));
        _entityManager.SetComponentData(task, progress);
        _entityManager.SetComponentData(active, activeReservation);
        Tick();
        pending = _entityManager.GetBuffer<DroneTransportPendingReservationElement>(_candidates, true);
        Assert.AreEqual(4, pending[0].Reservation.RemainingQuantity + pending[1].Reservation.RemainingQuantity);
        progress = _entityManager.GetComponentData<DroneTransportProgress>(task);
        Assert.AreEqual(5, progress.ReservedQuantity);
        Assert.AreEqual(0, progress.DeliveredQuantity);
        DroneTransportReservationUtility.Release(ref progress, ref activeReservation);
        DroneTransportReservationUtility.Release(ref progress, ref activeReservation);
        Assert.AreEqual(4, progress.ReservedQuantity);
        _entityManager.SetComponentData(task, progress);
        _entityManager.SetComponentData(active, activeReservation);
        Entity cancel = DroneTaskRequestUtility.SubmitCancel(_entityManager, task);
        Tick();
        Assert.AreEqual(DroneTaskRequestStatusEnum.Cancelled, Result(cancel).Status);
        Assert.IsFalse(_entityManager.Exists(task));
        Assert.AreEqual(0, _entityManager.GetBuffer<DroneTransportPendingReservationElement>(_candidates).Length);
    }

    [Test]
    public void StorageSuccess_CreditsOnlyItsReservationRoot_AndPickupDoesNotCompleteEitherDirection()
    {
        Entity source = Storage(new int2(-2, 0), 6);
        Entity destination = Storage(new int2(2, 0), 0);
        Entity worker = Worker(float3.zero);
        Entity importRequest = DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.StorageImport,
            destination, ItemTypeEnum.Iron, 5);
        Entity exportRequest = DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.StorageExport,
            source, ItemTypeEnum.Iron, 5);
        Tick();
        Entity importTask = Result(importRequest).Task;
        Entity exportTask = Result(exportRequest).Task;
        Tick();
        EvaluateRoutes();
        Tick();
        var pending = _entityManager.GetBuffer<DroneTransportPendingReservationElement>(_candidates);
        int importIndex = pending[0].Candidate.Task == importTask ? 0 : 1;
        var approval = pending[importIndex];
        var reservation = DroneTransportReservationUtility.Take(ref approval);
        pending[importIndex] = approval;
        Assert.AreEqual(0, DroneTransportReservationUtility.Take(ref approval).RemainingQuantity);
        var progress = _entityManager.GetComponentData<DroneTransportProgress>(importTask);
        int collected = MoveStored(source, worker, 2);
        Assert.AreEqual(2, collected);
        Assert.IsTrue(DroneTransportReservationUtility.TryReduce(ref progress, ref reservation, collected));
        Assert.AreEqual(0, progress.DeliveredQuantity);
        Assert.AreEqual(0, _entityManager.GetComponentData<DroneTransportProgress>(exportTask).DeliveredQuantity);
        Entities.CreateStoredItem(destination, ItemTypeEnum.Iron);
        Entities.CreateStoredItem(destination, ItemTypeEnum.Iron);
        int stored = MoveStored(worker, destination, collected);
        Assert.AreEqual(1, stored);
        Assert.IsTrue(DroneTransportReservationUtility.TryRecordDelivery(ref progress, ref reservation, stored));
        Assert.AreEqual(1, progress.DeliveredQuantity);
        Assert.AreEqual(1, progress.ReservedQuantity);
        Assert.AreEqual(1, reservation.RemainingQuantity);
        Assert.IsFalse(DroneTransportReservationUtility.TryRecordDelivery(ref progress, ref reservation, 2));
        Assert.IsFalse(DroneTransportReservationUtility.TryReduce(ref progress, ref reservation, 2));
        DroneTransportReservationUtility.Release(ref progress, ref reservation);
        _entityManager.SetComponentData(importTask, progress);
        var otherProgress = _entityManager.GetComponentData<DroneTransportProgress>(exportTask);
        Assert.AreEqual(0, otherProgress.DeliveredQuantity);
        Assert.AreEqual(3, otherProgress.ReservedQuantity);
        Assert.AreEqual(1, _entityManager.GetBuffer<StoredItemElement>(worker, true).Length);
        Assert.AreEqual(3, DroneSchedulingUtility.CountInventory(_entityManager, destination, ItemTypeEnum.Iron));
        Assert.AreEqual(5, _entityManager.GetComponentData<DroneTransportRequirement>(importTask).Quantity);
    }

    private int MoveStored(Entity source, Entity destination, int maximum)
    {
        using var registryQuery = _entityManager.CreateEntityQuery(typeof(ItemRegistry));
        Entity registry = registryQuery.GetSingletonEntity();
        int2 position = _entityManager.HasComponent<GridPosition>(destination)
            ? _entityManager.GetComponentData<GridPosition>(destination).Value : int2.zero;
        using var ecb = new EntityCommandBuffer(Allocator.Temp);
        int moved = 0;
        while (moved < maximum)
        {
            var items = _entityManager.GetBuffer<StoredItemElement>(source, true);
            if (items.IsEmpty)
            {
                break;
            }
            int slot = _entityManager.HasComponent<Storage>(destination)
                ? DroneSchedulingUtility.FindStorageSlot(_entityManager, destination, ItemTypeEnum.Iron, registry) : 0;
            if (slot < 0)
            {
                break;
            }
            if (!ItemOwnershipApplySystem.TryTransferItem(_entityManager, items[0].ItemEntity, ItemTypeEnum.Iron,
                source, destination, slot, position, ecb))
            {
                break;
            }
            moved++;
        }
        ecb.Playback(_entityManager);
        return moved;
    }

    private Entity PreparedStation(long minX, long minY, long maxX, long maxY)
    {
        Entity station = _entityManager.CreateEntity(typeof(DroneStation), typeof(DroneStationNetworkDecision));
        SetBounds(station, minX, minY, maxX, maxY);
        return station;
    }

    private void SetBounds(Entity station, long minX, long minY, long maxX, long maxY)
    {
        _entityManager.SetComponentData(station, new DroneStationNetworkDecision
        {
            RepresentativeStation = station,
            ActivityBounds = new DroneStationActivityBounds { MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY }
        });
    }

    private Entity ActualStation(int2 position, ulong stamp)
    {
        Entity station = Entities.CreateBuilding(BuildingTypeEnum.DroneStation, position, new int2(3, 3));
        _entityManager.AddComponent<DroneStation>(station);
        _entityManager.AddComponent<DroneStationNetworkDecision>(station);
        _entityManager.AddComponent<DroneStationNetwork>(station);
        _entityManager.AddComponentData(station, new PlacementStamp(stamp, 0));
        return station;
    }

    private Entity Storage(int2 position, int count)
    {
        Entity storage = Entities.CreateStorage(position, new int2(1, 1), slotCount: count > 3 ? 2 : 1);
        _entityManager.AddComponentData(storage, new PlacementStamp(1, 0));
        for (int i = 0; i < count; i++)
        {
            Entities.CreateStoredItem(storage, ItemTypeEnum.Iron, i / 3);
        }
        return storage;
    }

    private Entity Worker(float3 position)
    {
        Entity worker = _entityManager.CreateEntity(typeof(DroneWorker), typeof(DroneWorkerObservation),
            typeof(DroneWorkerAssignment), typeof(DroneCargoState), typeof(StoredItemElement));
        _entityManager.SetComponentData(worker, new DroneWorkerObservation { Position = position, Revision = 1, CanAcceptTask = true });
        return worker;
    }

    private void Tick()
    {
        Simulation.UpdateAndComplete(_decision);
        Simulation.UpdateAndComplete(_reservation);
        Simulation.UpdateAndComplete(_transportReservation);
        Simulation.UpdateAndComplete(_execution);
        Simulation.UpdateAndComplete(_lifecycle);
        Simulation.UpdateAndComplete(_publish);
        _end.Update();
    }

    private void EvaluateRoutes()
    {
        using var query = _entityManager.CreateEntityQuery(typeof(DroneRouteEvaluationRequest));
        using var routes = query.ToEntityArray(Allocator.Temp);
        foreach (Entity route in routes)
        {
            var request = _entityManager.GetComponentData<DroneRouteEvaluationRequest>(route);
            var result = new DroneRouteEvaluationResult
            {
                EvaluationRevision = request.EvaluationRevision,
                Status = DroneRouteEvaluationStatusEnum.Reachable,
                TotalDistance = 10
            };
            if (_entityManager.HasComponent<DroneRouteEvaluationResult>(route))
            {
                _entityManager.SetComponentData(route, result);
            }
            else
            {
                _entityManager.AddComponentData(route, result);
            }
        }
    }

    private DynamicBuffer<DroneTransportPlanDecisionElement> Plans() =>
        _entityManager.GetBuffer<DroneTransportPlanDecisionElement>(_candidates, true);

    private DroneTaskRequestResult Result(Entity request)
    {
        Assert.IsTrue(DroneTaskRequestUtility.TryConsumeResult(_entityManager, request, out var result));
        return result;
    }
}
