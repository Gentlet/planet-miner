using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 월드 아이템의 공간 좌표(GridPosition -> Item Entity) 고속 조회를 위한 Unmanaged 싱글톤 인덱스.
/// 
/// [책임]
/// - 그리드 좌표(int2)를 키로 하여 해당 위치에 존재하는 월드 아이템들을 O(1)에 조회합니다.
/// - 한 셀에 여러 아이템이 존재할 수 있도록 NativeParallelMultiHashMap 기반으로 구성됩니다.
/// - SynchronizationGroup의 ItemSpatialSyncSystem에 의해 매 프레임 살아있는 월드 아이템으로 완전 재구축(Clear & Rebuild)됩니다.
/// </summary>
public struct ItemSpatialIndex : IComponentData
{
    public NativeParallelMultiHashMap<int2, Entity> Map;

    /// <summary>
    /// Job/Burst 내부에서 안전하게 읽기 전용으로 전달할 수 있는 뷰를 반환합니다.
    /// </summary>
    public NativeParallelMultiHashMap<int2, Entity>.ReadOnly AsReadOnly() => Map.AsReadOnly();

    /// <summary>
    /// 해당 좌표에 첫 번째 아이템이 존재하는지 확인하고 이터레이터를 가져옵니다.
    /// </summary>
    public bool TryGetFirstItem(int2 position, out Entity item, out NativeParallelMultiHashMapIterator<int2> iterator)
    {
        return Map.TryGetFirstValue(position, out item, out iterator);
    }

    /// <summary>
    /// 이터레이터를 통해 해당 좌표의 다음 아이템을 가져옵니다.
    /// </summary>
    public bool TryGetNextItem(out Entity item, ref NativeParallelMultiHashMapIterator<int2> iterator)
    {
        return Map.TryGetNextValue(out item, ref iterator);
    }

    /// <summary>
    /// 해당 좌표에 존재하는 아이템의 총 개수를 반환합니다.
    /// </summary>
    public int CountItemsAt(int2 position)
    {
        return Map.CountValuesForKey(position);
    }

    /// <summary>
    /// 해당 좌표에 아이템이 하나라도 존재하는지 여부를 반환합니다.
    /// </summary>
    public bool HasItemAt(int2 position)
    {
        return Map.ContainsKey(position);
    }
}
