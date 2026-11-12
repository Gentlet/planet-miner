using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

/// <summary>트리 루트/상세 조회/실제 활성 연구를 분리한다. 화면 초안 외 상태는 Registry에서 다시 읽는다.</summary>
public sealed class ResearchPanelController : MonoBehaviour
{
    private World _world;
    private Entity _registry;
    private ulong _feedbackToken;
    private readonly HashSet<Entity> _pending = new HashSet<Entity>();
    private string _definitionSignature;
    private float _nextRefresh;
    public readonly List<ResearchUiNode> Nodes = new List<ResearchUiNode>();
    public readonly List<ResearchUiNode> VisibleNodes = new List<ResearchUiNode>();
    public int RootId { get; private set; }
    public int ViewedId { get; private set; }
    public int ActiveId { get; private set; }
    public string ActiveText { get; private set; }
    public string Feedback { get; private set; }
    public bool Ready { get; private set; }
    public int LayoutVersion { get; private set; }
    public event Action Changed;

    private void OnEnable()
    {
        Refresh();
    }

    private void OnDisable()
    {
        Disconnect();
        Ready = false;
        Changed?.Invoke();
    }

    private void Disconnect()
    {
        WorldUiRequestFeedbackRelay.Unregister(_feedbackToken);
        _feedbackToken = 0;
        _pending.Clear();
        _world = null;
        _registry = Entity.Null;
        RootId = 0;
        ViewedId = 0;
        ActiveId = 0;
        _definitionSignature = null;
        Feedback = string.Empty;
        Nodes.Clear();
        VisibleNodes.Clear();
        LayoutVersion++;
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime < _nextRefresh)
        {
            return;
        }
        _nextRefresh = Time.unscaledTime + 0.2f;
        Refresh();
    }

    public void Refresh()
    {
        World current = World.DefaultGameObjectInjectionWorld;
        if (!ReferenceEquals(current, _world))
        {
            Disconnect();
            _world = current;
            if (_world != null)
            {
                if (_world.IsCreated)
                {
                    _feedbackToken = WorldUiRequestFeedbackRelay.Register(_world, ReceiveFeedback);
                }
            }
        }
        Ready = false;
        Nodes.Clear();
        VisibleNodes.Clear();
        if (_world == null)
        {
            Changed?.Invoke();
            return;
        }
        if (!_world.IsCreated)
        {
            Changed?.Invoke();
            return;
        }
        var manager = _world.EntityManager;
        using var query = manager.CreateEntityQuery(typeof(ResearchRegistry), typeof(ResearchState),
            typeof(ResearchDefinitionElement), typeof(ResearchPrerequisiteElement), typeof(ResearchProgressElement),
            typeof(ResearchIngredientElement), typeof(ResearchRewardElement));
        if (query.CalculateEntityCount() != 1)
        {
            Changed?.Invoke();
            return;
        }
        _registry = query.GetSingletonEntity();
        ActiveId = manager.GetComponentData<ResearchState>(_registry).ActiveResearchId;
        var definitions = manager.GetBuffer<ResearchDefinitionElement>(_registry, true);
        var prerequisites = manager.GetBuffer<ResearchPrerequisiteElement>(_registry, true);
        var progress = manager.GetBuffer<ResearchProgressElement>(_registry, true);
        var ingredients = manager.GetBuffer<ResearchIngredientElement>(_registry, true);
        var rewards = manager.GetBuffer<ResearchRewardElement>(_registry, true);
        using var buildingQuery = manager.CreateEntityQuery(typeof(BuildingConfig), typeof(BuildingConfigElement));
        NativeArray<BuildingConfigElement> buildingConfigs = default;
        if (buildingQuery.CalculateEntityCount() == 1)
        {
            buildingConfigs = manager.GetBuffer<BuildingConfigElement>(buildingQuery.GetSingletonEntity(), true).AsNativeArray();
        }
        var complete = new HashSet<int>();
        foreach (var entry in progress)
        {
            if (entry.IsCompleted)
            {
                complete.Add(entry.ResearchId);
            }
        }
        var signature = new StringBuilder();
        foreach (var definition in definitions)
        {
            var node = new ResearchUiNode
            {
                Id = definition.StableId, Name = definition.DisplayName.ToString(),
                Description = definition.Description.ToString(), RequiredProgress = definition.RequiredProgress
            };
            node.Active = node.Id == ActiveId;
            node.Completed = complete.Contains(node.Id);
            node.Available = !node.Completed;
            signature.Append(node.Id).Append(':');
            for (int i = 0; i < definition.PrerequisiteCount; i++)
            {
                int parent = prerequisites[definition.PrerequisiteStart + i].ResearchId;
                node.Parents.Add(parent);
                node.Available &= complete.Contains(parent);
                signature.Append(parent).Append(',');
            }
            signature.Append(';');
            foreach (var entry in progress)
            {
                if (entry.ResearchId == definition.StableId)
                {
                    node.Progress = entry.Progress;
                    break;
                }
            }
            var materialText = new StringBuilder();
            materialText.AppendLine("1주기 재료 / 처음부터 완료까지 계획 총량");
            for (int i = 0; i < definition.IngredientCount; i++)
            {
                var ingredient = ingredients[definition.IngredientStart + i];
                materialText.AppendLine(ingredient.ItemType + " " + ingredient.Amount + " / " +
                    ResearchTreeLayoutUtility.PlannedTotal(definition.RequiredProgress, definition.ProgressPerCycle, ingredient.Amount).ToString("G6"));
            }
            materialText.AppendLine("계획 총량은 남은 재료/실제 소비량이 아닙니다.");
            node.Materials = materialText.ToString();
            var rewardText = new StringBuilder();
            for (int i = 0; i < definition.RewardCount; i++)
            {
                var reward = rewards[definition.RewardStart + i];
                switch (reward.Type)
                {
                    case ResearchRewardTypeEnum.BuildingUnlock:
                        string buildingName = reward.BuildingId.ToString();
                        if (buildingConfigs.IsCreated &&
                            BuildingConfigLookupUtility.TryGetConfig(buildingConfigs, reward.BuildingId, out var building))
                        {
                            buildingName = building.DisplayName.ToString();
                        }
                        rewardText.AppendLine("건물 해금: " + buildingName);
                        break;
                    case ResearchRewardTypeEnum.RecipeUnlock:
                        rewardText.AppendLine("레시피 해금: " + reward.RecipeId);
                        break;
                    case ResearchRewardTypeEnum.StatModifier:
                        rewardText.AppendLine(reward.StatKey + " +" + (reward.Bonus * 100f).ToString("G4") + "%");
                        break;
                }
            }
            node.Rewards = rewardText.ToString();
            Nodes.Add(node);
        }
        string nextSignature = signature.ToString();
        if (_definitionSignature != nextSignature)
        {
            _definitionSignature = nextSignature;
            LayoutVersion++;
        }
        if (Find(RootId) == null)
        {
            if (Nodes.Count > 0)
            {
                RootId = Nodes[0].Id;
                foreach (var node in Nodes)
                {
                    if (node.Parents.Count == 0)
                    {
                        RootId = node.Id;
                        break;
                    }
                }
                LayoutVersion++;
            }
        }
        if (Find(ViewedId) == null)
        {
            ViewedId = ActiveId == 0 ? RootId : ActiveId;
        }
        Ready = ResearchTreeLayoutUtility.TryArrange(Nodes, RootId, VisibleNodes);
        using var buildings = manager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<ResearchCycleState>() },
            None = new[] { ComponentType.ReadOnly<PendingBuildingDemolition>() }
        });
        var active = Find(ActiveId);
        ActiveText = active == null ? "활성 연구 없음" : "진행 연구: " + active.Name + " " + active.Progress.ToString("G6") + "/" + active.RequiredProgress.ToString("G6");
        ActiveText += "\n작동 가능한 연구건물 " + buildings.CalculateEntityCount() + "개";
        if (active != null)
        {
            ActiveText += "\n1주기 재료\n";
            foreach (var definition in definitions)
            {
                if (definition.StableId != ActiveId)
                {
                    continue;
                }
                for (int i = 0; i < definition.IngredientCount; i++)
                {
                    var ingredient = ingredients[definition.IngredientStart + i];
                    ActiveText += ingredient.ItemType + " " + ingredient.Amount + "\n";
                }
            }
        }
        Changed?.Invoke();
    }

    public ResearchUiNode Find(int id)
    {
        foreach (var node in Nodes)
        {
            if (node.Id == id)
            {
                return node;
            }
        }
        return null;
    }

    public void SelectRoot(int id)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }
        if (Find(id) == null)
        {
            return;
        }
        RootId = id;
        ViewedId = id;
        LayoutVersion++;
        ResearchTreeLayoutUtility.TryArrange(Nodes, RootId, VisibleNodes);
        Changed?.Invoke();
    }

    public void ViewNode(int id)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }
        if (Find(id) == null)
        {
            return;
        }
        ViewedId = id;
        Changed?.Invoke();
    }

    public Entity StartViewed()
    {
        if (!isActiveAndEnabled || !Ready)
        {
            return Entity.Null;
        }
        if (_world == null)
        {
            return Entity.Null;
        }
        if (!_world.IsCreated)
        {
            return Entity.Null;
        }
        if (!ReferenceEquals(_world, World.DefaultGameObjectInjectionWorld))
        {
            return Entity.Null;
        }
        var node = Find(ViewedId);
        if (node == null)
        {
            return Entity.Null;
        }
        if (!node.Available || node.Active)
        {
            return Entity.Null;
        }
        Entity request = ResearchSelectionRequestUtility.Submit(_world.EntityManager, node.Id);
        _world.EntityManager.AddComponentData(request, new WorldUiRequestOwner { FeedbackToken = _feedbackToken });
        _pending.Add(request);
        Feedback = "연구 선택 요청 대기";
        Changed?.Invoke();
        return request;
    }

    private void ReceiveFeedback(WorldUiRequestFeedback feedback)
    {
        if (!isActiveAndEnabled || !_pending.Remove(feedback.Request))
        {
            return;
        }
        Feedback = feedback.Message;
        Refresh();
    }
}
