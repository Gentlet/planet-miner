using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

public class Phase5CrafterInputSlotTests : EcsWorldTestFixture
{
    private Entity _recipe;
    private Entity _items;

    [Test]
    public void EmptyIngredients_ProduceZeroSlots()
    {
        CreateInputs(100, 50);

        var slots = Calculate();

        Assert.AreEqual(0, slots.Length);
    }

    [TestCase(1, 1)]
    [TestCase(99, 1)]
    [TestCase(100, 1)]
    [TestCase(101, 2)]
    [TestCase(200, 2)]
    public void RequiredAmount_UsesMinimumWholeStacks(int amount, int expectedSlots)
    {
        CreateInputs(100, 50, new RecipeIngredientElement(ItemTypeEnum.Iron, amount));

        var slots = Calculate();

        Assert.AreEqual(expectedSlots, slots.Length);
        for (int i = 0; i < slots.Length; i++)
        {
            Assert.AreEqual(ItemTypeEnum.Iron, slots[i].ItemType);
        }
    }

    [Test]
    public void DuplicateIngredients_AreSummedBeforeRounding_AndGroupedByFirstAppearance()
    {
        CreateInputs(100, 50,
            new RecipeIngredientElement(ItemTypeEnum.Copper, 30),
            new RecipeIngredientElement(ItemTypeEnum.Iron, 20),
            new RecipeIngredientElement(ItemTypeEnum.Copper, 80),
            new RecipeIngredientElement(ItemTypeEnum.Iron, 20));

        var slots = Calculate();

        Assert.AreEqual(4, slots.Length);
        Assert.AreEqual(ItemTypeEnum.Copper, slots[0].ItemType);
        Assert.AreEqual(ItemTypeEnum.Copper, slots[1].ItemType);
        Assert.AreEqual(ItemTypeEnum.Copper, slots[2].ItemType);
        Assert.AreEqual(ItemTypeEnum.Iron, slots[3].ItemType);
        // 계산은 원본 레시피를 변경하지 않는다.
        var ingredients = _entityManager.GetBuffer<RecipeIngredientElement>(_recipe, true);
        Assert.AreEqual(30, ingredients[0].Amount);
        Assert.AreEqual(4, ingredients.Length);
    }

    [Test]
    public void DuplicateAmountAboveIntMaxValue_DoesNotOverflow()
    {
        CreateInputs(int.MaxValue, 50,
            new RecipeIngredientElement(ItemTypeEnum.Iron, int.MaxValue),
            new RecipeIngredientElement(ItemTypeEnum.Iron, int.MaxValue));

        var slots = Calculate();

        Assert.AreEqual(2, slots.Length);
        Assert.AreEqual(ItemTypeEnum.Iron, slots[0].ItemType);
        Assert.AreEqual(ItemTypeEnum.Iron, slots[1].ItemType);
    }

    [Test]
    public void ExactStorageSlotLimit_IsAccepted()
    {
        CreateInputs(100, 50,
            new RecipeIngredientElement(ItemTypeEnum.Copper, 50),
            new RecipeIngredientElement(ItemTypeEnum.Iron, 100 * (GameConstants.MaxStorageSlots - 1)));

        var slots = Calculate();

        Assert.AreEqual(GameConstants.MaxStorageSlots, slots.Length);
        Assert.AreEqual(ItemTypeEnum.Copper, slots[0].ItemType);
        Assert.AreEqual(ItemTypeEnum.Iron, slots[slots.Length - 1].ItemType);
    }

    [Test]
    public void TotalSlotsAboveLimit_ReturnsNoPartialLayout()
    {
        CreateInputs(100, 50,
            new RecipeIngredientElement(ItemTypeEnum.Copper, 50),
            new RecipeIngredientElement(ItemTypeEnum.Iron, 100 * GameConstants.MaxStorageSlots));

        AssertCalculationFails(BuildingInputSlotCalculationErrorEnum.SlotLimitExceeded);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void InvalidDuplicateAmount_IsNotHiddenByValidRows(int amount)
    {
        CreateInputs(100, 50,
            new RecipeIngredientElement(ItemTypeEnum.Iron, 10),
            new RecipeIngredientElement(ItemTypeEnum.Iron, amount));

        AssertCalculationFails(BuildingInputSlotCalculationErrorEnum.InvalidIngredientAmount);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void InvalidMaxStack_DoesNotUseRegistryDefault(int maxStack)
    {
        CreateInputs(maxStack, 50, new RecipeIngredientElement(ItemTypeEnum.Iron, 1));

        AssertCalculationFails(BuildingInputSlotCalculationErrorEnum.InvalidMaxStack);
    }

    [TestCase(ItemTypeEnum.None)]
    [TestCase((ItemTypeEnum)255)]
    public void UnregisteredIngredient_DoesNotUseRegistryDefault(ItemTypeEnum itemType)
    {
        CreateInputs(100, 50, new RecipeIngredientElement(itemType, 1));

        AssertCalculationFails(BuildingInputSlotCalculationErrorEnum.UnregisteredItemType);
    }

    [Test]
    public void MismatchedRegistryEntry_IsRejected()
    {
        CreateRecipe(new[] { new RecipeIngredientElement(ItemTypeEnum.Iron, 1) });
        CreateItems(100, 50, mismatchIronEntry: true);

        AssertCalculationFails(BuildingInputSlotCalculationErrorEnum.UnregisteredItemType);
    }

    [Test]
    public void CalculationRunsInsideBurstJob()
    {
        CreateInputs(100, 50,
            new RecipeIngredientElement(ItemTypeEnum.Iron, 150),
            new RecipeIngredientElement(ItemTypeEnum.Copper, 1));
        using var slots = new NativeReference<FixedList512Bytes<BuildingInputSlotElement>>(Allocator.TempJob);
        using var error = new NativeReference<BuildingInputSlotCalculationErrorEnum>(Allocator.TempJob);

        new CalculateSlotsJob
        {
            Ingredients = _entityManager.GetBuffer<RecipeIngredientElement>(_recipe, true),
            ItemRegistry = _entityManager.GetComponentData<ItemRegistry>(_items),
            Items = _entityManager.GetBuffer<ItemConfigElement>(_items, true),
            Slots = slots,
            Error = error
        }.Schedule().Complete();

        Assert.AreEqual(BuildingInputSlotCalculationErrorEnum.None, error.Value);
        Assert.AreEqual(3, slots.Value.Length);
        Assert.AreEqual(ItemTypeEnum.Iron, slots.Value[0].ItemType);
        Assert.AreEqual(ItemTypeEnum.Iron, slots.Value[1].ItemType);
        Assert.AreEqual(ItemTypeEnum.Copper, slots.Value[2].ItemType);
    }

    private FixedList512Bytes<BuildingInputSlotElement> Calculate()
    {
        var ingredients = _entityManager.GetBuffer<RecipeIngredientElement>(_recipe, true);
        bool success = BuildingInputSlotUtility.TryCalculate(
            ingredients, 0, ingredients.Length,
            _entityManager.GetComponentData<ItemRegistry>(_items),
            _entityManager.GetBuffer<ItemConfigElement>(_items, true), out var slots, out var error);
        Assert.IsTrue(success, error.ToString());
        Assert.AreEqual(BuildingInputSlotCalculationErrorEnum.None, error);
        return slots;
    }

    private void AssertCalculationFails(BuildingInputSlotCalculationErrorEnum expectedError)
    {
        var ingredients = _entityManager.GetBuffer<RecipeIngredientElement>(_recipe, true);
        bool success = BuildingInputSlotUtility.TryCalculate(
            ingredients, 0, ingredients.Length,
            _entityManager.GetComponentData<ItemRegistry>(_items),
            _entityManager.GetBuffer<ItemConfigElement>(_items, true), out var slots, out var error);
        Assert.IsFalse(success);
        Assert.AreEqual(expectedError, error);
        Assert.AreEqual(0, slots.Length, "실패한 계산은 부분 슬롯 구성을 공개하면 안 된다.");
    }

    private void CreateInputs(int ironMaxStack, int copperMaxStack, params RecipeIngredientElement[] ingredients)
    {
        CreateRecipe(ingredients);
        CreateItems(ironMaxStack, copperMaxStack);
    }

    private void CreateRecipe(RecipeIngredientElement[] ingredients)
    {
        _recipe = _entityManager.CreateEntity();
        var input = _entityManager.AddBuffer<RecipeIngredientElement>(_recipe);
        for (int i = 0; i < ingredients.Length; i++)
        {
            input.Add(ingredients[i]);
        }
    }

    private void CreateItems(int ironMaxStack, int copperMaxStack, bool mismatchIronEntry = false)
    {
        // Invalid input rows are deliberately constructed only in these utility tests.
        _items = _entityManager.CreateEntity(typeof(ItemRegistry));
        _entityManager.SetComponentData(_items, new ItemRegistry { DefaultMaxStack = 50 });
        var entries = _entityManager.AddBuffer<ItemConfigElement>(_items);
        for (int i = 0; i <= (int)ItemTypeEnum.Copper; i++)
        {
            entries.Add(new ItemConfigElement((ItemTypeEnum)i, 50));
        }

        entries[(int)ItemTypeEnum.Iron] = new ItemConfigElement(
            mismatchIronEntry ? ItemTypeEnum.Copper : ItemTypeEnum.Iron, ironMaxStack);
        entries[(int)ItemTypeEnum.Copper] = new ItemConfigElement(ItemTypeEnum.Copper, copperMaxStack);
    }

    [BurstCompile(CompileSynchronously = true)]
    private struct CalculateSlotsJob : IJob
    {
        [ReadOnly] public DynamicBuffer<RecipeIngredientElement> Ingredients;
        public ItemRegistry ItemRegistry;
        [ReadOnly] public DynamicBuffer<ItemConfigElement> Items;
        public NativeReference<FixedList512Bytes<BuildingInputSlotElement>> Slots;
        public NativeReference<BuildingInputSlotCalculationErrorEnum> Error;

        public void Execute()
        {
            BuildingInputSlotUtility.TryCalculate(
                Ingredients, 0, Ingredients.Length, ItemRegistry, Items, out var slots, out var error);
            Slots.Value = slots;
            Error.Value = error;
        }
    }
}
