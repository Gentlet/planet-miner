using Unity.Entities;
using Unity.Mathematics;

/// <summary>현재 슬롯의 품목과 수량만 검사하고 갱신한다. 입고 자격과 경합은 호출 페이즈가 소유한다.</summary>
internal static class InventorySlotUtility
{
    public static int Count(in DynamicBuffer<StorageSlotElement> slots, ItemTypeEnum itemType)
    {
        int quantity = 0;
        foreach (var slot in slots)
        {
            if (slot.ItemType == itemType) quantity += slot.Quantity;
        }
        return quantity;
    }

    public static int FreeQuantity(in StorageSlotElement slot, ItemTypeEnum itemType, int maximumStack)
    {
        return slot.Quantity == 0 || slot.ItemType == itemType ? math.max(0, maximumStack - slot.Quantity) : 0;
    }

    public static bool TryAdd(DynamicBuffer<StorageSlotElement> slots, int index, ItemTypeEnum type, int quantity, int maximumStack)
    {
        if ((uint)index >= (uint)slots.Length || type == ItemTypeEnum.None || quantity <= 0) return false;
        var slot = slots[index];
        if (quantity > FreeQuantity(slot, type, maximumStack)) return false;
        slots[index] = new StorageSlotElement(type, slot.Quantity + quantity);
        return true;
    }

    public static int Take(DynamicBuffer<StorageSlotElement> slots, int index, ItemTypeEnum type, int quantity)
    {
        if ((uint)index >= (uint)slots.Length || quantity <= 0) return 0;
        var slot = slots[index];
        if (slot.ItemType != type || slot.Quantity <= 0) return 0;
        int taken = math.min(slot.Quantity, quantity);
        slot.Quantity -= taken;
        slots[index] = slot.Quantity == 0 ? default : slot;
        return taken;
    }

    // 호출자가 전체 재료 묶음을 검사한 뒤 낮은 슬롯부터 차감한다.
    public static void Consume(DynamicBuffer<StorageSlotElement> slots, ItemTypeEnum type, int quantity)
    {
        for (int index = 0; index < slots.Length && quantity > 0; index++)
        {
            quantity -= Take(slots, index, type, quantity);
        }
    }

    private static int FindProductInsertionIndex(in DynamicBuffer<ProductSlotElement> slots, int slotIndex)
    {
        for (int index = 0; index < slots.Length; index++)
        {
            if (slots[index].SlotIndex >= slotIndex) return index;
        }
        return slots.Length;
    }

    public static int ProductFreeQuantity(in DynamicBuffer<ProductSlotElement> slots, int slotIndex, ItemTypeEnum type, int maximumStack)
    {
        int index = FindProductInsertionIndex(slots, slotIndex);
        if (index == slots.Length || slots[index].SlotIndex != slotIndex) return maximumStack;
        var slot = slots[index];
        return slot.ItemType == type ? math.max(0, maximumStack - slot.Quantity) : 0;
    }

    public static bool TryAddProduct(DynamicBuffer<ProductSlotElement> slots, int slotIndex, ItemTypeEnum type, int quantity, int maximumStack)
    {
        if (slotIndex < 0 || type == ItemTypeEnum.None || quantity <= 0) return false;
        int index = FindProductInsertionIndex(slots, slotIndex);
        if (index == slots.Length || slots[index].SlotIndex != slotIndex)
        {
            if (quantity > maximumStack) return false;
            slots.Insert(index, new ProductSlotElement(slotIndex, type, quantity));
            return true;
        }

        var slot = slots[index];
        if (slot.ItemType != type || quantity > maximumStack - slot.Quantity) return false;
        slot.Quantity += quantity;
        slots[index] = slot;
        return true;
    }

    public static int TakeProduct(DynamicBuffer<ProductSlotElement> slots, int slotIndex, ItemTypeEnum type, int quantity)
    {
        int index = FindProductInsertionIndex(slots, slotIndex);
        if (index == slots.Length || quantity <= 0) return 0;
        var slot = slots[index];
        if (slot.SlotIndex != slotIndex || slot.ItemType != type) return 0;
        int taken = math.min(slot.Quantity, quantity);
        slot.Quantity -= taken;
        if (slot.Quantity == 0) slots.RemoveAt(index);
        else slots[index] = slot;
        return taken;
    }
}
