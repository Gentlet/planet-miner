using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 종류·티어별 프리팹을 명시적으로 등록한다. 크기와 표시 이름은 BuildingConfig가 소유한다.
/// Baker는 별도 DB 엔티티를 만들며 Initialization이 설정과의 대응·중복·원형을 검증한다.
/// 런타임 매핑은 World 동안 불변이다. 프리팹 이름으로 종류나 티어를 추론하지 않는다.
/// </summary>
[DisallowMultipleComponent]
public class BuildingPrefabDatabaseAuthoring : MonoBehaviour
{
    [System.Serializable]
    public struct BuildingPrefabEntry
    {
        public BuildingTypeEnum Type;
        public int Tier;
        public GameObject Prefab;

        public BuildingPrefabEntry(BuildingTypeEnum type, int tier, GameObject prefab)
        {
            Type = type;
            Tier = tier;
            Prefab = prefab;
        }
    }

    [Tooltip("종류·양수 티어·프리팹을 명시적으로 등록합니다. 크기와 표시 이름은 Config/BuildingConfig에서 편집합니다.")]
    [FormerlySerializedAs("_entries")]
    public List<BuildingPrefabEntry> Prefabs = new List<BuildingPrefabEntry>();

    public class BuildingPrefabDatabaseBaker : Baker<BuildingPrefabDatabaseAuthoring>
    {
        public override void Bake(BuildingPrefabDatabaseAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent<BuildingPrefabDatabase>(entity);
            var buffer = AddBuffer<BuildingPrefabElement>(entity);
            foreach (var entry in authoring.Prefabs)
            {
                // 무효 등록도 검증 경계로 넘겨 미사용 행의 설정 오류를 조용히 제외하지 않는다.
                Entity prefabEntity = entry.Prefab == null
                    ? Entity.Null
                    : GetEntity(entry.Prefab, TransformUsageFlags.Renderable);
                buffer.Add(new BuildingPrefabElement(entry.Type, entry.Tier, prefabEntity));
            }
        }
    }
}
