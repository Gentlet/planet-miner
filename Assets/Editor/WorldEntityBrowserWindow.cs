using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class WorldEntityBrowserWindow : EditorWindow
{
    // Unity Entities 6.6의 Hierarchy가 사용하는 Runtime Inspector 선택 경로다.
    private static readonly Action<World, Entity> SelectForInspector =
        Type.GetType("Unity.Entities.Editor.EntitySelectionProxy, Unity.Entities.Editor")
            ?.GetMethod("SelectEntity", new[] { typeof(World), typeof(Entity) })
            ?.CreateDelegate(typeof(Action<World, Entity>)) as Action<World, Entity>;

    private Label _worldLabel;
    private ScrollView _groupsView;
    private EntityGroup[] _groups;
    private World _world;

    [MenuItem("Tools/Planet Miner/World Entities")]
    private static void Open()
    {
        var window = GetWindow<WorldEntityBrowserWindow>("World Entities");
        window.minSize = new Vector2(380f, 240f);
    }

    public void CreateGUI()
    {
        ReleaseQueries();
        rootVisualElement.Clear();
        rootVisualElement.style.paddingLeft = 8f;
        rootVisualElement.style.paddingRight = 8f;
        rootVisualElement.style.paddingTop = 6f;

        _worldLabel = new Label { name = "world" };
        _worldLabel.style.marginBottom = 6f;
        _groupsView = new ScrollView();
        _groupsView.style.flexGrow = 1f;
        rootVisualElement.Add(_worldLabel);
        rootVisualElement.Add(_groupsView);

        _groups = new[]
        {
            new EntityGroup("Resource", ComponentType.ReadOnly<ResourceNode>()),
            new EntityGroup("Item", ComponentType.ReadOnly<ItemIdentity>()),
            new EntityGroup("Building", ComponentType.ReadOnly<BuildingIdentity>()),
            new EntityGroup("ConfigData", ComponentType.ReadOnly<ConfigDataDebugTag>(), includeGridPosition: false)
        };
        foreach (var group in _groups)
        {
            _groupsView.Add(group.View);
        }
        OnInspectorUpdate();
    }

    private void OnInspectorUpdate()
    {
        if (_groups == null)
        {
            return;
        }

        var world = GetActiveGameWorld();
        if (!ReferenceEquals(_world, world))
        {
            _world = world;
            foreach (var group in _groups)
            {
                group.SetWorld(world);
            }
        }

        _worldLabel.text = world?.Name ?? "실행 중인 게임 월드가 없습니다.";
        _groupsView.style.display = world != null ? DisplayStyle.Flex : DisplayStyle.None;
        foreach (var group in _groups)
        {
            group.Refresh();
        }
    }

    private static World GetActiveGameWorld()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (!IsWorldCreated(world))
        {
            return null;
        }
        if ((world.Flags & WorldFlags.Game) != WorldFlags.Game)
        {
            return null;
        }
        return world;
    }

    private static bool IsWorldCreated(World world)
    {
        if (world == null)
        {
            return false;
        }
        return world.IsCreated;
    }

    private void OnDisable() => ReleaseQueries();

    private void ReleaseQueries()
    {
        if (_groups != null)
        {
            foreach (var group in _groups)
            {
                group.SetWorld(null);
            }
        }
        _groups = null;
        _world = null;
    }

    private sealed class EntityGroup
    {
        private const float RowHeight = 20f;
        private const int VisibleRows = 8;
        private readonly ComponentType[] _requiredComponents;
        private readonly List<Entity> _entities = new();
        private readonly ListView _list;
        private readonly Label _empty;
        private World _world;
        private EntityQuery _query;
        private int? _orderVersion;

        internal Foldout View { get; }

        internal EntityGroup(string name, ComponentType type, bool includeGridPosition = true)
        {
            _requiredComponents = includeGridPosition
                ? new[] { type, ComponentType.ReadOnly<GridPosition>() }
                : new[] { type };
            View = new Foldout { text = name, name = name, viewDataKey = $"world-entities-{name}", value = true };
            View.style.marginBottom = 8f;
            var header = new EntityRow();
            header.SetValues("이름", includeGridPosition ? "좌표" : string.Empty);
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            View.Add(header);

            _list = new ListView(_entities, RowHeight, CreateSelectableRow, BindRow)
            {
                name = "entities",
                selectionType = SelectionType.Single,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly
            };
            _list.style.flexShrink = 0f;
            _list.unbindItem = (element, _) => ((EntityRow)element).Target = Entity.Null;
            _list.RegisterCallback<NavigationSubmitEvent>(_ => InspectEntity(GetSelectedEntity()), TrickleDown.TrickleDown);
            _empty = new Label("표시할 엔티티가 없습니다.");
            View.Add(_list);
            View.Add(_empty);
            View.RegisterValueChangedCallback(change =>
            {
                if (change.newValue)
                {
                    Refresh();
                }
            });
            UpdateListLayout();
        }

        internal void SetWorld(World world)
        {
            // 폐기된 World의 쿼리는 World가 이미 정리한다.
            if (IsWorldCreated(_world))
            {
                _query.Dispose();
            }
            _world = world;
            _orderVersion = null;
            _list.SetSelectionWithoutNotify(Array.Empty<int>());
            _entities.Clear();
            if (world != null)
            {
                _query = world.EntityManager.CreateEntityQuery(new EntityQueryDesc
                {
                    All = _requiredComponents,
                    Options = EntityQueryOptions.IncludeDisabledEntities
                });
            }
            UpdateListLayout();
            _list.RefreshItems();
        }

        internal void Refresh()
        {
            if (!IsWorldCreated(_world))
            {
                return;
            }
            if (!View.value)
            {
                return;
            }

            RefreshEntities();
            _list.RefreshItems();
        }

        private void RefreshEntities()
        {
            // 좌표 변경은 표시 중인 행에 반영하고, 엔티티 목록은 구조가 바뀔 때만 다시 읽는다.
            int orderVersion = _query.GetCombinedComponentOrderVersion(includeEntityType: false);
            if (_orderVersion != orderVersion)
            {
                Entity selected = GetSelectedEntity();
                using var current = _query.ToEntityArray(Allocator.Temp);
                _entities.Clear();
                foreach (Entity entity in current)
                {
                    _entities.Add(entity);
                }
                _orderVersion = orderVersion;
                FinishListChange(selected);
            }
        }

        private Entity GetSelectedEntity()
        {
            int index = _list.selectedIndex;
            return index >= 0 && index < _entities.Count ? _entities[index] : Entity.Null;
        }

        private void FinishListChange(Entity selected)
        {
            int index = _entities.IndexOf(selected);
            _list.SetSelectionWithoutNotify(index >= 0 ? new[] { index } : Array.Empty<int>());
            UpdateListLayout();
        }

        private VisualElement CreateSelectableRow()
        {
            var row = new EntityRow();
            row.RegisterCallback<PointerDownEvent>(SelectRow, TrickleDown.TrickleDown);
            return row;
        }

        private void SelectRow(PointerDownEvent pointer)
        {
            if (pointer.button == 0)
            {
                // 각 그룹은 선택을 유지하므로 같은 행을 다시 클릭할 때도 Inspector를 갱신한다.
                InspectEntity(((EntityRow)pointer.currentTarget).Target);
            }
        }

        private void InspectEntity(Entity entity)
        {
            if (!IsWorldCreated(_world))
            {
                return;
            }
            if (!ReferenceEquals(_world, GetActiveGameWorld()))
            {
                return;
            }

            if (!_world.EntityManager.Exists(entity))
            {
                return;
            }
            if (SelectForInspector == null)
            {
                Debug.LogWarning("현재 Entities 패키지에서 엔티티 Inspector 선택 기능을 찾을 수 없습니다.");
                return;
            }
            SelectForInspector(_world, entity);
        }

        private void UpdateListLayout()
        {
            bool hasEntities = _entities.Count > 0;
            _list.style.height = Mathf.Min(_entities.Count, VisibleRows) * RowHeight;
            _list.style.display = hasEntities ? DisplayStyle.Flex : DisplayStyle.None;
            _empty.style.display = hasEntities ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void BindRow(VisualElement element, int index)
        {
            var row = (EntityRow)element;
            row.Target = Entity.Null;
            if (!IsWorldCreated(_world))
            {
                row.SetValues(string.Empty, string.Empty);
                return;
            }

            Entity entity = _entities[index];
            var manager = _world.EntityManager;
            if (!manager.Exists(entity))
            {
                row.SetValues(string.Empty, string.Empty);
                return;
            }

            row.Target = entity;
            string name = manager.GetName(entity);
            string position = string.Empty;
            if (manager.HasComponent<GridPosition>(entity))
            {
                var cell = manager.GetComponentData<GridPosition>(entity).Value;
                position = $"({cell.x}, {cell.y})";
            }
            row.SetValues(string.IsNullOrEmpty(name) ? entity.ToString() : name, position);
        }
    }

    private sealed class EntityRow : VisualElement
    {
        private readonly Label _entityName = new() { name = "entity-name" };
        private readonly Label _cell = new() { name = "grid-cell" };
        internal Entity Target { get; set; }

        internal EntityRow()
        {
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.paddingLeft = 4f;
            _entityName.style.flexGrow = 1f;
            _entityName.style.flexBasis = 0f;
            _entityName.style.overflow = Overflow.Hidden;
            _entityName.style.textOverflow = TextOverflow.Ellipsis;
            _cell.style.width = 148f;
            _cell.style.unityTextAlign = TextAnchor.MiddleRight;
            _cell.style.flexShrink = 0f;
            Add(_entityName);
            Add(_cell);
        }

        internal void SetValues(string name, string cell)
        {
            _entityName.text = name;
            _cell.text = cell;
            _entityName.tooltip = name;
            _cell.tooltip = cell;
        }
    }
}
