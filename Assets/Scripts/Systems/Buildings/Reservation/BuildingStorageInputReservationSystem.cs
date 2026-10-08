using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: Reservation에서 같은 틱 건물 입고 후보의 저장 슬롯 경합을 중재한다.
/// 입력·생성자: BuildingItemInputDecisionSystem의 활성 후보, 기존 StoredItemElement와 입력 전용 슬롯/품목 스택 한도.
/// 출력·소유권: 허용 결정의 TargetSlotIndex 또는 거부/비활성 상태만 쓴다. 저장 실물 버퍼는 변경하지 않는다.
/// 이번 Job의 PendingAdditions로 앞서 승인한 품목/수량을 합산하여 같은 빈 슬롯의 중복 사용을 막는다. 영속 예약이 아니다.
/// 이용·정리: BuildingItemStorageApplySystem이 승인 슬롯에 입고한다. 임시 집계는 Job 완료 후 해제하며 ECB 기록은 없다.
/// </summary>
[UpdateInGroup(typeof(BuildingReservationGroup))]
[BurstCompile]
public partial struct BuildingStorageInputReservationSystem : ISystem
{
    private ComponentLookup<Storage> _storageLookup;
    private BufferLookup<StoredItemElement> _storedBufferLookup;
    private BufferLookup<BuildingInputSlotElement> _inputSlotLookup;
    private BufferLookup<ItemConfigElement> _itemConfigLookup;
    private EntityQuery _inputQuery;
    private EntityQuery _itemRegistryQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _storageLookup = state.GetComponentLookup<Storage>(true);
        _storedBufferLookup = state.GetBufferLookup<StoredItemElement>(true);
        _inputSlotLookup = state.GetBufferLookup<BuildingInputSlotElement>(true);
        _itemConfigLookup = state.GetBufferLookup<ItemConfigElement>(true);
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
        _inputSlotLookup.Update(ref state);
        _itemConfigLookup.Update(ref state);

        Entity itemRegistryEntity = Entity.Null;
        if (!_itemRegistryQuery.IsEmptyIgnoreFilter)
        {
            itemRegistryEntity = _itemRegistryQuery.GetSingletonEntity();
        }

        int requestCount = _inputQuery.CalculateEntityCount();
        int initialCapacity = math.max(64, requestCount);

        // 프레임 내 예약 품목과 수량을 함께 유지하여 새 슬롯의 잔여 용량도 재사용한다.
        var pendingAdditions = new NativeParallelHashMap<int2, BuildingStorageInputReservationJob.PendingSlot>(initialCapacity, Allocator.TempJob);

        var job = new BuildingStorageInputReservationJob
        {
            StorageLookup = _storageLookup,
            StoredBufferLookup = _storedBufferLookup,
            InputSlotLookup = _inputSlotLookup,
            ItemRegistryEntity = itemRegistryEntity,
            ItemConfigLookup = _itemConfigLookup,
            PendingAdditions = pendingAdditions
        };

        // 단일 워커 스레드로 직렬 비동기 스케줄링: 레이스 컨디션 없이 메인 스레드 오프로딩
        var jobHandle = job.Schedule(_inputQuery, state.Dependency);
        pendingAdditions.Dispose(jobHandle);

        state.Dependency = jobHandle;
    }
}

/// <summary>
/// 각 아이템의 창고 슬롯 배정과 경합을 순차 처리하는 단일 워커 Burst Job.
/// </summary>
[BurstCompile]
public partial struct BuildingStorageInputReservationJob : IJobEntity
{
    /// <summary>이번 Reservation Job에서 승인한 슬롯 품목/수량의 임시 집계. 컴포넌트나 영속 재고로 게시하지 않는다.</summary>
    public struct PendingSlot
    {
        public ItemTypeEnum ItemType;
        public int Count;
    }

    [ReadOnly]
    public ComponentLookup<Storage> StorageLookup;

    [ReadOnly]
    public BufferLookup<StoredItemElement> StoredBufferLookup;

    [ReadOnly]
    public BufferLookup<BuildingInputSlotElement> InputSlotLookup;

    public Entity ItemRegistryEntity;

    [ReadOnly]
    public BufferLookup<ItemConfigElement> ItemConfigLookup;

    public NativeParallelHashMap<int2, PendingSlot> PendingAdditions;

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

        bool hasInputSlots = InputSlotLookup.HasBuffer(building);
        bool hasItemConfig = ItemConfigLookup.HasBuffer(ItemRegistryEntity);
        DynamicBuffer<BuildingInputSlotElement> inputSlots = default;
        if (hasInputSlots)
        {
            inputSlots = InputSlotLookup[building];
            if (inputSlots.Length != slotCount || slotCount > GameConstants.MaxStorageSlots ||
                !hasItemConfig)
            {
                inputDecision.CanDeposit = false;
                inputDecision.TargetSlotIndex = -1;
                inputDecisionEnabled.ValueRW = false;
                return;
            }
        }

        ItemTypeEnum itemType = itemIdentity.Type;
        int maxStack = 0;
        if (hasItemConfig)
        {
            maxStack = ItemRegistry.GetMaxStack(ItemConfigLookup[ItemRegistryEntity], itemType);
        }
        // 빈 슬롯도 한 개의 실물을 수용할 양수 한도가 필요하다. 설정 부재를 기본값으로 보충하지 않는다.
        if (maxStack <= 0)
        {
            inputDecision.CanDeposit = false;
            inputDecision.TargetSlotIndex = -1;
            inputDecisionEnabled.ValueRW = false;
            return;
        }

        // FixedList512Bytes를 사용하여 unsafe 코드 없이 스택 기반 O(1) 슬롯 점유 집계 (GameConstants.MaxStorageSlots 상한 준수)
        int safeSlotCount = math.min(slotCount, GameConstants.MaxStorageSlots);
        var slotOccupancy = new FixedList512Bytes<int>();
        var slotTypes = new FixedList512Bytes<ItemTypeEnum>();

        for (int s = 0; s < safeSlotCount; s++)
        {
            slotOccupancy.Add(0);
            // 전용 슬롯은 비어 있어도 품목이 정해져 있다. 같은 프레임의 예약끼리도 스택을 공유한다.
            slotTypes.Add(hasInputSlots ? inputSlots[s].ItemType : (ItemTypeEnum)255);
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

        // 저장 버퍼는 입고 전이므로 같은 Job에서 먼저 승인된 후보의 품목/수량을 합산해야 한다.
        for (int s = 0; s < safeSlotCount; s++)
        {
            if (PendingAdditions.TryGetValue(new int2(building.Index, s), out var pending))
            {
                slotOccupancy[s] = slotOccupancy[s] + pending.Count;
                slotTypes[s] = pending.ItemType;
            }
        }

        // 적재 가능 슬롯 탐색
        int sameTypeSlot = -1;
        int firstEmptySlot = -1;

        for (int s = 0; s < safeSlotCount; s++)
        {
            if (hasInputSlots && inputSlots[s].ItemType != itemType)
            {
                continue;
            }

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

            var slotKey = new int2(building.Index, targetSlot);
            PendingAdditions.TryGetValue(slotKey, out var pending);
            PendingAdditions[slotKey] = new PendingSlot
            {
                ItemType = itemType,
                Count = pending.Count + 1
            };
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
