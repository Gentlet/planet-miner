using Unity.Entities;
using UnityEngine;


public class ItemAuthoring : MonoBehaviour
{
    [SerializeField]
    private ItemTypeEnum _type = ItemTypeEnum.None;

    public class Baker : Baker<ItemAuthoring>
    {
        public override void Bake(ItemAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Item
            {
                type = authoring._type
            });
            AddComponent(entity, new GridPosition
            {
                gridPosition = authoring.transform.position.ToGridCell()
            });
            AddComponent<ItemCellChanged>(entity);
            SetComponentEnabled<ItemCellChanged>(entity, false);
        }
    }
}
