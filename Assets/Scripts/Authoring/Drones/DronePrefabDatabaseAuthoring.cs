using Unity.Entities;
using UnityEngine;

public class DronePrefabDatabaseAuthoring : MonoBehaviour
{
    [SerializeField]
    private GameObject _prefab;

    private class Baker : Baker<DronePrefabDatabaseAuthoring>
    {
        public override void Bake(DronePrefabDatabaseAuthoring authoring)
        {
            if (authoring._prefab == null)
                return;

            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(
                entity,
                new DronePrefab
                {
                    value = GetEntity(
                        authoring._prefab,
                        TransformUsageFlags.Dynamic)
                });
        }
    }
}
