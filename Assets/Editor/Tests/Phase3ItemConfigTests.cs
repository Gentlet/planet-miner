using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

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
    public void Test01_InitializeItemRegistry_CreatesSingletonAndBlobArray()
    {
        // Act
        _blobRef = ItemConfigInitSystem.InitializeItemRegistry(_entityManager);

        // Assert
        Assert.IsTrue(_blobRef.IsCreated);
        ref var registry = ref _blobRef.Value;
        Assert.AreEqual(System.Enum.GetValues(typeof(ItemTypeEnum)).Length, registry.Items.Length);

        var query = _entityManager.CreateEntityQuery(typeof(ItemRegistry));
        Assert.AreEqual(1, query.CalculateEntityCount());
        var singleton = query.GetSingleton<ItemRegistry>();
        Assert.IsTrue(singleton.Value.IsCreated);
    }

    [Test]
    public void Test02_DefaultConfig_MapsCorrectMaxStacks()
    {
        // Act: Initialize with null json (fallback defaults)
        _blobRef = ItemConfigInitSystem.InitializeItemRegistry(_entityManager);
        ref var registry = ref _blobRef.Value;

        // Assert
        Assert.AreEqual(0, registry.GetMaxStack(ItemTypeEnum.None));
        Assert.AreEqual(50, registry.GetMaxStack(ItemTypeEnum.Iron_Ore));
        Assert.AreEqual(50, registry.GetMaxStack(ItemTypeEnum.Copper_Ore));
        Assert.AreEqual(50, registry.GetMaxStack(ItemTypeEnum.Coal));
        Assert.AreEqual(50, registry.GetMaxStack(ItemTypeEnum.Stone));
        Assert.AreEqual(100, registry.GetMaxStack(ItemTypeEnum.Iron));
        Assert.AreEqual(100, registry.GetMaxStack(ItemTypeEnum.Copper));
        Assert.AreEqual(100, registry.GetMaxStack(ItemTypeEnum.Iron_Stick));
        Assert.AreEqual(100, registry.GetMaxStack(ItemTypeEnum.Copper_Stick));
        Assert.AreEqual(1, registry.GetMaxStack(ItemTypeEnum.Drone));
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
    public void Test04_LookupAndFallback_Behaviors()
    {
        // Arrange
        _blobRef = ItemConfigInitSystem.InitializeItemRegistry(_entityManager);
        ref var registry = ref _blobRef.Value;

        // Direct array lookup equals helper method
        Assert.AreEqual(registry.Items[(int)ItemTypeEnum.Iron_Ore].MaxStack, registry.GetMaxStack(ItemTypeEnum.Iron_Ore));

        // Fallback with invalid type returns DefaultMaxStack (50)
        var invalidType = (ItemTypeEnum)250;
        Assert.AreEqual(50, registry.GetMaxStack(invalidType));

        // Fallback with explicit value
        Assert.AreEqual(77, registry.GetMaxStack(invalidType, fallbackMaxStack: 77));
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

    [Test]
    public void Test06_BurstJob_CanReadItemRegistryBlob_WithoutSafetyErrors()
    {
        // 1. 전역 싱글톤 초기화
        _blobRef = ItemConfigInitSystem.InitializeItemRegistry(_entityManager);
        var registry = _entityManager.CreateEntityQuery(typeof(ItemRegistry)).GetSingleton<ItemRegistry>();

        var resultStack = new NativeReference<int>(Allocator.TempJob);

        try
        {
            // 2. Burst Job 실행 (Iron_Stick -> MaxStack 100)
            var job = new BurstItemReadTestJob
            {
                Registry = registry,
                TargetItemType = ItemTypeEnum.Iron_Stick,
                ResultMaxStack = resultStack
            };

            var handle = IJobExtensions.Schedule(job);
            handle.Complete();

            // 3. Job 실행 결과 확인
            Assert.AreEqual(100, resultStack.Value, "Burst job should read Iron_Stick MaxStack (100) successfully.");
        }
        finally
        {
            resultStack.Dispose();
        }
    }
}

/// <summary>
/// Burst Job 내부에서 ItemRegistryBlob 포인터 안전 접근성을 검증하는 테스트용 Job.
/// </summary>
[BurstCompile]
public struct BurstItemReadTestJob : IJob
{
    [ReadOnly]
    public ItemRegistry Registry;

    public ItemTypeEnum TargetItemType;

    public NativeReference<int> ResultMaxStack;

    public void Execute()
    {
        ref var registryBlob = ref Registry.Value.Value;
        ResultMaxStack.Value = registryBlob.GetMaxStack(TargetItemType);
    }
}
