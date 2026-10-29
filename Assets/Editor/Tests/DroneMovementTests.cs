using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>실제 경로 Producer/이동/관측과 기존 작업 관리/EndSimulation을 연결한다. 충전과 픽업/납품 성공은 만들지 않는다.</summary>
public class DroneMovementTests : EcsWorldTestFixture
{
    private SimulationPreparationGroup _preparation;
    private DroneDecisionGroup _decision;
    private DroneReservationGroup _reservation;
    private DroneExecutionGroup _execution;
    private DroneStateApplyGroup _apply;
    private EndSimulationEntityCommandBufferSystem _end;
    private Entity _station;
    private double _time;
    public override void SetUp()
    {
        base.SetUp();
        _time = 0;
        Entities.PrepareSimulationConfiguration();
        CreateGameplayPrefabDatabases();
        Assert.IsTrue(DroneConfigInitializationSystem.Initialize(_entityManager));
        _end = _world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
        _preparation = _world.GetOrCreateSystemManaged<SimulationPreparationGroup>();
        _decision = _world.GetOrCreateSystemManaged<DroneDecisionGroup>();
        _reservation = _world.GetOrCreateSystemManaged<DroneReservationGroup>();
        _execution = _world.GetOrCreateSystemManaged<DroneExecutionGroup>();
        _apply = _world.GetOrCreateSystemManaged<DroneStateApplyGroup>();
        _preparation.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneWorkerObservationPreparationSystem>());
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneFlightDecisionSystem>());
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskDecisionSystem>());
        _reservation.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskReservationSystem>());
        _execution.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskExecutionSystem>());
        _execution.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneMovementExecutionSystem>());
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskLifecycleApplySystem>());
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneMovementApplySystem>());
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskAssignmentPublishSystem>());
        _preparation.SortSystems();
        _decision.SortSystems();
        _reservation.SortSystems();
        _execution.SortSystems();
        _apply.SortSystems();
        _station = Entity.Null;
        _station = Station(new int2(-40, -40), new DroneStationActivityBounds
        {
            MinX = -100, MinY = -100, MaxX = 100, MaxY = 100
        });
    }

    [TestCase(DroneLogisticsTaskKindEnum.StorageImport)]
    [TestCase(DroneLogisticsTaskKindEnum.StorageExport)]
    public void RealRouteAndMovement_ReachRotatedCenter_WithoutPickupOrRepeatedConsumption(DroneLogisticsTaskKindEnum kind)
    {
        Entity source = Storage(new int2(4, 0), new int2(2, 3), DirectionEnum.Right, 3);
        Entity destination = Storage(new int2(8, 0), new int2(1, 1), DirectionEnum.Up, 0);
        Entity worker = Worker(float3.zero, 100f);
        Entity target = kind == DroneLogisticsTaskKindEnum.StorageImport ? destination : source;
        DroneTaskRequestUtility.SubmitCreate(_entityManager, kind, target, ItemTypeEnum.Iron, 3);
        Entity assignment = WaitForAssignment(worker);
        float2 center = new float2(5.5f, 1f);
        float before = _entityManager.GetComponentData<DroneBatteryState>(worker).CurrentCharge;
        TickWithoutCommit(100f);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.MovingToSource, _entityManager.GetComponentData<DroneTaskAssignment>(assignment).State);
        _end.Update();
        Assert.AreEqual(new float3(center, 0), _entityManager.GetComponentData<LocalTransform>(worker).Position);
        Assert.AreEqual(new int2(5, 1), _entityManager.GetComponentData<GridPosition>(worker).Value);
        Assert.AreEqual(before - math.length(center), _entityManager.GetComponentData<DroneBatteryState>(worker).CurrentCharge, 0.0001f);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.AwaitingCollection, _entityManager.GetComponentData<DroneTaskAssignment>(assignment).State);
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(worker).Length);
        Assert.AreEqual(3, DroneSchedulingUtility.CountInventory(_entityManager, source, ItemTypeEnum.Iron));
        Assert.AreEqual(0, DroneSchedulingUtility.CountInventory(_entityManager, destination, ItemTypeEnum.Iron));
        Tick();
        var observation = _entityManager.GetComponentData<DroneWorkerObservation>(worker);
        float charge = _entityManager.GetComponentData<DroneBatteryState>(worker).CurrentCharge;
        Tick();
        Assert.AreEqual(observation.Revision, _entityManager.GetComponentData<DroneWorkerObservation>(worker).Revision);
        Assert.AreEqual(new float3(center, 0), observation.Position);
        Assert.IsFalse(observation.CanAcceptTask);
        Assert.AreEqual(charge, _entityManager.GetComponentData<DroneBatteryState>(worker).CurrentCharge);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ChargingBeforePickup_IsPlanned_AndWaitsWithoutInventingChargeOrReleasingClaim(bool sourceIsStation)
    {
        using var maximum = _entityManager.CreateEntityQuery(typeof(DroneBatteryConfig));
        _entityManager.SetComponentData(maximum.GetSingletonEntity(), new DroneBatteryConfig { MaximumCharge = 10f });
        Entity stop;
        Entity source;
        if (sourceIsStation)
        {
            stop = Station(new int2(4, 0), new DroneStationActivityBounds { MinX = -100, MinY = -100, MaxX = 100, MaxY = 100 });
            _entityManager.SetComponentData(stop, new Storage(2));
            source = stop;
            for (int i = 0; i < 3; i++)
            {
                Entities.CreateStoredItem(source, ItemTypeEnum.Iron);
            }
        }
        else
        {
            stop = Station(new int2(2, 0), new DroneStationActivityBounds { MinX = -100, MinY = -100, MaxX = 100, MaxY = 100 });
            source = Storage(new int2(5, 0), new int2(1, 1), DirectionEnum.Up, 3);
        }
        Entity destination = Storage(new int2(8, 0), new int2(1, 1), DirectionEnum.Up, 0);
        Entity worker = Worker(new float3(0.5f, 0.5f, 0), sourceIsStation ? 4f : 6f);
        Entity request = DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.StorageImport, destination, ItemTypeEnum.Iron, 3);
        Entity assignment = WaitForAssignment(worker);
        Assert.IsTrue(DroneTaskRequestUtility.TryConsumeResult(_entityManager, request, out var response));
        Tick(10f);
        Assert.AreEqual(stop, _entityManager.GetComponentData<DroneMovementState>(worker).ChargingStation);
        float remaining = sourceIsStation ? 0f : 4f;
        for (int i = 0; i < 3; i++)
        {
            Tick();
        }
        Assert.AreEqual(DroneActivityKind.WaitingForCharge, _entityManager.GetComponentData<DroneActivityState>(worker).Activity);
        Assert.AreEqual(stop, _entityManager.GetComponentData<DroneMovementState>(worker).ChargingStation);
        Assert.AreEqual(remaining, _entityManager.GetComponentData<DroneBatteryState>(worker).CurrentCharge, 0.0001f);
        Assert.AreEqual(DroneMovementFailureEnum.None, _entityManager.GetComponentData<DroneMovementResult>(worker).Failure);
        Assert.AreEqual(3, _entityManager.GetComponentData<DroneTransportProgress>(response.Task).ReservedQuantity);
        Assert.AreEqual(assignment, _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment);
        Assert.IsFalse(_entityManager.HasComponent<DroneDocked>(worker));
        // 충전 접속 전 일시적으로 슬롯이 찬 경우에도 실제 중단으로 바꾸지 않는다.
        Entity occupied = Entities.CreateStoredItem(stop, ItemTypeEnum.Iron, sourceIsStation ? 1 : 0);
        Tick();
        Assert.AreEqual(DroneActivityKind.WaitingForCharge, _entityManager.GetComponentData<DroneActivityState>(worker).Activity);
        Assert.AreEqual(stop, _entityManager.GetComponentData<DroneMovementState>(worker).ChargingStation);
        Assert.AreEqual(3, _entityManager.GetComponentData<DroneTransportProgress>(response.Task).ReservedQuantity);
        var items = _entityManager.GetBuffer<StoredItemElement>(stop);
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i].ItemEntity == occupied)
            {
                items.RemoveAt(i);
                break;
            }
        }
        _entityManager.DestroyEntity(occupied);
        // 후속 충전 소유자의 실제 입력을 대역으로 제공한다. 이동 코드가 자동 충전한 값이 아니다.
        _entityManager.SetComponentData(worker, new DroneBatteryState { CurrentCharge = 10f });
        Tick(10f);
        float2 sourcePoint = sourceIsStation ? new float2(4.5f, 0.5f) : new float2(5.5f, 0.5f);
        Assert.AreEqual(new float3(sourcePoint, 0), _entityManager.GetComponentData<LocalTransform>(worker).Position);
        Assert.AreEqual(sourceIsStation ? 10f : 7f, _entityManager.GetComponentData<DroneBatteryState>(worker).CurrentCharge, 0.0001f);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.AwaitingCollection, _entityManager.GetComponentData<DroneTaskAssignment>(assignment).State);
        Assert.AreEqual(3, DroneSchedulingUtility.CountInventory(_entityManager, source, ItemTypeEnum.Iron));
    }

    [Test]
    public void FreeFlight_CrossesUncoveredCellsWithoutCancellingItsAssignment()
    {
        _entityManager.SetComponentData(_station, new GridPosition(0, 0));
        _entityManager.SetComponentData(_station, new DroneStationNetworkDecision
        {
            RepresentativeStation = _station, ActivityBounds = new DroneStationActivityBounds
            {
                MinX = -1, MinY = -1, MaxX = 1, MaxY = 1
            }
        });
        Station(new int2(2, 0), new DroneStationActivityBounds
        {
            MinX = 1, MinY = -1, MaxX = 3, MaxY = 1
        });
        Station(new int2(2, 2), new DroneStationActivityBounds
        {
            MinX = 1, MinY = 1, MaxX = 3, MaxY = 3
        });
        Station(new int2(2, 4), new DroneStationActivityBounds
        {
            MinX = 1, MinY = 3, MaxX = 3, MaxY = 5
        });
        Entity source = Station(new int2(2, 6), new DroneStationActivityBounds
        {
            MinX = 1, MinY = 5, MaxX = 3, MaxY = 7
        });
        for (int i = 0; i < 3; i++)
        {
            Entities.CreateStoredItem(source, ItemTypeEnum.Iron);
        }
        Storage(new int2(3, 5), new int2(1, 1), DirectionEnum.Up, 0);
        Entity worker = Worker(float3.zero, 100f);
        DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.StorageExport, source, ItemTypeEnum.Iron, 3);
        Entity assignment = WaitForAssignment(worker);
        Tick(0.6f);
        int2 cell = _entityManager.GetComponentData<GridPosition>(worker).Value;
        using var query = _entityManager.CreateEntityQuery(typeof(DroneStationNetworkDecision));
        using var networks = query.ToComponentDataArray<DroneStationNetworkDecision>(Allocator.Temp);
        bool covered = false;
        for (int i = 0; i < networks.Length; i++)
        {
            covered |= networks[i].ActivityBounds.Contains(cell);
        }
        Assert.IsFalse(covered);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.MovingToSource, _entityManager.GetComponentData<DroneTaskAssignment>(assignment).State);
        Assert.AreEqual(97.6f, _entityManager.GetComponentData<DroneBatteryState>(worker).CurrentCharge, 0.0001f);
        Tick(100f);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.AwaitingCollection, _entityManager.GetComponentData<DroneTaskAssignment>(assignment).State);
        Assert.AreEqual(3, DroneSchedulingUtility.CountInventory(_entityManager, source, ItemTypeEnum.Iron));
    }

    [Test]
    public void FreeFlight_CrossesAConnectedCoverageHole_AndKeepsCargoAndOriginalAssignment()
    {
        // 연결된 L자 망의 빈 셀을 통과해도 기존 적재품/배정/revision은 유지한다.
        _entityManager.SetComponentData(_station, new GridPosition(0, 0));
        SetNetworkBounds(_station, new DroneStationActivityBounds { MinX = -1, MinY = -1, MaxX = 1, MaxY = 1 });
        Entity first = Station(new int2(2, 0), new DroneStationActivityBounds { MinX = 1, MinY = -1, MaxX = 3, MaxY = 1 });
        Entity second = Station(new int2(4, 0), new DroneStationActivityBounds { MinX = 3, MinY = -1, MaxX = 5, MaxY = 1 });
        Entity third = Station(new int2(4, 2), new DroneStationActivityBounds { MinX = 3, MinY = 1, MaxX = 5, MaxY = 3 });
        Entity destination = Station(new int2(4, 4), new DroneStationActivityBounds { MinX = 3, MinY = 3, MaxX = 5, MaxY = 5 });
        Fill(_station);
        Fill(first);
        Fill(second);
        Fill(third);
        Entity worker = Worker(float3.zero, 100f);
        Entity assignment = RetargetingCargo(worker, 1);
        for (int i = 0; i < 5; i++)
        {
            Tick();
        }
        var selected = _entityManager.GetComponentData<DroneTaskAssignment>(assignment);
        Assert.AreEqual(DroneActionKindEnum.StoreCargo, selected.NextAction);
        Assert.AreEqual(destination, selected.Destination);
        float3 before = _entityManager.GetComponentData<LocalTransform>(worker).Position;
        Tick(0.6f);
        float3 after = _entityManager.GetComponentData<LocalTransform>(worker).Position;
        int2 cell = _entityManager.GetComponentData<GridPosition>(worker).Value;
        Assert.AreEqual(new int2(2, 2), cell);
        Assert.IsFalse(NetworkContains(cell));
        Assert.AreEqual(2.4f, math.distance(before, after), 0.0001f);
        var current = _entityManager.GetComponentData<DroneTaskAssignment>(assignment);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.MovingToDestination, current.State);
        Assert.AreEqual(selected.Revision, current.Revision);
        Assert.AreEqual(1, _entityManager.GetBuffer<StoredItemElement>(worker).Length);
        Assert.AreEqual(assignment, _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment);
    }

    [Test]
    public void EnergyWaiting_PreservesCargo_AndDoesNotBlockAnotherFeasibleWorker()
    {
        using var registry = _entityManager.CreateEntityQuery(typeof(ItemConfigElement));
        int stack = ItemRegistry.GetMaxStack(_entityManager.GetBuffer<ItemConfigElement>(registry.GetSingletonEntity(), true), ItemTypeEnum.Iron);
        for (int i = 0; i < stack; i++)
        {
            Entities.CreateStoredItem(_station, ItemTypeEnum.Iron);
        }
        Storage(new int2(1, 0), new int2(1, 1), DirectionEnum.Up, 3);
        Entity destination = Storage(new int2(5, 0), new int2(1, 1), DirectionEnum.Up, 0);
        Entity loaded = Worker(float3.zero, 1f);
        Entity assignment = RetargetingCargo(loaded, 1);
        Entity idle = Worker(float3.zero, 100f);
        DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.StorageImport, destination, ItemTypeEnum.Iron, 3);
        for (int i = 0; i < 4; i++)
        {
            Tick();
        }
        var pending = _entityManager.GetComponentData<DroneTaskAssignment>(assignment);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Retargeting, pending.State);
        Assert.AreEqual(DroneActionKindEnum.None, pending.NextAction);
        Assert.AreEqual(1u, pending.Revision);
        Assert.AreEqual(1, _entityManager.GetBuffer<StoredItemElement>(loaded).Length);
        Assert.AreNotEqual(Entity.Null, _entityManager.GetComponentData<DroneWorkerAssignment>(idle).Assignment);
    }

    [Test]
    public void ChargingDetours_ContributeToPublishedDistance_AndStopAtTheFirstStation()
    {
        Entity first = Station(new int2(60, 40), new DroneStationActivityBounds { MinX = -100, MinY = -100, MaxX = 200, MaxY = 200 });
        Station(new int2(120, 40), new DroneStationActivityBounds { MinX = -100, MinY = -100, MaxX = 200, MaxY = 200 });
        Storage(new int2(160, 0), new int2(1, 1), DirectionEnum.Up, 3);
        Entity destination = Storage(new int2(180, 0), new int2(1, 1), DirectionEnum.Up, 0);
        Entity worker = Worker(new float3(0.5f, 0.5f, 0), 100f);
        DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.StorageImport, destination, ItemTypeEnum.Iron, 3);
        WaitForAssignment(worker);
        using var query = _entityManager.CreateEntityQuery(typeof(DroneRouteEvaluationResult));
        using var results = query.ToComponentDataArray<DroneRouteEvaluationResult>(Allocator.Temp);
        Assert.AreEqual(1, results.Length);
        float firstLeg = math.length(new float2(60, 40));
        float expected = firstLeg + 60f + math.length(new float2(40, -40)) + 20f;
        Assert.AreEqual(expected, results[0].TotalDistance, 0.001f);
        Tick(100f);
        Assert.AreEqual(first, _entityManager.GetComponentData<DroneMovementState>(worker).ChargingStation);
        Assert.AreEqual(new float3(60.5f, 40.5f, 0), _entityManager.GetComponentData<LocalTransform>(worker).Position);
        Assert.AreEqual(100f - firstLeg, _entityManager.GetComponentData<DroneBatteryState>(worker).CurrentCharge, 0.001f);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ZeroBatteryAtWorkTarget_OnlyRetainsAlreadyConfirmedArrival(bool confirmedArrival)
    {
        Entity destination = Storage(new int2(4, 0), new int2(1, 1), DirectionEnum.Up, 0);
        float3 goal = new float3(4.5f, 0.5f, 0);
        Entity worker = Worker(confirmedArrival ? float3.zero : goal, confirmedArrival ? math.length(goal.xy) : 0f);
        Entity assignmentEntity = RetargetingCargo(worker, 1);
        var assignment = _entityManager.GetComponentData<DroneTaskAssignment>(assignmentEntity);
        assignment.Destination = destination;
        assignment.AssignedQuantity = 1;
        assignment.State = DroneTaskAssignmentStateEnum.MovingToDestination;
        assignment.NextAction = DroneActionKindEnum.StoreCargo;
        _entityManager.SetComponentData(assignmentEntity, assignment);
        if (confirmedArrival)
        {
            Tick(10f);
            Assert.AreEqual(DroneTaskAssignmentStateEnum.AwaitingDelivery, _entityManager.GetComponentData<DroneTaskAssignment>(assignmentEntity).State);
        }
        Tick(0.1f);
        var result = _entityManager.GetComponentData<DroneMovementResult>(worker);
        Assert.AreEqual(0f, _entityManager.GetComponentData<DroneBatteryState>(worker).CurrentCharge);
        Assert.AreEqual(1, _entityManager.GetBuffer<StoredItemElement>(worker).Length);
        Assert.AreEqual(0, DroneSchedulingUtility.CountInventory(_entityManager, destination, ItemTypeEnum.Iron));
        if (confirmedArrival)
        {
            // 이미 확정된 도착은 새 이동이 아니다. 행동 결과를 기다리며 같은 도착을 실패로 되돌리지 않는다.
            Assert.AreEqual(goal, _entityManager.GetComponentData<LocalTransform>(worker).Position);
            Assert.AreEqual(DroneTaskAssignmentStateEnum.AwaitingDelivery, _entityManager.GetComponentData<DroneTaskAssignment>(assignmentEntity).State);
            Assert.IsTrue(result.Arrived);
            Assert.AreEqual(DroneMovementFailureEnum.None, result.Failure);
        }
        else
        {
            // 목표와 같은 좌표라는 이유만으로 0 배터리의 새 도착 신호를 만들지는 않는다.
            Assert.IsFalse(result.Arrived);
            Assert.AreEqual(DroneMovementFailureEnum.EnergyUnavailable, result.Failure);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InvalidGoalWithPositiveBattery_SettlesWithoutStartingNormalReturn(bool disabled)
    {
        Entity source = Storage(new int2(4, 0), new int2(1, 1), DirectionEnum.Up, 3);
        Entity destination = Storage(new int2(8, 0), new int2(1, 1), DirectionEnum.Up, 0);
        Entity worker = Worker(float3.zero, 100f);
        Entity request = DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.StorageImport, destination, ItemTypeEnum.Iron, 3);
        WaitForAssignment(worker);
        Assert.IsTrue(DroneTaskRequestUtility.TryConsumeResult(_entityManager, request, out var response));
        if (disabled)
        {
            _entityManager.AddComponent<Disabled>(source);
        }
        else
        {
            _entityManager.AddComponent<PendingBuildingDemolition>(source);
        }
        Tick(10f);
        Assert.AreEqual(float3.zero, _entityManager.GetComponentData<LocalTransform>(worker).Position);
        Assert.AreEqual(100f, _entityManager.GetComponentData<DroneBatteryState>(worker).CurrentCharge);
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment);
        Assert.AreEqual(0, _entityManager.GetComponentData<DroneTransportProgress>(response.Task).ReservedQuantity);
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<DroneMovementState>(worker).ReturnStation);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EnergyFailure_ReleasesQuotaAndPreservesCargo_WhileReturningOrWaiting(bool noStation)
    {
        Entity near = Station(new int2(2, 0), new DroneStationActivityBounds
        {
            MinX = -100, MinY = -100, MaxX = 100, MaxY = 100
        });
        Entity source = Storage(new int2(4, 0), new int2(1, 1), DirectionEnum.Up, 3);
        Entity destination = Storage(new int2(8, 0), new int2(1, 1), DirectionEnum.Up, 0);
        Entity worker = Worker(new float3(0.5f, 0.5f, 0), 100f);
        Entity request = DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.StorageImport, destination, ItemTypeEnum.Iron, 3);
        Entity assignmentEntity = WaitForAssignment(worker);
        Assert.IsTrue(DroneTaskRequestUtility.TryConsumeResult(_entityManager, request, out var response));
        Entities.CreateStoredItem(worker, ItemTypeEnum.Iron);
        Entities.CreateStoredItem(worker, ItemTypeEnum.Iron);
        _entityManager.SetComponentData(worker, new DroneCargoState
        {
            Origin = DroneCargoOriginEnum.Recovery
        });
        var assignment = _entityManager.GetComponentData<DroneTaskAssignment>(assignmentEntity);
        assignment.State = DroneTaskAssignmentStateEnum.MovingToDestination;
        assignment.NextAction = DroneActionKindEnum.StoreCargo;
        _entityManager.SetComponentData(assignmentEntity, assignment);
        var claim = _entityManager.GetComponentData<DroneTransportReservation>(assignmentEntity);
        var progress = _entityManager.GetComponentData<DroneTransportProgress>(response.Task);
        Assert.IsTrue(DroneTransportReservationUtility.TryReduce(ref progress, ref claim, 2));
        _entityManager.SetComponentData(response.Task, progress);
        _entityManager.SetComponentData(assignmentEntity, claim);
        _entityManager.SetComponentData(worker, new DroneBatteryState
        {
            CurrentCharge = 0
        });
        if (noStation)
        {
            _entityManager.AddComponent<Disabled>(_station);
            _entityManager.AddComponent<Disabled>(near);
        }
        float3 before = _entityManager.GetComponentData<LocalTransform>(worker).Position;
        Tick(0.2f);
        Tick(0.2f);
        float distance = math.distance(before, _entityManager.GetComponentData<LocalTransform>(worker).Position);
        Assert.AreEqual(noStation?0f:0.2f, distance, 0.0001f);
        Assert.AreEqual(0f, _entityManager.GetComponentData<DroneBatteryState>(worker).CurrentCharge);
        Assert.AreEqual(0, _entityManager.GetComponentData<DroneTransportProgress>(response.Task).ReservedQuantity);
        Assert.AreEqual(0, _entityManager.GetComponentData<DroneTransportProgress>(response.Task).DeliveredQuantity);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Retargeting, _entityManager.GetComponentData<DroneTaskAssignment>(assignmentEntity).State);
        Assert.AreEqual(2, _entityManager.GetBuffer<StoredItemElement>(worker).Length);
        Assert.AreEqual(assignmentEntity, _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment);
        Assert.IsFalse(_entityManager.HasComponent<ItemIdentity>(worker));
        Tick(100f);
        Tick();
        Assert.AreEqual(0f, _entityManager.GetComponentData<DroneBatteryState>(worker).CurrentCharge);
        Assert.AreEqual(2, _entityManager.GetBuffer<StoredItemElement>(worker).Length);
        if (!noStation)
        {
            Assert.AreEqual(near, _entityManager.GetComponentData<DroneMovementState>(worker).ChargingStation);
            Assert.AreEqual(DroneActivityKind.WaitingForCharge, _entityManager.GetComponentData<DroneActivityState>(worker).Activity);
        }
    }

    [Test]
    public void DropSearch_UsesNearestExternalCell_AndArrivalDoesNotDropCargo()
    {
        using var registry = _entityManager.CreateEntityQuery(typeof(ItemConfigElement));
        int stack = ItemRegistry.GetMaxStack(_entityManager.GetBuffer<ItemConfigElement>(registry.GetSingletonEntity(), true), ItemTypeEnum.Iron);
        for (int i = 0; i < stack; i++)
        {
            Entities.CreateStoredItem(_station, ItemTypeEnum.Iron);
        }
        Entity site = _entityManager.CreateEntity(typeof(ConstructionSite), typeof(GridPosition), typeof(BuildingFootprint), typeof(Direction));
        _entityManager.SetComponentData(site, new GridPosition(-1, -1));
        _entityManager.SetComponentData(site, new BuildingFootprint(new int2(3, 2)));
        _entityManager.SetComponentData(site, new Direction(DirectionEnum.Right));
        Entity worker = Worker(float3.zero, 100f);
        Entity assignment = RetargetingCargo(worker, 1);
        for (int i = 0; i < 4; i++)
        {
            Tick();
        }
        var current = _entityManager.GetComponentData<DroneTaskAssignment>(assignment);
        Assert.AreEqual(DroneActionKindEnum.DropCargo, current.NextAction);
        Assert.AreEqual(new int2(1, 0), current.DropPosition);
        Tick(10f);
        Assert.AreEqual(new float3(1, 0, 0), _entityManager.GetComponentData<LocalTransform>(worker).Position);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.AwaitingDelivery, _entityManager.GetComponentData<DroneTaskAssignment>(assignment).State);
        Assert.AreEqual(1, _entityManager.GetBuffer<StoredItemElement>(worker).Length);
    }

    [TestCase(DroneLogisticsTaskKindEnum.StorageImport)]
    [TestCase(DroneLogisticsTaskKindEnum.StorageExport)]
    public void AutomaticTransfer_UsesPublishedArrivals_AndPreservesClaimAndItemIdentity(DroneLogisticsTaskKindEnum kind)
    {
        EnableAutomaticActions();
        Entity source = Storage(new int2(4, 0), new int2(2, 3), DirectionEnum.Right, 3);
        Entity destination = Storage(new int2(8, 0), new int2(1, 1), DirectionEnum.Up, 0);
        Entity worker = Worker(float3.zero, 100f);
        Entity target = kind == DroneLogisticsTaskKindEnum.StorageImport ? destination : source;
        DroneTaskRequestUtility.SubmitCreate(_entityManager, kind, target, ItemTypeEnum.Iron, 3);
        Entity assignmentEntity = WaitForAssignment(worker);
        var assignment = _entityManager.GetComponentData<DroneTaskAssignment>(assignmentEntity);
        using var originals = _entityManager.GetBuffer<StoredItemElement>(source).ToNativeArray(Allocator.Temp);
        TickWithoutCommit(100f);
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(worker).Length);
        _end.Update();
        Assert.AreEqual(DroneTaskAssignmentStateEnum.AwaitingCollection,
            _entityManager.GetComponentData<DroneTaskAssignment>(assignmentEntity).State);
        Assert.AreEqual(3, _entityManager.GetBuffer<StoredItemElement>(source).Length);
        TickWithoutCommit(0f);
        Assert.AreEqual(3, _entityManager.GetBuffer<StoredItemElement>(worker).Length);
        using var results = _entityManager.CreateEntityQuery(typeof(DroneAutomaticActionRequest), typeof(DroneItemTransferResult));
        Assert.AreEqual(0, results.CalculateEntityCount(), "행동 결과는 EndSimulation 전에는 공개되지 않는다.");
        _end.Update();
        Assert.AreEqual(1, results.CalculateEntityCount());
        Assert.AreEqual(DroneItemTransferStatusEnum.Completed, results.GetSingleton<DroneItemTransferResult>().Status);
        var current = _entityManager.GetComponentData<DroneTaskAssignment>(assignmentEntity);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.MovingToDestination, current.State);
        Assert.AreEqual(DroneActionKindEnum.StoreCargo, current.NextAction);
        Assert.AreEqual(1UL, current.LastAppliedActionSequence);
        Assert.AreEqual(3, _entityManager.GetComponentData<DroneTransportReservation>(assignmentEntity).RemainingQuantity);
        var progress = _entityManager.GetComponentData<DroneTransportProgress>(assignment.Task);
        Assert.AreEqual(3, progress.ReservedQuantity);
        Assert.AreEqual(0, progress.DeliveredQuantity);
        Assert.AreEqual(3, _entityManager.GetComponentData<DroneTransportRequirement>(assignment.Task).Quantity);
        for (int i = 0; i < originals.Length; i++)
        {
            Assert.AreEqual(worker, _entityManager.GetComponentData<ItemOwnership>(originals[i].ItemEntity).Owner);
        }
        Tick(100f);
        Assert.AreEqual(0, results.CalculateEntityCount(), "자동 요청의 결과만 다음 Decision에서 소비한다.");
        Assert.AreEqual(DroneTaskAssignmentStateEnum.AwaitingDelivery,
            _entityManager.GetComponentData<DroneTaskAssignment>(assignmentEntity).State);
        Assert.AreEqual(3, _entityManager.GetBuffer<StoredItemElement>(worker).Length);
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(destination).Length);
        Tick(0f);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Completed, _entityManager.GetComponentData<DroneTaskAssignment>(assignmentEntity).State);
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(worker).Length);
        Assert.AreEqual(3, _entityManager.GetBuffer<StoredItemElement>(destination).Length);
        Assert.AreEqual(3, _entityManager.GetComponentData<DroneTransportProgress>(assignment.Task).DeliveredQuantity);
        Assert.AreEqual(0, _entityManager.GetComponentData<DroneTransportProgress>(assignment.Task).ReservedQuantity);
        for (int i = 0; i < originals.Length; i++)
        {
            Assert.AreEqual(destination, _entityManager.GetComponentData<ItemOwnership>(originals[i].ItemEntity).Owner);
        }
    }

    [TestCase(DroneLogisticsTaskKindEnum.StorageImport, 0)]
    [TestCase(DroneLogisticsTaskKindEnum.StorageImport, 1)]
    [TestCase(DroneLogisticsTaskKindEnum.StorageExport, 0)]
    [TestCase(DroneLogisticsTaskKindEnum.StorageExport, 1)]
    public void AutomaticPickup_CompetingWorkersReturnOnlyTheirUncollectedTransportQuantity(
        DroneLogisticsTaskKindEnum kind, int available)
    {
        EnableAutomaticActions();
        Entity source = Storage(new int2(4, 0), new int2(1, 1), DirectionEnum.Up, 5);
        Entity destination = Storage(new int2(8, 0), new int2(1, 1), DirectionEnum.Up, 0);
        Entity first = Worker(float3.zero, 100f);
        Entity second = Worker(new float3(0f, 1f, 0f), 100f);
        Entity target = kind == DroneLogisticsTaskKindEnum.StorageImport ? destination : source;
        DroneTaskRequestUtility.SubmitCreate(_entityManager, kind, target, ItemTypeEnum.Iron, 5);
        Entity firstAssignment = WaitForAssignment(first);
        Entity secondAssignment = WaitForAssignment(second);
        Entity task = _entityManager.GetComponentData<DroneTaskAssignment>(firstAssignment).Task;
        Assert.AreEqual(task, _entityManager.GetComponentData<DroneTaskAssignment>(secondAssignment).Task);
        CollectionAssert.AreEquivalent(new[] { 3, 2 }, new[]
        {
            _entityManager.GetComponentData<DroneTransportReservation>(firstAssignment).RemainingQuantity,
            _entityManager.GetComponentData<DroneTransportReservation>(secondAssignment).RemainingQuantity
        });
        Tick(100f);
        var stock = _entityManager.GetBuffer<StoredItemElement>(source);
        while (stock.Length > available)
        {
            Entity removed = stock[stock.Length - 1].ItemEntity;
            stock.RemoveAt(stock.Length - 1);
            _entityManager.DestroyEntity(removed);
            stock = _entityManager.GetBuffer<StoredItemElement>(source);
        }
        Tick(0f);
        Assert.AreEqual(available, _entityManager.GetBuffer<StoredItemElement>(first).Length);
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(second).Length);
        Assert.AreEqual(available, _entityManager.GetComponentData<DroneTransportReservation>(firstAssignment).RemainingQuantity);
        Assert.AreEqual(0, _entityManager.GetComponentData<DroneTransportReservation>(secondAssignment).RemainingQuantity);
        var progress = _entityManager.GetComponentData<DroneTransportProgress>(task);
        Assert.AreEqual(available, progress.ReservedQuantity);
        Assert.AreEqual(0, progress.DeliveredQuantity);
        Assert.AreEqual(5, _entityManager.GetComponentData<DroneTransportRequirement>(task).Quantity);
        Assert.AreEqual(available == 0 ? DroneTaskAssignmentStateEnum.Cancelled : DroneTaskAssignmentStateEnum.MovingToDestination,
            _entityManager.GetComponentData<DroneTaskAssignment>(firstAssignment).State);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Cancelled,
            _entityManager.GetComponentData<DroneTaskAssignment>(secondAssignment).State);
        Assert.AreEqual(available == 0 ? Entity.Null : firstAssignment,
            _entityManager.GetComponentData<DroneWorkerAssignment>(first).Assignment);
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<DroneWorkerAssignment>(second).Assignment);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AutomaticPickup_ConnectsConstructionSupplyAndWorldRecovery(bool recovery)
    {
        EnableAutomaticActions();
        Entity destination;
        if (recovery)
        {
            destination = Storage(new int2(8, 0), new int2(1, 1), DirectionEnum.Up, 0);
            Entity item = Entities.CreateBeltItem(new int2(4, 0), DirectionEnum.Up, 0f, itemType: ItemTypeEnum.Iron);
            _entityManager.SetComponentEnabled<BeltMovementState>(item, false);
            _entityManager.AddComponent<DroneRecoveryPending>(item);
        }
        else
        {
            Storage(new int2(4, 0), new int2(1, 1), DirectionEnum.Up, 3);
            destination = _entityManager.CreateEntity(typeof(BuildingType), typeof(ConstructionSite), typeof(GridPosition),
                typeof(BuildingFootprint), typeof(Direction), typeof(PlacementStamp));
            _entityManager.SetComponentData(destination, new BuildingType(BuildingTypeEnum.ConstructionSite, 0));
            _entityManager.SetComponentData(destination, new ConstructionSite(BuildingTypeEnum.Storage, 1));
            _entityManager.SetComponentData(destination, new GridPosition(10, 0));
            _entityManager.SetComponentData(destination, new BuildingFootprint(new int2(1, 1)));
            _entityManager.SetComponentData(destination, new Direction(DirectionEnum.Up));
            _entityManager.SetComponentData(destination, new PlacementStamp(1, 0));
            _entityManager.AddBuffer<StoredItemElement>(destination);
            _entityManager.AddBuffer<ConstructionMaterialRequirementElement>(destination)
                .Add(new ConstructionMaterialRequirementElement(ItemTypeEnum.Iron, 3));
        }
        Entity worker = Worker(float3.zero, 100f);
        Entity assignment = WaitForAssignment(worker);
        Tick(100f);
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(worker).Length);
        Tick(0f);
        Assert.AreEqual(recovery ? 1 : 3, _entityManager.GetBuffer<StoredItemElement>(worker).Length);
        Assert.AreEqual(recovery ? DroneCargoOriginEnum.Recovery : DroneCargoOriginEnum.Supply,
            _entityManager.GetComponentData<DroneCargoState>(worker).Origin);
        Assert.AreEqual(recovery ? DroneActionKindEnum.StoreCargo : DroneActionKindEnum.SupplyConstructionSite,
            _entityManager.GetComponentData<DroneTaskAssignment>(assignment).NextAction);
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(destination).Length);
        Tick(100f);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.AwaitingDelivery, _entityManager.GetComponentData<DroneTaskAssignment>(assignment).State);
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(destination).Length);
        Tick(0f);
        Assert.AreEqual(DroneTaskAssignmentStateEnum.Completed, _entityManager.GetComponentData<DroneTaskAssignment>(assignment).State);
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(worker).Length);
        Assert.AreEqual(recovery ? 1 : 3, _entityManager.GetBuffer<StoredItemElement>(destination).Length);
    }

    [Test]
    public void AutomaticPickup_LeavesExternalRequestResultWithItsCaller_AndDoesNotDuplicatePickup()
    {
        EnableAutomaticActions();
        Entity source = Storage(new int2(4, 0), new int2(1, 1), DirectionEnum.Up, 3);
        Entity destination = Storage(new int2(8, 0), new int2(1, 1), DirectionEnum.Up, 0);
        Entity worker = Worker(float3.zero, 100f);
        DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.StorageImport, destination, ItemTypeEnum.Iron, 3);
        Entity assignmentEntity = WaitForAssignment(worker);
        Tick(100f);
        // 실제 도착 뒤 다음 Preparation의 관측을 먼저 준비한 외부 수행부 입력이다.
        _preparation.Update();
        var observation = _entityManager.GetComponentData<DroneWorkerObservation>(worker);
        Entity request = DroneActionRequestUtility.Submit(_entityManager, new DroneActionReadyRequest
        {
            Action = new DroneActionIdentity
            {
                Assignment = assignmentEntity, AssignmentRevision = 1, Sequence = 7,
                Worker = worker, Kind = DroneActionKindEnum.CollectFromStorage, Target = source
            },
            WorkerObservationRevision = observation.Revision
        });
        Tick(0f);
        Tick(0f);
        Assert.IsTrue(_entityManager.Exists(request));
        Assert.IsTrue(DroneActionRequestUtility.TryConsumeResult(_entityManager, request, out var result));
        Assert.AreEqual(3, result.MovedQuantity);
        Assert.AreEqual(7UL, _entityManager.GetComponentData<DroneTaskAssignment>(assignmentEntity).LastAppliedActionSequence);
        using var own = _entityManager.CreateEntityQuery(typeof(DroneAutomaticActionRequest));
        Assert.AreEqual(0, own.CalculateEntityCount());
        Assert.AreEqual(3, _entityManager.GetBuffer<StoredItemElement>(worker).Length);
    }

    [Test]
    public void AutomaticPickup_ZeroBatteryBeforeCollectionReturnsClaimWithoutTakingItems()
    {
        EnableAutomaticActions();
        Entity source = Storage(new int2(4, 0), new int2(1, 1), DirectionEnum.Up, 3);
        Entity destination = Storage(new int2(8, 0), new int2(1, 1), DirectionEnum.Up, 0);
        Entity worker = Worker(float3.zero, 100f);
        DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.StorageImport, destination, ItemTypeEnum.Iron, 3);
        Entity assignment = WaitForAssignment(worker);
        Entity task = _entityManager.GetComponentData<DroneTaskAssignment>(assignment).Task;
        Tick(100f);
        _entityManager.SetComponentData(worker, new DroneBatteryState { CurrentCharge = 0f });
        Tick();
        Assert.AreEqual(DroneMovementFailureEnum.EnergyUnavailable, _entityManager.GetComponentData<DroneMovementResult>(worker).Failure);
        Tick();
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(worker).Length);
        Assert.AreEqual(3, _entityManager.GetBuffer<StoredItemElement>(source).Length);
        Assert.AreEqual(0, _entityManager.GetComponentData<DroneTransportProgress>(task).ReservedQuantity);
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment);
    }

    [Test]
    public void AutomaticPickup_MovedWorldTargetRejectsOldArrivalAndSettlesTheAssignment()
    {
        EnableAutomaticActions();
        Storage(new int2(8, 0), new int2(1, 1), DirectionEnum.Up, 0);
        Entities.CreateBelt(new int2(4, 0), DirectionEnum.Up);
        Entities.CreateBelt(new int2(4, 1), DirectionEnum.Up);
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<BeltSpatialSyncSystem>());
        Entity item = Entities.CreateBeltItem(new int2(4, 0), DirectionEnum.Up, 0.9f, itemType: ItemTypeEnum.Iron);
        _entityManager.AddComponent<DroneRecoveryPending>(item);
        SystemHandle beltMovement = _world.GetOrCreateSystem<BeltMovementExecutionSystem>();
        Simulation.UpdateAndComplete(beltMovement);
        Entity worker = Worker(float3.zero, 100f);
        WaitForAssignment(worker);
        Tick(100f);
        // 다음 건물 단계의 실제 벨트 Writer가 수집 목표를 옮긴 뒤 드론 판단을 실행한다.
        _entityManager.SetComponentData(item, new BeltMovementDecision(0.2f));
        Simulation.UpdateAndComplete(beltMovement);
        Assert.AreEqual(new int2(4, 1), _entityManager.GetComponentData<GridPosition>(item).Value);
        Tick(0f);
        Assert.AreEqual(DroneMovementFailureEnum.InvalidGoal, _entityManager.GetComponentData<DroneMovementResult>(worker).Failure);
        Tick(0f);
        Assert.IsTrue(_entityManager.GetComponentData<ItemOwnership>(item).IsWorldItem);
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(worker).Length);
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment);
    }

    private void EnableAutomaticActions()
    {
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneActionRequestDecisionSystem>());
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneItemTransferDecisionSystem>());
        _execution.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneItemTransferExecutionSystem>());
        _decision.SortSystems();
        _execution.SortSystems();
    }

    private void SetNetworkBounds(Entity station, DroneStationActivityBounds bounds)
    {
        _entityManager.SetComponentData(station, new DroneStationNetworkDecision
        {
            RepresentativeStation = _station,
            ActivityBounds = bounds
        });
    }

    private void Fill(Entity storage)
    {
        using var registry = _entityManager.CreateEntityQuery(typeof(ItemConfigElement));
        int maximum = ItemRegistry.GetMaxStack(_entityManager.GetBuffer<ItemConfigElement>(registry.GetSingletonEntity(), true), ItemTypeEnum.Iron);
        for (int i = 0; i < maximum; i++)
        {
            Entities.CreateStoredItem(storage, ItemTypeEnum.Iron);
        }
    }

    private bool NetworkContains(int2 cell)
    {
        using var query = _entityManager.CreateEntityQuery(typeof(DroneStationNetworkDecision));
        using var networks = query.ToComponentDataArray<DroneStationNetworkDecision>(Allocator.Temp);
        for (int i = 0; i < networks.Length; i++)
        {
            if (networks[i].ActivityBounds.Contains(cell))
            {
                return true;
            }
        }
        return false;
    }

    private Entity RetargetingCargo(Entity worker, int quantity)
    {
        Entity item = Entity.Null;
        for (int i = 0; i < quantity; i++)
        {
            item = Entities.CreateStoredItem(worker, ItemTypeEnum.Iron);
        }
        _entityManager.SetComponentData(worker, new DroneCargoState
        {
            Origin = DroneCargoOriginEnum.Recovery
        });
        Entity root = _entityManager.CreateEntity(typeof(DroneLogisticsTask));
        _entityManager.SetComponentData(root, new DroneLogisticsTask
        {
            Kind = DroneLogisticsTaskKindEnum.WorldItemRecovery, State = DroneLogisticsTaskStateEnum.Closed, CreationSequence = 1, ItemType = ItemTypeEnum.Iron, Target = item
        });
        using var sequences = _entityManager.CreateEntityQuery(typeof(DroneTaskSequence));
        _entityManager.SetComponentData(sequences.GetSingletonEntity(), new DroneTaskSequence
        {
            NextValue = 2
        });
        Entity assignment = _entityManager.CreateEntity(typeof(DroneTaskAssignment));
        _entityManager.SetComponentData(assignment, new DroneTaskAssignment
        {
            Worker = worker, Task = root, OriginalTaskCreationSequence = 1, Revision = 1, ItemType = ItemTypeEnum.Iron, State = DroneTaskAssignmentStateEnum.Retargeting
        });
        _entityManager.SetComponentData(worker, new DroneWorkerAssignment
        {
            Assignment = assignment
        });
        return assignment;
    }

    private void Tick(float dt = 0.1f)
    {
        TickWithoutCommit(dt);
        _end.Update();
    }

    private void TickWithoutCommit(float dt)
    {
        _time += dt;
        Simulation.SetDeltaTime(dt, _time);
        _preparation.Update();
        _decision.Update();
        _reservation.Update();
        _execution.Update();
        _apply.Update();
    }

    private Entity WaitForAssignment(Entity worker)
    {
        for (int i = 0; i < 8; i++)
        {
            Tick();
            Entity assignment = _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment;
            if (assignment != Entity.Null)
            {
                return assignment;
            }
        }
        Assert.Fail("Actual route Producer did not publish an assignment.");
        return Entity.Null;
    }

    private Entity Worker(float3 position, float charge)
    {
        Entity worker = _entityManager.CreateEntity(typeof(LocalTransform), typeof(LocalToWorld), typeof(DroneBatteryState));
        _entityManager.SetComponentData(worker, LocalTransform.FromPosition(position));
        _entityManager.SetComponentData(worker, new DroneBatteryState
        {
            CurrentCharge = charge
        });
        using var maximum = _entityManager.CreateEntityQuery(typeof(DroneBatteryConfig));
        var ecb = _end.CreateCommandBuffer();
        Assert.IsTrue(DroneSpawnUtility.TryInitializeWorkerRole(_entityManager, ref ecb, worker, position, maximum.GetSingleton<DroneBatteryConfig>().MaximumCharge, out var error), error);
        _end.Update();
        return worker;
    }

    private Entity Storage(int2 position, int2 size, DirectionEnum direction, int quantity)
    {
        Entity storage = _entityManager.CreateEntity(typeof(BuildingType), typeof(GridPosition), typeof(BuildingFootprint), typeof(Direction), typeof(Storage));
        _entityManager.SetComponentData(storage, new BuildingType(BuildingTypeEnum.Storage, 1));
        _entityManager.SetComponentData(storage, new GridPosition(position));
        _entityManager.SetComponentData(storage, new BuildingFootprint(size));
        _entityManager.SetComponentData(storage, new Direction(direction));
        _entityManager.SetComponentData(storage, new Storage(1));
        _entityManager.AddBuffer<StoredItemElement>(storage);
        for (int i = 0; i < quantity; i++)
        {
            Entities.CreateStoredItem(storage, ItemTypeEnum.Iron);
        }
        return storage;
    }

    private Entity Station(int2 position, DroneStationActivityBounds bounds)
    {
        Entity station = Storage(position, new int2(1, 1), DirectionEnum.Up, 0);
        _entityManager.SetComponentData(station, new BuildingType(BuildingTypeEnum.DroneStation, 1));
        _entityManager.AddComponent<DroneStation>(station);
        _entityManager.AddBuffer<DockedDroneElement>(station);
        _entityManager.AddComponentData(station, new DroneStationNetworkDecision
        {
            RepresentativeStation = _station == Entity.Null?station:_station, ActivityBounds = bounds
        });
        return station;
    }
}
