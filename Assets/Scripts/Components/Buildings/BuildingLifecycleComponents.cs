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

/// <summary>
/// [1. 역할]         : 공사 현장에 건설 자재 전달 및 수령 요청 (Command / Transient Request)
/// [2. Producer]     : 드론 운송 도착 시스템 (Phase 9), 플레이어/디버그 인터랙션, 테스트 러너
/// [3. Consumer]     : ConstructionLifecycleApplySystem (StateApplyGroup)
/// [4. Create Phase]: ExecutionGroup / StateApplyGroup
/// [5. Consume Phase]: StateApplyGroup (Phase 5)
/// [6. 수명주기]     : 단일 프레임 소비 (Consume-on-Apply). EndStateApplyEntityCommandBufferSystem에 의해 처리 및 요청 엔티티 파괴
/// [7. 결과 정책]    : 유효한 요구 자재일 경우 수령(DeliveredQuantity 증가, StoredItemElement 소유권 이전). 초과/오품/무효 현장 시 Strict Rejection (수령 거부, 아이템 소유권 유지, 요청 엔티티 소비)
/// [8. 안전망]       : 요청 처리 후 즉시 파괴되어 고아 요청 누수 방지
/// </summary>
public struct SupplyConstructionMaterialRequest : IComponentData, IRequestComponent
{
    /// <summary>
    /// 자재를 수령할 대상 공사 현장 엔티티.
    /// </summary>
    public Entity TargetSite;

    /// <summary>
    /// 전달할 자재 아이템 엔티티.
    /// </summary>
    public Entity ItemEntity;

    /// <summary>
    /// 전달할 자재 아이템의 종류.
    /// </summary>
    public ItemTypeEnum ItemType;

    public SupplyConstructionMaterialRequest(Entity targetSite, Entity itemEntity, ItemTypeEnum itemType)
    {
        TargetSite = targetSite;
        ItemEntity = itemEntity;
        ItemType = itemType;
    }
}

/// <summary>
/// [1. 역할]         : 진행 중인 공사 현장 취소 및 자재 반환 요청 (Command / Transient Request)
/// [2. Producer]     : 플레이어/UI 철거 및 취소 액션, 디버그 인터랙션, 테스트 러너
/// [3. Consumer]     : ConstructionLifecycleApplySystem (StateApplyGroup)
/// [4. Create Phase]: CommandGroup / ExecutionGroup / StateApplyGroup
/// [5. Consume Phase]: StateApplyGroup (Phase 5)
/// [6. 수명주기]     : 단일 프레임 소비 (Consume-on-Apply). EndStateApplyEntityCommandBufferSystem에 의해 처리 및 요청 엔티티 파괴
/// [7. 결과 정책]    : 유효한 현장일 경우 취소 플래그 마킹, 도착 자재 월드 아이템 방출, 현장 엔티티 파괴. 이미 파괴/완공된 대상일 경우 Strict Rejection / Idempotent Drop (요청만 소비)
/// [8. 안전망]       : 요청 처리 후 즉시 파괴되어 고아 요청 누수 방지
/// </summary>
public struct CancelConstructionRequest : IComponentData, IRequestComponent
{
    /// <summary>
    /// 취소할 대상 공사 현장 엔티티.
    /// </summary>
    public Entity TargetSite;

    public CancelConstructionRequest(Entity targetSite)
    {
        TargetSite = targetSite;
    }
}

/// <summary>
/// [1. 역할]         : 완공된 건물 철거 및 내용물/자재 반환 요청 (Command / Transient Request)
/// [2. Producer]     : 플레이어/UI 철거 액션, 드론 철거 완료 (Phase 9), 테스트 러너
/// [3. Consumer]     : BuildingLifecycleApplySystem (StateApplyGroup)
/// [4. Create Phase]: CommandGroup / ExecutionGroup / StateApplyGroup
/// [5. Consume Phase]: StateApplyGroup (Phase 5)
/// [6. 수명주기]     : 단일 프레임 소비 (Consume-on-Apply). EndStateApplyEntityCommandBufferSystem에 의해 처리 및 요청 엔티티 파괴
/// [7. 결과 정책]    : 유효한 건물일 경우 내용물 방출, 건설 재료 환급 스폰, 건물 엔티티 파괴. 파괴 불가/무효/기철거/공사현장 대상일 경우 Strict Rejection / Idempotent Drop (요청만 소비)
/// [8. 안전망]       : 요청 처리 후 즉시 파괴되어 고아 요청 누수 방지
/// </summary>
public struct DemolishBuildingRequest : IComponentData, IRequestComponent
{
    /// <summary>
    /// 철거할 대상 완공 건물 엔티티.
    /// </summary>
    public Entity TargetBuilding;

    public DemolishBuildingRequest(Entity targetBuilding)
    {
        TargetBuilding = targetBuilding;
    }
}
