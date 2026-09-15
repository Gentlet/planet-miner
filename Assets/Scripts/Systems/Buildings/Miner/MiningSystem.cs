using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[UpdateAfter(typeof(ItemTrackingSystem))]
public partial class MiningSystem : SystemBase
{
    private ChunkMapSystem _chunkMap;
    private ItemStorageSystem _itemStorage;
    private ItemTrackingSystem _itemTracking;
    private EntityQuery _minerOutputQuery;
    private readonly List<BuildingBoundaryConnection> _boundaryConnections = new();
    private readonly List<int2> _outputCells = new();
    private readonly List<int2> _footprintCells = new();
    private readonly List<ResourceCandidate> _resourceCandidates = new();
    private readonly List<ResourceCandidate> _matchingCandidates = new();
    private readonly List<ResourceTypeEnum> _resourceTypes = new();
    private readonly HashSet<Entity> _reportedMixedBuffers = new();

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
        _itemStorage = World.GetExistingSystemManaged<ItemStorageSystem>();
        _itemTracking = World.GetExistingSystemManaged<ItemTrackingSystem>();
        _minerOutputQuery = GetEntityQuery(
            ComponentType.ReadWrite<Miner>(),
            ComponentType.ReadOnly<GridPosition>(),
            ComponentType.ReadOnly<BuildingFootprint>(),
            ComponentType.ReadOnly<Direction>(),
            ComponentType.ReadWrite<BuildingOutputCursor>(),
            ComponentType.ReadWrite<ProducedItemElement>());

        RequireForUpdate<ItemPrefabElement>();
        RequireForUpdate<ItemStorageLimitElement>();
        RequireForUpdate<ResearchConfig>();
    }

    protected override void OnUpdate()
    {
        if (!EnsureSystems())
            return;

        _itemTracking.ApplyPendingChangesImmediate();
        using NativeArray<Entity> miners =
            _minerOutputQuery.ToEntityArray(Allocator.Temp);
        TryOutputProducedItems(miners);
        using NativeArray<ItemStorageLimitElement> storageLimits =
            DynamicBufferCopyUtility.CreateNativeCopy(
                SystemAPI.GetSingletonBuffer<ItemStorageLimitElement>(true),
                Allocator.Temp);
        EntityCommandBuffer ecb = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(World.Unmanaged);
        float deltaTime = SystemAPI.Time.DeltaTime;
        DynamicBuffer<ResearchStatModifierElement> researchModifiers =
            SystemAPI.GetSingletonBuffer<ResearchStatModifierElement>(true);
        float miningSpeedMultiplier = researchModifiers.GetStatMultiplier(
            ResearchStatModifierTypeEnum.MiningSpeed);

        for (int i = 0; i < miners.Length; i++)
        {
            Entity minerEntity = miners[i];
            Miner miner = EntityManager.GetComponentData<Miner>(minerEntity);
            int2 anchor = EntityManager
                .GetComponentData<GridPosition>(minerEntity)
                .gridPosition;
            DirectionEnum direction = EntityManager
                .GetComponentData<Direction>(minerEntity)
                .dir;
            int2 footprintSize = EntityManager
                .GetComponentData<BuildingFootprint>(minerEntity)
                .size;
            DynamicBuffer<ProducedItemElement> producedItems =
                EntityManager.GetBuffer<ProducedItemElement>(minerEntity);
            float progressDeltaTime = PowerProductionUtility
                .GetProgressDeltaTime(
                    EntityManager,
                    minerEntity,
                    deltaTime) * miningSpeedMultiplier;

            Mine(
                ref ecb,
                storageLimits,
                producedItems,
                minerEntity,
                ref miner,
                anchor,
                footprintSize,
                direction,
                progressDeltaTime);

            EntityManager.SetComponentData(minerEntity, miner);
        }
    }

    private void TryOutputProducedItems(
        NativeArray<Entity> miners)
    {
        for (int i = 0; i < miners.Length; i++)
        {
            Entity minerEntity = miners[i];
            DynamicBuffer<ProducedItemElement> producedItems =
                EntityManager.GetBuffer<ProducedItemElement>(minerEntity);

            if (producedItems.Length == 0)
                continue;

            int2 anchor = EntityManager
                .GetComponentData<GridPosition>(minerEntity)
                .gridPosition;
            DirectionEnum direction = EntityManager
                .GetComponentData<Direction>(minerEntity)
                .dir;
            int2 footprintSize = EntityManager
                .GetComponentData<BuildingFootprint>(minerEntity)
                .size;
            BuildingOutputCursor cursor = EntityManager
                .GetComponentData<BuildingOutputCursor>(minerEntity);
            BuildingBeltConnectionUtility.TryOutputItem<ProducedItemElement>(
                _chunkMap,
                EntityManager,
                _itemStorage,
                minerEntity,
                anchor,
                footprintSize,
                direction,
                ref cursor,
                _boundaryConnections,
                _outputCells);
            EntityManager.SetComponentData(minerEntity, cursor);
        }
    }

    private void Mine(
        ref EntityCommandBuffer ecb,
        NativeArray<ItemStorageLimitElement> storageLimits,
        DynamicBuffer<ProducedItemElement> producedItems,
        Entity minerEntity,
        ref Miner miner,
        int2 anchor,
        int2 footprintSize,
        DirectionEnum direction,
        float progressDeltaTime)
    {
        if (miner.speed <= 0f)
            return;

        miner.timer += progressDeltaTime;

        if (miner.timer < miner.speed)
            return;

        miner.timer = miner.speed;
        CollectResourceCandidates(
            anchor,
            footprintSize,
            direction);

        if (_resourceCandidates.Count == 0 ||
            !TrySelectResourceCandidate(
                minerEntity,
                producedItems,
                ref miner,
                out ResourceCandidate selectedCandidate))
            return;

        ItemTypeEnum itemType = selectedCandidate.deposit.type.ToItemType();
        int storageLimit = storageLimits.GetStorageLimit(itemType);

        if (storageLimit <= 0 ||
            producedItems.CountItems(itemType) >= storageLimit)
            return;

        CreateItemSpawnRequest(ref ecb, minerEntity, itemType);
        ConsumeDeposit(
            ref ecb,
            selectedCandidate.cell,
            selectedCandidate.entity,
            selectedCandidate.deposit);
        miner.timer -= miner.speed;
    }

    private void CollectResourceCandidates(
        int2 anchor,
        int2 size,
        DirectionEnum direction)
    {
        _resourceCandidates.Clear();
        BuildingFootprintUtility.GetOccupiedCells(
            anchor,
            size,
            direction,
            _footprintCells);

        for (int i = 0; i < _footprintCells.Count; i++)
        {
            int2 cell = _footprintCells[i];

            if (!_chunkMap.TryGetCellData(cell, out ChunkCell cellData) ||
                !cellData.HasResource ||
                !EntityManager.Exists(cellData.ResourceEntity))
                continue;

            ResourceDeposit deposit =
                EntityManager.GetComponentData<ResourceDeposit>(
                    cellData.ResourceEntity);

            if (deposit.amount <= 0)
                continue;

            _resourceCandidates.Add(new ResourceCandidate
            {
                cell = cell,
                entity = cellData.ResourceEntity,
                deposit = deposit
            });
        }
    }

    private bool TrySelectResourceCandidate(
        Entity minerEntity,
        DynamicBuffer<ProducedItemElement> producedItems,
        ref Miner miner,
        out ResourceCandidate selectedCandidate)
    {
        selectedCandidate = default;

        if (!TryGetTargetItemType(
                minerEntity,
                producedItems,
                ref miner,
                out ItemTypeEnum targetItemType))
            return false;

        _matchingCandidates.Clear();

        for (int i = 0; i < _resourceCandidates.Count; i++)
        {
            ResourceCandidate candidate = _resourceCandidates[i];

            if (candidate.deposit.type.ToItemType() == targetItemType)
                _matchingCandidates.Add(candidate);
        }

        if (_matchingCandidates.Count == 0)
            return false;

        selectedCandidate = _matchingCandidates[
            NextRandomIndex(ref miner, _matchingCandidates.Count)];
        return true;
    }

    private bool TryGetTargetItemType(
        Entity minerEntity,
        DynamicBuffer<ProducedItemElement> producedItems,
        ref Miner miner,
        out ItemTypeEnum targetItemType)
    {
        if (producedItems.Length > 0)
        {
            targetItemType = producedItems[0].type;

            for (int i = 1; i < producedItems.Length; i++)
            {
                if (producedItems[i].type == targetItemType)
                    continue;

                if (_reportedMixedBuffers.Add(minerEntity))
                {
                    Debug.LogError(
                        $"Miner produced-item buffer contains mixed item types. Entity: {minerEntity}");
                }

                return false;
            }

            _reportedMixedBuffers.Remove(minerEntity);
            return targetItemType.IsValid();
        }

        _reportedMixedBuffers.Remove(minerEntity);
        _resourceTypes.Clear();

        for (int i = 0; i < _resourceCandidates.Count; i++)
        {
            ResourceTypeEnum resourceType =
                _resourceCandidates[i].deposit.type;

            if (!_resourceTypes.Contains(resourceType))
                _resourceTypes.Add(resourceType);
        }

        if (_resourceTypes.Count == 0)
        {
            targetItemType = ItemTypeEnum.None;
            return false;
        }

        ResourceTypeEnum selectedResourceType = _resourceTypes[
            NextRandomIndex(ref miner, _resourceTypes.Count)];
        targetItemType = selectedResourceType.ToItemType();
        return targetItemType.IsValid();
    }

    private static int NextRandomIndex(ref Miner miner, int count)
    {
        if (count <= 1)
            return 0;

        Unity.Mathematics.Random random = new(
            miner.randomState == 0 ? 1u : miner.randomState);
        int index = random.NextInt(count);
        miner.randomState = random.state;
        return index;
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

    private bool EnsureSystems()
    {
        if (_chunkMap == null)
            _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();

        if (_itemStorage == null)
            _itemStorage = World.GetExistingSystemManaged<ItemStorageSystem>();

        if (_itemTracking == null)
            _itemTracking = World.GetExistingSystemManaged<ItemTrackingSystem>();

        return _chunkMap != null &&
               _itemStorage != null &&
               _itemTracking != null;
    }

    private struct ResourceCandidate
    {
        public int2 cell;
        public Entity entity;
        public ResourceDeposit deposit;
    }
}
