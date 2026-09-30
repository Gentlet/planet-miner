using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;

public class Phase3BuildingRuntimeConfigTests : EcsWorldTestFixture
{
    [Test]
    public void Test06_BuildingConfigInitSystem_ExecutesOnceAndPublishes()
    {
        var initSystem = _world.GetOrCreateSystemManaged<BuildingConfigInitSystem>();

        // OnCreate 시점에 이미 LoadAndPublish가 실행되어 싱글톤 게시됨
        var query = _entityManager.CreateEntityQuery(typeof(BuildingConfig), typeof(BuildingConfigElement));
        Assert.AreEqual(1, query.CalculateEntityCount(), "시스템 생성 후 싱글톤 엔티티가 게시되어야 함");

        var runtimeQuery = _entityManager.CreateEntityQuery(typeof(BuildingRuntimeConfig), typeof(BuildingRuntimeConfigElement));
        Assert.AreEqual(1, runtimeQuery.CalculateEntityCount(), "하위 호환성 싱글톤 엔티티가 게시되어야 함");

        // 추가 Update 실행해도 중복 생성되지 않음을 확인
        initSystem.Update();
        Assert.AreEqual(1, query.CalculateEntityCount(), "중복 실행 시에도 단일 싱글톤만 유지되어야 함");
    }
}
