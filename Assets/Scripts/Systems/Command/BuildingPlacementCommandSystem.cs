using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// 역할·목적: 외부 배치 요청을 검증해 현장과 자재 요구량을 만들고 기존 벨트의 방향 변경을 처리한다.
/// 처리 단계: Command. 입력은 BuildingPlacementRequest/후보 버퍼, 건물 설정과 직전 Synchronization의 공간 인덱스다.
/// 출력·소유권: 현장 생성, PlacementStamp와 요구/보관 버퍼 초기화, 벨트 방향과 표시 회전을 EndCommand에 기록한다.
/// StrictAllOrNothing은 요청 묶음 전체를 거부하고 부분 배치 정책은 승인 후보만 기록한다. 드론 작업/순번 생성은 담당하지 않는다.
/// 요청 간 중재: Submit의 ReceiptSequence 순서로 처리하고 최종 승인 후보의 셀만 임시 공유한다. 기존 벨트 방향 변경도 첫 승인 우선이다.
/// 정리·가시화: 처리한 요청/후보는 EndCommand에서 삭제하고 생성 현장도 이때 실체화한다. 공간 점유 등록은 Synchronization까지 기다린다.
/// </summary>
[UpdateInGroup(typeof(CommandGroup))]
public partial struct BuildingPlacementCommandSystem : ISystem
{
    private EntityQuery _requests;

    private struct PendingPlacement
    {
        public Entity Entity;
        public BuildingPlacementRequest Request;
    }

    private struct ReceiptComparer : IComparer<PendingPlacement>
    {
        public int Compare(PendingPlacement first, PendingPlacement second)
        {
            return first.Request.ReceiptSequence.CompareTo(second.Request.ReceiptSequence);
        }
    }

    public void OnCreate(ref SystemState state)
    {
        _requests = state.GetEntityQuery(ComponentType.ReadOnly<BuildingPlacementRequest>());
        WorldSequenceUtility.GetOrCreate(state.EntityManager, new BuildingPlacementReceiptSequence { NextValue = 1, CurrentTick = 1 });
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

        var buildingMap = SystemAPI.GetSingleton<BuildingSpatialIndex>().Map;
        var resourceMap = SystemAPI.GetSingleton<ResourceSpatialIndex>().Map;
        var itemMap = SystemAPI.GetSingleton<ItemSpatialIndex>().Map;

        if (!buildingMap.IsCreated || !resourceMap.IsCreated || !itemMap.IsCreated)
        {
            return;
        }

        var ecbSystem = state.World.GetOrCreateSystemManaged<EndCommandEntityCommandBufferSystem>();

        bool hasConfig = SystemAPI.TryGetSingletonEntity<BuildingConfig>(out var configEntity);
        if (!hasConfig || !state.EntityManager.HasBuffer<BuildingConfigElement>(configEntity) ||
            !state.EntityManager.HasBuffer<BuildingConstructionMaterialElement>(configEntity))
        {
            var rejection = ecbSystem.CreateCommandBuffer();
            SimulationFailureUtility.Record(ref rejection, "[BuildingPlacementCommandSystem] Missing building configuration. Placement rejected.");
            foreach (var (_, entity) in SystemAPI.Query<RefRO<BuildingPlacementRequest>>().WithEntityAccess())
            {
                rejection.DestroyEntity(entity);
            }
            return;
        }

        if (_requests.IsEmptyIgnoreFilter)
        {
            BuildingPlacementRequestUtility.AdvanceTick(state.EntityManager);
            return;
        }

        // 직접 읽기가 끝날 때까지 새 Job을 발행하지 않는다. 선행 Writer만 기다리고
        // 다른 Reader의 완료나 Validator의 부수 효과에는 의존하지 않는다.
        JobHandle.CombineDependencies(
            SystemAPI.GetSingleton<BuildingSpatialIndexFence>().State.GetReaderDependency(),
            SystemAPI.GetSingleton<ResourceSpatialIndexFence>().State.GetReaderDependency(),
            SystemAPI.GetSingleton<ItemSpatialIndexFence>().State.GetReaderDependency()).Complete();

        // 요청 묶음의 검증과 기록 동안 같은 크기·해금·자재 정의를 사용한다.
        var ecb = ecbSystem.CreateCommandBuffer();
        NativeArray<BuildingConfigElement> configs = default;
        NativeArray<BuildingConstructionMaterialElement> materials = default;

        var rawConfigs = state.EntityManager.GetBuffer<BuildingConfigElement>(configEntity);
        configs = rawConfigs.ToNativeArray(Allocator.Temp);

        var rawMaterials = state.EntityManager.GetBuffer<BuildingConstructionMaterialElement>(configEntity);
        materials = rawMaterials.ToNativeArray(Allocator.Temp);
        SystemAPI.TryGetSingletonBuffer<BuildingPrefabElement>(out var buildingPrefabs, true);

        // 쿼리/Entity 순서는 접수 순서가 아니다. 후보 버퍼는 구조 변경 없이 처리 직전에 다시 얻는다.
        using var orderedRequests = new NativeList<PendingPlacement>(Allocator.Temp);
        foreach (var (request, candidateBuffer, entity) in
                 SystemAPI.Query<RefRO<BuildingPlacementRequest>, DynamicBuffer<PlacementRequestCandidateElement>>()
                     .WithEntityAccess())
        {
            orderedRequests.Add(new PendingPlacement { Entity = entity, Request = request.ValueRO });
        }

        orderedRequests.AsArray().Sort(new ReceiptComparer());
        // 묶음 내부의 임시 선점과 구분한다. 최종 승인 뒤에만 쓰므로 Strict 실패가 후속 요청을 막지 않는다.
        using var approvedCells = new NativeParallelHashSet<int2>(16, Allocator.Temp);
        var settingsRead = new BuildingSettingsUtility.SettingsReadContext(state.EntityManager);
        ulong currentTick = 0;
        bool currentTickRead = false;
        for (int requestIndex = 0; requestIndex < orderedRequests.Length; requestIndex++)
        {
            var pending = orderedRequests[requestIndex];
            Entity entity = pending.Entity;
            var request = pending.Request;
            if (request.ReceiptSequence == 0 || HasDuplicateReceipt(orderedRequests.AsArray(), requestIndex))
            {
                // 접수 계약을 우회한 입력에는 Query/Entity 기반 임의 우선순위를 부여하지 않는다.
                Debug.LogError("Building placement request rejected: missing or duplicate receipt sequence. Use BuildingPlacementRequestUtility.Submit.");
                ecb.DestroyEntity(entity);
                continue;
            }

            var candidateBuffer = state.EntityManager.GetBuffer<PlacementRequestCandidateElement>(entity, true);
            if (candidateBuffer.IsEmpty)
            {
                ecb.DestroyEntity(entity);
                continue;
            }

            int count = candidateBuffer.Length;
            var candidates = new NativeArray<PlacementCandidate>(count, Allocator.Temp);
            var results = new NativeArray<PlacementValidationResult>(count, Allocator.Temp);
            var settingsRejections = new NativeArray<PlacementValidationCode>(count, Allocator.Temp);

            for (int i = 0; i < count; i++)
            {
                var candidate = candidateBuffer[i];
                candidates[i] = candidate;
                if (candidate.Settings.HasValues &&
                    BuildingConfigLookupUtility.TryGetConfig(configs, candidate.BuildingId, out var definition) &&
                    !BuildingSettingsUtility.IsAllowed(definition.BuildingType, candidate.Settings, ref settingsRead))
                {
                    settingsRejections[i] = PlacementValidationCode.InvalidBuildingSettings;
                }
            }

            // 직전 동기화의 점유·앞 요청의 최종 승인·이 묶음 내부 후보 충돌을 검사한다.
            // 같은 틱의 취소/철거가 기록됐더라도 인덱스가 갱신되기 전 점유는 그대로 사용한다.
            BuildingPlacementValidationUtility.ValidateBatchPlacement(
                state.EntityManager,
                candidates,
                request.Flags,
                buildingMap.AsReadOnly(),
                resourceMap.AsReadOnly(),
                itemMap.AsReadOnly(),
                results,
                configs,
                approvedCells: approvedCells, settingsRejections: settingsRejections);

            // 후보의 명시 Tick을 우선하되 버퍼 순서를 유지한다. 이 Stamp가 이후 최초 공급 우선순위의 근거가 된다.
            ulong defaultTick = request.RequestTick;
            if (defaultTick == 0)
            {
                if (!currentTickRead)
                {
                    currentTick = SystemAPI.GetSingleton<BuildingPlacementReceiptSequence>().CurrentTick;
                    currentTickRead = true;
                }
                defaultTick = currentTick;
            }

            for (int i = 0; i < count; i++)
            {
                var candidate = candidates[i];
                var res = results[i];
                ulong candidateTick = candidateBuffer[i].RequestTick > 0 ? candidateBuffer[i].RequestTick : defaultTick;

                if (res.Code == PlacementValidationCode.BeltUpgradeAllowed)
                {
                    // 승인에 사용한 같은 인덱스의 원점 벨트를 EndCommand에서 변경한다.
                    var existingBelt = buildingMap[candidate.OriginPosition];
                    RecordBeltDirection(ref ecb, state.EntityManager, existingBelt.Entity, candidate, buildingPrefabs);
                    BuildingPlacementValidationUtility.ClaimApprovedPlacement(candidate, approvedCells);
                    continue;
                }

                if (res.IsValid)
                {
                    var flags = res.HasGroundItems ? ConstructionSiteFlags.AwaitingItemClearance : ConstructionSiteFlags.None;
                    var site = new ConstructionSite(candidate.TargetType, candidate.TargetTier, candidate.BuildingId, flags);
                    var stamp = new PlacementStamp(candidateTick, (uint)i, request.ReceiptSequence);
                    Entity siteEntity = BuildingLifecycleUtility.CreateConstructionSite(ref ecb, candidate.OriginPosition,
                        candidate.FootprintSize, candidate.Direction, site, stamp, candidateBuffer[i].Settings);

                    // 요구 수량과 실물 참조를 분리한다. 현장은 요구량만으로 자재를 소유하지 않으며
                    var reqBuffer = ecb.AddBuffer<ConstructionMaterialRequirementElement>(siteEntity);

                    BuildingConfigLookupUtility.PopulateRequirements(materials, candidate.BuildingId, ref reqBuffer);

                    BuildingPlacementValidationUtility.ClaimApprovedPlacement(candidate, approvedCells);
                }
            }

            candidates.Dispose();
            results.Dispose();
            settingsRejections.Dispose();

            // 요청 엔티티 소비 (Consume-on-Apply)
            ecb.DestroyEntity(entity);
        }

        if (configs.IsCreated)
        {
            configs.Dispose();
        }
        if (materials.IsCreated)
        {
            materials.Dispose();
        }

        BuildingPlacementRequestUtility.AdvanceTick(state.EntityManager);
    }

    private static void RecordBeltDirection(ref EntityCommandBuffer ecb, EntityManager manager, Entity belt,
        in PlacementCandidate candidate, in DynamicBuffer<BuildingPrefabElement> buildingPrefabs)
    {
        ecb.SetComponent(belt, new Direction(candidate.Direction));
        if (!manager.HasComponent<LocalTransform>(belt))
        {
            return;
        }

        // 프리팹에 자체 보정이 없을 때만 새 방향에 맞춰 단위 메시의 크기와 중심을 보정한다.
        bool fitFootprint = buildingPrefabs.IsCreated &&
            PrefabLookupUtility.TryGetBuildingPrefab(buildingPrefabs, candidate.BuildingId, out Entity prefab) &&
            !manager.HasComponent<PostTransformMatrix>(prefab);
        BuildingLifecycleUtility.RecordVisualTransform(ref ecb, belt, manager.GetComponentData<LocalTransform>(belt),
            candidate.FootprintSize, candidate.Direction, fitFootprint);
    }

    private static bool HasDuplicateReceipt(NativeArray<PendingPlacement> requests, int index)
    {
        ulong receipt = requests[index].Request.ReceiptSequence;
        if (index > 0 && requests[index - 1].Request.ReceiptSequence == receipt)
        {
            return true;
        }

        return index + 1 < requests.Length && requests[index + 1].Request.ReceiptSequence == receipt;
    }
}
