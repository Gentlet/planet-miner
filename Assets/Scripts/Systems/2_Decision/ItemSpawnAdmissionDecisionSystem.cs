using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

/// <summary>
/// 역할·목적: 철거 승인 건물의 새 입고와 현장 내부의 새 월드 생성이 실행 후보가 되는 것을 앞단에서 막는다.
/// 처리 단계: Decision. 입력은 활성 SpawnItemRequest와 현재 PendingBuildingDemolition/현장 footprint다.
/// 출력·소유권: 금지 요청을 즉시 비활성화한다. 기존 실물의 Owner나 현장 자재/예약 상태는 변경하지 않는다.
/// 정리·가시화: 거부 요청은 EndStateApply에 삭제하고 허용 요청은 ItemLifecycleApplySystem이 처리한다. World 위치는 Apply에서도 최종 재검사한다.
/// </summary>
[UpdateInGroup(typeof(DecisionGroup))]
public partial struct ItemSpawnAdmissionDecisionSystem : ISystem
{
    private EntityQuery _requestQuery;
    private ComponentLookup<PendingBuildingDemolition> _pendingLookup;

    public void OnCreate(ref SystemState state)
    {
        _requestQuery = SystemAPI.QueryBuilder()
            .WithAllRW<SpawnItemRequest>()
            .Build();
        _pendingLookup = state.GetComponentLookup<PendingBuildingDemolition>(true);
        state.RequireForUpdate(_requestQuery);
    }

    public void OnUpdate(ref SystemState state)
    {
        _pendingLookup.Update(ref state);
        var footprints = ConstructionSiteWorldItemUtility.CaptureFootprints(state.EntityManager, Allocator.TempJob);
        var ecbSystem = state.World.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>();
        var job = new ItemSpawnAdmissionDecisionJob
        {
            PendingLookup = _pendingLookup,
            ConstructionFootprints = footprints,
            ECB = ecbSystem.CreateCommandBuffer()
        };
        var admissionHandle = job.Schedule(_requestQuery, state.Dependency);
        state.Dependency = footprints.Dispose(admissionHandle);
        ecbSystem.AddJobHandleForProducer(state.Dependency);
    }
}

[BurstCompile]
public partial struct ItemSpawnAdmissionDecisionJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<PendingBuildingDemolition> PendingLookup;
    [ReadOnly] public NativeArray<ConstructionSiteWorldItemUtility.Footprint> ConstructionFootprints;
    public EntityCommandBuffer ECB;

    public void Execute(
        Entity requestEntity,
        RefRW<SpawnItemRequest> request,
        EnabledRefRW<SpawnItemRequest> enabled)
    {
        ItemSpawnDestination destination = request.ValueRO.Destination;
        if (destination == ItemSpawnDestination.World)
        {
            if (ConstructionSiteWorldItemUtility.IsWorldPositionAllowed(ConstructionFootprints, request.ValueRO.Position))
            {
                return;
            }

            // 현장 안의 새 실물을 만들지 않는 정책으로 완공 판단과 뒤늦은 World Spawn의 경합을 없앤다.
            enabled.ValueRW = false;
            ECB.DestroyEntity(requestEntity);
            return;
        }
        if (destination != ItemSpawnDestination.Storage && destination != ItemSpawnDestination.Product)
        {
            return;
        }
        Entity owner = request.ValueRO.TargetOwner;
        if (owner == Entity.Null)
        {
            return;
        }
        if (!PendingLookup.HasComponent(owner))
        {
            return;
        }
        // 삭제 ECB가 재생되기 전에도 비활성화 상태로 생성 후보를 차단한다.
        // 실제 아이템 생성 단계가 철거 요청을 다시 해석하도록 만들지 않는다.
        enabled.ValueRW = false;
        ECB.DestroyEntity(requestEntity);
    }
}
