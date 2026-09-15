using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

public class DroneChargingPowerTests
{
    private World _world;
    private EntityManager _entityManager;
    private PowerGridSystem _powerGrid;
    private DroneChargingDemandSystem _demandSystem;
    private DroneChargingSystem _chargingSystem;
    private CoalGeneratorFuelSystem _fuelSystem;
    private Entity _droneConfigEntity;

    [SetUp]
    public void SetUp()
    {
        _world = new World(nameof(DroneChargingPowerTests));
        _entityManager = _world.EntityManager;
        _world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
        _world.GetOrCreateSystemManaged<ChunkMapSystem>();
        _world.GetOrCreateSystemManaged<ItemStorageSystem>();
        _demandSystem = _world.GetOrCreateSystemManaged<
            DroneChargingDemandSystem>();
        _powerGrid = _world.GetOrCreateSystemManaged<PowerGridSystem>();
        _fuelSystem = _world.GetOrCreateSystemManaged<
            CoalGeneratorFuelSystem>();
        _chargingSystem = _world.GetOrCreateSystemManaged<
            DroneChargingSystem>();
        _droneConfigEntity = CreateConfigs();
        CreatePowerPole(new int2(1, 0));
        _world.SetTime(new TimeData(1d, 1f));
    }

    [TearDown]
    public void TearDown()
    {
        _world.Dispose();
    }

    [Test]
    public void FullSupplyChargesAtConfiguredSpeed()
    {
        Entity station = CreateStation();
        Entity drone = CreateStoredDrone(station, 80f, 100f);
        CreateStandardGenerator(20f);

        RunChargeFrame(false);

        Assert.That(GetBattery(drone), Is.EqualTo(90f));
        Assert.That(GetConsumer(station).maximumConsumption, Is.EqualTo(10f));
        Assert.That(GetConsumer(station).currentConsumption, Is.EqualTo(10f));
        Assert.That(GetConsumer(station).supplyRatio, Is.EqualTo(1f));
    }

    [Test]
    public void PartialSupplyScalesChargingSpeedBySameGridRatio()
    {
        Entity station = CreateStation();
        Entity drone = CreateStoredDrone(station, 80f, 100f);
        CreateStandardGenerator(5f);

        RunChargeFrame(false);

        Assert.That(GetConsumer(station).supplyRatio, Is.EqualTo(0.5f));
        Assert.That(GetBattery(drone), Is.EqualTo(85f));
    }

    [Test]
    public void PowerLossPreservesChargeAndRecoveryContinuesFromSameValue()
    {
        Entity station = CreateStation();
        Entity drone = CreateStoredDrone(station, 80f, 100f);

        RunChargeFrame(false);

        Assert.That(GetConsumer(station).supplyRatio, Is.EqualTo(0f));
        Assert.That(GetBattery(drone), Is.EqualTo(80f));

        CreateStandardGenerator(20f);
        RunChargeFrame(false);

        Assert.That(GetConsumer(station).supplyRatio, Is.EqualTo(1f));
        Assert.That(GetBattery(drone), Is.EqualTo(90f));
    }

    [Test]
    public void FinalPartialChargeRequestsOnlyNeededPower()
    {
        Entity station = CreateStation();
        Entity drone = CreateStoredDrone(station, 98f, 100f);
        CreateStandardGenerator(20f);

        RunChargeFrame(false);

        Assert.That(GetConsumer(station).maximumConsumption, Is.EqualTo(2f));
        Assert.That(GetConsumer(station).currentConsumption, Is.EqualTo(2f));
        Assert.That(GetBattery(drone), Is.EqualTo(100f));
        Assert.That(
            _entityManager.GetComponentData<DroneState>(drone).value,
            Is.EqualTo(DroneStateEnum.Stored));
        Assert.That(
            _entityManager.HasComponent<DisableRendering>(drone),
            Is.True);
    }

    [Test]
    public void CoalFuelConsumptionUsesActualDroneChargingDemand()
    {
        Entity station = CreateStation();
        CreateStoredDrone(station, 80f, 100f);
        Entity generator = CreateCoalGenerator(100f);

        RunChargeFrame(true);

        Assert.That(
            _entityManager.GetComponentData<PowerGenerator>(generator)
                .currentGeneration,
            Is.EqualTo(10f));
        Assert.That(
            _entityManager.GetComponentData<CoalGenerator>(generator)
                .remainingFuelEnergy,
            Is.EqualTo(90f));
    }

    [Test]
    public void EmergencyReturnBecomesStoredChargingDemand()
    {
        Entity station = CreateStation();
        Entity drone = CreateReturningDrone(station);
        CreateStandardGenerator(20f);
        DroneMovementSystem movementSystem = _world
            .GetOrCreateSystemManaged<DroneMovementSystem>();
        DroneStationStorageSystem storageSystem = _world
            .GetOrCreateSystemManaged<DroneStationStorageSystem>();

        movementSystem.Update();
        storageSystem.Update();
        RunChargeFrame(false);

        Assert.That(_entityManager.HasComponent<StoredDrone>(drone), Is.True);
        Assert.That(
            _entityManager.GetBuffer<StoredDroneElement>(station).Length,
            Is.EqualTo(1));
        Assert.That(GetConsumer(station).maximumConsumption, Is.EqualTo(10f));
        Assert.That(GetBattery(drone), Is.EqualTo(10f));
        Assert.That(
            _entityManager.GetComponentData<DroneState>(drone).value,
            Is.EqualTo(DroneStateEnum.AwaitingCharge));
        Assert.That(
            _entityManager.GetComponentData<DroneStationNetwork>(station)
                .networkId,
            Is.EqualTo(77));
    }

    [Test]
    public void MainFacilityKeepsGeneratorStationAndChargingConsumerRoles()
    {
        Entity prefab = _entityManager.CreateEntity(
            typeof(Prefab),
            typeof(LocalTransform));
        Entity database = _entityManager.CreateEntity(
            typeof(BuildingPrefabElement));
        _entityManager.GetBuffer<BuildingPrefabElement>(database).Add(
            new BuildingPrefabElement
            {
                type = BuildingTypeEnum.MainFacility,
                prefab = prefab,
                size = new int2(3)
            });
        MainFacilityBootstrapSystem bootstrap = _world
            .GetOrCreateSystemManaged<MainFacilityBootstrapSystem>();

        bootstrap.Update();

        using EntityQuery query = _entityManager.CreateEntityQuery(
            typeof(MainFacility));
        Entity mainFacility = query.GetSingletonEntity();
        Assert.That(
            _entityManager.HasComponent<PowerGenerator>(mainFacility),
            Is.True);
        Assert.That(
            _entityManager.HasComponent<PowerConsumer>(mainFacility),
            Is.True);
        Assert.That(
            _entityManager.HasComponent<DroneStation>(mainFacility),
            Is.True);
        Assert.That(
            _entityManager.GetComponentData<PowerConsumer>(mainFacility)
                .maximumConsumption,
            Is.EqualTo(0f));
    }

    private Entity CreateConfigs()
    {
        Entity config = _entityManager.CreateEntity(
            typeof(DroneConfig),
            typeof(StartingItemConfigElement),
            typeof(ItemStorageLimitElement),
            typeof(PowerConfig),
            typeof(CoalGeneratorConfig),
            typeof(PowerPoleConfigElement),
            typeof(PowerGeneratorConfigElement),
            typeof(PowerConsumerConfigElement));
        _entityManager.SetComponentData(config, new DroneConfig
        {
            chargingSpeed = 10f,
            chargingPowerConsumptionPerDrone = 10f
        });
        _entityManager.SetComponentData(config, new CoalGeneratorConfig
        {
            coalEnergyPerItem = 600f,
            fuelStorageCapacity = 10
        });
        _entityManager.GetBuffer<ItemStorageLimitElement>(config).Add(
            new ItemStorageLimitElement
            {
                itemType = ItemTypeEnum.Drone,
                maxAmount = 1
            });
        _entityManager.GetBuffer<PowerPoleConfigElement>(config).Add(
            new PowerPoleConfigElement
            {
                buildingType = BuildingTypeEnum.PowerPole,
                supplyRange = new int2(5),
                connectionRange = new int2(8)
            });
        _entityManager.GetBuffer<PowerGeneratorConfigElement>(config).Add(
            new PowerGeneratorConfigElement
            {
                generatorType = PowerGeneratorTypeEnum.MainFacility,
                maximumGeneration = 40f
            });
        return config;
    }

    private Entity CreateStation()
    {
        Entity station = _entityManager.CreateEntity(
            typeof(DroneStation),
            typeof(DroneStationNetwork),
            typeof(BuildingOccupant),
            typeof(GridPosition),
            typeof(BuildingFootprint),
            typeof(Direction),
            typeof(Storage),
            typeof(PowerConsumer),
            typeof(StoredItemElement),
            typeof(StoredDroneElement));
        _entityManager.SetComponentData(
            station,
            new DroneStationNetwork { networkId = 77 });
        _entityManager.SetComponentData(
            station,
            new GridPosition { gridPosition = int2.zero });
        SetSingleCellFootprint(station);
        _entityManager.SetComponentData(
            station,
            new Storage { capacity = 10 });
        return station;
    }

    private Entity CreateStoredDrone(
        Entity station,
        float currentBattery,
        float maximumBattery)
    {
        Entity drone = CreateDrone(currentBattery, maximumBattery);
        _entityManager.SetComponentData(
            drone,
            new DroneState { value = DroneStateEnum.AwaitingCharge });
        _entityManager.AddComponentData(
            drone,
            new StoredDrone { stationEntity = station });
        _entityManager.GetBuffer<StoredDroneElement>(station).Add(
            new StoredDroneElement { droneEntity = drone });
        return drone;
    }

    private Entity CreateReturningDrone(Entity station)
    {
        Entity drone = CreateDrone(0f, 100f);
        _entityManager.SetComponentData(
            drone,
            new DroneState { value = DroneStateEnum.EmergencyReturning });
        _entityManager.SetComponentData(drone, new DroneAssignment
        {
            returnStation = station,
            emergencyReturn = true
        });
        return drone;
    }

    private Entity CreateDrone(float currentBattery, float maximumBattery)
    {
        Entity drone = _entityManager.CreateEntity(
            typeof(ActiveDrone),
            typeof(DroneBattery),
            typeof(DroneState),
            typeof(DroneAssignment),
            typeof(DroneCargo),
            typeof(GridPosition),
            typeof(LocalTransform),
            typeof(StoredItemElement));
        _entityManager.SetComponentData(drone, new ActiveDrone
        {
            movementSpeed = 4f,
            emergencyMovementSpeed = 0.5f
        });
        _entityManager.SetComponentData(drone, new DroneBattery
        {
            current = currentBattery,
            maximum = maximumBattery,
            consumptionPerDistance = 1f
        });
        _entityManager.SetComponentData(
            drone,
            new GridPosition { gridPosition = int2.zero });
        _entityManager.SetComponentData(
            drone,
            LocalTransform.FromPosition(new float3(0f, 0f, -0.2f)));
        return drone;
    }

    private void CreatePowerPole(int2 cell)
    {
        Entity pole = _entityManager.CreateEntity(
            typeof(PowerPole),
            typeof(GridPosition),
            typeof(BuildingFootprint),
            typeof(Direction),
            typeof(BuildingOccupant));
        _entityManager.SetComponentData(
            pole,
            new GridPosition { gridPosition = cell });
        SetSingleCellFootprint(pole);
    }

    private Entity CreateStandardGenerator(float maximumGeneration)
    {
        Entity generator = _entityManager.CreateEntity(
            typeof(PowerGenerator),
            typeof(GridPosition),
            typeof(BuildingFootprint),
            typeof(Direction),
            typeof(BuildingOccupant));
        _entityManager.SetComponentData(generator, new PowerGenerator
        {
            type = PowerGeneratorTypeEnum.MainFacility,
            maximumGeneration = maximumGeneration
        });
        _entityManager.SetComponentData(
            generator,
            new GridPosition { gridPosition = int2.zero });
        SetSingleCellFootprint(generator);
        return generator;
    }

    private Entity CreateCoalGenerator(float remainingFuelEnergy)
    {
        Entity generator = _entityManager.CreateEntity(
            typeof(CoalGenerator),
            typeof(PowerGenerator),
            typeof(GridPosition),
            typeof(BuildingFootprint),
            typeof(Direction),
            typeof(BuildingOccupant),
            typeof(StoredItemElement));
        _entityManager.SetComponentData(generator, new CoalGenerator
        {
            remainingFuelEnergy = remainingFuelEnergy
        });
        _entityManager.SetComponentData(generator, new PowerGenerator
        {
            type = PowerGeneratorTypeEnum.CoalGenerator,
            maximumGeneration = 100f
        });
        _entityManager.SetComponentData(
            generator,
            new GridPosition { gridPosition = int2.zero });
        SetSingleCellFootprint(generator);
        return generator;
    }

    private void SetSingleCellFootprint(Entity entity)
    {
        _entityManager.SetComponentData(
            entity,
            new BuildingFootprint { size = new int2(1, 1) });
        _entityManager.SetComponentData(
            entity,
            new Direction { dir = DirectionEnum.Up });
    }

    private void RunChargeFrame(bool consumeCoalFuel)
    {
        _demandSystem.Update();
        _powerGrid.Update();

        if (consumeCoalFuel)
            _fuelSystem.Update();

        _chargingSystem.Update();
    }

    private float GetBattery(Entity droneEntity)
    {
        return _entityManager.GetComponentData<DroneBattery>(droneEntity)
            .current;
    }

    private PowerConsumer GetConsumer(Entity stationEntity)
    {
        return _entityManager.GetComponentData<PowerConsumer>(stationEntity);
    }
}
