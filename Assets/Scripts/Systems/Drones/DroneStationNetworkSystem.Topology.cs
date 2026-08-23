using Unity.Entities;

public partial class DroneStationNetworkSystem
{
    private void RebuildNetworkTopology()
    {
        CollectRegisteredNetworkStations();
        InitializeDisjointSets();
        MergeOverlappingStations();
        BuildNetworkIds();
        PublishNetworkIds();
    }

    private void CollectRegisteredNetworkStations()
    {
        _networkStations.Clear();
        _networkBounds.Clear();

        for (int i = 0; i < _currentStations.Count; i++)
        {
            Entity stationEntity = _currentStations[i];

            if (!_registrations.TryGetValue(
                    stationEntity,
                    out StationRegistration registration))
                continue;

            _networkStations.Add(stationEntity);
            _networkBounds.Add(registration.ActivityBounds);
        }
    }

    private void InitializeDisjointSets()
    {
        _parents.Clear();

        for (int i = 0; i < _networkStations.Count; i++)
            _parents.Add(i);
    }

    private void MergeOverlappingStations()
    {
        for (int leftIndex = 0;
             leftIndex < _networkStations.Count;
             leftIndex++)
        {
            for (int rightIndex = leftIndex + 1;
                 rightIndex < _networkStations.Count;
                 rightIndex++)
            {
                if (!_networkBounds[leftIndex].Overlaps(
                        _networkBounds[rightIndex]))
                    continue;

                Union(leftIndex, rightIndex);
            }
        }
    }

    private void BuildNetworkIds()
    {
        _networkIdByRoot.Clear();

        for (int i = 0; i < _networkStations.Count; i++)
        {
            int root = FindRoot(i);
            int candidateNetworkId = _networkStations[i].Index + 1;

            if (!_networkIdByRoot.TryGetValue(
                    root,
                    out int currentNetworkId))
            {
                _networkIdByRoot.Add(root, candidateNetworkId);
                continue;
            }

            if (candidateNetworkId < currentNetworkId)
                _networkIdByRoot[root] = candidateNetworkId;
        }
    }

    private void PublishNetworkIds()
    {
        for (int i = 0; i < _networkStations.Count; i++)
        {
            Entity stationEntity = _networkStations[i];
            int networkId = _networkIdByRoot[FindRoot(i)];

            if (EntityManager.HasComponent<DroneStationNetwork>(stationEntity))
            {
                DroneStationNetwork currentNetwork = EntityManager
                    .GetComponentData<DroneStationNetwork>(stationEntity);

                if (currentNetwork.networkId == networkId)
                    continue;

                EntityManager.SetComponentData(
                    stationEntity,
                    new DroneStationNetwork { networkId = networkId });
                continue;
            }

            EntityManager.AddComponentData(
                stationEntity,
                new DroneStationNetwork { networkId = networkId });
        }
    }

    private int FindRoot(int stationIndex)
    {
        int parent = _parents[stationIndex];

        if (parent == stationIndex)
            return stationIndex;

        int root = FindRoot(parent);
        _parents[stationIndex] = root;
        return root;
    }

    private void Union(int leftIndex, int rightIndex)
    {
        int leftRoot = FindRoot(leftIndex);
        int rightRoot = FindRoot(rightIndex);

        if (leftRoot == rightRoot)
            return;

        if (leftRoot < rightRoot)
            _parents[rightRoot] = leftRoot;
        else
            _parents[leftRoot] = rightRoot;
    }
}
