using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 창고 입고 판정을 받은 아이템들의 슬롯 예약 및 경합을 해결하는 시스템.
/// 
/// [책임]
/// - ReservationGroup(Phase 3)에서 실행됩니다.
/// - BuildingItemInputDecisionSystem(Phase 2)에서 입고 가능 판정(CanDeposit == true, TargetSlotIndex == -1)을 받은 아이템을 수집합니다.
/// - 대상 창고(Storage)의 슬롯 수(SlotCount), 기존 적재 버퍼(StoredItemElement), ItemConfig.MaxStack을 기반으로 슬롯을 계산합니다.
/// - 동일 프레임에 여러 아이템이 동일 창고로 동시 유입될 때의 슬롯 경합을 안전하게 직렬화하여 선점합니다.
///   1) 동일 아이템 타입이 이미 존재하고 잔여 공간이 있는 슬롯 우선 배정 (스택 병합)
///   2) 여유 슬롯이 없으면 0 ~ SlotCount - 1 중 비어 있는 최소 번호 슬롯 신규 배정
///   3) 창고가 만석이거나 수용 불가능한 경우 CanDeposit = false, TargetSlotIndex = -1로 전환하여 입고 보류
/// - 예약 성공 시 TargetSlotIndex를 확정하여 StateApplyGroup의 BuildingItemStorageApplySystem에 전달합니다.
/// </summary>
[UpdateInGroup(typeof(ReservationGroup))]
[BurstCompile]
public partial struct BuildingStorageInputReservationSystem : ISystem
{
    private ComponentLookup<Storage> _storageLookup;
    private BufferLookup<StoredItemElement> _storedBufferLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _storageLookup = state.GetComponentLookup<Storage>(true);
        _storedBufferLookup = state.GetBufferLookup<StoredItemElement>(true);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _storageLookup.Update(ref state);
        _storedBufferLookup.Update(ref state);

        ItemConfig itemConfig = new ItemConfig(50);
        DynamicBuffer<ItemConfigElement> itemConfigBuffer = default;
        if (SystemAPI.HasSingleton<ItemConfig>())
        {
            var configEntity = SystemAPI.GetSingletonEntity<ItemConfig>();
            itemConfig = SystemAPI.GetComponent<ItemConfig>(configEntity);
            itemConfigBuffer = SystemAPI.GetBuffer<ItemConfigElement>(configEntity);
        }

        // 이번 프레임 내 동일 건물/슬롯에 추가 배정된 수량을 추적하는 맵
        // Key: new int2(building.Index, slotIndex), Value: 이번 프레임 추가 예약 수
        var pendingAdditions = new NativeParallelHashMap<int2, int>(64, Allocator.Temp);

        foreach (var (inputDecisionRw, enabledRw, itemIdentity) in 
                 SystemAPI.Query<RefRW<BuildingItemInputDecision>, EnabledRefRW<BuildingItemInputDecision>, RefRO<ItemIdentity>>())
        {
            if (!inputDecisionRw.ValueRO.CanDeposit || inputDecisionRw.ValueRO.TargetSlotIndex != -1)
            {
                continue;
            }

            Entity building = inputDecisionRw.ValueRO.TargetBuilding;
            if (!_storageLookup.HasComponent(building) || !_storedBufferLookup.HasBuffer(building))
            {
                inputDecisionRw.ValueRW.CanDeposit = false;
                inputDecisionRw.ValueRW.TargetSlotIndex = -1;
                enabledRw.ValueRW = false;
                continue;
            }

            var storage = _storageLookup[building];
            var storedBuffer = _storedBufferLookup[building];
            int slotCount = storage.SlotCount;
            if (slotCount <= 0)
            {
                inputDecisionRw.ValueRW.CanDeposit = false;
                inputDecisionRw.ValueRW.TargetSlotIndex = -1;
                enabledRw.ValueRW = false;
                continue;
            }

            ItemTypeEnum itemType = itemIdentity.ValueRO.Type;
            int maxStack = itemConfig.DefaultMaxStack;
            if (itemConfigBuffer.IsCreated)
            {
                maxStack = itemConfigBuffer.GetMaxStack(in itemConfig, itemType);
            }

            // 1. 슬롯별 (현재 버퍼 수량 + 이번 프레임 예약 수량) 및 아이템 타입 집계
            var slotOccupancy = new NativeArray<int>(slotCount, Allocator.Temp);
            var slotTypes = new NativeArray<ItemTypeEnum>(slotCount, Allocator.Temp);
            for (int s = 0; s < slotCount; s++)
            {
                slotTypes[s] = (ItemTypeEnum)255; // Empty
            }

            for (int b = 0; b < storedBuffer.Length; b++)
            {
                int slot = storedBuffer[b].SlotIndex;
                if (slot >= 0 && slot < slotCount)
                {
                    slotOccupancy[slot]++;
                    slotTypes[slot] = storedBuffer[b].ItemType;
                }
            }

            for (int s = 0; s < slotCount; s++)
            {
                if (pendingAdditions.TryGetValue(new int2(building.Index, s), out int pendingCount))
                {
                    slotOccupancy[s] += pendingCount;
                }
            }

            // 2. 적재 가능 슬롯 탐색
            int sameTypeSlot = -1;
            int firstEmptySlot = -1;

            for (int s = 0; s < slotCount; s++)
            {
                if (slotOccupancy[s] == 0)
                {
                    if (firstEmptySlot == -1)
                    {
                        firstEmptySlot = s;
                    }
                }
                else if (slotTypes[s] == itemType && slotOccupancy[s] < maxStack)
                {
                    sameTypeSlot = s;
                    break; // 가장 낮은 번호의 여유 슬롯 선택
                }
            }

            int targetSlot = sameTypeSlot != -1 ? sameTypeSlot : firstEmptySlot;

            if (targetSlot != -1)
            {
                // 배정 성공: TargetSlotIndex 확정 및 누적 예약 반영
                inputDecisionRw.ValueRW.TargetSlotIndex = targetSlot;

                int currentPending = 0;
                pendingAdditions.TryGetValue(new int2(building.Index, targetSlot), out currentPending);
                pendingAdditions[new int2(building.Index, targetSlot)] = currentPending + 1;
            }
            else
            {
                // 배정 실패: 창고 만석으로 이번 프레임 입고 불발
                inputDecisionRw.ValueRW.CanDeposit = false;
                inputDecisionRw.ValueRW.TargetSlotIndex = -1;
                enabledRw.ValueRW = false;
            }

            slotOccupancy.Dispose();
            slotTypes.Dispose();
        }

        pendingAdditions.Dispose();
    }
}
