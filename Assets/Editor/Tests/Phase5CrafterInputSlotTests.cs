using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

public class Phase5CrafterInputSlotTests
{
    private BlobAssetReference<RecipeBlob> _recipe;
    private BlobAssetReference<ItemRegistryBlob> _items;

    [TearDown]
    public void TearDown()
    {
        if (_recipe.IsCreated)
        {
            _recipe.Dispose();
        }

        if (_items.IsCreated)
        {
            _items.Dispose();
        }
    }

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
        CreateInputs(100, 50, new RecipeIngredientBlob(ItemTypeEnum.Iron, amount));

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
            new RecipeIngredientBlob(ItemTypeEnum.Copper, 30),
            new RecipeIngredientBlob(ItemTypeEnum.Iron, 20),
            new RecipeIngredientBlob(ItemTypeEnum.Copper, 80),
            new RecipeIngredientBlob(ItemTypeEnum.Iron, 20));

        var slots = Calculate();

        Assert.AreEqual(4, slots.Length);
        Assert.AreEqual(ItemTypeEnum.Copper, slots[0].ItemType);
        Assert.AreEqual(ItemTypeEnum.Copper, slots[1].ItemType);
        Assert.AreEqual(ItemTypeEnum.Copper, slots[2].ItemType);
        Assert.AreEqual(ItemTypeEnum.Iron, slots[3].ItemType);
        // 계산은 원본 레시피를 변경하지 않는다.
        Assert.AreEqual(30, _recipe.Value.Ingredients[0].Amount);
        Assert.AreEqual(4, _recipe.Value.Ingredients.Length);
    }

    [Test]
    public void DuplicateAmountAboveIntMaxValue_DoesNotOverflow()
    {
        CreateInputs(int.MaxValue, 50,
            new RecipeIngredientBlob(ItemTypeEnum.Iron, int.MaxValue),
            new RecipeIngredientBlob(ItemTypeEnum.Iron, int.MaxValue));

        var slots = Calculate();

        Assert.AreEqual(2, slots.Length);
        Assert.AreEqual(ItemTypeEnum.Iron, slots[0].ItemType);
        Assert.AreEqual(ItemTypeEnum.Iron, slots[1].ItemType);
    }

    [Test]
    public void ExactStorageSlotLimit_IsAccepted()
    {
        CreateInputs(100, 50,
            new RecipeIngredientBlob(ItemTypeEnum.Copper, 50),
            new RecipeIngredientBlob(ItemTypeEnum.Iron, 100 * (GameConstants.MaxStorageSlots - 1)));

        var slots = Calculate();

        Assert.AreEqual(GameConstants.MaxStorageSlots, slots.Length);
        Assert.AreEqual(ItemTypeEnum.Copper, slots[0].ItemType);
        Assert.AreEqual(ItemTypeEnum.Iron, slots[slots.Length - 1].ItemType);
    }

    [Test]
    public void TotalSlotsAboveLimit_ReturnsNoPartialLayout()
    {
        CreateInputs(100, 50,
            new RecipeIngredientBlob(ItemTypeEnum.Copper, 50),
            new RecipeIngredientBlob(ItemTypeEnum.Iron, 100 * GameConstants.MaxStorageSlots));

        AssertCalculationFails(BuildingInputSlotCalculationErrorEnum.SlotLimitExceeded);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void InvalidDuplicateAmount_IsNotHiddenByValidRows(int amount)
    {
        CreateInputs(100, 50,
            new RecipeIngredientBlob(ItemTypeEnum.Iron, 10),
            new RecipeIngredientBlob(ItemTypeEnum.Iron, amount));

        AssertCalculationFails(BuildingInputSlotCalculationErrorEnum.InvalidIngredientAmount);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void InvalidMaxStack_DoesNotUseRegistryDefault(int maxStack)
    {
        CreateInputs(maxStack, 50, new RecipeIngredientBlob(ItemTypeEnum.Iron, 1));

        AssertCalculationFails(BuildingInputSlotCalculationErrorEnum.InvalidMaxStack);
    }

    [TestCase(ItemTypeEnum.None)]
    [TestCase((ItemTypeEnum)255)]
    public void UnregisteredIngredient_DoesNotUseRegistryDefault(ItemTypeEnum itemType)
    {
        CreateInputs(100, 50, new RecipeIngredientBlob(itemType, 1));

        AssertCalculationFails(BuildingInputSlotCalculationErrorEnum.UnregisteredItemType);
    }

    [Test]
    public void MismatchedRegistryEntry_IsRejected()
    {
        CreateRecipe(new[] { new RecipeIngredientBlob(ItemTypeEnum.Iron, 1) });
        CreateItems(100, 50, mismatchIronEntry: true);

        AssertCalculationFails(BuildingInputSlotCalculationErrorEnum.UnregisteredItemType);
    }

    [Test]
    public void CalculationRunsInsideBurstJob()
    {
        CreateInputs(100, 50,
            new RecipeIngredientBlob(ItemTypeEnum.Iron, 150),
            new RecipeIngredientBlob(ItemTypeEnum.Copper, 1));
        using var slots = new NativeReference<FixedList512Bytes<BuildingInputSlotElement>>(Allocator.TempJob);
        using var error = new NativeReference<BuildingInputSlotCalculationErrorEnum>(Allocator.TempJob);

        new CalculateSlotsJob
        {
            Recipe = _recipe,
            Items = _items,
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
        bool success = BuildingInputSlotUtility.TryCalculate(
            ref _recipe.Value.Ingredients, ref _items.Value, out var slots, out var error);
        Assert.IsTrue(success, error.ToString());
        Assert.AreEqual(BuildingInputSlotCalculationErrorEnum.None, error);
        return slots;
    }

    private void AssertCalculationFails(BuildingInputSlotCalculationErrorEnum expectedError)
    {
        bool success = BuildingInputSlotUtility.TryCalculate(
            ref _recipe.Value.Ingredients, ref _items.Value, out var slots, out var error);
        Assert.IsFalse(success);
        Assert.AreEqual(expectedError, error);
        Assert.AreEqual(0, slots.Length, "실패한 계산은 부분 슬롯 구성을 공개하면 안 된다.");
    }

    private void CreateInputs(int ironMaxStack, int copperMaxStack, params RecipeIngredientBlob[] ingredients)
    {
        CreateRecipe(ingredients);
        CreateItems(ironMaxStack, copperMaxStack);
    }

    private void CreateRecipe(RecipeIngredientBlob[] ingredients)
    {
        using (var builder = new BlobBuilder(Allocator.Temp))
        {
            ref var recipe = ref builder.ConstructRoot<RecipeBlob>();
            recipe.Id = 1;
            var input = builder.Allocate(ref recipe.Ingredients, ingredients.Length);
            builder.Allocate(ref recipe.Outputs, 0);
            for (int i = 0; i < ingredients.Length; i++)
            {
                input[i] = ingredients[i];
            }

            _recipe = builder.CreateBlobAssetReference<RecipeBlob>(Allocator.Persistent);
        }
    }

    private void CreateItems(int ironMaxStack, int copperMaxStack, bool mismatchIronEntry = false)
    {
        using (var builder = new BlobBuilder(Allocator.Temp))
        {
            ref var items = ref builder.ConstructRoot<ItemRegistryBlob>();
            items.DefaultMaxStack = 50;
            var entries = builder.Allocate(ref items.Items, (int)ItemTypeEnum.Copper + 1);
            for (int i = 0; i < entries.Length; i++)
            {
                entries[i] = new ItemDataBlob((ItemTypeEnum)i, 50);
            }

            entries[(int)ItemTypeEnum.Iron] = new ItemDataBlob(
                mismatchIronEntry ? ItemTypeEnum.Copper : ItemTypeEnum.Iron, ironMaxStack);
            entries[(int)ItemTypeEnum.Copper] = new ItemDataBlob(ItemTypeEnum.Copper, copperMaxStack);
            _items = builder.CreateBlobAssetReference<ItemRegistryBlob>(Allocator.Persistent);
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    private struct CalculateSlotsJob : IJob
    {
        [ReadOnly] public BlobAssetReference<RecipeBlob> Recipe;
        [ReadOnly] public BlobAssetReference<ItemRegistryBlob> Items;
        public NativeReference<FixedList512Bytes<BuildingInputSlotElement>> Slots;
        public NativeReference<BuildingInputSlotCalculationErrorEnum> Error;

        public void Execute()
        {
            BuildingInputSlotUtility.TryCalculate(
                ref Recipe.Value.Ingredients, ref Items.Value, out var slots, out var error);
            Slots.Value = slots;
            Error.Value = error;
        }
    }
}
