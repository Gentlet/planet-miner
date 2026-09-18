using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 특정 그리드 좌표에 위치한 벨트 건물의 핵심 요약 정보 (Unmanaged).
/// </summary>
public struct BeltInfo
{
    public Entity Entity;
    public DirectionEnum Direction;
    public float Speed;

    public BeltInfo(Entity entity, DirectionEnum direction, float speed)
    {
        Entity = entity;
        Direction = direction;
        Speed = speed;
    }
}

/// <summary>
/// 벨트 건물의 그리드 공간 좌표(GridPosition -> BeltInfo) 고속 조회를 위한 Unmanaged 싱글톤 인덱스.
/// 
/// [책임]
/// - 그리드 좌표(int2)를 키로 하여 해당 위치에 배치된 벨트 건물의 정보를 O(1)에 조회합니다.
/// - 벨트는 1타일에 1개만 배치되므로 NativeParallelHashMap 기반으로 구성됩니다.
/// - SynchronizationGroup의 BeltSpatialSyncSystem에 의해 살아있는 벨트 건물 데이터로 완전 재구축(Clear & Rebuild)됩니다.
/// </summary>
public struct BeltSpatialIndex : IComponentData
{
    public NativeParallelHashMap<int2, BeltInfo> Map;

    /// <summary>
    /// Job/Burst 내부에서 안전하게 읽기 전용으로 전달할 수 있는 뷰를 반환합니다.
    /// </summary>
    public NativeParallelHashMap<int2, BeltInfo>.ReadOnly AsReadOnly() => Map.AsReadOnly();

    /// <summary>
    /// 해당 좌표에 벨트가 존재하는지 확인하고 벨트 정보를 가져옵니다.
    /// </summary>
    public bool TryGetBelt(int2 position, out BeltInfo info)
    {
        return Map.TryGetValue(position, out info);
    }

    /// <summary>
    /// 해당 좌표에 벨트가 존재하는지 여부를 반환합니다.
    /// </summary>
    public bool HasBeltAt(int2 position)
    {
        return Map.ContainsKey(position);
    }
}
