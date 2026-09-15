using System.Collections.Generic;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public class PowerGridTopologyTests : EcsWorldTestFixture
{
    private ChunkMapSystem _chunkMap;
    private PowerGridSystem _powerGrid;

    [SetUp]
    public void SetUp()
    {
        _chunkMap = _world.GetOrCreateSystemManaged<ChunkMapSystem>();
        _world.GetOrCreateSystemManaged<ItemStorageSystem>();
        _powerGrid = _world.GetOrCreateSystemManaged<PowerGridSystem>();
        CreatePowerConfig(new int2(5, 5), new int2(8, 8));
    }

    [Test]
    public void RemovingBridgePoleSplitsPowerGrid()
    {
        Entity left = CreateRegisteredPowerPole(new int2(0, 0));
        Entity bridge = CreateRegisteredPowerPole(new int2(8, 0));
        Entity right = CreateRegisteredPowerPole(new int2(16, 0));

        _powerGrid.Update();

        Assert.That(GetGrid(left), Is.EqualTo(GetGrid(bridge)));
        Assert.That(GetGrid(bridge), Is.EqualTo(GetGrid(right)));

        Assert.That(_powerGrid.TryUnregisterPowerPole(bridge), Is.True);
        _entityManager.DestroyEntity(bridge);
        _powerGrid.Update();

        Assert.That(GetGrid(left), Is.Not.EqualTo(GetGrid(right)));
    }

    [Test]
    public void OverlappingSupplyDoesNotMergeDisconnectedPoles()
    {
        ReplacePowerConfig(new int2(5, 5), new int2(2, 2));
        Entity first = CreateRegisteredPowerPole(new int2(0, 0));
        Entity second = CreateRegisteredPowerPole(new int2(8, 0));

        _powerGrid.Update();

        List<Entity> coveringPoles = new List<Entity>();
        _chunkMap.GetPowerPolesCoveringCell(new int2(4, 0), coveringPoles);

        Assert.That(coveringPoles, Has.Count.EqualTo(2));
        Assert.That(GetGrid(first), Is.Not.EqualTo(GetGrid(second)));
    }

    [Test]
    public void NearestPoleUsesStableIdToBreakDistanceTie()
    {
        ReplacePowerConfig(new int2(5, 5), new int2(2, 2));
        Entity first = CreateRegisteredPowerPole(new int2(0, 0));
        CreateRegisteredPowerPole(new int2(8, 0));

        _powerGrid.Update();

        Assert.That(
            _powerGrid.TryGetNearestPowerPole(new int2(4, 0), out Entity nearest),
            Is.True);
        Assert.That(nearest, Is.EqualTo(first));
    }

    [Test]
    public void ConnectablePoleQueryUsesBidirectionalConnectionBounds()
    {
        Entity existingPole = CreateRegisteredPowerPole(int2.zero);
        CreateRegisteredPowerPole(new int2(20, 0));
        _powerGrid.Update();
        List<Entity> connectablePoles = new List<Entity>();

        _powerGrid.GetConnectablePowerPoles(
            new int2(8, 0),
            new int2(2, 2),
            connectablePoles);

        Assert.That(connectablePoles, Is.EqualTo(new[] { existingPole }));
    }

    [Test]
    public void InclusiveSupplyBoundsEnumerateEveryCell()
    {
        GridBounds bounds = PowerGridRangeUtility.GetBounds(
            int2.zero,
            new int2(1, 1));
        List<int2> cells = new List<int2>();

        bounds.GetCells(cells);

        Assert.That(cells, Has.Count.EqualTo(9));
        Assert.That(cells, Does.Contain(new int2(-1, -1)));
        Assert.That(cells, Does.Contain(new int2(0, 0)));
        Assert.That(cells, Does.Contain(new int2(1, 1)));
    }

    [Test]
    public void DuplicateSupplyRegistrationDoesNotLoseExistingIndex()
    {
        Entity powerPole = _entityManager.CreateEntity();
        GridBounds bounds = PowerGridRangeUtility.GetBounds(
            int2.zero,
            new int2(1, 1));

        Assert.That(
            _chunkMap.TryRegisterPowerPoleSupply(powerPole, bounds),
            Is.True);
        Assert.That(
            _chunkMap.TryRegisterPowerPoleSupply(powerPole, bounds),
            Is.False);

        List<Entity> coveringPoles = new List<Entity>();
        _chunkMap.GetPowerPolesCoveringCell(int2.zero, coveringPoles);
        Assert.That(coveringPoles, Is.EqualTo(new[] { powerPole }));
    }

    [Test]
    public void ConnectedGridAggregatesGenerationDemandAndSupplyRatio()
    {
        ReplacePowerConfig(new int2(5, 5), new int2(2, 2));
        Entity powerPole = CreateRegisteredPowerPole(new int2(1, 0));
        Entity generator = CreatePowerGenerator(int2.zero, 40f);
        Entity consumer = CreatePowerConsumer(new int2(2, 0), 100f);

        _powerGrid.Update();

        Entity powerGrid = GetGrid(powerPole);
        PowerGridState state = _entityManager
            .GetComponentData<PowerGridState>(powerGrid);
        PowerConsumer consumerState = _entityManager
            .GetComponentData<PowerConsumer>(consumer);

        Assert.That(
            _entityManager.GetComponentData<PowerGridConnection>(generator)
                .powerPoleEntity,
            Is.EqualTo(powerPole));
        Assert.That(state.availableGeneration, Is.EqualTo(40f));
        Assert.That(state.maximumDemand, Is.EqualTo(100f));
        Assert.That(state.actualConsumption, Is.EqualTo(40f));
        Assert.That(state.sparePower, Is.EqualTo(0f));
        Assert.That(state.supplyRatio, Is.EqualTo(0.4f));
        Assert.That(state.connectedBuildingCount, Is.EqualTo(2));
        Assert.That(consumerState.currentConsumption, Is.EqualTo(40f));
        Assert.That(consumerState.supplyRatio, Is.EqualTo(0.4f));
    }

    [Test]
    public void GridWithoutConsumersHasFullSupplyAndSpareGeneration()
    {
        Entity powerPole = CreateRegisteredPowerPole(new int2(1, 0));
        CreatePowerGenerator(int2.zero, 40f);

        _powerGrid.Update();

        PowerGridState state = _entityManager
            .GetComponentData<PowerGridState>(GetGrid(powerPole));

        Assert.That(state.maximumDemand, Is.EqualTo(0f));
        Assert.That(state.actualConsumption, Is.EqualTo(0f));
        Assert.That(state.sparePower, Is.EqualTo(40f));
        Assert.That(state.supplyRatio, Is.EqualTo(1f));
    }

    [Test]
    public void MainFacilityCreatesPowerGridWithoutSeparatePowerPole()
    {
        Entity mainFacility = CreateMainFacilityPowerRoot(40f, 10f);

        _powerGrid.Update();

        Entity powerGrid = GetGrid(mainFacility);
        PowerGridState state = _entityManager
            .GetComponentData<PowerGridState>(powerGrid);
        PowerConsumer consumer = _entityManager
            .GetComponentData<PowerConsumer>(mainFacility);

        Assert.That(
            _entityManager.GetComponentData<PowerGridConnection>(mainFacility)
                .powerPoleEntity,
            Is.EqualTo(mainFacility));
        Assert.That(state.availableGeneration, Is.EqualTo(40f));
        Assert.That(state.maximumDemand, Is.EqualTo(10f));
        Assert.That(consumer.supplyRatio, Is.EqualTo(1f));
    }

    [Test]
    public void PowerPoleConnectsToMainFacilityPowerGrid()
    {
        Entity mainFacility = CreateMainFacilityPowerRoot(40f, 0f);
        Entity powerPole = CreateRegisteredPowerPole(new int2(8, 0));
        Entity consumer = CreatePowerConsumer(new int2(13, 0), 10f);

        _powerGrid.Update();

        Assert.That(GetGrid(powerPole), Is.EqualTo(GetGrid(mainFacility)));
        Assert.That(
            _entityManager.GetComponentData<PowerGridConnection>(consumer)
                .powerPoleEntity,
            Is.EqualTo(powerPole));
        Assert.That(
            _entityManager.GetComponentData<PowerConsumer>(consumer)
                .supplyRatio,
            Is.EqualTo(1f));
    }

    [Test]
    public void CoalGeneratorWithoutFuelProvidesNoPower()
    {
        Entity powerPole = CreateRegisteredPowerPole(new int2(1, 0));
        Entity generator = CreateCoalGenerator(int2.zero, 0f);
        CreatePowerConsumer(new int2(2, 0), 50f);

        _powerGrid.Update();

        PowerGridState state = _entityManager
            .GetComponentData<PowerGridState>(GetGrid(powerPole));
        PowerGenerator generatorState = _entityManager
            .GetComponentData<PowerGenerator>(generator);

        Assert.That(state.availableGeneration, Is.EqualTo(0f));
        Assert.That(state.actualConsumption, Is.EqualTo(0f));
        Assert.That(generatorState.currentGeneration, Is.EqualTo(0f));
    }

    [Test]
    public void CoalGeneratorOnlyProducesActualResidualDemand()
    {
        Entity powerPole = CreateRegisteredPowerPole(new int2(1, 0));
        CreatePowerGenerator(int2.zero, 40f);
        Entity coalGenerator = CreateCoalGenerator(new int2(2, 0), 600f);
        CreatePowerConsumer(new int2(1, 1), 70f);

        _powerGrid.Update();

        PowerGridState state = _entityManager
            .GetComponentData<PowerGridState>(GetGrid(powerPole));
        PowerGenerator coalState = _entityManager
            .GetComponentData<PowerGenerator>(coalGenerator);

        Assert.That(state.availableGeneration, Is.EqualTo(140f));
        Assert.That(state.actualConsumption, Is.EqualTo(70f));
        Assert.That(
            coalState.currentGeneration,
            Is.EqualTo(30f).Within(0.0001f));
    }

    [Test]
    public void CoalGeneratorsShareLoadWithSameActivationRatio()
    {
        CreateRegisteredPowerPole(new int2(1, 0));
        Entity first = CreateCoalGenerator(int2.zero, 600f);
        Entity second = CreateCoalGenerator(new int2(2, 0), 600f);
        CreatePowerConsumer(new int2(1, 1), 100f);

        _powerGrid.Update();

        PowerGenerator firstState = _entityManager
            .GetComponentData<PowerGenerator>(first);
        PowerGenerator secondState = _entityManager
            .GetComponentData<PowerGenerator>(second);

        Assert.That(firstState.currentGeneration, Is.EqualTo(50f));
        Assert.That(secondState.currentGeneration, Is.EqualTo(50f));
    }

    [Test]
    public void UnconnectedConsumerHasZeroSupply()
    {
        CreateRegisteredPowerPole(int2.zero);
        Entity consumer = CreatePowerConsumer(new int2(20, 0), 10f);

        _powerGrid.Update();

        PowerConsumer state = _entityManager
            .GetComponentData<PowerConsumer>(consumer);
        Assert.That(
            _entityManager.HasComponent<PowerGridConnection>(consumer),
            Is.False);
        Assert.That(state.currentConsumption, Is.EqualTo(0f));
        Assert.That(state.supplyRatio, Is.EqualTo(0f));
    }

    [TestCase(2, 0, false)]
    [TestCase(3, 1, true)]
    public void MultiCellParticipantRanksAllCoveredCellsThenStableId(
        int secondPoleX, int secondPoleY, bool expectFirst)
    {
        ReplacePowerConfig(new int2(5, 5), int2.zero);
        Entity first = CreateRegisteredPowerPole(new int2(-1, 0));
        Entity second = CreateRegisteredPowerPole(new int2(secondPoleX, secondPoleY));
        Entity consumer = CreatePowerConsumer(
            int2.zero, 10f, new int2(4, 1), DirectionEnum.Up);

        _powerGrid.Update();

        Assert.That(
            _entityManager.GetComponentData<PowerGridConnection>(consumer).powerPoleEntity,
            Is.EqualTo(expectFirst ? first : second));
    }

    [Test]
    public void RotatedFootprintConnectsWhenOnlyNonAnchorCellIsCovered()
    {
        ReplacePowerConfig(int2.zero, new int2(2, 2));
        Entity powerPole = CreateRegisteredPowerPole(new int2(11, -1));
        Entity consumer = CreatePowerConsumer(
            new int2(10, 0),
            10f,
            new int2(2, 3),
            DirectionEnum.Right);

        _powerGrid.Update();

        Assert.That(
            _entityManager.GetComponentData<PowerGridConnection>(consumer)
                .powerPoleEntity,
            Is.EqualTo(powerPole));
    }

    [Test]
    public void RemovingBridgeReconnectsConsumerAndReaggregatesSplitGrids()
    {
        Entity left = CreateRegisteredPowerPole(new int2(0, 0));
        Entity bridge = CreateRegisteredPowerPole(new int2(8, 0));
        Entity right = CreateRegisteredPowerPole(new int2(16, 0));
        CreatePowerGenerator(new int2(0, 1), 40f);
        Entity consumer = CreatePowerConsumer(new int2(12, 0), 10f);

        _powerGrid.Update();

        Assert.That(
            _entityManager.GetComponentData<PowerGridConnection>(consumer)
                .powerPoleEntity,
            Is.EqualTo(bridge));

        Assert.That(_powerGrid.TryUnregisterPowerPole(bridge), Is.True);
        _entityManager.DestroyEntity(bridge);
        _powerGrid.Update();

        PowerGridConnection consumerConnection = _entityManager
            .GetComponentData<PowerGridConnection>(consumer);
        PowerGridState leftState = _entityManager
            .GetComponentData<PowerGridState>(GetGrid(left));
        PowerGridState rightState = _entityManager
            .GetComponentData<PowerGridState>(GetGrid(right));

        Assert.That(consumerConnection.powerPoleEntity, Is.EqualTo(right));
        Assert.That(leftState.availableGeneration, Is.EqualTo(40f));
        Assert.That(leftState.maximumDemand, Is.EqualTo(0f));
        Assert.That(leftState.supplyRatio, Is.EqualTo(1f));
        Assert.That(rightState.availableGeneration, Is.EqualTo(0f));
        Assert.That(rightState.maximumDemand, Is.EqualTo(10f));
        Assert.That(rightState.actualConsumption, Is.EqualTo(0f));
        Assert.That(rightState.supplyRatio, Is.EqualTo(0f));
    }

    [Test]
    public void MainFacilityBootstrapCreatesReservedOccupantRequestAtOrigin()
    {
        Entity droneConfigEntity = _entityManager.CreateEntity(
            typeof(DroneConfig));
        _entityManager.SetComponentData(
            droneConfigEntity,
            new DroneConfig
            {
                defaultTaskPriority = 5,
                stationStorageCapacity = 10,
                stationActivityRangeInChunks = new int2(1, 1)
            });
        _entityManager.CreateEntity(typeof(StartingItemConfigElement));
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
                size = new int2(1, 1)
            });
        MainFacilityBootstrapSystem bootstrap = _world
            .GetOrCreateSystemManaged<MainFacilityBootstrapSystem>();

        bootstrap.Update();

        Entity mainFacility = _entityManager
            .CreateEntityQuery(typeof(MainFacility))
            .GetSingletonEntity();
        PowerGenerator generator = _entityManager
            .GetComponentData<PowerGenerator>(mainFacility);

        Assert.That(
            _entityManager.GetComponentData<GridPosition>(mainFacility)
                .gridPosition,
            Is.EqualTo(int2.zero));
        Assert.That(
            _entityManager.HasComponent<BuildingOccupantRequest>(mainFacility),
            Is.True);
        Assert.That(
            _entityManager.HasComponent<IndestructibleBuilding>(mainFacility),
            Is.True);
        Assert.That(
            _entityManager.HasComponent<PowerPole>(mainFacility),
            Is.True);
        Assert.That(
            _entityManager.GetComponentData<BuildingFootprint>(mainFacility)
                .size,
            Is.EqualTo(new int2(1, 1)));
        Assert.That(_chunkMap.IsBuildingReserved(int2.zero), Is.True);
        Assert.That(generator.type, Is.EqualTo(PowerGeneratorTypeEnum.MainFacility));
        Assert.That(generator.maximumGeneration, Is.EqualTo(40f));
        Assert.That(generator.currentGeneration, Is.EqualTo(40f));
    }

    private Entity CreateRegisteredPowerPole(int2 cell)
    {
        Entity powerPole = _entityManager.CreateEntity(
            typeof(PowerPole),
            typeof(GridPosition),
            typeof(BuildingFootprint),
            typeof(Direction),
            typeof(BuildingOccupant));
        _entityManager.SetComponentData(
            powerPole,
            new GridPosition { gridPosition = cell });
        SetFootprint(powerPole, new int2(1, 1), DirectionEnum.Up);
        return powerPole;
    }

    private Entity CreateMainFacilityPowerRoot(
        float generation,
        float maximumConsumption)
    {
        Entity mainFacility = _entityManager.CreateEntity(
            typeof(MainFacility),
            typeof(PowerPole),
            typeof(PowerGenerator),
            typeof(PowerConsumer),
            typeof(GridPosition),
            typeof(BuildingFootprint),
            typeof(Direction),
            typeof(BuildingOccupant));
        _entityManager.SetComponentData(
            mainFacility,
            new PowerGenerator
            {
                type = PowerGeneratorTypeEnum.MainFacility,
                maximumGeneration = generation,
                currentGeneration = generation
            });
        _entityManager.SetComponentData(
            mainFacility,
            new PowerConsumer
            {
                maximumConsumption = maximumConsumption
            });
        SetFootprint(mainFacility, new int2(1, 1), DirectionEnum.Up);
        return mainFacility;
    }

    private Entity CreatePowerGenerator(int2 cell, float generation)
    {
        Entity generator = _entityManager.CreateEntity(
            typeof(PowerGenerator),
            typeof(GridPosition),
            typeof(BuildingFootprint),
            typeof(Direction),
            typeof(BuildingOccupant));
        _entityManager.SetComponentData(
            generator,
            new GridPosition { gridPosition = cell });
        _entityManager.SetComponentData(
            generator,
            new PowerGenerator
            {
                type = PowerGeneratorTypeEnum.MainFacility,
                maximumGeneration = generation,
                currentGeneration = generation
            });
        SetFootprint(generator, new int2(1, 1), DirectionEnum.Up);
        return generator;
    }

    private Entity CreatePowerConsumer(
        int2 cell,
        float maximumConsumption,
        int2? footprintSize = null,
        DirectionEnum direction = DirectionEnum.Up)
    {
        Entity consumer = _entityManager.CreateEntity(
            typeof(PowerConsumer),
            typeof(GridPosition),
            typeof(BuildingFootprint),
            typeof(Direction),
            typeof(BuildingOccupant));
        _entityManager.SetComponentData(
            consumer,
            new GridPosition { gridPosition = cell });
        _entityManager.SetComponentData(
            consumer,
            new PowerConsumer
            {
                maximumConsumption = maximumConsumption
            });
        SetFootprint(
            consumer,
            footprintSize ?? new int2(1, 1),
            direction);
        return consumer;
    }

    private Entity CreateCoalGenerator(int2 cell, float remainingFuelEnergy)
    {
        Entity generator = _entityManager.CreateEntity(
            typeof(CoalGenerator),
            typeof(PowerGenerator),
            typeof(GridPosition),
            typeof(BuildingFootprint),
            typeof(Direction),
            typeof(BuildingOccupant),
            typeof(StoredItemElement));
        _entityManager.SetComponentData(
            generator,
            new GridPosition { gridPosition = cell });
        _entityManager.SetComponentData(
            generator,
            new CoalGenerator
            {
                remainingFuelEnergy = remainingFuelEnergy
            });
        _entityManager.SetComponentData(
            generator,
            new PowerGenerator
            {
                type = PowerGeneratorTypeEnum.CoalGenerator,
                maximumGeneration = 100f
            });
        SetFootprint(generator, new int2(1, 1), DirectionEnum.Up);
        return generator;
    }

    private void SetFootprint(
        Entity entity,
        int2 size,
        DirectionEnum direction)
    {
        _entityManager.SetComponentData(
            entity,
            new BuildingFootprint
            {
                size = BuildingFootprintUtility.NormalizeSize(size)
            });
        _entityManager.SetComponentData(
            entity,
            new Direction { dir = direction });
    }

    private Entity GetGrid(Entity powerPole)
    {
        Assert.That(
            _powerGrid.TryGetPowerGrid(powerPole, out Entity powerGrid),
            Is.True);
        return powerGrid;
    }

    private void ReplacePowerConfig(int2 supplyRange, int2 connectionRange)
    {
        Entity configEntity = _entityManager
            .CreateEntityQuery(typeof(PowerConfig))
            .GetSingletonEntity();
        DynamicBuffer<PowerPoleConfigElement> configs =
            _entityManager.GetBuffer<PowerPoleConfigElement>(configEntity);
        configs.Clear();
        configs.Add(CreatePowerPoleConfig(supplyRange, connectionRange));
    }

    private void CreatePowerConfig(int2 supplyRange, int2 connectionRange)
    {
        Entity configEntity = _entityManager.CreateEntity(
            typeof(PowerConfig),
            typeof(CoalGeneratorConfig),
            typeof(PowerPoleConfigElement),
            typeof(PowerGeneratorConfigElement),
            typeof(PowerConsumerConfigElement));
        _entityManager
            .GetBuffer<PowerPoleConfigElement>(configEntity)
            .Add(CreatePowerPoleConfig(supplyRange, connectionRange));
        _entityManager
            .GetBuffer<PowerGeneratorConfigElement>(configEntity)
            .Add(new PowerGeneratorConfigElement
            {
                generatorType = PowerGeneratorTypeEnum.MainFacility,
                maximumGeneration = 40f
            });
        _entityManager.SetComponentData(
            configEntity,
            new CoalGeneratorConfig
            {
                coalEnergyPerItem = 600f,
                fuelStorageCapacity = 10
            });
    }

    private static PowerPoleConfigElement CreatePowerPoleConfig(
        int2 supplyRange,
        int2 connectionRange)
    {
        return new PowerPoleConfigElement
        {
            buildingType = BuildingTypeEnum.PowerPole,
            supplyRange = supplyRange,
            connectionRange = connectionRange
        };
    }
}
