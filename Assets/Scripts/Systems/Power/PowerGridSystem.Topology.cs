using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

public partial class PowerGridSystem
{
    private readonly HashSet<Entity> _visitedPowerPoles = new();
    private readonly Queue<Entity> _pendingPowerPoles = new();
    private readonly List<Entity> _connectedComponent = new();

    private void RebuildTopology()
    {
        DestroyPowerGridEntities();
        RemovePowerGridConnections();
        OrderPowerPoles();
        _visitedPowerPoles.Clear();

        for (int i = 0; i < _orderedPowerPoles.Count; i++)
        {
            PowerPoleTopologyData start = _orderedPowerPoles[i];

            if (!_visitedPowerPoles.Add(start.Entity))
                continue;

            CollectConnectedComponent(start.Entity);
            CreatePowerGridForConnectedComponent();
        }
    }

    private void CollectConnectedComponent(Entity startEntity)
    {
        _pendingPowerPoles.Clear();
        _connectedComponent.Clear();
        _pendingPowerPoles.Enqueue(startEntity);

        while (_pendingPowerPoles.Count > 0)
        {
            Entity currentEntity = _pendingPowerPoles.Dequeue();
            PowerPoleTopologyData current = _powerPoles[currentEntity];
            _connectedComponent.Add(currentEntity);

            for (int i = 0; i < _orderedPowerPoles.Count; i++)
            {
                PowerPoleTopologyData candidate = _orderedPowerPoles[i];

                if (_visitedPowerPoles.Contains(candidate.Entity))
                    continue;

                if (!PowerGridRangeUtility.ArePolesConnected(current, candidate))
                    continue;

                _visitedPowerPoles.Add(candidate.Entity);
                _pendingPowerPoles.Enqueue(candidate.Entity);
            }
        }
    }

    private void CreatePowerGridForConnectedComponent()
    {
        int stableGridId = int.MaxValue;

        for (int i = 0; i < _connectedComponent.Count; i++)
        {
            int stablePoleId = _powerPoles[_connectedComponent[i]].StableId;
            stableGridId = math.min(stableGridId, stablePoleId);
        }

        Entity powerGridEntity = EntityManager.CreateEntity();
        EntityManager.AddComponentData(
            powerGridEntity,
            new PowerGrid { stableId = stableGridId });
        EntityManager.AddComponentData(
            powerGridEntity,
            new PowerGridState { supplyRatio = 1f });
        _powerGridEntities.Add(powerGridEntity);

        for (int i = 0; i < _connectedComponent.Count; i++)
        {
            Entity powerPoleEntity = _connectedComponent[i];
            EntityManager.AddComponentData(
                powerPoleEntity,
                new PowerGridConnection
                {
                    powerPoleEntity = powerPoleEntity,
                    powerGridEntity = powerGridEntity
                });
        }
    }

    private void RemovePowerGridConnections()
    {
        foreach (Entity powerPoleEntity in _powerPoles.Keys)
        {
            if (!EntityManager.Exists(powerPoleEntity))
                continue;

            if (!EntityManager.HasComponent<PowerGridConnection>(powerPoleEntity))
                continue;

            EntityManager.RemoveComponent<PowerGridConnection>(powerPoleEntity);
        }
    }

    private void OrderPowerPoles()
    {
        _orderedPowerPoles.Clear();
        _orderedPowerPoles.AddRange(_powerPoles.Values);
        _orderedPowerPoles.Sort(
            (first, second) => first.StableId.CompareTo(second.StableId));
    }

    private void DestroyPowerGridEntities()
    {
        for (int i = 0; i < _powerGridEntities.Count; i++)
        {
            Entity powerGridEntity = _powerGridEntities[i];

            if (EntityManager.Exists(powerGridEntity))
                EntityManager.DestroyEntity(powerGridEntity);
        }

        _powerGridEntities.Clear();
    }
}
