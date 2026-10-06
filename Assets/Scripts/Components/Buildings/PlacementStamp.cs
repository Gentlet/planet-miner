using Unity.Entities;

/// <summary>
/// 역할·목적: 권위 있는 배치 Tick·같은 요청의 후보 Order를 유지하는 불변 메타데이터다. Entity 생성 순서와 구분한다.
/// 부착 엔티티: 승인된 공사 현장과 해당 현장에서 생성한 완공 건물이다. SpawnBuildingRequest에도 값으로 전달할 수 있다.
/// 생성: BuildingPlacementCommandSystem(Command)이 개별 RequestTick→헤더 Tick→현재 Tick 순으로 Tick을 정하고 원래 후보 인덱스를 Order로 기록한다.
/// 이용: ConstructionLifecycleApplySystem(StateApply)이 완공 생성에 승계하며 드론의 최초 공급 선두/거리 동률 비교가 읽는다.
/// 제거: 배치 후 값을 변경하지 않으며 현장/건물 엔티티 삭제 시 함께 제거한다. 같은 Tick의 Order는 모든 요청을 통합한 별도 순번이 아니다.
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
