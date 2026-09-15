using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

[UpdateAfter(typeof(ChunkMapSystem))]
[UpdateAfter(typeof(CoalGeneratorInputSystem))]
[UpdateBefore(typeof(CoalGeneratorFuelSystem))]
[UpdateBefore(typeof(MiningSystem))]
[UpdateBefore(typeof(BuildingDestroySystem))]
public partial class PowerGridSystem : SystemBase
{
    private readonly Dictionary<Entity, PowerPoleTopologyData> _powerPoles = new();
    private readonly List<PowerPoleTopologyData> _orderedPowerPoles = new();
    private readonly List<Entity> _powerGridEntities = new();
    private ChunkMapSystem _chunkMap;
    private ItemStorageSystem _itemStorage;
    private EntityQuery _unregisteredPowerPoleQuery;
    private EntityQuery _powerParticipantQuery;
    private EntityQuery _powerGeneratorQuery;
    private EntityQuery _powerConsumerQuery;
    private int _nextStablePowerPoleId = 1;
    private bool _topologyDirty;

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
        _itemStorage = World.GetExistingSystemManaged<ItemStorageSystem>();
        _unregisteredPowerPoleQuery = GetEntityQuery(new EntityQueryDesc
        {
            All = new[]
            {
                ComponentType.ReadOnly<PowerPole>(),
                ComponentType.ReadOnly<GridPosition>(),
                ComponentType.ReadOnly<BuildingOccupant>()
            },
            None = new[]
            {
                ComponentType.ReadOnly<PowerGridConnection>()
            }
        });
        _powerParticipantQuery = GetEntityQuery(new EntityQueryDesc
        {
            All = new[]
            {
                ComponentType.ReadOnly<GridPosition>(),
                ComponentType.ReadOnly<BuildingFootprint>(),
                ComponentType.ReadOnly<Direction>(),
                ComponentType.ReadOnly<BuildingOccupant>()
            },
            Any = new[]
            {
                ComponentType.ReadOnly<PowerGenerator>(),
                ComponentType.ReadOnly<PowerConsumer>()
            }
        });
        _powerGeneratorQuery = GetEntityQuery(
            ComponentType.ReadOnly<PowerGenerator>(),
            ComponentType.ReadOnly<BuildingOccupant>());
        _powerConsumerQuery = GetEntityQuery(
            ComponentType.ReadWrite<PowerConsumer>(),
            ComponentType.ReadOnly<BuildingOccupant>());
        RequireForUpdate<PowerConfig>();
    }

    protected override void OnUpdate()
    {
        if (_chunkMap == null)
            _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();

        if (_chunkMap == null)
            return;

        if (_itemStorage == null)
            _itemStorage = World.GetExistingSystemManaged<ItemStorageSystem>();

        if (_itemStorage == null)
            return;

        DynamicBuffer<PowerPoleConfigElement> configs =
            SystemAPI.GetSingletonBuffer<PowerPoleConfigElement>(true);

        if (!PowerGridRangeUtility.TryGetPowerPoleConfig(
                configs,
                out PowerPoleConfigElement config))
        {
            Debug.LogError("Power pole config was not found.");
            return;
        }

        RemoveUnregisteredPowerPoleConnections();
        bool topologyChanged = RegisterPendingPowerPoles(config);

        if (_topologyDirty || topologyChanged)
        {
            RebuildTopology();
            _topologyDirty = false;
        }

        ReconnectPowerParticipants();
        UpdatePowerGridStates(SystemAPI.Time.DeltaTime);
    }

    protected override void OnDestroy()
    {
        if (_chunkMap != null)
            UnregisterAllPowerPoleSupplyRanges();

        DestroyPowerGridEntities();
        _powerPoles.Clear();
    }

}
