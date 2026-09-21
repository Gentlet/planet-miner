using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 창고 입고 판정을 받은 아이템들의 슬롯 예약 및 경합을 해결하는 시스템.
/// 
/// [책임]
/// - ReservationGroup(Phase 3)에서 실행됩니다.
/// - BuildingItemInputDecisionSystem(Phase 2)에서 입고 가능 판정(CanDeposit == true, TargetSlotIndex == -1)을 받은 아이템을 수집합니다.
/// - 단일 워커 스레드 Job(BuildingStorageInputReservationJob)을 스케줄링하여 메인 스레드 부하를 0으로 유지하면서,
///   순차 실행을 통해 동일 창고로의 슬롯 중복 배정 경합(Race Condition)을 안전하게 해결합니다.
/// - ItemConfig.MaxStack 기반 스택 병합 및 신규 슬롯 배정을 확정하여 BuildingItemStorageApplySystem에 전달합니다.
/// </summary>
[UpdateInGroup(typeof(ReservationGroup))]
[BurstCompile]
public partial struct BuildingStorageInputReservationSystem : ISystem
{
    private ComponentLookup<Storage> _storageLookup;
    private BufferLookup<StoredItemElement> _storedBufferLookup;
    private EntityQuery _inputQuery;
    private EntityQuery _itemRegistryQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _storageLookup = state.GetComponentLookup<Storage>(true);
        _storedBufferLookup = state.GetBufferLookup<StoredItemElement>(true);
        _itemRegistryQuery = state.GetEntityQuery(ComponentType.ReadOnly<ItemRegistry>());

        _inputQuery = SystemAPI.QueryBuilder()
            .WithAllRW<BuildingItemInputDecision>()
            .WithAll<ItemIdentity>()
            .Build();
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

        ItemRegistry itemRegistry = default;
        if (!_itemRegistryQuery.IsEmptyIgnoreFilter)
        {
            itemRegistry = _itemRegistryQuery.GetSingleton<ItemRegistry>();
        }

        int requestCount = _inputQuery.CalculateEntityCount();
        int initialCapacity = math.max(64, requestCount);

        // 이번 프레임 내 동일 건물/슬롯에 추가 배정된 수량을 추적하는 맵 (단일 워커 스레드 Job 내에서 순차 갱신)
        var pendingAdditions = new NativeParallelHashMap<int2, int>(initialCapacity, Allocator.TempJob);

        var job = new BuildingStorageInputReservationJob
        {
            StorageLookup = _storageLookup,
            StoredBufferLookup = _storedBufferLookup,
            ItemRegistry = itemRegistry,
            PendingAdditions = pendingAdditions
        };

        // 단일 워커 스레드로 직렬 비동기 스케줄링: 레이스 컨디션 없이 메인 스레드 오프로딩
        var jobHandle = job.Schedule(_inputQuery, state.Dependency);
        pendingAdditions.Dispose(jobHandle);

        state.Dependency = jobHandle;
    }
}

/// <summary>
/// 각 아이템의 창고 슬롯 배정 및 경합을 순차적으로 안전하게 해결하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct BuildingStorageInputReservationJob : IJobEntity
{
    [ReadOnly]
    public ComponentLookup<Storage> StorageLookup;

    [ReadOnly]
    public BufferLookup<StoredItemElement> StoredBufferLookup;

    [ReadOnly]
    public ItemRegistry ItemRegistry;

    public NativeParallelHashMap<int2, int> PendingAdditions;

    public void Execute(
        ref BuildingItemInputDecision inputDecision,
        EnabledRefRW<BuildingItemInputDecision> inputDecisionEnabled,
        in ItemIdentity itemIdentity)
    {
        if (!inputDecision.CanDeposit || inputDecision.TargetSlotIndex != -1)
        {
            return;
        }

        Entity building = inputDecision.TargetBuilding;
        if (!StorageLookup.HasComponent(building) || !StoredBufferLookup.HasBuffer(building))
        {
            inputDecision.CanDeposit = false;
            inputDecision.TargetSlotIndex = -1;
            inputDecisionEnabled.ValueRW = false;
            return;
        }

        var storage = StorageLookup[building];
        var storedBuffer = StoredBufferLookup[building];
        int slotCount = storage.SlotCount;
        if (slotCount <= 0)
        {
            inputDecision.CanDeposit = false;
            inputDecision.TargetSlotIndex = -1;
            inputDecisionEnabled.ValueRW = false;
            return;
        }

        ItemTypeEnum itemType = itemIdentity.Type;
        int maxStack = 50;
        if (ItemRegistry.Value.IsCreated)
        {
            maxStack = ItemRegistry.Value.Value.GetMaxStack(itemType);
        }

        // FixedList512Bytes를 사용하여 unsafe 코드 없이 스택 기반 O(1) 슬롯 점유 집계 (GameConstants.MaxStorageSlots 상한 준수)
        int safeSlotCount = math.min(slotCount, GameConstants.MaxStorageSlots);
        var slotOccupancy = new FixedList512Bytes<int>();
        var slotTypes = new FixedList512Bytes<ItemTypeEnum>();

        for (int s = 0; s < safeSlotCount; s++)
        {
            slotOccupancy.Add(0);
            slotTypes.Add((ItemTypeEnum)255); // Empty
        }

        for (int b = 0; b < storedBuffer.Length; b++)
        {
            int slot = storedBuffer[b].SlotIndex;
            if (slot >= 0 && slot < safeSlotCount)
            {
                slotOccupancy[slot] = slotOccupancy[slot] + 1;
                slotTypes[slot] = storedBuffer[b].ItemType;
            }
        }

        for (int s = 0; s < safeSlotCount; s++)
        {
            if (PendingAdditions.TryGetValue(new int2(building.Index, s), out int pendingCount))
            {
                slotOccupancy[s] = slotOccupancy[s] + pendingCount;
            }
        }

        // 적재 가능 슬롯 탐색
        int sameTypeSlot = -1;
        int firstEmptySlot = -1;

        for (int s = 0; s < safeSlotCount; s++)
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
            // 배정 성공
            inputDecision.TargetSlotIndex = targetSlot;

            int currentPending = 0;
            PendingAdditions.TryGetValue(new int2(building.Index, targetSlot), out currentPending);
            PendingAdditions[new int2(building.Index, targetSlot)] = currentPending + 1;
        }
        else
        {
            // 배정 실패: 만석으로 이번 프레임 입고 불발
            inputDecision.CanDeposit = false;
            inputDecision.TargetSlotIndex = -1;
            inputDecisionEnabled.ValueRW = false;
        }
    }
}
