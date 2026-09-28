using System;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 다중 타일/배치 묶음 설치 시의 충돌 중재 정책 플래그.
/// </summary>
[Flags]
public enum PlacementFlags : byte
{
    /// <summary>
    /// 기본 정책: 다중 배치 묶음 중 단 1개 타일이라도 충돌/불가 시 전체 배치 요청을 롤백(전부 취소).
    /// </summary>
    StrictAllOrNothing = 0,

    /// <summary>
    /// 부분 배치 허용: 충돌하는 후보만 건너뛰고, 설치 가능한 나머지 후보는 정상 승인하여 배치.
    /// </summary>
    AllowPartialPlacement = 1 << 0
}

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
/// 건물 배치 타당성 검증 결과 코드.
/// </summary>
public enum PlacementValidationCode : byte
{
    /// <summary>
    /// 정상 배치 가능.
    /// </summary>
    Success = 0,

    /// <summary>
    /// 비정상 크기/풋프린트 (가로 또는 세로가 1 미만).
    /// </summary>
    InvalidFootprint = 1,

    /// <summary>
    /// 이미 완공된 건물이 점유 중임.
    /// </summary>
    BlockedByBuilding = 2,

    /// <summary>
    /// 이미 다른 공사 현장이 점유 중이거나, 동일 프레임 앞선 요청이 선점함.
    /// </summary>
    BlockedByConstructionSite = 3,

    /// <summary>
    /// 채굴기인데 풋프린트 하부에 자원 노드가 하나도 없음.
    /// </summary>
    RequiresResourceNode = 4,

    /// <summary>
    /// 기존 벨트 위에 새 벨트를 덮어쓰는 업그레이드/방향 교체 배치 (승인).
    /// </summary>
    BeltUpgradeAllowed = 5,

    /// <summary>
    /// StrictAllOrNothing 정책으로 인해 묶음 내 다른 후보의 충돌로 인해 함께 롤백/취소됨.
    /// </summary>
    BatchAllOrNothingRolledBack = 6,

    /// <summary>
    /// 연구가 아직 완료되지 않아 잠긴 건물임 (건설 거부).
    /// </summary>
    BlockedByResearch = 7
}

/// <summary>
/// 건물 배치 타당성 검증 상세 결과 구조체 (Unmanaged).
/// </summary>
public struct PlacementValidationResult
{
    public PlacementValidationCode Code;
    public bool HasGroundItems;

    public bool IsValid => Code == PlacementValidationCode.Success || Code == PlacementValidationCode.BeltUpgradeAllowed;

    public PlacementValidationResult(PlacementValidationCode code, bool hasGroundItems = false)
    {
        Code = code;
        HasGroundItems = hasGroundItems;
    }
}

/// <summary>
/// 다중/단일 배치 검증에 전달되는 후보 매개변수 구조체 (Unmanaged).
/// </summary>
public struct PlacementCandidate
{
    public BuildingTypeEnum TargetType;
    public int2 FootprintSize;
    public int2 OriginPosition;
    public DirectionEnum Direction;

    public PlacementCandidate(BuildingTypeEnum targetType, int2 footprintSize, int2 originPosition, DirectionEnum direction = DirectionEnum.Up)
    {
        TargetType = targetType;
        FootprintSize = footprintSize;
        OriginPosition = originPosition;
        Direction = direction;
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
    public int ReservedQuantity;  // 운송 중인 예약 수량 (드론 출발 시)

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
