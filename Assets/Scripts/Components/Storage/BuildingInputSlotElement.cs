using Unity.Entities;

/// <summary>
/// 건물 입력 슬롯별 허용 품목. 슬롯 구성은 각 건물의 입력 규칙에 따라 결정한다.
/// 버퍼 인덱스는 StoredItemElement.SlotIndex와 대응하며, 버퍼 길이가 입력 슬롯 수다.
/// 실제 보관 아이템과 슬롯당 최대 스택은 각각 StoredItemElement와 ItemRegistry가 소유한다.
/// 빈 버퍼는 입력 슬롯이 없는 구성이다. 제작기는 레시피 변경 시 구성을 갱신하고 입고 예약이 이를 읽는다.
/// </summary>
[InternalBufferCapacity(0)]
public struct BuildingInputSlotElement : IBufferElementData
{
    public ItemTypeEnum ItemType;

    public BuildingInputSlotElement(ItemTypeEnum itemType)
    {
        ItemType = itemType;
    }
}
