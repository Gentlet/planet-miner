using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

[UpdateAfter(typeof(StorageSystem))]
[UpdateAfter(typeof(DroneCargoTransferSystem))]
[UpdateBefore(typeof(BuildingDestroySystem))]
public partial class DroneIdentityConversionSystem : SystemBase
{
    private EntityQuery _stationQuery;
    private EntityQuery _storageLimitQuery;
    private ItemStorageSystem _itemStorage;
    private readonly List<Entity> _activationCandidates = new();
    private readonly List<Entity> _storedDrones = new();

    protected override void OnCreate()
    {
        _stationQuery = GetEntityQuery(
            ComponentType.ReadOnly<DroneStation>(),
            ComponentType.ReadOnly<DroneStationNetwork>(),
            ComponentType.ReadOnly<Storage>(),
            ComponentType.ReadOnly<GridPosition>(),
            ComponentType.ReadOnly<StoredItemElement>(),
            ComponentType.ReadOnly<StoredDroneElement>());
        _storageLimitQuery = GetEntityQuery(
            ComponentType.ReadOnly<ItemStorageLimitElement>());
        _itemStorage = World.GetOrCreateSystemManaged<ItemStorageSystem>();
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
