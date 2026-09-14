using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public partial class DefaultUI
{
    private const float ResearchPanelRefreshInterval = 0.1f;
    private const float ResearchNodeWidth = 220f;
    private const float ResearchNodeHeight = 140f;
    private const float ResearchNodeHorizontalGap = 46f;
    private const float ResearchNodeVerticalGap = 60f;

    private readonly Dictionary<FixedString64Bytes, Button> _researchNodeButtons = new();
    private readonly Dictionary<FixedString64Bytes, Button> _researchListButtons = new();
    private readonly List<ResearchPrerequisiteElement> _researchEdges = new();
    private Dictionary<FixedString64Bytes, Vector2> _researchPositions = new();
    private FixedString64Bytes _inspectedResearchId;
    private FixedString64Bytes _researchTreeRootId;
    private FixedString64Bytes _displayedResearchId;
    private ScrollView _researchAvailableList;
    private ScrollView _researchTreeScroll;
    private Label _researchListEmpty;
    private Label _researchInspectedLabel;
    private Label _researchInspectedDescription;
    private Button _researchStartButton;
    private EntityManager _researchEntityManager;
    private EntityQuery _researchBuildingQuery;
    private EntityQuery _researchConfigQuery;
    private bool _hasResearchQueries;
    private VisualElement _researchPanelRoot;
    private VisualElement _researchTreeCanvas;
    private Label _researchActiveLabel;
    private Label _researchBuildingCountLabel;
    private Button _researchCloseButton;
    private float _nextResearchPanelRefreshTime;
    private bool _researchPanelBound;

    private void BindResearchPanel(VisualElement root)
    {
        _researchPanelRoot = root.Q<VisualElement>("research-panel-root");
        _researchTreeCanvas = root.Q<VisualElement>("research-tree-canvas");
        _researchActiveLabel = root.Q<Label>("research-active-label");
        _researchBuildingCountLabel = root.Q<Label>("research-building-count-label");
        _researchCloseButton = root.Q<Button>("research-close-button");
        _researchAvailableList = root.Q<ScrollView>("research-available-list");
        _researchTreeScroll = root.Q<ScrollView>("research-tree-scroll");
        _researchListEmpty = root.Q<Label>("research-list-empty");
        _researchInspectedLabel = root.Q<Label>("research-inspected-label");
        _researchInspectedDescription = root.Q<Label>("research-inspected-description");
        _researchStartButton = root.Q<Button>("research-start-button");

        if (_researchAvailableList == null)
            return;
        if (_researchTreeScroll == null)
            return;
        if (_researchListEmpty == null)
            return;
        if (_researchInspectedLabel == null)
            return;
        if (_researchInspectedDescription == null)
            return;
        if (_researchStartButton == null)
            return;

        if (_researchPanelRoot == null)
            return;

        if (_researchTreeCanvas == null)
            return;

        if (_researchActiveLabel == null)
            return;

        if (_researchBuildingCountLabel == null)
            return;

        if (_researchCloseButton == null)
            return;

        World world = World.DefaultGameObjectInjectionWorld;

        if (world == null)
            return;

        if (!world.IsCreated)
            return;

        _researchEntityManager = world.EntityManager;
        _researchBuildingQuery = _researchEntityManager.CreateEntityQuery(
            ComponentType.ReadOnly<ResearchBuilding>());
        _researchConfigQuery = _researchEntityManager.CreateEntityQuery(
            ComponentType.ReadOnly<ResearchConfig>());
        _hasResearchQueries = true;
        _researchCloseButton.clicked += CloseResearchPanel;
        _researchStartButton.clicked += StartInspectedResearch;
        _researchTreeCanvas.generateVisualContent += DrawResearchConnections;
        _researchPanelBound = true;
        CloseResearchPanel();
    }

    private void UnbindResearchPanel()
    {
        if (_researchCloseButton != null)
            _researchCloseButton.clicked -= CloseResearchPanel;

        if (_researchStartButton != null)
            _researchStartButton.clicked -= StartInspectedResearch;
        if (_researchTreeCanvas != null)
        {
            _researchTreeCanvas.generateVisualContent -= DrawResearchConnections;
            _researchTreeCanvas.Clear();
        }
        _researchAvailableList?.Clear();
        _researchListButtons.Clear();
        _researchEdges.Clear();
        _researchPositions.Clear();
        _inspectedResearchId = default;
        _researchTreeRootId = default;
        _displayedResearchId = default;
        _researchAvailableList = null;
        _researchTreeScroll = null;
        _researchListEmpty = null;
        _researchInspectedLabel = null;
        _researchInspectedDescription = null;
        _researchStartButton = null;

        _hasResearchQueries = false;
        _researchNodeButtons.Clear();
        _researchPanelRoot = null;
        _researchTreeCanvas = null;
        _researchActiveLabel = null;
        _researchBuildingCountLabel = null;
        _researchCloseButton = null;
        _researchPanelBound = false;
    }

    private void OpenResearchPanel()
    {
        if (!_researchPanelBound)
            return;

        GetComponent<BuildingUI>()?.Close();
        _researchPanelRoot.style.display = DisplayStyle.Flex;
        _nextResearchPanelRefreshTime = 0f;
        RefreshResearchPanel();
    }

    private void CloseResearchPanel()
    {
        if (_researchPanelRoot != null)
            _researchPanelRoot.style.display = DisplayStyle.None;
    }

    private void UpdateResearchPanel()
    {
        if (!_researchPanelBound)
            return;

        if (_researchPanelRoot.resolvedStyle.display == DisplayStyle.None)
            return;

        Keyboard keyboard = Keyboard.current;

        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            CloseResearchPanel();
            return;
        }

        if (Time.unscaledTime < _nextResearchPanelRefreshTime)
            return;

        _nextResearchPanelRefreshTime =
            Time.unscaledTime + ResearchPanelRefreshInterval;
        RefreshResearchPanel();
    }

    private void RefreshResearchPanel()
    {
        if (!TryGetResearchConfigEntity(out Entity configEntity))
        {
            _researchActiveLabel.text = "연구 설정을 불러오는 중입니다";
            _researchBuildingCountLabel.text = "작동 가능한 연구건물: 0개";
            return;
        }

        DynamicBuffer<ResearchDefinitionElement> definitions =
            _researchEntityManager.GetBuffer<ResearchDefinitionElement>(
                configEntity,
                true);
        DynamicBuffer<ResearchPrerequisiteElement> prerequisites =
            _researchEntityManager.GetBuffer<ResearchPrerequisiteElement>(
                configEntity,
                true);
        DynamicBuffer<ResearchProgressElement> progress =
            _researchEntityManager.GetBuffer<ResearchProgressElement>(
                configEntity,
                true);
        ResearchState researchState = _researchEntityManager
            .GetComponentData<ResearchState>(configEntity);
        var ingredients = _researchEntityManager.GetBuffer<ResearchIngredientElement>(configEntity, true);

        if (_inspectedResearchId.Length == 0)
        {
            _inspectedResearchId = researchState.activeResearchId;
            for (int i = 0; i < definitions.Length && _inspectedResearchId.Length == 0; i++)
            {
                var id = definitions[i].stableId;
                int index = progress.FindProgressIndex(id);
                if (index >= 0 && !progress[index].completed &&
                    prerequisites.ArePrerequisitesCompleted(progress, id))
                    _inspectedResearchId = id;
            }
        }
        if (_researchTreeRootId.Length == 0)
            _researchTreeRootId = _inspectedResearchId;
        EnsureResearchNodeButtons(definitions, prerequisites);
        UpdateResearchNodeButtons(
            definitions,
            prerequisites,
            progress,
            ingredients,
            researchState);
        UpdateResearchPanelSummary(definitions, progress, ingredients, researchState);
    }

    private void EnsureResearchNodeButtons(
        DynamicBuffer<ResearchDefinitionElement> definitions,
        DynamicBuffer<ResearchPrerequisiteElement> prerequisites)
    {
        if (_researchListButtons.Count != definitions.Length)
        {
            _researchAvailableList.Clear();
            _researchListButtons.Clear();
            for (int i = 0; i < definitions.Length; i++)
            {
                FixedString64Bytes id = definitions[i].stableId;
                Button button = new(() => SelectResearchFromList(id));
                button.AddToClassList("research-node");
                button.AddToClassList("research-list-entry");
                _researchAvailableList.Add(button);
                _researchListButtons.Add(id, button);
            }
            _displayedResearchId = default;
        }

        if (_displayedResearchId.Equals(_researchTreeRootId))
            return;

        _displayedResearchId = _researchTreeRootId;
        _researchTreeCanvas.Clear();
        _researchNodeButtons.Clear();
        _researchEdges.Clear();
        var ids = new List<FixedString64Bytes>(definitions.Length);
        for (int i = 0; i < definitions.Length; i++)
            ids.Add(definitions[i].stableId);
        for (int i = 0; i < prerequisites.Length; i++)
            _researchEdges.Add(prerequisites[i]);
        _researchPositions = ResearchTreeLayout.Build(ids, _researchEdges, _researchTreeRootId);
        float width = ResearchNodeWidth;
        float height = ResearchNodeHeight;
        foreach (var pair in _researchPositions)
        {
            FixedString64Bytes id = pair.Key;
            Vector2 position = GetResearchNodePosition(pair.Value);
            Button button = new(() => InspectResearch(id));
            button.AddToClassList("research-node");
            button.style.position = Position.Absolute;
            button.style.left = position.x;
            button.style.top = position.y;
            button.style.width = ResearchNodeWidth;
            button.style.height = ResearchNodeHeight;
            _researchTreeCanvas.Add(button);
            _researchNodeButtons.Add(id, button);
            width = Mathf.Max(width, position.x + ResearchNodeWidth);
            height = Mathf.Max(height, position.y + ResearchNodeHeight);
        }
        _researchTreeCanvas.style.width = width;
        _researchTreeCanvas.style.height = height;
        _researchTreeCanvas.MarkDirtyRepaint();
        _researchTreeScroll.scrollOffset = Vector2.zero;
    }

    private static Vector2 GetResearchNodePosition(Vector2 slot)
    {
        return new Vector2(
            slot.x * (ResearchNodeWidth + ResearchNodeHorizontalGap),
            slot.y * (ResearchNodeHeight + ResearchNodeVerticalGap));
    }

    private void DrawResearchConnections(MeshGenerationContext context)
    {
        Painter2D painter = context.painter2D;
        painter.lineWidth = 2f;
        painter.strokeColor = new Color(0.45f, 0.62f, 0.76f);
        foreach (var edge in _researchEdges)
        {
            if (!_researchPositions.TryGetValue(edge.prerequisiteId, out Vector2 parent))
                continue;
            if (!_researchPositions.TryGetValue(edge.researchId, out Vector2 child))
                continue;
            Vector2 start = GetResearchNodePosition(parent) +
                            new Vector2(ResearchNodeWidth * 0.5f, ResearchNodeHeight);
            Vector2 end = GetResearchNodePosition(child) +
                          new Vector2(ResearchNodeWidth * 0.5f, 0f);
            painter.BeginPath();
            painter.MoveTo(start);
            painter.BezierCurveTo(start + Vector2.up * 30f, end - Vector2.up * 30f, end);
            painter.Stroke();
            painter.BeginPath();
            painter.MoveTo(end + new Vector2(-5f, -8f));
            painter.LineTo(end);
            painter.LineTo(end + new Vector2(5f, -8f));
            painter.Stroke();
        }
    }

    private void InspectResearch(FixedString64Bytes researchId)
    {
        _inspectedResearchId = researchId;
        _nextResearchPanelRefreshTime = 0f;
    }

    private void SelectResearchFromList(FixedString64Bytes researchId)
    {
        _researchTreeRootId = researchId;
        InspectResearch(researchId);
    }

    private void StartInspectedResearch()
    {
        if (!TryGetResearchConfigEntity(out Entity configEntity))
            return;
        var progress = _researchEntityManager.GetBuffer<ResearchProgressElement>(configEntity, true);
        var prerequisites = _researchEntityManager.GetBuffer<ResearchPrerequisiteElement>(configEntity, true);
        var state = _researchEntityManager.GetComponentData<ResearchState>(configEntity);
        int index = progress.FindProgressIndex(_inspectedResearchId);
        if (index < 0)
            return;
        if (progress[index].completed || state.activeResearchId.Equals(_inspectedResearchId))
            return;
        if (!prerequisites.ArePrerequisitesCompleted(progress, _inspectedResearchId))
            return;

        RequestResearchSelection(_inspectedResearchId);
        _researchStartButton.SetEnabled(false);
    }

    private void UpdateResearchNodeButtons(
        DynamicBuffer<ResearchDefinitionElement> definitions,
        DynamicBuffer<ResearchPrerequisiteElement> prerequisites,
        DynamicBuffer<ResearchProgressElement> progress,
        DynamicBuffer<ResearchIngredientElement> ingredients,
        ResearchState researchState)
    {
        int availableCount = 0;
        _researchStartButton.SetEnabled(false);
        for (int i = 0; i < definitions.Length; i++)
        {
            ResearchDefinitionElement definition = definitions[i];

            int progressIndex = progress.FindProgressIndex(definition.stableId);
            ResearchProgressElement progressElement = progressIndex >= 0
                ? progress[progressIndex]
                : default;
            bool completed = progressElement.completed;
            bool active = researchState.activeResearchId.Equals(
                definition.stableId);
            bool available = !completed && prerequisites
                .ArePrerequisitesCompleted(progress, definition.stableId);
            string stateText = completed
                ? "완료"
                : active
                    ? "연구 중"
                    : available
                        ? "선택 가능"
                        : GetPrerequisiteText(
                            definition.stableId,
                            definitions,
                            prerequisites,
                            progress);
            float progressPercent = definition.requiredProgress > 0f
                ? math.saturate(
                    progressElement.progress / definition.requiredProgress) * 100f
                : 0f;

            string text = $"{definition.displayName}\n{stateText}\n진척도 {progressPercent:0}%";
            bool selected = definition.stableId.Equals(_inspectedResearchId);
            if (_researchNodeButtons.TryGetValue(definition.stableId, out Button button))
                UpdateResearchButton(button, text, definition.description.ToString(), active, completed, available, selected);
            if (_researchListButtons.TryGetValue(definition.stableId, out Button listButton))
            {
                listButton.style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
                if (available)
                {
                    availableCount++;
                    UpdateResearchButton(listButton, text, definition.description.ToString(), active, completed, available, selected);
                }
            }
            if (selected)
            {
                _researchInspectedLabel.text = definition.displayName.ToString();
                _researchInspectedDescription.text =
                    $"{definition.description}\n{stateText}\t소모 재료: {GetResearchMaterialText(definition, ingredients)}";
                _researchInspectedDescription.tooltip =
                    "총량 = 올림(필요 진척도 / 주기당 진척도) × 주기당 재료 수량. 연구 변경이나 병렬 진행으로 낭비된 재료는 포함하지 않습니다.";
                _researchStartButton.text = active ? "연구 중" : completed ? "연구 완료" : "연구 시작";
                _researchStartButton.SetEnabled(available && !active);
                _researchStartButton.tooltip = researchState.activeResearchId.Length > 0 && !active
                    ? "연구를 변경하면 진행 중인 로컬 주기가 초기화됩니다. 이미 소비한 재료는 반환되지 않습니다."
                    : "선택한 연구를 시작합니다.";
            }
        }
        _researchListEmpty.style.display = availableCount == 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private static void UpdateResearchButton(
        Button button, string text, string tooltip,
        bool active, bool completed, bool available, bool selected)
    {
        button.text = text;
        button.tooltip = tooltip;
        button.EnableInClassList("research-node-active", active);
        button.EnableInClassList("research-node-completed", completed);
        button.EnableInClassList("research-node-locked", !available && !active && !completed);
        button.EnableInClassList("research-node-selected", selected);
    }

    private void UpdateResearchPanelSummary(
        DynamicBuffer<ResearchDefinitionElement> definitions,
        DynamicBuffer<ResearchProgressElement> progress,
        DynamicBuffer<ResearchIngredientElement> ingredients,
        ResearchState researchState)
    {
        if (researchState.activeResearchId.Length == 0)
        {
            _researchActiveLabel.text = "활성 연구: 없음";
        }
        else if (definitions.TryGetDefinition(
                     researchState.activeResearchId,
                     out ResearchDefinitionElement activeResearch))
        {
            int progressIndex = progress.FindProgressIndex(
                researchState.activeResearchId);
            float currentProgress = progressIndex >= 0
                ? progress[progressIndex].progress
                : 0f;
            _researchActiveLabel.text =
                $"활성 연구: {activeResearch.displayName}  ·  {currentProgress:0.#} / {activeResearch.requiredProgress:0.#}\n" +
                $"{activeResearch.description}\n" +
                $"소모 재료: {GetResearchMaterialText(activeResearch, ingredients)}";
        }
        else
        {
            _researchActiveLabel.text = "활성 연구 정보를 찾을 수 없습니다";
        }

        int buildingCount = _hasResearchQueries
            ? _researchBuildingQuery.CalculateEntityCount()
            : 0;
        _researchBuildingCountLabel.text =
            $"작동 가능한 연구건물: {buildingCount}개";
    }

    private static string GetResearchMaterialText(
        ResearchDefinitionElement definition,
        DynamicBuffer<ResearchIngredientElement> ingredients)
    {
        double cost = System.Math.Ceiling((double)definition.requiredProgress / definition.progressPerCycle);
        var text = new System.Text.StringBuilder();
        for (int i = 0; i < ingredients.Length; i++)
        {
            ResearchIngredientElement ingredient = ingredients[i];
            if (!ingredient.researchId.Equals(definition.stableId))
                continue;

            if (text.Length > 0)
                text.Append(" · ");
            text.Append(ingredient.itemType.GetDisplayName());
            text.Append(" ×");
            text.Append((ingredient.amount * cost).ToString("0"));
        }
        return text.Length > 0 ? text.ToString() : "없음";
    }

    private static string GetPrerequisiteText(
        FixedString64Bytes researchId,
        DynamicBuffer<ResearchDefinitionElement> definitions,
        DynamicBuffer<ResearchPrerequisiteElement> prerequisites,
        DynamicBuffer<ResearchProgressElement> progress)
    {
        string text = "추가 필요: ";
        bool hasPrerequisite = false;

        for (int i = 0; i < prerequisites.Length; i++)
        {
            ResearchPrerequisiteElement prerequisite = prerequisites[i];

            if (!prerequisite.researchId.Equals(researchId))
                continue;

            int index = progress.FindProgressIndex(prerequisite.prerequisiteId);
            if (index >= 0 && progress[index].completed)
                continue;

            if (hasPrerequisite)
                text += ", ";

            if (definitions.TryGetDefinition(
                    prerequisite.prerequisiteId,
                    out ResearchDefinitionElement definition))
                text += definition.displayName.ToString();
            else
                text += prerequisite.prerequisiteId.ToString();

            hasPrerequisite = true;
        }

        return hasPrerequisite ? text : "선택 불가";
    }

    private bool TryGetResearchConfigEntity(out Entity configEntity)
    {
        if (!_hasResearchQueries || _researchConfigQuery.IsEmptyIgnoreFilter)
        {
            configEntity = Entity.Null;
            return false;
        }

        configEntity = _researchConfigQuery.GetSingletonEntity();
        return true;
    }

    private void RequestResearchSelection(FixedString64Bytes researchId)
    {
        Entity requestEntity = _researchEntityManager.CreateEntity();
        _researchEntityManager.AddComponentData(
            requestEntity,
            new ResearchSelectionRequest { researchId = researchId });
        _nextResearchPanelRefreshTime = 0f;
    }
}
