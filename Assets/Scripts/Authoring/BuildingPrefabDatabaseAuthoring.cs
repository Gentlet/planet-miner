using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 건물 프리팹 데이터베이스 로드 모드 (인스펙터 목록 직접 지정 vs Resources 폴더 자동 탐색).
/// </summary>
public enum BuildingPrefabLoadMode
{
    InspectorList,
    ResourcesAutoLoad
}

/// <summary>
/// 역할·목적: Inspector 목록 또는 Resources/Prefabs/Building에서 건물 종류→프리팹/기본 크기 DB를 베이킹한다.
/// 부착 대상: SubScene의 DB Authoring GameObject. Baker는 별도 DB 엔티티에 BuildingPrefabDatabase/BuildingPrefabElement를 게시한다.
/// 이용: PrefabDatabaseInitializationSystem이 유일성/필수 원형을 검증하고 건물 생성 경계가 참조한다.
/// 수명·경계: 베이킹 참조는 World 동안 불변으로 사용하는 계약이다. 런타임 BuildingType·위치·작업 상태는 여기서 부착하지 않는다.
/// </summary>
[DisallowMultipleComponent]
public class BuildingPrefabDatabaseAuthoring : MonoBehaviour
{
    [Tooltip("프리팹 로드 방식 설정 (인스펙터 직접 지정 vs Resources 폴더 자동 탐색)")]
    public BuildingPrefabLoadMode LoadMode = BuildingPrefabLoadMode.InspectorList;

    /// <summary>Inspector의 건물 원형 등록 값. Size는 방향 적용 전 기본 크기이며 Baker가 각 축을 최소 1로 정규화한다.</summary>
    [System.Serializable]
    public struct BuildingPrefabEntry
    {
        public BuildingTypeEnum Type;
        public GameObject Prefab;
        public Vector2Int Size;

        public BuildingPrefabEntry(BuildingTypeEnum type, GameObject prefab, Vector2Int size)
        {
            Type = type;
            Prefab = prefab;
            Size = size;
        }
    }

    [Tooltip("인스펙터 목록 등록 방식 사용 시 건물 프리팹 리스트")]
    [FormerlySerializedAs("_entries")]
    public List<BuildingPrefabEntry> Prefabs = new List<BuildingPrefabEntry>();

    /// <summary>
    /// 건물 종류에 따른 기본 Footprint 규격을 반환합니다.
    /// </summary>
    public static int2 GetDefaultFootprint(BuildingTypeEnum type)
    {
        switch (type)
        {
            case BuildingTypeEnum.Belt:
            case BuildingTypeEnum.Splitter:
            case BuildingTypeEnum.Merger:
            case BuildingTypeEnum.PowerPole:
            case BuildingTypeEnum.Storage:
                return new int2(1, 1);

            case BuildingTypeEnum.Miner:
            case BuildingTypeEnum.Crafter:
                return new int2(2, 2);

            case BuildingTypeEnum.CoalGenerator:
            case BuildingTypeEnum.DroneStation:
            case BuildingTypeEnum.ResearchBuilding:
            case BuildingTypeEnum.MainFacility:
                return new int2(3, 3);

            default:
                return new int2(1, 1);
        }
    }

    /// <summary>
    /// Resources/Prefabs/Building 폴더에서 프리팹을 탐색하여 목록을 자동으로 채웁니다.
    /// </summary>
    [ContextMenu("Populate From Resources")]
    public void PopulateFromResources()
    {
        Prefabs.Clear();

        var loadedPrefabs = Resources.LoadAll<GameObject>("Prefabs/Building");
        if (loadedPrefabs == null || loadedPrefabs.Length == 0)
        {
            return;
        }

        foreach (var prefab in loadedPrefabs)
        {
            string prefabName = prefab.name.Trim().Replace(" ", "_");

            if (Enum.TryParse<BuildingTypeEnum>(prefabName, true, out var buildingType))
            {
                if (buildingType != BuildingTypeEnum.None && buildingType != BuildingTypeEnum.ConstructionSite)
                {
                    int2 defaultSize = GetDefaultFootprint(buildingType);
                    Prefabs.Add(new BuildingPrefabEntry(
                        buildingType,
                        prefab,
                        new Vector2Int(defaultSize.x, defaultSize.y)));
                }
            }
        }
    }

    /// <summary>Unity 베이킹에서 선택된 로드 모드로 DB 버퍼를 만든다. 런타임 필수 항목/중복 검증은 Initialization이 담당한다.</summary>
    public class BuildingPrefabDatabaseBaker : Baker<BuildingPrefabDatabaseAuthoring>
    {
        public override void Bake(BuildingPrefabDatabaseAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent<BuildingPrefabDatabase>(entity);
            var buffer = AddBuffer<BuildingPrefabElement>(entity);

            if (authoring.LoadMode == BuildingPrefabLoadMode.ResourcesAutoLoad)
            {
                var loadedPrefabs = Resources.LoadAll<GameObject>("Prefabs/Building");
                if (loadedPrefabs != null)
                {
                    foreach (var prefab in loadedPrefabs)
                    {
                        string prefabName = prefab.name.Trim().Replace(" ", "_");
                        if (Enum.TryParse<BuildingTypeEnum>(prefabName, true, out var buildingType) &&
                            buildingType != BuildingTypeEnum.None &&
                            buildingType != BuildingTypeEnum.ConstructionSite)
                        {
                            int2 footprint = GetDefaultFootprint(buildingType);
                            Entity prefabEntity = GetEntity(prefab, TransformUsageFlags.Renderable);
                            buffer.Add(new BuildingPrefabElement(buildingType, prefabEntity, footprint));
                        }
                    }
                }
            }
            else
            {
                for (int i = 0; i < authoring.Prefabs.Count; i++)
                {
                    var entry = authoring.Prefabs[i];
                    if (entry.Prefab != null && entry.Type != BuildingTypeEnum.None)
                    {
                        int2 footprint = new int2(
                            Mathf.Max(1, entry.Size.x),
                            Mathf.Max(1, entry.Size.y));

                        Entity prefabEntity = GetEntity(entry.Prefab, TransformUsageFlags.Renderable);
                        buffer.Add(new BuildingPrefabElement(entry.Type, prefabEntity, footprint));
                    }
                }
            }
        }
    }
}
