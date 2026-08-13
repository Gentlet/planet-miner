using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public partial class PowerGridSystem
{
    private readonly List<Entity> _unregisteredPowerPoles = new();

    public bool TryUnregisterPowerPole(Entity powerPoleEntity)
    {
        if (!_powerPoles.TryGetValue(
                powerPoleEntity,
                out PowerPoleTopologyData powerPole))
            return false;

        GridBounds supplyBounds = PowerGridRangeUtility.GetBounds(
            powerPole.Center,
            powerPole.SupplyRange);
        bool removedAllSupplyCells = _chunkMap.TryUnregisterPowerPoleSupply(
            powerPoleEntity,
            supplyBounds);

        _powerPoles.Remove(powerPoleEntity);
        _unregisteredPowerPoles.Add(powerPoleEntity);
        _topologyDirty = true;
        return removedAllSupplyCells;
    }

    private bool RegisterPendingPowerPoles(PowerPoleConfigElement config)
    {
        using NativeArray<Entity> pendingEntities =
            _unregisteredPowerPoleQuery.ToEntityArray(Allocator.Temp);
        bool topologyChanged = false;

        for (int i = 0; i < pendingEntities.Length; i++)
        {
            Entity powerPoleEntity = pendingEntities[i];

            if (_powerPoles.ContainsKey(powerPoleEntity))
                continue;

            int2 center = EntityManager
                .GetComponentData<GridPosition>(powerPoleEntity)
                .gridPosition;
            PowerPoleTopologyData powerPole = new PowerPoleTopologyData(
                powerPoleEntity,
                _nextStablePowerPoleId,
                center,
                config.supplyRange,
                config.connectionRange);
            GridBounds supplyBounds = PowerGridRangeUtility.GetBounds(
                center,
                config.supplyRange);

            if (!_chunkMap.TryRegisterPowerPoleSupply(
                    powerPoleEntity,
                    supplyBounds))
            {
                Debug.LogError(
                    $"Power pole supply registration failed. Entity : {powerPoleEntity}, Cell : {center}");
                _chunkMap.TryUnregisterBuilding(powerPoleEntity);
                EntityManager.DestroyEntity(powerPoleEntity);
                continue;
            }

            _powerPoles.Add(powerPoleEntity, powerPole);
            EntityManager.SetComponentData(
                powerPoleEntity,
                new PowerPole { stableId = _nextStablePowerPoleId });
            _nextStablePowerPoleId++;
            topologyChanged = true;
        }

        return topologyChanged;
    }

    private void UnregisterAllPowerPoleSupplyRanges()
    {
        foreach (PowerPoleTopologyData powerPole in _powerPoles.Values)
        {
            _chunkMap.TryUnregisterPowerPoleSupply(
                powerPole.Entity,
                PowerGridRangeUtility.GetBounds(
                    powerPole.Center,
                    powerPole.SupplyRange));
        }
    }

    private void RemoveUnregisteredPowerPoleConnections()
    {
        for (int i = 0; i < _unregisteredPowerPoles.Count; i++)
        {
            Entity powerPoleEntity = _unregisteredPowerPoles[i];

            if (!EntityManager.Exists(powerPoleEntity))
                continue;

            if (!EntityManager.HasComponent<PowerGridConnection>(powerPoleEntity))
                continue;

            EntityManager.RemoveComponent<PowerGridConnection>(powerPoleEntity);
        }

        _unregisteredPowerPoles.Clear();
    }
}
