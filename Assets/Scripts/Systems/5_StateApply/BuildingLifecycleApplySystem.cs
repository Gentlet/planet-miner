using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// 완공 건물 엔티티의 탄생(생성) 및 컴포넌트 초기화 전담 시스템.
/// 
/// [책임]
/// - StateApplyGroup(Phase 5)에서 실행.
/// - SpawnBuildingRequest를 소비하여 새 건물 엔티티를 생성하고 공통 및 타입별 컴포넌트를 원자적으로 초기화.
/// - 프리팹 데이터베이스(BuildingPrefabElement)에 등록된 프리팹을 우선 인스턴스화.
/// - 프리팹 DB 활성화 환경에서 프리팹 누락 시 Strict Fail (스폰 거부, 에러 로깅, 고아 엔티티 방지).
/// - 프리팹 DB가 없는 순수 시뮬레이션 환경(단위 테스트)에서는 FallbackBuildingArchetype으로 안전 격리 생성.
/// - BuildingConfig(속도, 저장 용량 등)를 반영하여 타입별 필수 상태/의사결정/버퍼 컴포넌트 주입.
/// - 요청 엔티티는 단일 프레임 내에 파괴 (Consume-on-Apply).
/// </summary>
[UpdateInGroup(typeof(StateApplyGroup))]
public partial struct BuildingLifecycleApplySystem : ISystem
{
    private EntityArchetype _fallbackBuildingArchetype;
    private EntityQuery _spawnQuery;
    private EntityQuery _prefabDbQuery;
    private EntityQuery _buildingConfigQuery;
    private BufferLookup<BuildingPrefabElement> _prefabBufferLookup;
    private BufferLookup<BuildingConfigElement> _configBufferLookup;

    public void OnCreate(ref SystemState state)
    {
        // 프리팹 데이터베이스가 없는 순수 시뮬레이션 환경(테스트 등)에서 사용할 Fallback 아키타입
        _fallbackBuildingArchetype = state.EntityManager.CreateArchetype(
            ComponentType.ReadWrite<BuildingType>(),
            ComponentType.ReadWrite<BuildingFootprint>(),
            ComponentType.ReadWrite<GridPosition>(),
            ComponentType.ReadWrite<Direction>(),
            ComponentType.ReadWrite<PlacementStamp>(),
            ComponentType.ReadWrite<LocalTransform>()
        );

        _spawnQuery = SystemAPI.QueryBuilder()
            .WithAll<SpawnBuildingRequest>()
            .Build();

        _prefabDbQuery = SystemAPI.QueryBuilder()
            .WithAll<BuildingPrefabElement>()
            .Build();

        _buildingConfigQuery = SystemAPI.QueryBuilder()
            .WithAll<BuildingConfig, BuildingConfigElement>()
            .Build();

        _prefabBufferLookup = state.GetBufferLookup<BuildingPrefabElement>(true);
        _configBufferLookup = state.GetBufferLookup<BuildingConfigElement>(true);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        if (_spawnQuery.IsEmptyIgnoreFilter)
        {
            return;
        }

        var ecbSystem = state.World.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>();
        var ecb = ecbSystem.CreateCommandBuffer();

        _prefabBufferLookup.Update(ref state);
        _configBufferLookup.Update(ref state);

        bool hasPrefabDb = !_prefabDbQuery.IsEmptyIgnoreFilter;
        Entity prefabDbEntity = hasPrefabDb ? _prefabDbQuery.GetSingletonEntity() : Entity.Null;

        bool hasConfig = !_buildingConfigQuery.IsEmptyIgnoreFilter;
        Entity configEntity = hasConfig ? _buildingConfigQuery.GetSingletonEntity() : Entity.Null;

        var spawnJob = new SpawnBuildingApplyJob
        {
            ECB = ecb,
            HasPrefabDb = hasPrefabDb,
            PrefabDbEntity = prefabDbEntity,
            HasConfig = hasConfig,
            ConfigEntity = configEntity,
            FallbackBuildingArchetype = _fallbackBuildingArchetype,
            PrefabBufferLookup = _prefabBufferLookup,
            ConfigBufferLookup = _configBufferLookup
        };

        var handle = spawnJob.Schedule(_spawnQuery, state.Dependency);
        ecbSystem.AddJobHandleForProducer(handle);
        state.Dependency = handle;
    }
}

/// <summary>
/// SpawnBuildingRequest를 소비하여 완공 건물 엔티티를 생성하고 컴포넌트를 초기화하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct SpawnBuildingApplyJob : IJobEntity
{
    public EntityCommandBuffer ECB;
    public bool HasPrefabDb;
    public Entity PrefabDbEntity;
    public bool HasConfig;
    public Entity ConfigEntity;
    public EntityArchetype FallbackBuildingArchetype;

    [ReadOnly]
    public BufferLookup<BuildingPrefabElement> PrefabBufferLookup;

    [ReadOnly]
    public BufferLookup<BuildingConfigElement> ConfigBufferLookup;

    public void Execute(Entity requestEntity, in SpawnBuildingRequest request)
    {
        BuildingLifecycleUtility.SpawnBuilding(
            ref ECB,
            request.TargetType,
            request.Position,
            request.Direction,
            request.FootprintSize,
            request.Stamp,
            HasPrefabDb,
            PrefabDbEntity,
            HasConfig,
            ConfigEntity,
            FallbackBuildingArchetype,
            PrefabBufferLookup,
            ConfigBufferLookup
        );

        // 요청 엔티티 소비 (Consume-on-Apply)
        ECB.DestroyEntity(requestEntity);
    }
}
