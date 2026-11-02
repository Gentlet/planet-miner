using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

[UpdateInGroup(typeof(CommandGroup))]
public partial struct StorageFilterCommandSystem : ISystem
{
    private EntityQuery _requests;

    public void OnCreate(ref SystemState state)
    {
        _requests = SystemAPI.QueryBuilder().WithAll<ChangeStorageFilterRequest>().Build();
        state.RequireForUpdate(_requests);
        state.RequireForUpdate<ItemRegistry>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var end = state.World.GetOrCreateSystemManaged<EndCommandEntityCommandBufferSystem>();
        var entities = _requests.ToEntityListAsync(Allocator.TempJob, state.Dependency, out var ready);
        var handle = new ApplyStorageFiltersJob
        {
            Requests = entities.AsDeferredJobArray(),
            RequestLookup = SystemAPI.GetComponentLookup<ChangeStorageFilterRequest>(true),
            Types = SystemAPI.GetComponentLookup<BuildingType>(true),
            Filters = SystemAPI.GetComponentLookup<StorageFilter>(),
            Disabled = SystemAPI.GetComponentLookup<Disabled>(true),
            Prefabs = SystemAPI.GetComponentLookup<Prefab>(true),
            Demolition = SystemAPI.GetComponentLookup<PendingBuildingDemolition>(true),
            RegisteredItems = BuildingSettingsUtility.RegisteredItems(SystemAPI.GetSingletonBuffer<ItemConfigElement>(true)),
            ECB = end.CreateCommandBuffer()
        }.Schedule(JobHandle.CombineDependencies(state.Dependency, ready));
        state.Dependency = entities.Dispose(handle);
        end.AddJobHandleForProducer(state.Dependency);
    }
}

[BurstCompile]
internal struct ApplyStorageFiltersJob : Unity.Jobs.IJob
{
    [ReadOnly] public NativeArray<Entity> Requests;
    [ReadOnly] public ComponentLookup<ChangeStorageFilterRequest> RequestLookup;
    [ReadOnly] public ComponentLookup<BuildingType> Types;
    public ComponentLookup<StorageFilter> Filters;
    [ReadOnly] public ComponentLookup<Disabled> Disabled;
    [ReadOnly] public ComponentLookup<Prefab> Prefabs;
    [ReadOnly] public ComponentLookup<PendingBuildingDemolition> Demolition;
    public FixedBitSet RegisteredItems;
    public EntityCommandBuffer ECB;

    private struct Entry : System.IComparable<Entry>
    {
        public Entity Entity;
        public ulong Sequence;
        public int CompareTo(Entry other) => Sequence.CompareTo(other.Sequence);
    }

    public void Execute()
    {
        var ordered = new NativeList<Entry>(Requests.Length, Allocator.Temp);
        foreach (Entity entity in Requests)
        {
            ordered.Add(new Entry { Entity = entity, Sequence = RequestLookup[entity].Sequence });
        }
        ordered.AsArray().Sort();
        for (int i = 0; i < ordered.Length; i++)
        {
            var entry = ordered[i];
            var request = RequestLookup[entry.Entity];
            ECB.DestroyEntity(entry.Entity);
            bool duplicate = (i > 0 && ordered[i - 1].Sequence == entry.Sequence) ||
                (i + 1 < ordered.Length && ordered[i + 1].Sequence == entry.Sequence);
            if (entry.Sequence == 0 || duplicate)
            {
                continue;
            }
            if (!Types.HasComponent(request.Target) || !Filters.HasComponent(request.Target))
            {
                continue;
            }
            if (Types[request.Target].Type != BuildingTypeEnum.Storage || Disabled.HasComponent(request.Target) ||
                Prefabs.HasComponent(request.Target) || Demolition.HasComponent(request.Target))
            {
                continue;
            }
            if (BuildingSettingsUtility.IsValidFilter(request.Filter, RegisteredItems))
            {
                Filters[request.Target] = request.Filter;
            }
        }
        ordered.Dispose();
    }
}
