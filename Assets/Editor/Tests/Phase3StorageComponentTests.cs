using NUnit.Framework;
using PlanetMiner.Tests;

/// <summary>
/// Storage 컴포넌트, FixedBitSet, StorageFilter, StoredItemElement 단위 테스트.
/// </summary>
public class Phase3StorageComponentTests : EcsWorldTestFixture
{
    [Test]
    public void Test01_FixedBitSet_SetAndGet_OperatesAccurately()
    {
        // Arrange
        var bitSet = new FixedBitSet();

        // Initial state: all false
        Assert.IsFalse(bitSet.IsSet(0));
        Assert.IsFalse(bitSet.IsSet(63));
        Assert.IsFalse(bitSet.IsSet(64));
        Assert.IsFalse(bitSet.IsSet(127));

        // Act & Assert: Boundary bit operations
        bitSet.Set(0, true);
        bitSet.Set(63, true);
        bitSet.Set(64, true);
        bitSet.Set(127, true);

        Assert.IsTrue(bitSet.IsSet(0));
        Assert.IsTrue(bitSet.IsSet(63));
        Assert.IsTrue(bitSet.IsSet(64));
        Assert.IsTrue(bitSet.IsSet(127));

        // Other bits remain false
        Assert.IsFalse(bitSet.IsSet(1));
        Assert.IsFalse(bitSet.IsSet(62));
        Assert.IsFalse(bitSet.IsSet(65));
        Assert.IsFalse(bitSet.IsSet(126));

        // Unset
        bitSet.Set(64, false);
        Assert.IsFalse(bitSet.IsSet(64));

        // Clear
        bitSet.Clear();
        Assert.IsFalse(bitSet.IsSet(0));
        Assert.IsFalse(bitSet.IsSet(63));
        Assert.IsFalse(bitSet.IsSet(127));
    }

    [Test]
    public void Test02_StorageFilter_Modes_RespectMaskRules()
    {
        // AllowAll
        var allowAll = new StorageFilter(StorageFilterMode.AllowAll);
        Assert.IsTrue(allowAll.IsItemAllowed(ItemTypeEnum.Iron_Ore));
        Assert.IsTrue(allowAll.IsItemAllowed(ItemTypeEnum.Copper_Ore));
        Assert.IsTrue(allowAll.IsItemAllowed(ItemTypeEnum.Coal));
        Assert.IsTrue(allowAll.IsItemAllowed(ItemTypeEnum.Stone));

        // Whitelist
        var whitelist = new StorageFilter(StorageFilterMode.Whitelist);
        whitelist.Mask.Set((byte)ItemTypeEnum.Iron_Ore, true);
        whitelist.Mask.Set((byte)ItemTypeEnum.Coal, true);

        Assert.IsTrue(whitelist.IsItemAllowed(ItemTypeEnum.Iron_Ore));
        Assert.IsTrue(whitelist.IsItemAllowed(ItemTypeEnum.Coal));
        Assert.IsFalse(whitelist.IsItemAllowed(ItemTypeEnum.Copper_Ore));
        Assert.IsFalse(whitelist.IsItemAllowed(ItemTypeEnum.Stone));
        Assert.IsFalse(whitelist.IsItemAllowed(ItemTypeEnum.Iron));

        // Blacklist
        var blacklist = new StorageFilter(StorageFilterMode.Blacklist);
        blacklist.Mask.Set((byte)ItemTypeEnum.Stone, true);

        Assert.IsFalse(blacklist.IsItemAllowed(ItemTypeEnum.Stone));
        Assert.IsTrue(blacklist.IsItemAllowed(ItemTypeEnum.Iron_Ore));
        Assert.IsTrue(blacklist.IsItemAllowed(ItemTypeEnum.Copper_Ore));
        Assert.IsTrue(blacklist.IsItemAllowed(ItemTypeEnum.Coal));
    }

    [Test]
    public void Test04_StorageSlotCount_ExceedingMaxStorageSlots_ViolatesStorageInvariant()
    {
        // Arrange: MaxStorageSlots(120) 초과 SlotCount = 200 창고 생성
        var validationSystem = _world.GetOrCreateSystemManaged<WorldInvariantValidationSystem>();
        var storageEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(storageEntity, new Storage(slotCount: 200));
        _entityManager.AddBuffer<StoredItemElement>(storageEntity);

        // Act
        validationSystem.ResetViolationCount();
        validationSystem.Update();

        // Assert: SlotCount 초과에 대한 StorageInvariant 위반 검출 확인
        Assert.Greater(validationSystem.TotalViolationCount, 0,
            "SlotCount exceeding MaxStorageSlots must trigger StorageInvariant violation.");
    }

    [Test]
    public void Test05_StorageSlotCount_ZeroOrNegative_ViolatesStorageInvariant()
    {
        // Arrange: 비정상 SlotCount = 0 창고 생성
        var validationSystem = _world.GetOrCreateSystemManaged<WorldInvariantValidationSystem>();
        var storageEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(storageEntity, new Storage(slotCount: 0));
        _entityManager.AddBuffer<StoredItemElement>(storageEntity);

        // Act
        validationSystem.ResetViolationCount();
        validationSystem.Update();

        // Assert: 0 이하 SlotCount에 대한 StorageInvariant 위반 검출 확인
        Assert.Greater(validationSystem.TotalViolationCount, 0,
            "SlotCount <= 0 must trigger StorageInvariant violation.");
    }

    [Test]
    public void Test06_StorageSlotCount_MaxStorageSlotsBoundary_MaintainsInvariants()
    {
        // Arrange: 상한 경계값 SlotCount = 120 정상 창고 생성
        var validationSystem = _world.GetOrCreateSystemManaged<WorldInvariantValidationSystem>();
        var storageEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(storageEntity, new Storage(slotCount: GameConstants.MaxStorageSlots));
        _entityManager.AddBuffer<StoredItemElement>(storageEntity);

        // Act
        validationSystem.ResetViolationCount();
        validationSystem.Update();

        // Assert: 상한 경계값 정상 통과 및 위반 0건 확인
        Assert.AreEqual(0, validationSystem.TotalViolationCount,
            "SlotCount == MaxStorageSlots must maintain valid invariants with zero violations.");
    }
}
