using Unity.Entities;
using Unity.Mathematics;

public partial class DroneStationNetworkSystem
{
    public bool IsCellCovered(int2 cell)
    {
        if (!EnsureChunkMap())
            return false;

        _chunkMap.GetDroneStationsCoveringCell(cell, _coveringStations);
        return _coveringStations.Count > 0;
    }

    public bool TryGetNetworkId(
        Entity stationEntity,
        out int networkId)
    {
        networkId = 0;

        if (stationEntity == Entity.Null)
            return false;

        if (!EntityManager.Exists(stationEntity))
            return false;

        if (!EntityManager.HasComponent<DroneStationNetwork>(stationEntity))
            return false;

        networkId = EntityManager
            .GetComponentData<DroneStationNetwork>(stationEntity)
            .networkId;
        return networkId > 0;
    }

    public bool TryGetNetworkIdAtCell(int2 cell, out int networkId)
    {
        networkId = 0;

        if (!EnsureChunkMap())
            return false;

        _chunkMap.GetDroneStationsCoveringCell(cell, _coveringStations);

        for (int i = 0; i < _coveringStations.Count; i++)
        {
            if (TryGetNetworkId(_coveringStations[i], out networkId))
                return true;
        }

        networkId = 0;
        return false;
    }

    public bool TryFindNearestStation(
        int networkId,
        int2 cell,
        out Entity stationEntity)
    {
        if (networkId <= 0)
        {
            stationEntity = Entity.Null;
            return false;
        }

        return TryFindNearestStation(
            cell,
            networkId,
            true,
            out stationEntity);
    }

    public bool TryFindNearestStation(
        int2 cell,
        out Entity stationEntity)
    {
        return TryFindNearestStation(
            cell,
            0,
            false,
            out stationEntity);
    }

    private bool TryFindNearestStation(
        int2 cell,
        int networkId,
        bool restrictToNetwork,
        out Entity stationEntity)
    {
        stationEntity = Entity.Null;

        float nearestDistanceSquared = float.MaxValue;

        for (int i = 0; i < _networkStations.Count; i++)
        {
            Entity candidateEntity = _networkStations[i];

            if (restrictToNetwork)
            {
                if (!TryGetNetworkId(
                        candidateEntity,
                        out int candidateNetworkId))
                    continue;

                if (candidateNetworkId != networkId)
                    continue;
            }

            int2 candidateCell = EntityManager
                .GetComponentData<GridPosition>(candidateEntity)
                .gridPosition;
            float distanceSquared = math.distancesq(
                new float2(cell.x, cell.y),
                new float2(candidateCell.x, candidateCell.y));

            if (distanceSquared < nearestDistanceSquared)
            {
                stationEntity = candidateEntity;
                nearestDistanceSquared = distanceSquared;
                continue;
            }

            if (distanceSquared != nearestDistanceSquared)
                continue;

            if (stationEntity == Entity.Null)
            {
                stationEntity = candidateEntity;
                continue;
            }

            if (CompareEntities(candidateEntity, stationEntity) < 0)
                stationEntity = candidateEntity;
        }

        return stationEntity != Entity.Null;
    }
}
