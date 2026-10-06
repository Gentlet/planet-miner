using System;
using Unity.Entities;

/// <summary>
/// 역할·목적: 확정된 드론 공급/회수 생성 의도를 작업 엔티티와 World 생성 순번으로 기록한다.
/// 입력·출력: DroneTaskExecutionSystem이 대상·품목·회수 이유와 순번 상태를 제공한다. 이 유틸리티는 작업 필요 여부/우선순위를 다시 판단하지 않는다.
/// 수명·가시화: 순번 상태는 Execution OnCreate에서 준비해 World 동안 유지하고 작업 생성은 EndSimulation에 확정한다. 다음 시뮬레이션부터 배정 판단에 사용한다.
/// 제거: 작업 종료·참조 정리·삭제는 DroneTaskLifecycleApplySystem이 담당하며 번호를 삭제된 작업에 맞춰 되돌리거나 재사용하지 않는다.
/// </summary>
public static class DroneTaskCreationUtility
{
    /// <summary>
    /// DroneTaskExecutionSystem의 OnCreate에서만 호출한다. 번호 상태는 World 수명 동안 유지한다.
    /// </summary>
    public static Entity GetOrCreateSequence(EntityManager entityManager)
    {
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadWrite<DroneTaskSequence>());
        if (!query.IsEmptyIgnoreFilter)
        {
            return query.GetSingletonEntity();
        }

        var entity = entityManager.CreateEntity(typeof(DroneTaskSequence));
        entityManager.SetComponentData(entity, new DroneTaskSequence { NextValue = 1 });
        return entity;
    }

    public static Entity CreateConstructionSupplyTask(
        EntityCommandBuffer ecb,
        Entity site,
        ItemTypeEnum itemType,
        ref DroneTaskSequence sequence)
    {
        return CreateTask(ecb, site, itemType, DroneLogisticsTaskKindEnum.ConstructionSupply,
            DroneRecoveryReasonEnum.None, ref sequence);
    }

    public static Entity CreateRecoveryTask(
        EntityCommandBuffer ecb,
        Entity item,
        ItemTypeEnum itemType,
        DroneRecoveryReasonEnum reason,
        ref DroneTaskSequence sequence)
    {
        return CreateTask(ecb, item, itemType, DroneLogisticsTaskKindEnum.WorldItemRecovery,
            reason, ref sequence);
    }

    private static Entity CreateTask(
        EntityCommandBuffer ecb,
        Entity target,
        ItemTypeEnum itemType,
        DroneLogisticsTaskKindEnum kind,
        DroneRecoveryReasonEnum recoveryReason,
        ref DroneTaskSequence sequence)
    {
        if (sequence.NextValue == 0)
        {
            sequence.NextValue = 1;
        }

        if (sequence.NextValue == ulong.MaxValue)
        {
            throw new InvalidOperationException("Drone task creation sequence is exhausted.");
        }

        // 공급/회수에 같은 단조 증가 순번을 사용한다. 배치 Stamp와 별도의 작업 생성 순서를 보존한다.
        var task = ecb.CreateEntity();
        ecb.AddComponent(task, new DroneLogisticsTask
        {
            Kind = kind,
            State = DroneLogisticsTaskStateEnum.Open,
            Target = target,
            ItemType = itemType,
            CreationSequence = sequence.NextValue++,
            RecoveryReason = recoveryReason
        });
        return task;
    }
}
