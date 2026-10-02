using System;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 공사 현장 상태를 제어하는 플래그.
/// </summary>
[Flags]
public enum ConstructionSiteFlags : byte
{
    None = 0,

    /// <summary>
    /// 공사 현장 바닥에 월드 아이템이 잔류하여 드론의 회수 완료를 대기 중인 상태.
    /// 회수가 완료되기 전까지는 완공으로 전환되지 않음.
    /// </summary>
    AwaitingItemClearance = 1 << 0,

    /// <summary>
    /// 공사 현장이 취소되어 파괴 진행 중인 상태. 완공 전환 대상에서 즉시 제외됨.
    /// </summary>
    Cancelled = 1 << 1
}

/// <summary>
/// 건설 진행 중인 공사 현장 엔티티를 나타내는 Unmanaged Blittable 컴포넌트.
/// 
/// [소유권 및 상태 전이 계약]
/// - 생성 시점: 배치 요청이 검증을 통과하여 승인되었을 때 CommandGroup에서 생성.
/// - 공간 인덱스: BuildingTypeEnum.ConstructionSite 및 BuildingFootprint를 통해 BuildingSpatialIndex에 완공 건물과 동일하게 통합 등록.
/// - 자재 운반 및 공사: Drone/Worker에 의해 자재가 조달되고 건설 진행도(Progress)가 1.0f에 도달하면 완공 건물로 전환.
/// - 완공 전환 시점: StateApplyGroup에서 ConstructionSite 컴포넌트가 제거되고 TargetBuildingType 및 해당 건물의 전용 컴포넌트(Storage, Miner 등)가 부착됨.
/// </summary>
public struct ConstructionSite : IComponentData
{
    /// <summary>
    /// 완공 시 생성될 건물의 목표 타입.
    /// </summary>
    public BuildingTypeEnum TargetBuildingType;

    /// <summary>
    /// 건설 진행도 (0.0f ~ 1.0f).
    /// </summary>
    public float Progress;

    /// <summary>
    /// 공사 현장 상태 플래그.
    /// </summary>
    public ConstructionSiteFlags Flags;

    public ConstructionSite(BuildingTypeEnum targetBuildingType, float progress = 0.0f, ConstructionSiteFlags flags = ConstructionSiteFlags.None)
    {
        TargetBuildingType = targetBuildingType;
        Progress = progress;
        Flags = flags;
    }
}

/// <summary>
/// 공사 현장에 필요한 자재 요구량, 조달량 및 운송 예약량 버퍼 엘리먼트 (Unmanaged).
/// </summary>
public struct ConstructionMaterialRequirementElement : IBufferElementData
{
    public ItemTypeEnum ItemType;
    public int RequiredQuantity;  // 완공에 필요한 총 수량
    public int DeliveredQuantity; // 현장에 도착/수령된 실제 수량
    public int ReservedQuantity;  // 등록된 운송 건의 활성 예약 합계 (출발 대기 포함, 공사 수명주기가 정산)

    // 순수 계산 프로퍼티 (상태 부작용 없음)
    public int RemainingRequired => math.max(0, RequiredQuantity - DeliveredQuantity);
    public int RemainingToReserve => math.max(0, RequiredQuantity - (DeliveredQuantity + ReservedQuantity));
    public bool IsSatisfied => DeliveredQuantity >= RequiredQuantity;
    public bool IsFullyReserved => (DeliveredQuantity + ReservedQuantity) >= RequiredQuantity;

    public ConstructionMaterialRequirementElement(
        ItemTypeEnum itemType,
        int requiredQuantity,
        int deliveredQuantity = 0,
        int reservedQuantity = 0)
    {
        ItemType = itemType;
        RequiredQuantity = requiredQuantity;
        DeliveredQuantity = deliveredQuantity;
        ReservedQuantity = reservedQuantity;
    }
}
