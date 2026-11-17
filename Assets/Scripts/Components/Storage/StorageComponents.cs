using Unity.Entities;

/// <summary>
/// 역할·목적: 건물 보관 슬롯 수를 소유한다. 한 슬롯의 아이템 최대 개수는 ItemRegistry의 품목별 MaxStack과 구분한다.
/// 부착 엔티티: Storage·MainFacility·DroneStation 등 보관 건물과 재료 입력을 가진 Crafter다.
/// 생성: BuildingLifecycleUtility가 건물 초기화에 붙이며 Crafter는 0슬롯으로 시작한다.
/// 이용: BuildingStorageInputReservationSystem(Reservation), BuildingItemStorageApplySystem(StateApply), 드론 계획/인계가 용량을 검사한다. CrafterRecipeCommandSystem(Command)이 레시피 입력 슬롯 수로 갱신한다.
/// 제거: 건물 엔티티 삭제 시 함께 제거한다. 공사 현장과 드론 적재량을 이 슬롯 수로 정의하지 않는다.
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
/// 인덱스가 Storage 슬롯 번호인 건물의 실제 재고다. 빈 슬롯은 기본값이다.
/// 소비/입출고의 소유 페이즈가 수량을 갱신하며 건물 생성, 정거장 역할 전환은 해당 종료 ECB에 공개한다.
/// </summary>
[InternalBufferCapacity(4)]
public struct StorageSlotElement : IBufferElementData
{
    public ItemTypeEnum ItemType;
    public int Quantity;

    public StorageSlotElement(ItemTypeEnum itemType, int quantity)
    {
        ItemType = itemType;
        Quantity = quantity;
    }
}
