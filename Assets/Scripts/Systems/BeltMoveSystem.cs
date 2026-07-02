using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateAfter(typeof(ChunkMapSystem))]
public partial class BeltMoveSystem : SystemBase
{
    private const float itemSpacing = 0.25f;
    private const float alignmentEpsilon = 0.001f;
    private ChunkMapSystem _chunkMap;
    private EntityQuery _itemsQuery;

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
        _itemsQuery = SystemAPI.QueryBuilder()
            .WithAll<Item, LocalTransform>()
            .Build();
        RequireForUpdate<Item>();
    }

    protected override void OnUpdate()
    {
        if (_chunkMap == null)
        {
            _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
            if (_chunkMap == null)
                return;
        }

        int itemCount = _itemsQuery.CalculateEntityCount();
        if (itemCount == 0)
            return;

        NativeParallelMultiHashMap<int2, ItemSpatialEntry> itemsByCell = new NativeParallelMultiHashMap<int2, ItemSpatialEntry>(itemCount, Allocator.TempJob);

        BuildItemsByCellJob buildItemsByCellJob = new BuildItemsByCellJob
        {
            itemsByCell = itemsByCell.AsParallelWriter()
        };

        MoveItemsOnBeltsJob job = new MoveItemsOnBeltsJob
        {
            deltaTime = SystemAPI.Time.DeltaTime,
            itemSpacingSq = itemSpacing * itemSpacing,
            beltCells = _chunkMap.GetBeltCellsReadOnly(),
            itemsByCell = itemsByCell,
            belts = SystemAPI.GetComponentLookup<Belt>(true),
            directions = SystemAPI.GetComponentLookup<Direction>(true)
        };

        Dependency = buildItemsByCellJob.ScheduleParallel(Dependency);
        Dependency = job.ScheduleParallel(Dependency);
        Dependency = itemsByCell.Dispose(Dependency);
        Dependency.Complete();
    }

    private struct ItemSpatialEntry
    {
        public Entity entity;
        public float2 position;
    }

    [BurstCompile]
    private partial struct BuildItemsByCellJob : IJobEntity
    {
        public NativeParallelMultiHashMap<int2, ItemSpatialEntry>.ParallelWriter itemsByCell;

        private void Execute(Entity entity, in LocalTransform transform, in Item item)
        {
            float3 position = transform.Position;
            int2 cell = position.ToGridCell();

            itemsByCell.Add(cell, new ItemSpatialEntry
            {
                entity = entity,
                position = position.ToFloat2()
            });
        }
    }

    [BurstCompile]
    private partial struct MoveItemsOnBeltsJob : IJobEntity
    {
        public float deltaTime;
        public float itemSpacingSq;

        [ReadOnly]
        public NativeParallelHashMap<int2, Entity>.ReadOnly beltCells;

        [ReadOnly]
        public NativeParallelMultiHashMap<int2, ItemSpatialEntry> itemsByCell;

        [ReadOnly]
        public ComponentLookup<Belt> belts;

        [ReadOnly]
        public ComponentLookup<Direction> directions;

        private void Execute(Entity entity, ref LocalTransform transform, in Item item)
        {
            float3 itemPos = transform.Position;
            int2 itemCell = itemPos.ToGridCell();

            if (!itemPos.IsInsideCell(itemCell))
                return;

            if (!TryGetBelt(itemCell, out Belt belt, out Direction direction))
                return;

            MoveAlongBelt(entity, ref transform, itemPos, itemCell, belt, direction);
        }

        private bool TryGetBelt(int2 cell, out Belt belt, out Direction direction)
        {
            if (!beltCells.TryGetValue(cell, out Entity beltEntity) ||
                !belts.HasComponent(beltEntity) ||
                !directions.HasComponent(beltEntity))
            {
                belt = default;
                direction = default;
                return false;
            }

            belt = belts[beltEntity];
            direction = directions[beltEntity];
            return true;
        }

        private void MoveAlongBelt(Entity entity, ref LocalTransform transform, float3 itemPos, int2 itemCell, Belt belt, Direction beltDirection)
        {
            float3 alignmentTarget = GetAlignmentTarget(itemPos, itemCell, beltDirection);

            if (math.distancesq(itemPos, alignmentTarget) > alignmentEpsilon * alignmentEpsilon)
            {
                MoveToPosition(entity, ref transform, itemPos, alignmentTarget, belt.speed);
                return;
            }

            int2 targetCell = itemCell + beltDirection.dir.ToInt2();
            float3 targetPosition = new float3(targetCell.x, targetCell.y, itemPos.z);

            MoveToPosition(entity, ref transform, itemPos, targetPosition, belt.speed);
        }

        private float3 GetAlignmentTarget(float3 itemPos, int2 itemCell, Direction beltDirection)
        {
            switch (beltDirection.dir)
            {
                case DirectionEnum.Up:
                case DirectionEnum.Down:
                    return new float3(itemCell.x, itemPos.y, itemPos.z);
                case DirectionEnum.Left:
                case DirectionEnum.Right:
                    return new float3(itemPos.x, itemCell.y, itemPos.z);
                default:
                    return itemPos;
            }
        }

        private void MoveToPosition(Entity entity, ref LocalTransform transform, float3 itemPos, float3 targetPosition, float speed)
        {
            float3 offset = targetPosition - itemPos;
            float distance = math.length(offset);

            if (distance == 0f)
            {
                transform.Position = targetPosition;
                return;
            }

            float3 direction = offset / distance;
            float moveDistance = math.min(distance, speed * deltaTime);
            int2 currentCell = itemPos.ToGridCell();
            int2 targetCell = targetPosition.ToGridCell();
            float2 current = itemPos.ToFloat2();
            float2 moveDirection = direction.ToFloat2();

            moveDistance = LimitMoveDistanceInCell(moveDistance, entity, current, moveDirection, currentCell);

            if (moveDistance > 0f && !currentCell.Equals(targetCell))
                moveDistance = LimitMoveDistanceInCell(moveDistance, entity, current, moveDirection, targetCell);

            if (moveDistance > 0f)
                transform.Position = itemPos + direction * moveDistance;
        }

        private float LimitMoveDistanceInCell(float moveDistance, Entity entity, float2 current, float2 direction, int2 cell)
        {
            NativeParallelMultiHashMapIterator<int2> itemIterator;
            ItemSpatialEntry itemEntry;

            if (itemsByCell.TryGetFirstValue(cell, out itemEntry, out itemIterator))
            {
                do
                {
                    if (itemEntry.entity == entity)
                        continue;

                    moveDistance = LimitMoveDistance(moveDistance, current, direction, itemEntry.position);
                }
                while (itemsByCell.TryGetNextValue(out itemEntry, ref itemIterator));
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
