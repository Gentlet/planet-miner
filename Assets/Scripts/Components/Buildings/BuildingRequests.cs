using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 완공 건물 하나의 직접 생성과 런타임 컴포넌트 초기화를 요청한다.
/// 부착 엔티티: 위치·종류 등을 담은 별도 일회성 요청 엔티티. 생성될 건물에 붙이는 상태가 아니다.
/// 생성: 외부 Producer가 소비 전에 실체화하는 계약이며 현재 직접 생성은 테스트가 담당한다.
/// 현재 ConstructionLifecycleApplySystem은 이 요청을 만들지 않고 공통 SpawnBuilding API로 완공 건물을 직접 생성한다.
/// 이용: BuildingLifecycleApplySystem(StateApply)이 등록된 프리팹을 인스턴스화한다. DB/항목 누락 시 대체 생성 없이 중단 오류를 기록한다.
/// 제거: 성공/실패와 관계없이 처리한 요청을 EndStateApply에서 삭제한다. 생성 실패 시 요청 자체를 재시도하지 않는다.
/// </summary>
public struct SpawnBuildingRequest : IComponentData, IRequestComponent
{
    /// <summary>
    /// 스폰할 대상 건물 종류.
    /// </summary>
    public BuildingTypeEnum TargetType;

    /// <summary>
    /// 건물이 배치될 원점 그리드 좌표.
    /// </summary>
    public int2 Position;

    /// <summary>
    /// 건물이 바라보는 방향.
    /// </summary>
    public DirectionEnum Direction;

    /// <summary>
    /// 건물의 Footprint 크기 (int2.zero 이하인 경우 Config 또는 기본 규격을 사용).
    /// </summary>
    public int2 FootprintSize;

    /// <summary>
    /// 배치 우선순위 타임스탬프 (배치 요청 또는 공사 현장에서 승계).
    /// </summary>
    public PlacementStamp Stamp;

    public SpawnBuildingRequest(
        BuildingTypeEnum targetType,
        int2 position,
        DirectionEnum direction = DirectionEnum.Up,
        int2 footprintSize = default,
        PlacementStamp stamp = default)
    {
        TargetType = targetType;
        Position = position;
        Direction = direction;
        FootprintSize = footprintSize;
        Stamp = stamp;
    }
}

/// <summary>
/// 역할·목적: 완공 건물의 철거 승인을 요청한다. 공사 현장 취소는 CancelConstructionRequest로 처리한다.
/// 부착 엔티티: TargetBuilding을 참조하는 별도 일회성 요청 엔티티. 승인 상태와 구분한다.
/// 생성: 외부 입력이 BuildingDemolitionCommandSystem 실행 전에 실체화하는 계약이며 현재 직접 Producer는 테스트다.
/// 이용: BuildingDemolitionCommandSystem(Command)만 요청을 검증/소비하고 승인 결과를 대상의 PendingBuildingDemolition으로 전달한다.
/// 승인 후 입고·생산·출고·운송은 앞단에서 중단하며 BuildingLifecycleApplySystem(StateApply)이 내용물 반환·비용 환급·철거를 반영한다.
/// 제거: 승인/거부/중복 요청 모두 EndCommand에서 삭제한다. 실제 건물 삭제는 승인 상태를 통해 EndStateApply까지 이어진다.
/// </summary>
public struct DemolishBuildingRequest : IComponentData, IRequestComponent
{
    /// <summary>
    /// 철거할 대상 완공 건물 엔티티.
    /// Command 이후에는 요청이 삭제되고 대상 건물의 승인 상태가 수명주기를 이어받는다.
    /// </summary>
    public Entity TargetBuilding;

    public DemolishBuildingRequest(Entity targetBuilding)
    {
        TargetBuilding = targetBuilding;
    }
}
