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
/// [7. 결과 정책]    : 프리팹 DB 환경에서 프리팹 누락 시 Strict Fail (스폰 거부 및 에러 로깅). DB 부재 시 Fallback 아키타입 생성
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
