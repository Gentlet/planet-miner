using System;
using Unity.Entities;

/// <summary>
/// 역할·목적: 외부 드론 행동 완료 신호의 World 접수 순번과 요청→결과 소비 수명을 소유한다.
/// 입력·출력: 주 스레드에서 Submit이 행동 요청/인계 계획 버퍼를 즉시 만들고 ReceiptSequence를 부여한다. 실제 수행부 호출은 후속 연결이다.
/// 이용: 인계 Decision/Execution이 준비한 계획을 DroneTaskLifecycleApplySystem이 정산한 뒤 외부 수행부가 TryConsumeResult로 결과를 읽는다.
/// 수명·가시화: 접수 순번 상태는 World 수명 동안 유지한다. 요청 제거/결과 게시는 EndStateApply, 결과 확인 후 엔티티 삭제는 외부 소비 경계다. 실물을 직접 변경하지 않는다.
/// </summary>
public static class DroneActionRequestUtility
{
    /// <summary>시뮬레이션 전 주 스레드 입력을 접수한다. 외부가 준 행동 번호와 별도로 World 접수 순번을 발급한다.</summary>
    public static Entity Submit(EntityManager manager, in DroneActionReadyRequest request)
    {
        Entity sequenceEntity = GetOrCreateReceiptSequence(manager);
        var sequence = manager.GetComponentData<DroneActionReceiptSequence>(sequenceEntity);
        if (sequence.NextValue == 0 || sequence.NextValue == ulong.MaxValue)
        {
            throw new InvalidOperationException("Drone action receipt sequence is exhausted or invalid.");
        }

        var accepted = request;
        accepted.ReceiptSequence = sequence.NextValue;
        sequence.NextValue++;
        manager.SetComponentData(sequenceEntity, sequence);
        // 요청과 계획 버퍼를 같은 엔티티에 준비한다. 결과 공개 후 외부 소비까지 같은 엔티티가 행동 참조를 유지한다.
        Entity entity = manager.CreateEntity(typeof(DroneActionReadyRequest), typeof(DroneItemTransferDecision));
        manager.AddBuffer<DroneItemTransferItemDecisionElement>(entity);
        manager.AddBuffer<DroneItemTransferSlotDecisionElement>(entity);
        manager.SetComponentData(entity, accepted);
        return entity;
    }

    /// <summary>EndStateApply 이후 공개된 결과를 외부 수행부가 읽고 요청/결과 엔티티를 소비한다.</summary>
    public static bool TryConsumeResult(EntityManager manager, Entity entity, out DroneItemTransferResult result)
    {
        result = default;
        if (entity == Entity.Null)
        {
            return false;
        }

        if (!manager.Exists(entity))
        {
            return false;
        }

        if (manager.HasComponent<DroneActionReadyRequest>(entity))
        {
            return false;
        }

        if (!manager.HasComponent<DroneItemTransferResult>(entity))
        {
            return false;
        }

        var completed = manager.GetComponentData<DroneItemTransferResult>(entity);
        if (completed.Status == DroneItemTransferStatusEnum.None)
        {
            return false;
        }

        result = completed;
        manager.DestroyEntity(entity);
        return true;
    }

    private static Entity GetOrCreateReceiptSequence(EntityManager manager)
    {
        using var query = manager.CreateEntityQuery(ComponentType.ReadWrite<DroneActionReceiptSequence>());
        if (!query.IsEmptyIgnoreFilter)
        {
            return query.GetSingletonEntity();
        }

        Entity entity = manager.CreateEntity(typeof(DroneActionReceiptSequence));
        manager.SetComponentData(entity, new DroneActionReceiptSequence { NextValue = 1 });
        return entity;
    }
}
