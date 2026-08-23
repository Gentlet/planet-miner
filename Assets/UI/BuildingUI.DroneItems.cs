using Unity.Entities;

public partial class BuildingUI
{
    private void BuildDroneItemChoices()
    {
        _transferItemTypes.Clear();
        _transferItemNames.Clear();

        for (ItemTypeEnum itemType = ItemTypeEnum.Iron_Ore;
             itemType < ItemTypeEnum.Count;
             itemType++)
        {
            _transferItemTypes.Add(itemType);
            _transferItemNames.Add(GetItemDisplayName(itemType));
        }

        _droneItemField.choices = _transferItemNames;
        _droneItemField.index = _transferItemTypes.Count > 0 ? 0 : -1;
        _droneQuantityField.value = 1;
    }

    private void RequestDroneItemInsertion()
    {
        if (!TryGetDroneTransferInput(
                out ItemTypeEnum itemType,
                out int quantity))
            return;

        DroneBuildingItemRequestUtility.TryCreateRequest(
            _entityManager,
            _selectedBuilding,
            itemType,
            quantity,
            DroneTaskTypeEnum.InsertBuildingItem);
    }

    private void RequestDroneItemRemoval()
    {
        if (!TryGetDroneTransferInput(
                out ItemTypeEnum itemType,
                out int requestedQuantity))
            return;

        int availableQuantity = CountAvailableStoredItems(
            _selectedBuilding,
            itemType);
        int quantity = System.Math.Min(
            requestedQuantity,
            availableQuantity);

        if (quantity <= 0)
            return;

        DroneBuildingItemRequestUtility.TryCreateRequest(
            _entityManager,
            _selectedBuilding,
            itemType,
            quantity,
            DroneTaskTypeEnum.RemoveBuildingItem);
    }

    private bool TryGetDroneTransferInput(
        out ItemTypeEnum itemType,
        out int quantity)
    {
        itemType = ItemTypeEnum.None;
        quantity = 0;

        if (!_entityManager.Exists(_selectedBuilding))
            return false;

        if (!_entityManager.HasBuffer<StoredItemElement>(_selectedBuilding))
            return false;

        int selectedIndex = _droneItemField.index;

        if (selectedIndex < 0 || selectedIndex >= _transferItemTypes.Count)
            return false;

        quantity = _droneQuantityField.value;

        if (quantity <= 0)
            return false;

        itemType = _transferItemTypes[selectedIndex];
        return true;
    }

    private int CountAvailableStoredItems(
        Entity owner,
        ItemTypeEnum itemType)
    {
        DynamicBuffer<StoredItemElement> storedItems = _entityManager
            .GetBuffer<StoredItemElement>(owner, true);
        int count = 0;

        for (int i = 0; i < storedItems.Length; i++)
        {
            StoredItemElement item = storedItems[i];

            if (item.type != itemType)
                continue;

            if (!_entityManager.Exists(item.itemEntity))
                continue;

            if (_entityManager.HasComponent<DroneItemReservation>(
                    item.itemEntity))
                continue;

            count++;
        }

        return count;
    }
}
