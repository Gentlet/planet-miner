using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// BeltMovementDecisionSystem에서 결정된 PlannedProgress를 소비하여
/// 아이템의 진행 상태(BeltMovementState), 그리드 좌표(GridPosition),
/// 실제 렌더링 위치(LocalTransform)를 전진 및 갱신하는 실행 시스템.
/// 
/// [책임]
/// - ExecutionGroup(Phase 4)에서 실행됩니다.
/// - DecisionGroup에서 산출된 BeltMovementDecision.PlannedProgress를 실제 Progress에 가산합니다.
/// - 타일 경계 횡단(newProgress >= 1.0f) 시 GridPosition을 전진시키고 Progress를 새 타일 기준으로 보정합니다.
/// - 그리드 중심과 방향, 진행률을 결합하여 LocalTransform.Position을 정확한 2D 월드 좌표로 갱신합니다.
/// - Consume-on-Execution 원칙에 따라 처리가 끝난 PlannedProgress를 0.0f로 소비(초기화)합니다.
/// - BeltSpatialIndexFence에 Reader JobHandle을 등록하여 SynchronizationGroup과의 데이터 경합을 비차단 방식으로 제어합니다.
/// - 별도의 룩업(ComponentLookup) 없이 순수 쿼리(ref/in)만으로 동작하여 컨테이너 Aliasing이 없는 100% Safe Parallel Job을 보장합니다.
/// </summary>
[UpdateInGroup(typeof(ExecutionGroup))]
[BurstCompile]
public partial struct BeltMovementExecutionSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.HasSingleton<BeltSpatialIndex>() || !SystemAPI.HasSingleton<BeltSpatialIndexFence>())
        {
            return;
        }

        var beltIndex = SystemAPI.GetSingleton<BeltSpatialIndex>();
        ref var beltFence = ref SystemAPI.GetSingletonRW<BeltSpatialIndexFence>().ValueRW;

        var job = new BeltMovementExecutionJob
        {
            BeltMap = beltIndex.Map
        };

        // Reader 의존성: 마지막 Writer가 끝난 뒤 읽기 실행
        var jobDep = JobHandle.CombineDependencies(state.Dependency, beltFence.GetReaderDependency());
        var jobHandle = job.ScheduleParallel(jobDep);

        // Fence에 Reader Handle 등록
        beltFence.AddReader(jobHandle);

        state.Dependency = jobHandle;
    }
}

/// <summary>
/// 벨트 위 활성화된 각 아이템의 위치 및 진행 상태를 병렬로 갱신하고 의사결정 값을 소비하는 Safe Burst Job.
/// </summary>
[BurstCompile]
public partial struct BeltMovementExecutionJob : IJobEntity
{
    [ReadOnly]
    public NativeParallelHashMap<int2, BeltInfo> BeltMap;

    public void Execute(
        Entity entity,
        ref BeltMovementState state,
        ref GridPosition gridPos,
        ref LocalTransform transform,
        ref BeltMovementDecision decision,
        in ItemOwnership ownership)
    {
        // 1. 월드 아이템이 아니면 벨트 실행 대상이 아님
        if (!ownership.IsWorldItem)
        {
            decision.PlannedProgress = 0.0f;
            return;
        }

        // 2. 현재 타일에 벨트가 존재하는지 확인
        if (!BeltMap.TryGetValue(gridPos.Value, out BeltInfo currentBelt))
        {
            decision.PlannedProgress = 0.0f;
            return;
        }

        float planned = decision.PlannedProgress;
        // Consume-on-Execution: 이번 프레임의 이동 계획 소비
        decision.PlannedProgress = 0.0f;

        int2 currentDir = currentBelt.Direction.ToInt2();

        // 3. 이동 계획이 있는 경우 Progress 전진 및 타일 횡단 처리 (큰 DeltaTime 대응 다중 홉 방어 루프)
        if (planned > 0.0f)
        {
            float newProgress = state.Progress + planned;
            int hopCount = 0;

            while (newProgress >= 1.0f && hopCount < GameConstants.MaxTileHopsPerFrame)
            {
                int2 nextPos = gridPos.Value + currentDir;
                if (BeltMap.TryGetValue(nextPos, out BeltInfo nextBelt))
                {
                    // 다음 타일에 벨트가 있으므로 다음 타일로 인계 및 잔여 진행도 이월
                    gridPos.Value = nextPos;
                    newProgress -= 1.0f;
                    currentBelt = nextBelt;
                    currentDir = nextBelt.Direction.ToInt2();
                    hopCount++;
                }
                else
                {
                    // 다음 타일에 벨트가 없는 종단: 현재 타일 출구(1.0f)에 정지
                    newProgress = 1.0f;
                    break;
                }
            }

            // 최종 진행도 확정 (최대 홉 수 도달 시 또는 종단 처리 시 Invariant 위반 방지를 위해 1.0f 상한 클램핑)
            state.Progress = math.min(newProgress, 1.0f);
        }

        // 4. LocalTransform.Position 갱신
        // 타일 중심 = (gridPos.x, gridPos.y)
        // Progress == 0.0f -> 입구 (center - dir * 0.5f)
        // Progress == 0.5f -> 타일 중심 (center)
        // Progress == 1.0f -> 출구 (center + dir * 0.5f)
        float2 center = new float2(gridPos.Value.x, gridPos.Value.y);
        float2 dirFloat = new float2(currentDir.x, currentDir.y);
        float2 visualPos = center + dirFloat * (state.Progress - 0.5f);

        transform.Position = new float3(visualPos.x, visualPos.y, 0f);
    }
}
