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
            movement[item] = new BeltMovementState(0f, direction);
            movement.SetComponentEnabled(item, true);
        }
        if (transforms.HasComponent(item))
        {
            transforms[item] = LocalTransform.FromPosition(BeltPathUtility.Position(position, 0f, direction, direction));
        }
    }
}
