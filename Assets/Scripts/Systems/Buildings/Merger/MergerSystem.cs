using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateAfter(typeof(BeltMoveSystem))]
[UpdateAfter(typeof(SplitterSystem))]
[UpdateBefore(typeof(MiningSystem))]
public partial class MergerSystem : SystemBase
{
    private ChunkMapSystem _chunkMap;
    private ItemTrackingSystem _itemTracking;
    private readonly List<Entity> _itemsInCell = new();
    private readonly List<InputItem> _inputItems = new();

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
        _itemTracking = World.GetExistingSystemManaged<ItemTrackingSystem>();
        RequireForUpdate<Merger>();
    }

    protected override void OnUpdate()
    {
        if (!EnsureSystems())
            return;

        _itemTracking.ApplyPendingChangesImmediate();

        foreach (var (merger, gridPosition, direction, transform) in
                 SystemAPI.Query<
                     RefRW<Merger>,
                     RefRO<GridPosition>,
                     RefRW<Direction>,
                     RefRW<LocalTransform>>())
        {
            int2 mergerCell = gridPosition.ValueRO.gridPosition;
            Merger mergerData = merger.ValueRO;
            Entity previousOutputBelt = mergerData.outputBelt;

            if (!TryResolveOutput(
                    mergerCell,
                    ref mergerData,
                    direction.ValueRO.dir,
                    out DirectionEnum forward))
            {
                merger.ValueRW = mergerData;
                continue;
            }

            if (mergerData.outputBelt != previousOutputBelt ||
                direction.ValueRO.dir != forward)
            {
                direction.ValueRW.dir = forward;
                transform.ValueRW.Rotation = quaternion.RotateZ(
                    math.radians((float)forward.ToDegrees()));
            }

            int2 outputCell = mergerCell + forward.ToInt2();

            BuildInputItems(mergerCell, forward);

            if (_inputItems.Count == 0)
            {
                merger.ValueRW = mergerData;
                continue;
            }

            mergerData.nextInputDirection =
                NormalizeInputDirection(mergerData.nextInputDirection, forward);

            while (_inputItems.Count > 0)
            {
                if (!TryFindInputItem(
                        mergerData.nextInputDirection,
                        forward,
                        out int inputItemIndex,
                        out DirectionEnum inputDirection))
                    break;

                Entity itemEntity = _inputItems[inputItemIndex].entity;
                LocalTransform itemTransform =
                    EntityManager.GetComponentData<LocalTransform>(itemEntity);
                float3 outputPosition =
                    new float3(
                        outputCell.x,
                        outputCell.y,
                        itemTransform.Position.z);

                if (!_itemTracking.TryMoveItemImmediate(
                        itemEntity,
                        outputCell,
                        outputPosition))
                    break;

                _inputItems.RemoveAt(inputItemIndex);
                mergerData.nextInputDirection =
                    GetNextInputDirection(inputDirection, forward);
            }

            merger.ValueRW = mergerData;
        }
    }

    private bool TryResolveOutput(
        int2 mergerCell,
        ref Merger merger,
        DirectionEnum currentDirection,
        out DirectionEnum outputDirection)
    {
        if (IsSelectedOutputValid(
                mergerCell,
                merger.outputBelt,
                currentDirection))
        {
            outputDirection = currentDirection;
            return true;
        }

        merger.outputBelt = Entity.Null;

        if (!TryFindOldestOutputBelt(
                mergerCell,
                out Entity outputBelt,
                out outputDirection))
            return false;

        merger.outputBelt = outputBelt;
        merger.nextInputDirection = outputDirection
            .NextDirection()
            .NextDirection();
        return true;
    }

    private bool IsSelectedOutputValid(
        int2 mergerCell,
        Entity outputBelt,
        DirectionEnum outputDirection)
    {
        if (outputBelt == Entity.Null)
            return false;

        int2 outputCell = mergerCell + outputDirection.ToInt2();

        if (!_chunkMap.TryGetBelt(outputCell, out Entity indexedBelt))
            return false;

        if (indexedBelt != outputBelt)
            return false;

        return IsBeltPointing(outputBelt, outputDirection);
    }

    private bool TryFindOldestOutputBelt(
        int2 mergerCell,
        out Entity outputBelt,
        out DirectionEnum outputDirection)
    {
        outputBelt = Entity.Null;
        outputDirection = DirectionEnum.Up;
        ulong oldestInstallationOrder = ulong.MaxValue;

        for (int directionIndex = 0;
             directionIndex < (int)DirectionEnum.Count;
             directionIndex++)
        {
            DirectionEnum candidateDirection =
                (DirectionEnum)directionIndex;
            int2 candidateCell =
                mergerCell + candidateDirection.ToInt2();

            if (!_chunkMap.TryGetBelt(
                    candidateCell,
                    out Entity candidateBelt))
                continue;

            if (!IsBeltPointing(candidateBelt, candidateDirection))
                continue;

            Belt belt = EntityManager.GetComponentData<Belt>(candidateBelt);

            if (belt.installationOrder >= oldestInstallationOrder)
                continue;

            outputBelt = candidateBelt;
            outputDirection = candidateDirection;
            oldestInstallationOrder = belt.installationOrder;
        }

        return outputBelt != Entity.Null;
    }

    private void BuildInputItems(int2 mergerCell, DirectionEnum forward)
    {
        _inputItems.Clear();
        _chunkMap.GetItems(mergerCell, _itemsInCell);

        float2 cellCenter = new float2(mergerCell.x, mergerCell.y);
        DirectionEnum back = forward.NextDirection().NextDirection();
        DirectionEnum left = back.NextDirection();
        DirectionEnum right = forward.NextDirection();
        float2 forwardOffset = forward.ToInt2();
        float2 backOffset = back.ToInt2();
        float2 leftOffset = left.ToInt2();
        float2 rightOffset = right.ToInt2();

        for (int i = 0; i < _itemsInCell.Count; i++)
        {
            Entity itemEntity = _itemsInCell[i];
            float3 position =
                EntityManager.GetComponentData<LocalTransform>(itemEntity).Position;
            float2 relativePosition = position.xy - cellCenter;

            DirectionEnum inputDirection = back;
            float inputScore = math.dot(relativePosition, backOffset);
            float leftScore = math.dot(relativePosition, leftOffset);
            float rightScore = math.dot(relativePosition, rightOffset);

            if (leftScore > inputScore)
            {
                inputDirection = left;
                inputScore = leftScore;
            }

            if (rightScore > inputScore)
            {
                inputDirection = right;
                inputScore = rightScore;
            }

            if (math.dot(relativePosition, forwardOffset) > inputScore)
                continue;

            if (!IsInputBeltConnected(mergerCell, inputDirection))
                continue;

            _inputItems.Add(new InputItem
            {
                entity = itemEntity,
                inputDirection = inputDirection,
                distanceSq = math.lengthsq(relativePosition)
            });
        }
    }

    private bool IsInputBeltConnected(
        int2 mergerCell,
        DirectionEnum inputDirection)
    {
        int2 inputCell = mergerCell + inputDirection.ToInt2();

        if (!_chunkMap.TryGetBelt(inputCell, out Entity inputBelt))
            return false;

        DirectionEnum directionTowardMerger = inputDirection
            .NextDirection()
            .NextDirection();
        return IsBeltPointing(inputBelt, directionTowardMerger);
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

    private bool TryFindInputItem(
        DirectionEnum startDirection,
        DirectionEnum forward,
        out int inputItemIndex,
        out DirectionEnum inputDirection)
    {
        inputDirection = startDirection;

        for (int i = 0; i < 3; i++)
        {
            inputItemIndex = FindClosestInputItem(inputDirection);

            if (inputItemIndex >= 0)
                return true;

            inputDirection = GetNextInputDirection(inputDirection, forward);
        }

        inputItemIndex = -1;
        return false;
    }

    private int FindClosestInputItem(DirectionEnum inputDirection)
    {
        int closestIndex = -1;

        for (int i = 0; i < _inputItems.Count; i++)
        {
            InputItem candidate = _inputItems[i];

            if (candidate.inputDirection != inputDirection)
                continue;

            if (closestIndex < 0 ||
                CompareInputItems(candidate, _inputItems[closestIndex]) < 0)
                closestIndex = i;
        }

        return closestIndex;
    }

    private static DirectionEnum NormalizeInputDirection(
        DirectionEnum inputDirection,
        DirectionEnum forward)
    {
        DirectionEnum right = forward.NextDirection();
        DirectionEnum back = right.NextDirection();
        DirectionEnum left = back.NextDirection();

        if (inputDirection == back ||
            inputDirection == left ||
            inputDirection == right)
            return inputDirection;

        return back;
    }

    private static DirectionEnum GetNextInputDirection(
        DirectionEnum inputDirection,
        DirectionEnum forward)
    {
        DirectionEnum right = forward.NextDirection();
        DirectionEnum back = right.NextDirection();
        DirectionEnum left = back.NextDirection();

        if (inputDirection == back)
            return left;
        if (inputDirection == left)
            return right;

        return back;
    }

    private static int CompareInputItems(InputItem first, InputItem second)
    {
        int distanceComparison = first.distanceSq.CompareTo(second.distanceSq);

        if (distanceComparison != 0)
            return distanceComparison;

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
        public DirectionEnum inputDirection;
        public float distanceSq;
    }
}
