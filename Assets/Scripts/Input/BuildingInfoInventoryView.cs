using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

internal sealed class BuildingInfoInventoryView
{
    private readonly BuildingInfoItemCatalog _catalog;
    private readonly Action<int> _activateStored;
    private readonly List<BuildingInfoItemTile> _tiles = new();
    private readonly VisualElement _grid;
    private readonly Label _heading;
    private readonly Button _activateButton;
    private readonly string _title;
    private IReadOnlyList<BuildingInfoSlot> _slots = Array.Empty<BuildingInfoSlot>();
    private int _selectedSlot = -1;

    public VisualElement Root { get; } = new();

    public BuildingInfoInventoryView(BuildingInfoItemCatalog catalog, string title, string gridName,
        Action<int> activateStored = null)
    {
        _catalog = catalog;
        _activateStored = activateStored;
        _title = title;
        Root.style.marginTop = 6;
        _heading = new Label(title);
        Root.Add(_heading);
        _grid = BuildingInfoItemTile.NewGrid(gridName);
        Root.Add(_grid);
        if (activateStored != null)
        {
            _activateButton = new Button(Activate) { text = "1기 출고", name = gridName + "-activate" };
            Root.Add(_activateButton);
            UpdateActivateButton();
        }
    }

    public void Render(IReadOnlyList<BuildingInfoSlot> slots)
    {
        _slots = slots;
        Root.style.display = slots.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        if (slots.Count == 0)
        {
            ResetSelection();
            return;
        }

        while (_tiles.Count < slots.Count)
        {
            int index = _tiles.Count;
            var tile = new BuildingInfoItemTile(_catalog, () => Select(index)) { name = _grid.name + "-" + index };
            _grid.Add(tile);
            _tiles.Add(tile);
        }

        int occupied = 0;
        for (int index = 0; index < _tiles.Count; index++)
        {
            var tile = _tiles[index];
            bool visible = index < slots.Count;
            tile.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible)
            {
                continue;
            }

            var data = slots[index];
            bool hasReservation = data.ReservedQuantity > 0;
            string description = hasReservation ? $"이관 예약 {data.ReservedQuantity}기" : null;
            string value = data.Quantity > 0 ? data.Quantity.ToString() : hasReservation ? $"예약 {data.ReservedQuantity}" : null;
            tile.Show(data.Type, value, description, hasReservation);
            tile.focusable = data.CanActivateStored;
            tile.SetSelected(index == _selectedSlot && data.CanActivateStored);
            if (data.Quantity > 0)
            {
                occupied++;
            }
        }
        _heading.text = $"{_title}  {occupied} / {slots.Count}칸";
        UpdateActivateButton();
    }

    public void ResetSelection()
    {
        _selectedSlot = -1;
        UpdateActivateButton();
    }

    private void Select(int index)
    {
        if (index >= _slots.Count)
        {
            return;
        }

        var slot = _slots[index];
        if (slot.CanActivateStored)
        {
            if (_selectedSlot >= 0)
            {
                _tiles[_selectedSlot].SetSelected(false);
            }
            _selectedSlot = index;
            _tiles[index].SetSelected(true);
            UpdateActivateButton();
        }
    }

    private void Activate()
    {
        if (HasStoredSelection())
        {
            _activateStored(_slots[_selectedSlot].SourceIndex);
        }
    }

    private bool HasStoredSelection() => _selectedSlot >= 0 && _selectedSlot < _slots.Count &&
        _slots[_selectedSlot].CanActivateStored;

    private void UpdateActivateButton()
    {
        if (_activateButton != null)
        {
            _activateButton.style.display = HasStoredSelection() ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
