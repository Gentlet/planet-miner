using System;
using Unity.Collections;
using Unity.Entities;

public static class ResearchSelectionRequestUtility
{
    /// <summary>시뮬레이션 실행 전 접수한다. ID의 선택 자격은 Command가 판단하며 빈 ID도 선택 해제로 해석하지 않는다.</summary>
    public static Entity Submit(EntityManager manager, FixedString32Bytes researchId)
    {
        using var query = manager.CreateEntityQuery(typeof(ResearchSelectionRequestSequence));
        bool createSequence = query.IsEmptyIgnoreFilter;
        ulong next = createSequence ? 1 : query.GetSingleton<ResearchSelectionRequestSequence>().NextValue;
        if (next == 0 || next == ulong.MaxValue)
        {
            throw new InvalidOperationException("Research selection request sequence must be positive and not exhausted.");
        }

        Entity sequence = createSequence ? manager.CreateEntity(typeof(ResearchSelectionRequestSequence)) : query.GetSingletonEntity();
        manager.SetComponentData(sequence, new ResearchSelectionRequestSequence { NextValue = next + 1 });
        Entity request = manager.CreateEntity(typeof(ResearchSelectionRequest));
        manager.SetComponentData(request, new ResearchSelectionRequest { ResearchId = researchId, Sequence = next });
        return request;
    }

    /// <summary>EndCommand가 요청을 소비하고 결과를 공개한 뒤 한 번 반환하며 요청/결과 엔티티를 함께 제거한다.</summary>
    public static bool TryConsumeResult(EntityManager manager, Entity request, out ResearchSelectionResult result)
    {
        result = default;
        if (request == Entity.Null)
        {
            return false;
        }
        if (manager.HasComponent<ResearchSelectionRequest>(request))
        {
            return false;
        }
        if (!manager.HasComponent<ResearchSelectionResult>(request))
        {
            return false;
        }

        result = manager.GetComponentData<ResearchSelectionResult>(request);
        manager.DestroyEntity(request);
        return true;
    }
}
