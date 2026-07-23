using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public partial class ChunkMapSystem : SystemBase
{
    private const int initialCapacity = 1024;
    private readonly Dictionary<int2, Chunk> _chunks = new();
    private readonly Dictionary<Entity, int2> _itemCellByEntity = new();
    private readonly HashSet<int2> _activeBeltCells = new();
    private NativeParallelHashMap<int2, Entity> _beltByCell;
    private NativeParallelHashSet<int2> _reservedCells;

    protected override void OnCreate()
    {
        _beltByCell = new NativeParallelHashMap<int2, Entity>(initialCapacity, Allocator.Persistent);
        _reservedCells = new NativeParallelHashSet<int2>(initialCapacity, Allocator.Persistent);
    }

    protected override void OnUpdate()
    {
        EntityCommandBuffer ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);
        foreach (var (request, requestEntity) in SystemAPI.Query<RefRO<BuildingOccupantRequest>>().WithEntityAccess())
        {
            TryRegisterBuilding(ecb, requestEntity);
        }

        foreach (var (request, requestEntity) in SystemAPI.Query<RefRO<ResourceOccupantRequest>>().WithEntityAccess())
        {
            TryRegisterResource(ecb, requestEntity);
        }

    }

    protected override void OnDestroy()
    {
        if (_beltByCell.IsCreated)
            _beltByCell.Dispose();

        if (_reservedCells.IsCreated)
            _reservedCells.Dispose();
    }

    public bool IsBuildingOccupied(int2 cell)
    {
        return TryGetCellData(cell, out ChunkCell cellData) && cellData.hasBuilding;
    }
    public bool IsBuildingReserved(int2 cell)
    {
        return _reservedCells.Contains(cell);
    }
    public bool IsBuildingOccupiedOrReserved(int2 cell)
    {
        return IsBuildingOccupied(cell) || IsBuildingReserved(cell);
    }

    public bool TryGetBuilding(int2 cell, out Entity entity)
    {
        if (!TryGetCellData(cell, out ChunkCell cellData) || !cellData.hasBuilding)
        {
            entity = Entity.Null;
            return false;
        }

        entity = cellData.buildingEntity;
        return true;
    }

    public NativeParallelHashMap<int2, Entity>.ReadOnly GetBeltCellsReadOnly()
    {
        return _beltByCell.AsReadOnly();
    }

    public bool TryReserveBuilding(int2 cell)
    {
        if (IsBuildingOccupiedOrReserved(cell))
            return false;

        EnsureReservedCapacity();
        GetOrCreateCellData(cell);

        return _reservedCells.Add(cell);
    }
    public bool TryUnreserveBuilding(int2 cell)
    {
        if (!IsBuildingReserved(cell))
            return false;

        return _reservedCells.Remove(cell);
    }

    private bool TryRegisterBuilding(EntityCommandBuffer ecb, Entity entity)
    {
        if (!EntityManager.HasComponent<BuildingOccupantRequest>(entity))
            return false;
        if (!EntityManager.HasComponent<GridPosition>(entity))
            return false;

        GridPosition pos = EntityManager.GetComponentData<GridPosition>(entity);

        EnsureReservedCapacity();
        ChunkCell cellData = GetOrCreateCellData(pos.gridPosition);

        if (!TryUnreserveBuilding(pos.gridPosition))
            UnityEngine.Debug.LogWarning($"Not Reserved Building Spawned. Cell : {pos.gridPosition}");

        if (!cellData.TrySetBuilding(entity))
        {
            UnityEngine.Debug.LogError($"Failed ChunkCell.TrySetBuilding. Type : {EntityManager.GetComponentData<BuildingType>(entity).ToString()}, Cell : {pos.gridPosition}");
            ecb.DestroyEntity(entity);
            return false;
        }

        if (EntityManager.HasComponent<Belt>(entity))
        {
            EnsureBeltCapacity();

            if (!_beltByCell.TryAdd(pos.gridPosition, entity))
            {
                cellData.TryRemoveBuilding(entity);
                UnityEngine.Debug.LogError($"Failed BeltIndex.TryAdd. Type : {EntityManager.GetComponentData<BuildingType>(entity).ToString()}, Cell : {pos.gridPosition}");
                ecb.DestroyEntity(entity);
                return false;
            }

            SortItemsForBelt(pos.gridPosition);

            if (cellData.items.Count > 0)
                _activeBeltCells.Add(pos.gridPosition);
        }

        ecb.RemoveComponent<BuildingOccupantRequest>(entity);
        ecb.AddComponent<BuildingOccupant>(entity);

        return true;
    }
    public bool TryUnregisterBuilding(int2 cell, Entity entity)
    {
        if (!TryGetCellData(cell, out ChunkCell cellData))
            return false;

        if (cellData.buildingEntity != entity)
            return false;

        cellData.TryRemoveBuilding(entity);

        if (_beltByCell.TryGetValue(cell, out Entity beltEntity) && beltEntity == entity)
        {
            _beltByCell.Remove(cell);
            _activeBeltCells.Remove(cell);
        }

        return true;
    }

    public bool TryGetChunk(int2 chunkPosition, out Chunk chunk)
    {
        return _chunks.TryGetValue(chunkPosition, out chunk);
    }
    public Chunk GetOrCreateChunk(int2 chunkPosition)
    {
        if (!_chunks.TryGetValue(chunkPosition, out Chunk chunk))
        {
            chunk = new Chunk(chunkPosition);
            _chunks.Add(chunkPosition, chunk);
        }

        return chunk;
    }

    public bool TryGetCellData(int2 cell, out ChunkCell cellData)
    {
        int2 chunkPosition = ChunkUtility.ToChunkPosition(cell);

        if (!_chunks.TryGetValue(chunkPosition, out Chunk chunk))
        {
            cellData = null;
            return false;
        }

        cellData = chunk.GetCellByWorldPosition(cell);
        return true;
    }

    public IEnumerable<Chunk> GetChunks()
    {
        return _chunks.Values;
    }

    public FloorTypeEnum GetFloor(int2 cell)
    {
        if (!TryGetCellData(cell, out ChunkCell cellData))
            return FloorTypeEnum.Bare;

        return cellData.floor;
    }

    public void SetFloor(int2 cell, FloorTypeEnum floor)
    {
        GetOrCreateCellData(cell).SetFloor(floor);
    }

    private bool TryRegisterResource(EntityCommandBuffer ecb, Entity entity)
    {
        if (!EntityManager.HasComponent<ResourceOccupantRequest>(entity))
            return false;
        if (!EntityManager.HasComponent<GridPosition>(entity))
            return false;
        if (!EntityManager.HasComponent<ResourceDeposit>(entity))
            return false;

        GridPosition pos = EntityManager.GetComponentData<GridPosition>(entity);
        ResourceDeposit resource = EntityManager.GetComponentData<ResourceDeposit>(entity);
        ChunkCell cellData = GetOrCreateCellData(pos.gridPosition);

        if (!cellData.TrySetResource(resource.type, entity))
        {
            UnityEngine.Debug.LogError($"Failed ChunkCell.TrySetResource. Type : {resource.type}, Cell : {pos.gridPosition}");
            ecb.DestroyEntity(entity);
            return false;
        }

        ecb.RemoveComponent<ResourceOccupantRequest>(entity);
        ecb.AddComponent<ResourceOccupant>(entity);

        return true;
    }

    public bool TryUnregisterResource(int2 cell, Entity entity)
    {
        if (!TryGetCellData(cell, out ChunkCell cellData))
            return false;

        return cellData.TryRemoveResource(entity);
    }

    public int GetItemCount(int2 cell)
    {
        if (!TryGetCellData(cell, out ChunkCell cellData))
            return 0;

        return cellData.items.Count;
    }

    public void GetItems(int2 cell, List<Entity> results)
    {
        results.Clear();

        if (!TryGetCellData(cell, out ChunkCell cellData))
            return;

        for (int i = 0; i < cellData.items.Count; i++)
            results.Add(cellData.items[i]);
    }

    public bool TryGetRegisteredItemCell(Entity item, out int2 cell)
    {
        return _itemCellByEntity.TryGetValue(item, out cell);
    }

    public bool TryRegisterItem(int2 cell, Entity item)
    {
        if (item == Entity.Null ||
            !EntityManager.Exists(item) ||
            !EntityManager.HasComponent<Item>(item) ||
            !EntityManager.HasComponent<LocalTransform>(item) ||
            !EntityManager.HasComponent<GridPosition>(item) ||
            !EntityManager.HasComponent<ItemCellChanged>(item) ||
            _itemCellByEntity.ContainsKey(item))
            return false;

        ChunkCell cellData = GetOrCreateCellData(cell);
        if (!cellData.TryAddItem(item))
            return false;

        _itemCellByEntity.Add(item, cell);

        if (_beltByCell.ContainsKey(cell))
            _activeBeltCells.Add(cell);

        return true;
    }

    public bool TryUnregisterItem(int2 cell, Entity item)
    {
        if (!_itemCellByEntity.TryGetValue(item, out int2 registeredCell) ||
            !registeredCell.Equals(cell) ||
            !TryGetCellData(cell, out ChunkCell cellData) ||
            !cellData.RemoveItem(item))
            return false;

        _itemCellByEntity.Remove(item);

        if (cellData.items.Count == 0)
            _activeBeltCells.Remove(cell);

        return true;
    }

    public void CopyActiveBeltCells(List<int2> results)
    {
        results.Clear();

        foreach (int2 cell in _activeBeltCells)
            results.Add(cell);
    }

    public bool TryGetBelt(int2 cell, out Entity beltEntity)
    {
        return _beltByCell.TryGetValue(cell, out beltEntity);
    }

    public void SortItemsForBelt(int2 cell)
    {
        if (!TryGetCellData(cell, out ChunkCell cellData) ||
            !_beltByCell.TryGetValue(cell, out Entity beltEntity) ||
            !EntityManager.Exists(beltEntity) ||
            !EntityManager.HasComponent<Direction>(beltEntity))
            return;

        DirectionEnum direction = EntityManager.GetComponentData<Direction>(beltEntity).dir;

        for (int i = 1; i < cellData.items.Count; i++)
        {
            int currentIndex = i;

            while (currentIndex > 0 && IsItemAhead(cellData.items[currentIndex], cellData.items[currentIndex - 1], direction))
            {
                cellData.SwapItems(currentIndex, currentIndex - 1);
                currentIndex--;
            }
        }
    }

    private bool IsItemAhead(Entity item, Entity otherItem, DirectionEnum direction)
    {
        if (!EntityManager.Exists(item) ||
            !EntityManager.Exists(otherItem) ||
            !EntityManager.HasComponent<LocalTransform>(item) ||
            !EntityManager.HasComponent<LocalTransform>(otherItem))
            return false;

        float3 itemPosition = EntityManager.GetComponentData<LocalTransform>(item).Position;
        float3 otherPosition = EntityManager.GetComponentData<LocalTransform>(otherItem).Position;

        switch (direction)
        {
            case DirectionEnum.Right:
                return itemPosition.x > otherPosition.x;
            case DirectionEnum.Left:
                return itemPosition.x < otherPosition.x;
            case DirectionEnum.Up:
                return itemPosition.y > otherPosition.y;
            case DirectionEnum.Down:
                return itemPosition.y < otherPosition.y;
            default:
                return false;
        }
    }

    private void Clear()
    {
        _chunks.Clear();
        _itemCellByEntity.Clear();
        _activeBeltCells.Clear();
        _beltByCell.Clear();
        _reservedCells.Clear();
    }

    private void EnsureReservedCapacity()
    {
        if ( _reservedCells.Capacity <= _reservedCells.Count() )
            _reservedCells.Capacity *= 2;
    }
    private void EnsureBeltCapacity()
    {
        if (_beltByCell.Capacity <= _beltByCell.Count())
            _beltByCell.Capacity *= 2;
    }

    private ChunkCell GetOrCreateCellData(int2 cell)
    {
        int2 chunkPosition = ChunkUtility.ToChunkPosition(cell);

        if (!_chunks.TryGetValue(chunkPosition, out Chunk chunk))
        {
            chunk = GetOrCreateChunk(chunkPosition);
        }

        return chunk.GetCellByWorldPosition(cell);
    }
}
