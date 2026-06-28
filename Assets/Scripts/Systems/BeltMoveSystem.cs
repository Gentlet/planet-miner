using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateAfter(typeof(GridOccupancySystem))]
public partial class BeltMoveSystem : SystemBase
{
    private const float itemSpacing = 0.25f;
    private GridOccupancySystem _gridOccupancy;
    private EntityQuery _itemsQuery;

    protected override void OnCreate()
    {
        _gridOccupancy = World.GetExistingSystemManaged<GridOccupancySystem>();
        _itemsQuery = SystemAPI.QueryBuilder()
            .WithAll<Item, BeltMoveTarget, LocalTransform>()
            .Build();
        RequireForUpdate<Item>();
    }

    protected override void OnUpdate()
    {
        if (_gridOccupancy == null)
        {
            _gridOccupancy = World.GetExistingSystemManaged<GridOccupancySystem>();
            if (_gridOccupancy == null)
                return;
        }

        int itemCount = _itemsQuery.CalculateEntityCount();
        if (itemCount == 0)
            return;

        NativeArray<Entity> itemEntities = _itemsQuery.ToEntityArray(Allocator.TempJob);
        NativeArray<LocalTransform> itemTransforms = _itemsQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
        NativeParallelMultiHashMap<int2, ItemSpatialEntry> itemsByCell = new NativeParallelMultiHashMap<int2, ItemSpatialEntry>(itemCount, Allocator.TempJob);
        NativeParallelMultiHashMap<int2, float2> reservedPositionsByCell = new NativeParallelMultiHashMap<int2, float2>(itemCount, Allocator.TempJob);

        for (int i = 0; i < itemEntities.Length; i++)
        {
            float3 position = itemTransforms[i].Position;
            int2 cell = position.ToGridCell();
            itemsByCell.Add(cell, new ItemSpatialEntry
            {
                Entity = itemEntities[i],
                Position = new float2(position.x, position.y)
            });
        }

        MoveItemsOnBeltsJob job = new MoveItemsOnBeltsJob
        {
            DeltaTime = SystemAPI.Time.DeltaTime,
            ItemSpacing = itemSpacing,
            OccupiedCells = _gridOccupancy.GetOccupiedCellsReadOnly(),
            ItemsByCell = itemsByCell,
            ReservedPositionsByCell = reservedPositionsByCell,
            Belts = SystemAPI.GetComponentLookup<Belt>(true),
            Directions = SystemAPI.GetComponentLookup<Direction>(true)
        };

        Dependency = job.Schedule(Dependency);
        Dependency = itemEntities.Dispose(Dependency);
        Dependency = itemTransforms.Dispose(Dependency);
        Dependency = itemsByCell.Dispose(Dependency);
        Dependency = reservedPositionsByCell.Dispose(Dependency);
    }

    private struct ItemSpatialEntry
    {
        public Entity Entity;
        public float2 Position;
    }

    [BurstCompile]
    private partial struct MoveItemsOnBeltsJob : IJobEntity
    {
        public float DeltaTime;
        public float ItemSpacing;

        [ReadOnly]
        public NativeParallelHashMap<int2, Entity>.ReadOnly OccupiedCells;

        [ReadOnly]
        public NativeParallelMultiHashMap<int2, ItemSpatialEntry> ItemsByCell;

        public NativeParallelMultiHashMap<int2, float2> ReservedPositionsByCell;

        [ReadOnly]
        public ComponentLookup<Belt> Belts;

        [ReadOnly]
        public ComponentLookup<Direction> Directions;

        private void Execute(Entity entity, ref LocalTransform transform, ref BeltMoveTarget moveTarget, in Item item)
        {
            float3 itemPos = transform.Position;

            if (moveTarget.isMoving)
            {
                MoveToTarget(entity, ref transform, ref moveTarget, itemPos);
                return;
            }

            int2 itemCell = itemPos.ToGridCell();

            if (!itemPos.IsInsideCell(itemCell))
                return;

            if (!TryGetBelt(itemCell, out Belt belt, out Direction direction))
                return;

            moveTarget.targetCell = itemCell + direction.dir.ToInt2();
            moveTarget.speed = belt.speed;
            moveTarget.isMoving = true;

            MoveToTarget(entity, ref transform, ref moveTarget, itemPos);
        }

        private bool TryGetBelt(int2 cell, out Belt belt, out Direction direction)
        {
            if (!OccupiedCells.TryGetValue(cell, out Entity beltEntity) ||
                !Belts.HasComponent(beltEntity) ||
                !Directions.HasComponent(beltEntity))
            {
                belt = default;
                direction = default;
                return false;
            }

            belt = Belts[beltEntity];
            direction = Directions[beltEntity];
            return true;
        }

        private void MoveToTarget(Entity entity, ref LocalTransform transform, ref BeltMoveTarget moveTarget, float3 itemPos)
        {
            float3 targetPosition = new float3(moveTarget.targetCell.x, moveTarget.targetCell.y, itemPos.z);
            transform.Position = MoveTowardsWithSpacing(entity, itemPos, targetPosition, moveTarget.speed * DeltaTime);

            float2 reservedPosition = new float2(transform.Position.x, transform.Position.y);
            ReservedPositionsByCell.Add(transform.Position.ToGridCell(), reservedPosition);

            if (math.distancesq(transform.Position, targetPosition) == 0f)
                moveTarget.isMoving = false;
        }

        private float3 MoveTowardsWithSpacing(Entity entity, float3 current, float3 target, float maxDistanceDelta)
        {
            float3 offset = target - current;
            float distance = math.length(offset);

            if (distance == 0f)
                return target;

            float3 direction = offset / distance;
            float moveDistance = math.min(distance, maxDistanceDelta);
            float2 current2 = new float2(current.x, current.y);
            float2 direction2 = new float2(direction.x, direction.y);
            int2 currentCell = current.ToGridCell();
            int2 targetCell = target.ToGridCell();
            int2 directionCell = GetDirectionCell(direction2);
            int2 nextCell = targetCell + directionCell;

            moveDistance = LimitMoveDistanceInCell(moveDistance, entity, current2, direction2, currentCell);
            moveDistance = LimitMoveDistanceInCell(moveDistance, entity, current2, direction2, targetCell);
            moveDistance = LimitMoveDistanceInCell(moveDistance, entity, current2, direction2, nextCell);

            if (moveDistance <= 0f)
                return current;

            return current + direction * moveDistance;
        }

        private int2 GetDirectionCell(float2 direction)
        {
            if (math.abs(direction.x) > math.abs(direction.y))
                return new int2(direction.x > 0f ? 1 : -1, 0);

            if (math.abs(direction.y) > 0f)
                return new int2(0, direction.y > 0f ? 1 : -1);

            return int2.zero;
        }

        private float LimitMoveDistanceInCell(float moveDistance, Entity entity, float2 current, float2 direction, int2 cell)
        {
            NativeParallelMultiHashMapIterator<int2> itemIterator;
            ItemSpatialEntry itemEntry;

            if (ItemsByCell.TryGetFirstValue(cell, out itemEntry, out itemIterator))
            {
                do
                {
                    if (itemEntry.Entity == entity)
                        continue;

                    moveDistance = LimitMoveDistance(moveDistance, current, direction, itemEntry.Position);
                }
                while (ItemsByCell.TryGetNextValue(out itemEntry, ref itemIterator));
            }

            NativeParallelMultiHashMapIterator<int2> reservedIterator;
            float2 reservedPosition;

            if (ReservedPositionsByCell.TryGetFirstValue(cell, out reservedPosition, out reservedIterator))
            {
                do
                {
                    moveDistance = LimitMoveDistance(moveDistance, current, direction, reservedPosition);
                }
                while (ReservedPositionsByCell.TryGetNextValue(out reservedPosition, ref reservedIterator));
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
            float spacingSq = ItemSpacing * ItemSpacing;

            if (perpendicularDistanceSq >= spacingSq)
                return moveDistance;

            float allowedDistance = projectedDistance - math.sqrt(spacingSq - perpendicularDistanceSq);

            return math.min(moveDistance, math.max(0f, allowedDistance));
        }
    }
}
