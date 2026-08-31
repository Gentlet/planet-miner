using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Entities;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

public class BuildingUIBindingTests
{
    private const string DefaultUxmlPath = "Assets/UI/Modes/Default/DefaultUI.uxml";

    [Test]
    public void BindVisualTree_WithValidDefaultUI_SuccessfullyBindsAllElements()
    {
        VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DefaultUxmlPath);
        Assert.IsNotNull(uxml, $"UXML 에셋을 찾을 수 없습니다: {DefaultUxmlPath}");

        VisualElement root = uxml.Instantiate();
        GameObject go = new("BuildingUI_Test");
        BuildingUI ui = go.AddComponent<BuildingUI>();

        try
        {
            bool success = ui.BindVisualTree(root);
            Assert.IsTrue(success, "모든 필수 UXML 요소가 포함된 기본 UI 바인딩에 성공해야 합니다.");
            Assert.IsTrue(ui.IsBound, "IsBound 상태가 true여야 합니다.");
        }
        finally
        {
            ui.UnbindVisualTree();
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void BindVisualTree_WithEmptyRoot_LogsErrorAndFails()
    {
        VisualElement emptyRoot = new();
        GameObject go = new("BuildingUI_Test");
        BuildingUI ui = go.AddComponent<BuildingUI>();

        LogAssert.Expect(
            LogType.Error,
            new Regex(@"\[BuildingUI\] 필수 UXML 요소 바인딩 실패 \(\d+개 누락\):.*"));

        try
        {
            bool success = ui.BindVisualTree(emptyRoot);
            Assert.IsFalse(success, "필수 요소가 누락된 경우 바인딩이 실패해야 합니다.");
            Assert.IsFalse(ui.IsBound, "IsBound 상태가 false여야 합니다.");
        }
        finally
        {
            ui.UnbindVisualTree();
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void BindVisualTree_RepeatedBindingAndUnbinding_TransitionsCleanly()
    {
        VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(DefaultUxmlPath);
        VisualElement root = uxml.Instantiate();
        GameObject go = new("BuildingUI_Test");
        BuildingUI ui = go.AddComponent<BuildingUI>();

        try
        {
            Assert.IsTrue(ui.BindVisualTree(root));
            Assert.IsTrue(ui.IsBound);

            ui.UnbindVisualTree();
            Assert.IsFalse(ui.IsBound);

            Assert.IsTrue(ui.BindVisualTree(root));
            Assert.IsTrue(ui.IsBound);
        }
        finally
        {
            ui.UnbindVisualTree();
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void BindVisualTree_NullRootAndNoUIDocument_LogsErrorAndFails()
    {
        GameObject go = new("BuildingUI_Test");
        BuildingUI ui = go.AddComponent<BuildingUI>();

        LogAssert.Expect(
            LogType.Error,
            new Regex(@"\[BuildingUI\] 바인딩 실패: UIDocument 또는 rootVisualElement가 유효하지 않습니다\..*"));

        try
        {
            bool success = ui.BindVisualTree(null);
            Assert.IsFalse(success, "UIDocument/루트가 없는 경우 실패해야 합니다.");
            Assert.IsFalse(ui.IsBound, "IsBound 상태가 false여야 합니다.");
        }
        finally
        {
            ui.UnbindVisualTree();
            Object.DestroyImmediate(go);
        }
    }
}
