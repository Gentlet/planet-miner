using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

public class BuildingUIPoolingTests
{
    [Test]
    public void SetItemRow_WhenCalledRepeatedly_ReusesRowElementsWithoutAllocatingNew()
    {
        VisualElement container = new();

        // 1차 갱신: 행 2개 추가
        BuildingUI.SetItemRow(container, 0, ItemTypeEnum.Iron_Ore, 5, 10, "투입 대기", false);
        BuildingUI.SetItemRow(container, 1, ItemTypeEnum.Copper_Ore, 3, 10, "투입 대기", false);

        Assert.AreEqual(2, container.childCount);
        VisualElement firstRow = container[0];
        VisualElement secondRow = container[1];

        Label firstNameLabel = (Label)firstRow[0];
        Label firstCountLabel = (Label)firstRow[1];
        Assert.AreEqual("철 광석", firstNameLabel.text);
        Assert.AreEqual("5 / 10", firstCountLabel.text);

        // 2차 갱신: 값 변경 시 새 객체 생성 없이 동일한 VisualElement 인스턴스 재사용
        BuildingUI.SetItemRow(container, 0, ItemTypeEnum.Iron_Ore, 8, 10, "투입 중", true);

        Assert.AreEqual(2, container.childCount);
        Assert.AreSame(firstRow, container[0]);
        Assert.AreEqual("8 / 10", firstCountLabel.text);
        Assert.IsTrue(firstRow.ClassListContains("item-row-exception"));
    }

    [Test]
    public void TrimItemRowsAndSetEmptyState_HidesExcessRowsAndTogglesEmptyLabel()
    {
        VisualElement container = new();

        BuildingUI.SetItemRow(container, 0, ItemTypeEnum.Iron_Ore, 5, 10, "보관 중", false);
        BuildingUI.SetItemRow(container, 1, ItemTypeEnum.Copper_Ore, 3, 10, "보관 중", false);
        BuildingUI.TrimItemRowsAndSetEmptyState(container, 2);

        // 2개 행이 모두 Flex 상태이고 empty-label은 None
        VisualElement row0 = container[0];
        VisualElement row1 = container[1];
        Label emptyLabel = BuildingUI.GetOrCreateEmptyLabel(container);

        Assert.AreEqual(DisplayStyle.Flex, row0.style.display.value);
        Assert.AreEqual(DisplayStyle.Flex, row1.style.display.value);
        Assert.AreEqual(DisplayStyle.None, emptyLabel.style.display.value);

        // 1개로 축소
        BuildingUI.TrimItemRowsAndSetEmptyState(container, 1);
        Assert.AreEqual(DisplayStyle.Flex, row0.style.display.value);
        Assert.AreEqual(DisplayStyle.None, row1.style.display.value);
        Assert.AreEqual(DisplayStyle.None, emptyLabel.style.display.value);

        // 0개로 축소 (아이템 없음)
        BuildingUI.TrimItemRowsAndSetEmptyState(container, 0);
        Assert.AreEqual(DisplayStyle.None, row0.style.display.value);
        Assert.AreEqual(DisplayStyle.None, row1.style.display.value);
        Assert.AreEqual(DisplayStyle.Flex, emptyLabel.style.display.value);
    }

    [Test]
    public void SetStorageSlot_And_SetEmptyStorageSlot_TogglesClassesAndTooltips()
    {
        VisualElement container = new();

        // 0번 슬롯 채움, 1번 슬롯 빈칸
        BuildingUI.SetStorageSlot(container, 0, ItemTypeEnum.Iron, 50, 100);
        BuildingUI.SetEmptyStorageSlot(container, 1);
        BuildingUI.TrimStorageSlots(container, 2);

        Assert.AreEqual(2, container.childCount);

        VisualElement slot0 = container[0];
        VisualElement slot1 = container[1];

        Assert.IsTrue(slot0.ClassListContains("storage-slot-filled"));
        Assert.IsFalse(slot0.ClassListContains("storage-slot-empty"));
        Assert.AreEqual("철 50 / 100", slot0.tooltip);

        Assert.IsTrue(slot1.ClassListContains("storage-slot-empty"));
        Assert.IsFalse(slot1.ClassListContains("storage-slot-filled"));
        Assert.AreEqual(string.Empty, slot1.tooltip);

        // 슬롯 재사용: 0번 슬롯을 빈칸으로 전환
        BuildingUI.SetEmptyStorageSlot(container, 0);
        Assert.AreSame(slot0, container[0]);
        Assert.IsTrue(slot0.ClassListContains("storage-slot-empty"));
        Assert.IsFalse(slot0.ClassListContains("storage-slot-filled"));
    }

    [Test]
    public void Container_WhenSwitchingBetweenStorageAndCrafter_HidesIncompatibleElements()
    {
        VisualElement container = new();

        // 1. 창고 모드: storage-slot 2개 생성
        BuildingUI.SetStorageSlot(container, 0, ItemTypeEnum.Iron, 50, 100);
        BuildingUI.SetEmptyStorageSlot(container, 1);
        BuildingUI.TrimStorageSlots(container, 2);

        VisualElement slot0 = container[0];
        VisualElement slot1 = container[1];
        Assert.AreEqual(DisplayStyle.Flex, slot0.style.display.value);
        Assert.AreEqual(DisplayStyle.Flex, slot1.style.display.value);

        // 2. 제작기 모드로 전환: item-row 1개 추가 후 TrimItemRowsAndSetEmptyState 호출
        BuildingUI.SetItemRow(container, 0, ItemTypeEnum.Iron_Ore, 5, 10, "필요 1개", false);
        BuildingUI.TrimItemRowsAndSetEmptyState(container, 1);

        // storage-slot들은 모두 None으로 숨겨져야 하고, item-row만 Flex여야 함
        Assert.AreEqual(DisplayStyle.None, slot0.style.display.value);
        Assert.AreEqual(DisplayStyle.None, slot1.style.display.value);

        VisualElement itemRow0 = BuildingUI.GetOrCreateItemRow(container, 0);
        Assert.AreEqual(DisplayStyle.Flex, itemRow0.style.display.value);

        // 3. 다시 창고 모드로 전환: TrimStorageSlots 호출
        BuildingUI.TrimStorageSlots(container, 2);
        Assert.AreEqual(DisplayStyle.Flex, slot0.style.display.value);
        Assert.AreEqual(DisplayStyle.Flex, slot1.style.display.value);
        Assert.AreEqual(DisplayStyle.None, itemRow0.style.display.value);
    }
}
