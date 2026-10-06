using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: Execution에서 승인된 채굴을 진행하고 한 틱에 최대 하나의 생산 결과를 기록한다.
/// 입력·생성자: MinerDecisionSystem의 대상/허용 결정, 채굴 속도와 ResourceConfig의 무한 자원 설정.
/// 출력·소유권: MinerState.Progress, 유한 ResourceNode.Amount와 채굴기의 ProductResult를 쓴다. 초과 진행량은 다음 틱으로 보존한다.
/// 이용·정리: ItemLifecycleApplySystem이 결과를 실물/ProductItemElement로 바꾸고 결과 버퍼를 비운다. 미소비 결과가 있으면 추가 채굴하지 않는다.
/// 가시화: 고갈 자원 삭제만 EndBuilding ECB에 기록하며 공간 인덱스는 이후 Synchronization에서 갱신한다.
/// </summary>
[UpdateInGroup(typeof(BuildingExecutionGroup))]
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
            .WithAllRW<MinerState, ProductResult>()
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

        var ecbSystem = state.World.GetExistingSystemManaged<EndBuildingEntityCommandBufferSystem>();
        var ecb = ecbSystem.CreateCommandBuffer();

        float dt = math.min(SystemAPI.Time.DeltaTime, GameConstants.MaxSimulationDeltaTime);

        var job = new MinerExecutionJob
        {
            DeltaTime = dt,
            IsResourceInfinite = isResourceInfinite,
            ResourceNodeLookup = _resourceNodeLookup,
            ECB = ecb
        };

        // 여러 채굴기가 같은 자원을 선택할 수 있으므로 공유 매장량은 순차 적용하고 각 Execute가 최신 Amount를 재검사한다.
        var jobHandle = job.Schedule(_minerQuery, state.Dependency);
        ecbSystem.AddJobHandleForProducer(jobHandle);
        state.Dependency = jobHandle;
    }
}

/// <summary>
/// 각 채굴기의 진행도 누적 및 ProductResult 기록을 처리하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct MinerExecutionJob : IJobEntity
{
    public float DeltaTime;
    public bool IsResourceInfinite;

    public ComponentLookup<ResourceNode> ResourceNodeLookup;
    public EntityCommandBuffer ECB;

    public void Execute(
        ref MinerState state,
        ref DynamicBuffer<ProductResult> productResults,
        in MinerDecision decision)
    {
        // 1. 유효 채굴 대상 및 미소비 생산 결과 확인
        if (!decision.CanMine || decision.TargetResource == Entity.Null || productResults.Length > 0)
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

        // 3. 채굴 완료(Progress >= 1.0f) 시 같은 프레임 StateApply에서 소비할 생산 결과 기록
        if (state.Progress >= 1.0f)
        {
            state.Progress = math.max(0.0f, state.Progress - 1.0f);

            productResults.Add(new ProductResult(resNode.ResourceType, count: 1, slotIndex: 0));

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
