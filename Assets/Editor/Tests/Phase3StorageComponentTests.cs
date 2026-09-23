using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;

/// <summary>
/// Task 3.1: Storage 컴포넌트, FixedBitSet, StorageFilter, StoredItemElement 단위 테스트.
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
    public void Test03_StorageEntity_ComponentAndBufferInitialization()
    {
        // Arrange & Act
        var storageEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(storageEntity, new Storage(slotCount: 10));

        var filter = new StorageFilter(StorageFilterMode.Whitelist);
        filter.Mask.Set((byte)ItemTypeEnum.Iron_Ore, true);
        _entityManager.AddComponentData(storageEntity, filter);

        var buffer = _entityManager.AddBuffer<StoredItemElement>(storageEntity);

        // Create a dummy item entity
        var itemEntity = _entityManager.CreateEntity(typeof(ItemIdentity), typeof(ItemOwnership));
        _entityManager.SetComponentData(itemEntity, new ItemIdentity(ItemTypeEnum.Iron_Ore));
        _entityManager.SetComponentData(itemEntity, ItemOwnership.Stored(storageEntity));

        // Add to buffer
        buffer.Add(new StoredItemElement(itemEntity, ItemTypeEnum.Iron_Ore, slotIndex: 0));

        // Assert
        var storage = _entityManager.GetComponentData<Storage>(storageEntity);
        Assert.AreEqual(10, storage.SlotCount);

        var retrievedFilter = _entityManager.GetComponentData<StorageFilter>(storageEntity);
        Assert.AreEqual(StorageFilterMode.Whitelist, retrievedFilter.Mode);
        Assert.IsTrue(retrievedFilter.IsItemAllowed(ItemTypeEnum.Iron_Ore));

        var retrievedBuffer = _entityManager.GetBuffer<StoredItemElement>(storageEntity);
        Assert.AreEqual(1, retrievedBuffer.Length);
        Assert.AreEqual(itemEntity, retrievedBuffer[0].ItemEntity);
        Assert.AreEqual(ItemTypeEnum.Iron_Ore, retrievedBuffer[0].ItemType);
        Assert.AreEqual(0, retrievedBuffer[0].SlotIndex);
    }
}
