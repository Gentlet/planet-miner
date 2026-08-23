using Unity.Collections;
using Unity.Entities;

[UpdateAfter(typeof(ItemTrackingSystem))]
[UpdateBefore(typeof(DroneTaskCommandSystem))]
public partial class DroneWorldItemRecoveryRequestSystem : SystemBase
{
    private EntityQuery _requestQuery;

    protected override void OnCreate()
    {
        _requestQuery = GetEntityQuery(
            ComponentType.ReadOnly<DroneWorldItemRecoveryCreateRequest>());
    }

    protected override void OnUpdate()
    {
        using NativeArray<Entity> requests =
            _requestQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < requests.Length; i++)
            TryPromoteRequest(requests[i]);
    }

    private void TryPromoteRequest(Entity requestEntity)
    {
        DroneWorldItemRecoveryCreateRequest request = EntityManager
            .GetComponentData<DroneWorldItemRecoveryCreateRequest>(requestEntity);

        if (request.itemEntity == Entity.Null)
        {
            EntityManager.DestroyEntity(requestEntity);
            return;
        }

        if (!EntityManager.Exists(request.itemEntity))
        {
            EntityManager.DestroyEntity(requestEntity);
            return;
        }

        if (EntityManager.HasComponent<StoredItem>(request.itemEntity))
            return;

        if (EntityManager.HasComponent<Disabled>(request.itemEntity))
            return;

        if (!EntityManager.HasComponent<Item>(request.itemEntity))
        {
            EntityManager.DestroyEntity(requestEntity);
            return;
        }

        if (!EntityManager.HasComponent<GridPosition>(request.itemEntity))
            return;

        EntityManager.AddComponentData(requestEntity, new DroneTaskCreateRequest
        {
            type = DroneTaskTypeEnum.RecoverWorldItem,
            priorityClass = DroneTaskPriorityClassEnum.Normal,
            normalPriority = request.normalPriority,
            totalQuantity = 1
        });
        EntityManager.AddComponentData(requestEntity,
            new DroneWorldItemRecoveryTaskData
            {
                itemEntity = request.itemEntity
            });
        EntityManager.RemoveComponent<DroneWorldItemRecoveryCreateRequest>(
            requestEntity);
    }
}
