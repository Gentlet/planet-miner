using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

[UpdateAfter(typeof(StorageSystem))]
[UpdateAfter(typeof(DroneCargoTransferSystem))]
[UpdateBefore(typeof(BuildingDestroySystem))]
public partial class DroneIdentityConversionSystem : SystemBase
{
    private EntityQuery _stationQuery;
    private ItemStorageSystem _itemStorage;
    private ItemTrackingSystem _itemTracking;
    private readonly List<Entity> _activationCandidates = new();
    private readonly List<Entity> _storedDrones = new();

    protected override void OnCreate()
    {
        _stationQuery = GetEntityQuery(
            ComponentType.ReadOnly<DroneStation>(),
            ComponentType.ReadOnly<DroneStationNetwork>(),
            ComponentType.ReadOnly<StoredItemElement>(),
            ComponentType.ReadOnly<StoredDroneElement>());
        _itemStorage = World.GetOrCreateSystemManaged<ItemStorageSystem>();
        _itemTracking = World.GetOrCreateSystemManaged<ItemTrackingSystem>();
        RequireForUpdate<DroneConfig>();
    }

    protected override void OnUpdate()
    {
        DroneConfig config = SystemAPI.GetSingleton<DroneConfig>();
        _activationCandidates.Clear();
        using NativeArray<Entity> stations =
            _stationQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < stations.Length; i++)
            CollectActivationCandidates(stations[i]);

        for (int i = 0; i < _activationCandidates.Count; i++)
        {
            Entity itemEntity = _activationCandidates[i];

            if (!EntityManager.Exists(itemEntity))
                continue;

            StoredItem storedItem = EntityManager.GetComponentData<StoredItem>(
                itemEntity);
            TryActivateStoredDroneItem(
                storedItem.owner,
                itemEntity,
                config);
        }
    }
}
