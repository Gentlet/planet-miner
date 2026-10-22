using Unity.Entities;
using UnityEngine;

/// <summary>활성 드론 표시 원형을 명시적으로 등록한다. Baker는 참조만 게시하고 수행자 상태는 런타임 생성이 소유한다.</summary>
[DisallowMultipleComponent]
public class DronePrefabDatabaseAuthoring : MonoBehaviour
{
    public GameObject Prefab;

    public class DronePrefabDatabaseBaker : Baker<DronePrefabDatabaseAuthoring>
    {
        public override void Bake(DronePrefabDatabaseAuthoring authoring)
        {
            Entity database = GetEntity(TransformUsageFlags.None);
            AddComponent<DronePrefabDatabase>(database);
            Entity prefab = authoring.Prefab == null
                ? Entity.Null
                : GetEntity(authoring.Prefab, TransformUsageFlags.Dynamic);
            AddComponent(database, new DronePrefab(prefab));
        }
    }
}
