using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

internal readonly struct InventoryReturn
{
    public readonly ItemTypeEnum ItemType;
    public readonly int Quantity;

    public InventoryReturn(ItemTypeEnum itemType, int quantity)
    {
        ItemType = itemType;
        Quantity = quantity;
    }
}

/// <summary>월드 아이템을 공통 위치와 초기 상태로 생성한다. 공개 경계는 호출자의 ECB가 결정한다.</summary>
public static class ItemLifecycleUtility
{


    /// <summary>호출자가 전달한 종료 ECB에 월드 아이템 삭제를 기록한다.</summary>
    public static void RecordDestroy(ref EntityCommandBuffer ecb, Entity item)
    {
        ecb.DestroyEntity(item);
    }

    /// <summary>격자 위치에 월드 실물 하나를 기록한다. 반환 참조는 ECB 재생 뒤 유효하다.</summary>
    public static Entity SpawnPrefabItem(
        ref EntityCommandBuffer ecb,
        Entity prefab,
        ItemTypeEnum itemType,
        int2 gridPosition)
    {
        var worldPosition = new float3(gridPosition.x, gridPosition.y, 0f);
        Entity item = RuntimePrefabLifecycleUtility.Instantiate(ref ecb, prefab);
        InitializeItemRole(ref ecb, item, itemType, gridPosition, worldPosition);
        return item;
    }

    /// <summary>생성된 실물의 품목, 위치, 이동과 요청 초기값을 기록한다.</summary>
    private static void InitializeItemRole(ref EntityCommandBuffer ecb, Entity item, ItemTypeEnum itemType,
        int2 gridPosition, float3 worldPosition)
    {
        ecb.AddComponent(item, new ItemIdentity(itemType));
        ecb.AddComponent(item, new GridPosition(gridPosition));
        ecb.AddComponent(item, LocalTransform.FromPosition(worldPosition));

        // 요청/이동 컴포넌트는 미리 부착하되 비활성으로 시작해 생성 직후에는 삭제나 벨트 이동을 시작하지 않는다.
        ecb.AddComponent<DestroyItemRequest>(item);
        ecb.SetComponentEnabled<DestroyItemRequest>(item, false);
        ecb.AddComponent<BeltMovementState>(item);
        ecb.SetComponentEnabled<BeltMovementState>(item, false);
        ecb.AddComponent<BeltMovementDecision>(item);
        ecb.AddComponent<BuildingItemInputDecision>(item);
        ecb.SetComponentEnabled<BuildingItemInputDecision>(item, false);

    }

    internal static bool TryReturnInventory(ref EntityCommandBuffer ecb, in BufferLookup<ItemPrefabElement> prefabs,
        Entity database, in NativeList<InventoryReturn> inventory, int2 position, bool demolition)
    {
        if (inventory.IsEmpty) return true;
        if (!prefabs.TryGetBuffer(database, out var entries))
        {
            SimulationFailureUtility.Record(ref ecb, "[ItemLifecycleUtility] Missing prefab database for inventory return.");
            return false;
        }

        using var resolved = new NativeList<Entity>(inventory.Length, Allocator.Temp);
        for (int index = 0; index < inventory.Length; index++)
        {
            if (!PrefabLookupUtility.TryGetItemPrefab(entries, inventory[index].ItemType, out var prefab))
            {
                SimulationFailureUtility.Record(ref ecb, "[ItemLifecycleUtility] Missing item prefab for inventory return.");
                return false;
            }
            resolved.Add(prefab);
        }

        for (int index = 0; index < inventory.Length; index++)
        {
            var entry = inventory[index];
            for (int count = 0; count < entry.Quantity; count++)
            {
                Entity item = SpawnPrefabItem(ref ecb, resolved[index], entry.ItemType, position);
                if (demolition)
                    ecb.AddComponent(item, new DroneRecoveryPending { Reason = DroneRecoveryReasonEnum.BuildingDemolition });
            }
        }
        return true;
    }
}
