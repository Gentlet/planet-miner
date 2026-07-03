using Unity.Entities;
using UnityEngine;


public class ItemAuthoring : MonoBehaviour
{
    public class Baker : Baker<ItemAuthoring>
    {
        public override void Bake(ItemAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<Item>(entity);
        }
    }
}
