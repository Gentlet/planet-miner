using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public partial class PowerGridSystem
{
    private readonly List<GeneratorDispatchEntry> _generatorDispatchEntries =
        new();
    private readonly Dictionary<Entity, float> _standardCapacityByGrid = new();
    private readonly Dictionary<Entity, float> _coalCapacityByGrid = new();

    private void AccumulateAvailableGeneration(float deltaTime)
    {
        _generatorDispatchEntries.Clear();
        _standardCapacityByGrid.Clear();
        _coalCapacityByGrid.Clear();
        CoalGeneratorConfig coalConfig =
            SystemAPI.GetSingleton<CoalGeneratorConfig>();

        using NativeArray<Entity> generators =
            _powerGeneratorQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < generators.Length; i++)
        {
            Entity generatorEntity = generators[i];
            PowerGenerator generator = EntityManager
                .GetComponentData<PowerGenerator>(generatorEntity);
            generator.currentGeneration = 0f;
            EntityManager.SetComponentData(generatorEntity, generator);

            if (!TryGetConnectedGridState(
                    generatorEntity,
                    out Entity powerGridEntity,
                    out PowerGridState state))
                continue;

            bool isCoalGenerator =
                generator.type == PowerGeneratorTypeEnum.CoalGenerator;
            float availableCapacity = isCoalGenerator
                ? GetCoalGeneratorCapacity(
                    generatorEntity,
                    generator,
                    coalConfig,
                    deltaTime)
                : math.max(0f, generator.maximumGeneration);

            state.availableGeneration += availableCapacity;
            AddConnectedBuilding(generatorEntity, ref state);
            _powerGridStates[powerGridEntity] = state;
            AddCapacity(
                isCoalGenerator
                    ? _coalCapacityByGrid
                    : _standardCapacityByGrid,
                powerGridEntity,
                availableCapacity);
            _generatorDispatchEntries.Add(new GeneratorDispatchEntry
            {
                generatorEntity = generatorEntity,
                powerGridEntity = powerGridEntity,
                availableCapacity = availableCapacity,
                isCoalGenerator = isCoalGenerator
            });
        }
    }

    private float GetCoalGeneratorCapacity(
        Entity generatorEntity,
        in PowerGenerator generator,
        in CoalGeneratorConfig config,
        float deltaTime)
    {
        if (!EntityManager.HasComponent<CoalGenerator>(generatorEntity))
            return 0f;

        CoalGenerator coalGenerator = EntityManager
            .GetComponentData<CoalGenerator>(generatorEntity);
        int storedCoalCount = _itemStorage.GetStoredItemCount(
            generatorEntity,
            ItemTypeEnum.Coal);
        return CoalGeneratorPowerUtility.GetAvailableGeneration(
            generator.maximumGeneration,
            coalGenerator.remainingFuelEnergy,
            storedCoalCount,
            config.coalEnergyPerItem,
            deltaTime);
    }

    private void DispatchGenerators()
    {
        for (int i = 0; i < _generatorDispatchEntries.Count; i++)
        {
            GeneratorDispatchEntry entry = _generatorDispatchEntries[i];
            PowerGridState state = _powerGridStates[entry.powerGridEntity];
            float standardCapacity = GetCapacity(
                _standardCapacityByGrid,
                entry.powerGridEntity);
            float coalCapacity = GetCapacity(
                _coalCapacityByGrid,
                entry.powerGridEntity);
            float standardGeneration = math.min(
                standardCapacity,
                state.actualConsumption);
            float coalGeneration = math.max(
                0f,
                state.actualConsumption - standardGeneration);
            float activationRatio = entry.isCoalGenerator
                ? CoalGeneratorPowerUtility.GetActivationRatio(
                    coalGeneration,
                    coalCapacity)
                : CoalGeneratorPowerUtility.GetActivationRatio(
                    standardGeneration,
                    standardCapacity);
            PowerGenerator generator = EntityManager
                .GetComponentData<PowerGenerator>(entry.generatorEntity);
            generator.currentGeneration =
                entry.availableCapacity * activationRatio;
            EntityManager.SetComponentData(entry.generatorEntity, generator);
        }
    }

    private static void AddCapacity(
        Dictionary<Entity, float> capacityByGrid,
        Entity powerGridEntity,
        float capacity)
    {
        capacityByGrid.TryGetValue(powerGridEntity, out float currentCapacity);
        capacityByGrid[powerGridEntity] = currentCapacity + capacity;
    }

    private static float GetCapacity(
        Dictionary<Entity, float> capacityByGrid,
        Entity powerGridEntity)
    {
        return capacityByGrid.TryGetValue(
            powerGridEntity,
            out float capacity)
            ? capacity
            : 0f;
    }

    private struct GeneratorDispatchEntry
    {
        public Entity generatorEntity;
        public Entity powerGridEntity;
        public float availableCapacity;
        public bool isCoalGenerator;
    }
}
