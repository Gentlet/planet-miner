using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 플레이어 배치 묶음의 충돌 정책·기본 Tick·World 접수 순서를 전달하는 일회성 요청 헤더.
/// 부착 엔티티: PlacementRequestCandidateElement 버퍼를 가진 별도 배치 요청 엔티티. 현장/완공 건물에 붙이지 않는다.
/// 생성: 플레이어 입력은 사전 확인 후 BuildingPlacementRequestUtility.Submit으로 요청과 후보를 함께 접수한다. 런타임 UI 연결은 후속이며 테스트도 같은 API를 사용한다.
/// 이용: BuildingPlacementCommandSystem(Command)이 ReceiptSequence 순서로 최종 검증하고 현장 생성/벨트 방향 변경의 승인 셀을 이번 처리 동안 공유한다.
/// 제거: 성공/실패와 관계없이 처리한 요청과 후보 버퍼를 EndCommand에서 삭제한다. 거부 요청을 자동 재시도하지 않는다.
/// </summary>
public struct BuildingPlacementRequest : IComponentData
{
    /// <summary>
    /// 다중 배치 묶음 충돌 중재 정책 (기본값: StrictAllOrNothing).
    /// </summary>
    public PlacementFlags Flags;

    /// <summary>
    /// 호출자가 명시하는 설치 기준 Tick. 0이면 공통 Command Tick을 사용하며 실제 접수 순번과 구분한다.
    /// </summary>
    public ulong RequestTick;

    /// <summary>Submit이 발급하는 양수 World 접수번호. RequestTick·후보 순서·Entity 생성 순서와 구분하며 외부에서 지정하지 않는다.</summary>
    public ulong ReceiptSequence;

    public BuildingPlacementRequest(PlacementFlags flags = PlacementFlags.StrictAllOrNothing, ulong requestTick = 0)
    {
        Flags = flags;
        RequestTick = requestTick;
        ReceiptSequence = 0;
    }
}

/// <summary>
/// 역할·목적: 배치·직접 생성이 공유하는 World 접수번호와 기본 설치 Tick의 원본. 점유나 개별 건물 Stamp는 저장하지 않는다.
/// 생성·소유권: BuildingPlacementRequestUtility가 NextValue=1, CurrentTick=1로 준비한다. Submit/SubmitSpawn은 번호를 발급하고 배치 Command만 Tick을 전진한다.
/// 수명: 요청 소비와 무관하게 World 종료까지 유지하며 번호를 재사용하지 않는다.
/// </summary>
public struct BuildingPlacementReceiptSequence : IComponentData
{
    public ulong NextValue;
    public ulong CurrentTick;
}

/// <summary>
/// 역할·목적: 요청 하나에 단일/다중 건물의 종류·기본 크기·좌하단 위치·방향·개별 Tick을 전달한다.
/// 부착 엔티티: BuildingPlacementRequest와 같은 요청 엔티티의 버퍼. 후보 자체는 공사 현장의 상태가 아니다.
/// 생성: 플레이어 입력이 Submit에 후보 묶음을 전달한다. 현재 연결된 호출자는 테스트이며 런타임 UI는 후속이다.
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
