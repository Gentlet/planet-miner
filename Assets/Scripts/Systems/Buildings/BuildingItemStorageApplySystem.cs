using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// 창고 아이템의 입고 및 출고 상태 전이를 일괄 적용하는 시스템.
/// 
/// [책임]
/// - StateApplyGroup(Phase 5)에서 ItemOwnershipApplySystem 직전에 실행됩니다.
/// - 단일 워커 스레드 Burst Job(BuildingItemInputApplyJob, BuildingItemOutputApplyJob)을 순차적으로 스케줄링하여
///   메인 스레드 부하를 0으로 유지하면서, 버퍼 조작과 월드 컴포넌트 상태 전이를 즉각적이고 안전하게 적용합니다.
/// - [입고]: 슬롯 예약이 완료된 아이템(CanDeposit == true && TargetSlotIndex >= 0)을 창고 버퍼(DynamicBuffer<StoredItemElement>)에 적재하고,
///           BeltMovementState를 비활성화한 뒤 TransferOwnershipRequest(TargetOwner = 창고)를 발행합니다.
/// - [출고]: 출고가 확정된 건물(CanOutput == true)의 버퍼에서 대상 아이템(ItemToOutput)을 제거하고,
///           GridPosition, Direction, BeltMovementState(Progress = 0.0f), LocalTransform을 벨트 시작점으로 복원한 뒤
///           TransferOwnershipRequest(TargetOwner = Entity.Null)를 발행하여 월드 아이템으로 전환합니다.
/// - [엄격한 단일 책임 분리]: 이 시스템은 창고 버퍼와 월드 상태 전이만 처리하며,
///   최종 소유권(ItemOwnership) 갱신은 뒤이어 실행되는 ItemOwnershipApplySystem에 위임합니다.
/// </summary>
[UpdateInGroup(typeof(StateApplyGroup))]
[UpdateBefore(typeof(ItemOwnershipApplySystem))]
[BurstCompile]
public partial struct BuildingItemStorageApplySystem : ISystem
{
    private BufferLookup<StoredItemElement> _storedBufferLookup;
    private ComponentLookup<BeltMovementState> _beltMovementStateLookup;
    private ComponentLookup<BeltMovementDecision> _beltMovementDecisionLookup;
    private ComponentLookup<TransferOwnershipRequest> _transferOwnershipRequestLookup;
    private ComponentLookup<GridPosition> _gridPositionLookup;
    private ComponentLookup<Direction> _directionLookup;
    private ComponentLookup<LocalTransform> _transformLookup;

    private EntityQuery _inputQuery;
    private EntityQuery _outputQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _storedBufferLookup = state.GetBufferLookup<StoredItemElement>(false);
        _beltMovementStateLookup = state.GetComponentLookup<BeltMovementState>(false);
        _beltMovementDecisionLookup = state.GetComponentLookup<BeltMovementDecision>(false);
        _transferOwnershipRequestLookup = state.GetComponentLookup<TransferOwnershipRequest>(false);
        _gridPositionLookup = state.GetComponentLookup<GridPosition>(false);
        _directionLookup = state.GetComponentLookup<Direction>(false);
        _transformLookup = state.GetComponentLookup<LocalTransform>(false);

        _inputQuery = SystemAPI.QueryBuilder()
            .WithAllRW<BuildingItemInputDecision>()
            .WithAll<ItemIdentity>()
            .Build();

        _outputQuery = SystemAPI.QueryBuilder()
            .WithAllRW<BuildingItemOutputDecision>()
            .Build();
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

        _storedBufferLookup.Update(ref state);
        _beltMovementStateLookup.Update(ref state);
        _beltMovementDecisionLookup.Update(ref state);
        _transferOwnershipRequestLookup.Update(ref state);
        _gridPositionLookup.Update(ref state);
        _directionLookup.Update(ref state);
        _transformLookup.Update(ref state);

        // 1. [입고 Job 스케줄링]
        var inputJob = new BuildingItemInputApplyJob
        {
            StoredBufferLookup = _storedBufferLookup,
            BeltMovementStateLookup = _beltMovementStateLookup,
            TransferOwnershipRequestLookup = _transferOwnershipRequestLookup
        };
        var inputHandle = inputJob.Schedule(_inputQuery, state.Dependency);

        // 2. [출고 Job 스케줄링]
        var outputJob = new BuildingItemOutputApplyJob
        {
            BeltMap = beltIndex.Map,
            StoredBufferLookup = _storedBufferLookup,
            GridPositionLookup = _gridPositionLookup,
            DirectionLookup = _directionLookup,
            BeltMovementStateLookup = _beltMovementStateLookup,
            BeltMovementDecisionLookup = _beltMovementDecisionLookup,
            TransformLookup = _transformLookup,
            TransferOwnershipRequestLookup = _transferOwnershipRequestLookup
        };

        var outputDep = JobHandle.CombineDependencies(inputHandle, beltFence.GetReaderDependency());
        var outputHandle = outputJob.Schedule(_outputQuery, outputDep);

        beltFence.AddReader(outputHandle);

        state.Dependency = outputHandle;
    }
}

/// <summary>
/// 슬롯 예약이 완료된 아이템을 창고 버퍼에 수납하고 벨트 상태를 비활성화하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct BuildingItemInputApplyJob : IJobEntity
{
    public BufferLookup<StoredItemElement> StoredBufferLookup;
    public ComponentLookup<BeltMovementState> BeltMovementStateLookup;
    public ComponentLookup<TransferOwnershipRequest> TransferOwnershipRequestLookup;

    public void Execute(
        Entity entity,
        ref BuildingItemInputDecision inputDecision,
        EnabledRefRW<BuildingItemInputDecision> inputDecisionEnabled,
        in ItemIdentity itemIdentity)
    {
        if (!inputDecision.CanDeposit || inputDecision.TargetSlotIndex < 0)
        {
            return;
        }

        Entity building = inputDecision.TargetBuilding;
        if (!StoredBufferLookup.HasBuffer(building))
        {
            inputDecision.CanDeposit = false;
            inputDecision.TargetSlotIndex = -1;
            inputDecisionEnabled.ValueRW = false;
            return;
        }

        var buffer = StoredBufferLookup[building];
        buffer.Add(new StoredItemElement(entity, itemIdentity.Type, inputDecision.TargetSlotIndex));

        // 벨트 이동 상태 비활성화 (보관 상태로 진입)
        if (BeltMovementStateLookup.HasComponent(entity))
        {
            BeltMovementStateLookup.SetComponentEnabled(entity, false);
        }

        // 입고 의사결정 컴포넌트 비활성화 (소비 완료)
        inputDecisionEnabled.ValueRW = false;

        // 소유권 이전 요청 발행 (ItemOwnershipApplySystem에서 Stored(building)으로 최종 반영)
        if (TransferOwnershipRequestLookup.HasComponent(entity))
        {
            TransferOwnershipRequestLookup[entity] = new TransferOwnershipRequest(building);
            TransferOwnershipRequestLookup.SetComponentEnabled(entity, true);
        }
    }
}

/// <summary>
/// 출고가 확정된 창고의 버퍼에서 아이템을 제거하고 외향 벨트 컴포넌트를 복원하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct BuildingItemOutputApplyJob : IJobEntity
{
    [ReadOnly]
    public NativeParallelHashMap<int2, BeltInfo> BeltMap;

    public BufferLookup<StoredItemElement> StoredBufferLookup;
    public ComponentLookup<GridPosition> GridPositionLookup;
    public ComponentLookup<Direction> DirectionLookup;
    public ComponentLookup<BeltMovementState> BeltMovementStateLookup;
    public ComponentLookup<BeltMovementDecision> BeltMovementDecisionLookup;
    public ComponentLookup<LocalTransform> TransformLookup;
    public ComponentLookup<TransferOwnershipRequest> TransferOwnershipRequestLookup;

    public void Execute(
        Entity buildingEntity,
        ref BuildingItemOutputDecision outputDecision,
        EnabledRefRW<BuildingItemOutputDecision> outputDecisionEnabled)
    {
        if (!outputDecision.CanOutput)
        {
            return;
        }

        Entity itemToOutput = outputDecision.ItemToOutput;
        int2 targetBeltPos = outputDecision.TargetBeltPosition;

        // 대상 외향 벨트가 여전히 존재하는지 확인
        if (!BeltMap.TryGetValue(targetBeltPos, out BeltInfo beltInfo))
        {
            outputDecision.CanOutput = false;
            outputDecision.ItemToOutput = Entity.Null;
            outputDecisionEnabled.ValueRW = false;
            return;
        }

        // 창고 버퍼에서 아이템 제거
        if (StoredBufferLookup.HasBuffer(buildingEntity))
        {
            var buffer = StoredBufferLookup[buildingEntity];
            int removeIndex = -1;
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i].ItemEntity == itemToOutput)
                {
                    removeIndex = i;
                    break;
                }
            }

            if (removeIndex != -1)
            {
                buffer.RemoveAt(removeIndex);
            }
        }

        // 아이템 엔티티의 월드 상태 복원
        if (GridPositionLookup.HasComponent(itemToOutput))
        {
            GridPositionLookup[itemToOutput] = new GridPosition(targetBeltPos);
        }

        if (DirectionLookup.HasComponent(itemToOutput))
        {
            DirectionLookup[itemToOutput] = new Direction(beltInfo.Direction);
        }

        if (BeltMovementStateLookup.HasComponent(itemToOutput))
        {
            BeltMovementStateLookup[itemToOutput] = new BeltMovementState(0.0f);
            BeltMovementStateLookup.SetComponentEnabled(itemToOutput, true);
        }

        if (BeltMovementDecisionLookup.HasComponent(itemToOutput))
        {
            BeltMovementDecisionLookup[itemToOutput] = new BeltMovementDecision(0.0f, false);
            BeltMovementDecisionLookup.SetComponentEnabled(itemToOutput, true);
        }

        if (TransformLookup.HasComponent(itemToOutput))
        {
            float2 center = new float2(targetBeltPos.x, targetBeltPos.y);
            float2 dirFloat = new float2(beltInfo.Direction.ToInt2().x, beltInfo.Direction.ToInt2().y);
            float2 visualPos = center + dirFloat * (0.0f - 0.5f);
            TransformLookup[itemToOutput] = LocalTransform.FromPosition(new float3(visualPos.x, visualPos.y, 0f));
        }

        // 소유권 이전 요청 발행 (ItemOwnershipApplySystem에서 WorldItem으로 최종 반영)
        if (TransferOwnershipRequestLookup.HasComponent(itemToOutput))
        {
            TransferOwnershipRequestLookup[itemToOutput] = new TransferOwnershipRequest(Entity.Null);
            TransferOwnershipRequestLookup.SetComponentEnabled(itemToOutput, true);
        }

        // 출고 의사결정 컴포넌트 비활성화 (소비 완료)
        outputDecision.CanOutput = false;
        outputDecision.ItemToOutput = Entity.Null;
        outputDecisionEnabled.ValueRW = false;
    }
}
