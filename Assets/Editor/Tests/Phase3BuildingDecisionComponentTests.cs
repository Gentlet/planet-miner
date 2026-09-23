using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// BuildingItemInputDecision 및 BuildingItemOutputDecision 단위 테스트.
/// </summary>
public class Phase3BuildingDecisionComponentTests : EcsWorldTestFixture
{
    [Test]
    public void Test01_BuildingItemInputDecision_InitializationAndEnableable()
    {
        // Arrange
        var itemEntity = _entityManager.CreateEntity(typeof(BuildingItemInputDecision));
        var dummyBuilding = _entityManager.CreateEntity();

        // Act: Initial values
        _entityManager.SetComponentData(itemEntity, new BuildingItemInputDecision(dummyBuilding, canDeposit: false, targetSlotIndex: -1));

        // Assert: Field values
        var decision = _entityManager.GetComponentData<BuildingItemInputDecision>(itemEntity);
        Assert.AreEqual(dummyBuilding, decision.TargetBuilding);
        Assert.IsFalse(decision.CanDeposit);
        Assert.AreEqual(-1, decision.TargetSlotIndex);

        // IEnableableComponent behavior: Disable
        _entityManager.SetComponentEnabled<BuildingItemInputDecision>(itemEntity, false);
        Assert.IsFalse(_entityManager.IsComponentEnabled<BuildingItemInputDecision>(itemEntity));

        var enabledQuery = _world.EntityManager.CreateEntityQuery(typeof(BuildingItemInputDecision));
        Assert.AreEqual(0, enabledQuery.CalculateEntityCount(), "Disabled decision component should not match standard query.");

        // IEnableableComponent behavior: Enable
        _entityManager.SetComponentEnabled<BuildingItemInputDecision>(itemEntity, true);
        Assert.IsTrue(_entityManager.IsComponentEnabled<BuildingItemInputDecision>(itemEntity));
        Assert.AreEqual(1, enabledQuery.CalculateEntityCount(), "Enabled decision component should match query.");
    }

    [Test]
    public void Test02_BuildingItemOutputDecision_InitializationAndEnableable()
    {
        // Arrange
        var buildingEntity = _entityManager.CreateEntity(typeof(BuildingItemOutputDecision));
        var dummyItem = _entityManager.CreateEntity();
        var targetBeltPos = new int2(3, 7);

        // Act: Initial values
        _entityManager.SetComponentData(buildingEntity, new BuildingItemOutputDecision(canOutput: true, itemToOutput: dummyItem, targetBeltPosition: targetBeltPos));

        // Assert: Field values
        var decision = _entityManager.GetComponentData<BuildingItemOutputDecision>(buildingEntity);
        Assert.IsTrue(decision.CanOutput);
        Assert.AreEqual(dummyItem, decision.ItemToOutput);
        Assert.AreEqual(targetBeltPos, decision.TargetBeltPosition);

        // IEnableableComponent behavior: Disable
        _entityManager.SetComponentEnabled<BuildingItemOutputDecision>(buildingEntity, false);
        Assert.IsFalse(_entityManager.IsComponentEnabled<BuildingItemOutputDecision>(buildingEntity));

        var enabledQuery = _world.EntityManager.CreateEntityQuery(typeof(BuildingItemOutputDecision));
        Assert.AreEqual(0, enabledQuery.CalculateEntityCount(), "Disabled decision component should not match query.");

        // IEnableableComponent behavior: Re-enable
        _entityManager.SetComponentEnabled<BuildingItemOutputDecision>(buildingEntity, true);
        Assert.IsTrue(_entityManager.IsComponentEnabled<BuildingItemOutputDecision>(buildingEntity));
        Assert.AreEqual(1, enabledQuery.CalculateEntityCount());
    }
}
