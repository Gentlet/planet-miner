using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>
/// 검증된 아이템 프리팹의 공통 런타임 구성을 호출자의 ECB에 기록한다.
/// 프리팹 조회·실패 처리·소유 버퍼 등록·Playback 시점은 호출자가 소유한다.
/// </summary>
public static class ItemLifecycleUtility
{
    public static Entity SpawnPrefabItem(
        ref EntityCommandBuffer ecb,
        Entity prefab,
        ItemTypeEnum itemType,
        int2 gridPosition,
        float3 worldPosition,
        ItemOwnership ownership)
    {
        Entity item = ecb.Instantiate(prefab);
        ecb.SetComponent(item, new ItemIdentity(itemType));
        ecb.AddComponent(item, new GridPosition(gridPosition));
        ecb.SetComponent(item, LocalTransform.FromPosition(worldPosition));
        ecb.AddComponent(item, ownership);

        ecb.AddComponent<DestroyItemRequest>(item);
        ecb.SetComponentEnabled<DestroyItemRequest>(item, false);
        ecb.AddComponent<TransferOwnershipRequest>(item);
        ecb.SetComponentEnabled<TransferOwnershipRequest>(item, false);
        ecb.AddComponent<BeltMovementState>(item);
        ecb.SetComponentEnabled<BeltMovementState>(item, false);
        ecb.AddComponent<BeltMovementDecision>(item);
        ecb.AddComponent<BuildingItemInputDecision>(item);
        ecb.SetComponentEnabled<BuildingItemInputDecision>(item, false);

        if (ownership.Owner != Entity.Null)
        {
            ecb.AddComponent<DisableRendering>(item);
        }

        return item;
    }
}
