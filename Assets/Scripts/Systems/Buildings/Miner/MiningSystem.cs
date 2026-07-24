using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[UpdateAfter(typeof(ItemTrackingSystem))]
public partial class MiningSystem : SystemBase
{
    private ChunkMapSystem _chunkMap;
    private ItemStorageSystem _itemStorage;
    private ItemTrackingSystem _itemTracking;
    private EntityQuery _minerOutputQuery;

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
        _itemStorage = World.GetExistingSystemManaged<ItemStorageSystem>();
        _itemTracking = World.GetExistingSystemManaged<ItemTrackingSystem>();
        _minerOutputQuery = GetEntityQuery(
            ComponentType.ReadWrite<Miner>(),
            ComponentType.ReadOnly<GridPosition>(),
            ComponentType.ReadOnly<Direction>(),
            ComponentType.ReadWrite<ProducedItemElement>());

        RequireForUpdate<ItemPrefabElement>();
        RequireForUpdate<ItemStorageLimitElement>();
    }

    protected override void OnUpdate()
    {
        if (!EnsureSystems())
            return;

        _itemTracking.ApplyPendingChangesImmediate();
        using NativeArray<Entity> miners = _minerOutputQuery.ToEntityArray(Allocator.Temp);
        TryOutputProducedItems(miners);

        using NativeArray<ItemStorageLimitElement> storageLimits =
            CopyBuffer(SystemAPI.GetSingletonBuffer<ItemStorageLimitElement>(true));
        EntityCommandBuffer ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(World.Unmanaged);
        float deltaTime = SystemAPI.Time.DeltaTime;

        for (int i = 0; i < miners.Length; i++)
        {
            Entity minerEntity = miners[i];
            Miner miner = EntityManager.GetComponentData<Miner>(minerEntity);
            int2 minerCell = EntityManager.GetComponentData<GridPosition>(minerEntity).gridPosition;
            DynamicBuffer<ProducedItemElement> producedItems =
                EntityManager.GetBuffer<ProducedItemElement>(minerEntity);

            Mine(
                ref ecb,
                storageLimits,
                producedItems,
                minerEntity,
                ref miner,
                minerCell,
                deltaTime);

            EntityManager.SetComponentData(minerEntity, miner);
        }
    }

    private void TryOutputProducedItems(NativeArray<Entity> miners)
    {
        for (int i = 0; i < miners.Length; i++)
        {
            Entity minerEntity = miners[i];
            DynamicBuffer<ProducedItemElement> producedItems =
                EntityManager.GetBuffer<ProducedItemElement>(minerEntity);

            if (producedItems.Length == 0)
                continue;

            int2 minerCell = EntityManager.GetComponentData<GridPosition>(minerEntity).gridPosition;
            DirectionEnum direction = EntityManager.GetComponentData<Direction>(minerEntity).dir;
            int2 outputCell = minerCell + direction.ToInt2();
            _itemStorage.TryRestoreProducedItemImmediate(minerEntity, 0, outputCell);
        }
    }

    private void Mine(
        ref EntityCommandBuffer ecb,
        NativeArray<ItemStorageLimitElement> storageLimits,
        DynamicBuffer<ProducedItemElement> producedItems,
        Entity minerEntity,
        ref Miner miner,
        int2 minerCell,
        float deltaTime)
    {
        if (miner.speed <= 0f)
            return;

        miner.timer += deltaTime;
        if (miner.timer < miner.speed)
            return;

        miner.timer = miner.speed;

        if (!_chunkMap.TryGetCellData(minerCell, out ChunkCell cellData) || !cellData.hasResource)
            return;

        Entity depositEntity = cellData.resourceEntity;
        ResourceDeposit deposit = EntityManager.GetComponentData<ResourceDeposit>(depositEntity);
        if (deposit.amount <= 0)
            return;

        ItemTypeEnum itemType = deposit.type.ToItemType();
        int storageLimit = storageLimits.GetStorageLimit(itemType);

        if (storageLimit <= 0 ||
            producedItems.CountItems(itemType) >= storageLimit)
            return;

        CreateItemSpawnRequest(ref ecb, minerEntity, itemType);
        ConsumeDeposit(ref ecb, minerCell, depositEntity, deposit);
        miner.timer -= miner.speed;
    }

    private void ConsumeDeposit(
        ref EntityCommandBuffer ecb,
        int2 cell,
        Entity depositEntity,
        ResourceDeposit deposit)
    {
        deposit.amount--;

        if (deposit.amount <= 0)
        {
            _chunkMap.TryUnregisterResource(cell, depositEntity);
            ecb.DestroyEntity(depositEntity);
            return;
        }

        ecb.SetComponent(depositEntity, deposit);
    }

    private static void CreateItemSpawnRequest(
        ref EntityCommandBuffer ecb,
        Entity owner,
        ItemTypeEnum itemType)
    {
        Entity requestEntity = ecb.CreateEntity();
        ecb.AddComponent(requestEntity, new ItemSpawnRequest
        {
            owner = owner,
            itemType = itemType
        });
    }

    private static NativeArray<T> CopyBuffer<T>(DynamicBuffer<T> buffer)
        where T : unmanaged, IBufferElementData
    {
        NativeArray<T> copy = new NativeArray<T>(buffer.Length, Allocator.Temp);

        for (int i = 0; i < buffer.Length; i++)
            copy[i] = buffer[i];

        return copy;
    }

    private bool EnsureSystems()
    {
        if (_chunkMap == null)
            _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();

        if (_itemStorage == null)
            _itemStorage = World.GetExistingSystemManaged<ItemStorageSystem>();

        if (_itemTracking == null)
            _itemTracking = World.GetExistingSystemManaged<ItemTrackingSystem>();

        return _chunkMap != null && _itemStorage != null && _itemTracking != null;
    }
}
