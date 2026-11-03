using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public readonly struct TaskOverviewRow
{
    public readonly Entity Task;
    public readonly ulong Sequence;
    public readonly string Text;
    public TaskOverviewRow(Entity task, ulong sequence, string text)
    {
        Task = task;
        Sequence = sequence;
        Text = text;
    }
}

/// <summary>전체 작업 원본을 조회한다. 대상 선택 외 명령/취소/우선순위 상태를 만들지 않는다.</summary>
[RequireComponent(typeof(WorldSelectionController))]
public sealed class TaskOverviewController : MonoBehaviour
{
    private World _world;
    private WorldSelectionController _selection;
    private float _nextRefresh;
    public readonly List<TaskOverviewRow> Rows = new List<TaskOverviewRow>();
    public event Action Changed;

    private void OnEnable()
    {
        _selection = GetComponent<WorldSelectionController>();
        Refresh();
    }

    private void OnDisable()
    {
        _world = null;
        Rows.Clear();
        Changed?.Invoke();
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
        Rows.Clear();
        _world = World.DefaultGameObjectInjectionWorld;
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
        using var tasks = manager.CreateEntityQuery(typeof(DroneLogisticsTask));
        using var owners = tasks.ToEntityArray(Allocator.Temp);
        using var assignments = manager.CreateEntityQuery(typeof(DroneTaskAssignment));
        using var workers = assignments.ToComponentDataArray<DroneTaskAssignment>(Allocator.Temp);
        foreach (Entity owner in owners)
        {
            var task = manager.GetComponentData<DroneLogisticsTask>(owner);
            int active = 0;
            foreach (var assignment in workers)
            {
                if (assignment.Task == owner && !DroneSchedulingUtility.IsTerminal(assignment.State))
                {
                    active++;
                }
            }
            string status = task.State.ToString();
            if (active > 0 && (task.State == DroneLogisticsTaskStateEnum.Closed || task.State == DroneLogisticsTaskStateEnum.Cancelled))
            {
                status += " / 화물 복구/정산 중";
            }
            string amount = string.Empty;
            if (manager.HasComponent<DroneTransportRequirement>(owner) && manager.HasComponent<DroneTransportProgress>(owner))
            {
                var goal = manager.GetComponentData<DroneTransportRequirement>(owner);
                var progress = manager.GetComponentData<DroneTransportProgress>(owner);
                amount = " / 완료 " + progress.DeliveredQuantity + "/" + goal.Quantity + " / 확보 " + progress.ReservedQuantity;
            }
            if (task.Kind == DroneLogisticsTaskKindEnum.ConstructionSupply)
            {
                if (manager.HasBuffer<ConstructionMaterialRequirementElement>(task.Target))
                {
                    foreach (var material in manager.GetBuffer<ConstructionMaterialRequirementElement>(task.Target, true))
                    {
                        if (material.ItemType == task.ItemType)
                        {
                            amount = " / 현장 " + material.DeliveredQuantity + "/" + material.RequiredQuantity + " / 예약 " + material.ReservedQuantity;
                        }
                    }
                }
            }
            string text = "#" + task.CreationSequence + " " + task.Kind + " / " + task.ItemType + " / " + status +
                amount + " / 수행 드론 " + active + " / 대상 " + task.Target;
            Rows.Add(new TaskOverviewRow(owner, task.CreationSequence, text));
        }
        Rows.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));
        Changed?.Invoke();
    }

    public bool SelectTarget(Entity task)
    {
        if (!isActiveAndEnabled)
        {
            return false;
        }
        if (_world == null)
        {
            return false;
        }
        if (!_world.IsCreated)
        {
            return false;
        }
        if (!ReferenceEquals(_world, World.DefaultGameObjectInjectionWorld))
        {
            return false;
        }
        var manager = _world.EntityManager;
        if (!manager.HasComponent<DroneLogisticsTask>(task))
        {
            return false;
        }
        return _selection.SelectEntity(_world, manager.GetComponentData<DroneLogisticsTask>(task).Target);
    }
}
