using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace PlanetMiner.Tests
{
    /// <summary>
    /// 역할·목적: 제품 Instantiate를 사용할 격리 사례에 최소 ECS Prefab과 종류별 DB를 제공한다.
    /// 호출·입출력: 테스트 EntityManager에 건물/아이템/자원 DB·Prefab을 생성하여 DB 엔티티를 반환한다.
    /// 수명: RemoveDatabase는 지정 DB만 제거하고 전체 정리는 World Dispose가 담당한다. 실제 자산 Baker/SubScene 로딩 검증을 대신하지 않는다.
    /// </summary>
    public static class TestPrefabDatabaseFactory
    {
        public static Entity CreateBuildings(EntityManager manager)
        {
            // 최소 Prefab만 등록하고 종류/진행/버퍼 등 런타임 상태는 제품 생성 시스템이 초기화하게 둔다.
            Entity database = manager.CreateEntity(typeof(BuildingPrefabDatabase));
            manager.AddBuffer<BuildingPrefabElement>(database);
            foreach (BuildingTypeEnum type in Enum.GetValues(typeof(BuildingTypeEnum)))
            {
                if (type == BuildingTypeEnum.None || type == BuildingTypeEnum.Count || type == BuildingTypeEnum.ConstructionSite)
                {
                    continue;
                }
                Entity prefab = manager.CreateEntity(typeof(Prefab), typeof(LocalTransform));
                manager.GetBuffer<BuildingPrefabElement>(database).Add(new BuildingPrefabElement(type, prefab, new int2(1, 1)));
            }
            return database;
        }

        public static Entity CreateItems(EntityManager manager)
        {
            Entity database = manager.CreateEntity(typeof(ItemPrefabDatabase));
            manager.AddBuffer<ItemPrefabElement>(database);
            foreach (ItemTypeEnum type in Enum.GetValues(typeof(ItemTypeEnum)))
            {
                if (type == ItemTypeEnum.None)
                {
                    continue;
                }
                Entity prefab = manager.CreateEntity(typeof(Prefab), typeof(LocalTransform), typeof(ItemIdentity));
                manager.SetComponentData(prefab, new ItemIdentity(type));
                manager.GetBuffer<ItemPrefabElement>(database).Add(new ItemPrefabElement(type, prefab));
            }
            return database;
        }

        public static Entity CreateResources(EntityManager manager)
        {
            Entity database = manager.CreateEntity(typeof(ResourcePrefabDatabase));
            manager.AddBuffer<ResourcePrefabElement>(database);
            foreach (ItemTypeEnum type in new[] { ItemTypeEnum.Iron_Ore, ItemTypeEnum.Copper_Ore, ItemTypeEnum.Coal, ItemTypeEnum.Stone })
            {
                Entity prefab = manager.CreateEntity(typeof(Prefab), typeof(LocalTransform));
                manager.GetBuffer<ResourcePrefabElement>(database).Add(new ResourcePrefabElement(type, prefab));
            }
            return database;
        }

        public static void RemoveDatabase<T>(EntityManager manager) where T : unmanaged, IComponentData
        {
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<T>());
            manager.DestroyEntity(query);
        }
    }
}
