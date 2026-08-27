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

        foreach (var (splitter, gridPosition, direction, transform, splitterEntity) in
                 SystemAPI.Query<
                         RefRW<Splitter>,
                         RefRO<GridPosition>,
                         RefRW<Direction>,
                         RefRW<LocalTransform>>()
                     .WithEntityAccess())
        {
            int2 splitterCell = gridPosition.ValueRO.gridPosition;
            Splitter splitterData = splitter.ValueRO;
            Entity previousInputBelt = splitterData.inputBelt;
            DynamicBuffer<SplitterRetainedItemElement> retainedItems =
                EntityManager.GetBuffer<SplitterRetainedItemElement>(
                    splitterEntity);

            if (previousInputBelt != Entity.Null &&
                !IsSelectedInputValid(
                    splitterCell,
                    previousInputBelt,
                    direction.ValueRO.dir))
            {
                RetainCurrentItems(splitterCell, retainedItems);
            }

            bool hasInput = TryResolveInput(
                splitterCell,
                ref splitterData,
                direction.ValueRO.dir,
                out DirectionEnum forward);

            if (splitterData.inputBelt != previousInputBelt ||
                direction.ValueRO.dir != forward)
            {
                direction.ValueRW.dir = forward;
                transform.ValueRW.Rotation = quaternion.RotateZ(
                    math.radians((float)forward.ToDegrees()));
            }

            RemoveInvalidRetainedItems(splitterCell, retainedItems);

            BuildInputItems(
                splitterCell,
                forward,
                hasInput,
                retainedItems);

            if (_inputItems.Count == 0)
            {
                splitter.ValueRW = splitterData;
                continue;
            }

            splitterData.nextOutputDirection =
                NormalizeOutputDirection(splitterData.nextOutputDirection, forward);

            for (int i = 0; i < _inputItems.Count; i++)
            {
                Entity itemEntity = _inputItems[i].entity;

                if (!TryOutputItem(
                        itemEntity,
                        splitterCell,
                        forward,
                        ref splitterData))
                    break;

                RemoveRetainedItem(itemEntity, retainedItems);
            }

            splitter.ValueRW = splitterData;
        }
    }

    private bool TryResolveInput(
        int2 splitterCell,
        ref Splitter splitter,
        DirectionEnum currentDirection,
        out DirectionEnum inputDirection)
    {
        if (IsSelectedInputValid(
                splitterCell,
                splitter.inputBelt,
                currentDirection))
        {
            inputDirection = currentDirection;
            return true;
        }

        splitter.inputBelt = Entity.Null;

        if (!TryFindOldestInputBelt(
                splitterCell,
                out Entity inputBelt,
                out inputDirection))
        {
            inputDirection = currentDirection;
            return false;
        }

        splitter.inputBelt = inputBelt;
        splitter.nextOutputDirection = inputDirection;
        return true;
    }

    private bool IsSelectedInputValid(
        int2 splitterCell,
        Entity inputBelt,
        DirectionEnum inputDirection)
    {
        if (inputBelt == Entity.Null)
            return false;

        DirectionEnum inputSide = inputDirection
            .NextDirection()
            .NextDirection();
        int2 inputCell = splitterCell + inputSide.ToInt2();

        if (!_chunkMap.TryGetBelt(inputCell, out Entity indexedBelt))
            return false;

        if (indexedBelt != inputBelt)
            return false;

        return IsBeltPointing(inputBelt, inputDirection);
    }

    private bool TryFindOldestInputBelt(
        int2 splitterCell,
        out Entity inputBelt,
        out DirectionEnum inputDirection)
    {
        inputBelt = Entity.Null;
        inputDirection = DirectionEnum.Up;
        ulong oldestInstallationOrder = ulong.MaxValue;

        for (int directionIndex = 0;
             directionIndex < (int)DirectionEnum.Count;
             directionIndex++)
        {
            DirectionEnum inputSide = (DirectionEnum)directionIndex;
            int2 candidateCell = splitterCell + inputSide.ToInt2();

            if (!_chunkMap.TryGetBelt(
                    candidateCell,
                    out Entity candidateBelt))
                continue;

            DirectionEnum directionTowardSplitter = inputSide
                .NextDirection()
                .NextDirection();

            if (!IsBeltPointing(
                    candidateBelt,
                    directionTowardSplitter))
                continue;

            Belt belt = EntityManager.GetComponentData<Belt>(candidateBelt);

            if (belt.installationOrder >= oldestInstallationOrder)
                continue;

            inputBelt = candidateBelt;
            inputDirection = directionTowardSplitter;
            oldestInstallationOrder = belt.installationOrder;
        }

        return inputBelt != Entity.Null;
    }

    private void BuildInputItems(
        int2 splitterCell,
        DirectionEnum forward,
        bool hasInput,
        DynamicBuffer<SplitterRetainedItemElement> retainedItems)
    {
        _inputItems.Clear();
        _chunkMap.GetItems(splitterCell, _itemsInCell);

        int2 forwardOffset = forward.ToInt2();

        for (int i = 0; i < _itemsInCell.Count; i++)
        {
            Entity itemEntity = _itemsInCell[i];
            float3 position = EntityManager
                .GetComponentData<LocalTransform>(itemEntity)
                .Position;

            if (!ContainsRetainedItem(itemEntity, retainedItems) &&
                (!hasInput ||
                 !IsItemFromSelectedInput(
                     position.xy,
                     splitterCell,
                     forward)))
            {
                continue;
            }

            _inputItems.Add(new InputItem
            {
                entity = itemEntity,
                progress = position.x * forwardOffset.x + position.y * forwardOffset.y
            });
        }

        _inputItems.Sort(CompareInputItems);
    }

    private static bool IsItemFromSelectedInput(
        float2 itemPosition,
        int2 splitterCell,
        DirectionEnum forward)
    {
        float2 relativePosition =
            itemPosition - new float2(splitterCell.x, splitterCell.y);
        DirectionEnum back = forward.NextDirection().NextDirection();
        DirectionEnum right = forward.NextDirection();
        DirectionEnum left = right.NextDirection().NextDirection();
        float inputScore = math.dot(relativePosition, back.ToInt2());

        return inputScore >= math.dot(relativePosition, forward.ToInt2()) &&
               inputScore >= math.dot(relativePosition, right.ToInt2()) &&
               inputScore >= math.dot(relativePosition, left.ToInt2());
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

            if (_chunkMap.TryGetBelt(
                    outputCell,
                    out Entity outputBelt) &&
                IsBeltPointing(outputBelt, outputDirection))
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

    private void RetainCurrentItems(
        int2 splitterCell,
        DynamicBuffer<SplitterRetainedItemElement> retainedItems)
    {
        _chunkMap.GetItems(splitterCell, _itemsInCell);

        for (int i = 0; i < _itemsInCell.Count; i++)
        {
            Entity itemEntity = _itemsInCell[i];

            if (ContainsRetainedItem(itemEntity, retainedItems))
                continue;

            retainedItems.Add(new SplitterRetainedItemElement
            {
                itemEntity = itemEntity
            });
        }
    }

    private void RemoveInvalidRetainedItems(
        int2 splitterCell,
        DynamicBuffer<SplitterRetainedItemElement> retainedItems)
    {
        for (int i = retainedItems.Length - 1; i >= 0; i--)
        {
            Entity itemEntity = retainedItems[i].itemEntity;

            if (!EntityManager.Exists(itemEntity) ||
                !_chunkMap.TryGetRegisteredItemCell(
                    itemEntity,
                    out int2 itemCell) ||
                !itemCell.Equals(splitterCell))
            {
                retainedItems.RemoveAt(i);
            }
        }
    }

    private static bool ContainsRetainedItem(
        Entity itemEntity,
        DynamicBuffer<SplitterRetainedItemElement> retainedItems)
    {
        for (int i = 0; i < retainedItems.Length; i++)
        {
            if (retainedItems[i].itemEntity == itemEntity)
                return true;
        }

        return false;
    }

    private static void RemoveRetainedItem(
        Entity itemEntity,
        DynamicBuffer<SplitterRetainedItemElement> retainedItems)
    {
        for (int i = retainedItems.Length - 1; i >= 0; i--)
        {
            if (retainedItems[i].itemEntity == itemEntity)
                retainedItems.RemoveAt(i);
        }
    }

    private bool IsBeltPointing(
        Entity beltEntity,
        DirectionEnum direction)
    {
        if (!EntityManager.Exists(beltEntity))
            return false;

        if (!EntityManager.HasComponent<Belt>(beltEntity))
            return false;

        if (!EntityManager.HasComponent<Direction>(beltEntity))
            return false;

        return EntityManager.GetComponentData<Direction>(beltEntity).dir ==
               direction;
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
