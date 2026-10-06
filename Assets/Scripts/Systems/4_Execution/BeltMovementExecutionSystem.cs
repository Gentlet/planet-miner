using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// 역할·목적: Execution에서 Decision의 전진 계획을 활성 월드 실물의 실제 이동 상태에 반영한다.
/// 입력·생성자: BeltMovementDecisionSystem의 PlannedProgress와 Synchronization의 벨트 인덱스.
/// 출력·소유권: BeltMovementState.Progress, GridPosition과 LocalTransform을 함께 갱신한다. 소유권/렌더 태그는 쓰지 않는다.
/// 정리: PlannedProgress는 적용 전에 0으로 소비하여 같은 계획을 반복 실행하지 않는다. 다음 셀에 벨트가 없으면 종단에서 멈춘다.
/// 가시화: 값은 Job 완료 후 Reader에 보이며 아이템 인덱스는 Synchronization에서 재구축한다. ECB 구조 변경은 없다.
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
