using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 배치 후보 묶음의 충돌 정책과 기본 요청 Tick을 전달하는 일회성 요청 헤더.
/// 부착 엔티티: PlacementRequestCandidateElement 버퍼를 가진 별도 배치 요청 엔티티. 현장/완공 건물에 붙이지 않는다.
/// 생성: 외부 입력이 Command 실행 전에 요청과 후보를 준비하는 계약이다. 현재 런타임 UI/블루프린트 Producer는 미구현이며 테스트가 직접 생성한다.
/// 이용: BuildingPlacementCommandSystem(Command)이 후보를 검증하고 승인한 후보의 현장·자재 요구량·PlacementStamp를 기록한다.
/// 제거: 성공/실패와 관계없이 처리한 요청과 후보 버퍼를 EndCommand에서 삭제한다. 거부 요청을 자동 재시도하지 않는다.
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
/// 역할·목적: 요청 하나에 단일/다중 건물의 종류·기본 크기·좌하단 위치·방향·개별 Tick을 전달한다.
/// 부착 엔티티: BuildingPlacementRequest와 같은 요청 엔티티의 버퍼. 후보 자체는 공사 현장의 상태가 아니다.
/// 생성: 외부 요청 Producer가 헤더와 함께 채운다. 현재 직접 생성은 배치 테스트가 담당한다.
/// 이용: BuildingPlacementCommandSystem(Command)이 버퍼 순서로 검증하며, 승인 후보의 원래 인덱스를 PlacementStamp.Order로 사용한다.
/// 개별 RequestTick이 양수이면 헤더/현재 Tick보다 우선하지만 후보의 처리 순서를 Tick으로 재정렬하지는 않는다.
/// 제거: 처리 후 요청 엔티티와 함께 EndCommand에서 제거한다. 승인 후보의 값은 별도 현장 컴포넌트로 옮겨진다.
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
