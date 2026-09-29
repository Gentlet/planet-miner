using Unity.Entities;

/// <summary>
/// 월드에 매장된 천연 자원 노드 컴포넌트 (Unmanaged / Blittable).
/// GridPosition 컴포넌트와 함께 부착되어 위치를 단일 원본(Source of Truth)으로 관리.
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
