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
        float researchSpeedMultiplier = GetResearchSpeedMultiplier(
            ResearchStatModifierTypeEnum.BeltSpeed);
        _beltMaximumSpeedLabel.text = researchSpeedMultiplier > 1f
            ? $"최대 이동 속도: {belt.speed * researchSpeedMultiplier:0.##} 칸/초 (연구 ×{researchSpeedMultiplier:0.##})"
            : $"최대 이동 속도: {belt.speed:0.##} 칸/초";
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
        int rowIndex = 0;

        for (ItemTypeEnum itemType = ItemTypeEnum.Iron_Ore;
             itemType < ItemTypeEnum.Count;
             itemType++)
        {
            if (!_beltItemCounts.TryGetValue(itemType, out int count))
                continue;

            SetBeltItemRow(rowIndex, itemType, count);
            rowIndex++;
        }

        TrimBeltItemRowsAndSetEmptyState(rowIndex);
    }

    private void SetBeltItemRow(int index, ItemTypeEnum itemType, int count)
    {
        VisualElement row = GetOrCreateBeltItemRow(index);
        Label itemNameLabel = (Label)row[0];
        Label itemCountLabel = (Label)row[1];

        itemNameLabel.text = GetItemDisplayName(itemType);
        itemCountLabel.text = $"×{count}";
    }

    private VisualElement GetOrCreateBeltItemRow(int index)
    {
        int currentRowCount = 0;
        VisualElement targetRow = null;

        for (int i = 0; i < _beltItemContainer.childCount; i++)
        {
            VisualElement child = _beltItemContainer[i];
            if (!child.ClassListContains("belt-item-row"))
                continue;

            if (currentRowCount == index)
            {
                targetRow = child;
                break;
            }
            currentRowCount++;
        }

        if (targetRow == null)
        {
            targetRow = new VisualElement();
            targetRow.AddToClassList("belt-item-row");

            Label itemNameLabel = new();
            itemNameLabel.AddToClassList("belt-item-name");
            targetRow.Add(itemNameLabel);

            Label itemCountLabel = new();
            itemCountLabel.AddToClassList("belt-item-count");
            targetRow.Add(itemCountLabel);

            _beltItemContainer.Add(targetRow);
        }

        targetRow.style.display = DisplayStyle.Flex;
        return targetRow;
    }

    private void TrimBeltItemRowsAndSetEmptyState(int activeCount)
    {
        int currentRowCount = 0;
        for (int i = 0; i < _beltItemContainer.childCount; i++)
        {
            VisualElement child = _beltItemContainer[i];
            if (child.ClassListContains("belt-item-row"))
            {
                if (currentRowCount >= activeCount)
                    child.style.display = DisplayStyle.None;
                else
                    child.style.display = DisplayStyle.Flex;

                currentRowCount++;
            }
            else if (!child.ClassListContains("empty-label"))
            {
                child.style.display = DisplayStyle.None;
            }
        }

        Label emptyLabel = GetOrCreateEmptyLabel(_beltItemContainer);
        emptyLabel.style.display = activeCount == 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
