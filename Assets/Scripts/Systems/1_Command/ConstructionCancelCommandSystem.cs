using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>
/// 역할·목적: 완공 전 현장을 취소하고 도착해 보관 중인 자재를 기존 실물 그대로 월드에 반환한다.
/// 처리 단계: Command. 입력은 CancelConstructionRequest와 대상 ConstructionSite/보관 실물/위치/활성 Destroy 상태다.
/// 출력·소유권: Cancelled를 즉시 표시하고 실물 Owner·위치·렌더 변경과 현장/요청 삭제를 EndCommand에 기록한다.
/// 드론 작업/예약 정리는 이후 Decision→Reservation→StateApply의 기존 관리 소유자가 처리한다.
/// 정리·가시화: EndCommand 이후 현장은 공급/완공 대상에서 사라진다. 공간 인덱스는 Synchronization에 갱신하므로 같은 틱 재배치는 기존 점유로 거부한다.
/// </summary>
[UpdateInGroup(typeof(CommandGroup))]
public partial struct ConstructionCancelCommandSystem : ISystem
{
    private EntityQuery _requestQuery;
    private ComponentLookup<ConstructionSite> _siteLookup;
    private BufferLookup<StoredItemElement> _storedBufferLookup;
    private ComponentLookup<DestroyItemRequest> _destroyRequestLookup;
    private ComponentLookup<GridPosition> _gridPosLookup;

    public void OnCreate(ref SystemState state)
    {
        _requestQuery = SystemAPI.QueryBuilder()
            .WithAll<CancelConstructionRequest>()
            .Build();

        _siteLookup = state.GetComponentLookup<ConstructionSite>(false);
        _storedBufferLookup = state.GetBufferLookup<StoredItemElement>(true);
        _destroyRequestLookup = state.GetComponentLookup<DestroyItemRequest>(true);
        _gridPosLookup = state.GetComponentLookup<GridPosition>(true);
    }

    public void OnUpdate(ref SystemState state)
    {
        if (_requestQuery.IsEmptyIgnoreFilter)
        {
            return;
        }

        var ecbSystem = state.World.GetOrCreateSystemManaged<EndCommandEntityCommandBufferSystem>();
        var ecb = ecbSystem.CreateCommandBuffer();

        _siteLookup.Update(ref state);
        _storedBufferLookup.Update(ref state);
        _destroyRequestLookup.Update(ref state);
        _gridPosLookup.Update(ref state);

        var job = new CancelConstructionCommandJob
        {
            ECB = ecb,
            SiteLookup = _siteLookup,
            StoredBufferLookup = _storedBufferLookup,
            DestroyRequestLookup = _destroyRequestLookup,
            GridPosLookup = _gridPosLookup
        };
        state.Dependency = job.Schedule(_requestQuery, state.Dependency);
        ecbSystem.AddJobHandleForProducer(state.Dependency);
    }
}

/// <summary>
/// CancelConstructionRequest를 소비하여 현장을 취소하고 자재를 반환하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct CancelConstructionCommandJob : IJobEntity
{
    public EntityCommandBuffer ECB;
    [ReadOnly] public ComponentLookup<DestroyItemRequest> DestroyRequestLookup;

    public ComponentLookup<ConstructionSite> SiteLookup;

    [ReadOnly]
    public ComponentLookup<GridPosition> GridPosLookup;

    [ReadOnly]
    public BufferLookup<StoredItemElement> StoredBufferLookup;

    public void Execute(Entity requestEntity, in CancelConstructionRequest request)
    {
        // 1. 대상 공사 현장 엔티티 검증 (무효, 기파괴, 완공 건물 등)
        if (request.TargetSite == Entity.Null)
        {
            // 대상이 없거나 이미 완공된 건물이거나 파괴된 경우 요청만 안전하게 소비 (Idempotent Drop)
            ECB.DestroyEntity(requestEntity);
            return;
        }

        if (!SiteLookup.HasComponent(request.TargetSite))
        {
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

        // 2. 취소 플래그 즉시 마킹 (동일 프레임 중복 요청의 추가 반환 차단)
        site.Flags |= ConstructionSiteFlags.Cancelled;
        SiteLookup[request.TargetSite] = site;

        // 3. 현장 위치 확인
        int2 sitePos = int2.zero;
        if (GridPosLookup.HasComponent(request.TargetSite))
        {
            sitePos = GridPosLookup[request.TargetSite].Value;
        }
        float3 worldPos = new float3(sitePos.x, sitePos.y, 0f);

        // 현장에 도착한 실물만 반환한다. 미도착 요구량이나 다른 드론의 적재품은 여기서 새로 생성/이전하지 않는다.
        // Owner·좌표·렌더를 같은 ECB에 기록해 현장 삭제와 반환이 EndCommand에서 함께 확정되게 한다.
        if (StoredBufferLookup.HasBuffer(request.TargetSite))
        {
            var storedItems = StoredBufferLookup[request.TargetSite];
            for (int i = 0; i < storedItems.Length; i++)
            {
                Entity item = storedItems[i].ItemEntity;
                if (item == Entity.Null)
                {
                    continue;
                }

                if (DestroyRequestLookup.HasComponent(item) && DestroyRequestLookup.IsComponentEnabled(item))
                {
                    continue;
                }

                ECB.SetComponent(item, ItemOwnership.WorldItem);
                ECB.RemoveComponent<DisableRendering>(item);
                ECB.SetComponent(item, new GridPosition(sitePos));
                ECB.SetComponent(item, LocalTransform.FromPosition(worldPos));
            }
        }

        // 5. 공사 현장 엔티티 파괴 (점유 인덱스는 Synchronization에서 갱신)
        ECB.DestroyEntity(request.TargetSite);

        // 6. 요청 엔티티 소비 (Consume-on-Apply)
        ECB.DestroyEntity(requestEntity);
    }
}
