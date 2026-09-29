using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 건물 배치 요청 엔티티에 부착되는 요청 헤더 컴포넌트 (Unmanaged).
/// 
/// [수명주기 계약 (Consume-on-Apply)]
/// - UI 또는 블루프린트 시스템에서 생성된 후 CommandGroup의 BuildingPlacementCommandSystem에서 소비.
/// - 처리가 완료되면 성공/실패 여부와 관계없이 해당 프레임에 엔티티가 파괴되어 고아 요청 누수 방지.
/// </summary>
public struct BuildingPlacementRequest : IComponentData
{
    /// <summary>
    /// 다중 배치 묶음 충돌 중재 정책 (기본값: StrictAllOrNothing).
    /// </summary>
    public PlacementFlags Flags;

    /// <summary>
    /// 요청 시점의 시뮬레이션 틱 (0이면 시스템 현재 틱을 자동 부여).
    /// </summary>
    public ulong RequestTick;

    public BuildingPlacementRequest(PlacementFlags flags = PlacementFlags.StrictAllOrNothing, ulong requestTick = 0)
    {
        Flags = flags;
        RequestTick = requestTick;
    }
}

/// <summary>
/// BuildingPlacementRequest 엔티티에 첨부되는 배치 후보 버퍼 엘리먼트 (Unmanaged).
/// 하나의 배치 요청에 다수의 후보(드래그 설치, 복사-붙여넣기 등)가 포함될 수 있음.
/// </summary>
public struct PlacementRequestCandidateElement : IBufferElementData
{
    public BuildingTypeEnum TargetType;
    public int2 FootprintSize;
    public int2 OriginPosition;
    public DirectionEnum Direction;
    public ulong RequestTick;

    public PlacementRequestCandidateElement(
        BuildingTypeEnum targetType,
        int2 footprintSize,
        int2 originPosition,
        DirectionEnum direction = DirectionEnum.Up,
        ulong requestTick = 0)
    {
        TargetType = targetType;
        FootprintSize = footprintSize;
        OriginPosition = originPosition;
        Direction = direction;
        RequestTick = requestTick;
    }

    public static implicit operator PlacementCandidate(PlacementRequestCandidateElement elem)
    {
        return new PlacementCandidate(elem.TargetType, elem.FootprintSize, elem.OriginPosition, elem.Direction);
    }
}
