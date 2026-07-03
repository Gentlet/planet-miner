using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;

public class BeltAuthoring : MonoBehaviour
{
    public int2 pos;
    public DirectionEnum dir = DirectionEnum.Right;
    public float speed = 2f;

    public class Baker : Baker<BeltAuthoring>
    {
        public override void Bake(BeltAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new BuildingType
            {
                type = BuildingTypeEnum.Belt
            });

            AddComponent(entity, new GridPosition
            {
                gridPosition = authoring.pos
            });
            AddComponent(entity, new Direction
            {
                dir = authoring.dir
            });
            AddComponent(entity, new Belt
            {
                speed = authoring.speed
            });
        }
    }
}
