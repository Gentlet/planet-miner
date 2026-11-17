using Unity.Entities;

/// <summary>
/// 채워진 생산품 슬롯을 번호순으로 보관한다. 슬롯 0은 주생산품이다.
/// 생산 Apply가 더하고, 레시피 변경은 잔여 입력을 이관한다. 벨트 출고와 드론 픽업이 수량을 차감한다.
/// 건물 단계의 반영 결과는 EndBuilding 이후 드론이 사용한다.
/// </summary>
[InternalBufferCapacity(2)]
public struct ProductSlotElement : IBufferElementData
{
    public int SlotIndex;
    public ItemTypeEnum ItemType;
    public int Quantity;

    public ProductSlotElement(int slotIndex, ItemTypeEnum itemType, int quantity)
    {
        SlotIndex = slotIndex;
        ItemType = itemType;
        Quantity = quantity;
    }
}
