using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[UpdateAfter(typeof(PowerConfigLoadSystem))]
[UpdateBefore(typeof(ChunkMapSystem))]
public partial class MainFacilityBootstrapSystem : SystemBase
{
    private static readonly int2 MainFacilityCell = int2.zero;

    private ChunkMapSystem _chunkMap;
    private EntityQuery _mainFacilityQuery;
    private readonly List<int2> _footprintCells = new();
    private readonly List<int2> _reservedCells = new();
    private bool _loggedReservationFailure;

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
        _mainFacilityQuery = GetEntityQuery(
            ComponentType.ReadOnly<MainFacility>());
        RequireForUpdate<PowerConfig>();
        RequireForUpdate<DroneConfig>();
        RequireForUpdate<StartingItemConfigElement>();
        RequireForUpdate<BuildingPrefabElement>();
    }

    protected override void OnUpdate()
    {
        if (!_mainFacilityQuery.IsEmptyIgnoreFilter)
        {
            Enabled = false;
            return;
        }

        if (_chunkMap == null)
            _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
        if (_chunkMap == null)
            return;

        DynamicBuffer<BuildingPrefabElement> definitions =
            SystemAPI.GetSingletonBuffer<BuildingPrefabElement>(true);
        if (!definitions.TryGetDefinition(
                BuildingTypeEnum.MainFacility,
                out BuildingPrefabElement definition))
        {
            Debug.LogError("Main facility prefab definition was not found.");
            Enabled = false;
            return;
        }
        if (definition.prefab == Entity.Null)
        {
            Debug.LogError("Main facility prefab entity was null.");
            Enabled = false;
            return;
        }

        DynamicBuffer<PowerGeneratorConfigElement> generatorConfigs =
            SystemAPI.GetSingletonBuffer<PowerGeneratorConfigElement>(true);
        if (!TryGetMainFacilityGeneration(
                generatorConfigs,
                out float maximumGeneration))
        {
            Debug.LogError("Main facility power generator config was not found.");
            Enabled = false;
            return;
        }

        DroneConfig droneConfig = SystemAPI.GetSingleton<DroneConfig>();
        using NativeArray<StartingItemConfigElement> startingItemConfigs =
            DynamicBufferCopyUtility.CreateNativeCopy(
                SystemAPI.GetSingletonBuffer<StartingItemConfigElement>(true),
                Allocator.Temp);

        int2 footprintSize = BuildingFootprintUtility.NormalizeSize(definition.size);
        if (!TryReserveFootprint(footprintSize))
        {
            if (!_loggedReservationFailure)
            {
                Debug.LogError(
                    $"Main facility could not reserve its bootstrap footprint. Anchor: {MainFacilityCell}, Size: {footprintSize}");
                _loggedReservationFailure = true;
            }
            return;
        }

        Entity mainFacility = EntityManager.Instantiate(definition.prefab);
        float2 visualCenterOffset =
            BuildingFootprintUtility.GetVisualCenterOffset(
                footprintSize,
                DirectionEnum.Up);
        LocalTransform transform = LocalTransform.FromPosition(
            new float3(
                MainFacilityCell.x + visualCenterOffset.x,
                MainFacilityCell.y + visualCenterOffset.y,
                0f));

        if (EntityManager.HasComponent<LocalTransform>(mainFacility))
            EntityManager.SetComponentData(mainFacility, transform);
        else
            EntityManager.AddComponentData(mainFacility, transform);

        EntityManager.AddComponentData(
            mainFacility,
            new PostTransformMatrix
            {
                Value = float4x4.Scale(
                    new float3(footprintSize.x, footprintSize.y, 1f))
            });

        EntityManager.AddComponentData(
            mainFacility,
            new BuildingType { type = BuildingTypeEnum.MainFacility });
        EntityManager.AddComponentData(
            mainFacility,
            new GridPosition { gridPosition = MainFacilityCell });
        EntityManager.AddComponentData(
            mainFacility,
            new Direction { dir = DirectionEnum.Up });
        EntityManager.AddComponent<BuildingOccupantRequest>(mainFacility);
        EntityManager.AddComponent<MainFacility>(mainFacility);
        EntityManager.AddComponent<IndestructibleBuilding>(mainFacility);
        EntityManager.AddComponentData(
            mainFacility,
            new Storage
            {
                capacity = droneConfig.stationStorageCapacity
            });
        EntityManager.AddBuffer<StoredItemElement>(mainFacility);
        EntityManager.AddBuffer<StoredDroneElement>(mainFacility);
        EntityManager.AddComponentData(
            mainFacility,
            new DroneStation
            {
                activityRangeInChunks =
                    droneConfig.stationActivityRangeInChunks,
                footprintSize = footprintSize,
                isMainStation = true
            });
        EntityManager.AddComponentData(
            mainFacility,
            new PowerConsumer());
        EntityManager.AddComponentData(
            mainFacility,
            new PowerGenerator
            {
                type = PowerGeneratorTypeEnum.MainFacility,
                maximumGeneration = maximumGeneration,
                currentGeneration = maximumGeneration
            });
        EntityManager.AddComponent<PowerPole>(mainFacility);

        CreateStartingItemRequests(mainFacility, startingItemConfigs);

        Enabled = false;
    }

    private void CreateStartingItemRequest(
        Entity mainFacility,
        ItemTypeEnum itemType,
        int quantity)
    {
        if (quantity <= 0)
            return;

        for (int i = 0; i < quantity; i++)
        {
            Entity requestEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(
                requestEntity,
                new StartingItemSpawnRequest
                {
                    owner = mainFacility,
                    itemType = itemType
                });
        }
    }

    private void CreateStartingItemRequests(
        Entity mainFacility,
        NativeArray<StartingItemConfigElement> itemConfigs)
    {
        for (int i = 0; i < itemConfigs.Length; i++)
        {
            StartingItemConfigElement itemConfig = itemConfigs[i];
            CreateStartingItemRequest(
                mainFacility,
                itemConfig.itemType,
                itemConfig.quantity);
        }
    }

    private bool TryReserveFootprint(int2 footprintSize)
    {
        BuildingFootprintUtility.GetOccupiedCells(
            MainFacilityCell,
            footprintSize,
            DirectionEnum.Up,
            _footprintCells);
        _reservedCells.Clear();

        for (int i = 0; i < _footprintCells.Count; i++)
        {
            int2 cell = _footprintCells[i];

            if (_chunkMap.TryReserveBuilding(cell))
            {
                _reservedCells.Add(cell);
                continue;
            }

            BuildingPlacementReservationUtility.Release(
                _chunkMap,
                _reservedCells);
            return false;
        }

        return true;
    }

    private static bool TryGetMainFacilityGeneration(
        DynamicBuffer<PowerGeneratorConfigElement> configs,
        out float maximumGeneration)
    {
        for (int i = 0; i < configs.Length; i++)
        {
            if (configs[i].generatorType != PowerGeneratorTypeEnum.MainFacility)
                continue;

            maximumGeneration = configs[i].maximumGeneration;
            return true;
        }

        maximumGeneration = 0f;
        return false;
    }
}
