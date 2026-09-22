using Unity.Entities;

/// <summary>
/// 생산 건물의 Execution 결과를 StateApply 단계로 전달하는 임시 생산 결과 버퍼 요소.
/// 
/// [책임]
/// - Execution 단계에서 생산이 완료되면 생산 건물의 DynamicBuffer<ProductResult>에 결과를 기록합니다.
/// - StateApply 단계의 ItemLifecycleApplySystem이 결과를 소비하여 실제 Item Entity를 생성하고
///   생산 건물의 DynamicBuffer<ProductItemElement>에 반영합니다.
/// - Buffer가 비어 있으면 처리할 생산 결과가 없음을 의미합니다.
/// - Count를 통해 한 종류의 생산 결과가 여러 개 생성되는 경우를 표현할 수 있습니다.
/// - SlotIndex는 생산 건물 내부 출력 정책(예: 0=주 생산물, 1=부산물)에 사용합니다.
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
