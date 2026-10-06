using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// 역할·목적: StateApply에서 예약된 건물 입고와 벨트 출고를 기존 실물에 반영한다.
/// 입력·생성자: 입고/출고 Decision과 두 Reservation의 승인 슬롯/대상, 현재 보관/생산 버퍼와 벨트 인덱스.
/// 출력·소유권: 보관 버퍼를 추가/제거하고 출고 위치·방향·벨트 상태를 갱신한다. TransferOwnershipRequest를 활성화하여 Owner/렌더는 ItemOwnershipApplySystem에 넘긴다.
/// 입고 Job 뒤 출고 Job을 연결하고 인덱스 Reader를 Fence에 등록한다. 활성 Destroy 실물은 인계에서 제외한다.
/// 정리·가시화: 성공/거부된 유효 인계 결정은 비활성화한다. 이 시스템은 ECB를 기록하지 않으며 렌더 구조 변경은 Ownership의 EndStateApply에 확정한다.
/// </summary>
[UpdateInGroup(typeof(StateApplyGroup))]
[UpdateBefore(typeof(ItemOwnershipApplySystem))]
[BurstCompile]
public partial struct BuildingItemStorageApplySystem : ISystem
{
    private BufferLookup<StoredItemElement> _storedBufferLookup;
    private BufferLookup<ProductItemElement> _productBufferLookup;
    private ComponentLookup<BeltMovementState> _beltMovementStateLookup;
    private ComponentLookup<TransferOwnershipRequest> _transferOwnershipRequestLookup;
    private ComponentLookup<DestroyItemRequest> _destroyRequestLookup;
    private ComponentLookup<GridPosition> _gridPositionLookup;
    private ComponentLookup<Direction> _directionLookup;
    private ComponentLookup<LocalTransform> _transformLookup;

    private EntityQuery _inputQuery;
    private EntityQuery _outputQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _storedBufferLookup = state.GetBufferLookup<StoredItemElement>(false);
        _productBufferLookup = state.GetBufferLookup<ProductItemElement>(false);
        _beltMovementStateLookup = state.GetComponentLookup<BeltMovementState>(false);
        _transferOwnershipRequestLookup = state.GetComponentLookup<TransferOwnershipRequest>(false);
        _destroyRequestLookup = state.GetComponentLookup<DestroyItemRequest>(true);
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
        _productBufferLookup.Update(ref state);
        _beltMovementStateLookup.Update(ref state);
        _transferOwnershipRequestLookup.Update(ref state);
        _destroyRequestLookup.Update(ref state);
        _gridPositionLookup.Update(ref state);
        _directionLookup.Update(ref state);
        _transformLookup.Update(ref state);

        // 같은 저장 버퍼/실물 상태의 쓰기를 직렬로 연결한다. Owner/렌더 반영은 뒤의 일반 Ownership에 동일 실물로 전달한다.
        // 1. [입고 Job 스케줄링]
        var inputJob = new BuildingItemInputApplyJob
        {
            StoredBufferLookup = _storedBufferLookup,
            BeltMovementStateLookup = _beltMovementStateLookup,
            TransferOwnershipRequestLookup = _transferOwnershipRequestLookup,
            DestroyRequestLookup = _destroyRequestLookup
        };
        var inputHandle = inputJob.Schedule(_inputQuery, state.Dependency);

        // 2. [출고 Job 스케줄링]
        var outputJob = new BuildingItemOutputApplyJob
        {
            BeltMap = beltIndex.Map,
            StoredBufferLookup = _storedBufferLookup,
            ProductBufferLookup = _productBufferLookup,
            GridPositionLookup = _gridPositionLookup,
            DirectionLookup = _directionLookup,
            BeltMovementStateLookup = _beltMovementStateLookup,
            TransformLookup = _transformLookup,
            TransferOwnershipRequestLookup = _transferOwnershipRequestLookup,
            DestroyRequestLookup = _destroyRequestLookup
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
    [ReadOnly] public ComponentLookup<DestroyItemRequest> DestroyRequestLookup;
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

        if (DestroyRequestLookup.HasComponent(entity) && DestroyRequestLookup.IsComponentEnabled(entity))
        {
            inputDecision.CanDeposit = false;
            inputDecision.TargetSlotIndex = -1;
            inputDecisionEnabled.ValueRW = false;
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
    [ReadOnly] public ComponentLookup<DestroyItemRequest> DestroyRequestLookup;
    [ReadOnly]
    public NativeParallelHashMap<int2, BeltInfo> BeltMap;

    public BufferLookup<StoredItemElement> StoredBufferLookup;
    public BufferLookup<ProductItemElement> ProductBufferLookup;
    public ComponentLookup<GridPosition> GridPositionLookup;
    public ComponentLookup<Direction> DirectionLookup;
    public ComponentLookup<BeltMovementState> BeltMovementStateLookup;
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

        if (DestroyRequestLookup.HasComponent(itemToOutput) && DestroyRequestLookup.IsComponentEnabled(itemToOutput))
        {
            outputDecision.CanOutput = false;
            outputDecision.ItemToOutput = Entity.Null;
            outputDecisionEnabled.ValueRW = false;
            return;
        }

        // 대상 외향 벨트가 여전히 존재하는지 확인
        if (!BeltMap.TryGetValue(targetBeltPos, out BeltInfo beltInfo))
        {
            outputDecision.CanOutput = false;
            outputDecision.ItemToOutput = Entity.Null;
            outputDecisionEnabled.ValueRW = false;
            return;
        }

        // 생산물 버퍼 또는 창고 버퍼에서 아이템 제거
        if (ProductBufferLookup.HasBuffer(buildingEntity))
        {
            var buffer = ProductBufferLookup[buildingEntity];
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
        else if (StoredBufferLookup.HasBuffer(buildingEntity))
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
