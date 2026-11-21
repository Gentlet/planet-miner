using System;
using Unity.Entities;

/// <summary>
/// 역할/목적: 확정된 드론 공급/회수/일반 운반/철거 생성 의도를 작업 엔티티와 World 생성 순번으로 기록한다.
/// 입력/출력: Execution이 대상/품목/회수 이유와 운반 목표량/영역/목적 및 순번 상태를 제공한다. 작업 필요 여부나 우선순위는 다시 판단하지 않는다.
/// 수명·가시화: 순번 상태는 Execution OnCreate에서 준비해 World 동안 유지하고 작업 생성은 EndSimulation에 확정한다. 다음 시뮬레이션부터 배정 판단에 사용한다.
/// 제거: 작업 종료·참조 정리·삭제는 DroneTaskLifecycleApplySystem이 담당하며 번호를 삭제된 작업에 맞춰 되돌리거나 재사용하지 않는다.
/// </summary>
public static class DroneTaskCreationUtility
{
    internal static Entity RecordCreation(EntityCommandBuffer ecb, in DroneTaskCreationDecisionElement creation,
        ref DroneTaskSequence sequence, bool hasExplicitRecoveryIntent = false, bool stationDemolition = false)
    {
        switch (creation.Kind)
        {
            case DroneLogisticsTaskKindEnum.BuildingDemolition:
                return CreateBuildingDemolitionTask(ecb, creation.Target, ref sequence, stationDemolition);
            case DroneLogisticsTaskKindEnum.ConstructionSupply:
                return CreateTask(ecb, creation.Target, creation.ItemType, creation.Kind,
                    DroneRecoveryReasonEnum.None, ref sequence);
            case DroneLogisticsTaskKindEnum.WorldItemRecovery:
                return CreateTask(ecb, creation.Target, creation.ItemType, creation.Kind,
                    creation.RecoveryReason, ref sequence, hasExplicitRecoveryIntent,
                    purpose: creation.Purpose, demolitionRoot: creation.DemolitionRoot);
            default:
                Entity task = CreateTask(ecb, creation.Target, creation.ItemType, creation.Kind,
                    DroneRecoveryReasonEnum.None, ref sequence, area: creation.Area, purpose: creation.Purpose,
                    demolitionRoot: creation.DemolitionRoot);
                ecb.AddComponent(task, new DroneTransportRequirement { Quantity = creation.Quantity });
                ecb.AddComponent(task, new DroneTransportProgress());
        return task;
        }
    }

    public static Entity CreateBuildingDemolitionTask(EntityCommandBuffer ecb, Entity target, ref DroneTaskSequence sequence,
        bool stationDemolition = false)
    {
        Entity task = CreateTask(ecb, target, ItemTypeEnum.None, DroneLogisticsTaskKindEnum.BuildingDemolition,
            DroneRecoveryReasonEnum.None, ref sequence);
        ecb.AddComponent(task, new DroneDemolitionProgress());
        ecb.AddBuffer<DroneDemolitionRefundElement>(task);
        // deferred root는 생성과 같은 ECB에서만 건물 연결에 기록한다.
        // 정거장은 충전을 보존하는 준비 상태로 시작한다. 실제 중단은 전원 수납 뒤의 확인이 소유한다.
        if (stationDemolition)
        {
            ecb.SetComponent(target, new PreparingStationDemolition { Task = task });
        }
        else
        {
            ecb.SetComponent(target, new PendingBuildingDemolition { Task = task });
        }
        return task;
    }

    private static Entity CreateTask(
        EntityCommandBuffer ecb,
        Entity target,
        ItemTypeEnum itemType,
        DroneLogisticsTaskKindEnum kind,
        DroneRecoveryReasonEnum recoveryReason,
        ref DroneTaskSequence sequence,
        bool hasExplicitRecoveryIntent = false,
        DroneTransportAreaEnum area = default, DroneTransportPurposeEnum purpose = default,
        Entity demolitionRoot = default)
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
            Area = area,
            Purpose = purpose,
            DemolitionRoot = demolitionRoot,
            State = DroneLogisticsTaskStateEnum.Open,
            Target = target,
            ItemType = itemType,
            CreationSequence = sequence.NextValue++,
            RecoveryReason = recoveryReason,
            HasExplicitRecoveryIntent = hasExplicitRecoveryIntent
        });
        if (kind == DroneLogisticsTaskKindEnum.BuildingDemolition)
            ecb.AddBuffer<DroneStationRelocationMemberElement>(task);
        return task;
    }
}
