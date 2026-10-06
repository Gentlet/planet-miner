using Unity.Entities;

/// <summary>
/// 역할·목적: 천연 자원의 품목과 남은 매장량을 소유한다. 위치 원본은 별도 GridPosition이다.
/// 부착 엔티티: 셀별 자원 노드 실물 엔티티다.
/// 생성: ResourceGenerationCommandSystem(Command)이 등록된 자원 프리팹을 인스턴스화하여 EndCommand에 품목·매장량·좌표를 기록한다.
/// 이용: ResourceSpatialSyncSystem(Synchronization)이 위치를 등록하고 MinerDecisionSystem(Decision)이 자격을 검사한다. MinerExecutionSystem(Execution)이 유한 모드에서 매장량을 차감한다.
/// 제거: 매장량 고갈 시 MinerExecutionSystem이 EndBuilding에 자원 엔티티 삭제를 기록한다. 무한 모드는 Amount를 차감하지 않는다.
/// </summary>
public struct ResourceNode : IComponentData
{
    public ItemTypeEnum ResourceType; // 채굴되는 광석 종류 (1 byte)
    public int Amount;                // 남은 매장량 (4 bytes)

    public ResourceNode(ItemTypeEnum resourceType, int amount)
    {
        ResourceType = resourceType;
        Amount = amount;
    }
}
