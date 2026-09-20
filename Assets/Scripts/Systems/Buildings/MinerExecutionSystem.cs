using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 채굴기의 진행도를 누적하고 채굴 완료 시 채굴기 내부 버퍼 적재를 위한 SpawnItemRequest를 발행하는 시스템.
/// 
/// [책임]
/// - ExecutionGroup(Phase 4)에서 실행됩니다.
/// - MinerDecision이 활성화된 채굴기를 대상으로 채굴 진행도(Progress)를 시간(DeltaTime * MiningSpeed)에 따라 누적합니다.
/// - [내부 버퍼 모델]:
///   - 진행도가 1.0f에 도달하면, Progress를 1.0f 차감(초과분 보존)하고 채굴기 자신을 TargetOwner로 하는 SpawnItemRequest를 발행합니다.
///   - 발행된 요청은 Phase 5 EndStateApplyEntityCommandBufferSystem 및 ItemLifecycleApplySystem을 통해 채굴기의 DynamicBuffer<StoredItemElement>에 적재됩니다.
///   - 버퍼에 들어간 아이템은 이후 Phase 2/5의 기존 출고 시스템(BuildingItemOutputDecisionSystem / BuildingItemStorageApplySystem)에 의해 외부 벨트로 방출됩니다.
///   - ResourceConfig의 IsResourceInfinite가 false인 경우 ResourceNode의 Amount를 1 차감하며, 고갈 시 자원 엔티티를 파괴합니다.
/// - 별도의 Phase 4 전용 ECB 없이, 기존 EndStateApplyEntityCommandBufferSystem을 활용합니다.
/// </summary>
[UpdateInGroup(typeof(ExecutionGroup))]
[BurstCompile]
public partial struct MinerExecutionSystem : ISystem
{
    private ComponentLookup<ResourceNode> _resourceNodeLookup;
    private EntityQuery _minerQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _resourceNodeLookup = state.GetComponentLookup<ResourceNode>(false);

        _minerQuery = SystemAPI.QueryBuilder()
            .WithAllRW<MinerState>()
            .WithAll<MinerDecision>()
            .Build();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        _resourceNodeLookup.Update(ref state);

        bool isResourceInfinite = false;
        if (SystemAPI.HasSingleton<ResourceConfig>())
        {
            isResourceInfinite = SystemAPI.GetSingleton<ResourceConfig>().IsResourceInfinite;
        }

        var ecbSystem = state.World.GetExistingSystemManaged<EndStateApplyEntityCommandBufferSystem>();
        var ecb = ecbSystem.CreateCommandBuffer();

        var job = new MinerExecutionJob
        {
            DeltaTime = SystemAPI.Time.DeltaTime,
            IsResourceInfinite = isResourceInfinite,
            ResourceNodeLookup = _resourceNodeLookup,
            ECB = ecb
        };

        var jobHandle = job.Schedule(_minerQuery, state.Dependency);
        ecbSystem.AddJobHandleForProducer(jobHandle);
        state.Dependency = jobHandle;
    }
}

/// <summary>
/// 각 채굴기의 진행도 누적 및 내부 버퍼 스폰 요청을 처리하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct MinerExecutionJob : IJobEntity
{
    public float DeltaTime;
    public bool IsResourceInfinite;

    public ComponentLookup<ResourceNode> ResourceNodeLookup;
    public EntityCommandBuffer ECB;

    public void Execute(
        Entity minerEntity,
        ref MinerState state,
        in MinerDecision decision)
    {
        // 1. 유효 채굴 대상 확인
        if (!decision.CanMine || decision.TargetResource == Entity.Null)
        {
            return;
        }

        if (!ResourceNodeLookup.HasComponent(decision.TargetResource))
        {
            return;
        }

        ResourceNode resNode = ResourceNodeLookup[decision.TargetResource];
        if (resNode.Amount <= 0)
        {
            return;
        }

        // 2. 채굴 진행도 누적
        state.Progress += state.MiningSpeed * DeltaTime;

        // 3. 채굴 완료(Progress >= 1.0f) 시 내부 버퍼 적재 요청 발행
        if (state.Progress >= 1.0f)
        {
            state.Progress = math.max(0.0f, state.Progress - 1.0f);

            // 채굴기 자신을 TargetOwner로 하는 스폰 요청 발행
            Entity req = ECB.CreateEntity();
            ECB.AddComponent(req, new SpawnItemRequest(resNode.ResourceType, minerEntity));

            // 자원 매장량 차감 및 고갈 검사
            if (!IsResourceInfinite)
            {
                resNode.Amount--;
                ResourceNodeLookup[decision.TargetResource] = resNode;

                if (resNode.Amount <= 0)
                {
                    ECB.DestroyEntity(decision.TargetResource);
                }
            }
        }
    }
}
