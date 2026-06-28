using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public partial class GridOccupancySystem : SystemBase
{
    private const int initialCapacity = 1024;
    private NativeParallelHashMap<int2, Entity> _entityByCell;
    private NativeParallelHashSet<int2> _reservedCells;
    public int Count => _entityByCell.Count();


    protected override void OnCreate()
    {
        _entityByCell = new NativeParallelHashMap<int2, Entity>(initialCapacity, Allocator.Persistent);
        _reservedCells = new NativeParallelHashSet<int2>(initialCapacity, Allocator.Persistent);
    }

    protected override void OnUpdate()
    {
        EntityCommandBuffer ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);
        foreach (var (request, requestEntity) in SystemAPI.Query<RefRO<GridOccupantRequest>>().WithEntityAccess())
        {
            TryRegister(ecb, requestEntity);
        }
    }

    protected override void OnDestroy()
    {
        if (_entityByCell.IsCreated)
            _entityByCell.Dispose();

        if (_reservedCells.IsCreated)
            _reservedCells.Dispose();
    }

    public bool IsOccupied(int2 cell)
    {
        return _entityByCell.ContainsKey(cell);
    }

    public bool IsReserved(int2 cell)
    {
        return _reservedCells.Contains(cell);
    }

    public bool IsOccupiedOrReserved(int2 cell)
    {
        return _entityByCell.ContainsKey(cell) || _reservedCells.Contains(cell);
    }

    public bool TryGetEntity(int2 cell, out Entity entity)
    {
        return _entityByCell.TryGetValue(cell, out entity);
    }

    public NativeParallelHashMap<int2, Entity>.ReadOnly GetOccupiedCellsReadOnly()
    {
        return _entityByCell.AsReadOnly();
    }

    public bool TryReserve(int2 cell)
    {
        if (IsOccupiedOrReserved(cell))
            return false;

        EnsureCapacity();

        return _reservedCells.Add(cell);
    }
    public bool TryUnreserve(int2 cell)
    {
        if (!IsReserved(cell))
            return false;

        return _reservedCells.Remove(cell);
    }

    private bool TryRegister(EntityCommandBuffer ecb, Entity entity)
    {
        if (!EntityManager.HasComponent<GridOccupantRequest>(entity))
            return false;
        if (!EntityManager.HasComponent<GridPosition>(entity))
            return false;

        GridPosition pos = EntityManager.GetComponentData<GridPosition>(entity);

        EnsureCapacity();

        if (!TryUnreserve(pos.gridPosition))
            UnityEngine.Debug.LogWarning($"Not Reserved Building Spawned. Cell : {pos.gridPosition}");

        if (!_entityByCell.TryAdd(pos.gridPosition, entity))
        {
            UnityEngine.Debug.LogError($"Failed Occupany.TryAdd. Type : {EntityManager.GetComponentData<BuildingType>(entity).ToString()}, Cell : {pos.gridPosition}");
            ecb.DestroyEntity(entity);
            return false;
        }

        ecb.RemoveComponent<GridOccupantRequest>(entity);
        ecb.AddComponent<GridOccupant>(entity);

        return true;
    }

    private bool TryUnregister(int2 cell, Entity entity)
    {
        if (!_entityByCell.TryGetValue(cell, out Entity registeredEntity))
            return false;

        if (registeredEntity != entity)
            return false;

        return _entityByCell.Remove(cell);
    }

    private void Clear()
    {
        _entityByCell.Clear();
        _reservedCells.Clear();
    }

    private void EnsureCapacity()
    {
        if ( _entityByCell.Capacity <= _entityByCell.Count() )
            _entityByCell.Capacity *= 2;

        if ( _reservedCells.Capacity <= _reservedCells.Count() )
            _reservedCells.Capacity *= 2;
    }
}
