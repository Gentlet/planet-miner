using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[UpdateAfter(typeof(ConstructionCompletionSystem))]
public partial class BuildingSpawnSystem : SystemBase
{
    private ChunkMapSystem _chunkMap;
    private EntityQuery _buildingRuntimeConfigQuery;
    private EntityQuery _powerConfigQuery;
    private EntityQuery _droneConfigQuery;

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
        _buildingRuntimeConfigQuery = GetEntityQuery(
            ComponentType.ReadOnly<BuildingRuntimeConfig>(),
            ComponentType.ReadOnly<BuildingRuntimeConfigElement>());
        _powerConfigQuery = GetEntityQuery(
            ComponentType.ReadOnly<PowerConfig>(),
            ComponentType.ReadOnly<PowerConsumerConfigElement>(),
            ComponentType.ReadOnly<PowerGeneratorConfigElement>());
        _droneConfigQuery = GetEntityQuery(
            ComponentType.ReadOnly<DroneConfig>());
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
        bool hasDroneConfig = !_droneConfigQuery.IsEmptyIgnoreFilter;
        DroneConfig droneConfig = hasDroneConfig
            ? _droneConfigQuery.GetSingleton<DroneConfig>()
            : default;
        bool hasBuildingRuntimeConfig =
            !_buildingRuntimeConfigQuery.IsEmptyIgnoreFilter;
        DynamicBuffer<BuildingRuntimeConfigElement> buildingRuntimeConfigs =
            hasBuildingRuntimeConfig
                ? _buildingRuntimeConfigQuery
                    .GetSingletonBuffer<BuildingRuntimeConfigElement>(true)
                : default;

        foreach (var (request, requestEntity) in
                 SystemAPI.Query<RefRO<BuildingSpawnRequest>>().WithEntityAccess())
        {
            BuildingSpawnRequest spawnRequest = request.ValueRO;
            BuildingRuntimeConfigElement runtimeConfig = default;

            if (BuildingRuntimeConfigParser.RequiresRuntimeConfig(
                    spawnRequest.type) &&
                (!hasBuildingRuntimeConfig ||
                 !TryGetRuntimeConfig(
                     buildingRuntimeConfigs,
                     spawnRequest.type,
                     out runtimeConfig)))
            {
                Debug.LogError(
                    $"Building spawn request was rejected because its runtime config was not loaded. Type : {spawnRequest.type}");
                _chunkMap.ReleaseBuildingReservation(
                    spawnRequest.gridPosition,
                    spawnRequest.type,
                    spawnRequest.dir);
                ecb.DestroyEntity(requestEntity);
                continue;
            }

            if (spawnRequest.type == BuildingTypeEnum.DroneStation &&
                !hasDroneConfig)
            {
                Debug.LogError(
                    "Drone station spawn request was rejected because the drone config was not loaded.");
                _chunkMap.ReleaseBuildingReservation(
                    spawnRequest.gridPosition,
                    spawnRequest.type,
                    spawnRequest.dir);
                ecb.DestroyEntity(requestEntity);
                continue;
            }

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
            int2 footprintSize = BuildingFootprintUtility.NormalizeSize(
                definition.size);
            float2 visualCenterOffset =
                BuildingFootprintUtility.GetVisualCenterOffset(
                    footprintSize,
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
            ecb.AddComponent(
                instance,
                new PostTransformMatrix
                {
                    Value = float4x4.Scale(
                        new float3(footprintSize.x, footprintSize.y, 1f))
                });
            ecb.AddComponent(instance, new BuildingType { type = spawnRequest.type });
            ecb.AddComponent(instance, new GridPosition { gridPosition = anchor });
            ecb.AddComponent(instance, new Direction { dir = direction });
            ecb.AddComponent(instance, new BuildingFootprint { size = footprintSize });
            ecb.AddComponent(instance, new BuildingOccupantRequest());

            AddBuildingBehavior(
                ref ecb,
                instance,
                spawnRequest,
                anchor,
                footprintSize,
                runtimeConfig,
                droneConfig);
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
        int2 anchor,
        int2 footprintSize,
        BuildingRuntimeConfigElement runtimeConfig,
        DroneConfig droneConfig)
    {
        switch (request.type)
        {
            case BuildingTypeEnum.Belt:
                AddBeltBehavior(ref ecb, instance, runtimeConfig);
                break;
            case BuildingTypeEnum.Miner:
                AddMinerBehavior(ref ecb, instance, anchor, runtimeConfig);
                break;
            case BuildingTypeEnum.Crafter:
                AddCrafterBehavior(
                    ref ecb,
                    instance,
                    request.selectedItemType,
                    runtimeConfig);
                break;
            case BuildingTypeEnum.Splitter:
                ecb.AddComponent(instance, new Splitter
                {
                    nextOutputDirection = request.dir
                });
                ecb.AddBuffer<SplitterRetainedItemElement>(instance);
                break;
            case BuildingTypeEnum.Merger:
                ecb.AddComponent(instance, new Merger
                {
                    nextInputDirection =
                        request.dir.NextDirection().NextDirection()
                });
                break;
            case BuildingTypeEnum.Storage:
                AddStorageBehavior(ref ecb, instance, runtimeConfig);
                break;
            case BuildingTypeEnum.PowerPole:
                ecb.AddComponent(instance, new PowerPole());
                break;
            case BuildingTypeEnum.CoalGenerator:
                ecb.AddComponent(instance, new CoalGenerator());
                ecb.AddBuffer<StoredItemElement>(instance);
                break;
            case BuildingTypeEnum.DroneStation:
                ecb.AddComponent(
                    instance,
                    new Storage
                    {
                        capacity = droneConfig.stationStorageCapacity
                });
                ecb.AddBuffer<StoredItemElement>(instance);
                ecb.AddBuffer<StoredDroneElement>(instance);
                ecb.AddComponent(instance, new PowerConsumer());
                ecb.AddComponent(
                    instance,
                    new DroneStation
                    {
                        activityRangeInChunks =
                            droneConfig.stationActivityRangeInChunks,
                        isMainStation = false
                    });
                break;
            case BuildingTypeEnum.ResearchBuilding:
                ecb.AddComponent(instance, new ResearchBuilding
                {
                    speed = runtimeConfig.speed,
                    state = ResearchBuildingStateEnum.NoActiveResearch
                });
                ecb.AddBuffer<StoredItemElement>(instance);
                break;
        }
    }

    private static void AddBeltBehavior(
        ref EntityCommandBuffer ecb,
        Entity instance,
        BuildingRuntimeConfigElement config)
    {
        ecb.AddComponent(instance, new Belt { speed = config.speed });
    }

    private static void AddMinerBehavior(
        ref EntityCommandBuffer ecb,
        Entity instance,
        int2 anchor,
        BuildingRuntimeConfigElement config)
    {
        uint randomState = math.hash(anchor);
        ecb.AddComponent(instance, new Miner
        {
            speed = config.speed,
            randomState = randomState == 0 ? 1u : randomState
        });
        ecb.AddComponent(instance, new BuildingOutputCursor());
        ecb.AddBuffer<ProducedItemElement>(instance);
    }

    private static void AddCrafterBehavior(
        ref EntityCommandBuffer ecb,
        Entity instance,
        ItemTypeEnum selectedItemType,
        BuildingRuntimeConfigElement config)
    {
        ecb.AddComponent(instance, new Crafter
        {
            speed = config.speed,
            selectedItemType = selectedItemType,
            progress = 0f,
            state = selectedItemType.IsValid()
                ? CrafterStateEnum.Idle
                : CrafterStateEnum.NoRecipe
        });
        ecb.AddComponent(instance, new BuildingOutputCursor());
        ecb.AddBuffer<StoredItemElement>(instance);
        ecb.AddBuffer<ProducedItemElement>(instance);
    }

    private static void AddStorageBehavior(
        ref EntityCommandBuffer ecb,
        Entity instance,
        BuildingRuntimeConfigElement config)
    {
        ecb.AddComponent(instance, new Storage
        {
            capacity = config.storageCapacity
        });
        ecb.AddComponent(instance, new BuildingOutputCursor());
        ecb.AddBuffer<StoredItemElement>(instance);
    }

    private static bool TryGetRuntimeConfig(
        DynamicBuffer<BuildingRuntimeConfigElement> configs,
        BuildingTypeEnum buildingType,
        out BuildingRuntimeConfigElement config)
    {
        for (int i = 0; i < configs.Length; i++)
        {
            if (configs[i].buildingType != buildingType)
                continue;

            config = configs[i];
            return true;
        }

        config = default;
        return false;
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
