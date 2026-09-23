using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

/// <summary>
/// 아이템 소유권 상태 적용 시스템 (State Owner).
/// 
/// [책임]
/// - StateApplyGroup(Phase 5)에서 실행.
/// - TransferOwnershipRequest를 처리하여 ItemOwnership의 단일 원본(Source of Truth)을 갱신.
/// - 단일 워커 Burst Job(ItemOwnershipApplyJob)으로 소유권 변경 순차 적용.
/// - Consume-on-Apply 원칙에 따라 처리 즉시 TransferOwnershipRequest를 비활성화.
/// </summary>
[UpdateInGroup(typeof(StateApplyGroup))]
[BurstCompile]
public partial struct ItemOwnershipApplySystem : ISystem
{
    private EntityStorageInfoLookup _entityStorageInfoLookup;
    private EntityQuery _requestQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _entityStorageInfoLookup = state.GetEntityStorageInfoLookup();

        _requestQuery = SystemAPI.QueryBuilder()
            .WithAllRW<ItemOwnership>()
            .WithAllRW<TransferOwnershipRequest>()
            .Build();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _entityStorageInfoLookup.Update(ref state);

        var job = new ItemOwnershipApplyJob
        {
            EntityStorageInfoLookup = _entityStorageInfoLookup
        };

        state.Dependency = job.Schedule(_requestQuery, state.Dependency);
    }
}

/// <summary>
/// TransferOwnershipRequest를 순차적으로 소비하여 ItemOwnership을 갱신하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct ItemOwnershipApplyJob : IJobEntity
{
    [ReadOnly]
    public EntityStorageInfoLookup EntityStorageInfoLookup;

    public void Execute(
        ref ItemOwnership ownership,
        RefRW<TransferOwnershipRequest> request,
        EnabledRefRW<TransferOwnershipRequest> requestEnabled)
    {
        Entity targetOwner = request.ValueRO.TargetOwner;

        if (targetOwner == Entity.Null)
        {
            // 월드로 방출 (월드 아이템 전환)
            ownership = ItemOwnership.WorldItem;
        }
        else if (EntityStorageInfoLookup.Exists(targetOwner))
        {
            // 특정 건물/창고 보관 아이템으로 전환
            ownership = ItemOwnership.Stored(targetOwner);
        }
        // 수신자가 유효하지 않은(파괴된) 유령 엔티티인 경우 소유권 변경을 무시하고 Drop

        // Consume-on-Apply: 처리 완료 즉시 비활성화
        requestEnabled.ValueRW = false;
    }
}
