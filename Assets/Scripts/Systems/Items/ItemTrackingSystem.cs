using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateAfter(typeof(BeltMoveSystem))]
public partial class ItemTrackingSystem : SystemBase
{
    private ChunkMapSystem _chunkMap;
    private bool _hasInitializeItems;

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
    }

    protected override void OnUpdate()
    {
        if (_chunkMap == null)
        {
            _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
            if (_chunkMap == null)
                return;
        }

        if (!_hasInitializeItems)
        {
            InitializeItems();
            _hasInitializeItems = true;
        }

        foreach (var (transform, gridPosition, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRW<GridPosition>>().WithAll<Item>().WithNone<DepositedItem>().WithEntityAccess())
        {
            int2 cell = transform.ValueRO.Position.ToGridCell();

            if (cell.Equals(gridPosition.ValueRO.gridPosition))
            {
                _chunkMap.RegisterItem(cell, entity);
                continue;
            }

            _chunkMap.TryUnregisterItem(gridPosition.ValueRO.gridPosition, entity);
            _chunkMap.RegisterItem(cell, entity);
            gridPosition.ValueRW.gridPosition = cell;
        }
    }

    private void InitializeItems()
    {
        foreach (var (transform, gridPosition, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRW<GridPosition>>().WithAll<Item>().WithNone<DepositedItem>().WithEntityAccess())
        {
            int2 cell = transform.ValueRO.Position.ToGridCell();

            _chunkMap.RegisterItem(cell, entity);
            gridPosition.ValueRW.gridPosition = cell;
        }
    }
}
