using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateAfter(typeof(ChunkMapSystem))]
[UpdateAfter(typeof(ItemTrackingSystem))]
[UpdateBefore(typeof(MiningSystem))]
[UpdateBefore(typeof(CrafterSystem))]
public partial class BeltMoveSystem : SystemBase
{
    private ChunkMapSystem _chunkMap;
    private readonly List<int2> _activeCellPositions = new();
    private readonly List<Entity> _itemsInCell = new();
    private readonly List<ActiveBeltCell> _activeCells = new();
    private readonly List<ItemSpatialEntry> _itemSnapshots = new();

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

        BuildActiveCellSnapshots();

        if (_activeCells.Count == 0)
            return;

        NativeArray<ActiveBeltCell> activeCells =
            new NativeArray<ActiveBeltCell>(_activeCells.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        NativeArray<ItemSpatialEntry> itemSnapshots =
            new NativeArray<ItemSpatialEntry>(_itemSnapshots.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        NativeArray<MoveResult> moveResults =
            new NativeArray<MoveResult>(_itemSnapshots.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        for (int i = 0; i < _activeCells.Count; i++)
            activeCells[i] = _activeCells[i];

        for (int i = 0; i < _itemSnapshots.Count; i++)
            itemSnapshots[i] = _itemSnapshots[i];

        MoveActiveBeltCellsJob job = new MoveActiveBeltCellsJob
        {
            deltaTime = SystemAPI.Time.DeltaTime,
            itemSpacingSq = GameConstants.itemSpacing * GameConstants.itemSpacing,
            activeCells = activeCells,
            itemSnapshots = itemSnapshots,
            moveResults = moveResults,
            transforms = SystemAPI.GetComponentLookup<LocalTransform>(),
            cellChanged = SystemAPI.GetComponentLookup<ItemCellChanged>()
        };

        Dependency = job.Schedule(activeCells.Length, 1, Dependency);
        Dependency = activeCells.Dispose(Dependency);
        Dependency = itemSnapshots.Dispose(Dependency);
        Dependency = moveResults.Dispose(Dependency);
    }

    private void BuildActiveCellSnapshots()
    {
        _activeCells.Clear();
        _itemSnapshots.Clear();
        _chunkMap.CopyActiveBeltCells(_activeCellPositions);

        for (int i = 0; i < _activeCellPositions.Count; i++)
        {
            int2 cell = _activeCellPositions[i];

            Entity beltEntity;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            bool hasIndexedBelt = _chunkMap.TryGetBelt(cell, out beltEntity);
            bool isValidBelt = hasIndexedBelt &&
                               EntityManager.Exists(beltEntity) &&
                               EntityManager.HasComponent<Belt>(beltEntity) &&
                               EntityManager.HasComponent<Direction>(beltEntity);

            UnityEngine.Assertions.Assert.IsTrue(
                isValidBelt,
                $"Active belt cell contains an invalid belt index. Cell: {cell}, Entity: {beltEntity}");

            if (!isValidBelt)
                continue;
#else
            _chunkMap.TryGetBelt(cell, out beltEntity);
#endif

            Belt belt = EntityManager.GetComponentData<Belt>(beltEntity);
            Direction direction = EntityManager.GetComponentData<Direction>(beltEntity);
            int currentStartIndex = _itemSnapshots.Count;
            AppendCellSnapshot(cell);
            int currentItemCount = _itemSnapshots.Count - currentStartIndex;

            if (currentItemCount == 0)
                continue;

            int2 nextCell = cell + direction.dir.ToInt2();
            int nextStartIndex = _itemSnapshots.Count;
            AppendCellSnapshot(nextCell);

            _activeCells.Add(new ActiveBeltCell
            {
                cell = cell,
                direction = direction.dir,
                speed = belt.speed,
                currentStartIndex = currentStartIndex,
                currentItemCount = currentItemCount,
                nextStartIndex = nextStartIndex,
                nextItemCount = _itemSnapshots.Count - nextStartIndex
            });
        }
    }

    private void AppendCellSnapshot(int2 cell)
    {
        _chunkMap.GetItems(cell, _itemsInCell);

        for (int i = 0; i < _itemsInCell.Count; i++)
        {
            Entity item = _itemsInCell[i];

            _itemSnapshots.Add(new ItemSpatialEntry
            {
                entity = item,
                position = EntityManager.GetComponentData<LocalTransform>(item).Position
            });
        }
    }

    private struct ActiveBeltCell
    {
        public int2 cell;
        public DirectionEnum direction;
        public float speed;
        public int currentStartIndex;
        public int currentItemCount;
        public int nextStartIndex;
        public int nextItemCount;
    }

    private struct ItemSpatialEntry
    {
        public Entity entity;
        public float3 position;
    }

    private struct MoveResult
    {
        public bool isAligning;
        public float movedDistance;
        public DirectionEnum moveDirection;
        public float alignmentDistanceSq;
        public float3 alignmentTarget;
    }

    [BurstCompile]
    private struct MoveActiveBeltCellsJob : IJobParallelFor
    {
        public float deltaTime;
        public float itemSpacingSq;

        [ReadOnly]
        public NativeArray<ActiveBeltCell> activeCells;

        [ReadOnly]
        public NativeArray<ItemSpatialEntry> itemSnapshots;

        [NativeDisableParallelForRestriction]
        public NativeArray<MoveResult> moveResults;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<LocalTransform> transforms;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<ItemCellChanged> cellChanged;

        public void Execute(int index)
        {
            ActiveBeltCell activeCell = activeCells[index];
            int endIndex = activeCell.currentStartIndex + activeCell.currentItemCount;

            for (int itemIndex = activeCell.currentStartIndex; itemIndex < endIndex; itemIndex++)
            {
                ItemSpatialEntry item = itemSnapshots[itemIndex];

                LocalTransform transform = transforms[item.entity];
                MoveResult moveResult = MoveItemOnBelt(
                    item.entity,
                    ref transform,
                    item.position,
                    activeCell);
                moveResults[itemIndex] = moveResult;
                transforms[item.entity] = transform;

                if (!transform.Position.ToGridCell().Equals(activeCell.cell))
                    cellChanged.SetComponentEnabled(item.entity, true);
            }

            if (!TryFindAlignmentStuck(
                    activeCell,
                    out Entity closestAligningEntity,
                    out float3 closestAlignmentTarget))
                return;

            if (HasItemAtAlignmentTarget(
                    activeCell,
                    closestAligningEntity,
                    closestAlignmentTarget))
                return;

            LocalTransform closestTransform = transforms[closestAligningEntity];
            closestTransform.Position = closestAlignmentTarget;
            transforms[closestAligningEntity] = closestTransform;

            if (!closestTransform.Position.ToGridCell().Equals(activeCell.cell))
                cellChanged.SetComponentEnabled(closestAligningEntity, true);
        }

        private bool TryFindAlignmentStuck(
            ActiveBeltCell activeCell,
            out Entity closestEntity,
            out float3 closestAlignmentTarget)
        {
            closestEntity = Entity.Null;
            closestAlignmentTarget = default;

            if (activeCell.speed * deltaTime <= GameConstants.alignmentEpsilon)
                return false;

            float closestAlignmentDistanceSq = float.MaxValue;
            int endIndex = activeCell.currentStartIndex + activeCell.currentItemCount;

            for (int itemIndex = activeCell.currentStartIndex; itemIndex < endIndex; itemIndex++)
            {
                MoveResult moveResult = moveResults[itemIndex];

                if (moveResult.movedDistance > GameConstants.alignmentEpsilon)
                    continue;

                for (int otherIndex = itemIndex + 1; otherIndex < endIndex; otherIndex++)
                {
                    MoveResult otherMoveResult = moveResults[otherIndex];

                    if (otherMoveResult.movedDistance > GameConstants.alignmentEpsilon ||
                        (!moveResult.isAligning && !otherMoveResult.isAligning) ||
                        math.dot(
                            moveResult.moveDirection.ToInt2(),
                            otherMoveResult.moveDirection.ToInt2()) > 0)
                        continue;

                    if (moveResult.isAligning)
                    {
                        SelectCloserAlignmentItem(
                            itemSnapshots[itemIndex],
                            moveResult,
                            ref closestEntity,
                            ref closestAlignmentTarget,
                            ref closestAlignmentDistanceSq);
                    }

                    if (otherMoveResult.isAligning)
                    {
                        SelectCloserAlignmentItem(
                            itemSnapshots[otherIndex],
                            otherMoveResult,
                            ref closestEntity,
                            ref closestAlignmentTarget,
                            ref closestAlignmentDistanceSq);
                    }
                }
            }

            return closestEntity != Entity.Null;
        }

        private bool HasItemAtAlignmentTarget(
            ActiveBeltCell activeCell,
            Entity aligningEntity,
            float3 alignmentTarget)
        {
            float clearanceSq =
                GameConstants.alignmentEpsilon * GameConstants.alignmentEpsilon;
            int endIndex = activeCell.currentStartIndex + activeCell.currentItemCount;

            for (int itemIndex = activeCell.currentStartIndex; itemIndex < endIndex; itemIndex++)
            {
                Entity otherEntity = itemSnapshots[itemIndex].entity;

                if (otherEntity == aligningEntity)
                    continue;

                float3 otherPosition = transforms[otherEntity].Position;

                if (math.distancesq(alignmentTarget.xy, otherPosition.xy) <= clearanceSq)
                    return true;
            }

            return false;
        }

        private static void SelectCloserAlignmentItem(
            ItemSpatialEntry item,
            MoveResult moveResult,
            ref Entity closestEntity,
            ref float3 closestAlignmentTarget,
            ref float closestAlignmentDistanceSq)
        {
            float distanceEpsilonSq =
                GameConstants.alignmentEpsilon * GameConstants.alignmentEpsilon;
            bool isSameDistance =
                math.abs(moveResult.alignmentDistanceSq - closestAlignmentDistanceSq) <=
                distanceEpsilonSq;
            bool isCloser =
                moveResult.alignmentDistanceSq < closestAlignmentDistanceSq - distanceEpsilonSq;

            if (!isCloser &&
                (!isSameDistance || (closestEntity != Entity.Null && item.entity.Index >= closestEntity.Index)))
                return;

            closestEntity = item.entity;
            closestAlignmentTarget = moveResult.alignmentTarget;
            closestAlignmentDistanceSq = moveResult.alignmentDistanceSq;
        }

        private MoveResult MoveItemOnBelt(
            Entity entity,
            ref LocalTransform transform,
            float3 itemPosition,
            ActiveBeltCell activeCell)
        {
            float3 alignmentTarget = GetAlignmentTarget(itemPosition, activeCell.cell, activeCell.direction);
            float alignmentDistanceSq = math.distancesq(itemPosition, alignmentTarget);

            if (alignmentDistanceSq >
                GameConstants.alignmentEpsilon * GameConstants.alignmentEpsilon)
            {
                return new MoveResult
                {
                    isAligning = true,
                    movedDistance = MoveToPosition(
                        entity,
                        ref transform,
                        itemPosition,
                        alignmentTarget,
                        activeCell),
                    moveDirection = DirectionExtension.GetMoveDirection(itemPosition, alignmentTarget),
                    alignmentDistanceSq = alignmentDistanceSq,
                    alignmentTarget = alignmentTarget
                };
            }

            int2 targetCell = activeCell.cell + activeCell.direction.ToInt2();
            float3 targetPosition = new float3(targetCell.x, targetCell.y, itemPosition.z);
            return new MoveResult
            {
                movedDistance = MoveToPosition(entity, ref transform, itemPosition, targetPosition, activeCell),
                moveDirection = activeCell.direction
            };
        }

        private static float3 GetAlignmentTarget(float3 itemPosition, int2 itemCell, DirectionEnum direction)
        {
            switch (direction)
            {
                case DirectionEnum.Up:
                case DirectionEnum.Down:
                    return new float3(itemCell.x, itemPosition.y, itemPosition.z);
                case DirectionEnum.Left:
                case DirectionEnum.Right:
                    return new float3(itemPosition.x, itemCell.y, itemPosition.z);
                default:
                    return itemPosition;
            }
        }

        private float MoveToPosition(
            Entity entity,
            ref LocalTransform transform,
            float3 itemPosition,
            float3 targetPosition,
            ActiveBeltCell activeCell)
        {
            float3 offset = targetPosition - itemPosition;
            float distance = math.length(offset);

            if (distance == 0f)
            {
                transform.Position = targetPosition;
                return 0f;
            }

            float3 direction = offset / distance;
            float moveDistance = math.min(distance, activeCell.speed * deltaTime);
            float2 current = itemPosition.ToFloat2();
            float2 moveDirection = direction.ToFloat2();

            moveDistance = LimitMoveDistanceInRange(
                moveDistance,
                entity,
                current,
                moveDirection,
                activeCell.currentStartIndex,
                activeCell.currentItemCount);

            int2 targetCell = targetPosition.ToGridCell();
            if (moveDistance > 0f && !activeCell.cell.Equals(targetCell))
            {
                moveDistance = LimitMoveDistanceInRange(
                    moveDistance,
                    entity,
                    current,
                    moveDirection,
                    activeCell.nextStartIndex,
                    activeCell.nextItemCount);
            }

            if (moveDistance > 0f)
                transform.Position = itemPosition + direction * moveDistance;

            return moveDistance;
        }

        private float LimitMoveDistanceInRange(
            float moveDistance,
            Entity entity,
            float2 current,
            float2 direction,
            int startIndex,
            int itemCount)
        {
            int endIndex = startIndex + itemCount;

            for (int i = startIndex; i < endIndex; i++)
            {
                ItemSpatialEntry otherItem = itemSnapshots[i];

                if (otherItem.entity == entity)
                    continue;

                moveDistance = LimitMoveDistance(moveDistance, current, direction, otherItem.position.ToFloat2());
            }

            return moveDistance;
        }

        private float LimitMoveDistance(float moveDistance, float2 current, float2 direction, float2 otherPosition)
        {
            float2 toOther = otherPosition - current;
            float projectedDistance = math.dot(toOther, direction);

            if (projectedDistance <= 0f)
                return moveDistance;

            float perpendicularDistanceSq = math.lengthsq(toOther - direction * projectedDistance);

            if (perpendicularDistanceSq >= itemSpacingSq)
                return moveDistance;

            float allowedDistance = projectedDistance - math.sqrt(itemSpacingSq - perpendicularDistanceSq);
            return math.min(moveDistance, math.max(0f, allowedDistance));
        }
    }
}
