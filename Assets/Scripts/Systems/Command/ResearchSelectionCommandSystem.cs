using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

/// <summary>
/// 전역 선택 소유자다. 접수 순서의 모든 유효 변경을 단일 Job으로 처리하고 최종 활성 ID/로컬 초기화/개별 결과를 EndCommand에 공개한다.
/// 전역 진척도/완료와 아이템을 변경하지 않는다. 이번 틱의 최종 활성 연구는 이후 Preparation부터 사용한다.
/// </summary>
[UpdateInGroup(typeof(CommandGroup))]
public partial struct ResearchSelectionCommandSystem : ISystem
{
    private EntityQuery _requests;
    private EntityQuery _cycles;

    public void OnCreate(ref SystemState state)
    {
        _requests = SystemAPI.QueryBuilder().WithAll<ResearchSelectionRequest>().Build();
        _cycles = SystemAPI.QueryBuilder().WithAll<ResearchCycleState>()
            .WithOptions(EntityQueryOptions.IncludeDisabledEntities).Build();
        state.RequireForUpdate(_requests);
        state.RequireForUpdate<ResearchRegistry>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var end = state.World.GetOrCreateSystemManaged<EndCommandEntityCommandBufferSystem>();
        var requests = _requests.ToEntityListAsync(Allocator.TempJob, state.Dependency, out var requestsReady);
        var cycles = _cycles.ToEntityListAsync(Allocator.TempJob, state.Dependency, out var cyclesReady);
        var handle = new SelectResearchJob
        {
            Requests = requests.AsDeferredJobArray(),
            Cycles = cycles.AsDeferredJobArray(),
            Registry = SystemAPI.GetSingletonEntity<ResearchRegistry>(),
            RequestLookup = SystemAPI.GetComponentLookup<ResearchSelectionRequest>(true),
            States = SystemAPI.GetComponentLookup<ResearchState>(true),
            ResetResults = SystemAPI.GetComponentLookup<ResearchCycleResetResult>(true),
            Definitions = SystemAPI.GetBufferLookup<ResearchDefinitionElement>(true),
            Prerequisites = SystemAPI.GetBufferLookup<ResearchPrerequisiteElement>(true),
            Progress = SystemAPI.GetBufferLookup<ResearchProgressElement>(true),
            ECB = end.CreateCommandBuffer()
        }.Schedule(JobHandle.CombineDependencies(state.Dependency, requestsReady, cyclesReady));
        state.Dependency = JobHandle.CombineDependencies(requests.Dispose(handle), cycles.Dispose(handle));
        end.AddJobHandleForProducer(state.Dependency);
    }

    private struct RequestRecord
    {
        public Entity Entity;
        public ResearchSelectionRequest Request;
    }

    private struct RequestComparer : IComparer<RequestRecord>
    {
        public int Compare(RequestRecord left, RequestRecord right)
        {
            return left.Request.Sequence.CompareTo(right.Request.Sequence);
        }
    }

    [BurstCompile]
    private struct SelectResearchJob : IJob
    {
        [ReadOnly] public NativeArray<Entity> Requests;
        [ReadOnly] public NativeArray<Entity> Cycles;
        public Entity Registry;
        [ReadOnly] public ComponentLookup<ResearchSelectionRequest> RequestLookup;
        [ReadOnly] public ComponentLookup<ResearchState> States;
        [ReadOnly] public ComponentLookup<ResearchCycleResetResult> ResetResults;
        [ReadOnly] public BufferLookup<ResearchDefinitionElement> Definitions;
        [ReadOnly] public BufferLookup<ResearchPrerequisiteElement> Prerequisites;
        [ReadOnly] public BufferLookup<ResearchProgressElement> Progress;
        public EntityCommandBuffer ECB;

        public void Execute()
        {
            using var ordered = new NativeList<RequestRecord>(Requests.Length, Allocator.Temp);
            for (int i = 0; i < Requests.Length; i++)
            {
                Entity entity = Requests[i];
                ordered.Add(new RequestRecord { Entity = entity, Request = RequestLookup[entity] });
            }
            ordered.AsArray().Sort(new RequestComparer());

            var definitions = Definitions[Registry];
            var prerequisites = Prerequisites[Registry];
            var progress = Progress[Registry];
            using var completedById = new NativeHashMap<FixedString32Bytes, bool>(progress.Length, Allocator.Temp);
            foreach (var entry in progress)
            {
                completedById.Add(entry.ResearchId, entry.IsCompleted);
            }
            var selected = States[Registry];
            bool changed = false;
            foreach (var record in ordered)
            {
                var status = Select(record.Request.ResearchId, definitions, prerequisites, completedById, ref selected);
                changed |= status == ResearchSelectionStatusEnum.Selected;
                ECB.RemoveComponent<ResearchSelectionRequest>(record.Entity);
                ECB.AddComponent(record.Entity, new ResearchSelectionResult
                {
                    RequestedResearchId = record.Request.ResearchId,
                    Status = status
                });
            }

            if (!changed)
            {
                return;
            }
            ECB.SetComponent(Registry, selected);
            // 표시 결과가 없는 유효한 사전 등록 Registry도 기존 연구 진행을 유지한다.
            if (ResetResults.TryGetComponent(Registry, out var notice))
            {
                notice.Revision++;
                notice.Reason = ResearchCycleResetReasonEnum.SelectionChanged;
                ECB.SetComponent(Registry, notice);
            }
            // 요청 사이에는 연구가 진행하지 않는다. A→B→A도 변경 이력을 보존해 건물별 주기를 한 번 초기화한다.
            foreach (Entity building in Cycles)
            {
                ECB.SetComponent(building, default(ResearchCycleState));
            }
        }

        private static ResearchSelectionStatusEnum Select(FixedString32Bytes researchId,
            DynamicBuffer<ResearchDefinitionElement> definitions, DynamicBuffer<ResearchPrerequisiteElement> prerequisites,
            NativeHashMap<FixedString32Bytes, bool> completedById, ref ResearchState selected)
        {
            for (int i = 0; i < definitions.Length; i++)
            {
                var definition = definitions[i];
                if (definition.StableId != researchId)
                {
                    continue;
                }
                // 초기화가 ID 전체 대응/선행 그래프를 검증한다. 런타임은 해당 연구의 최신 완료 자격만 읽는다.
                if (completedById[researchId])
                {
                    return ResearchSelectionStatusEnum.RejectedCompleted;
                }
                for (int j = 0; j < definition.PrerequisiteCount; j++)
                {
                    var prerequisite = prerequisites[definition.PrerequisiteStart + j].ResearchId;
                    if (!completedById[prerequisite])
                    {
                        return ResearchSelectionStatusEnum.RejectedPrerequisites;
                    }
                }
                if (selected.ActiveResearchId == researchId)
                {
                    return ResearchSelectionStatusEnum.AlreadySelected;
                }

                selected.ActiveResearchId = researchId;
                return ResearchSelectionStatusEnum.Selected;
            }
            return ResearchSelectionStatusEnum.RejectedUnknownResearch;
        }
    }
}
