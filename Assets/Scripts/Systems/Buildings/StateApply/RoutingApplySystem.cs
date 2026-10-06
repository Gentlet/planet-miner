using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// 역할·목적: StateApply에서 Reservation을 통과한 분배/합류 전달을 실물 위치와 영속 라우팅 상태에 반영한다.
/// 입력·생성자: Splitter/Merger Decision의 RoutingTransferDecision 중 BeltDestinationReservationSystem이 유지한 활성 후보.
/// 출력·소유권: 실물 GridPosition/Direction/벨트 진행도/LocalTransform과 라우터 기준선·커서를 즉시 갱신한다. Owner/생산 버퍼는 쓰지 않는다.
/// 이용: 최신 위치는 이후 철거/Ownership·드론 인계 검사가 읽고 다음 Decision은 성공한 포트 이후의 커서를 사용한다.
/// 정리·가시화: 적용 여부와 관계없이 전달 결정을 비활성화한다. ECB 기록은 없고 공간 인덱스는 Synchronization에서 갱신한다.
/// </summary>
[UpdateInGroup(typeof(BuildingStateApplyGroup))]
[UpdateBefore(typeof(ItemOwnershipApplySystem))]
[BurstCompile]
public partial struct RoutingApplySystem : ISystem
{
    private ComponentLookup<GridPosition> _gridPositionLookup;
    private ComponentLookup<Direction> _directionLookup;
    private ComponentLookup<BeltMovementState> _beltMovementStateLookup;
    private ComponentLookup<LocalTransform> _transformLookup;
    private ComponentLookup<SplitterRoutingState> _splitterRoutingStateLookup;
    private ComponentLookup<MergerRoutingState> _mergerRoutingStateLookup;

    private EntityQuery _activeTransferQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _gridPositionLookup = state.GetComponentLookup<GridPosition>(false);
        _directionLookup = state.GetComponentLookup<Direction>(false);
        _beltMovementStateLookup = state.GetComponentLookup<BeltMovementState>(false);
        _transformLookup = state.GetComponentLookup<LocalTransform>(false);
        _splitterRoutingStateLookup = state.GetComponentLookup<SplitterRoutingState>(false);
        _mergerRoutingStateLookup = state.GetComponentLookup<MergerRoutingState>(false);

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
        _mergerRoutingStateLookup.Update(ref state);

        var applyJob = new RoutingApplyJob
        {
            GridPositionLookup = _gridPositionLookup,
            DirectionLookup = _directionLookup,
            BeltMovementStateLookup = _beltMovementStateLookup,
            TransformLookup = _transformLookup,
            SplitterRoutingStateLookup = _splitterRoutingStateLookup,
            MergerRoutingStateLookup = _mergerRoutingStateLookup
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
    public ComponentLookup<MergerRoutingState> MergerRoutingStateLookup;

    public void Execute(
        Entity routerEntity,
        ref RoutingTransferDecision decision,
        EnabledRefRW<RoutingTransferDecision> decisionEnabled)
    {
        Entity item = decision.Item;
        Entity targetBelt = decision.TargetBelt;

        if (item != Entity.Null && targetBelt != Entity.Null &&
            GridPositionLookup.HasComponent(item) &&
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

            // 실물 전달 조건을 통과했을 때만 기준선과 커서를 전진한다. 후보 생성/경합 탈락만으로 포트 순서를 소비하지 않는다.
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

            // 5. Merger 영속 라우팅 상태 갱신
            if (MergerRoutingStateLookup.HasComponent(routerEntity) &&
                GridPositionLookup.HasComponent(routerEntity))
            {
                ref var mergerState = ref MergerRoutingStateLookup.GetRefRW(routerEntity).ValueRW;
                int2 routerPos = GridPositionLookup[routerEntity].Value;

                // 기준 출력 벨트 및 전방 방향 동기화
                if (decision.TargetBelt != Entity.Null)
                {
                    mergerState.OutputBelt = decision.TargetBelt;
                    if (DirectionLookup.HasComponent(decision.TargetBelt))
                    {
                        mergerState.ForwardDirection = DirectionLookup[decision.TargetBelt].dir;
                    }
                }

                // 유입된 포트 인덱스 역산 및 커서 전진
                if (decision.SourceBelt != Entity.Null && GridPositionLookup.HasComponent(decision.SourceBelt))
                {
                    int2 sourceBeltPos = GridPositionLookup[decision.SourceBelt].Value;
                    if (RoutingDirectionUtility.TryGetDirection(routerPos, sourceBeltPos, out DirectionEnum inRelativeDir))
                    {
                        byte portIndex = RoutingDirectionUtility.GetMergerPortIndex(mergerState.ForwardDirection, inRelativeDir);
                        mergerState.InputCursor = RoutingDirectionUtility.AdvanceCursor(portIndex);
                    }
                }
            }
        }

        // 6. 프레임 의사결정 소비 및 비활성화
        decision.Item = Entity.Null;
        decision.SourceBelt = Entity.Null;
        decision.TargetBelt = Entity.Null;
        decisionEnabled.ValueRW = false;
    }
}
