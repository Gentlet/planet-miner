using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

/// <summary>기존 Task Decision의 요청 판단/제어 의도를 준비한다. 작업/예약/실물은 변경하지 않는다.</summary>
internal static class DroneTaskRequestDecisionUtility
{
    internal struct Record
    {
        public Entity Entity;
        public DroneTaskRequest Request;
    }

    private struct Comparer : IComparer<Record>
    {
        public int Compare(Record left, Record right) => left.Request.Sequence.CompareTo(right.Request.Sequence);
    }

    public static NativeList<Record> ReadRequests(EntityManager manager, EntityQuery query)
    {
        using var entities = query.ToEntityArray(Allocator.Temp);
        var records = new NativeList<Record>(entities.Length, Allocator.Temp);
        foreach (Entity entity in entities)
        {
            records.Add(new Record { Entity = entity, Request = manager.GetComponentData<DroneTaskRequest>(entity) });
        }

        records.AsArray().Sort(new Comparer());
        return records;
    }

    public static void Prepare(EntityManager manager, Entity candidates, EntityQuery requests, NativeArray<Entity> tasks, NativeArray<Entity> sites, NativeArray<Entity> assignments)
    {
        var controls = manager.GetBuffer<DroneTaskGenerationDecisionElement>(candidates);
        controls.Clear();
        using var records = ReadRequests(manager, requests);
        foreach (var record in records)
        {
            var request = record.Request;
            var decision = new DroneTaskRequestDecision
            {
                Status = DroneTaskRequestStatusEnum.Rejected
            };
            if (request.Operation == DroneTaskRequestOperationEnum.Create)
            {
                decision.Status = ValidateCreation(manager, request, sites, out var reason);
                decision.RecoveryReason = reason;
                if (decision.Status == DroneTaskRequestStatusEnum.Created)
                {
                    var key = DroneTaskGenerationKey.From(request.Kind, request.Target, request.ItemType);
                    foreach (Entity taskEntity in tasks)
                    {
                        var task = manager.GetComponentData<DroneLogisticsTask>(taskEntity);
                        if (DroneSchedulingUtility.IsTaskValid(manager, task, sites) && key.Equals(Key(task)) && !IsCancellationPlanned(manager, candidates, taskEntity))
                        {
                            decision.Task = taskEntity;
                            decision.Status = DroneTaskRequestStatusEnum.Existing;
                            break;
                        }
                    }

                    if (decision.Task == Entity.Null)
                    {
                        foreach (var earlier in records)
                        {
                            if (earlier.Entity == record.Entity)
                            {
                                break;
                            }

                            if (earlier.Request.Operation != DroneTaskRequestOperationEnum.Create || !key.Equals(DroneTaskGenerationKey.From(earlier.Request.Kind, earlier.Request.Target, earlier.Request.ItemType)))
                            {
                                continue;
                            }

                            var prior = manager.GetComponentData<DroneTaskRequestDecision>(earlier.Entity);
                            if (prior.Status != DroneTaskRequestStatusEnum.Created && prior.Status != DroneTaskRequestStatusEnum.Existing)
                            {
                                continue;
                            }

                            if (prior.Task != Entity.Null && IsCancellationPlanned(manager, candidates, prior.Task))
                            {
                                continue;
                            }

                            decision.Task = prior.Task;
                            if (prior.Task == Entity.Null)
                            {
                                decision.CreatingRequest = prior.CreatingRequest == Entity.Null ? earlier.Entity : prior.CreatingRequest;
                            }
                            decision.Status = DroneTaskRequestStatusEnum.Existing;
                            break;
                        }
                    }

                    controls.Add(new DroneTaskGenerationDecisionElement { Key = key, Blocked = false });
                }
            }
            else if (request.Operation == DroneTaskRequestOperationEnum.Cancel)
            {
                decision.Status = ValidateCancellation(manager, candidates, request.Task, assignments);
                if (decision.Status == DroneTaskRequestStatusEnum.Cancelled)
                {
                    controls.Add(new DroneTaskGenerationDecisionElement { Key = Key(manager.GetComponentData<DroneLogisticsTask>(request.Task)), Blocked = true, CancelledTask = request.Task });
                }
            }
            else
            {
                decision.Status = DroneTaskRequestStatusEnum.Unsupported;
            }

            manager.SetComponentData(record.Entity, decision);
        }
    }

    public static DroneTaskGenerationKey Key(in DroneLogisticsTask task) => DroneTaskGenerationKey.From(task.Kind, task.Target, task.ItemType);
    public static bool IsCancellationPlanned(EntityManager manager, Entity candidates, Entity task)
    {
        foreach (var decision in manager.GetBuffer<DroneTaskGenerationDecisionElement>(candidates, true))
        {
            if (decision.CancelledTask == task)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsBlocked(EntityManager manager, Entity candidates, DroneTaskGenerationKey key)
    {
        var controls = manager.GetBuffer<DroneTaskGenerationDecisionElement>(candidates, true);
        for (int i = controls.Length - 1; i >= 0; i--)
        {
            if (controls[i].Key.Equals(key))
            {
                return controls[i].Blocked;
            }
        }

        foreach (var block in manager.GetBuffer<DroneTaskGenerationBlockElement>(candidates, true))
        {
            if (block.Key.Equals(key))
            {
                return true;
            }
        }

        return false;
    }

    private static DroneTaskRequestStatusEnum ValidateCreation(EntityManager manager, in DroneTaskRequest request, NativeArray<Entity> sites, out DroneRecoveryReasonEnum reason)
    {
        reason = DroneRecoveryReasonEnum.None;
        if (request.Kind != DroneLogisticsTaskKindEnum.ConstructionSupply && request.Kind != DroneLogisticsTaskKindEnum.WorldItemRecovery)
        {
            return DroneTaskRequestStatusEnum.Unsupported;
        }

        if (request.ItemType == ItemTypeEnum.None)
        {
            return DroneTaskRequestStatusEnum.Rejected;
        }

        if (request.Kind == DroneLogisticsTaskKindEnum.ConstructionSupply)
        {
            if (!DroneSchedulingUtility.IsSite(manager, request.Target))
            {
                return DroneTaskRequestStatusEnum.Rejected;
            }

            foreach (var requirement in manager.GetBuffer<ConstructionMaterialRequirementElement>(request.Target, true))
            {
                if (requirement.ItemType == request.ItemType && !requirement.IsSatisfied)
                {
                    return DroneTaskRequestStatusEnum.Created;
                }
            }

            return DroneTaskRequestStatusEnum.Rejected;
        }

        if (!DroneSchedulingUtility.IsWorldItem(manager, request.Target, request.ItemType))
        {
            return DroneTaskRequestStatusEnum.Rejected;
        }

        if (manager.HasComponent<DroneRecoveryPending>(request.Target))
        {
            reason = DroneRecoveryReasonEnum.DroneDrop;
        }
        else if (DroneSchedulingUtility.IsUnderSite(manager, request.Target, sites))
        {
            reason = DroneRecoveryReasonEnum.SiteClearance;
        }

        return reason == DroneRecoveryReasonEnum.None ? DroneTaskRequestStatusEnum.Rejected : DroneTaskRequestStatusEnum.Created;
    }

    public static DroneTaskRequestStatusEnum ValidateCancellation(EntityManager manager, Entity candidates, Entity taskEntity, NativeArray<Entity> assignments, bool checkPlan = true)
    {
        if (!DroneSchedulingUtility.IsActiveEntity(manager, taskEntity))
        {
            return DroneTaskRequestStatusEnum.Rejected;
        }

        if (!manager.HasComponent<DroneLogisticsTask>(taskEntity))
        {
            return DroneTaskRequestStatusEnum.Rejected;
        }

        var task = manager.GetComponentData<DroneLogisticsTask>(taskEntity);
        if (task.State != DroneLogisticsTaskStateEnum.Open)
        {
            return DroneTaskRequestStatusEnum.Rejected;
        }

        if (task.Kind != DroneLogisticsTaskKindEnum.ConstructionSupply && task.Kind != DroneLogisticsTaskKindEnum.WorldItemRecovery)
        {
            return DroneTaskRequestStatusEnum.Unsupported;
        }

        if (checkPlan && IsCancellationPlanned(manager, candidates, taskEntity))
        {
            return DroneTaskRequestStatusEnum.Rejected;
        }

        foreach (Entity assignment in assignments)
        {
            if (manager.GetComponentData<DroneTaskAssignment>(assignment).Task == taskEntity)
            {
                return DroneTaskRequestStatusEnum.Busy;
            }
        }

        foreach (var pending in manager.GetBuffer<DroneTaskPendingPublicationElement>(candidates, true))
        {
            if (pending.Candidate.Task == taskEntity && (pending.Quantity > 0 || pending.CommittedQuantity > 0 || pending.PublicationQueued))
            {
                return DroneTaskRequestStatusEnum.Busy;
            }
        }

        if (task.Kind == DroneLogisticsTaskKindEnum.ConstructionSupply && DroneSchedulingUtility.IsSite(manager, task.Target))
        {
            foreach (var requirement in manager.GetBuffer<ConstructionMaterialRequirementElement>(task.Target, true))
            {
                if (requirement.ItemType == task.ItemType && requirement.ReservedQuantity > 0)
                {
                    return DroneTaskRequestStatusEnum.Busy;
                }
            }
        }

        return DroneTaskRequestStatusEnum.Cancelled;
    }
}
