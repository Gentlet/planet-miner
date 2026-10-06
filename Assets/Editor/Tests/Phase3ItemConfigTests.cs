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
    public void Test03_CustomJson_OverridesValues()
    {
        // 지정/미지정 품목을 같은 Registry에서 읽어 덮어쓴 값과 기본 규칙을 구분한다.
        // Arrange: Custom JSON with custom MaxStack
        string customJson = @"{
            ""DefaultMaxStack"": 64,
            ""Items"": [
                { ""ItemType"": ""Iron_Ore"", ""MaxStack"": 999 },
                { ""ItemType"": ""Drone"", ""MaxStack"": 5 }
            ]
        }";

        // Act
        Entity configEntity = ItemConfigInitSystem.InitializeItemRegistry(_entityManager, customJson);
        var registry = _entityManager.GetComponentData<ItemRegistry>(configEntity);
        var items = _entityManager.GetBuffer<ItemConfigElement>(configEntity, true);

        // Assert: Overridden items
        Assert.AreEqual(999, registry.GetMaxStack(items, ItemTypeEnum.Iron_Ore));
        Assert.AreEqual(5, registry.GetMaxStack(items, ItemTypeEnum.Drone));

        // Non-overridden items keep their default/fallback values
        Assert.AreEqual(50, registry.GetMaxStack(items, ItemTypeEnum.Copper_Ore));
        Assert.AreEqual(100, registry.GetMaxStack(items, ItemTypeEnum.Iron));

        // Fallback uses DefaultMaxStack (64)
        Assert.AreEqual(64, registry.DefaultMaxStack);
        var invalidType = (ItemTypeEnum)250;
        Assert.AreEqual(64, registry.GetMaxStack(items, invalidType));
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
        var singleton = query.GetSingleton<ItemRegistry>();
        Entity configEntity = query.GetSingletonEntity();
        Assert.IsTrue(_entityManager.HasBuffer<ItemConfigElement>(configEntity));
        var items = _entityManager.GetBuffer<ItemConfigElement>(configEntity, true);
        Assert.AreEqual(System.Enum.GetValues(typeof(ItemTypeEnum)).Length, items.Length);
        Assert.Greater(singleton.GetMaxStack(items, ItemTypeEnum.Iron), 0);
    }

}
