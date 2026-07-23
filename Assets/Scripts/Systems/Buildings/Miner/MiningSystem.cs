using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateAfter(typeof(ItemTrackingSystem))]
public partial class MiningSystem : SystemBase
{
    private ChunkMapSystem _chunkMap;
    private readonly Dictionary<int2, int> _reservedOutputItemCounts = new();

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
        RequireForUpdate<ItemPrefabElement>();
    }

    protected override void OnUpdate()
    {
        if (_chunkMap == null)
        {
            _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
            if (_chunkMap == null)
                return;
        }

        DynamicBuffer<ItemPrefabElement> itemPrefabs = SystemAPI.GetSingletonBuffer<ItemPrefabElement>(true);
        EntityCommandBuffer ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);
        float deltaTime = SystemAPI.Time.DeltaTime;

        _reservedOutputItemCounts.Clear();

        foreach (var (miner, gridPosition, direction) in SystemAPI.Query<RefRW<Miner>, RefRO<GridPosition>, RefRO<Direction>>())
        {
            Mine(ref ecb, itemPrefabs, ref miner.ValueRW, gridPosition.ValueRO.gridPosition, direction.ValueRO.dir, deltaTime);
        }
    }

    private void Mine(ref EntityCommandBuffer ecb, DynamicBuffer<ItemPrefabElement> itemPrefabs, ref Miner miner, int2 minerCell, DirectionEnum direction, float deltaTime)
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
        Entity itemPrefab = FindPrefab(itemPrefabs, itemType);
        if (itemPrefab == Entity.Null)
            return;

        int2 outputCell = minerCell + direction.ToInt2();
        if (!CanMining(outputCell))
            return;

        ReserveOutputItem(outputCell);
        SpawnItem(ref ecb, itemPrefab, outputCell, itemType);
        ConsumeDeposit(ref ecb, minerCell, depositEntity, deposit);
        miner.timer -= miner.speed;
    }

    private bool CanMining(int2 outputCell)
    {
        _reservedOutputItemCounts.TryGetValue(outputCell, out int reservedCount);

        return _chunkMap.GetItemCount(outputCell) + reservedCount < GameConstants.MaximumItemInCell;
    }

    private void ReserveOutputItem(int2 outputCell)
    {
        if (_reservedOutputItemCounts.TryGetValue(outputCell, out int reservedCount))
        {
            _reservedOutputItemCounts[outputCell] = reservedCount + 1;
            return;
        }

        _reservedOutputItemCounts.Add(outputCell, 1);
    }

    private void SpawnItem(ref EntityCommandBuffer ecb, Entity itemPrefab, int2 outputCell, ItemTypeEnum type)
    {
        Entity item = ecb.Instantiate(itemPrefab);
        LocalTransform transform = LocalTransform.FromPosition(new float3(outputCell.x, outputCell.y, 0f));
        GridPosition gridPosition = new GridPosition { gridPosition = outputCell };
        Item itemComponent = new Item { type = type };

        ecb.SetComponent(item, transform);
        ecb.AddComponent(item, gridPosition);
        ecb.AddComponent(item, itemComponent);
        ecb.AddComponent<ItemCellChanged>(item);
        ecb.SetComponentEnabled<ItemCellChanged>(item, true);
    }

    private void ConsumeDeposit(ref EntityCommandBuffer ecb, int2 cell, Entity depositEntity, ResourceDeposit deposit)
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

    private static Entity FindPrefab(DynamicBuffer<ItemPrefabElement> prefabs, ItemTypeEnum type)
    {
        for (int i = 0; i < prefabs.Length; i++)
        {
            if (prefabs[i].type == type)
                return prefabs[i].prefab;
        }

        return Entity.Null;
    }
}
