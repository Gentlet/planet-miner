using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

/// <summary>
/// 역할·목적: 완공 건물의 철거 요청을 승인/거부하고 승인 건물의 해당 틱 동작을 중단한다.
/// 처리 단계: Command. 입력은 별도 요청 엔티티의 DemolishBuildingRequest와 대상 건물/보호/프리팹 상태다.
/// 출력·소유권: 승인 건물의 기존 Transfer를 비활성화하고 ProductResult를 비우며 PendingBuildingDemolition을 EndCommand에 기록한다.
/// 이후 Decision은 승인 상태로 실행 후보를 차단하고 BuildingLifecycleApplySystem(StateApply)이 반환/환급/실제 삭제를 담당한다.
/// 정리·가시화: 승인/거부/중복 요청 모두 EndCommand에서 삭제한다. 요청은 실행 전에 실체화하고 승인 대상/조건은 StateApply까지 유지한다.
/// </summary>
[UpdateInGroup(typeof(CommandGroup))]
public partial struct BuildingDemolitionCommandSystem : ISystem
{
    private EntityQuery _requestQuery;
    private ComponentLookup<BuildingType> _buildingTypeLookup;
    private ComponentLookup<IndestructibleBuilding> _indestructibleLookup;
    private ComponentLookup<Prefab> _prefabLookup;
    private ComponentLookup<PendingBuildingDemolition> _pendingLookup;
    private ComponentLookup<TransferOwnershipRequest> _transferLookup;
    private BufferLookup<StoredItemElement> _storedLookup;
    private BufferLookup<ProductItemElement> _productLookup;
    private BufferLookup<ProductResult> _resultLookup;

    public void OnCreate(ref SystemState state)
    {
        _requestQuery = SystemAPI.QueryBuilder().WithAll<DemolishBuildingRequest>().Build();
        _buildingTypeLookup = state.GetComponentLookup<BuildingType>(true);
        _indestructibleLookup = state.GetComponentLookup<IndestructibleBuilding>(true);
        _prefabLookup = state.GetComponentLookup<Prefab>(true);
        _pendingLookup = state.GetComponentLookup<PendingBuildingDemolition>(true);
        _transferLookup = state.GetComponentLookup<TransferOwnershipRequest>(false);
        _storedLookup = state.GetBufferLookup<StoredItemElement>(true);
        _productLookup = state.GetBufferLookup<ProductItemElement>(true);
        _resultLookup = state.GetBufferLookup<ProductResult>(false);
        state.RequireForUpdate(_requestQuery);
    }

    public void OnUpdate(ref SystemState state)
    {
        _buildingTypeLookup.Update(ref state);
        _indestructibleLookup.Update(ref state);
        _prefabLookup.Update(ref state);
        _pendingLookup.Update(ref state);
        _transferLookup.Update(ref state);
        _storedLookup.Update(ref state);
        _productLookup.Update(ref state);
        _resultLookup.Update(ref state);
        var ecbSystem = state.World.GetOrCreateSystemManaged<EndCommandEntityCommandBufferSystem>();
        // 승인 상태는 EndCommand까지 보이지 않으므로 같은 Job의 중복 요청은 별도 집합으로 차단한다.
        var acceptedTargets = new NativeParallelHashSet<Entity>(_requestQuery.CalculateEntityCount(), Allocator.TempJob);
        var job = new ValidateBuildingDemolitionJob
        {
            ECB = ecbSystem.CreateCommandBuffer(),
            BuildingTypeLookup = _buildingTypeLookup,
            IndestructibleLookup = _indestructibleLookup,
            PrefabLookup = _prefabLookup,
            PendingLookup = _pendingLookup,
            TransferLookup = _transferLookup,
            StoredLookup = _storedLookup,
            ProductLookup = _productLookup,
            ResultLookup = _resultLookup,
            AcceptedTargets = acceptedTargets
        };
        var handle = job.Schedule(_requestQuery, state.Dependency);
        state.Dependency = acceptedTargets.Dispose(handle);
        ecbSystem.AddJobHandleForProducer(state.Dependency);
    }
}

[BurstCompile]
public partial struct ValidateBuildingDemolitionJob : IJobEntity
{
    public EntityCommandBuffer ECB;
    [ReadOnly] public ComponentLookup<BuildingType> BuildingTypeLookup;
    [ReadOnly] public ComponentLookup<IndestructibleBuilding> IndestructibleLookup;
    [ReadOnly] public ComponentLookup<Prefab> PrefabLookup;
    [ReadOnly] public ComponentLookup<PendingBuildingDemolition> PendingLookup;
    public ComponentLookup<TransferOwnershipRequest> TransferLookup;
    [ReadOnly] public BufferLookup<StoredItemElement> StoredLookup;
    [ReadOnly] public BufferLookup<ProductItemElement> ProductLookup;
    public BufferLookup<ProductResult> ResultLookup;
    public NativeParallelHashSet<Entity> AcceptedTargets;

    public void Execute(Entity requestEntity, in DemolishBuildingRequest request)
    {
        if (!CanDemolish(request.TargetBuilding))
        {
            ECB.DestroyEntity(requestEntity);
            return;
        }

        if (!AcceptedTargets.Add(request.TargetBuilding))
        {
            ECB.DestroyEntity(requestEntity);
            return;
        }

        // 이전 틱에서 남은 출고/생산 결과도 끊어야 승인 후 추가 입출고가 발생하지 않는다.
        // 이번 틱 새 후보는 EndCommand에 공개되는 승인 상태를 읽어 Decision에서 차단한다.
        CancelBufferedTransfers(request.TargetBuilding);
        if (ResultLookup.HasBuffer(request.TargetBuilding))
        {
            ResultLookup[request.TargetBuilding].Clear();
        }
        // 외부 요청의 수명은 여기서 끝내고 이후 철거 수명주기는 대상 건물의 상태로 전달한다.
        ECB.AddComponent<PendingBuildingDemolition>(request.TargetBuilding);
        ECB.DestroyEntity(requestEntity);
    }

    private void CancelBufferedTransfers(Entity building)
    {
        if (StoredLookup.HasBuffer(building))
        {
            var items = StoredLookup[building];
            for (int i = 0; i < items.Length; i++)
            {
                CancelTransfer(items[i].ItemEntity);
            }
        }
        if (ProductLookup.HasBuffer(building))
        {
            var items = ProductLookup[building];
            for (int i = 0; i < items.Length; i++)
            {
                CancelTransfer(items[i].ItemEntity);
            }
        }
    }

    private void CancelTransfer(Entity item)
    {
        if (item == Entity.Null)
        {
            return;
        }
        if (!TransferLookup.HasComponent(item))
        {
            return;
        }
        TransferLookup.SetComponentEnabled(item, false);
    }

    private bool CanDemolish(Entity target)
    {
        if (target == Entity.Null)
        {
            return false;
        }

        if (!BuildingTypeLookup.HasComponent(target))
        {
            return false;
        }

        // 불변 프리팹 DB의 원형은 월드에 배치된 완공 건물이 아니다.
        if (PrefabLookup.HasComponent(target))
        {
            return false;
        }

        if (PendingLookup.HasComponent(target))
        {
            return false;
        }

        BuildingTypeEnum type = BuildingTypeLookup[target].Type;
        if (type == BuildingTypeEnum.None || type == BuildingTypeEnum.ConstructionSite || type == BuildingTypeEnum.MainFacility)
        {
            return false;
        }

        return !IndestructibleLookup.HasComponent(target);
    }
}
