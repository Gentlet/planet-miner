using Unity.Collections;
using Unity.Entities;

/// <summary>검증/슬롯 계산 실패 사유. ECS 상태가 아닌 호출자 반환 값이며 부분 슬롯 적용 여부를 결정한다.</summary>
public enum BuildingInputSlotCalculationErrorEnum : byte
{
    None,
    InvalidIngredientAmount,
    UnregisteredItemType,
    InvalidMaxStack,
    SlotLimitExceeded
}

/// <summary>
/// 역할·목적: 레시피 요구량과 품목별 MaxStack에서 전용 입력 슬롯 구성을 계산한다.
/// 입력·출력: 재료 범위와 품목 설정 버퍼를 읽어 FixedList 슬롯과 실패 사유를 반환한다. ECS 상태 변경/힙 할당은 하지 않는다.
/// 이용: CrafterRecipeCommandSystem은 성공 결과로 Storage·필터·입력 슬롯을 함께 바꾼다. 적용과 실패 처리, 범위 유효성은 호출자가 소유한다.
/// 수명: 결과는 호출자에게 반환하는 일시적인 값이다. 같은 품목의 요구량은 합치고 실패 시 부분 슬롯을 외부에 공개하지 않는다.
/// </summary>
public static class BuildingInputSlotUtility
{
    /// <summary>
    /// 동일 품목의 요구량을 합산하고, 전달된 요구량을 담을 최소 스택 수만큼 전용 슬롯을 만든다.
    /// 품목 순서는 재료 목록의 최초 등장 순서이며 같은 품목의 슬롯은 연속이다.
    /// RecipeIngredientElement의 품목/수량만 사용하며 제작 시간, 출력물, 건물 종류에는 의존하지 않는다.
    /// 성공 시 slots.Length가 입력 용량이며, 빈 재료 목록은 0슬롯이다.
    /// 실패 시 slots는 항상 비어 있으므로 부분 구성을 적용하지 않는다.
    /// 슬롯은 필요 수량이 아닌 ItemRegistry의 MaxStack까지 수용한다.
    /// </summary>
    public static bool TryCalculate(
        DynamicBuffer<RecipeIngredientElement> ingredients,
        int ingredientStart,
        int ingredientCount,
        DynamicBuffer<ItemConfigElement> items,
        out FixedList512Bytes<BuildingInputSlotElement> slots,
        out BuildingInputSlotCalculationErrorEnum error)
    {
        slots = default;
        error = ValidateIngredients(ingredients, ingredientStart, ingredientCount, items);
        if (error != BuildingInputSlotCalculationErrorEnum.None)
        {
            return false;
        }

        // 모든 품목 계산이 끝날 때까지 출력과 분리한 임시 목록에 적는다. 슬롯 상한 실패로 부분 구성이 적용되지 않게 한다.
        FixedList512Bytes<BuildingInputSlotElement> calculatedSlots = default;
        for (int i = 0; i < ingredientCount; i++)
        {
            ItemTypeEnum itemType = ingredients[ingredientStart + i].ItemType;
            long requiredAmount = SumRequiredAmountAtFirstOccurrence(ingredients, ingredientStart, ingredientCount, i, itemType);
            if (requiredAmount == 0)
            {
                continue;
            }

            int maxStack = ItemRegistry.GetMaxStack(items, itemType);
            long requiredSlots = RequiredSlotCount(requiredAmount, maxStack);
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

    /// <summary>검증된 비음수 수량과 양수 MaxStack의 슬롯 수를 오버플로 없이 올림 계산한다.</summary>
    internal static long RequiredSlotCount(long quantity, int maximumStack)
    {
        long slots = quantity / maximumStack;
        return quantity % maximumStack == 0 ? slots : slots + 1;
    }

    private static BuildingInputSlotCalculationErrorEnum ValidateIngredients(
        DynamicBuffer<RecipeIngredientElement> ingredients,
        int ingredientStart,
        int ingredientCount,
        DynamicBuffer<ItemConfigElement> items)
    {
        for (int i = 0; i < ingredientCount; i++)
        {
            var ingredient = ingredients[ingredientStart + i];
            if (ingredient.Amount <= 0)
            {
                return BuildingInputSlotCalculationErrorEnum.InvalidIngredientAmount;
            }

            int itemIndex = (int)ingredient.ItemType;
            if (ingredient.ItemType == ItemTypeEnum.None || itemIndex >= items.Length)
            {
                return BuildingInputSlotCalculationErrorEnum.UnregisteredItemType;
            }

            var item = items[itemIndex];
            if (item.ItemType != ingredient.ItemType)
            {
                return BuildingInputSlotCalculationErrorEnum.UnregisteredItemType;
            }

            if (item.MaxStack <= 0)
            {
                return BuildingInputSlotCalculationErrorEnum.InvalidMaxStack;
            }
        }

        return BuildingInputSlotCalculationErrorEnum.None;
    }

    private static long SumRequiredAmountAtFirstOccurrence(
        DynamicBuffer<RecipeIngredientElement> ingredients, int ingredientStart, int ingredientCount,
        int candidateIndex, ItemTypeEnum itemType)
    {
        long amount = 0;
        // ValidateIngredients가 양수 수량을 보장한다. 0은 앞선 행에서 이미 계산한 품목이다.
        for (int i = 0; i < ingredientCount; i++)
        {
            var ingredient = ingredients[ingredientStart + i];
            if (ingredient.ItemType != itemType)
            {
                continue;
            }
            if (i < candidateIndex)
            {
                return 0;
            }
            amount += ingredient.Amount;
        }

        return amount;
    }
}
