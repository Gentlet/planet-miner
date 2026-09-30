using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;

/// <summary>
/// ItemRegistry 및 아이템별 MaxStack 불변 Blob 설정 인프라 단위 테스트.
/// </summary>
public class Phase3ItemConfigTests : EcsWorldTestFixture
{
    private SystemHandle _initConfigHandle;
    private BlobAssetReference<ItemRegistryBlob> _blobRef;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        _initConfigHandle = _world.GetOrCreateSystem(typeof(ItemConfigInitSystem));
    }

    [TearDown]
    public override void TearDown()
    {
        if (_blobRef.IsCreated)
        {
            _blobRef.Dispose();
        }
        base.TearDown();
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
        _blobRef = ItemConfigInitSystem.InitializeItemRegistry(_entityManager, customJson);
        ref var registry = ref _blobRef.Value;

        // Assert: Overridden items
        Assert.AreEqual(999, registry.GetMaxStack(ItemTypeEnum.Iron_Ore));
        Assert.AreEqual(5, registry.GetMaxStack(ItemTypeEnum.Drone));

        // Non-overridden items keep their default/fallback values
        Assert.AreEqual(50, registry.GetMaxStack(ItemTypeEnum.Copper_Ore));
        Assert.AreEqual(100, registry.GetMaxStack(ItemTypeEnum.Iron));

        // Fallback uses DefaultMaxStack (64)
        Assert.AreEqual(64, registry.DefaultMaxStack);
        var invalidType = (ItemTypeEnum)250;
        Assert.AreEqual(64, registry.GetMaxStack(invalidType));
    }

    [Test]
    public void Test05_SystemUpdate_InitializesAutomatically()
    {
        // Pre-condition: No ItemRegistry singleton
        Assert.IsFalse(_world.EntityManager.CreateEntityQuery(typeof(ItemRegistry)).CalculateEntityCount() > 0);

        // Act: Update system
        _initConfigHandle.Update(_world.Unmanaged);

        // Assert: Singleton created and BlobAsset populated
        var query = _world.EntityManager.CreateEntityQuery(typeof(ItemRegistry));
        Assert.AreEqual(1, query.CalculateEntityCount());
        var singleton = query.GetSingleton<ItemRegistry>();
        Assert.IsTrue(singleton.Value.IsCreated);
        Assert.AreEqual(System.Enum.GetValues(typeof(ItemTypeEnum)).Length, singleton.Value.Value.Items.Length);
        Assert.Greater(singleton.Value.Value.GetMaxStack(ItemTypeEnum.Iron), 0);
    }

}
