using System.Collections.Generic;
using Unity.Entities;
using UnityEngine.UIElements;

public partial class BuildingUI
{
    private readonly List<Entity> _beltItems = new();
    private readonly Dictionary<ItemTypeEnum, int> _beltItemCounts = new();

    private void RefreshBelt()
    {
        if (!_entityManager.HasComponent<GridPosition>(_selectedBuilding))
        {
            Close();
            return;
        }

        SetBeltLayout();

        Belt belt = _entityManager.GetComponentData<Belt>(_selectedBuilding);
        GridPosition gridPosition =
            _entityManager.GetComponentData<GridPosition>(_selectedBuilding);

        int totalItemCount = CountBeltItems(gridPosition.gridPosition);
        UpdateBeltItemRows();

        SetStatus(
            totalItemCount > 0
                ? $"벨트 위 아이템 {totalItemCount}개"
                : "벨트 위 아이템 없음",
            totalItemCount > 0 ? "status-normal" : "status-waiting");
        _beltMaximumSpeedLabel.text =
            $"최대 이동 속도: {belt.speed:0.##} 칸/초";
    }

    private int CountBeltItems(Unity.Mathematics.int2 beltCell)
    {
        _chunkMap.GetItems(beltCell, _beltItems);
        _beltItemCounts.Clear();
        int totalItemCount = 0;

        for (int index = 0; index < _beltItems.Count; index++)
        {
            Entity itemEntity = _beltItems[index];

            if (!_entityManager.Exists(itemEntity))
                continue;

            if (!_entityManager.HasComponent<Item>(itemEntity))
                continue;

            Item item = _entityManager.GetComponentData<Item>(itemEntity);

            if (!item.type.IsValid())
                continue;

            _beltItemCounts.TryGetValue(item.type, out int count);
            _beltItemCounts[item.type] = count + 1;
            totalItemCount++;
        }

        return totalItemCount;
    }

    private void UpdateBeltItemRows()
    {
        _beltItemContainer.Clear();

        for (ItemTypeEnum itemType = ItemTypeEnum.Iron_Ore;
             itemType < ItemTypeEnum.Count;
             itemType++)
        {
            if (!_beltItemCounts.TryGetValue(itemType, out int count))
                continue;

            AddBeltItemRow(itemType, count);
        }

        if (_beltItemContainer.childCount == 0)
            AddEmptyLabel(_beltItemContainer);
    }

    private void AddBeltItemRow(ItemTypeEnum itemType, int count)
    {
        VisualElement row = new();
        row.AddToClassList("belt-item-row");

        Label itemNameLabel = new(GetItemDisplayName(itemType));
        itemNameLabel.AddToClassList("belt-item-name");

        Label itemCountLabel = new($"×{count}");
        itemCountLabel.AddToClassList("belt-item-count");

        row.Add(itemNameLabel);
        row.Add(itemCountLabel);
        _beltItemContainer.Add(row);
    }
}
