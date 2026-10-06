using Unity.Entities;

/// <summary>
/// 역할·목적: 건물 입력 슬롯별 허용 품목을 정의하여 같은 품목의 재료를 전용 슬롯에 보관한다.
/// 부착 엔티티: 현재 BuildingLifecycleUtility가 만드는 Crafter 건물의 버퍼다. 일반 보관 건물에는 별도 전용 슬롯을 만들지 않는다.
/// 생성: Crafter 생성 시 빈 버퍼를 붙이고 CrafterRecipeCommandSystem(Command)이 BuildingInputSlotUtility로 품목별 ceil(요구량/MaxStack) 슬롯을 구성한다.
/// 이용: BuildingStorageInputReservationSystem(Reservation)과 드론의 보관 검증이 읽는다. 버퍼 인덱스는 StoredItemElement.SlotIndex, 길이는 Storage.SlotCount와 대응한다.
/// 제거: 레시피 변경·해제 시 기존 슬롯 구성을 비우고 재작성한다. 버퍼 자체는 건물 삭제까지 유지한다.
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
