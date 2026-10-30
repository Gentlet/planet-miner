using System;
using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>실제 전력 배분과 정박/충전/재배차의 틱 경계를 연결한다.</summary>
public class DroneChargingTests : EcsWorldTestFixture
{
    private SimulationPreparationGroup _preparation;
    private DroneDecisionGroup _decision;
    private DroneReservationGroup _reservation;
    private DroneExecutionGroup _execution;
    private DroneStateApplyGroup _apply;
    private EndSimulationEntityCommandBufferSystem _end;
    private double _time;
    private PowerSimulationGroup _power;
    private bool _hasProductionLoad;
    private SystemHandle _loadDecision;
    private SystemHandle _loadExecution;
    private SystemHandle _loadApply;

    public override void SetUp()
    {
        base.SetUp();
        _time = 0;
        _hasProductionLoad = false;
        Entities.PrepareSimulationConfiguration();
        Entities.PrepareBuildingConfiguration(BuildingTypeEnum.MainFacility, BuildingTypeEnum.DroneStation,
            BuildingTypeEnum.PowerPole);
        using (var registry = _entityManager.CreateEntityQuery(typeof(ItemRegistry)))
        {
            var items = _entityManager.GetBuffer<ItemConfigElement>(registry.GetSingletonEntity());
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].ItemType == ItemTypeEnum.Drone)
                    items[i] = new ItemConfigElement(ItemTypeEnum.Drone, 50);
            }
        }
        CreateGameplayPrefabDatabases();
        TestPrefabDatabaseFactory.CreateDrones(_entityManager);
        Assert.IsTrue(DroneConfigInitializationSystem.Initialize(_entityManager));
        _power = _world.GetOrCreateSystemManaged<PowerSimulationGroup>();
        var powerDecision = _world.GetOrCreateSystemManaged<PowerDecisionGroup>();
        var powerReservation = _world.GetOrCreateSystemManaged<PowerReservationGroup>();
        var powerExecution = _world.GetOrCreateSystemManaged<PowerExecutionGroup>();
        var powerApply = _world.GetOrCreateSystemManaged<PowerStateApplyGroup>();
        powerDecision.AddSystemToUpdateList(_world.GetOrCreateSystem<PowerGridConnectionDecisionSystem>());
        powerDecision.AddSystemToUpdateList(_world.GetOrCreateSystem<PowerSupplyDecisionSystem>());
        powerReservation.AddSystemToUpdateList(_world.GetOrCreateSystem<PowerGridTopologyReservationSystem>());
        powerReservation.AddSystemToUpdateList(_world.GetOrCreateSystem<PowerBudgetReservationSystem>());
        powerExecution.AddSystemToUpdateList(_world.GetOrCreateSystem<PowerSupplyExecutionSystem>());
        powerApply.AddSystemToUpdateList(_world.GetOrCreateSystem<PowerGridTopologyApplySystem>());
        _power.AddSystemToUpdateList(powerDecision);
        _power.AddSystemToUpdateList(powerReservation);
        _power.AddSystemToUpdateList(powerExecution);
        _power.AddSystemToUpdateList(powerApply);
        _power.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<EndPowerEntityCommandBufferSystem>());
        powerDecision.SortSystems();
        powerReservation.SortSystems();
        powerExecution.SortSystems();
        powerApply.SortSystems();
        _power.SortSystems();
        _end = _world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
        _preparation = _world.GetOrCreateSystemManaged<SimulationPreparationGroup>();
        _decision = _world.GetOrCreateSystemManaged<DroneDecisionGroup>();
        _reservation = _world.GetOrCreateSystemManaged<DroneReservationGroup>();
        _execution = _world.GetOrCreateSystemManaged<DroneExecutionGroup>();
        _apply = _world.GetOrCreateSystemManaged<DroneStateApplyGroup>();
        _preparation.AddSystemToUpdateList(_world.GetOrCreateSystem<PowerSupplyResetPreparationSystem>());
        _preparation.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneChargingPreparationSystem>());
        _preparation.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingPowerDemandPreparationSystem>());
        _preparation.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneWorkerObservationPreparationSystem>());
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneStationNetworkDecisionSystem>());
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneFlightDecisionSystem>());
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskDecisionSystem>());
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneActionRequestDecisionSystem>());
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneItemTransferDecisionSystem>());
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneStationAutomationDecisionSystem>());
        _decision.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneStationLifecycleDecisionSystem>());
        _reservation.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskReservationSystem>());
        _reservation.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneStationActivationReservationSystem>());
        _reservation.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneReturnReservationSystem>());
        _reservation.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneStationLifecycleReservationSystem>());
        _execution.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneItemTransferExecutionSystem>());
        _execution.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskExecutionSystem>());
        _execution.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneMovementExecutionSystem>());
        _execution.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneChargingExecutionSystem>());
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskLifecycleApplySystem>());
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneMovementApplySystem>());
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneTaskAssignmentPublishSystem>());
        _apply.AddSystemToUpdateList(_world.GetOrCreateSystem<DroneStationLifecycleApplySystem>());
        _preparation.SortSystems();
        _decision.SortSystems();
        _reservation.SortSystems();
        _execution.SortSystems();
        _apply.SortSystems();
    }

    [TestCase(0f, 0f)]
    [TestCase(5f, 0.5f)]
    [TestCase(40f, 1f)]
    public void RealPower_SharedSupplyRatio_ChargesEveryDockedDrone(float generation, float expectedCharge)
    {
        SetGeneration(generation);
        Entity station = SpawnStation();
        Entity first = Park(station, 0f);
        Entity second = Park(station, 0f);
        Tick(0.1f);
        Assert.AreEqual(1f, Demand(station), 0.000001f);
        Assert.AreEqual(expectedCharge, Charge(first), 0.00001f);
        Assert.AreEqual(expectedCharge, Charge(second), 0.00001f);
        Assert.LessOrEqual((double)Charge(first) * 0.5 + Charge(second) * 0.5,
            Supply(station).AllocatedEnergy + 0.000001);
        Assert.AreEqual(expectedCharge > 0 ? DroneActivityKind.Charging : DroneActivityKind.WaitingForCharge,
            _entityManager.GetComponentData<DroneActivityState>(first).Activity);
    }

    [Test]
    public void NearFull_RequestsOnlyMissingCharge_AndStoresOnTheFollowingDecision()
    {
        Entity station = SpawnStation();
        Entity worker = Park(station, 99.75f);
        Tick(0.1f);
        Assert.AreEqual(0.125f, Demand(station), 0.000001f);
        Assert.AreEqual(100f, Charge(worker));
        Assert.IsTrue(_entityManager.HasComponent<DroneWorker>(worker));
        Assert.IsTrue(_entityManager.HasComponent<DroneDocked>(worker));
        Assert.AreEqual(DroneActivityKind.Idle, _entityManager.GetComponentData<DroneActivityState>(worker).Activity);
        Tick();
        Assert.IsFalse(_entityManager.HasComponent<DroneWorker>(worker));
        Assert.AreEqual(station, _entityManager.GetComponentData<ItemOwnership>(worker).Owner);
        Assert.AreEqual(100f, Charge(worker));
    }

    [Test]
    public void NonBinaryChargingRate_ReachesExactFullChargeWithoutCreatingEnergy()
    {
        using var query = _entityManager.CreateEntityQuery(typeof(DroneChargingConfig));
        _entityManager.SetComponentData(query.GetSingletonEntity(), new DroneChargingConfig
        {
            ChargePerSecond = 7f, PowerPerDrone = 3f
        });
        Entity station = SpawnStation();
        Entity first = Park(station, 99.75f);
        Entity second = Park(station, 99.5f);
        Tick(0.1f);
        Assert.AreEqual(100f, Charge(first));
        Assert.AreEqual(100f, Charge(second));
        Assert.LessOrEqual(0.75d * 3d / 7d, Supply(station).AllocatedEnergy);
    }

    [Test]
    public void GeneralDockSlots_ChargeTogetherWithAllFiveDedicatedSlots()
    {
        Entity station = SpawnStation();
        var workers = new Entity[6];
        for (int i = 0; i < workers.Length; i++) workers[i] = Park(station, 0f);
        var dock = _entityManager.GetComponentData<DroneDocked>(workers[5]);
        int general = _entityManager.GetComponentData<Storage>(station).SlotCount -
            _entityManager.GetComponentData<DroneStationStorage>(station).DedicatedSlotCount;
        Assert.Less(dock.SlotIndex, general);
        Tick(0.1f);
        Assert.AreEqual(3f, Demand(station), 0.00001f);
        foreach (Entity worker in workers) Assert.AreEqual(1f, Charge(worker), 0.00001f);
    }

    [Test]
    public void NewDock_RequestsNoEnergyUntilTheNextPreparation()
    {
        Entity station = SpawnStation();
        Assert.IsTrue(DroneTargetPointUtility.TryGetTargetPoint(_entityManager, station, out float2 point));
        Entity worker = Worker(new float3(point, 0), 0f);
        Tick(0.1f);
        Assert.IsTrue(_entityManager.HasComponent<DroneDocked>(worker));
        Assert.AreEqual(0f, Demand(station));
        Assert.AreEqual(0f, Charge(worker));
        Tick(0.1f);
        Assert.AreEqual(0.5f, Demand(station), 0.000001f);
        Assert.AreEqual(1f, Charge(worker), 0.00001f);
    }

    [TestCase(0f)]
    [TestCase(40f)]
    public void ExplicitDock_WithNoMovementTarget_CanDispatchBeforeFullCharge(float generation)
    {
        SetGeneration(generation);
        Entity station = SpawnStation();
        Assert.IsTrue(DroneTargetPointUtility.TryGetTargetPoint(_entityManager, station, out float2 point));
        Entity worker = Worker(new float3(point, 0), 50f);
        DroneStationRequestUtility.Submit(_entityManager, worker, station, DroneStationOperation.Dock);
        Tick();
        Assert.IsTrue(_entityManager.HasComponent<DroneDocked>(worker));
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<DroneMovementState>(worker).ChargingStation);
        Storage(new int2(1, 1), 1);
        Entity destination = Storage(new int2(1, 2));
        DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.StorageImport,
            destination, ItemTypeEnum.Iron, 1);
        Tick(0.1f);
        float charged = Charge(worker);
        Assert.AreEqual(generation > 0f ? DroneActivityKind.Charging : DroneActivityKind.WaitingForCharge,
            _entityManager.GetComponentData<DroneActivityState>(worker).Activity);
        Until(() => !_entityManager.HasComponent<DroneDocked>(worker));
        Assert.AreEqual(charged, Charge(worker));
        Assert.Less(charged, 100f);
        Until(() => Assignment(worker) != Entity.Null);
    }

    [Test]
    public void PowerRecovery_ChangesWaitingToCharging_WithoutLosingDockOrBattery()
    {
        SetGeneration(0f);
        Entity station = SpawnStation();
        Entity worker = Park(station, 20f);
        Tick(0.1f);
        Assert.AreEqual(20f, Charge(worker));
        Assert.AreEqual(DroneActivityKind.WaitingForCharge, _entityManager.GetComponentData<DroneActivityState>(worker).Activity);
        SetGeneration(40f);
        Tick(0.1f);
        Assert.AreEqual(21f, Charge(worker), 0.00001f);
        Assert.IsTrue(_entityManager.HasComponent<DroneDocked>(worker));
        Assert.AreEqual(DroneActivityKind.Charging, _entityManager.GetComponentData<DroneActivityState>(worker).Activity);
    }

    [Test]
    public void DisconnectedStation_DoesNotChargeDespitePositiveDemand()
    {
        Entity station = SpawnStation(BuildingTypeEnum.DroneStation, 20);
        Entity worker = Park(station, 0f);
        Tick(0.1f);
        Assert.AreEqual(0.5f, Demand(station), 0.000001f);
        Assert.AreEqual(0f, Supply(station).AllocatedEnergy);
        Assert.AreEqual(0f, Charge(worker));
    }

    [Test]
    public void LargeDeltaTime_UsesTheSamePowerTimeLimitForDemandAndCharge()
    {
        Entity station = SpawnStation();
        Entity worker = Park(station, 0f);
        Tick(10f);
        Assert.AreEqual(0.5f, Demand(station), 0.000001f);
        Assert.AreEqual(1f, Charge(worker), 0.00001f);
    }

    [Test]
    public void ChargedBattery_BecomesDispatchInputNextTick_AndFinishesTransportAndRecharge()
    {
        Entity station = SpawnStation();
        Entity worker = Park(station, 0f);
        Storage(new int2(1, 1), 1);
        Entity destination = Storage(new int2(1, 2));
        DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.StorageImport,
            destination, ItemTypeEnum.Iron, 1);
        Tick(0.1f);
        Assert.AreEqual(1f, Charge(worker), 0.00001f);
        Assert.IsTrue(_entityManager.HasComponent<DroneDocked>(worker));
        Assert.AreEqual(Entity.Null, Assignment(worker));
        Until(() => !_entityManager.HasComponent<DroneDocked>(worker));
        Assert.Less(Charge(worker), 100f);
        Until(() => Assignment(worker) != Entity.Null);
        Until(() => DroneSchedulingUtility.CountInventory(_entityManager, destination, ItemTypeEnum.Iron) == 1, 1f);
        Until(() => _entityManager.HasComponent<DroneDocked>(worker), 1f);
        for (int i = 0; i < 110 && _entityManager.HasComponent<DroneWorker>(worker); i++) Tick(0.1f);
        Assert.IsTrue(_entityManager.HasComponent<ItemIdentity>(worker));
        Assert.AreEqual(station, _entityManager.GetComponentData<ItemOwnership>(worker).Owner);
        Assert.AreEqual(100f, Charge(worker));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PersistentProductionLoad_ChargingEventuallyStores_WithinFundedEnergy(bool tinyRate)
    {
        float chargePerSecond = tinyRate ? 0.00001f : 10f;
        if (tinyRate)
        {
            using var config = _entityManager.CreateEntityQuery(typeof(DroneChargingConfig));
            _entityManager.SetComponentData(config.GetSingletonEntity(), new DroneChargingConfig
            {
                ChargePerSecond = chargePerSecond, PowerPerDrone = 5f
            });
        }
        SetGeneration(tinyRate ? 40f : 5f);
        Entity station = SpawnStation();
        float initialCharge = tinyRate ? 99.99997f : 99.75f;
        Entity worker = Park(station, initialCharge);
        Entity crafter = AddPersistentProductionLoad();
        double fundedCharge = 0d;
        for (int i = 0; i < 200 && _entityManager.HasComponent<DroneWorker>(worker); i++)
        {
            var batteryBefore = _entityManager.GetComponentData<DroneBatteryState>(worker);
            Tick(0.1f);
            var supply = Supply(station);
            double allocatedCharge = (double)supply.AllocatedEnergy * chargePerSecond / 5d;
            fundedCharge += allocatedCharge;
            var batteryAfter = _entityManager.GetComponentData<DroneBatteryState>(worker);
            double storedChargeIncrease = (double)batteryAfter.CurrentCharge - batteryBefore.CurrentCharge +
                batteryAfter.FractionalCharge - batteryBefore.FractionalCharge;
            Assert.LessOrEqual(storedChargeIncrease, allocatedCharge + 0.000000000001d);
            Assert.IsTrue(DroneBatteryUtility.IsValid(batteryAfter, 100f));
            Assert.LessOrEqual((double)Charge(worker) - initialCharge, fundedCharge + 0.000000001d);
            if (!tinyRate && Demand(station) > 0f)
            {
                Assert.Greater(supply.SupplyRatio, 0f);
                Assert.Less(supply.SupplyRatio, 1f);
            }
        }
        Assert.AreEqual(100f, Charge(worker));
        Assert.AreEqual(0d, _entityManager.GetComponentData<DroneBatteryState>(worker).FractionalCharge);
        Assert.IsTrue(_entityManager.HasComponent<ItemIdentity>(worker));
        Assert.AreEqual(station, _entityManager.GetComponentData<ItemOwnership>(worker).Owner);
        Assert.IsFalse(_entityManager.HasComponent<DroneDocked>(worker));
        Assert.Greater(_entityManager.GetComponentData<CrafterState>(crafter).Progress, 0f);
        Assert.Less(_entityManager.GetComponentData<CrafterState>(crafter).Progress, 1f);
    }

    [Test]
    public void FundedFraction_SurvivesNoPowerUndockAndMovement()
    {
        SetGeneration(5f);
        Entity station = SpawnStation();
        Entity worker = Park(station, 99.75f);
        AddPersistentProductionLoad();
        Tick(0.1f);
        var chargedBattery = _entityManager.GetComponentData<DroneBatteryState>(worker);
        Assert.Greater(chargedBattery.FractionalCharge, 0d);
        SetGeneration(0f);
        Tick(0.1f);
        var waiting = _entityManager.GetComponentData<DroneBatteryState>(worker);
        Assert.AreEqual(chargedBattery.CurrentCharge, waiting.CurrentCharge);
        Assert.AreEqual(chargedBattery.FractionalCharge, waiting.FractionalCharge);
        Assert.AreEqual(DroneActivityKind.WaitingForCharge, _entityManager.GetComponentData<DroneActivityState>(worker).Activity);

        Storage(new int2(1, 1), 1);
        Entity destination = Storage(new int2(1, 2));
        DroneTaskRequestUtility.SubmitCreate(_entityManager, DroneLogisticsTaskKindEnum.StorageImport,
            destination, ItemTypeEnum.Iron, 1);
        Until(() => !_entityManager.HasComponent<DroneDocked>(worker));
        Assert.AreEqual(chargedBattery.FractionalCharge, _entityManager.GetComponentData<DroneBatteryState>(worker).FractionalCharge);
        Until(() => Assignment(worker) != Entity.Null);
        Entity assignment = Assignment(worker);
        Until(() => _entityManager.GetComponentData<DroneTaskAssignment>(assignment).State ==
            DroneTaskAssignmentStateEnum.MovingToDestination);
        float before = Charge(worker);
        Tick(0.1f);
        Assert.AreEqual(before - 0.4f, Charge(worker), 0.00001f);
        Assert.AreEqual(chargedBattery.FractionalCharge, _entityManager.GetComponentData<DroneBatteryState>(worker).FractionalCharge);
    }

    [TestCase(-1d)]
    [TestCase(double.NaN)]
    [TestCase(1d)]
    public void InvalidExistingFraction_RejectsActivationWithoutChangingBattery(double fraction)
    {
        Entity drone = _entityManager.CreateEntity(typeof(LocalTransform), typeof(DroneBatteryState));
        _entityManager.SetComponentData(drone, LocalTransform.Identity);
        _entityManager.SetComponentData(drone, new DroneBatteryState { CurrentCharge = 100f, FractionalCharge = fraction });
        var ecb = _end.CreateCommandBuffer();
        Assert.IsFalse(DroneSpawnUtility.TryInitializeWorkerRole(_entityManager, ref ecb, drone, float3.zero, 100f, out _));
        _end.Update();
        Assert.IsFalse(_entityManager.HasComponent<DroneWorker>(drone));
        Assert.AreEqual(100f, Charge(drone));
        Assert.AreEqual(fraction, _entityManager.GetComponentData<DroneBatteryState>(drone).FractionalCharge);
    }

    private Entity AddPersistentProductionLoad()
    {
        BuildingPlacementRequestUtility.SubmitSpawn(_entityManager,
            new SpawnBuildingRequest(BuildingTypeEnum.PowerPole, 1, new int2(3, 0), DirectionEnum.Up));
        BuildingPlacementRequestUtility.SubmitSpawn(_entityManager,
            new SpawnBuildingRequest(BuildingTypeEnum.Crafter, 1, new int2(4, 0), DirectionEnum.Up));
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<BuildingLifecycleApplySystem>());
        _world.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>().Update();
        using var query = _entityManager.CreateEntityQuery(typeof(CrafterState));
        Entity crafter = query.GetSingletonEntity();
        // 재료 선소비를 마친 긴 제작을 입력으로 두고, 진행은 실제 제작 시스템이 수행한다.
        _entityManager.SetComponentData(crafter, new CrafterState(1) { IsCraftingActive = true });
        using var recipes = _entityManager.CreateEntityQuery(typeof(RecipeRegistry));
        var buffer = _entityManager.GetBuffer<RecipeConfigElement>(recipes.GetSingletonEntity());
        var recipe = buffer[0];
        recipe.CraftTime = 100000f;
        buffer[0] = recipe;
        _preparation.AddSystemToUpdateList(_world.GetOrCreateSystem<CrafterWorkPreparationSystem>());
        _preparation.SortSystems();
        _loadDecision = _world.GetOrCreateSystem<CrafterDecisionSystem>();
        _loadExecution = _world.GetOrCreateSystem<CrafterExecutionSystem>();
        _loadApply = _world.GetOrCreateSystem<CrafterStateApplySystem>();
        _hasProductionLoad = true;
        return crafter;
    }

    private Entity SpawnStation(BuildingTypeEnum type = BuildingTypeEnum.MainFacility, int x = 0)
    {
        BuildingPlacementRequestUtility.SubmitSpawn(_entityManager,
            new SpawnBuildingRequest(type, 1, new int2(x, 0), DirectionEnum.Up));
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<BuildingLifecycleApplySystem>());
        _world.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>().Update();
        using var query = _entityManager.CreateEntityQuery(typeof(BuildingType), typeof(DroneStation));
        using var stations = query.ToEntityArray(Unity.Collections.Allocator.Temp);
        foreach (Entity station in stations)
        {
            if (_entityManager.GetComponentData<GridPosition>(station).Value.x != x) continue;
            return station;
        }
        Assert.Fail("Station spawn did not publish.");
        return Entity.Null;
    }

    private Entity Park(Entity station, float charge)
    {
        Assert.IsTrue(DroneTargetPointUtility.TryGetTargetPoint(_entityManager, station, out float2 point));
        Entity worker = Worker(new float3(point, 0), charge);
        for (int i = 0; i < 20 && !_entityManager.HasComponent<DroneDocked>(worker); i++) Tick();
        Assert.IsTrue(_entityManager.HasComponent<DroneDocked>(worker), DescribeMovement(worker));
        return worker;
    }

    private void SetGeneration(float value)
    {
        using var query = _entityManager.CreateEntityQuery(typeof(BuildingConfig));
        var configs = _entityManager.GetBuffer<BuildingConfigElement>(query.GetSingletonEntity());
        for (int i = 0; i < configs.Length; i++)
        {
            if (configs[i].BuildingType != BuildingTypeEnum.MainFacility) continue;
            var row = configs[i];
            row.Power.MaximumGeneration = value;
            configs[i] = row;
        }
    }

    private PowerSupplyResult Supply(Entity station) => _entityManager.GetComponentData<PowerSupplyResult>(station);
    private float Demand(Entity station) => _entityManager.GetComponentData<PowerDemand>(station).RequestedEnergy;

    private Entity Storage(int2 position, int quantity = 0)
    {
        Entity storage = Entities.CreateStorage(position, new int2(1, 1), slotCount: 1);
        for (int i = 0; i < quantity; i++) Entities.CreateStoredItem(storage, ItemTypeEnum.Iron);
        return storage;
    }

    private Entity Worker(float3 position, float charge)
    {
        Entity worker = _entityManager.CreateEntity(typeof(LocalTransform), typeof(LocalToWorld), typeof(DroneBatteryState));
        _entityManager.SetComponentData(worker, LocalTransform.FromPosition(position));
        _entityManager.SetComponentData(worker, new DroneBatteryState { CurrentCharge = charge });
        var ecb = _end.CreateCommandBuffer();
        Assert.IsTrue(DroneSpawnUtility.TryInitializeWorkerRole(_entityManager, ref ecb, worker, position, 100, out string error), error);
        _end.Update();
        return worker;
    }

    private Entity Assignment(Entity worker) => _entityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment;
    private float Charge(Entity worker) => _entityManager.GetComponentData<DroneBatteryState>(worker).CurrentCharge;

    private string DescribeMovement(Entity worker)
    {
        var decision = _entityManager.GetComponentData<DroneMovementDecision>(worker);
        var movement = _entityManager.GetComponentData<DroneMovementState>(worker);
        var activity = _entityManager.GetComponentData<DroneActivityState>(worker);
        string dock = _entityManager.HasComponent<DroneDocked>(worker)
            ? _entityManager.GetComponentData<DroneDocked>(worker).Station.ToString() : "none";
        return $"Decision={decision.Purpose}, Station={decision.Station}, Ready={decision.Ready}; " +
            $"ChargingStation={movement.ChargingStation}, ReturnStation={movement.ReturnStation}, " +
            $"IsNormalReturn={movement.IsNormalReturn}, Activity={activity.Activity}, Dock={dock}";
    }

    private void Until(Func<bool> condition, float dt = 0f)
    {
        for (int i = 0; i < 20 && !condition(); i++) Tick(dt);
        Assert.IsTrue(condition(), "The actual simulation pipeline did not reach the expected state.");
    }

    private void Tick(float dt = 0f)
    {
        TickWithoutCommit(dt);
        _end.Update();
    }

    private void TickWithoutCommit(float dt = 0f)
    {
        _time += dt;
        Simulation.SetDeltaTime(dt, _time);
        _preparation.Update();
        _power.Update();
        if (_hasProductionLoad)
        {
            Simulation.UpdateAndComplete(_loadDecision);
            Simulation.UpdateAndComplete(_loadExecution);
            Simulation.UpdateAndComplete(_loadApply);
            _world.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>().Update();
        }
        _decision.Update();
        _reservation.Update();
        _execution.Update();
        _apply.Update();
    }
}
