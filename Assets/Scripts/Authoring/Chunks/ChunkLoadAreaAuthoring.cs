using Unity.Entities;
using UnityEngine;

public class ChunkLoadAreaAuthoring : MonoBehaviour
{
    [SerializeField]
    private Vector2Int _originChunk = new Vector2Int(-2, -2);

    [SerializeField]
    private Vector2Int _size = new Vector2Int(5, 5);

    private class Baker : Baker<ChunkLoadAreaAuthoring>
    {
        public override void Bake(ChunkLoadAreaAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new ChunkLoadArea
            {
                originChunk = new Unity.Mathematics.int2(authoring._originChunk.x, authoring._originChunk.y),
                size = new Unity.Mathematics.int2(Mathf.Max(1, authoring._size.x), Mathf.Max(1, authoring._size.y))
            });
        }
    }
}
