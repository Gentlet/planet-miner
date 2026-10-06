using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;

/// <summary>
/// 역할·목적: 건물 설정 초기 게시와 중복 생성 방지에 대한 NUnit EditMode 회귀 검증.
/// 입력·검사: BuildingConfigInitSystem의 OnCreate/Update를 구분해 BuildingConfig와 호환 BuildingRuntimeConfig의 singleton 게시를 검사한다.
/// 수명: EcsWorldTestFixture가 각 사례의 독립 World를 준비하고 종료 시 해제한다.
/// </summary>
public class Phase3BuildingRuntimeConfigTests : EcsWorldTestFixture
{
    [Test]
    public void Test06_BuildingConfigInitSystem_ExecutesOnceAndPublishes()
    {
        // OnCreate 게시와 이후 Update를 나누어 초기 데이터와 반복 초기화 중복을 각각 확인한다.
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
