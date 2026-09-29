using Unity.Entities;

/// <summary>
/// 채굴기, 제작기 등 생산 건물의 출력 대기 버퍼 요소 (Unmanaged).
/// 완성품과 부산물을 일반 보관/재료 버퍼(StoredItemElement)와 물리적으로 분리.
/// </summary>
[InternalBufferCapacity(8)]
public struct ProductItemElement : IBufferElementData
{
    public Entity ItemEntity;     // 생산된 아이템 엔티티
    public ItemTypeEnum ItemType; // 아이템 종류 (O(1) 캐싱)
    public int SlotIndex;         // 소속된 슬롯 번호 (Crafter의 경우 0=주완성품, 1=부산품)

    public ProductItemElement(Entity itemEntity, ItemTypeEnum itemType, int slotIndex = 0)
    {
        ItemEntity = itemEntity;
        ItemType = itemType;
        SlotIndex = slotIndex;
    }
}
