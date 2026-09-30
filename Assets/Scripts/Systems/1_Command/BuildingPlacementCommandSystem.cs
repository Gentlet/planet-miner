using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// 건설 모드 UI 또는 블루프린트에서 인큐된 BuildingPlacementRequest를 원자적으로 소비하고
/// 검증을 거쳐 ConstructionSite 엔티티를 생성하는 Command 시스템.
/// 
/// [책임 및 계약]
/// - CommandGroup(Phase 1)에서 실행되어 배치 요청을 일괄 처리.
/// - BuildingConfig(해금 상태, 자재 요구량) 및 공간 인덱스를 참조하여 배치 타당성 검증.
/// - StrictAllOrNothing(기본) 정책 시 단 1개 타일이라도 충돌 시 전체 요청 롤백.
/// - 벨트 덮어쓰기: 동일 벨트는 즉시 Direction만 갱신(자재 소모 0), 다른 벨트는 ConstructionSite 생성.
/// - 승인된 후보에는 PlacementStamp(Tick, Order) 발급 및 ConstructionMaterialRequirementElement 버퍼 부착.
/// - 처리가 완료된 요청 엔티티는 해당 프레임에 즉시 파괴(Consume-on-Apply).
/// </summary>
[UpdateInGroup(typeof(CommandGroup))]
public partial struct BuildingPlacementCommandSystem : ISystem
{
    private ulong _currentTick;

    public void OnCreate(ref SystemState state)
    {
        _currentTick = 1;
        state.RequireForUpdate<BuildingSpatialIndexFence>();
        state.RequireForUpdate<ResourceSpatialIndexFence>();
        state.RequireForUpdate<ItemSpatialIndexFence>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<BuildingSpatialIndex>() ||
            !SystemAPI.HasSingleton<ResourceSpatialIndex>() ||
            !SystemAPI.HasSingleton<ItemSpatialIndex>())
        {
            return;
        }

        // 직접 읽기가 끝날 때까지 새 Job을 발행하지 않는다. 선행 Writer만 기다리고
        // 다른 Reader의 완료나 Validator의 부수 효과에는 의존하지 않는다.
        JobHandle.CombineDependencies(
            SystemAPI.GetSingleton<BuildingSpatialIndexFence>().GetReaderDependency(),
            SystemAPI.GetSingleton<ResourceSpatialIndexFence>().GetReaderDependency(),
            SystemAPI.GetSingleton<ItemSpatialIndexFence>().GetReaderDependency()).Complete();

        var buildingMap = SystemAPI.GetSingleton<BuildingSpatialIndex>().Map;
        var resourceMap = SystemAPI.GetSingleton<ResourceSpatialIndex>().Map;
        var itemMap = SystemAPI.GetSingleton<ItemSpatialIndex>().Map;

        if (!buildingMap.IsCreated || !resourceMap.IsCreated || !itemMap.IsCreated)
        {
            return;
        }

        var ecbSystem = state.World.GetOrCreateSystemManaged<EndCommandEntityCommandBufferSystem>();
        var ecb = ecbSystem.CreateCommandBuffer();

        // BuildingConfig 조회 (존재 시 해금 상태 및 자재 요구량 사용)
        bool hasConfig = SystemAPI.TryGetSingletonEntity<BuildingConfig>(out var configEntity);
        NativeArray<BuildingConfigElement> configs = default;
        NativeArray<BuildingConstructionMaterialElement> materials = default;

        if (hasConfig)
        {
            var rawConfigs = state.EntityManager.GetBuffer<BuildingConfigElement>(configEntity);
            configs = rawConfigs.ToNativeArray(Allocator.Temp);

            var rawMaterials = state.EntityManager.GetBuffer<BuildingConstructionMaterialElement>(configEntity);
            materials = rawMaterials.ToNativeArray(Allocator.Temp);
        }

        // 모든 BuildingPlacementRequest 쿼리 및 처리
        foreach (var (request, candidateBuffer, entity) in
                 SystemAPI.Query<RefRO<BuildingPlacementRequest>, DynamicBuffer<PlacementRequestCandidateElement>>()
                     .WithEntityAccess())
        {
            if (candidateBuffer.IsEmpty)
            {
                ecb.DestroyEntity(entity);
                continue;
            }

            int count = candidateBuffer.Length;
            var candidates = new NativeArray<PlacementCandidate>(count, Allocator.Temp);
            var results = new NativeArray<PlacementValidationResult>(count, Allocator.Temp);

            for (int i = 0; i < count; i++)
            {
                candidates[i] = candidateBuffer[i];
            }

            // 배치 타당성 및 동일 프레임 선점 충돌 검증
            if (hasConfig)
            {
                BuildingPlacementValidationUtility.ValidateBatchPlacement(
                    candidates,
                    request.ValueRO.Flags,
                    buildingMap.AsReadOnly(),
                    resourceMap.AsReadOnly(),
                    itemMap.AsReadOnly(),
                    results,
                    configs);
            }
            else
            {
                BuildingPlacementValidationUtility.ValidateBatchPlacement(
                    candidates,
                    request.ValueRO.Flags,
                    buildingMap.AsReadOnly(),
                    resourceMap.AsReadOnly(),
                    itemMap.AsReadOnly(),
                    results);
            }

            ulong defaultTick = request.ValueRO.RequestTick > 0 ? request.ValueRO.RequestTick : _currentTick;

            for (int i = 0; i < count; i++)
            {
                var candidate = candidates[i];
                var res = results[i];
                ulong candidateTick = candidateBuffer[i].RequestTick > 0 ? candidateBuffer[i].RequestTick : defaultTick;

                if (res.Code == PlacementValidationCode.BeltUpgradeAllowed)
                {
                    // 벨트 덮어쓰기 처리: 동일 벨트인지 다른 벨트인지 판정
                    if (buildingMap.TryGetValue(candidate.OriginPosition, out var existingBuilding))
                    {
                        if (existingBuilding.Type == candidate.TargetType)
                        {
                            // 동일 벨트: 자재 소모 없이 즉시 Direction만 갱신 (옵션 2)
                            ecb.SetComponent(existingBuilding.Entity, new Direction(candidate.Direction));
                            continue;
                        }
                    }

                    // 다른 벨트인 경우 신규 공사 현장 생성으로 진행 (옵션 1)
                }

                if (res.IsValid)
                {
                    // 공사 현장 엔티티 생성
                    var siteEntity = ecb.CreateEntity();
                    ecb.AddComponent(siteEntity, new BuildingType(BuildingTypeEnum.ConstructionSite));

                    // Size에는 방향 적용 전 크기를 저장한다. 점유 조회 시에만 회전한다.
                    int2 baseSize = math.max(candidate.FootprintSize, new int2(1, 1));
                    ecb.AddComponent(siteEntity, new BuildingFootprint(baseSize));
                    ecb.AddComponent(siteEntity, new GridPosition(candidate.OriginPosition));
                    ecb.AddComponent(siteEntity, new Direction(candidate.Direction));

                    var flags = ConstructionSiteFlags.None;
                    if (res.HasGroundItems)
                    {
                        flags |= ConstructionSiteFlags.AwaitingItemClearance;
                    }

                    ecb.AddComponent(siteEntity, new ConstructionSite(candidate.TargetType, 0.0f, flags));
                    ecb.AddComponent(siteEntity, new PlacementStamp(candidateTick, (uint)i));
                    ecb.AddComponent(siteEntity, LocalTransform.FromPosition(candidate.OriginPosition.x, candidate.OriginPosition.y, 0f));

                    // 자재 요구량 버퍼 및 자재 수납 버퍼 부착
                    var reqBuffer = ecb.AddBuffer<ConstructionMaterialRequirementElement>(siteEntity);
                    ecb.AddBuffer<StoredItemElement>(siteEntity);

                    if (hasConfig && materials.IsCreated)
                    {
                        BuildingConfigLookupUtility.PopulateRequirements(materials, candidate.TargetType, ref reqBuffer);
                    }
                }
            }

            candidates.Dispose();
            results.Dispose();

            // 요청 엔티티 소비 (Consume-on-Apply)
            ecb.DestroyEntity(entity);
        }

        if (hasConfig)
        {
            if (configs.IsCreated) configs.Dispose();
            if (materials.IsCreated) materials.Dispose();
        }

        _currentTick++;
    }
}
