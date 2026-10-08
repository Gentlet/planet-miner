using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;

/// <summary>
/// 역할·목적: 아이템 설정 JSON과 품목별 MaxStack 게시에 대한 NUnit EditMode 회귀 검증.
/// 입력·검사: 사용자 JSON/초기화 Update로 지정 값·미등록 종류 기본 조회·자동 Registry 게시를 검사한다.
/// 수명: EcsWorldTestFixture가 각 사례의 독립 World를 준비하고 종료 시 해제한다.
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
    public void Test03_CustomJson_PublishesConfiguredValues()
    {
        // 모든 실제 품목의 명시적 설정을 같은 원본 버퍼에서 읽는다.
        // Arrange: Complete JSON with configured MaxStack
        string customJson = @"{
            ""Items"": [
                { ""ItemType"": ""Iron_Ore"", ""MaxStack"": 999 },
                { ""ItemType"": ""Copper_Ore"", ""MaxStack"": 50 },
                { ""ItemType"": ""Coal"", ""MaxStack"": 50 },
                { ""ItemType"": ""Stone"", ""MaxStack"": 50 },
                { ""ItemType"": ""Iron"", ""MaxStack"": 100 },
                { ""ItemType"": ""Copper"", ""MaxStack"": 100 },
                { ""ItemType"": ""Iron_Stick"", ""MaxStack"": 100 },
                { ""ItemType"": ""Copper_Stick"", ""MaxStack"": 100 },
                { ""ItemType"": ""Drone"", ""MaxStack"": 5 }
            ]
        }";

        // Act
        Entity configEntity = ItemConfigInitSystem.InitializeItemRegistry(_entityManager, customJson);
        var items = _entityManager.GetBuffer<ItemConfigElement>(configEntity, true);

        // Assert: Configured item limits
        Assert.AreEqual(999, ItemRegistry.GetMaxStack(items, ItemTypeEnum.Iron_Ore));
        Assert.AreEqual(5, ItemRegistry.GetMaxStack(items, ItemTypeEnum.Drone));

        Assert.AreEqual(50, ItemRegistry.GetMaxStack(items, ItemTypeEnum.Copper_Ore));
        Assert.AreEqual(100, ItemRegistry.GetMaxStack(items, ItemTypeEnum.Iron));

        // An unregistered item has no capacity; it is not replaced with a default.
        var invalidType = (ItemTypeEnum)250;
        Assert.AreEqual(0, ItemRegistry.GetMaxStack(items, invalidType));
    }

    [Test]
    public void Test05_SystemUpdate_InitializesAutomatically()
    {
        // Pre-condition: No ItemRegistry singleton
        Assert.IsFalse(_world.EntityManager.CreateEntityQuery(typeof(ItemRegistry)).CalculateEntityCount() > 0);

        // Act: Update system
        _initConfigHandle.Update(_world.Unmanaged);

        // Assert: Singleton created and its item buffer populated
        var query = _world.EntityManager.CreateEntityQuery(typeof(ItemRegistry));
        Assert.AreEqual(1, query.CalculateEntityCount());
        Entity configEntity = query.GetSingletonEntity();
        Assert.IsTrue(_entityManager.HasBuffer<ItemConfigElement>(configEntity));
        var items = _entityManager.GetBuffer<ItemConfigElement>(configEntity, true);
        Assert.AreEqual(System.Enum.GetValues(typeof(ItemTypeEnum)).Length, items.Length);
        Assert.Greater(ItemRegistry.GetMaxStack(items, ItemTypeEnum.Iron), 0);
    }

}
