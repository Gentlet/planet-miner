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

        for (int i = 0; i < itemEntities.Length; i++)
        {
            float3 position = itemTransforms[i].Position;
            int2 cell = position.ToGridCell();
            itemsByCell.Add(cell, new ItemSpatialEntry
            {
                entity = itemEntities[i],
                position = position.ToFloat2()
            });
        }

        MoveItemsOnBeltsJob job = new MoveItemsOnBeltsJob
        {
            deltaTime = SystemAPI.Time.DeltaTime,
            itemSpacing = itemSpacing,
            occupiedCells = _gridOccupancy.GetOccupiedCellsReadOnly(),
            itemsByCell = itemsByCell,
            belts = SystemAPI.GetComponentLookup<Belt>(true),
            directions = SystemAPI.GetComponentLookup<Direction>(true)
        };

        Dependency = job.ScheduleParallel(Dependency);
        Dependency = itemEntities.Dispose(Dependency);
        Dependency = itemTransforms.Dispose(Dependency);
        Dependency = itemsByCell.Dispose(Dependency);
    }

    private struct ItemSpatialEntry
    {
        public Entity entity;
        public float2 position;
    }

    [BurstCompile]
    private partial struct MoveItemsOnBeltsJob : IJobEntity
    {
        public float deltaTime;
        public float itemSpacing;

        [ReadOnly]
        public NativeParallelHashMap<int2, Entity>.ReadOnly occupiedCells;

        [ReadOnly]
        public NativeParallelMultiHashMap<int2, ItemSpatialEntry> itemsByCell;

        [ReadOnly]
        public ComponentLookup<Belt> belts;

        [ReadOnly]
        public ComponentLookup<Direction> directions;

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
            if (!occupiedCells.TryGetValue(cell, out Entity beltEntity) ||
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

        private void MoveToTarget(Entity entity, ref LocalTransform transform, ref BeltMoveTarget moveTarget, float3 itemPos)
        {
            float3 targetPosition = new float3(moveTarget.targetCell.x, moveTarget.targetCell.y, itemPos.z);

            float3 offset = targetPosition - itemPos;
            float distance = math.length(offset);

            if (distance == 0f)
            {
                transform.Position = targetPosition;
                moveTarget.isMoving = false;
                return;
            }

            float3 direction = offset / distance;
            float moveDistance = math.min(distance, moveTarget.speed * deltaTime);
            int2 currentCell = itemPos.ToGridCell();
            int2 targetCell = targetPosition.ToGridCell();

            moveDistance = LimitMoveDistanceInCell(moveDistance, entity, itemPos.ToFloat2(), direction.ToFloat2(), currentCell);
            moveDistance = LimitMoveDistanceInCell(moveDistance, entity, itemPos.ToFloat2(), direction.ToFloat2(), targetCell);

            if (moveDistance > 0f)
                transform.Position = itemPos + direction * moveDistance;

            if (math.distancesq(transform.Position, targetPosition) == 0f)
                moveTarget.isMoving = false;
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
            float spacingSq = itemSpacing * itemSpacing;

            if (perpendicularDistanceSq >= spacingSq)
                return moveDistance;

            float allowedDistance = projectedDistance - math.sqrt(spacingSq - perpendicularDistanceSq);

            return math.min(moveDistance, math.max(0f, allowedDistance));
        }
    }
}
