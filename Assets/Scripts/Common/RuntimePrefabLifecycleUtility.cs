using Unity.Collections;
using Unity.Entities;

/// <summary>
/// 프리팹 복제 기록과 런타임 실물의 World 수명 전환을 연결한다.
/// EndCommand/EndBuilding/EndSimulation은 ECB 재생 뒤 연결 그룹 전체의 장면 소유권을 해제하고 반환한다.
/// </summary>
public static class RuntimePrefabLifecycleUtility
{
    internal static EntityQuery CreatePendingSceneDetachQuery(EntityCommandBufferSystem owner)
    {
        using var builder = new EntityQueryBuilder(Allocator.Temp);
        return builder.WithAll<PendingPrefabSceneDetach>()
            .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
            .Build(owner);
    }

    public static Entity Instantiate(ref EntityCommandBuffer ecb, Entity prefab)
    {
        Entity instance = ecb.Instantiate(prefab);
        ecb.AddComponent<PendingPrefabSceneDetach>(instance);
        return instance;
    }

    public static void DetachSceneOwnership(EntityManager manager, EntityQuery pendingInstances)
    {
        if (pendingInstances.IsEmptyIgnoreFilter)
        {
            return;
        }

        using var roots = pendingInstances.ToEntityArray(Allocator.Temp);
        using var entities = new NativeList<Entity>(roots.Length, Allocator.Temp);
        using var visited = new NativeHashSet<Entity>(roots.Length, Allocator.Temp);
        foreach (Entity root in roots)
        {
            AddSurvivingEntity(manager, root, visited, entities);
            if (!manager.HasBuffer<LinkedEntityGroup>(root))
            {
                continue;
            }

            foreach (LinkedEntityGroup member in manager.GetBuffer<LinkedEntityGroup>(root, true))
            {
                AddSurvivingEntity(manager, member.Value, visited, entities);
            }
        }

        // 구조 변경 전에 연결 그룹을 모두 복사한다. 원본 버퍼는 컴포넌트 제거 중 무효화될 수 있다.
        var sceneOwnership = new ComponentTypeSet(typeof(SceneTag), typeof(SceneSection), typeof(PendingPrefabSceneDetach));
        manager.RemoveComponent(entities.AsArray(), sceneOwnership);
    }

    private static void AddSurvivingEntity(EntityManager manager, Entity entity,
        NativeHashSet<Entity> visited, NativeList<Entity> entities)
    {
        // 같은 ECB가 자식을 먼저 삭제했을 수 있으므로 남아 있는 연결 참조만 처리한다.
        if (!manager.Exists(entity))
        {
            return;
        }
        if (!visited.Add(entity))
        {
            return;
        }

        entities.Add(entity);
    }
}
