using System.Text.RegularExpressions;
using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// 역할·목적: 건물 설정 초기 게시와 중복 생성 방지에 대한 NUnit EditMode 회귀 검증.
/// 입력·검사: 초기 게시의 유일성과 잘못된 건축 자재의 공개 거부/사전 등록 실패 시 원본 보존을 검사한다.
/// 수명: EcsWorldTestFixture가 각 사례의 독립 World를 준비하고 종료 시 해제한다.
/// </summary>
public class Phase3BuildingRuntimeConfigTests : EcsWorldTestFixture
{
    [TestCase(ItemTypeEnum.None, 1)]
    [TestCase((ItemTypeEnum)255, 1)]
    [TestCase(ItemTypeEnum.Iron, 0)]
    [TestCase(ItemTypeEnum.Iron, -1)]
    public void InvalidConstructionMaterial_PublicationRejectsAndPreregisteredInitStops(ItemTypeEnum itemType, int quantity)
    {
        Assert.IsTrue(BuildingConfigLoader.TryLoadConfigFromResources(
            BuildingConfigLoader.DefaultResourcePath, out var configs, out var materials));
        Assert.IsNotEmpty(materials);
        var validMaterial = materials[0];
        var invalidMaterial = validMaterial;
        invalidMaterial.ItemType = itemType;
        invalidMaterial.Quantity = quantity;
        materials[0] = invalidMaterial;

        // 공개 API가 무효 행을 부분 게시하지 않고 거부하는지 확인한다.
        LogAssert.Expect(LogType.Error, new Regex("BuildingConfigLoader.*Construction material"));
        Assert.AreEqual(Entity.Null, BuildingConfigLoader.PublishConfig(_entityManager, configs, materials));
        using var query = _entityManager.CreateEntityQuery(typeof(BuildingConfig));
        Assert.AreEqual(0, query.CalculateEntityCount());

        // 초기화 전 사전 등록된 입력도 같은 검사로 중단하고 원본을 보존해야 한다.
        materials[0] = validMaterial;
        Entity entity = BuildingConfigLoader.PublishConfig(_entityManager, configs, materials);
        Assert.AreNotEqual(Entity.Null, entity);
        var preregisteredMaterials = _entityManager.GetBuffer<BuildingConstructionMaterialElement>(entity);
        preregisteredMaterials[0] = invalidMaterial;
        LogAssert.Expect(LogType.Error, new Regex("BuildingConfigLoader.*Construction material"));
        LogAssert.Expect(LogType.Error, new Regex("BuildingConfigInitSystem.*Game simulation stopped"));
        _world.GetOrCreateSystemManaged<BuildingConfigInitSystem>();

        Assert.IsTrue(SimulationFailureUtility.HasFatalError(_entityManager));
        Assert.AreEqual(entity, query.GetSingletonEntity());
        var preservedMaterials = _entityManager.GetBuffer<BuildingConstructionMaterialElement>(entity);
        Assert.AreEqual(materials.Count, preservedMaterials.Length);
        Assert.AreEqual(itemType, preservedMaterials[0].ItemType);
        Assert.AreEqual(quantity, preservedMaterials[0].Quantity);
    }

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
