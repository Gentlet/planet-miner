using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// 창고 아이템의 입고 및 출고 상태 전이를 일괄 적용하는 시스템.
/// 
/// [책임]
/// - StateApplyGroup(Phase 5)에서 ItemOwnershipApplySystem 직전에 실행됩니다.
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

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _storedBufferLookup = state.GetBufferLookup<StoredItemElement>(false);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _storedBufferLookup.Update(ref state);

        // 1. [입고 처리] 슬롯 확정된 아이템을 창고 버퍼에 적재
        foreach (var (inputDecisionRw, enabledRw, itemIdentity, entity) in 
                 SystemAPI.Query<RefRW<BuildingItemInputDecision>, EnabledRefRW<BuildingItemInputDecision>, RefRO<ItemIdentity>>()
                          .WithEntityAccess())
        {
            if (!inputDecisionRw.ValueRO.CanDeposit || inputDecisionRw.ValueRO.TargetSlotIndex < 0)
            {
                continue;
            }

            Entity building = inputDecisionRw.ValueRO.TargetBuilding;
            if (!SystemAPI.Exists(building) || !_storedBufferLookup.HasBuffer(building))
            {
                // 대상 건물이 유효하지 않으면 입고 취소
                inputDecisionRw.ValueRW.CanDeposit = false;
                inputDecisionRw.ValueRW.TargetSlotIndex = -1;
                enabledRw.ValueRW = false;
                continue;
            }

            var buffer = _storedBufferLookup[building];
            buffer.Add(new StoredItemElement(entity, itemIdentity.ValueRO.Type, inputDecisionRw.ValueRO.TargetSlotIndex));

            // 벨트 이동 상태 비활성화 (보관 상태로 진입)
            if (SystemAPI.HasComponent<BeltMovementState>(entity))
            {
                SystemAPI.SetComponentEnabled<BeltMovementState>(entity, false);
            }

            // 입고 의사결정 컴포넌트 비활성화 (소비 완료)
            enabledRw.ValueRW = false;

            // 소유권 이전 요청 발행 (ItemOwnershipApplySystem에서 Stored(building)으로 최종 반영)
            if (SystemAPI.HasComponent<TransferOwnershipRequest>(entity))
            {
                SystemAPI.SetComponent(entity, new TransferOwnershipRequest(building));
                SystemAPI.SetComponentEnabled<TransferOwnershipRequest>(entity, true);
            }
        }

        // 2. [출고 처리] 출고 결정된 건물의 버퍼에서 아이템을 제거하고 외향 벨트로 방출
        if (SystemAPI.HasSingleton<BeltSpatialIndex>())
        {
            var beltIndex = SystemAPI.GetSingleton<BeltSpatialIndex>();

            foreach (var (outputDecisionRw, enabledRw, buildingEntity) in 
                     SystemAPI.Query<RefRW<BuildingItemOutputDecision>, EnabledRefRW<BuildingItemOutputDecision>>()
                              .WithEntityAccess())
            {
                if (!outputDecisionRw.ValueRO.CanOutput)
                {
                    continue;
                }

                Entity itemToOutput = outputDecisionRw.ValueRO.ItemToOutput;
                int2 targetBeltPos = outputDecisionRw.ValueRO.TargetBeltPosition;

                // 대상 외향 벨트가 여전히 존재하는지 확인
                if (!beltIndex.Map.TryGetValue(targetBeltPos, out BeltInfo beltInfo))
                {
                    // 외향 벨트가 사라졌으면 출고 취소
                    outputDecisionRw.ValueRW.CanOutput = false;
                    outputDecisionRw.ValueRW.ItemToOutput = Entity.Null;
                    enabledRw.ValueRW = false;
                    continue;
                }

                // 창고 버퍼에서 아이템 제거
                if (_storedBufferLookup.HasBuffer(buildingEntity))
                {
                    var buffer = _storedBufferLookup[buildingEntity];
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
                if (SystemAPI.Exists(itemToOutput))
                {
                    if (SystemAPI.HasComponent<GridPosition>(itemToOutput))
                    {
                        SystemAPI.SetComponent(itemToOutput, new GridPosition(targetBeltPos));
                    }

                    if (SystemAPI.HasComponent<Direction>(itemToOutput))
                    {
                        SystemAPI.SetComponent(itemToOutput, new Direction(beltInfo.Direction));
                    }

                    if (SystemAPI.HasComponent<BeltMovementState>(itemToOutput))
                    {
                        SystemAPI.SetComponent(itemToOutput, new BeltMovementState(0.0f));
                        SystemAPI.SetComponentEnabled<BeltMovementState>(itemToOutput, true);
                    }

                    if (SystemAPI.HasComponent<BeltMovementDecision>(itemToOutput))
                    {
                        SystemAPI.SetComponent(itemToOutput, new BeltMovementDecision(0.0f, false));
                        SystemAPI.SetComponentEnabled<BeltMovementDecision>(itemToOutput, true);
                    }

                    if (SystemAPI.HasComponent<LocalTransform>(itemToOutput))
                    {
                        float2 center = new float2(targetBeltPos.x, targetBeltPos.y);
                        float2 dirFloat = new float2(beltInfo.Direction.ToInt2().x, beltInfo.Direction.ToInt2().y);
                        float2 visualPos = center + dirFloat * (0.0f - 0.5f);
                        SystemAPI.SetComponent(itemToOutput, LocalTransform.FromPosition(new float3(visualPos.x, visualPos.y, 0f)));
                    }

                    // 소유권 이전 요청 발행 (ItemOwnershipApplySystem에서 WorldItem으로 최종 반영)
                    if (SystemAPI.HasComponent<TransferOwnershipRequest>(itemToOutput))
                    {
                        SystemAPI.SetComponent(itemToOutput, new TransferOwnershipRequest(Entity.Null));
                        SystemAPI.SetComponentEnabled<TransferOwnershipRequest>(itemToOutput, true);
                    }
                }

                // 출고 의사결정 컴포넌트 비활성화 (소비 완료)
                outputDecisionRw.ValueRW.CanOutput = false;
                outputDecisionRw.ValueRW.ItemToOutput = Entity.Null;
                enabledRw.ValueRW = false;
            }
        }
    }
}
