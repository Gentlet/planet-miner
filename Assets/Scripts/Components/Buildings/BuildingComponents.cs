using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 월드에 존재하는 건물의 종류를 정의하는 열거형.
/// </summary>
public enum BuildingTypeEnum : byte
{
    None,
    Storage,
    Miner,
    Crafter,
    PowerPole,
    CoalGenerator,
    ResearchBuilding,
    MainFacility,
    DroneStation,
    Count
}

/// <summary>
/// 건물의 종류 식별 컴포넌트.
/// </summary>
public struct BuildingType : IComponentData
{
    public BuildingTypeEnum Type;

    public BuildingType(BuildingTypeEnum type)
    {
        Type = type;
    }

    public static implicit operator BuildingTypeEnum(BuildingType b) => b.Type;
    public static implicit operator BuildingType(BuildingTypeEnum type) => new BuildingType(type);
}

/// <summary>
/// 건물이 차지하는 기본 그리드 타일 크기 컴포넌트.
/// </summary>
public struct BuildingFootprint : IComponentData
{
    public int2 Size;

    public BuildingFootprint(int width, int height)
    {
        Size = new int2(width, height);
    }

    public BuildingFootprint(int2 size)
    {
        Size = size;
    }

    /// <summary>
    /// 건물 회전 방향에 따른 유효 점유 크기를 반환.
    /// Left / Right (90도 회전)인 경우 가로와 세로가 스왑.
    /// </summary>
    public int2 GetEffectiveSize(DirectionEnum direction)
    {
        int2 normalized = math.max(Size, new int2(1, 1));
        return (direction == DirectionEnum.Left || direction == DirectionEnum.Right)
            ? new int2(normalized.y, normalized.x)
            : normalized;
    }
}

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
