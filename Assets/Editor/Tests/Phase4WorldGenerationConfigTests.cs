using System.Text.RegularExpressions;
using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

public class Phase4WorldGenerationConfigTests : EcsWorldTestFixture
{
    [TestCase("255")]
    [TestCase("None")]
    [TestCase("UnknownOre")]
    public void UndefinedResourceType_RejectsEntireConfig(string resourceType)
    {
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
