using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 벨트 위의 월드 아이템 엔티티에 부착되는 입고 의사결정 컴포넌트 (상태-의사결정 분리).
/// 벨트 끝(Progress >= 1.0f - Epsilon)에 도달하여 건물 입고를 시도할 때 활성화(Enable).
/// </summary>
public struct BuildingItemInputDecision : IComponentData, IEnableableComponent
{
    /// <summary>
    /// 입고를 시도할 대상 건물 엔티티.
    /// </summary>
    public Entity TargetBuilding;

    /// <summary>
    /// 입고 가능 여부 (필터 통과 및 건물 내 여유 공간 존재 시 true).
    /// </summary>
    public bool CanDeposit;

    /// <summary>
    /// 배정된 창고/건물 슬롯 번호 (0 ~ SlotCount - 1).
    /// Decision 단계에서는 기본값 -1(미배정)이며, ReservationPhase에서 경합 해결 후 최종 확정.
    /// </summary>
    public int TargetSlotIndex;

    public BuildingItemInputDecision(Entity targetBuilding, bool canDeposit = false, int targetSlotIndex = -1)
    {
        TargetBuilding = targetBuilding;
        CanDeposit = canDeposit;
        TargetSlotIndex = targetSlotIndex;
    }
}

/// <summary>
/// 보관/생산 건물 엔티티에 부착되는 출고 의사결정 컴포넌트 (상태-의사결정 분리).
/// 건물 내부에 아이템이 있고 외부 벨트로 방출할 조건이 만족되었을 때 활성화(Enable).
/// (건물당 프레임당 1개 아이템 순차 방출)
/// </summary>
public struct BuildingItemOutputDecision : IComponentData, IEnableableComponent
{
    /// <summary>
    /// 출고 가능 여부 (외향 벨트 존재 및 벨트 시작점 간격 확보 시 true).
    /// </summary>
    public bool CanOutput;

    /// <summary>
    /// 방출할 대상 아이템 엔티티 (건물 StoredItemElement 버퍼의 FIFO 0번 아이템 등).
    /// </summary>
    public Entity ItemToOutput;

    /// <summary>
    /// 아이템이 올려질 대상 외향 벨트의 그리드 좌표.
    /// </summary>
    public int2 TargetBeltPosition;

    public BuildingItemOutputDecision(bool canOutput, Entity itemToOutput, int2 targetBeltPosition)
    {
        CanOutput = canOutput;
        ItemToOutput = itemToOutput;
        TargetBeltPosition = targetBeltPosition;
    }
}
