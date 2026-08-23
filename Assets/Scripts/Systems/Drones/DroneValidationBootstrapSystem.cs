using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateAfter(typeof(MainFacilityBootstrapSystem))]
[UpdateAfter(typeof(DroneStationNetworkSystem))]
[DisableAutoCreation]
public partial class DroneValidationBootstrapSystem : SystemBase
{
    private EntityQuery _activeDroneQuery;
    private EntityQuery _mainStationQuery;

    protected override void OnCreate()
    {
        _activeDroneQuery = GetEntityQuery(
            ComponentType.ReadOnly<ActiveDrone>());
        _mainStationQuery = GetEntityQuery(
            ComponentType.ReadOnly<DroneStation>(),
            ComponentType.ReadOnly<DroneStationNetwork>(),
            ComponentType.ReadOnly<GridPosition>(),
            ComponentType.ReadOnly<BuildingOccupant>());
        RequireForUpdate<DroneConfig>();
        RequireForUpdate<DronePrefab>();
    }

    protected override void OnUpdate()
    {
        if (!_activeDroneQuery.IsEmptyIgnoreFilter)
        {
            Enabled = false;
            return;
        }

        if (!TryGetMainStation(out Entity stationEntity))
            return;

        DroneConfig config = SystemAPI.GetSingleton<DroneConfig>();
        Entity dronePrefab = SystemAPI.GetSingleton<DronePrefab>().value;

        if (dronePrefab == Entity.Null)
            return;

        if (!EntityManager.Exists(dronePrefab))
            return;

        GridPosition stationPosition = EntityManager
            .GetComponentData<GridPosition>(stationEntity);
        DroneStationNetwork network = EntityManager
            .GetComponentData<DroneStationNetwork>(stationEntity);
        Entity droneEntity = EntityManager.Instantiate(dronePrefab);
        LocalTransform transform = EntityManager
            .GetComponentData<LocalTransform>(droneEntity);
        transform.Position = new float3(
            stationPosition.gridPosition.x,
            stationPosition.gridPosition.y,
            -0.2f);
        EntityManager.SetComponentData(droneEntity, transform);
        EntityManager.AddComponentData(
            droneEntity,
            new GridPosition
            {
                gridPosition = stationPosition.gridPosition
            });
        EntityManager.AddComponentData(
            droneEntity,
            new ActiveDrone
            {
                carryingCapacity = config.carryingCapacity,
                movementSpeed = config.movementSpeed,
                emergencyMovementSpeed = config.emergencyMovementSpeed
            });
        EntityManager.AddComponentData(
            droneEntity,
            new DroneBattery
            {
                current = config.maximumBattery,
                maximum = config.maximumBattery,
                consumptionPerDistance =
                    config.batteryConsumptionPerDistance
            });
        EntityManager.AddComponentData(
            droneEntity,
            new DroneState { value = DroneStateEnum.Stored });
        EntityManager.AddComponentData(
            droneEntity,
            new DroneAssignment
            {
                returnStation = stationEntity,
                networkId = network.networkId
            });
        EntityManager.AddComponentData(
            droneEntity,
            new DroneCargo { itemType = ItemTypeEnum.None });
        EntityManager.AddBuffer<StoredItemElement>(droneEntity);
        DroneStationStorageUtility.TryAddStoredDrone(
            EntityManager,
            stationEntity,
            droneEntity);
        EntityManager.AddComponent<ValidationDrone>(droneEntity);
        Enabled = false;
    }

    private bool TryGetMainStation(out Entity stationEntity)
    {
        stationEntity = Entity.Null;
        using NativeArray<Entity> stations =
            _mainStationQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < stations.Length; i++)
        {
            DroneStation station = EntityManager
                .GetComponentData<DroneStation>(stations[i]);

            if (!station.isMainStation)
                continue;

            stationEntity = stations[i];
            return true;
        }

        return false;
    }
}
