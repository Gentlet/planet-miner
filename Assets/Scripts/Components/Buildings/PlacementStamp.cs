using Unity.Entities;

/// <summary>
/// 월드 배치 순서를 나타내는 불변 메타데이터.
/// - 권위 있는 배치 요청이 확정될 때 Tick과 같은 Tick 내 결정적 Order를 기록.
/// - 공사 현장과 실제 건물 생성까지 요청의 값을 그대로 전달하며 이후 변경하지 않음.
/// - 실제 Entity 생성 순서나 Entity.Index/Version에 의존하지 않음.
/// - 배치 순서가 게임 규칙에 필요한 엔티티에만 선택적으로 부착.
/// </summary>
public struct PlacementStamp : IComponentData
{
    public ulong Tick;
    public uint Order;

    public PlacementStamp(ulong tick, uint order)
    {
        Tick = tick;
        Order = order;
    }

    /// <summary>
    /// 다른 배치보다 먼저 확정된 배치인지 비교.
    /// </summary>
    public bool IsEarlierThan(in PlacementStamp other)
    {
        return Tick < other.Tick ||
               (Tick == other.Tick && Order < other.Order);
    }
}
