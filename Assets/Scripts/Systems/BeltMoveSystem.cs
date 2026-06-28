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
        NativeArray<Entity> itemEntities = _itemsQuery.ToEntityArray(Allocator.TempJob);
        NativeArray<LocalTransform> itemTransforms = _itemsQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
        NativeList<float2> reservedPositions = new NativeList<float2>(itemCount, Allocator.TempJob);

        MoveItemsOnBeltsJob job = new MoveItemsOnBeltsJob
        {
            DeltaTime = SystemAPI.Time.DeltaTime,
            ItemSpacing = itemSpacing,
            OccupiedCells = _gridOccupancy.GetOccupiedCellsReadOnly(),
            ItemEntities = itemEntities,
            ItemTransforms = itemTransforms,
            ReservedPositions = reservedPositions,
            Belts = SystemAPI.GetComponentLookup<Belt>(true),
            Directions = SystemAPI.GetComponentLookup<Direction>(true)
        };

        Dependency = job.Schedule(Dependency);
        Dependency = itemEntities.Dispose(Dependency);
        Dependency = itemTransforms.Dispose(Dependency);
        Dependency = reservedPositions.Dispose(Dependency);
    }

    [BurstCompile]
    private partial struct MoveItemsOnBeltsJob : IJobEntity
    {
        public float DeltaTime;
        public float ItemSpacing;

        [ReadOnly]
        public NativeParallelHashMap<int2, Entity>.ReadOnly OccupiedCells;

        [ReadOnly]
        public NativeArray<Entity> ItemEntities;

        [ReadOnly]
        public NativeArray<LocalTransform> ItemTransforms;

        public NativeList<float2> ReservedPositions;

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

            ReservedPositions.Add(new float2(transform.Position.x, transform.Position.y));

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

            for (int i = 0; i < ItemEntities.Length; i++)
            {
                if (ItemEntities[i] == entity)
                    continue;

                float3 otherPosition = ItemTransforms[i].Position;
                moveDistance = LimitMoveDistance(moveDistance, current2, direction2, new float2(otherPosition.x, otherPosition.y));
            }

            for (int i = 0; i < ReservedPositions.Length; i++)
            {
                moveDistance = LimitMoveDistance(moveDistance, current2, direction2, ReservedPositions[i]);
            }

            if (moveDistance <= 0f)
                return current;

            return current + direction * moveDistance;
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
