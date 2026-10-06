using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// 역할·목적: 외부 배치 요청을 검증해 현장과 자재 요구량을 만들고 기존 벨트의 방향 변경을 처리한다.
/// 처리 단계: Command. 입력은 BuildingPlacementRequest/후보 버퍼, 건물 설정과 직전 Synchronization의 공간 인덱스다.
/// 출력·소유권: 현장 생성, PlacementStamp와 요구/보관 버퍼 초기화, 벨트 Direction 변경만 EndCommand에 기록한다.
/// StrictAllOrNothing은 요청 묶음 전체를 거부하고 부분 배치 정책은 승인 후보만 기록한다. 드론 작업/순번 생성은 담당하지 않는다.
/// 정리·가시화: 처리한 요청/후보는 EndCommand에서 삭제하고 생성 현장도 이때 실체화한다. 공간 점유 등록은 Synchronization까지 기다린다.
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

        // 설정 버퍼를 임시 배열로 복사해 검증과 ECB 기록 동안 같은 입력을 사용한다.
        // 여기서 해금/자재 요구량만 읽으며 설정이나 드론 관리 상태를 변경하지 않는다.
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

            // 직전 동기화의 점유와 이 요청 묶음 안의 후보 충돌을 검사한다.
            // 같은 틱의 취소/철거가 기록됐더라도 인덱스가 갱신되기 전 점유는 그대로 사용한다.
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

            // 후보의 명시 Tick을 우선하되 버퍼 순서를 유지한다. 이 Stamp가 이후 최초 공급 우선순위의 근거가 된다.
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
                            // 기존 같은 타입 벨트는 새 현장/요구 자재 없이 EndCommand에서 방향만 바꾼다.
                            ecb.SetComponent(existingBuilding.Entity, new Direction(candidate.Direction));
                            continue;
                        }
                    }

                    // 같은 타입 벨트의 방향 변경으로 처리하지 못한 승인 후보는 현장 생성 경로를 따른다.
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

                    ecb.AddComponent(siteEntity, new ConstructionSite(candidate.TargetType, flags));
                    ecb.AddComponent(siteEntity, new PlacementStamp(candidateTick, (uint)i));
                    ecb.AddComponent(siteEntity, LocalTransform.FromPosition(candidate.OriginPosition.x, candidate.OriginPosition.y, 0f));

                    // 요구 수량과 실물 참조를 분리한다. 현장은 요구량만으로 자재를 소유하지 않으며
                    // 드론 공급의 실제 성공분이 StoredItemElement/Owner와 DeliveredQuantity를 함께 채운다.
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
