using Unity.Collections;
using Unity.Entities;

public enum BuildingInputSlotCalculationErrorEnum : byte
{
    None,
    InvalidIngredientAmount,
    UnregisteredItemType,
    InvalidMaxStack,
    SlotLimitExceeded
}

/// <summary>
/// ECS 상태를 변경하거나 메모리를 할당하지 않는 건물 공통 입력 슬롯 계산.
/// 유효한 재료 목록/아이템 Blob 참조를 호출자가 제공하며, 결과 적용과 오류 처리는 호출자가 소유한다.
/// </summary>
public static class BuildingInputSlotUtility
{
    /// <summary>
    /// 동일 품목의 요구량을 합산하고, 전달된 요구량을 담을 최소 스택 수만큼 전용 슬롯을 만든다.
    /// 품목 순서는 재료 목록의 최초 등장 순서이며 같은 품목의 슬롯은 연속이다.
    /// RecipeIngredientBlob의 품목/수량만 사용하며 제작 시간, 출력물, 건물 종류에는 의존하지 않는다.
    /// 성공 시 slots.Length가 입력 용량이며, 빈 재료 목록은 0슬롯이다.
    /// 실패 시 slots는 항상 비어 있으므로 부분 구성을 적용하지 않는다.
    /// 슬롯은 필요 수량이 아닌 ItemRegistry의 MaxStack까지 수용한다.
    /// </summary>
    public static bool TryCalculate(
        ref BlobArray<RecipeIngredientBlob> ingredients,
        ref ItemRegistryBlob itemRegistry,
        out FixedList512Bytes<BuildingInputSlotElement> slots,
        out BuildingInputSlotCalculationErrorEnum error)
    {
        slots = default;
        error = ValidateIngredients(ref ingredients, ref itemRegistry);
        if (error != BuildingInputSlotCalculationErrorEnum.None)
        {
            return false;
        }

        FixedList512Bytes<BuildingInputSlotElement> calculatedSlots = default;
        for (int i = 0; i < ingredients.Length; i++)
        {
            ItemTypeEnum itemType = ingredients[i].ItemType;
            if (HasEarlierIngredient(ref ingredients, i, itemType))
            {
                continue;
            }

            long requiredAmount = SumRequiredAmount(ref ingredients, i, itemType);
            int maxStack = itemRegistry.GetMaxStack(itemType);
            long requiredSlots = (requiredAmount - 1) / maxStack + 1;
            if (requiredSlots > GameConstants.MaxStorageSlots - calculatedSlots.Length)
            {
                error = BuildingInputSlotCalculationErrorEnum.SlotLimitExceeded;
                return false;
            }

            for (int slot = 0; slot < requiredSlots; slot++)
            {
                calculatedSlots.Add(new BuildingInputSlotElement(itemType));
            }
        }

        slots = calculatedSlots;
        return true;
    }

    private static BuildingInputSlotCalculationErrorEnum ValidateIngredients(
        ref BlobArray<RecipeIngredientBlob> ingredients,
        ref ItemRegistryBlob itemRegistry)
    {
        for (int i = 0; i < ingredients.Length; i++)
        {
            var ingredient = ingredients[i];
            if (ingredient.Amount <= 0)
            {
                return BuildingInputSlotCalculationErrorEnum.InvalidIngredientAmount;
            }

            int itemIndex = (int)ingredient.ItemType;
            if (ingredient.ItemType == ItemTypeEnum.None || itemIndex >= itemRegistry.Items.Length)
            {
                return BuildingInputSlotCalculationErrorEnum.UnregisteredItemType;
            }

            if (itemRegistry.Items[itemIndex].ItemType != ingredient.ItemType)
            {
                return BuildingInputSlotCalculationErrorEnum.UnregisteredItemType;
            }

            if (itemRegistry.GetMaxStack(ingredient.ItemType) <= 0)
            {
                return BuildingInputSlotCalculationErrorEnum.InvalidMaxStack;
            }
        }

        return BuildingInputSlotCalculationErrorEnum.None;
    }

    private static bool HasEarlierIngredient(
        ref BlobArray<RecipeIngredientBlob> ingredients, int index, ItemTypeEnum itemType)
    {
        for (int i = 0; i < index; i++)
        {
            if (ingredients[i].ItemType == itemType)
            {
                return true;
            }
        }

        return false;
    }

    private static long SumRequiredAmount(
        ref BlobArray<RecipeIngredientBlob> ingredients, int firstIndex, ItemTypeEnum itemType)
    {
        long amount = 0;
        for (int i = firstIndex; i < ingredients.Length; i++)
        {
            if (ingredients[i].ItemType == itemType)
            {
                amount += ingredients[i].Amount;
            }
        }

        return amount;
    }
}
