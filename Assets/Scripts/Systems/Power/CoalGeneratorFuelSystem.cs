using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[UpdateAfter(typeof(PowerGridSystem))]
[UpdateBefore(typeof(MiningSystem))]
[UpdateBefore(typeof(CrafterSystem))]
public partial class CoalGeneratorFuelSystem : SystemBase
{
    private ItemStorageSystem _itemStorage;
    private EntityQuery _generatorQuery;

    protected override void OnCreate()
    {
        _itemStorage = World.GetExistingSystemManaged<ItemStorageSystem>();
        _generatorQuery = GetEntityQuery(
            ComponentType.ReadWrite<CoalGenerator>(),
            ComponentType.ReadWrite<PowerGenerator>(),
            ComponentType.ReadOnly<StoredItemElement>());
        RequireForUpdate<CoalGenerator>();
        RequireForUpdate<CoalGeneratorConfig>();
    }

    protected override void OnUpdate()
    {
        if (_itemStorage == null)
            _itemStorage = World.GetExistingSystemManaged<ItemStorageSystem>();

        if (_itemStorage == null)
            return;

        float deltaTime = SystemAPI.Time.DeltaTime;
        CoalGeneratorConfig config =
            SystemAPI.GetSingleton<CoalGeneratorConfig>();
        EntityCommandBuffer ecb = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(World.Unmanaged);

        using NativeArray<Entity> generators =
            _generatorQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < generators.Length; i++)
        {
            Entity generatorEntity = generators[i];
            PowerGenerator generator = EntityManager
                .GetComponentData<PowerGenerator>(generatorEntity);
            float requiredFuelEnergy =
                CoalGeneratorPowerUtility.GetConsumedFuelEnergy(
                    generator.currentGeneration,
                    deltaTime);

            if (requiredFuelEnergy <= 0f)
                continue;

            CoalGenerator coalGenerator = EntityManager
                .GetComponentData<CoalGenerator>(generatorEntity);

            while (coalGenerator.remainingFuelEnergy < requiredFuelEnergy)
            {
                if (!_itemStorage.TryConsumeStoredItem(
                        ref ecb,
                        generatorEntity,
                        ItemTypeEnum.Coal))
                    break;

                coalGenerator.remainingFuelEnergy += config.coalEnergyPerItem;
            }

            float consumedFuelEnergy = math.min(
                coalGenerator.remainingFuelEnergy,
                requiredFuelEnergy);
            coalGenerator.remainingFuelEnergy -= consumedFuelEnergy;
            EntityManager.SetComponentData(generatorEntity, coalGenerator);

            if (consumedFuelEnergy >= requiredFuelEnergy)
                continue;

            generator.currentGeneration = deltaTime > 0f
                ? consumedFuelEnergy / deltaTime
                : 0f;
            EntityManager.SetComponentData(generatorEntity, generator);
        }
    }
}
