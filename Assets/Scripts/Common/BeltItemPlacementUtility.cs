using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>승인된 실물을 벨트 입구에 배치한다. 소유권 이전과 라우팅 커서/결정 소비는 호출자가 소유한다.</summary>
internal static class BeltItemPlacementUtility
{
    internal static void PlaceAtStart(Entity item, int2 position, DirectionEnum direction,
        ref ComponentLookup<GridPosition> positions, ref ComponentLookup<Direction> directions,
        ref ComponentLookup<BeltMovementState> movement, ref ComponentLookup<LocalTransform> transforms)
    {
        if (positions.HasComponent(item))
        {
            positions[item] = new GridPosition(position);
        }
        if (directions.HasComponent(item))
        {
            directions[item] = new Direction(direction);
        }
        if (movement.HasComponent(item))
        {
            movement[item] = new BeltMovementState(0.0f);
            movement.SetComponentEnabled(item, true);
        }
        if (transforms.HasComponent(item))
        {
            float2 center = new float2(position.x, position.y);
            int2 directionVector = direction.ToInt2();
            float2 visualPosition = center + new float2(directionVector.x, directionVector.y) * -0.5f;
            transforms[item] = LocalTransform.FromPosition(new float3(visualPosition.x, visualPosition.y, 0f));
        }
    }
}
