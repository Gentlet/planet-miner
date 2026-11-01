using Unity.Entities;

/// <summary>
/// 초기 지급 정의 원본을 식별한다. 사전 등록의 유효성은 초기화 검사 후 보장하며 실제 지급 완료 상태와 구분한다.
/// </summary>
public struct StartingItemConfig : IComponentData
{
}

/// <summary>초기화가 한 번 게시하며 World 수명 동안 읽기 전용인 품목별 지급 개수다.</summary>
[InternalBufferCapacity(0)]
public struct StartingItemConfigElement : IBufferElementData
{
    public ItemTypeEnum ItemType;
    public int Quantity;

    public StartingItemConfigElement(ItemTypeEnum itemType, int quantity)
    {
        ItemType = itemType;
        Quantity = quantity;
    }
}
