using Unity.Entities;

/// <summary>종류별 World 순번 상태를 확보한다. 증가/고갈 검사와 기존 값의 유효성은 호출자가 소유한다.</summary>
public static class WorldSequenceUtility
{
    public static Entity GetOrCreate<TSequence>(EntityManager manager, TSequence initialValue)
        where TSequence : unmanaged, IComponentData
    {
        using var query = manager.CreateEntityQuery(ComponentType.ReadWrite<TSequence>());
        if (!query.IsEmptyIgnoreFilter)
        {
            return query.GetSingletonEntity();
        }

        Entity entity = manager.CreateEntity(typeof(TSequence));
        manager.SetComponentData(entity, initialValue);
        return entity;
    }
}
