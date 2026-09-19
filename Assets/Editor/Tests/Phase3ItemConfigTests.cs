using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;

/// <summary>
/// Task 3.2.2: ItemConfig 및 아이템별 MaxStack 설정 인프라 단위 테스트.
/// </summary>
public class Phase3ItemConfigTests : EcsWorldTestFixture
{
    private SystemHandle _initConfigHandle;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        _initConfigHandle = _world.GetOrCreateSystem(typeof(ItemConfigInitSystem));
    }

    [Test]
    public void Test01_InitializeItemConfig_CreatesSingletonAndBuffer()
    {
        // Act
        var singleton = ItemConfigInitSystem.InitializeItemConfig(_entityManager);

        // Assert
        Assert.IsTrue(_entityManager.Exists(singleton));
        Assert.IsTrue(_entityManager.HasComponent<ItemConfig>(singleton));
        Assert.IsTrue(_entityManager.HasBuffer<ItemConfigElement>(singleton));

        var buffer = _entityManager.GetBuffer<ItemConfigElement>(singleton);
        Assert.AreEqual((int)ItemTypeEnum.Count, buffer.Length);
    }

    [Test]
    public void Test02_DefaultConfig_MapsCorrectMaxStacks()
    {
        // Act: Initialize with null json (fallback defaults)
        var singleton = ItemConfigInitSystem.InitializeItemConfig(_entityManager);
        var buffer = _entityManager.GetBuffer<ItemConfigElement>(singleton);

        // Assert
        Assert.AreEqual(0, buffer.GetMaxStack(ItemTypeEnum.None));
        Assert.AreEqual(50, buffer.GetMaxStack(ItemTypeEnum.Iron_Ore));
        Assert.AreEqual(50, buffer.GetMaxStack(ItemTypeEnum.Copper_Ore));
        Assert.AreEqual(50, buffer.GetMaxStack(ItemTypeEnum.Coal));
        Assert.AreEqual(50, buffer.GetMaxStack(ItemTypeEnum.Stone));
        Assert.AreEqual(100, buffer.GetMaxStack(ItemTypeEnum.Iron));
        Assert.AreEqual(100, buffer.GetMaxStack(ItemTypeEnum.Copper));
        Assert.AreEqual(100, buffer.GetMaxStack(ItemTypeEnum.Iron_Stick));
        Assert.AreEqual(100, buffer.GetMaxStack(ItemTypeEnum.Copper_Stick));
        Assert.AreEqual(1, buffer.GetMaxStack(ItemTypeEnum.Drone));
    }

    [Test]
    public void Test03_CustomJson_OverridesValues()
    {
        // Arrange: Custom JSON with custom MaxStack
        string customJson = @"{
            ""DefaultMaxStack"": 64,
            ""Items"": [
                { ""ItemType"": ""Iron_Ore"", ""MaxStack"": 999 },
                { ""ItemType"": ""Drone"", ""MaxStack"": 5 }
            ]
        }";

        // Act
        var singleton = ItemConfigInitSystem.InitializeItemConfig(_entityManager, customJson);
        var buffer = _entityManager.GetBuffer<ItemConfigElement>(singleton);

        // Assert: Overridden items
        Assert.AreEqual(999, buffer.GetMaxStack(ItemTypeEnum.Iron_Ore));
        Assert.AreEqual(5, buffer.GetMaxStack(ItemTypeEnum.Drone));

        // Non-overridden items keep their default/fallback values
        Assert.AreEqual(50, buffer.GetMaxStack(ItemTypeEnum.Copper_Ore));
        Assert.AreEqual(100, buffer.GetMaxStack(ItemTypeEnum.Iron));

        // Fallback uses DefaultMaxStack (64) when querying with config
        var config = _entityManager.GetComponentData<ItemConfig>(singleton);
        Assert.AreEqual(64, config.DefaultMaxStack);
        var invalidType = (ItemTypeEnum)250;
        Assert.AreEqual(64, buffer.GetMaxStack(config, invalidType));
    }

    [Test]
    public void Test04_ExtensionMethod_O1LookupAndFallback()
    {
        // Arrange
        var singleton = ItemConfigInitSystem.InitializeItemConfig(_entityManager);
        var buffer = _entityManager.GetBuffer<ItemConfigElement>(singleton);
        var config = _entityManager.GetComponentData<ItemConfig>(singleton);

        // Direct array lookup equals extension method
        Assert.AreEqual(buffer[(int)ItemTypeEnum.Iron_Ore].MaxStack, buffer.GetMaxStack(ItemTypeEnum.Iron_Ore));

        // Fallback with config: returns config.DefaultMaxStack (50)
        var invalidType = (ItemTypeEnum)250;
        Assert.AreEqual(50, buffer.GetMaxStack(config, invalidType));

        // Fallback with explicit value
        Assert.AreEqual(77, buffer.GetMaxStack(invalidType, fallbackMaxStack: 77));

        // Fallback without config/explicit fallback returns 0
        Assert.AreEqual(0, buffer.GetMaxStack(invalidType));
    }

    [Test]
    public void Test05_SystemUpdate_InitializesAutomatically()
    {
        // Pre-condition: No ItemConfig singleton
        Assert.IsFalse(_world.EntityManager.CreateEntityQuery(typeof(ItemConfig)).CalculateEntityCount() > 0);

        // Act: Update system
        _initConfigHandle.Update(_world.Unmanaged);

        // Assert: Singleton created and buffer populated
        var query = _world.EntityManager.CreateEntityQuery(typeof(ItemConfig), typeof(ItemConfigElement));
        Assert.AreEqual(1, query.CalculateEntityCount());
        var buffer = query.GetSingletonBuffer<ItemConfigElement>();
        Assert.AreEqual((int)ItemTypeEnum.Count, buffer.Length);
        Assert.Greater(buffer.GetMaxStack(ItemTypeEnum.Iron), 0);
    }
}
