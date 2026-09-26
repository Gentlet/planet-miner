using Unity.Entities;
using UnityEngine;

/// <summary>
/// 아이템 프리팹 단일 엔티티 Authoring 컴포넌트.
/// </summary>
[DisallowMultipleComponent]
public class ItemAuthoring : MonoBehaviour
{
    [Tooltip("아이템 고유 식별 타입")]
    public ItemTypeEnum Type;

    public class ItemAuthoringBaker : Baker<ItemAuthoring>
    {
        public override void Bake(ItemAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new ItemIdentity(authoring.Type));
        }
    }
}
