using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

public partial class PowerGridSystem
{
    private readonly List<Entity> _coveringPowerPoles = new();

    public bool TryGetNearestPowerPole(int2 cell, out Entity powerPoleEntity)
    {
        powerPoleEntity = Entity.Null;
        _chunkMap.GetPowerPolesCoveringCell(cell, _coveringPowerPoles);

        long nearestSquaredDistance = long.MaxValue;
        int nearestStableId = int.MaxValue;

        for (int i = 0; i < _coveringPowerPoles.Count; i++)
        {
            Entity candidateEntity = _coveringPowerPoles[i];

            if (!_powerPoles.TryGetValue(
                    candidateEntity,
                    out PowerPoleTopologyData candidate))
                continue;

            long squaredDistance = PowerGridRangeUtility.GetSquaredDistance(
                cell,
                candidate.Center);

            if (squaredDistance > nearestSquaredDistance)
                continue;

            if (squaredDistance == nearestSquaredDistance &&
                candidate.StableId >= nearestStableId)
                continue;

            powerPoleEntity = candidateEntity;
            nearestSquaredDistance = squaredDistance;
            nearestStableId = candidate.StableId;
        }

        return powerPoleEntity != Entity.Null;
    }

    public bool TryGetPowerGrid(Entity powerPoleEntity, out Entity powerGridEntity)
    {
        if (!EntityManager.Exists(powerPoleEntity))
        {
            powerGridEntity = Entity.Null;
            return false;
        }
        if (!EntityManager.HasComponent<PowerGridConnection>(powerPoleEntity))
        {
            powerGridEntity = Entity.Null;
            return false;
        }

        powerGridEntity = EntityManager
            .GetComponentData<PowerGridConnection>(powerPoleEntity)
            .powerGridEntity;
        return powerGridEntity != Entity.Null;
    }

    public void GetConnectablePowerPoles(
        int2 center,
        int2 connectionRange,
        List<Entity> results)
    {
        results.Clear();

        for (int i = 0; i < _orderedPowerPoles.Count; i++)
        {
            PowerPoleTopologyData candidate = _orderedPowerPoles[i];

            if (!PowerGridRangeUtility.AreCentersConnected(
                    center,
                    connectionRange,
                    candidate.Center,
                    candidate.ConnectionRange))
                continue;

            results.Add(candidate.Entity);
        }
    }
}
