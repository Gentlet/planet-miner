using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>원본 건물에서 계산한 정거장별 영역과 공개 연결의 파생 항목. 셀별 점유나 망 외곽 사각형을 저장하지 않는다.</summary>
public struct DroneStationCoverage
{
    public Entity RepresentativeStation;
    public DroneStationActivityBounds Bounds;
}

/// <summary>
/// DroneStationSpatialSyncSystem이 생성·재구축·해제하는 공개 조회 맵. 소비자는 소유하지 않으며 Fence를 등록한다.
/// 이번 틱 DroneDecision은 이 이전 공개 맵 대신 DroneStationNetworkDecision과 건물 원본을 읽는다.
/// </summary>
public struct DroneStationSpatialIndex : IComponentData
{
    public NativeParallelHashMap<Entity, DroneStationCoverage> Map;

    public NativeParallelHashMap<Entity, DroneStationCoverage>.ReadOnly AsReadOnly() => Map.AsReadOnly();

    public static bool Contains(in NativeParallelHashMap<Entity, DroneStationCoverage>.ReadOnly stations,
        Entity network, int2 position)
    {
        if (network == Entity.Null) return false;
        foreach (var station in stations)
        {
            if (station.Value.RepresentativeStation == network && station.Value.Bounds.Contains(position)) return true;
        }
        return false;
    }
}

/// <summary>공유 정거장 맵의 Reader/Writer 접근. 재할당·해제 전에 전체 접근을 완료한다.</summary>
public struct DroneStationSpatialIndexFence : IFenceComponent
{
    /// <summary>인덱스별 독립 의존성. ref로 수정하거나 수정한 전체 컴포넌트를 저장한다.</summary>
    public NativeContainerFence State;
}
