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
/// 역할·목적: 소유자가 보관하는 개별 실물 참조·품목 캐시·슬롯을 관리한다. ItemOwnership.Owner와 함께 유지한다.
/// 부착 엔티티: 보관/제작 건물·공사 현장·드론 수행자의 보관 버퍼다. 실제 아이템은 별도 엔티티다.
/// 생성: BuildingLifecycleUtility의 건물 초기화와 BuildingPlacementCommandSystem(Command)의 현장 생성이 빈 버퍼를 붙인다. 실제 드론 등록 Producer는 후속이다.
/// 이용: BuildingItemStorageApplySystem/ItemLifecycleApplySystem/ItemOwnershipApplySystem(StateApply)이 입출고·생성을 반영하고 CrafterExecutionSystem(Execution)이 제작 재료를 선소비한다.
/// DroneTaskLifecycleApplySystem(StateApply)은 공통 실물 API 성공분으로 인계하고 취소·철거·완공은 해당 Construction/Building 시스템이 반환·소비한다.
/// 제거: 출고·인계·소비 때 원래 소유 버퍼에서 항목을 제거한다. 버퍼 자체는 소유자 삭제까지 유지한다. 소비는 항목 제거 후 실물 삭제를 기록한다.
/// </summary>
[InternalBufferCapacity(16)]
public struct StoredItemElement : IBufferElementData
{
    public Entity ItemEntity;     // 보관된 아이템 엔티티
    public ItemTypeEnum ItemType; // 아이템 종류 (O(1) 캐싱)
    public int SlotIndex;         // 건물은 Storage/전용 입력 슬롯 규칙, 현장·드론은 각 인계 계약의 슬롯 번호를 사용한다.

    public StoredItemElement(Entity itemEntity, ItemTypeEnum itemType, int slotIndex)
    {
        ItemEntity = itemEntity;
        ItemType = itemType;
        SlotIndex = slotIndex;
    }
}
