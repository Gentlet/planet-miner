using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

internal struct DroneFlightStation
{
    public Entity Entity;
    public Entity Network;
    public DroneStationActivityBounds Bounds;
    public float2 Position;
    public int2 Anchor;
    public PlacementStamp Stamp;
    public bool HasStamp;
    public bool CanDock;
}

internal struct DroneFlightSite
{
    public int2 Anchor;
    public int2 Size;
}

internal struct DroneFlightPlan
{
    public DroneRouteEvaluationStatusEnum Status;
    public float Distance;
    public float RemainingCharge;
    public float2 NextPosition;
    public Entity ChargingStation;
}

/// <summary>직선 자유 비행/충전 경유 거리와 필요 에너지를 같은 규칙으로 계획한다. 계획의 최대 충전량을 실물 배터리에 쓰지 않는다.</summary>
internal static class DroneFlightPlanningUtility
{
    public static DroneFlightPlan Plan(float2 origin, float2 goal, float charge, float maximum,
        float rate, NativeList<DroneFlightStation> stations)
    {
        var result = new DroneFlightPlan
        {
            Status = DroneRouteEvaluationStatusEnum.WaitingForEnergy,
            NextPosition = goal,
            RemainingCharge = charge
        };
        float direct = math.distance(origin, goal);
        if (direct * rate <= charge)
        {
            result.Status = DroneRouteEvaluationStatusEnum.Reachable;
            result.Distance = direct;
            result.RemainingCharge = math.max(0f, charge - direct * rate);
            return result;
        }

        int count = stations.Length + 2;
        var distances = new NativeArray<float>(count, Allocator.Temp);
        var previous = new NativeArray<int>(count, Allocator.Temp);
        var visited = new NativeArray<byte>(count, Allocator.Temp);
        try
        {
            for (int i = 0; i < count; i++)
            {
                distances[i] = float.MaxValue;
                previous[i] = -1;
            }
            distances[0] = 0f;
            for (int step = 0; step < count; step++)
            {
                int node = -1;
                for (int i = 0; i < count; i++)
                {
                    if (visited[i] != 0 || distances[i] == float.MaxValue)
                    {
                        continue;
                    }
                    if (node < 0 || distances[i] < distances[node] ||
                        (distances[i] == distances[node] && EarlierNode(i, node, stations)))
                    {
                        node = i;
                    }
                }
                if (node < 0)
                {
                    return result;
                }
                if (node == 1)
                {
                    break;
                }
                visited[node] = 1;
                float2 position = Position(node, origin, goal, stations);
                float available = node == 0 ? charge : maximum;
                for (int next = 1; next < count; next++)
                {
                    if (visited[next] != 0 || next == node)
                    {
                        continue;
                    }
                    if (next >= 2 && !stations[next - 2].CanDock)
                    {
                        continue;
                    }
                    float distance = math.distance(position, Position(next, origin, goal, stations));
                    if (distance * rate > available)
                    {
                        continue;
                    }
                    float total = distances[node] + distance;
                    if (total < distances[next])
                    {
                        distances[next] = total;
                        previous[next] = node;
                    }
                }
            }
            if (previous[1] < 0)
            {
                return result;
            }
            int first = 1;
            while (previous[first] > 0)
            {
                first = previous[first];
            }
            int last = previous[1];
            float lastCharge = last == 0 ? charge : maximum;
            float lastDistance = math.distance(Position(last, origin, goal, stations), goal);
            result.Status = DroneRouteEvaluationStatusEnum.Reachable;
            result.Distance = distances[1];
            result.RemainingCharge = math.max(0f, lastCharge - lastDistance * rate);
            result.NextPosition = Position(first, origin, goal, stations);
            result.ChargingStation = first >= 2 ? stations[first - 2].Entity : Entity.Null;
            return result;
        }
        finally
        {
            distances.Dispose();
            previous.Dispose();
            visited.Dispose();
        }
    }

    public static DroneFlightPlan PlanViaSource(float2 origin, float2 source, float2 destination,
        float charge, float maximum, float rate, NativeList<DroneFlightStation> stations)
    {
        var first = Plan(origin, source, charge, maximum, rate, stations);
        var best = new DroneFlightPlan { Status = DroneRouteEvaluationStatusEnum.WaitingForEnergy };
        if (first.Status == DroneRouteEvaluationStatusEnum.Reachable)
        {
            var second = Plan(source, destination, first.RemainingCharge, maximum, rate, stations);
            if (second.Status == DroneRouteEvaluationStatusEnum.Reachable)
            {
                best = first;
                best.Distance += second.Distance;
                best.RemainingCharge = second.RemainingCharge;
                if (best.ChargingStation == Entity.Null && second.ChargingStation != Entity.Null &&
                    math.all(second.NextPosition == source))
                {
                    // 수집점 자체에서 다음 구간을 위한 충전이 필요하면 그 도착부터 충전 경유로 유지한다.
                    best.ChargingStation = second.ChargingStation;
                }
            }
        }
        // 수집점에 직접 도달할 수 있어도 이후 구간을 위해 그 전에 충전해야 할 수 있다.
        for (int i = 0; i < stations.Length; i++)
        {
            if (!stations[i].CanDock)
            {
                continue;
            }
            var prefix = Plan(origin, stations[i].Position, charge, maximum, rate, stations);
            if (prefix.Status != DroneRouteEvaluationStatusEnum.Reachable)
            {
                continue;
            }
            var toSource = Plan(stations[i].Position, source, maximum, maximum, rate, stations);
            if (toSource.Status != DroneRouteEvaluationStatusEnum.Reachable)
            {
                continue;
            }
            var toDestination = Plan(source, destination, toSource.RemainingCharge, maximum, rate, stations);
            if (toDestination.Status != DroneRouteEvaluationStatusEnum.Reachable)
            {
                continue;
            }
            float distance = prefix.Distance + toSource.Distance + toDestination.Distance;
            if (best.Status != DroneRouteEvaluationStatusEnum.Reachable || distance < best.Distance)
            {
                best = prefix;
                best.Distance = distance;
                best.RemainingCharge = toDestination.RemainingCharge;
                if (best.ChargingStation == Entity.Null)
                {
                    best.ChargingStation = stations[i].Entity;
                    best.NextPosition = stations[i].Position;
                }
            }
        }
        return best;
    }

    public static int FindReturnStation(float2 origin, NativeList<DroneFlightStation> stations)
    {
        return SelectReturnStation(origin, stations, false, 0f, 0f, 0f, out _);
    }

    public static int PlanReturn(float2 origin, NativeList<DroneFlightStation> stations,
        float charge, float maximum, float energyPerDistance, out DroneFlightPlan plan)
    {
        return SelectReturnStation(origin, stations, true, charge, maximum, energyPerDistance, out plan);
    }

    private static int SelectReturnStation(float2 origin, NativeList<DroneFlightStation> stations,
        bool evaluateEnergy, float charge, float maximum, float energyPerDistance, out DroneFlightPlan selectedPlan)
    {
        selectedPlan = default;
        Entity network = Entity.Null;
        int2 cell = (int2)math.floor(origin);
        for (int i = 0; i < stations.Length; i++)
        {
            if (stations[i].Bounds.Contains(cell))
            {
                network = stations[i].Network;
                break;
            }
        }
        int selected = -1;
        float distance = float.MaxValue;
        bool selectedSameNetwork = false;
        for (int i = 0; i < stations.Length; i++)
        {
            var station = stations[i];
            if (!station.CanDock)
            {
                continue;
            }
            bool sameNetwork = network != Entity.Null && station.Network == network;
            var candidatePlan = evaluateEnergy
                ? Plan(origin, station.Position, charge, maximum, energyPerDistance, stations)
                : new DroneFlightPlan { Status = DroneRouteEvaluationStatusEnum.Reachable,
                    Distance = math.distance(origin, station.Position), NextPosition = station.Position };
            if (candidatePlan.Status != DroneRouteEvaluationStatusEnum.Reachable)
            {
                continue;
            }
            float candidateDistance = candidatePlan.Distance;
            bool preferred = selected < 0;
            if (selected >= 0)
            {
                if (sameNetwork != selectedSameNetwork)
                {
                    preferred = sameNetwork;
                }
                else if (candidateDistance != distance)
                {
                    preferred = candidateDistance < distance;
                }
                else
                {
                    preferred = Compare(station, stations[selected]) < 0;
                }
            }
            if (preferred)
            {
                selected = i;
                distance = candidateDistance;
                selectedSameNetwork = sameNetwork;
                selectedPlan = candidatePlan;
            }
        }
        return selected;
    }

    public static bool TryFindDrop(float2 origin, NativeList<DroneFlightStation> stations,
        NativeList<DroneFlightSite> sites, out int2 drop)
    {
        drop = default;
        Entity network = Entity.Null;
        int2 current = (int2)math.floor(origin);
        for (int i = 0; i < stations.Length; i++)
        {
            if (stations[i].Bounds.Contains(current))
            {
                network = stations[i].Network;
                break;
            }
        }
        if (network == Entity.Null)
        {
            return false;
        }
        using var xs = new NativeList<long>(Allocator.Temp);
        using var ys = new NativeList<long>(Allocator.Temp);
        xs.Add((long)math.floor(origin.x));
        xs.Add((long)math.ceil(origin.x));
        ys.Add((long)math.floor(origin.y));
        ys.Add((long)math.ceil(origin.y));
        for (int i = 0; i < stations.Length; i++)
        {
            if (stations[i].Network == network)
            {
                xs.Add(stations[i].Bounds.MinX);
                xs.Add(stations[i].Bounds.MaxX);
                ys.Add(stations[i].Bounds.MinY);
                ys.Add(stations[i].Bounds.MaxY);
            }
        }
        for (int i = 0; i < sites.Length; i++)
        {
            xs.Add((long)sites[i].Anchor.x - 1);
            xs.Add((long)sites[i].Anchor.x + sites[i].Size.x);
            ys.Add((long)sites[i].Anchor.y - 1);
            ys.Add((long)sites[i].Anchor.y + sites[i].Size.y);
        }
        bool found = false;
        float bestDistance = float.MaxValue;
        for (int x = 0; x < xs.Length; x++)
        {
            for (int y = 0; y < ys.Length; y++)
            {
                if (xs[x] < int.MinValue || xs[x] > int.MaxValue || ys[y] < int.MinValue || ys[y] > int.MaxValue)
                {
                    continue;
                }
                int2 cell = new int2((int)xs[x], (int)ys[y]);
                if (!ContainsNetwork(stations, network, cell) || IsInsideSite(sites, cell))
                {
                    continue;
                }
                float distance = math.distancesq(origin, (float2)cell);
                if (!found || distance < bestDistance || (distance == bestDistance &&
                    (cell.x < drop.x || (cell.x == drop.x && cell.y < drop.y))))
                {
                    found = true;
                    bestDistance = distance;
                    drop = cell;
                }
            }
        }
        return found;
    }

    public static bool IsInsideSite(NativeList<DroneFlightSite> sites, int2 cell)
    {
        for (int i = 0; i < sites.Length; i++)
        {
            var site = sites[i];
            if (cell.x >= site.Anchor.x && cell.y >= site.Anchor.y &&
                (long)cell.x < (long)site.Anchor.x + site.Size.x && (long)cell.y < (long)site.Anchor.y + site.Size.y)
            {
                return true;
            }
        }
        return false;
    }

    private static bool ContainsNetwork(NativeList<DroneFlightStation> stations, Entity network, int2 cell)
    {
        for (int i = 0; i < stations.Length; i++)
        {
            if (stations[i].Network == network && stations[i].Bounds.Contains(cell))
            {
                return true;
            }
        }
        return false;
    }

    public static int Compare(in DroneFlightStation left, in DroneFlightStation right)
    {
        return PlacementStamp.Compare(left.HasStamp, left.Stamp, left.Anchor, right.HasStamp, right.Stamp, right.Anchor);
    }

    private static bool EarlierNode(int left, int right, NativeList<DroneFlightStation> stations)
    {
        if (left < 2 || right < 2)
        {
            return left < right;
        }
        return Compare(stations[left - 2], stations[right - 2]) < 0;
    }

    private static float2 Position(int node, float2 origin, float2 goal, NativeList<DroneFlightStation> stations)
    {
        switch (node)
        {
            case 0:
                return origin;
            case 1:
                return goal;
            default:
                return stations[node - 2].Position;
        }
    }
}
