using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public partial class BuildingSpawnSystem : SystemBase
{
    private const int DefaultStorageCapacity = 10;
    private ChunkMapSystem _chunkMap;
    private EntityQuery _powerConfigQuery;

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
        _powerConfigQuery = GetEntityQuery(
            ComponentType.ReadOnly<PowerConfig>(),
            ComponentType.ReadOnly<PowerConsumerConfigElement>(),
            ComponentType.ReadOnly<PowerGeneratorConfigElement>());
        RequireForUpdate<BuildingPrefabElement>();
        RequireForUpdate<BuildingSpawnRequest>();
    }

    protected override void OnUpdate()
    {
        if (_chunkMap == null)
        {
            _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();

            if (_chunkMap == null)
                return;
        }

        DynamicBuffer<BuildingPrefabElement> definitions =
            SystemAPI.GetSingletonBuffer<BuildingPrefabElement>(true);
        EntityCommandBuffer ecb = SystemAPI
            .GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(World.Unmanaged);

        if (_powerConfigQuery.IsEmptyIgnoreFilter)
        {
            Debug.LogError(
                "Building spawn requests were rejected because the power config was not loaded.");

            foreach (var (request, requestEntity) in
                     SystemAPI.Query<RefRO<BuildingSpawnRequest>>()
                         .WithEntityAccess())
            {
                BuildingSpawnRequest spawnRequest = request.ValueRO;
                _chunkMap.ReleaseBuildingReservation(
                    spawnRequest.gridPosition,
                    spawnRequest.type,
                    spawnRequest.dir);
                ecb.DestroyEntity(requestEntity);
            }

            return;
        }

        DynamicBuffer<PowerConsumerConfigElement> powerConsumerConfigs =
            _powerConfigQuery
                .GetSingletonBuffer<PowerConsumerConfigElement>(true);
        DynamicBuffer<PowerGeneratorConfigElement> powerGeneratorConfigs =
            _powerConfigQuery
                .GetSingletonBuffer<PowerGeneratorConfigElement>(true);

        foreach (var (request, requestEntity) in
                 SystemAPI.Query<RefRO<BuildingSpawnRequest>>().WithEntityAccess())
        {
            BuildingSpawnRequest spawnRequest = request.ValueRO;

            if (!definitions.TryGetDefinition(
                    spawnRequest.type,
                    out BuildingPrefabElement definition))
            {
                _chunkMap.ReleaseBuildingReservation(
                    spawnRequest.gridPosition,
                    spawnRequest.type,
                    spawnRequest.dir);
                ecb.DestroyEntity(requestEntity);
                continue;
            }
            if (definition.prefab == Entity.Null)
            {
                _chunkMap.ReleaseBuildingReservation(
                    spawnRequest.gridPosition,
                    spawnRequest.type,
                    spawnRequest.dir);
                ecb.DestroyEntity(requestEntity);
                continue;
            }

            Entity instance = ecb.Instantiate(definition.prefab);
            int2 anchor = spawnRequest.gridPosition;
            DirectionEnum direction = spawnRequest.dir;
            float2 visualCenterOffset =
                BuildingFootprintUtility.GetVisualCenterOffset(
                    definition.size,
                    direction);

            ecb.SetComponent(
                instance,
                LocalTransform.FromPositionRotation(
                    new float3(
                        anchor.x + visualCenterOffset.x,
                        anchor.y + visualCenterOffset.y,
                        0f),
                    quaternion.RotateZ(
                        Mathf.Deg2Rad * direction.ToDegrees())));
            ecb.AddComponent(instance, new BuildingType { type = spawnRequest.type });
            ecb.AddComponent(instance, new GridPosition { gridPosition = anchor });
            ecb.AddComponent(instance, new Direction { dir = direction });
            ecb.AddComponent(instance, new BuildingOccupantRequest());

            AddBuildingBehavior(ref ecb, instance, spawnRequest, anchor);
            AddPowerComponents(
                ref ecb,
                instance,
                spawnRequest.type,
                powerConsumerConfigs,
                powerGeneratorConfigs);
            ecb.DestroyEntity(requestEntity);
        }
    }

    private static void AddBuildingBehavior(
        ref EntityCommandBuffer ecb,
        Entity instance,
        BuildingSpawnRequest request,
        int2 anchor)
    {
        switch (request.type)
        {
            case BuildingTypeEnum.Belt:
                ecb.AddComponent(instance, new Belt { speed = 10f });
                break;
            case BuildingTypeEnum.Miner:
                uint randomState = math.hash(anchor);
                ecb.AddComponent(instance, new Miner
                {
                    speed = 0.1f,
                    randomState = randomState == 0 ? 1u : randomState
                });
                ecb.AddComponent(instance, new BuildingOutputCursor());
                ecb.AddBuffer<ProducedItemElement>(instance);
                break;
            case BuildingTypeEnum.Crafter:
                ItemTypeEnum selectedItemType = request.selectedItemType;
                ecb.AddComponent(instance, new Crafter
                {
                    speed = 1f,
                    selectedItemType = selectedItemType,
                    progress = 0f,
                    state = selectedItemType.IsValid()
                        ? CrafterStateEnum.Idle
                        : CrafterStateEnum.NoRecipe
                });
                ecb.AddComponent(instance, new BuildingOutputCursor());
                ecb.AddBuffer<StoredItemElement>(instance);
                ecb.AddBuffer<ProducedItemElement>(instance);
                break;
            case BuildingTypeEnum.Splitter:
                ecb.AddComponent(instance, new Splitter
                {
                    nextOutputDirection = request.dir
                });
                break;
            case BuildingTypeEnum.Merger:
                ecb.AddComponent(instance, new Merger
                {
                    nextInputDirection =
                        request.dir.NextDirection().NextDirection()
                });
                break;
            case BuildingTypeEnum.Storage:
                ecb.AddComponent(instance, new Storage
                {
                    capacity = DefaultStorageCapacity
                });
                ecb.AddComponent(instance, new BuildingOutputCursor());
                ecb.AddBuffer<StoredItemElement>(instance);
                break;
            case BuildingTypeEnum.PowerPole:
                ecb.AddComponent(instance, new PowerPole());
                break;
            case BuildingTypeEnum.CoalGenerator:
                ecb.AddComponent(instance, new CoalGenerator());
                ecb.AddBuffer<StoredItemElement>(instance);
                break;
        }
    }

    private static void AddPowerComponents(
        ref EntityCommandBuffer ecb,
        Entity instance,
        BuildingTypeEnum type,
        DynamicBuffer<PowerConsumerConfigElement> consumerConfigs,
        DynamicBuffer<PowerGeneratorConfigElement> generatorConfigs)
    {
        AddPowerConsumerIfConfigured(
            ref ecb,
            instance,
            type,
            consumerConfigs);

        if (type == BuildingTypeEnum.CoalGenerator)
        {
            AddPowerGeneratorIfConfigured(
                ref ecb,
                instance,
                PowerGeneratorTypeEnum.CoalGenerator,
                generatorConfigs);
        }
    }

    private static void AddPowerConsumerIfConfigured(
        ref EntityCommandBuffer ecb,
        Entity instance,
        BuildingTypeEnum type,
        DynamicBuffer<PowerConsumerConfigElement> configs)
    {
        for (int i = 0; i < configs.Length; i++)
        {
            if (configs[i].buildingType != type)
                continue;

            ecb.AddComponent(
                instance,
                new PowerConsumer
                {
                    maximumConsumption = configs[i].maximumConsumption,
                    currentConsumption = 0f,
                    supplyRatio = 0f
                });
            return;
        }
    }

    private static void AddPowerGeneratorIfConfigured(
        ref EntityCommandBuffer ecb,
        Entity instance,
        PowerGeneratorTypeEnum generatorType,
        DynamicBuffer<PowerGeneratorConfigElement> configs)
    {
        for (int i = 0; i < configs.Length; i++)
        {
            if (configs[i].generatorType != generatorType)
                continue;

            ecb.AddComponent(
                instance,
                new PowerGenerator
                {
                    type = generatorType,
                    maximumGeneration = configs[i].maximumGeneration,
                    currentGeneration = 0f
                });
            return;
        }
    }
}
