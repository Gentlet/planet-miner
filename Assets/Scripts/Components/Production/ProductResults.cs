using Unity.Entities;

/// <summary>
/// 역할·목적: 이번 실행에서 완료한 생산의 품목·실물 개수·출력 슬롯을 StateApply에 전달하는 임시 결과다.
/// 부착 엔티티: Miner·Crafter 생산 건물의 버퍼이며 아이템 실물이나 영속 생산품 목록이 아니다.
/// 생성: BuildingLifecycleUtility가 빈 버퍼를 붙이고 MinerExecutionSystem/CrafterExecutionSystem(Execution)이 생산 완료 시 결과를 추가한다.
/// 이용: ItemLifecycleApplySystem(StateApply)이 Count개의 실물을 생성하여 ProductItemElement에 등록한다. 채굴/제작 Decision도 아직 반영되지 않은 결과를 출력 여유 계산에 포함한다.
/// 제거: ItemLifecycle이 결과를 처리한 뒤 버퍼를 Clear한다. BuildingDemolitionCommandSystem(Command)은 철거 승인 때 기존 결과를 Clear하며 이미 선소비한 재료/광물을 보상하지 않는다.
/// 버퍼 자체는 건물 삭제까지 유지한다. Count는 스택 하나의 수량이 아니라 생성할 개별 아이템 엔티티 수다.
/// </summary>
[InternalBufferCapacity(4)]
public struct ProductResult : IBufferElementData
{
    public ItemTypeEnum ItemType;
    public int Count;
    public int SlotIndex;

    public ProductResult(ItemTypeEnum itemType, int count = 1, int slotIndex = 0)
    {
        ItemType = itemType;
        Count = count;
        SlotIndex = slotIndex;
    }
}
