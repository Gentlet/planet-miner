using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public partial class ChunkMapSystem : SystemBase
{
    private const int initialCapacity = 1024;
    private readonly Dictionary<int2, Chunk> _chunks = new();
    private readonly Dictionary<Entity, int2> _itemCellByEntity = new();
    private readonly Dictionary<int2, Entity> _constructionSiteByCell = new();
    private readonly HashSet<int2> _activeBeltCells = new();
    private NativeParallelHashMap<int2, Entity> _beltByCell;
    private NativeParallelHashSet<int2> _reservedCells;
    private EntityQuery _buildingPrefabQuery;

    protected override void OnCreate()
    {
        _beltByCell = new NativeParallelHashMap<int2, Entity>(initialCapacity, Allocator.Persistent);
        _reservedCells = new NativeParallelHashSet<int2>(initialCapacity, Allocator.Persistent);
        _buildingPrefabQuery = GetEntityQuery(
            ComponentType.ReadOnly<BuildingPrefabElement>());
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

        return cellData.Floor;
    }

    public void SetFloor(int2 cell, FloorTypeEnum floor)
    {
        GetOrCreateCellData(cell).SetFloor(floor);
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
        if (_reservedCells.Capacity <= _reservedCells.Count())
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
            chunk = GetOrCreateChunk(chunkPosition);

        return chunk.GetCellByWorldPosition(cell);
    }
}
