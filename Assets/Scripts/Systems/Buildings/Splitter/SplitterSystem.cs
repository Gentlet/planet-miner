using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateAfter(typeof(BeltMoveSystem))]
[UpdateBefore(typeof(MiningSystem))]
public partial class SplitterSystem : SystemBase
{
    private ChunkMapSystem _chunkMap;
    private ItemTrackingSystem _itemTracking;
    private readonly List<Entity> _itemsInCell = new();
    private readonly List<InputItem> _inputItems = new();

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
        _itemTracking = World.GetExistingSystemManaged<ItemTrackingSystem>();
        RequireForUpdate<Splitter>();
    }

    protected override void OnUpdate()
    {
        if (!EnsureSystems())
            return;

        _itemTracking.ApplyPendingChangesImmediate();

        foreach (var (splitter, gridPosition, direction) in
                 SystemAPI.Query<RefRW<Splitter>, RefRO<GridPosition>, RefRO<Direction>>())
        {
            int2 splitterCell = gridPosition.ValueRO.gridPosition;
            DirectionEnum forward = direction.ValueRO.dir;

            BuildInputItems(splitterCell, forward);

            if (_inputItems.Count == 0)
                continue;

            Splitter splitterData = splitter.ValueRO;
            splitterData.nextOutputDirection =
                NormalizeOutputDirection(splitterData.nextOutputDirection, forward);

            for (int i = 0; i < _inputItems.Count; i++)
            {
                if (!TryOutputItem(
                        _inputItems[i].entity,
                        splitterCell,
                        forward,
                        ref splitterData))
                    break;
            }

            splitter.ValueRW = splitterData;
        }
    }

    private void BuildInputItems(int2 splitterCell, DirectionEnum forward)
    {
        _inputItems.Clear();
        _chunkMap.GetItems(splitterCell, _itemsInCell);

        int2 forwardOffset = forward.ToInt2();

        for (int i = 0; i < _itemsInCell.Count; i++)
        {
            Entity itemEntity = _itemsInCell[i];
            float3 position = EntityManager.GetComponentData<LocalTransform>(itemEntity).Position;

            _inputItems.Add(new InputItem
            {
                entity = itemEntity,
                progress = position.x * forwardOffset.x + position.y * forwardOffset.y
            });
        }

        _inputItems.Sort(CompareInputItems);
    }

    private bool TryOutputItem(
        Entity itemEntity,
        int2 splitterCell,
        DirectionEnum forward,
        ref Splitter splitter)
    {
        DirectionEnum outputDirection = splitter.nextOutputDirection;

        for (int i = 0; i < 3; i++)
        {
            int2 outputCell = splitterCell + outputDirection.ToInt2();

            if (_chunkMap.TryGetBelt(outputCell, out _))
            {
                LocalTransform transform =
                    EntityManager.GetComponentData<LocalTransform>(itemEntity);
                float3 outputPosition =
                    new float3(outputCell.x, outputCell.y, transform.Position.z);

                if (_itemTracking.TryMoveItemImmediate(
                        itemEntity,
                        outputCell,
                        outputPosition))
                {
                    splitter.nextOutputDirection =
                        GetNextOutputDirection(outputDirection, forward);
                    return true;
                }
            }

            outputDirection = GetNextOutputDirection(outputDirection, forward);
        }

        return false;
    }

    private static DirectionEnum NormalizeOutputDirection(
        DirectionEnum outputDirection,
        DirectionEnum forward)
    {
        DirectionEnum right = forward.NextDirection();
        DirectionEnum left = right.NextDirection().NextDirection();

        if (outputDirection == forward ||
            outputDirection == right ||
            outputDirection == left)
            return outputDirection;

        return forward;
    }

    private static DirectionEnum GetNextOutputDirection(
        DirectionEnum outputDirection,
        DirectionEnum forward)
    {
        DirectionEnum right = forward.NextDirection();
        DirectionEnum left = right.NextDirection().NextDirection();

        if (outputDirection == forward)
            return right;
        if (outputDirection == right)
            return left;

        return forward;
    }

    private static int CompareInputItems(InputItem first, InputItem second)
    {
        int progressComparison = second.progress.CompareTo(first.progress);

        if (progressComparison != 0)
            return progressComparison;

        int indexComparison = first.entity.Index.CompareTo(second.entity.Index);
        return indexComparison != 0
            ? indexComparison
            : first.entity.Version.CompareTo(second.entity.Version);
    }

    private bool EnsureSystems()
    {
        if (_chunkMap == null)
            _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();

        if (_itemTracking == null)
            _itemTracking = World.GetExistingSystemManaged<ItemTrackingSystem>();

        return _chunkMap != null && _itemTracking != null;
    }

    private struct InputItem
    {
        public Entity entity;
        public float progress;
    }
}
