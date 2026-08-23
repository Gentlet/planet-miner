using System;
using Unity.Entities;
using UnityEngine;

public partial class DroneIdentityConversionSystem
{
    private void CollectActivationCandidates(Entity stationEntity)
    {
        DynamicBuffer<StoredItemElement> storedItems = EntityManager
            .GetBuffer<StoredItemElement>(stationEntity, true);

        for (int i = 0; i < storedItems.Length; i++)
        {
            StoredItemElement storedItem = storedItems[i];

            if (storedItem.type != ItemTypeEnum.Drone)
                continue;

            if (!EntityManager.Exists(storedItem.itemEntity))
                continue;

            if (!EntityManager.HasComponent<StoredItem>(storedItem.itemEntity))
                continue;

            _activationCandidates.Add(storedItem.itemEntity);
        }
    }

    private bool TryActivateStoredDroneItem(
        Entity stationEntity,
        Entity itemEntity,
        DroneConfig config)
    {
        if (!IsValidActivationCandidate(stationEntity, itemEntity))
            return false;

        DroneStationNetwork network = EntityManager
            .GetComponentData<DroneStationNetwork>(stationEntity);

        if (!_itemStorage.TryDetachStoredItemForConversionImmediate(
                stationEntity,
                itemEntity,
                out ItemTypeEnum itemType))
            return false;

        try
        {
            AddActiveDroneIdentity(itemEntity, stationEntity, network, config);

            if (!DroneStationStorageUtility.TryAddStoredDrone(
                    EntityManager,
                    stationEntity,
                    itemEntity))
                throw new InvalidOperationException(
                    "Failed to register the activated drone in station storage.");

            EntityManager.RemoveComponent<Item>(itemEntity);
            EntityManager.RemoveComponent<ItemCellChanged>(itemEntity);
            return true;
        }
        catch (Exception exception)
        {
            RollBackFailedActivation(stationEntity, itemEntity, itemType);
            Debug.LogError(
                $"Drone item activation failed and was rolled back. Station: {stationEntity}, Item: {itemEntity}\n{exception}");
            return false;
        }
    }

    private bool IsValidActivationCandidate(
        Entity stationEntity,
        Entity itemEntity)
    {
        if (stationEntity == Entity.Null)
            return false;

        if (itemEntity == Entity.Null)
            return false;

        if (!EntityManager.Exists(stationEntity))
            return false;

        if (!EntityManager.Exists(itemEntity))
            return false;

        if (!EntityManager.HasComponent<DroneStation>(stationEntity))
            return false;

        if (!EntityManager.HasComponent<DroneStationNetwork>(stationEntity))
            return false;

        if (!EntityManager.HasBuffer<StoredDroneElement>(stationEntity))
            return false;

        if (!EntityManager.HasComponent<Item>(itemEntity))
            return false;

        if (EntityManager.GetComponentData<Item>(itemEntity).type !=
            ItemTypeEnum.Drone)
            return false;

        if (EntityManager.HasComponent<ActiveDrone>(itemEntity))
            return false;

        return true;
    }

    private void AddActiveDroneIdentity(
        Entity droneEntity,
        Entity stationEntity,
        DroneStationNetwork network,
        DroneConfig config)
    {
        EntityManager.AddComponentData(droneEntity, new ActiveDrone
        {
            carryingCapacity = config.carryingCapacity,
            movementSpeed = config.movementSpeed,
            emergencyMovementSpeed = config.emergencyMovementSpeed
        });
        EntityManager.AddComponentData(droneEntity, new DroneBattery
        {
            current = config.maximumBattery,
            maximum = config.maximumBattery,
            consumptionPerDistance = config.batteryConsumptionPerDistance
        });
        EntityManager.AddComponentData(
            droneEntity,
            new DroneState { value = DroneStateEnum.Stored });
        EntityManager.AddComponentData(droneEntity, new DroneAssignment
        {
            returnStation = stationEntity,
            networkId = network.networkId
        });
        EntityManager.AddComponentData(
            droneEntity,
            new DroneCargo { itemType = ItemTypeEnum.None });
        EntityManager.AddBuffer<StoredItemElement>(droneEntity);
    }

    private void RollBackFailedActivation(
        Entity stationEntity,
        Entity itemEntity,
        ItemTypeEnum itemType)
    {
        if (EntityManager.HasComponent<StoredDrone>(itemEntity))
        {
            DroneStationStorageUtility.TryReleaseStoredDrone(
                EntityManager,
                itemEntity);
        }

        RemoveActiveDroneIdentity(itemEntity);

        if (!EntityManager.HasComponent<Item>(itemEntity))
            EntityManager.AddComponentData(itemEntity, new Item { type = itemType });

        if (!EntityManager.HasComponent<ItemCellChanged>(itemEntity))
        {
            EntityManager.AddComponent<ItemCellChanged>(itemEntity);
            EntityManager.SetComponentEnabled<ItemCellChanged>(itemEntity, false);
        }

        _itemStorage.TryRestoreDetachedItemAfterConversionFailureImmediate(
            stationEntity,
            itemEntity,
            itemType);
    }
}
