using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// 역할·목적: Editor/Development Build에서 Synchronization 마지막에 아이템·벨트·보관 수량·자원 및 미소비 요청/결정의 정합성을 검사한다.
/// 입력·생성자: 여섯 phase가 반영한 ECS 원본과 SpatialSyncSystem의 공간 인덱스. 직접 맵 조회 전 네 Fence를 완료한다.
/// 출력·소유권: 위반 누적 수와 세션별 진단 파일을 기록하고 Debug.Break를 호출한다. 도메인 상태를 복구하거나 요청을 대신 소비하지 않는다.
/// 이용·정리: 개발자/테스트가 로그와 누적 수를 확인한다. 검사 주기는 프레임 간격으로 조정하며 임시 검사용 컨테이너는 검사 뒤 해제한다.
/// 가시화: 최종 EndSimulation 재생과 공간 Writer 완료 뒤의 상태만 검사한다. 진단 순번은 누적 위반 수 초기화와 별도로 유지하며 ECB 기록은 없다.
/// </summary>
[UpdateInGroup(typeof(SynchronizationGroup), OrderLast = true)]
public partial class WorldInvariantValidationSystem : SystemBase
{
    /// <summary>
    /// 검증 주기 (기본값: 매 1프레임마다 검사, N프레임 간격 조절 가능)
    /// </summary>
    public int CheckIntervalFrames = 1;
    private int _frameCounter;

    private string _logDirectory;
    private string _reportSessionId;
    private long _reportSequence;

    /// <summary>
    /// 무결성 위반 누적 횟수 (테스트 단언 및 진단용).
    /// </summary>
    public int TotalViolationCount { get; private set; }

    /// <summary>
    /// 무결성 위반 누적 횟수를 0으로 초기화.
    /// </summary>
    public void ResetViolationCount()
    {
        TotalViolationCount = 0;
    }

    /// <summary>
    /// 현재 World의 진단 출력 경로를 지정한다. 테스트는 실행별 전용 경로로 격리한다.
    /// 기존 보고 파일을 삭제하거나 이동하지 않으며 세션 ID와 보고 순번은 유지한다.
    /// </summary>
    public void SetLogDirectory(string logDirectory)
    {
        if (logDirectory == null)
        {
            throw new ArgumentNullException(nameof(logDirectory));
        }

        if (string.IsNullOrWhiteSpace(logDirectory))
        {
            throw new ArgumentException("Log directory must not be empty.", nameof(logDirectory));
        }

        _logDirectory = Path.GetFullPath(logDirectory);
    }

    protected override void OnCreate()
    {
        base.OnCreate();
        // 관리 코드 variant와 무관하게 기존 Editor/Development Player의 진단 범위를 유지한다.
        Enabled = Debug.isDebugBuild;
        if (!Enabled)
        {
            return;
        }
        // Logs/InvariantErrors/ 경로 설정 (프로젝트 루트 기준)
        _logDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "InvariantErrors"));
        _reportSessionId = Guid.NewGuid().ToString("N");
    }

    protected override void OnUpdate()
    {
        _frameCounter++;
        if (_frameCounter < CheckIntervalFrames)
        {
            return;
        }
        _frameCounter = 0;

        // Invariant 검증 실행
        ValidateInvariants();
    }

    private void ValidateInvariants()
    {
        if (SystemAPI.TryGetSingletonRW<BeltSpatialIndexFence>(out var beltFenceRw))
        {
            beltFenceRw.ValueRW.State.Complete();
        }
        if (SystemAPI.TryGetSingletonRW<ItemSpatialIndexFence>(out var itemFenceRw))
        {
            itemFenceRw.ValueRW.State.Complete();
        }
        if (SystemAPI.TryGetSingletonRW<BuildingSpatialIndexFence>(out var buildingFenceRw))
        {
            buildingFenceRw.ValueRW.State.Complete();
        }
        if (SystemAPI.TryGetSingletonRW<ResourceSpatialIndexFence>(out var resourceFenceRw))
        {
            resourceFenceRw.ValueRW.State.Complete();
        }

        if (!SystemAPI.TryGetSingleton<ItemSpatialIndex>(out var itemSpatialIndex))
        {
            itemSpatialIndex = default;
        }
        ComponentLookup<BeltMovementState>? beltMovementLookup = null;

        // 월드 실물과 공간 인덱스의 양방향 정합성.
        ValidateItemAndSpatialInvariants(itemSpatialIndex);

        // 정상 소비돼야 하는 일반 이전/삭제 요청의 잔류 검사. 여기서 대신 소비하지 않는다.
        ValidateRequestLifecycleInvariants();

        // 벨트 이동 계획 소비와 위치/간격 검사.
        ValidateBeltInvariants(itemSpatialIndex, ref beltMovementLookup);

        // 저장 슬롯과 보관 수량의 정합성을 검사한다.
        ValidateStorageInvariants();
        ValidateDroneStationInvariants();

        // 승인된 입출고/라우팅 결정의 잔류 검사.
        ValidateStorageDecisionInvariants();

        // 자원 원본과 자원 공간 인덱스의 양방향 정합성.
        ValidateResourceSpatialInvariants();
    }

    /// <summary>
    /// Item 도메인 및 공간 인덱스(ItemSpatialIndex) 간의 양방향 정합성을 검증.
    /// </summary>
    private void ValidateItemAndSpatialInvariants(in ItemSpatialIndex spatialIndex)
    {
        if (!spatialIndex.Map.IsCreated)
        {
            return;
        }

        foreach (var (pos, entity) in SystemAPI.Query<RefRO<GridPosition>>()
                     .WithAll<ItemIdentity>().WithNone<DestroyItemRequest>().WithEntityAccess())
        {
            if (!spatialIndex.ContainsItemAt(pos.ValueRO.Value, entity))
                ReportViolation("ItemSpatial", "World item is missing from its grid cell.", entity);
        }

        // 2. [역방향 검증] ItemSpatialIndex -> 실제 월드 아이템 정합성 확인
        var kvpArray = spatialIndex.Map.GetKeyValueArrays(Allocator.Temp);
        try
        {
            for (int i = 0; i < kvpArray.Length; i++)
            {
                int2 pos = kvpArray.Keys[i];
                Entity itemEntity = kvpArray.Values[i];

                // 인덱스 내 엔티티가 월드에 실존하는지 확인
                if (!SystemAPI.Exists(itemEntity))
                {
                    ReportViolation(
                        "SpatialIndex",
                        $"ItemSpatialIndex at ({pos.x}, {pos.y}) contains non-existent or destroyed entity ({itemEntity.Index}:{itemEntity.Version}). Dangling pointer detected.",
                        itemEntity
                    );
                    continue;
                }

                // 필수 컴포넌트 보유 확인
                if (!SystemAPI.HasComponent<ItemIdentity>(itemEntity) ||
                    !SystemAPI.TryGetComponent<GridPosition>(itemEntity, out var gridPos))
                {
                    ReportViolation(
                        "SpatialIndex",
                        $"Entity ({itemEntity.Index}:{itemEntity.Version}) indexed at ({pos.x}, {pos.y}) lacks ItemIdentity or GridPosition component.",
                        itemEntity
                    );
                    continue;
                }

                // 좌표 일치 확인
                if (!gridPos.Value.Equals(pos))
                {
                    ReportViolation(
                        "SpatialIndex",
                        $"Item position mismatch: Entity ({itemEntity.Index}:{itemEntity.Version}) has GridPosition ({gridPos.Value.x}, {gridPos.Value.y}), but is indexed at ({pos.x}, {pos.y}).",
                        itemEntity
                    );
                }
            }
        }
        finally
        {
            kvpArray.Dispose();
        }
    }

    /// <summary>
    /// 프레임 종료 시점(SynchronizationGroup)에 소비되지 않고 잔류한 1회성 Request 컴포넌트를 감시.
    /// </summary>
    private void ValidateRequestLifecycleInvariants()
    {
        // 2. DestroyItemRequest 잔류 감시 (StateApplyGroup에서 처리 및 엔티티가 파괴되었어야 함)
        foreach (var (_, entity) in 
                 SystemAPI.Query<RefRO<DestroyItemRequest>>()
                          .WithEntityAccess())
        {
            ReportViolation(
                "RequestLifecycle",
                $"DestroyItemRequest remained active on Entity ({entity.Index}:{entity.Version}) at the end of the frame (SynchronizationGroup). Entity was not destroyed in BuildingStateApplyGroup.",
                entity
            );
        }
    }

    /// <summary>
    /// 현재 벨트 건물과 실물 이동의 정합성을 Synchronization 마지막에 검증한다.
    /// - 1. 미소비(Unconsumed) 이동 계획(PlannedProgress) 잔류 감시
    /// - 2. 고아 벨트 아이템(벨트 없는 위치에서 활성화된 아이템) 감시
    /// - 3. GameConstants의 타일 수용량 초과 및 ItemSpacing 침범 감시
    /// - 4. 연속된 타일 경계 횡단 간격(Boundary Gap) 침범 감시
    /// </summary>
    private void ValidateBeltInvariants(in ItemSpatialIndex itemSpatialIndex,
        ref ComponentLookup<BeltMovementState>? sharedBeltMovementLookup)
    {
        if (!SystemAPI.TryGetSingleton<BeltSpatialIndex>(out var beltIndex) || !beltIndex.Map.IsCreated)
        {
            return;
        }

        if (!itemSpatialIndex.Map.IsCreated)
        {
            return;
        }

        // 1. 미소비 이동 계획 잔류 감시 (ExecutionGroup에서 정상 소비되었는지 확인)
        foreach (var (decision, entity) in 
                 SystemAPI.Query<RefRO<BeltMovementDecision>>()
                          .WithAll<BeltMovementState>()
                          .WithEntityAccess())
        {
            if (decision.ValueRO.PlannedProgress > 0.0f)
            {
                ReportViolation(
                    "BeltInvariant",
                    $"BeltMovementDecision.PlannedProgress ({decision.ValueRO.PlannedProgress}) was not consumed at the end of the frame on Entity ({entity.Index}:{entity.Version}). BuildingExecutionGroup execution may be missing.",
                    entity
                );
            }
        }

        // 2. 고아 벨트 아이템 감시 (벨트가 없는 위치인데 BeltMovementState가 활성화된 경우)
        foreach (var (pos, entity) in SystemAPI.Query<RefRO<GridPosition>>()
                     .WithAll<ItemIdentity, BeltMovementState>().WithEntityAccess())
        {
            if (!beltIndex.HasBeltAt(pos.ValueRO.Value))
                ReportViolation("BeltInvariant", "Item with active belt movement has no belt at its position.", entity);
        }

        // 3 & 4. 벨트 타일별 아이템 수용량 및 간격 침범 감시
        var beltKvpArray = beltIndex.Map.GetKeyValueArrays(Allocator.Temp);
        try
        {
            var beltMovementLookup = sharedBeltMovementLookup ??= SystemAPI.GetComponentLookup<BeltMovementState>(true);
            var itemIdentityLookup = SystemAPI.GetComponentLookup<ItemIdentity>(true);

            var progressList = new NativeList<float>(16, Allocator.Temp);
            var entityList = new NativeList<Entity>(16, Allocator.Temp);

            for (int i = 0; i < beltKvpArray.Length; i++)
            {
                int2 beltPos = beltKvpArray.Keys[i];
                BeltInfo beltInfo = beltKvpArray.Values[i];

                progressList.Clear();
                entityList.Clear();

                // 3-A. 현재 벨트 타일에 등록된 유효 벨트 아이템 수집
                if (itemSpatialIndex.TryGetFirstItem(beltPos, out Entity itemEntity, out var it))
                {
                    do
                    {
                        if (itemIdentityLookup.HasComponent(itemEntity) &&
                            beltMovementLookup.TryGetComponent(itemEntity, out var movement) &&
                            beltMovementLookup.IsComponentEnabled(itemEntity))
                        {
                            progressList.Add(movement.Progress);
                            entityList.Add(itemEntity);
                        }
                    } while (itemSpatialIndex.TryGetNextItem(out itemEntity, ref it));
                }

                int count = progressList.Length;
                if (count == 0) continue;

                // 3-B. 타일당 최대 수용량(1.0f / ItemSpacing) 초과 감시
                if (count > GameConstants.MaxItemsPerBeltTile)
                {
                    ReportViolation(
                        "BeltInvariant",
                        $"Belt tile at ({beltPos.x}, {beltPos.y}) exceeds maximum capacity: {count} items found (maximum allowed is {GameConstants.MaxItemsPerBeltTile}).",
                        entityList[0]
                    );
                }

                // 3-C. 동일 타일 내 간격 침범 감시 (Progress 순 단순 정렬 후 비교)
                for (int a = 0; a < count - 1; a++)
                {
                    for (int b = a + 1; b < count; b++)
                    {
                        if (progressList[a] > progressList[b])
                        {
                            float tempProg = progressList[a];
                            progressList[a] = progressList[b];
                            progressList[b] = tempProg;

                            Entity tempEnt = entityList[a];
                            entityList[a] = entityList[b];
                            entityList[b] = tempEnt;
                        }
                    }
                }

                for (int a = 0; a < count - 1; a++)
                {
                    float gap = progressList[a + 1] - progressList[a];
                    if (gap < GameConstants.ItemSpacing - GameConstants.AlignmentEpsilon)
                    {
                        ReportViolation(
                            "BeltInvariant",
                            $"Belt item spacing violated on tile ({beltPos.x}, {beltPos.y}): gap between item {entityList[a].Index} (progress {progressList[a]:F4}) and item {entityList[a + 1].Index} (progress {progressList[a + 1]:F4}) is {gap:F4} < {GameConstants.ItemSpacing:F4}.",
                            entityList[a]
                        );
                    }
                }

                // 4. 타일 경계(Boundary) 간격 침범 감시
                int2 nextPos = beltPos + beltInfo.Direction.ToInt2();
                if (beltIndex.TryGetBelt(nextPos, out BeltInfo nextBelt))
                {
                    float minNextProgress = float.MaxValue;
                    Entity nextTrailEntity = Entity.Null;

                    if (itemSpatialIndex.TryGetFirstItem(nextPos, out Entity nextItem, out var nextIt))
                    {
                        do
                        {
                            if (itemIdentityLookup.HasComponent(nextItem) &&
                                beltMovementLookup.TryGetComponent(nextItem, out var nextMovement) &&
                                beltMovementLookup.IsComponentEnabled(nextItem))
                            {
                                float prog = nextMovement.Progress;
                                if (prog < minNextProgress)
                                {
                                    minNextProgress = prog;
                                    nextTrailEntity = nextItem;
                                }
                            }
                        } while (itemSpatialIndex.TryGetNextItem(out nextItem, ref nextIt));
                    }

                    if (nextTrailEntity != Entity.Null)
                    {
                        float leadProgress = progressList[count - 1];
                        float boundaryGap = (1.0f - leadProgress) + minNextProgress;
                        if (boundaryGap < GameConstants.ItemSpacing - GameConstants.AlignmentEpsilon)
                        {
                            ReportViolation(
                                "BeltInvariant",
                                $"Belt boundary spacing violated between tile ({beltPos.x}, {beltPos.y}) and next tile ({nextPos.x}, {nextPos.y}): boundary gap is {boundaryGap:F4} < {GameConstants.ItemSpacing:F4} (lead progress: {leadProgress:F4}, next trail progress: {minNextProgress:F4}).",
                                entityList[count - 1]
                            );
                        }
                    }
                }
            }

            progressList.Dispose();
            entityList.Dispose();
        }
        finally
        {
            beltKvpArray.Dispose();
        }
    }

    /// <summary>
    /// 저장 슬롯, 생산품, 화물, 현장 도착량과 연구 회수량의 품목·수량을 검증한다.
    /// </summary>
    private void ValidateStorageInvariants()
    {
        bool hasRegistry = SystemAPI.TryGetSingletonBuffer<ItemConfigElement>(out var items, true);
        var inputs = SystemAPI.GetBufferLookup<BuildingInputSlotElement>(true);
        var storageBuffers = SystemAPI.GetBufferLookup<StorageSlotElement>(true);
        foreach (var (capacity, owner) in SystemAPI.Query<RefRO<Storage>>()
                     .WithOptions(EntityQueryOptions.IncludeDisabledEntities).WithEntityAccess())
        {
            if (!storageBuffers.TryGetBuffer(owner, out var slots))
            {
                ReportViolation("StorageInvariant", "Storage is missing its slot buffer.", owner);
                continue;
            }
            int count = capacity.ValueRO.SlotCount;
            if (count < 0 || count > GameConstants.MaxStorageSlots || count != slots.Length)
            {
                ReportViolation("StorageInvariant", "Storage slot length differs from configured capacity.", owner);
                continue;
            }
            bool hasInput = inputs.TryGetBuffer(owner, out var layout);
            if (hasInput && layout.Length != count)
            {
                ReportViolation("StorageInvariant", "Input slot layout differs from storage capacity.", owner);
            }
            if (count == 0)
            {
                if (!hasInput || !AllowsZeroInputSlots(owner))
                {
                    ReportViolation("StorageInvariant", "Storage has zero slots without a valid empty input configuration.", owner);
                }
                continue;
            }
            for (int index = 0; index < count; index++)
            {
                var slot = slots[index];
                if (!IsValidStack(slot.ItemType, slot.Quantity, hasRegistry, items) ||
                    (slot.Quantity > 0 && hasInput && ((uint)index >= (uint)layout.Length || layout[index].ItemType != slot.ItemType)))
                    ReportViolation("StorageInvariant", $"Invalid inventory at slot {index}.", owner);
            }
            ValidateDemolitionStorageClaims(owner, slots, hasRegistry, items);
        }

        foreach (var (slots, owner) in SystemAPI.Query<DynamicBuffer<ProductSlotElement>>().WithEntityAccess())
        {
            int previous = -1;
            foreach (var slot in slots)
            {
                if (slot.SlotIndex <= previous || slot.Quantity <= 0 ||
                    !IsValidStack(slot.ItemType, slot.Quantity, hasRegistry, items))
                    ReportViolation("ProductInvariant", $"Invalid output at slot {slot.SlotIndex}.", owner);
                previous = slot.SlotIndex;
            }
        }

        var capacityLookup = SystemAPI.TryGetSingleton<DroneCapacityState>(out var capacityState)
            ? capacityState.CarryingCapacity : int.MaxValue;
        foreach (var (cargo, owner) in SystemAPI.Query<RefRO<DroneCargoState>>().WithEntityAccess())
        {
            var value = cargo.ValueRO;
            bool valid = value.Quantity == 0
                ? value.ItemType == ItemTypeEnum.None && value.Origin == DroneCargoOriginEnum.None && !value.BlockAutomaticRecoveryOnDrop
                : value.Quantity > 0 && value.Quantity <= capacityLookup && value.ItemType != ItemTypeEnum.None &&
                  (value.Origin == DroneCargoOriginEnum.Supply || value.Origin == DroneCargoOriginEnum.Recovery ||
                   value.Origin == DroneCargoOriginEnum.DemolitionRecovery);
            if (!valid) ReportViolation("DroneCargoInvariant", "Cargo quantity, type or origin is invalid.", owner);
        }

        foreach (var (requirements, owner) in SystemAPI.Query<DynamicBuffer<ConstructionMaterialRequirementElement>>()
                     .WithAll<ConstructionSite>().WithEntityAccess())
        {
            foreach (var row in requirements)
            {
                if (row.DeliveredQuantity < 0 || row.ReservedQuantity < 0 ||
                    (long)row.DeliveredQuantity + row.ReservedQuantity > row.RequiredQuantity)
                    ReportViolation("ConstructionInvariant", "Delivered or reserved materials exceed the requirement.", owner);
            }
        }

        foreach (var (recoveries, owner) in SystemAPI.Query<DynamicBuffer<ResearchRecoveryElement>>().WithEntityAccess())
        {
            if (!storageBuffers.TryGetBuffer(owner, out var slots)) continue;
            foreach (var recovery in recoveries)
            {
                if ((uint)recovery.SlotIndex >= (uint)slots.Length || recovery.Quantity <= 0 ||
                    slots[recovery.SlotIndex].ItemType != recovery.ItemType ||
                    recovery.Quantity > slots[recovery.SlotIndex].Quantity)
                    ReportViolation("ResearchInvariant", "Recoverable quantity exceeds its inventory slot.", owner);
            }
        }
    }

    // 제작기와 연구소는 현재 정의에 필요한 입력 슬롯이 없거나 슬롯을 처음 구성하기 전에 빈 레이아웃을 유지할 수 있다.
    private bool AllowsZeroInputSlots(Entity owner)
    {
        if (!SystemAPI.TryGetComponent<StorageFilter>(owner, out var filter))
        {
            return false;
        }
        if (filter.Mode != StorageFilterMode.Whitelist || filter.Mask.Part0 != 0 || filter.Mask.Part1 != 0)
        {
            return false;
        }

        if (SystemAPI.TryGetComponent<CrafterState>(owner, out var crafter))
        {
            if (crafter.SelectedRecipeId == 0)
            {
                return true;
            }
            if (!SystemAPI.TryGetSingletonEntity<RecipeRegistry>(out var registry))
            {
                return false;
            }
            var definitions = SystemAPI.GetBufferLookup<RecipeConfigElement>(true);
            if (!definitions.TryGetBuffer(registry, out var recipes))
            {
                return false;
            }
            if (!RecipeConfigLookupUtility.TryGetRecipe(recipes, crafter.SelectedRecipeId, out var recipe))
            {
                return false;
            }
            return recipe.IngredientCount == 0;
        }

        if (!SystemAPI.HasComponent<ResearchInputConfigured>(owner))
        {
            return false;
        }
        if (!SystemAPI.IsComponentEnabled<ResearchInputConfigured>(owner))
        {
            return true;
        }
        if (!SystemAPI.TryGetSingletonEntity<ResearchRegistry>(out var research))
        {
            return false;
        }
        var ingredients = SystemAPI.GetBufferLookup<ResearchIngredientElement>(true);
        if (!ingredients.TryGetBuffer(research, out var rows))
        {
            return false;
        }
        return rows.IsEmpty;
    }

    private static bool IsValidStack(ItemTypeEnum type, int quantity, bool hasRegistry, DynamicBuffer<ItemConfigElement> items)
    {
        if (quantity == 0) return type == ItemTypeEnum.None;
        if (quantity < 0 || type == ItemTypeEnum.None) return false;
        return !hasRegistry || quantity <= ItemRegistry.GetMaxStack(items, type);
    }

    private void ValidateDemolitionStorageClaims(Entity owner, DynamicBuffer<StorageSlotElement> slots,
        bool hasRegistry, DynamicBuffer<ItemConfigElement> items)
    {
        if (!EntityManager.HasBuffer<DroneStationReservedSlotElement>(owner)) return;
        var reservations = EntityManager.GetBuffer<DroneStationReservedSlotElement>(owner, true);
        var storageRead = StationStorageUtility.StorageReadContext.Create(EntityManager, owner);
        int generalSlots = storageRead.NonFuelSlotCount - storageRead.DedicatedSlotCount;
        foreach (var claim in reservations)
        {
            Entity worker = claim.TransportWorker;
            if (worker == Entity.Null) continue;

            Entity link = EntityManager.HasComponent<DroneWorkerAssignment>(worker)
                ? EntityManager.GetComponentData<DroneWorkerAssignment>(worker).Assignment : Entity.Null;
            bool valid = EntityManager.HasComponent<DroneTaskAssignment>(link) &&
                (uint)claim.SlotIndex < (uint)generalSlots && claim.Quantity > 0;
            if (valid)
            {
                var assignment = EntityManager.GetComponentData<DroneTaskAssignment>(link);
                valid = assignment.Worker == worker && assignment.Destination == owner && assignment.Task == claim.Task &&
                    assignment.ItemType == claim.ItemType && !DroneSchedulingUtility.IsTerminal(assignment.State) &&
                    !storageRead.IsDockOccupied(claim.SlotIndex);
            }
            if (valid && hasRegistry)
            {
                int maximum = ItemRegistry.GetMaxStack(items, claim.ItemType);
                int free = InventorySlotUtility.FreeQuantity(slots[claim.SlotIndex], claim.ItemType, maximum);
                valid = DroneDemolitionStorageUtility.FreeQuantity(reservations, claim.SlotIndex,
                    claim.ItemType, free, worker) >= claim.Quantity;
            }
            if (!valid)
                ReportViolation("StorageInvariant", "Demolition transport slot reservation is inconsistent.", owner);
        }
    }

    /// <summary>
    /// 최종 공개 원본의 드론 전용 슬롯과 정박 양방향 소유권을 검사한다.
    /// </summary>
    private void ValidateDroneStationInvariants()
    {
        foreach (var (_, station) in SystemAPI.Query<RefRO<DroneStation>>()
                     .WithOptions(EntityQueryOptions.IncludeDisabledEntities).WithEntityAccess())
        {
            if (!EntityManager.HasComponent<DroneStationStorage>(station) || !EntityManager.HasComponent<Storage>(station) ||
                !EntityManager.HasBuffer<StorageSlotElement>(station) || !EntityManager.HasBuffer<DockedDroneElement>(station))
            {
                ReportViolation("DroneStationInvariant", "Station is missing required storage/dock configuration.", station);
            }
        }
        var registered = new HashSet<Entity>();
        foreach (var (stationStorage, storage, docked, items, station) in
                 SystemAPI.Query<RefRO<DroneStationStorage>, RefRO<Storage>, DynamicBuffer<DockedDroneElement>,
                     DynamicBuffer<StorageSlotElement>>().WithOptions(EntityQueryOptions.IncludeDisabledEntities).WithEntityAccess())
        {
            int total = StationStorageUtility.NonFuelSlotCount(EntityManager, station, storage.ValueRO.SlotCount);
            int dedicated = stationStorage.ValueRO.DedicatedSlotCount;
            if (dedicated <= 0 || total < dedicated || total > GameConstants.MaxStorageSlots)
            {
                ReportViolation("DroneStationInvariant", "Invalid dedicated/general capacity.", station);
            }
            for (int index = 0; index < math.min(total, items.Length); index++)
            {
                if (items[index].Quantity > 0 && !StationStorageUtility.IsItemSlotAllowed(index, total, dedicated, items[index].ItemType))
                    ReportViolation("DroneStationInvariant", "Item occupies an incompatible station slot.", station);
            }
            if (EntityManager.HasBuffer<DroneStationReservedSlotElement>(station))
            {
                using var reservedSlots = new NativeHashSet<int>(math.max(1, total), Allocator.Temp);
                foreach (var entry in EntityManager.GetBuffer<DroneStationReservedSlotElement>(station, true))
                {
                    if (entry.TransportWorker != Entity.Null) continue;
                    bool validReservation = (uint)entry.SlotIndex < (uint)math.min(total, items.Length) && reservedSlots.Add(entry.SlotIndex) &&
                        EntityManager.HasBuffer<DroneStationRelocationMemberElement>(entry.Task);
                    if (validReservation)
                    {
                        var roster = EntityManager.GetBuffer<DroneStationRelocationMemberElement>(entry.Task, true);
                        validReservation = (uint)entry.MemberIndex < (uint)roster.Length &&
                            !roster[entry.MemberIndex].Finished && roster[entry.MemberIndex].Destination == station &&
                            roster[entry.MemberIndex].SlotIndex == entry.SlotIndex;
                        if (validReservation && items[entry.SlotIndex].Quantity > 0)
                        {
                            var member = roster[entry.MemberIndex];
                            validReservation = member.Request != Entity.Null &&
                                EntityManager.HasComponent<DroneStationResult>(member.Request) &&
                                EntityManager.GetComponentData<DroneStationResult>(member.Request).Operation == DroneStationOperation.Store;
                        }
                    }
                    if (!validReservation)
                        ReportViolation("DroneStationInvariant", "Relocation reservation is inconsistent.", station);
                }
            }
            var slots = new HashSet<int>();
            foreach (var entry in docked)
            {
                if (!registered.Add(entry.Drone))
                {
                    ReportViolation("DroneStationInvariant", "Duplicate docked actor.", station);
                    continue;
                }
                if (!slots.Add(entry.SlotIndex))
                {
                    ReportViolation("DroneStationInvariant", "Duplicate docked slot.", station);
                    continue;
                }
                if (!StationStorageUtility.MatchesDockedWorker(EntityManager, station, entry, total, items))
                {
                    ReportViolation("DroneStationInvariant", "Docked actor/slot ownership is inconsistent.", station);
                }
            }
        }
        foreach (var (_, drone) in SystemAPI.Query<RefRO<DroneDocked>>()
                     .WithOptions(EntityQueryOptions.IncludeDisabledEntities).WithEntityAccess())
        {
            if (!registered.Contains(drone))
            {
                ReportViolation("DroneStationInvariant", "Docked actor has no station reference.", drone);
            }
        }
    }

    /// <summary>StateApply 뒤에도 잔류한 확정 입출력 결정을 검사한다.</summary>
    private void ValidateStorageDecisionInvariants()
    {
        // 1. BuildingItemInputDecision 확정 결정(CanDeposit == true && TargetSlotIndex >= 0) 잔류 감시
        foreach (var (decision, entity) in
                 SystemAPI.Query<RefRO<BuildingItemInputDecision>>()
                          .WithEntityAccess())
        {
            if (decision.ValueRO.CanDeposit && decision.ValueRO.TargetSlotIndex >= 0)
            {
                ReportViolation(
                    "DecisionLifecycle",
                    $"BuildingItemInputDecision remained confirmed (CanDeposit=true, TargetSlotIndex={decision.ValueRO.TargetSlotIndex}) at the end of the frame on Item ({entity.Index}:{entity.Version}). Decision was not consumed in BuildingStateApplyGroup.",
                    entity
                );
            }
        }

        // 2. BuildingItemOutputDecision 확정 결정(CanOutput == true) 잔류 감시
        foreach (var (decision, entity) in
                 SystemAPI.Query<RefRO<BuildingItemOutputDecision>>()
                          .WithEntityAccess())
        {
            if (decision.ValueRO.CanOutput)
            {
                ReportViolation(
                    "DecisionLifecycle",
                    $"BuildingItemOutputDecision remained confirmed (CanOutput=true, SourceSlot={decision.ValueRO.SourceSlotIndex}) at the end of the frame on Building ({entity.Index}:{entity.Version}). Decision was not consumed in BuildingStateApplyGroup.",
                    entity
                );
            }
        }

        // 3. RoutingTransferDecision 활성 결정 잔류 감시
        foreach (var (decision, entity) in
                 SystemAPI.Query<RefRO<RoutingTransferDecision>>()
                          .WithEntityAccess())
        {
            if (decision.ValueRO.Item != Entity.Null && decision.ValueRO.TargetBelt != Entity.Null)
            {
                ReportViolation(
                    "DecisionLifecycle",
                    $"RoutingTransferDecision remained active (Item={decision.ValueRO.Item.Index}:{decision.ValueRO.Item.Version}, TargetBelt={decision.ValueRO.TargetBelt.Index}:{decision.ValueRO.TargetBelt.Version}) at the end of the frame on Entity ({entity.Index}:{entity.Version}). Decision was not consumed in BuildingStateApplyGroup.",
                    entity
                );
            }
        }
    }

    /// <summary>
    /// 현재 자원 노드(ResourceNode)와 자원 공간 인덱스(ResourceSpatialIndex)의 정합성을 검증한다.
    /// - 1. ResourceNode가 부착된 엔티티가 ResourceSpatialIndex에 올바른 GridPosition으로 등록되어 있는지 검증
    /// - 2. ResourceSpatialIndex에 등록된 엔티티가 월드에 실존하고 ResourceNode를 가지고 있는지 검증
    /// </summary>
    private void ValidateResourceSpatialInvariants()
    {
        if (!SystemAPI.TryGetSingleton<ResourceSpatialIndex>(out var resourceIndex) || !resourceIndex.Map.IsCreated)
        {
            return;
        }

        // 1. [정방향 검증] ResourceNode -> ResourceSpatialIndex
        foreach (var (pos, resource, entity) in
                 SystemAPI.Query<RefRO<GridPosition>, RefRO<ResourceNode>>()
                          .WithEntityAccess())
        {
            int2 gridPos = pos.ValueRO.Value;
            if (!resourceIndex.TryGetResource(gridPos, out Entity indexedEntity))
            {
                ReportViolation(
                    "ResourceSpatial",
                    $"ResourceNode ({entity.Index}:{entity.Version}) at ({gridPos.x}, {gridPos.y}) is NOT registered in ResourceSpatialIndex.",
                    entity
                );
            }
            else if (indexedEntity != entity)
            {
                ReportViolation(
                    "ResourceSpatial",
                    $"ResourceSpatialIndex mismatch at ({gridPos.x}, {gridPos.y}): expected ({entity.Index}:{entity.Version}), but found ({indexedEntity.Index}:{indexedEntity.Version}).",
                    entity
                );
            }
        }

        // 2. [역방향 검증] ResourceSpatialIndex -> 실존 엔티티
        var kvpArray = resourceIndex.Map.GetKeyValueArrays(Allocator.Temp);
        try
        {
            for (int i = 0; i < kvpArray.Length; i++)
            {
                int2 pos = kvpArray.Keys[i];
                Entity resourceEntity = kvpArray.Values[i];

                if (!SystemAPI.Exists(resourceEntity))
                {
                    ReportViolation(
                        "ResourceSpatial",
                        $"ResourceSpatialIndex contains non-existent or destroyed Entity ({resourceEntity.Index}:{resourceEntity.Version}) at ({pos.x}, {pos.y}).",
                        resourceEntity
                    );
                    continue;
                }

                if (!SystemAPI.HasComponent<ResourceNode>(resourceEntity) ||
                    !SystemAPI.TryGetComponent<GridPosition>(resourceEntity, out var position))
                {
                    ReportViolation(
                        "ResourceSpatial",
                        $"Entity ({resourceEntity.Index}:{resourceEntity.Version}) in ResourceSpatialIndex lacks ResourceNode or GridPosition component.",
                        resourceEntity
                    );
                    continue;
                }

                int2 nodePos = position.Value;
                if (!nodePos.Equals(pos))
                {
                    ReportViolation(
                        "ResourceSpatial",
                        $"Position mismatch: ResourceNode ({resourceEntity.Index}:{resourceEntity.Version}) has GridPosition ({nodePos.x}, {nodePos.y}), but is indexed at ({pos.x}, {pos.y}).",
                        resourceEntity
                    );
                }
            }
        }
        finally
        {
            kvpArray.Dispose();
        }
    }

    /// <summary>
    /// 무결성 위반 발생 시 호출하는 리포팅 메서드.
    /// 콘솔 에러 출력 없이 진단 로그 파일을 생성하고 에디터를 일시정지(Debug.Break).
    /// </summary>
    /// <param name="category">위반 항목 카테고리 (예: Item, Drone, Spatial, Building)</param>
    /// <param name="message">위반 상세 내용 (기대값 vs 실제값 등)</param>
    /// <param name="entity">위반 대상 엔티티 (선택 사항)</param>
    public void ReportViolation(string category, string message, Entity entity = default)
    {
        TotalViolationCount++;
        // ResetViolationCount와 독립적인 순번이며 세션 ID는 같은 이름의 World도 구분한다.
        string reportId = $"{_reportSessionId}_{++_reportSequence}";

        try
        {
            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"invariant_error_{timestamp}_{reportId}.txt";
            string filePath = Path.Combine(_logDirectory, fileName);

            var sb = new StringBuilder();
            sb.AppendLine("=== PlanetMiner Invariant Violation Report ===");
            sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"World: {World.Name}");
            sb.AppendLine($"Report ID: {reportId}");
            sb.AppendLine($"Frame Count: {UnityEngine.Time.frameCount}");
            sb.AppendLine($"Category: {category}");
            if (entity != Entity.Null)
            {
                sb.AppendLine($"Entity: Index={entity.Index}, Version={entity.Version}");
            }
            sb.AppendLine("Message:");
            sb.AppendLine(message);
            sb.AppendLine("==============================================");

            using (var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, Encoding.UTF8))
            {
                writer.Write(sb.ToString());
            }
        }
        catch (Exception ex)
        {
            // 파일 쓰기 예외 발생 시 최소한의 디버그 정보 보존을 위한 처리
            System.Diagnostics.Debug.WriteLine($"[InvariantReport Error] World={World.Name}, Report={reportId}, Category={category}, Entity={entity}, Message={message}. Failed to write log file: {ex.Message}");
        }

        // 에디터 일시정지
        Debug.Break();
    }
}
