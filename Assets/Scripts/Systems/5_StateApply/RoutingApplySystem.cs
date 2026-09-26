using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// 승인된 라우팅 전달 결정(RoutingTransferDecision)을 실제 아이템 이동 및 영속 상태에 반영하는 시스템.
/// 
/// [책임]
/// - StateApplyGroup (Phase 5)에서 실행.
/// - ReservationGroup을 통과하여 활성 상태를 유지한 RoutingTransferDecision을 일괄 소비.
/// - 대상 아이템의 위치(GridPosition), 방향(Direction), 이동 진행도(BeltMovementState 0.0f), LocalTransform을 대상 벨트 시작점으로 갱신.
/// - Splitter/Merger의 영속 라우팅 상태(OutputCursor / InputCursor 등)를 실제 배출 포트 기준으로 갱신.
/// - RoutingTransferDecision을 비활성화하여 프레임 결정을 완전 소비.
/// </summary>
[UpdateInGroup(typeof(StateApplyGroup))]
[UpdateBefore(typeof(ItemOwnershipApplySystem))]
[BurstCompile]
public partial struct RoutingApplySystem : ISystem
{
    private ComponentLookup<GridPosition> _gridPositionLookup;
    private ComponentLookup<Direction> _directionLookup;
    private ComponentLookup<BeltMovementState> _beltMovementStateLookup;
    private ComponentLookup<LocalTransform> _transformLookup;
    private ComponentLookup<SplitterRoutingState> _splitterRoutingStateLookup;

    private EntityQuery _activeTransferQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _gridPositionLookup = state.GetComponentLookup<GridPosition>(false);
        _directionLookup = state.GetComponentLookup<Direction>(false);
        _beltMovementStateLookup = state.GetComponentLookup<BeltMovementState>(false);
        _transformLookup = state.GetComponentLookup<LocalTransform>(false);
        _splitterRoutingStateLookup = state.GetComponentLookup<SplitterRoutingState>(false);

        _activeTransferQuery = SystemAPI.QueryBuilder()
            .WithAllRW<RoutingTransferDecision>()
            .Build();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (_activeTransferQuery.IsEmptyIgnoreFilter)
        {
            return;
        }

        _gridPositionLookup.Update(ref state);
        _directionLookup.Update(ref state);
        _beltMovementStateLookup.Update(ref state);
        _transformLookup.Update(ref state);
        _splitterRoutingStateLookup.Update(ref state);

        var applyJob = new RoutingApplyJob
        {
            GridPositionLookup = _gridPositionLookup,
            DirectionLookup = _directionLookup,
            BeltMovementStateLookup = _beltMovementStateLookup,
            TransformLookup = _transformLookup,
            SplitterRoutingStateLookup = _splitterRoutingStateLookup
        };

        state.Dependency = applyJob.Schedule(_activeTransferQuery, state.Dependency);
    }
}

/// <summary>
/// 승인된 라우팅 전달 결정을 적용하는 단일 스레드 Burst Job.
/// </summary>
[BurstCompile]
public partial struct RoutingApplyJob : IJobEntity
{
    public ComponentLookup<GridPosition> GridPositionLookup;
    public ComponentLookup<Direction> DirectionLookup;
    public ComponentLookup<BeltMovementState> BeltMovementStateLookup;
    public ComponentLookup<LocalTransform> TransformLookup;
    public ComponentLookup<SplitterRoutingState> SplitterRoutingStateLookup;

    public void Execute(
        Entity routerEntity,
        ref RoutingTransferDecision decision,
        EnabledRefRW<RoutingTransferDecision> decisionEnabled)
    {
        Entity item = decision.Item;
        Entity targetBelt = decision.TargetBelt;

        if (item != Entity.Null && targetBelt != Entity.Null &&
            GridPositionLookup.HasComponent(targetBelt) &&
            DirectionLookup.HasComponent(targetBelt))
        {
            int2 targetBeltPos = GridPositionLookup[targetBelt].Value;
            DirectionEnum targetBeltDir = DirectionLookup[targetBelt].dir;

            // 1. 아이템 그리드 좌표 및 방향 갱신
            if (GridPositionLookup.HasComponent(item))
            {
                GridPositionLookup[item] = new GridPosition(targetBeltPos);
            }

            if (DirectionLookup.HasComponent(item))
            {
                DirectionLookup[item] = new Direction(targetBeltDir);
            }

            // 2. 벨트 진행도 0.0f로 재설정
            if (BeltMovementStateLookup.HasComponent(item))
            {
                BeltMovementStateLookup[item] = new BeltMovementState(0.0f);
                BeltMovementStateLookup.SetComponentEnabled(item, true);
            }

            // 3. LocalTransform을 대상 벨트 진입점 위치로 갱신
            if (TransformLookup.HasComponent(item))
            {
                float2 center = new float2(targetBeltPos.x, targetBeltPos.y);
                float2 dirFloat = new float2(targetBeltDir.ToInt2().x, targetBeltDir.ToInt2().y);
                float2 visualPos = center + dirFloat * (0.0f - 0.5f);
                TransformLookup[item] = LocalTransform.FromPosition(new float3(visualPos.x, visualPos.y, 0f));
            }

            // 4. Splitter 영속 라우팅 상태 갱신
            if (SplitterRoutingStateLookup.HasComponent(routerEntity) &&
                GridPositionLookup.HasComponent(routerEntity))
            {
                ref var splitterState = ref SplitterRoutingStateLookup.GetRefRW(routerEntity).ValueRW;
                int2 routerPos = GridPositionLookup[routerEntity].Value;

                // 기준 입력 벨트 및 전방 방향 동기화
                if (decision.SourceBelt != Entity.Null)
                {
                    splitterState.InputBelt = decision.SourceBelt;
                    if (DirectionLookup.HasComponent(decision.SourceBelt))
                    {
                        splitterState.ForwardDirection = DirectionLookup[decision.SourceBelt].dir;
                    }
                }

                // 배출된 포트 인덱스 역산 및 커서 전진
                if (RoutingDirectionUtility.TryGetDirection(routerPos, targetBeltPos, out DirectionEnum outDir))
                {
                    byte portIndex = RoutingDirectionUtility.GetSplitterPortIndex(splitterState.ForwardDirection, outDir);
                    splitterState.OutputCursor = RoutingDirectionUtility.AdvanceCursor(portIndex);
                }
            }
        }

        // 5. 프레임 의사결정 소비 및 비활성화
        decision.Item = Entity.Null;
        decision.SourceBelt = Entity.Null;
        decision.TargetBelt = Entity.Null;
        decisionEnabled.ValueRW = false;
    }
}
