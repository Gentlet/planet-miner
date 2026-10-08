using System;
using Unity.Entities;

/// <summary>
/// 역할·목적: 플레이어의 배치 묶음을 주 스레드에서 접수하고 World 접수번호를 발급한다.
/// 입력·출력: 사전 확인한 후보와 요청 정책을 같은 엔티티에 준비한다. 최종 승인과 점유 중재는 기존 배치 Command가 담당한다.
/// 수명: 번호 원본은 World 동안 유지하고 요청/후보는 EndCommand에서 소비한다. 공간 맵과 PlacementStamp는 변경하지 않는다.
/// </summary>
public static class BuildingPlacementRequestUtility
{
    public static Entity Submit(
        EntityManager manager,
        in BuildingPlacementRequest request,
        ReadOnlySpan<PlacementRequestCandidateElement> candidates)
    {
        Entity sequenceEntity = GetOrCreateReceiptSequence(manager);
        var sequence = manager.GetComponentData<BuildingPlacementReceiptSequence>(sequenceEntity);
        if (sequence.NextValue == 0 || sequence.NextValue == ulong.MaxValue)
        {
            throw new InvalidOperationException("Building placement receipt sequence is exhausted or invalid.");
        }

        var accepted = request;
        accepted.ReceiptSequence = sequence.NextValue;
        Entity entity = manager.CreateEntity(typeof(BuildingPlacementRequest), typeof(PlacementRequestCandidateElement));
        try
        {
            manager.SetComponentData(entity, accepted);
            var buffer = manager.GetBuffer<PlacementRequestCandidateElement>(entity);
            for (int i = 0; i < candidates.Length; i++)
            {
                buffer.Add(candidates[i]);
            }

            sequence.NextValue++;
            manager.SetComponentData(sequenceEntity, sequence);
            return entity;
        }
        catch
        {
            manager.DestroyEntity(entity);
            throw;
        }
    }

    private static Entity GetOrCreateReceiptSequence(EntityManager manager)
    {
        using var query = manager.CreateEntityQuery(ComponentType.ReadWrite<BuildingPlacementReceiptSequence>());
        if (!query.IsEmptyIgnoreFilter)
        {
            return query.GetSingletonEntity();
        }

        Entity entity = manager.CreateEntity(typeof(BuildingPlacementReceiptSequence));
        manager.SetComponentData(entity, new BuildingPlacementReceiptSequence { NextValue = 1 });
        return entity;
    }
}
