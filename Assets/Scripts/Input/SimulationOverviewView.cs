using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>전역 연구/작업 조회 화면이다. 트리 배치는 루트 변경에만 갱신하고 노드 조회는 스크롤을 유지한다.</summary>
[RequireComponent(typeof(WorldInputController), typeof(ResearchPanelController), typeof(TaskOverviewController))]
public sealed class SimulationOverviewView : MonoBehaviour
{
    private sealed class ResearchConnections : VisualElement
    {
        public readonly List<Vector2[]> Lines = new List<Vector2[]>();
        public ResearchConnections()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }
        private void Draw(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            painter.lineWidth = 2f;
            painter.strokeColor = new Color(0.4f, 0.65f, 0.8f);
            foreach (var points in Lines)
            {
                painter.BeginPath();
                painter.MoveTo(points[0]);
                painter.LineTo(points[1]);
                painter.Stroke();
            }
        }
    }
    private sealed class TaskRow
    {
        public readonly VisualElement Root = new VisualElement();
        public readonly Label Text = new Label();
        public readonly Button Select;
        public Action Click;
        public TaskRow()
        {
            Root.style.flexDirection = FlexDirection.Row;
            Text.style.flexGrow = 1;
            Text.style.whiteSpace = WhiteSpace.Normal;
            Root.Add(Text);
            Select = new Button(() => Click?.Invoke()) { text = "대상 조회" };
            Root.Add(Select);
        }
    }
    private ResearchPanelController _research;
    private TaskOverviewController _tasks;
    private GameObject _rootObject;
    private PanelSettings _settings;
    private VisualElement _panel;
    private VisualElement _researchPage;
    private VisualElement _taskPage;
    private VisualElement _roots;
    private ScrollView _graphScroll;
    private ResearchConnections _graph;
    private Label _active;
    private Label _detail;
    private Label _feedback;
    private Button _start;
    private VisualElement _taskRows;
    private readonly Dictionary<string, Button> _nodeButtons = new Dictionary<string, Button>();
    private readonly List<TaskRow> _rows = new List<TaskRow>();
    private int _layoutVersion = -1;
    private int _page;
    public int VisiblePage => _page;

    private void OnEnable()
    {
        _research = GetComponent<ResearchPanelController>();
        _tasks = GetComponent<TaskOverviewController>();
        _settings = ScriptableObject.CreateInstance<PanelSettings>();
        _settings.hideFlags = HideFlags.HideAndDontSave;
        _settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        _settings.referenceResolution = new Vector2Int(1920, 1080);
        _rootObject = new GameObject("Research and task overview") { hideFlags = HideFlags.DontSave };
        _rootObject.SetActive(false);
        _rootObject.transform.SetParent(transform, false);
        var document = _rootObject.AddComponent<UIDocument>();
        document.panelSettings = _settings;
        document.sortingOrder = 15;
        _rootObject.AddComponent<WorldInputUiBlocker>().Initialize(GetComponent<WorldInputController>());
        _rootObject.SetActive(true);
        var root = document.rootVisualElement;
        root.pickingMode = PickingMode.Ignore;
        root.style.position = Position.Absolute;
        root.style.left = 0;
        root.style.top = 0;
        root.style.right = 0;
        root.style.bottom = 0;
        root.style.unityFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var buttons = new VisualElement();
        buttons.AddToClassList(WorldInputUiBlocker.BlockingUiClass);
        buttons.style.position = Position.Absolute;
        buttons.style.left = 12;
        buttons.style.top = 12;
        buttons.style.width = 220;
        buttons.style.flexDirection = FlexDirection.Row;
        buttons.Add(MakeButton("연구", () => ShowPage(1)));
        buttons.Add(MakeButton("전체 작업", () => ShowPage(2)));
        root.Add(buttons);
        _panel = new VisualElement { name = "simulation-overview" };
        _panel.AddToClassList(WorldInputUiBlocker.BlockingUiClass);
        _panel.style.position = Position.Absolute;
        _panel.style.left = 12;
        _panel.style.top = 60;
        _panel.style.bottom = 112;
        _panel.style.width = 1050;
        _panel.style.backgroundColor = new Color(0.025f, 0.04f, 0.065f, 0.98f);
        _panel.style.color = Color.white;
        _panel.style.paddingLeft = 12;
        _panel.style.paddingRight = 12;
        _panel.style.paddingTop = 10;
        _panel.Add(MakeButton("닫기", () => ShowPage(0)));
        root.Add(_panel);
        BuildResearch();
        BuildTasks();
        _research.Changed += RenderResearch;
        _tasks.Changed += RenderTasks;
        _layoutVersion = -1;
        ShowPage(0);
        RenderResearch();
        RenderTasks();
    }

    private static Button MakeButton(string text, Action click)
    {
        var button = new Button(click) { text = text };
        button.style.minHeight = 30;
        button.style.minWidth = 90;
        button.style.marginRight = 6;
        button.style.marginBottom = 6;
        button.style.color = Color.white;
        button.style.backgroundColor = new Color(0.12f, 0.2f, 0.3f);
        return button;
    }

    private void BuildResearch()
    {
        _researchPage = new VisualElement();
        _researchPage.style.flexGrow = 1;
        _panel.Add(_researchPage);
        _active = new Label();
        _active.style.whiteSpace = WhiteSpace.Normal;
        _researchPage.Add(_active);
        _roots = new VisualElement();
        _roots.style.flexDirection = FlexDirection.Row;
        _roots.style.flexWrap = Wrap.Wrap;
        _researchPage.Add(_roots);
        _graphScroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal) { name = "research-graph-scroll" };
        _graphScroll.style.height = 320;
        _researchPage.Add(_graphScroll);
        _graph = new ResearchConnections();
        _graph.style.position = Position.Relative;
        _graphScroll.Add(_graph);
        var details = new ScrollView();
        details.style.flexGrow = 1;
        _detail = new Label { name = "research-detail" };
        _detail.style.whiteSpace = WhiteSpace.Normal;
        details.Add(_detail);
        _researchPage.Add(details);
        _start = MakeButton("조회한 연구 시작", () => _research.StartViewed());
        _start.name = "start-research";
        _researchPage.Add(_start);
        _feedback = new Label();
        _researchPage.Add(_feedback);
    }

    private void BuildTasks()
    {
        _taskPage = new VisualElement();
        _taskPage.style.flexGrow = 1;
        _taskPage.Add(new Label("전체 작업 조회 / 취소는 기존 현장/건물 조작을 사용합니다."));
        var scroll = new ScrollView();
        scroll.style.flexGrow = 1;
        _taskRows = new VisualElement();
        scroll.Add(_taskRows);
        _taskPage.Add(scroll);
        _panel.Add(_taskPage);
    }

    public void ShowPage(int page)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }
        _page = page;
        _panel.style.display = page == 0 ? DisplayStyle.None : DisplayStyle.Flex;
        _researchPage.style.display = page == 1 ? DisplayStyle.Flex : DisplayStyle.None;
        _taskPage.style.display = page == 2 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void RenderResearch()
    {
        if (_panel == null)
        {
            return;
        }
        _active.text = _research.Ready ? _research.ActiveText : "연구 설정 준비 중";
        _feedback.text = _research.Feedback;
        if (_layoutVersion != _research.LayoutVersion)
        {
            RebuildGraph();
        }
        foreach (var pair in _nodeButtons)
        {
            var node = _research.Find(pair.Key);
            if (node == null)
            {
                pair.Value.style.display = DisplayStyle.None;
                continue;
            }
            string state = node.Completed ? "완료" : (node.Active ? "진행 중" : (node.Available ? "시작 가능" : "잠김"));
            pair.Value.text = node.Name + "\n" + state + " " + node.Progress.ToString("G4") + "/" + node.RequiredProgress.ToString("G4");
            pair.Value.style.backgroundColor = node.Completed ? new Color(0.12f, 0.35f, 0.22f) :
                (node.Active ? new Color(0.15f, 0.35f, 0.6f) : (node.Available ? new Color(0.2f, 0.28f, 0.4f) : new Color(0.12f, 0.13f, 0.16f)));
        }
        var selected = _research.Find(_research.ViewedId);
        _start.SetEnabled(_research.Ready && selected != null && selected.Available && !selected.Active);
        if (selected == null)
        {
            _detail.text = "연구 노드를 선택하세요.";
            return;
        }
        string parents = string.Empty;
        foreach (string id in selected.Parents)
        {
            var parent = _research.Find(id);
            parents += "\n선행 " + (parent == null ? id : parent.Name) + (parent != null && parent.Completed ? " / 완료" : " / 미완료");
        }
        _detail.text = selected.Name + "\n" + selected.Description + parents + "\n" + selected.Materials + "\n보상\n" + selected.Rewards;
    }

    private void RebuildGraph()
    {
        _layoutVersion = _research.LayoutVersion;
        _roots.Clear();
        foreach (var node in _research.Nodes)
        {
            string id = node.Id;
            var button = MakeButton(node.Name, () => _research.SelectRoot(id));
            _roots.Add(button);
        }
        _graph.Clear();
        _nodeButtons.Clear();
        _graph.Lines.Clear();
        float width = 200f;
        float height = 100f;
        foreach (var node in _research.VisibleNodes)
        {
            string id = node.Id;
            var button = MakeButton(node.Name, () => _research.ViewNode(id));
            button.style.position = Position.Absolute;
            button.style.left = node.Depth * 230 + 12;
            button.style.top = node.Row * 90 + 12;
            button.style.width = 195;
            button.style.height = 64;
            button.style.whiteSpace = WhiteSpace.Normal;
            _graph.Add(button);
            _nodeButtons.Add(id, button);
            width = Mathf.Max(width, node.Depth * 230 + 230);
            height = Mathf.Max(height, node.Row * 90 + 95);
        }
        foreach (var node in _research.VisibleNodes)
        {
            foreach (string id in node.Parents)
            {
                if (!_nodeButtons.ContainsKey(id))
                {
                    continue;
                }
                var parent = _research.Find(id);
                _graph.Lines.Add(new[] { new Vector2(parent.Depth * 230 + 207, parent.Row * 90 + 44),
                    new Vector2(node.Depth * 230 + 12, node.Row * 90 + 44) });
            }
        }
        _graph.style.width = width;
        _graph.style.height = height;
        _graph.MarkDirtyRepaint();
        _graphScroll.scrollOffset = Vector2.zero;
    }

    private void RenderTasks()
    {
        if (_panel == null)
        {
            return;
        }
        while (_rows.Count < _tasks.Rows.Count)
        {
            var row = new TaskRow();
            _rows.Add(row);
            _taskRows.Add(row.Root);
        }
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            row.Root.style.display = i < _tasks.Rows.Count ? DisplayStyle.Flex : DisplayStyle.None;
            if (i >= _tasks.Rows.Count)
            {
                continue;
            }
            var data = _tasks.Rows[i];
            row.Text.text = data.Text;
            row.Click = () => _tasks.SelectTarget(data.Task);
        }
    }

    private void OnDisable()
    {
        if (_research != null)
        {
            _research.Changed -= RenderResearch;
        }
        if (_tasks != null)
        {
            _tasks.Changed -= RenderTasks;
        }
        if (_rootObject != null)
        {
            _rootObject.SetActive(false);
            Release(_rootObject);
        }
        if (_settings != null)
        {
            Release(_settings);
        }
        _panel = null;
        _nodeButtons.Clear();
        if (_graph != null)
        {
            _graph.Clear();
            _graph.Lines.Clear();
        }
        _graph = null;
        _researchPage = null;
        _taskPage = null;
        _roots = null;
        _taskRows = null;
        _graphScroll = null;
        _rows.Clear();
        _page = 0;
    }

    private static void Release(UnityEngine.Object owned)
    {
        if (Application.isPlaying)
        {
            Destroy(owned);
        }
        else
        {
            DestroyImmediate(owned);
        }
    }
}
