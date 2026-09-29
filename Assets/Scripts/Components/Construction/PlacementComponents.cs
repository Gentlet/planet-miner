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
