using Unity.Entities;

/// <summary>요청 제거와 결과 공개를 확인한 뒤 소비 소유자에게 결과를 반환하고 엔티티를 제거한다.</summary>
public static class RequestResultUtility
{
    public static bool TryConsumeResult<TRequest, TResult>(EntityManager manager, Entity request, out TResult result)
        where TRequest : unmanaged, IComponentData
        where TResult : unmanaged, IComponentData
    {
        result = default;
        if (request == Entity.Null)
        {
            return false;
        }
        if (manager.HasComponent<TRequest>(request))
        {
            return false;
        }
        if (!manager.HasComponent<TResult>(request))
        {
            return false;
        }

        result = manager.GetComponentData<TResult>(request);
        manager.DestroyEntity(request);
        return true;
    }
}
