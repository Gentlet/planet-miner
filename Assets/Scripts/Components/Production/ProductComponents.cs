using Unity.Entities;

/// <summary>
/// 역할·목적: 생산된 실물의 출력 대기를 관리하여 재료/보관 버퍼와 분리한다. ItemOwnership.Owner와 함께 유지한다.
/// 부착 엔티티: Miner·Crafter 생산 건물의 출력 버퍼다. 실물 아이템은 별도 엔티티다.
/// 생성: BuildingLifecycleUtility가 빈 버퍼를 붙이고 ItemLifecycleApplySystem(StateApply)이 ProductResult를 실물로 변환하여 항목을 추가한다.
/// 이용: ProductItemOutputDecisionSystem(Decision)이 출고 후보를 읽고 BuildingItemStorageApplySystem(StateApply)이 출고 항목을 제거한다. CrafterRecipeCommandSystem(Command)은 잔여 입력을 출력으로 이관한다.
/// 제거: 실물 출고·반환 때 항목을 제거하고 버퍼 자체는 건물 삭제까지 유지한다. SlotIndex=0은 주생산품, 후속 슬롯은 부산물/잔여 출력이다.
/// </summary>
[InternalBufferCapacity(8)]
public struct ProductItemElement : IBufferElementData
{
    public Entity ItemEntity;     // 생산된 아이템 엔티티
    public ItemTypeEnum ItemType; // 아이템 종류 (O(1) 캐싱)
    public int SlotIndex;         // 0은 주생산품, 1 이상은 부산물/레시피 변경 후 잔여 출력 슬롯이다.

    public ProductItemElement(Entity itemEntity, ItemTypeEnum itemType, int slotIndex = 0)
    {
        ItemEntity = itemEntity;
        ItemType = itemType;
        SlotIndex = slotIndex;
    }
}
