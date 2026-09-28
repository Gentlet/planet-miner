using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>
/// 공사 현장 취소 및 도착 자재 반환 전담 시스템.
/// 
/// [책임]
/// - StateApplyGroup(Phase 5)에서 실행:
///   [UpdateBefore(typeof(ConstructionMaterialApplySystem))]
///   [UpdateBefore(typeof(ConstructionCompletionApplySystem))]
/// - CancelConstructionRequest를 소비하여 공사 현장을 즉시 취소하고 보관 자재를 월드로 반환.
/// - 취소 대상 현장에 Cancelled 플래그를 마킹하여 동일 프레임 뒤따르는 ConstructionCompletionApplySystem의 완공 전환을 원천 차단 (취소 우선).
/// - 현장에 보관된 건설 자재(StoredItemElement)를 WorldItem으로 소유권 전환, DisableRendering 제거, 현장 좌표로 위치 설정.
/// - 공사 현장 엔티티(TargetSite)를 파괴(DestroyEntity).
/// - 무효한 현장, 이미 파괴된 현장, 이미 완공된 건물이 대상일 경우 안전하게 무시(Idempotent Drop).
/// - 요청 엔티티는 단일 프레임 내에 파괴 (Consume-on-Apply).
/// </summary>
[UpdateInGroup(typeof(StateApplyGroup))]
[UpdateBefore(typeof(ConstructionMaterialApplySystem))]
[UpdateBefore(typeof(ConstructionCompletionApplySystem))]
public partial struct ConstructionCancelApplySystem : ISystem
{
    private EntityQuery _requestQuery;
    private ComponentLookup<ConstructionSite> _siteLookup;
    private ComponentLookup<GridPosition> _gridPosLookup;
    private BufferLookup<StoredItemElement> _storedBufferLookup;

    public void OnCreate(ref SystemState state)
    {
        _requestQuery = SystemAPI.QueryBuilder()
            .WithAll<CancelConstructionRequest>()
            .Build();

        _siteLookup = state.GetComponentLookup<ConstructionSite>(false);
        _gridPosLookup = state.GetComponentLookup<GridPosition>(true);
        _storedBufferLookup = state.GetBufferLookup<StoredItemElement>(true);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        if (_requestQuery.IsEmptyIgnoreFilter)
        {
            return;
        }

        var ecbSystem = state.World.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>();
        var ecb = ecbSystem.CreateCommandBuffer();

        _siteLookup.Update(ref state);
        _gridPosLookup.Update(ref state);
        _storedBufferLookup.Update(ref state);

        var job = new CancelConstructionApplyJob
        {
            ECB = ecb,
            SiteLookup = _siteLookup,
            GridPosLookup = _gridPosLookup,
            StoredBufferLookup = _storedBufferLookup
        };

        var handle = job.Schedule(_requestQuery, state.Dependency);
        ecbSystem.AddJobHandleForProducer(handle);
        state.Dependency = handle;
    }
}

/// <summary>
/// CancelConstructionRequest를 소비하여 현장을 취소하고 자재를 반환하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct CancelConstructionApplyJob : IJobEntity
{
    public EntityCommandBuffer ECB;

    public ComponentLookup<ConstructionSite> SiteLookup;

    [ReadOnly]
    public ComponentLookup<GridPosition> GridPosLookup;

    [ReadOnly]
    public BufferLookup<StoredItemElement> StoredBufferLookup;

    public void Execute(Entity requestEntity, in CancelConstructionRequest request)
    {
        // 1. 대상 공사 현장 엔티티 검증 (무효, 기파괴, 완공 건물 등)
        if (request.TargetSite == Entity.Null || !SiteLookup.HasComponent(request.TargetSite))
        {
            // 대상이 없거나 이미 완공된 건물이거나 파괴된 경우 요청만 안전하게 소비 (Idempotent Drop)
            ECB.DestroyEntity(requestEntity);
            return;
        }

        var site = SiteLookup[request.TargetSite];
        if ((site.Flags & ConstructionSiteFlags.Cancelled) != 0)
        {
            // 이미 동일 프레임 앞선 요청에 의해 취소 처리됨 (중복 취소 방지)
            ECB.DestroyEntity(requestEntity);
            return;
        }

        // 2. 취소 플래그 즉시 마킹 (동일 프레임 ConstructionCompletionApplySystem의 완공 전환 차단)
        site.Flags |= ConstructionSiteFlags.Cancelled;
        SiteLookup[request.TargetSite] = site;

        // 3. 현장 위치 확인
        int2 sitePos = int2.zero;
        if (GridPosLookup.HasComponent(request.TargetSite))
        {
            sitePos = GridPosLookup[request.TargetSite].Value;
        }
        float3 worldPos = new float3(sitePos.x, sitePos.y, 0f);

        // 4. 도착 자재 전수 반환 (WorldItem 전환, DisableRendering 제거, 위치 주입)
        if (StoredBufferLookup.HasBuffer(request.TargetSite))
        {
            var storedItems = StoredBufferLookup[request.TargetSite];
            for (int i = 0; i < storedItems.Length; i++)
            {
                Entity item = storedItems[i].ItemEntity;
                if (item != Entity.Null)
                {
                    ECB.SetComponent(item, ItemOwnership.WorldItem);
                    ECB.RemoveComponent<DisableRendering>(item);
                    ECB.SetComponent(item, new GridPosition(sitePos));
                    ECB.SetComponent(item, LocalTransform.FromPosition(worldPos));
                }
            }
        }

        // 5. 공사 현장 엔티티 파괴 (동일 틱 Phase 6에서 공간 점유 자동 해제)
        ECB.DestroyEntity(request.TargetSite);

        // 6. 요청 엔티티 소비 (Consume-on-Apply)
        ECB.DestroyEntity(requestEntity);
    }
}
