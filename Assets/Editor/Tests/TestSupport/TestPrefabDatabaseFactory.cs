using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace PlanetMiner.Tests
{
    /// <summary>테스트가 실제 Instantiate 경로를 사용하도록 최소 베이킹 계약의 프리팹을 명시적으로 제공한다.</summary>
    public static class TestPrefabDatabaseFactory
    {
        public static Entity CreateBuildings(EntityManager manager)
        {
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
