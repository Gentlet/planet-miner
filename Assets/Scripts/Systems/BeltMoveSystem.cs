using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

partial struct BeltMoveSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach( var (iTransform, item) in 
            SystemAPI.Query<
                RefRW<LocalTransform>, 
                RefRO<Item>
                >())
        {
            float3 itemPos = iTransform.ValueRO.Position;

            foreach(var (bTransform, belt, dir) in 
                SystemAPI.Query<
                    RefRO<LocalTransform>, 
                    RefRO<Belt>,
                    RefRO<Direction>
                    >())
            {
                float3 beltPos = bTransform.ValueRO.Position;

                bool onBelt = math.abs(itemPos.x - beltPos.x) < 0.5f && math.abs(itemPos.y - beltPos.y) < 0.5f;

                if (onBelt)
                {
                    iTransform.ValueRW.Position += dir.ValueRO.dir.Tofloat3() * belt.ValueRO.speed * deltaTime;
                    break;
                }
            }
        }
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        
    }
}
