using Unity.Entities;

/// <summary>
/// 월드에 매장된 천연 자원 노드 컴포넌트 (Unmanaged / Blittable).
/// GridPosition 컴포넌트와 함께 부착되어 위치를 단일 원본(Source of Truth)으로 관리합니다.
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

/// <summary>
/// 자원 관련 전역 설정 싱글톤 컴포넌트.
/// 무한 매장량 모드 등의 런타임 규칙을 제공합니다.
/// </summary>
public struct ResourceConfig : IComponentData
{
    public bool IsResourceInfinite; // true일 경우 채굴 시 Amount를 차감하지 않음

    public ResourceConfig(bool isResourceInfinite)
    {
        IsResourceInfinite = isResourceInfinite;
    }
}
