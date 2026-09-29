using Unity.Entities;

/// <summary>
/// 창고의 보관 슬롯 용량을 정의하는 핵심 컴포넌트.
/// 
/// [책임]
/// - 창고가 보유한 총 슬롯 칸수(SlotCount)를 정의.
/// - 슬롯당 최대 스택 수는 아이템별 고유 설정에 따라 결정.
/// </summary>
public struct Storage : IComponentData
{
    public int SlotCount;

    public Storage(int slotCount)
    {
        SlotCount = slotCount;
    }
}

/// <summary>
/// 창고나 건물에 보관된 개별 아이템 엔티티와 슬롯 정보를 담는 버퍼 요소.
/// </summary>
[InternalBufferCapacity(16)]
public struct StoredItemElement : IBufferElementData
{
    public Entity ItemEntity;     // 보관된 아이템 엔티티
    public ItemTypeEnum ItemType; // 아이템 종류 (O(1) 캐싱)
    public int SlotIndex;         // 소속된 슬롯 번호 (0 ~ SlotCount - 1)

    public StoredItemElement(Entity itemEntity, ItemTypeEnum itemType, int slotIndex)
    {
        ItemEntity = itemEntity;
        ItemType = itemType;
        SlotIndex = slotIndex;
    }
}
