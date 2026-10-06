using System.Text.RegularExpressions;
using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// 역할·목적: 월드 설정 유효성 검사와 초기화 한 번 게시에 대한 NUnit EditMode 회귀 검증.
/// 입력·검사: 잘못된 resourceType/수량 상한과 초기화 호출로 부분 게시 방지·설정 엔티티 유일성을 검사한다.
/// 수명: EcsWorldTestFixture가 각 사례의 독립 World를 준비하고 종료 시 해제한다.
/// </summary>
public class Phase4WorldGenerationConfigTests : EcsWorldTestFixture
{
    [TestCase("255")]
    [TestCase("None")]
    [TestCase("UnknownOre")]
    public void UndefinedResourceType_RejectsEntireConfig(string resourceType)
    {
        // 잘못된 종류 하나가 포함된 전체 입력을 거부하여 유효 행만 부분 게시되지 않는지 확인한다.
        string json = Resources.Load<TextAsset>(WorldGenerationConfigLoader.DefaultResourcePath).text;
        json = json.Replace("Iron_Ore", resourceType);
        LogAssert.Expect(LogType.Error, new Regex(".*Invalid or unrecognized resource type.*"));
        Assert.IsFalse(WorldGenerationConfigLoader.TryParseAndValidateJson(json, out var seed, out var elements));
        Assert.AreEqual(0u, seed);
        Assert.IsNull(elements);
        Assert.AreEqual(0, _entityManager.CreateEntityQuery(typeof(ResourceGenerationSettings)).CalculateEntityCount());
    }

    [Test]
    public void Test06_WorldGenerationConfigLoadSystem_ExecutesOnceAndPublishes()
    {
        var systemHandle = _world.GetOrCreateSystem(typeof(WorldGenerationConfigLoadSystem));

        // 1회 Update 실행
        systemHandle.Update(_world.Unmanaged);

        // 싱글톤 게시 확인
        var query = _entityManager.CreateEntityQuery(typeof(ResourceGenerationSettings), typeof(ResourceGenerationConfigElement),
            typeof(FloorGenerationSettings), typeof(FloorBiomeElement), typeof(FloorVariantElement));
        Assert.AreEqual(1, query.CalculateEntityCount(), "시스템 실행 후 싱글톤 엔티티가 게시되어야 함");

        // 추가 Update 실행해도 중복 생성되지 않음을 확인
        systemHandle.Update(_world.Unmanaged);
        Assert.AreEqual(1, query.CalculateEntityCount(), "중복 실행 시에도 단일 싱글톤만 유지되어야 함");
    }

    [Test]
    public void Test08_ValidationFailure_MaxAmountEqualOrAboveIntMax_DoesNotPublish()
    {
        LogAssert.Expect(LogType.Error, new Regex(".*Invalid amount.*"));

        // maxAmount = int.MaxValue (2147483647)는 +1 오버플로를 유발하므로 거부되어야 함
        string overflowJson = $@"{{
            ""worldSeed"": 1,
            ""configs"": [
                {{
                    ""type"": ""Iron_Ore"",
                    ""weight"": 0.35,
                    ""minPatchRadius"": 1,
                    ""maxPatchRadius"": 2,
                    ""cellFillChance"": 0.1,
                    ""minAmount"": 100,
                    ""maxAmount"": {int.MaxValue}
                }}
            ]
        }}";

        bool success = WorldGenerationConfigLoader.TryParseAndValidateJson(overflowJson, out uint seed, out var elements);
        Assert.IsFalse(success, "maxAmount가 int.MaxValue이면 오버플로 방지를 위해 거부되어야 함");
        Assert.IsNull(elements);

        // 반면 int.MaxValue - 1은 정상 통과
        string validMaxJson = Resources.Load<TextAsset>(WorldGenerationConfigLoader.DefaultResourcePath).text
            .Replace("\"maxAmount\": 300", $"\"maxAmount\": {int.MaxValue - 1}");

        bool validSuccess = WorldGenerationConfigLoader.TryParseAndValidateJson(validMaxJson, out seed, out elements);
        Assert.IsTrue(validSuccess, "maxAmount가 int.MaxValue - 1이면 정상 통과해야 함");
        Assert.IsNotNull(elements);
    }
}
