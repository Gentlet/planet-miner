using System.Collections.Generic;
using Unity.Entities;

[UpdateAfter(typeof(ChunkMapSystem))]
public partial class DroneStationNetworkSystem : SystemBase
{
    private ChunkMapSystem _chunkMap;
    private EntityQuery _stationQuery;
    private readonly Dictionary<Entity, StationRegistration>
        _registrations = new();
    private readonly List<Entity> _currentStations = new();
    private readonly HashSet<Entity> _currentStationSet = new();
    private readonly List<Entity> _staleStations = new();
    private readonly List<Entity> _networkStations = new();
    private readonly List<GridBounds> _networkBounds = new();
    private readonly List<int> _parents = new();
    private readonly Dictionary<int, int> _networkIdByRoot = new();
    private readonly List<Entity> _coveringStations = new();

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
        _stationQuery = GetEntityQuery(
            ComponentType.ReadOnly<DroneStation>(),
            ComponentType.ReadOnly<GridPosition>(),
            ComponentType.ReadOnly<BuildingOccupant>());
    }

    protected override void OnUpdate()
    {
        if (!EnsureChunkMap())
            return;

        SynchronizeStationRegistrations();
        RebuildNetworkTopology();
    }

    private bool EnsureChunkMap()
    {
        if (_chunkMap == null)
            _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();

        return _chunkMap != null;
    }

    private readonly struct StationRegistration
    {
        public StationRegistration(GridBounds activityBounds)
        {
            ActivityBounds = activityBounds;
        }

        public GridBounds ActivityBounds { get; }
    }
}
