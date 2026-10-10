using System;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 배치 후보 묶음의 전체 승인/취소 또는 부분 승인 충돌 정책을 선택한다.
/// 부착 엔티티: 열거형 자체는 붙이지 않고 BuildingPlacementRequest.Flags에 포함한다.
/// 생성·이용: 외부 배치 Producer가 선택하고 BuildingPlacementCommandSystem(Command)이 검증 실패 후보가 있는 묶음을 처리할 때 적용한다.
/// 제거: 요청 엔티티가 EndCommand에서 삭제되면 함께 사라진다. StrictAllOrNothing은 기본값 0이며 실제 생성 후 rollback 요청이 아니다.
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
/// 역할·목적: 배치 후보의 승인/거부 이유를 구분하며 BeltUpgradeAllowed는 승인 가능한 기존 벨트 변경이다.
/// 부착 엔티티: 열거형 자체는 붙이지 않고 임시 PlacementValidationResult.Code에 포함한다.
/// 생성·이용: BuildingPlacementValidationUtility가 충돌·해금·자원을 검사하고 BuildingPlacementCommandSystem(Command)이 묶음 정책/현장 생성에 사용한다.
/// 제거: 요청 처리의 임시 검증 배열과 함께 수명이 끝난다. 별도 월드 상태나 공개 배치 결과로 영속 보관하지 않는다.
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
/// 역할·목적: 후보의 승인/거부 사유와 바닥 아이템 유무를 반환하는 배치 검증 결과.
/// 부착 엔티티: 없음. ECS 컴포넌트가 아닌 BuildingPlacementCommandSystem의 임시 검증 값이다.
/// 생성·이용: BuildingPlacementValidationUtility가 계산하고 Command가 현장 생성/정리 대기 플래그를 결정할 때 읽는다.
/// 제거: 해당 요청 처리 후 임시 결과 배열을 Dispose한다. 영속적인 배치 결과나 월드 상태로 보관하지 않는다.
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
/// 역할·목적: 배치 가능성 검사에 필요한 종류·기본 크기·위치·방향을 전달하는 후보 값.
/// 부착 엔티티: 없음. PlacementRequestCandidateElement를 검증용으로 변환한 일반 구조체이며 Tick은 포함하지 않는다.
/// 생성·이용: BuildingPlacementCommandSystem이 임시 배열로 만들고 BuildingPlacementValidationUtility가 묶음 충돌을 검사한다.
/// 제거: 요청 처리 후 임시 후보 배열을 Dispose한다. 승인 시 설치 Tick·요청 접수번호·원래 후보 인덱스로 별도 PlacementStamp를 만든다.
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
