using Unity.Entities;
using UnityEngine;

public class ResourceAuthoring : MonoBehaviour
{
    [SerializeField]
    private ResourceTypeEnum _type = ResourceTypeEnum.Iron_Ore;

    [SerializeField]
    private int _amount = 100;

    private class Baker : Baker<ResourceAuthoring>
    {
        public override void Bake(ResourceAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new GridPosition
            {
                gridPosition = authoring.transform.position.ToGridCell()
            });
            AddComponent(entity, new ResourceDeposit
            {
                type = authoring._type,
                amount = Mathf.Max(1, authoring._amount)
            });
            AddComponent<ResourceOccupantRequest>(entity);
        }
    }
}
