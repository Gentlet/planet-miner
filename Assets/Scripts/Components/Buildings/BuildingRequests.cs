using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// [1. 역할]         : 단일 완공 건물 엔티티 생성 및 컴포넌트 초기화 요청 (Command / Transient Request)
/// [2. Producer]     : Task 7.5 공사 완료 시스템, 부트스트랩 스폰, 테스트 러너
/// [3. Consumer]     : BuildingLifecycleApplySystem (StateApplyGroup)
/// [4. Create Phase]: CommandGroup / ExecutionGroup / StateApplyGroup
/// [5. Consume Phase]: StateApplyGroup (Phase 5)
/// [6. 수명주기]     : 단일 프레임 소비 (Consume-on-Apply). EndStateApplyEntityCommandBufferSystem에 의해 처리 및 엔티티 파괴
/// [7. 결과 정책]    : DB/프리팹 누락 시 스폰 거부·요청 소비 및 SimulationFatalError 게시. 대체 생성하지 않는다.
/// [8. 안전망]       : 요청 처리 후 즉시 파괴되어 고아 요청 누수 방지
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
/// [1. 역할]         : 완공된 건물 철거 및 내용물/자재 반환 요청 (Command / Transient Request)
/// [2. Producer]     : 플레이어/UI 철거 액션, 테스트 러너 (Command 검증 전에 요청을 실체화)
/// [3. Consumer]     : BuildingDemolitionCommandSystem 검증 -> BuildingLifecycleApplySystem 소비 / ItemLifecycleApplySystem 조회
/// [4. Create Phase]: BuildingDemolitionCommandSystem 실행 전. EndCommand 재생이나 그 이후에 새 요청을 생성하지 않는다.
/// [5. Consume Phase]: 거부/중복 요청은 EndCommand, 유효 요청은 EndStateApply에서 삭제
/// [6. 수명주기]     : Command 이후 남은 요청은 철거 확정 대상이다. StateApply까지 요청 대상과 철거 가능 조건을 유지한다.
/// [7. 결과 정책]    : 내용물 방출, 건설 재료 환급, 건물 파괴. 완료 생산물 폐기 및 대상 Storage/Product Spawn 거부.
/// </summary>
public struct DemolishBuildingRequest : IComponentData, IRequestComponent
{
    /// <summary>
    /// 철거할 대상 완공 건물 엔티티.
    /// Command 종료 후에는 검증된 대상만 남으며, StateApply에서는 이 값을 변경하지 않는다.
    /// ItemLifecycleApplySystem은 EndStateApply 재생 이전에 요청을 읽어 생성 여부를 결정한다.
    /// </summary>
    public Entity TargetBuilding;

    public DemolishBuildingRequest(Entity targetBuilding)
    {
        TargetBuilding = targetBuilding;
    }
}
