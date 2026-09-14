using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class ResearchUITests
{
    private static ResearchPrerequisiteElement Edge(string parent, string child)
    {
        return new ResearchPrerequisiteElement { prerequisiteId = parent, researchId = child };
    }

    [Test]
    public void Layout_MergedDescendantUsesLongestPathAndAppearsOnce()
    {
        FixedString64Bytes[] ids = { "root", "short", "middle", "long", "shared", "unrelated" };
        var edges = new[] { Edge("root", "short"), Edge("root", "middle"),
            Edge("middle", "long"), Edge("short", "shared"), Edge("long", "shared") };
        var positions = ResearchTreeLayout.Build(ids, edges, "root");
        Assert.That(positions.Count, Is.EqualTo(5));
        Assert.That(positions["shared"].y, Is.EqualTo(3));
        Assert.That(positions.ContainsKey("unrelated"), Is.False);
        foreach (var edge in edges)
            Assert.That(positions[edge.researchId].y, Is.GreaterThan(positions[edge.prerequisiteId].y));
        Assert.That(new HashSet<Vector2>(positions.Values).Count, Is.EqualTo(positions.Count));
    }

    [Test]
    public void Layout_ExternalPrerequisiteDoesNotHideDescendant()
    {
        FixedString64Bytes[] ids = { "root", "external", "child" };
        var positions = ResearchTreeLayout.Build(ids,
            new[] { Edge("root", "child"), Edge("external", "child") }, "root");
        Assert.That(positions.Count, Is.EqualTo(2));
        Assert.That(positions["child"].y, Is.EqualTo(1));
    }

    [Test]
    public void Layout_DefinitionOrderKeepsSiblingsStableWhenEdgesReordered()
    {
        FixedString64Bytes[] ids = { "second", "root", "first" };
        var edges = new[] { Edge("root", "first"), Edge("root", "second") };
        var positions = ResearchTreeLayout.Build(ids, edges, "root");
        System.Array.Reverse(edges);
        var reordered = ResearchTreeLayout.Build(ids, edges, "root");
        Assert.That(positions["second"].x, Is.LessThan(positions["first"].x));
        foreach (var pair in positions)
            Assert.That(reordered[pair.Key], Is.EqualTo(pair.Value));
    }

    [Test]
    public void Layout_LeafMissingRootAndCycleAreBounded()
    {
        FixedString64Bytes[] ids = { "root", "other" };
        Assert.That(ResearchTreeLayout.Build(ids, new ResearchPrerequisiteElement[0], "root").Count, Is.EqualTo(1));
        Assert.That(ResearchTreeLayout.Build(ids, new ResearchPrerequisiteElement[0], "missing"), Is.Empty);
        Assert.That(ResearchTreeLayout.Build(ids,
            new[] { Edge("root", "other"), Edge("other", "root") }, "root"), Is.Empty);
    }

    [Test]
    public void Panel_InspectionDoesNotStartResearchAndCompletionUpdatesAvailableList()
    {
        World previousWorld = World.DefaultGameObjectInjectionWorld;
        using var world = new World("ResearchUI_Test");
        World.DefaultGameObjectInjectionWorld = world;
        var manager = world.EntityManager;
        var go = new GameObject("ResearchUI_Test");
        go.SetActive(false);
        var ui = go.AddComponent<DefaultUI>();
        var root = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
            "Assets/UI/Modes/Default/DefaultUI.uxml").Instantiate();
        try
        {
            var endSimulation = world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
            world.GetOrCreateSystemManaged<CrafterConfigLoadSystem>().Update();
            world.GetOrCreateSystemManaged<ResearchConfigLoadSystem>().Update();
            using var configQuery = manager.CreateEntityQuery(ComponentType.ReadOnly<ResearchConfig>());
            using var requests = manager.CreateEntityQuery(ComponentType.ReadOnly<ResearchSelectionRequest>());
            Entity config = configQuery.GetSingletonEntity();
            Invoke(ui, "BindResearchPanel", root);
            Invoke(ui, "RefreshResearchPanel");
            var list = root.Q<ScrollView>("research-available-list");
            Assert.That(VisibleButtons(list), Is.EqualTo(2));
            Invoke(ui, "SelectResearchFromList", new FixedString64Bytes("logistics_distribution"));
            Invoke(ui, "RefreshResearchPanel");
            Assert.That(root.Q<VisualElement>("research-tree-canvas").childCount, Is.EqualTo(2));
            Assert.That(requests.CalculateEntityCount(), Is.Zero);
            var canvas = root.Q<VisualElement>("research-tree-canvas");
            VisualElement originalNode = canvas[0];
            StringAssert.Contains("철 ×10 · 구리 ×10",
                root.Q<Label>("research-inspected-description").text);

            Invoke(ui, "InspectResearch", new FixedString64Bytes("drone_logistics"));
            Invoke(ui, "RefreshResearchPanel");
            Assert.That(canvas.childCount, Is.EqualTo(2));
            Assert.That(canvas[0], Is.SameAs(originalNode), "Node inspection must preserve the graph elements.");
            StringAssert.Contains("철 막대 ×20 · 구리 막대 ×20",
                root.Q<Label>("research-inspected-description").text);
            Assert.That(root.Q<Button>("research-start-button").enabledSelf, Is.False);
            StringAssert.Contains("소재 가공", root.Q<Label>("research-inspected-description").text);
            Invoke(ui, "StartInspectedResearch");
            Assert.That(requests.CalculateEntityCount(), Is.Zero);

            Invoke(ui, "SelectResearchFromList", new FixedString64Bytes("material_processing"));
            Invoke(ui, "RefreshResearchPanel");
            Assert.That(canvas.childCount, Is.EqualTo(3));
            Assert.That(canvas[0], Is.Not.SameAs(originalNode));
            Invoke(ui, "StartInspectedResearch");
            Assert.That(requests.CalculateEntityCount(), Is.EqualTo(1));
            world.GetOrCreateSystemManaged<ResearchSelectionSystem>().Update();
            endSimulation.Update();
            Assert.That(manager.GetComponentData<ResearchState>(config).activeResearchId.ToString(),
                Is.EqualTo("material_processing"));
            Invoke(ui, "RefreshResearchPanel");
            Assert.That(root.Q<Button>("research-start-button").enabledSelf, Is.False);
            var activeLabel = root.Q<Label>("research-active-label");
            StringAssert.Contains("활성 연구: 소재 가공", activeLabel.text);
            StringAssert.Contains("철봉과 구리봉 제작 기술을 해금합니다.", activeLabel.text);
            StringAssert.Contains("소모 재료 (1주기): 철 ×1 · 구리 ×1", activeLabel.text);
            string activeText = activeLabel.text;
            Invoke(ui, "InspectResearch", new FixedString64Bytes("drone_logistics"));
            Invoke(ui, "RefreshResearchPanel");
            Assert.That(activeLabel.text, Is.EqualTo(activeText), "Inspection must not replace active research details.");
            Invoke(ui, "InspectResearch", new FixedString64Bytes("material_processing"));

            var definitions = manager.GetBuffer<ResearchDefinitionElement>(config);
            for (int i = 0; i < definitions.Length; i++)
            {
                if (!definitions[i].stableId.Equals(new FixedString64Bytes("material_processing")))
                    continue;
                var definition = definitions[i];
                definition.requiredProgress = 101f;
                definitions[i] = definition;
            }
            Invoke(ui, "RefreshResearchPanel");
            StringAssert.Contains("철 ×11 · 구리 ×11",
                root.Q<Label>("research-inspected-description").text);

            var progress = manager.GetBuffer<ResearchProgressElement>(config);
            int index = progress.FindProgressIndex("material_processing");
            var completed = progress[index];
            completed.completed = true;
            progress[index] = completed;
            manager.SetComponentData(config, new ResearchState());
            Invoke(ui, "RefreshResearchPanel");
            Assert.That(VisibleButtons(list), Is.EqualTo(2)); // logistics + newly unlocked coal
            Assert.That(root.Q<Button>("research-start-button").text, Is.EqualTo("연구 완료"));
            Assert.That(activeLabel.text, Is.EqualTo("활성 연구: 없음"));
            Invoke(ui, "InspectResearch", new FixedString64Bytes("drone_logistics"));
            Invoke(ui, "RefreshResearchPanel");
            StringAssert.DoesNotContain("소재 가공", root.Q<Label>("research-inspected-description").text);

            Invoke(ui, "UnbindResearchPanel");
            Invoke(ui, "BindResearchPanel", root);
            Invoke(ui, "RefreshResearchPanel");
            Assert.That(VisibleButtons(list), Is.EqualTo(2));
        }
        finally
        {
            Invoke(ui, "UnbindResearchPanel");
            Object.DestroyImmediate(go);
            World.DefaultGameObjectInjectionWorld = previousWorld;
        }
    }

    private static int VisibleButtons(ScrollView list)
    {
        int count = 0;
        foreach (var child in list.contentContainer.Children())
        {
            if (child.style.display.value != DisplayStyle.None)
                count++;
        }
        return count;
    }

    private static void Invoke(DefaultUI ui, string name, params object[] arguments)
    {
        typeof(DefaultUI).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(ui, arguments);
    }
}
