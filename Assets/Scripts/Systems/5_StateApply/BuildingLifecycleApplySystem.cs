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
        if (request.TargetType == BuildingTypeEnum.None || request.TargetType == BuildingTypeEnum.ConstructionSite)
        {
            // 무효하거나 공사 현장 타입은 완공 건물 스폰 대상이 아님
            ECB.DestroyEntity(requestEntity);
            return;
        }

        int2 baseFootprint = request.FootprintSize;
        float speed = (request.TargetType == BuildingTypeEnum.Belt) ? 2.0f : 1.0f;
        int storageCapacity = 20;

        if (HasConfig && ConfigEntity != Entity.Null && ConfigBufferLookup.HasBuffer(ConfigEntity))
        {
            var configBuffer = ConfigBufferLookup[ConfigEntity];
            if (BuildingConfigLookupUtility.TryGetConfig(configBuffer, request.TargetType, out var config))
            {
                speed = config.Speed;
                storageCapacity = config.StorageCapacity;
                if (baseFootprint.x <= 0 || baseFootprint.y <= 0)
                {
                    baseFootprint = config.Footprint;
                }
            }
        }

        if (baseFootprint.x <= 0 || baseFootprint.y <= 0)
        {
            baseFootprint = GetDefaultFootprint(request.TargetType);
        }

        BuildingFootprint footprint = new BuildingFootprint(baseFootprint);
        int2 effectiveSize = footprint.GetEffectiveSize(request.Direction);
        float3 spawnPosition = new float3(request.Position.x, request.Position.y, 0f);

        if (HasPrefabDb)
        {
            Entity prefabEntity = Entity.Null;
            int2 dbFootprint = int2.zero;

            if (PrefabDbEntity != Entity.Null && PrefabBufferLookup.HasBuffer(PrefabDbEntity))
            {
                var buffer = PrefabBufferLookup[PrefabDbEntity];
                PrefabLookupUtility.TryGetBuildingPrefab(buffer, request.TargetType, out prefabEntity, out dbFootprint);
            }

            if (prefabEntity == Entity.Null)
            {
                // 프리팹 DB 활성화 환경에서 프리팹 미등록 시 Strict Fail: 스폰 중단 및 로깅
                FixedString128Bytes msg = default;
                msg.Append((FixedString128Bytes)"[BuildingLifecycleApplySystem] Missing prefab for building type '");
                msg.Append(request.TargetType.ToFixedString());
                msg.Append((FixedString128Bytes)"'. SpawnBuildingRequest rejected.");
                UnityEngine.Debug.LogError(msg);

                ECB.DestroyEntity(requestEntity);
                return;
            }

            // 프리팹 인스턴스화
            Entity newBuilding = ECB.Instantiate(prefabEntity);

            // 공통 컴포넌트 주입
            ECB.AddComponent(newBuilding, new BuildingType(request.TargetType));
            ECB.AddComponent(newBuilding, new BuildingFootprint(effectiveSize));
            ECB.AddComponent(newBuilding, new GridPosition(request.Position));
            ECB.AddComponent(newBuilding, new Direction(request.Direction));
            ECB.AddComponent(newBuilding, request.Stamp);
            ECB.SetComponent(newBuilding, LocalTransform.FromPosition(spawnPosition));

            // 타입별 필수 컴포넌트 및 버퍼 주입
            AttachTypeSpecificComponents(ref ECB, newBuilding, request.TargetType, request.Direction, speed, storageCapacity);
        }
        else
        {
            // 프리팹 DB가 없는 순수 시뮬레이션 환경 (테스트 등): Fallback 아키타입으로 엔티티 생성
            Entity newBuilding = ECB.CreateEntity(FallbackBuildingArchetype);

            ECB.SetComponent(newBuilding, new BuildingType(request.TargetType));
            ECB.SetComponent(newBuilding, new BuildingFootprint(effectiveSize));
            ECB.SetComponent(newBuilding, new GridPosition(request.Position));
            ECB.SetComponent(newBuilding, new Direction(request.Direction));
            ECB.SetComponent(newBuilding, request.Stamp);
            ECB.SetComponent(newBuilding, LocalTransform.FromPosition(spawnPosition));

            AttachTypeSpecificComponents(ref ECB, newBuilding, request.TargetType, request.Direction, speed, storageCapacity);
        }

        // 요청 엔티티 소비 (Consume-on-Apply)
        ECB.DestroyEntity(requestEntity);
    }

    private static void AttachTypeSpecificComponents(
        ref EntityCommandBuffer ecb,
        Entity building,
        BuildingTypeEnum type,
        DirectionEnum direction,
        float speed,
        int storageCapacity)
    {
        switch (type)
        {
            case BuildingTypeEnum.Belt:
                ecb.AddComponent(building, new BeltComponent(speed > 0f ? speed : 2.0f));
                break;

            case BuildingTypeEnum.Miner:
                ecb.AddComponent(building, new MinerState(speed > 0f ? speed : 1.0f));
                ecb.AddComponent<MinerDecision>(building);
                ecb.SetComponentEnabled<MinerDecision>(building, false);
                ecb.AddBuffer<ProductItemElement>(building);
                ecb.AddBuffer<ProductResult>(building);
                ecb.AddComponent<BuildingItemOutputDecision>(building);
                ecb.SetComponentEnabled<BuildingItemOutputDecision>(building, false);
                break;

            case BuildingTypeEnum.Crafter:
                ecb.AddComponent(building, new CrafterState(selectedRecipeId: 0, speed: speed > 0f ? speed : 1.0f));
                ecb.AddComponent<CrafterDecision>(building);
                ecb.SetComponentEnabled<CrafterDecision>(building, false);
                ecb.AddBuffer<StoredItemElement>(building);
                ecb.AddBuffer<ProductItemElement>(building);
                ecb.AddBuffer<ProductResult>(building);
                ecb.AddComponent<BuildingItemOutputDecision>(building);
                ecb.SetComponentEnabled<BuildingItemOutputDecision>(building, false);
                break;

            case BuildingTypeEnum.Storage:
                ecb.AddComponent(building, new Storage(storageCapacity > 0 ? storageCapacity : 20));
                ecb.AddBuffer<StoredItemElement>(building);
                ecb.AddComponent(building, new StorageFilter());
                ecb.AddComponent<BuildingItemOutputDecision>(building);
                ecb.SetComponentEnabled<BuildingItemOutputDecision>(building, false);
                break;

            case BuildingTypeEnum.Splitter:
                ecb.AddComponent(building, new SplitterRoutingState(Entity.Null, direction, 0));
                ecb.AddComponent<RoutingTransferDecision>(building);
                ecb.SetComponentEnabled<RoutingTransferDecision>(building, false);
                break;

            case BuildingTypeEnum.Merger:
                ecb.AddComponent(building, new MergerRoutingState(Entity.Null, direction, 0));
                ecb.AddComponent<RoutingTransferDecision>(building);
                ecb.SetComponentEnabled<RoutingTransferDecision>(building, false);
                break;

            case BuildingTypeEnum.MainFacility:
                // 메인 기지는 기본 저장 공간을 제공
                if (storageCapacity > 0)
                {
                    ecb.AddComponent(building, new Storage(storageCapacity));
                    ecb.AddBuffer<StoredItemElement>(building);
                    ecb.AddComponent(building, new StorageFilter());
                    ecb.AddComponent<BuildingItemOutputDecision>(building);
                    ecb.SetComponentEnabled<BuildingItemOutputDecision>(building, false);
                }
                break;

            case BuildingTypeEnum.PowerPole:
            case BuildingTypeEnum.CoalGenerator:
            case BuildingTypeEnum.DroneStation:
            case BuildingTypeEnum.ResearchBuilding:
                // 전력/드론/연구 도메인 컴포넌트는 해당 Phase(Phase 8, 9, 10)에서 확장
                break;
        }
    }

    private static int2 GetDefaultFootprint(BuildingTypeEnum type)
    {
        switch (type)
        {
            case BuildingTypeEnum.Belt:
            case BuildingTypeEnum.Splitter:
            case BuildingTypeEnum.Merger:
            case BuildingTypeEnum.PowerPole:
            case BuildingTypeEnum.Storage:
                return new int2(1, 1);

            case BuildingTypeEnum.Miner:
            case BuildingTypeEnum.Crafter:
                return new int2(2, 2);

            case BuildingTypeEnum.CoalGenerator:
            case BuildingTypeEnum.DroneStation:
            case BuildingTypeEnum.ResearchBuilding:
            case BuildingTypeEnum.MainFacility:
                return new int2(3, 3);

            default:
                return new int2(1, 1);
        }
    }
}
