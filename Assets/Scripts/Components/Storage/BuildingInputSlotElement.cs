using Unity.Entities;

/// <summary>
/// 역할·목적: 건물 입력 슬롯별 허용 품목을 정의하여 같은 품목의 재료를 전용 슬롯에 보관한다.
/// 부착 엔티티: 공통 생성이 만드는 제작기/연구건물이다. 일반 보관 건물에는 전용 슬롯을 만들지 않는다.
/// 생성: 제작기는 레시피 Command가 품목별 슬롯을 구성하고 연구는 최초 Preparation이 전체 정의의 품목별 최대 2주기분 고정 슬롯을 구성한다.
/// 이용: BuildingStorageInputReservationSystem(Reservation)과 드론의 보관 검증이 읽는다. 버퍼 인덱스는 StoredItemElement.SlotIndex, 길이는 Storage.SlotCount와 대응한다.
/// 제거: 제작기는 레시피 변경/해제 시 재작성한다. 연구의 현재 품목/총량은 별도 ResearchInputLimitElement가 제한하고 슬롯 배치는 유지한다.
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
