using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public partial class PowerGridSystem
{
    private readonly Dictionary<Entity, PowerGridState> _powerGridStates = new();
    private readonly HashSet<Entity> _connectedPowerParticipants = new();
    private readonly List<int2> _participantFootprintCells = new();

    private void ReconnectPowerParticipants()
    {
        using NativeArray<Entity> participants =
            _powerParticipantQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < participants.Length; i++)
        {
            Entity participant = participants[i];
            int2 anchor = EntityManager
                .GetComponentData<GridPosition>(participant)
                .gridPosition;
            int2 footprintSize = EntityManager
                .GetComponentData<BuildingFootprint>(participant)
                .size;
            DirectionEnum direction = EntityManager
                .GetComponentData<Direction>(participant)
                .dir;

            if (TryResolvePowerGrid(
                    anchor,
                    footprintSize,
                    direction,
                    out Entity powerPoleEntity,
                    out Entity powerGridEntity))
            {
                SetPowerGridConnection(
                    participant,
                    powerPoleEntity,
                    powerGridEntity);
                continue;
            }

            RemovePowerGridConnection(participant);
        }
    }

    private bool TryResolvePowerGrid(
        int2 anchor,
        int2 footprintSize,
        DirectionEnum direction,
        out Entity powerPoleEntity,
        out Entity powerGridEntity)
    {
        powerPoleEntity = Entity.Null;
        powerGridEntity = Entity.Null;
        long nearestSquaredDistance = long.MaxValue;
        int nearestStableId = int.MaxValue;

        BuildingFootprintUtility.GetOccupiedCells(
            anchor,
            footprintSize,
            direction,
            _participantFootprintCells);

        for (int cellIndex = 0;
             cellIndex < _participantFootprintCells.Count;
             cellIndex++)
        {
            int2 cell = _participantFootprintCells[cellIndex];
            _chunkMap.GetPowerPolesCoveringCell(cell, _coveringPowerPoles);

            for (int poleIndex = 0;
                 poleIndex < _coveringPowerPoles.Count;
                 poleIndex++)
            {
                Entity candidateEntity = _coveringPowerPoles[poleIndex];

                if (!_powerPoles.TryGetValue(
                        candidateEntity,
                        out PowerPoleTopologyData candidate))
                    continue;

                if (!TryGetPowerGrid(candidateEntity, out Entity candidateGrid))
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
                powerGridEntity = candidateGrid;
                nearestSquaredDistance = squaredDistance;
                nearestStableId = candidate.StableId;
            }
        }

        return powerPoleEntity != Entity.Null;
    }

    private void SetPowerGridConnection(
        Entity participant,
        Entity powerPoleEntity,
        Entity powerGridEntity)
    {
        PowerGridConnection connection = new PowerGridConnection
        {
            powerPoleEntity = powerPoleEntity,
            powerGridEntity = powerGridEntity
        };

        if (EntityManager.HasComponent<PowerGridConnection>(participant))
        {
            EntityManager.SetComponentData(participant, connection);
            return;
        }

        EntityManager.AddComponentData(participant, connection);
    }

    private void RemovePowerGridConnection(Entity participant)
    {
        if (!EntityManager.HasComponent<PowerGridConnection>(participant))
            return;

        EntityManager.RemoveComponent<PowerGridConnection>(participant);
    }

    private void UpdatePowerGridStates(float deltaTime)
    {
        ResetPowerGridStates();
        AccumulateDemand();
        AccumulateAvailableGeneration(deltaTime);
        PublishPowerGridStates();
        DispatchGenerators();
        UpdateConsumerStates();
    }

    private void ResetPowerGridStates()
    {
        _powerGridStates.Clear();
        _connectedPowerParticipants.Clear();

        for (int i = 0; i < _powerGridEntities.Count; i++)
        {
            _powerGridStates.Add(
                _powerGridEntities[i],
                new PowerGridState { supplyRatio = 1f });
        }
    }

    private void AccumulateDemand()
    {
        using NativeArray<Entity> consumers =
            _powerConsumerQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < consumers.Length; i++)
        {
            Entity consumerEntity = consumers[i];
            if (!TryGetConnectedGridState(
                    consumerEntity,
                    out Entity powerGridEntity,
                    out PowerGridState state))
                continue;

            PowerConsumer consumer = EntityManager
                .GetComponentData<PowerConsumer>(consumerEntity);
            state.maximumDemand += math.max(0f, consumer.maximumConsumption);
            AddConnectedBuilding(consumerEntity, ref state);
            _powerGridStates[powerGridEntity] = state;
        }
    }

    private bool TryGetConnectedGridState(
        Entity participant,
        out Entity powerGridEntity,
        out PowerGridState state)
    {
        if (!EntityManager.HasComponent<PowerGridConnection>(participant))
        {
            powerGridEntity = Entity.Null;
            state = default;
            return false;
        }

        powerGridEntity = EntityManager
            .GetComponentData<PowerGridConnection>(participant)
            .powerGridEntity;
        return _powerGridStates.TryGetValue(powerGridEntity, out state);
    }

    private void AddConnectedBuilding(
        Entity participant,
        ref PowerGridState state)
    {
        if (_connectedPowerParticipants.Add(participant))
            state.connectedBuildingCount++;
    }

    private void PublishPowerGridStates()
    {
        for (int i = 0; i < _powerGridEntities.Count; i++)
        {
            Entity powerGridEntity = _powerGridEntities[i];
            PowerGridState state = _powerGridStates[powerGridEntity];

            state.supplyRatio = state.maximumDemand > 0f
                ? math.min(1f, state.availableGeneration / state.maximumDemand)
                : 1f;
            state.actualConsumption = state.maximumDemand * state.supplyRatio;
            state.sparePower = math.max(
                0f,
                state.availableGeneration - state.actualConsumption);

            _powerGridStates[powerGridEntity] = state;
            EntityManager.SetComponentData(powerGridEntity, state);
        }
    }

    private void UpdateConsumerStates()
    {
        using NativeArray<Entity> consumers =
            _powerConsumerQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < consumers.Length; i++)
        {
            Entity consumerEntity = consumers[i];
            PowerConsumer consumer = EntityManager
                .GetComponentData<PowerConsumer>(consumerEntity);

            if (TryGetConnectedGridState(
                    consumerEntity,
                    out _,
                    out PowerGridState state))
            {
                consumer.supplyRatio = state.supplyRatio;
                consumer.currentConsumption =
                    consumer.maximumConsumption * state.supplyRatio;
            }
            else
            {
                consumer.supplyRatio = 0f;
                consumer.currentConsumption = 0f;
            }

            EntityManager.SetComponentData(consumerEntity, consumer);
        }
    }
}
