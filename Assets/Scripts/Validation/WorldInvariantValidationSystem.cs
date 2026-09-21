#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Architecture V2 전용 데이터 무결성(Invariant) 검증 시스템.
/// 모든 6대 Phase의 상태 반영 및 동기화가 완전히 끝난 프레임 맨 마지막(SynchronizationGroup, OrderLast = true)에 실행됩니다.
/// 위반 감지 시 콘솔 에러 출력 없이 파일로 진단 로그를 저장하고 Debug.Break()로 에디터를 일시정지합니다.
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

    /// <summary>
    /// 무결성 위반 누적 횟수 (테스트 단언 및 진단용).
    /// </summary>
    public int TotalViolationCount { get; private set; }

    /// <summary>
    /// 무결성 위반 누적 횟수를 0으로 초기화합니다.
    /// </summary>
    public void ResetViolationCount()
    {
        TotalViolationCount = 0;
    }

    protected override void OnCreate()
    {
        base.OnCreate();
        // Logs/InvariantErrors/ 경로 설정 (프로젝트 루트 기준)
        _logDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "InvariantErrors"));
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
            beltFenceRw.ValueRW.Complete();
        }
        if (SystemAPI.TryGetSingletonRW<ItemSpatialIndexFence>(out var itemFenceRw))
        {
            itemFenceRw.ValueRW.Complete();
        }
        if (SystemAPI.TryGetSingletonRW<BuildingSpatialIndexFence>(out var buildingFenceRw))
        {
            buildingFenceRw.ValueRW.Complete();
        }
        if (SystemAPI.TryGetSingletonRW<ResourceSpatialIndexFence>(out var resourceFenceRw))
        {
            resourceFenceRw.ValueRW.Complete();
        }

        // Phase 1: Item & Spatial Invariant 검증
        ValidateItemAndSpatialInvariants();

        // Phase 1: 미소비(Unconsumed) 1회성 Request 잔류 검증
        ValidateRequestLifecycleInvariants();

        // Phase 2: Belt & Item Movement Invariant 검증
        ValidateBeltInvariants();

        // Phase 3: Storage Buffer & Item Invariant 검증
        ValidateStorageInvariants();

        // Phase 3: 미소비 확정 입출력 Decision 잔류 검증
        ValidateStorageDecisionInvariants();

        // Phase 4: Resource & Spatial Invariant 검증
        ValidateResourceSpatialInvariants();
    }

    /// <summary>
    /// Item 도메인 및 공간 인덱스(ItemSpatialIndex) 간의 양방향 정합성을 검증합니다.
    /// </summary>
    private void ValidateItemAndSpatialInvariants()
    {
        if (!SystemAPI.TryGetSingleton<ItemSpatialIndex>(out var spatialIndex) || !spatialIndex.Map.IsCreated)
        {
            return;
        }

        // 1. [정방향 검증] World Item -> ItemSpatialIndex 등록 여부 확인
        foreach (var (pos, ownership, entity) in 
                 SystemAPI.Query<RefRO<GridPosition>, RefRO<ItemOwnership>>()
                          .WithAll<ItemIdentity>()
                          .WithEntityAccess())
        {
            if (ownership.ValueRO.IsWorldItem)
            {
                int2 targetPos = pos.ValueRO.Value;
                bool foundInIndex = false;

                if (spatialIndex.TryGetFirstItem(targetPos, out Entity itemEntity, out var it))
                {
                    do
                    {
                        if (itemEntity == entity)
                        {
                            foundInIndex = true;
                            break;
                        }
                    } while (spatialIndex.TryGetNextItem(out itemEntity, ref it));
                }

                if (!foundInIndex)
                {
                    ReportViolation(
                        "ItemSpatial",
                        $"World Item ({entity.Index}:{entity.Version}) is not registered in ItemSpatialIndex at position ({targetPos.x}, {targetPos.y}). Spatial sync may be missing.",
                        entity
                    );
                }
            }
            else
            {
                // Stored Item: 보관 대상 Owner 엔티티의 실존 여부 확인 (고아 수납 아이템 방어)
                Entity owner = ownership.ValueRO.Owner;
                if (!SystemAPI.Exists(owner))
                {
                    ReportViolation(
                        "ItemOwnership",
                        $"Stored Item ({entity.Index}:{entity.Version}) references non-existent (destroyed) Owner Entity ({owner.Index}:{owner.Version}). Ownership cleanup may be missing.",
                        entity
                    );
                }
            }
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
                if (!SystemAPI.HasComponent<ItemOwnership>(itemEntity) || !SystemAPI.HasComponent<GridPosition>(itemEntity))
                {
                    ReportViolation(
                        "SpatialIndex",
                        $"Entity ({itemEntity.Index}:{itemEntity.Version}) indexed at ({pos.x}, {pos.y}) lacks ItemOwnership or GridPosition component.",
                        itemEntity
                    );
                    continue;
                }

                var ownership = SystemAPI.GetComponent<ItemOwnership>(itemEntity);
                var gridPos = SystemAPI.GetComponent<GridPosition>(itemEntity);

                // 보관(Stored) 아이템이 잘못 등록되었는지 확인
                if (!ownership.IsWorldItem)
                {
                    ReportViolation(
                        "SpatialIndex",
                        $"Stored item ({itemEntity.Index}:{itemEntity.Version}, Owner={ownership.Owner.Index}:{ownership.Owner.Version}) is erroneously registered in ItemSpatialIndex at ({pos.x}, {pos.y}).",
                        itemEntity
                    );
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
    /// 프레임 종료 시점(SynchronizationGroup)에 소비되지 않고 잔류한 1회성 Request 컴포넌트를 감시합니다.
    /// </summary>
    private void ValidateRequestLifecycleInvariants()
    {
        // 1. TransferOwnershipRequest 잔류 감시 (StateApplyGroup에서 처리 및 비활성화되었어야 함)
        foreach (var (_, entity) in 
                 SystemAPI.Query<RefRO<TransferOwnershipRequest>>()
                          .WithEntityAccess())
        {
            ReportViolation(
                "RequestLifecycle",
                $"TransferOwnershipRequest remained enabled on Entity ({entity.Index}:{entity.Version}) at the end of the frame (SynchronizationGroup). Request was not consumed in StateApplyGroup.",
                entity
            );
        }

        // 2. DestroyItemRequest 잔류 감시 (StateApplyGroup에서 처리 및 엔티티가 파괴되었어야 함)
        foreach (var (_, entity) in 
                 SystemAPI.Query<RefRO<DestroyItemRequest>>()
                          .WithEntityAccess())
        {
            ReportViolation(
                "RequestLifecycle",
                $"DestroyItemRequest remained active on Entity ({entity.Index}:{entity.Version}) at the end of the frame (SynchronizationGroup). Entity was not destroyed in StateApplyGroup.",
                entity
            );
        }
    }

    /// <summary>
    /// Phase 2 벨트 건물 및 아이템 이동 무결성(Invariant)을 검증합니다.
    /// - 1. 미소비(Unconsumed) 이동 계획(PlannedProgress) 잔류 감시
    /// - 2. 고아 벨트 아이템(벨트 없는 위치에서 활성화된 아이템) 감시
    /// - 3. 동일 벨트 타일 내 최대 수용량(4개) 초과 및 최소 간격(ItemSpacing 0.25f) 침범 감시
    /// - 4. 연속된 타일 경계 횡단 간격(Boundary Gap) 침범 감시
    /// </summary>
    private void ValidateBeltInvariants()
    {
        if (SystemAPI.TryGetSingletonRW<BeltSpatialIndexFence>(out var beltFenceRw))
        {
            beltFenceRw.ValueRW.Complete();
        }

        if (!SystemAPI.TryGetSingleton<BeltSpatialIndex>(out var beltIndex) || !beltIndex.Map.IsCreated)
        {
            return;
        }

        if (!SystemAPI.TryGetSingleton<ItemSpatialIndex>(out var itemSpatialIndex) || !itemSpatialIndex.Map.IsCreated)
        {
            return;
        }

        // 1. 미소비 이동 계획 잔류 감시 (ExecutionGroup에서 정상 소비되었는지 확인)
        foreach (var (decision, entity) in 
                 SystemAPI.Query<RefRO<BeltMovementDecision>>()
                          .WithEntityAccess())
        {
            if (decision.ValueRO.PlannedProgress > 0.0f)
            {
                ReportViolation(
                    "BeltInvariant",
                    $"BeltMovementDecision.PlannedProgress ({decision.ValueRO.PlannedProgress}) was not consumed at the end of the frame on Entity ({entity.Index}:{entity.Version}). ExecutionGroup execution may be missing.",
                    entity
                );
            }
        }

        // 2. 고아 벨트 아이템 감시 (벨트가 없는 위치인데 BeltMovementState가 활성화된 경우)
        foreach (var (pos, ownership, entity) in 
                 SystemAPI.Query<RefRO<GridPosition>, RefRO<ItemOwnership>>()
                          .WithAll<BeltMovementState>()
                          .WithEntityAccess())
        {
            if (ownership.ValueRO.IsWorldItem)
            {
                if (!beltIndex.HasBeltAt(pos.ValueRO.Value))
                {
                    ReportViolation(
                        "BeltInvariant",
                        $"Item ({entity.Index}:{entity.Version}) has active BeltMovementState at ({pos.ValueRO.Value.x}, {pos.ValueRO.Value.y}), but no belt building exists at this position.",
                        entity
                    );
                }
            }
        }

        // 3 & 4. 벨트 타일별 아이템 수용량 및 간격 침범 감시
        var beltKvpArray = beltIndex.Map.GetKeyValueArrays(Allocator.Temp);
        try
        {
            var beltMovementLookup = SystemAPI.GetComponentLookup<BeltMovementState>(true);
            var itemOwnershipLookup = SystemAPI.GetComponentLookup<ItemOwnership>(true);

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
                        if (SystemAPI.Exists(itemEntity) &&
                            itemOwnershipLookup.HasComponent(itemEntity) &&
                            itemOwnershipLookup[itemEntity].IsWorldItem &&
                            beltMovementLookup.HasComponent(itemEntity) &&
                            beltMovementLookup.IsComponentEnabled(itemEntity))
                        {
                            progressList.Add(beltMovementLookup[itemEntity].Progress);
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
                            if (SystemAPI.Exists(nextItem) &&
                                itemOwnershipLookup.HasComponent(nextItem) &&
                                itemOwnershipLookup[nextItem].IsWorldItem &&
                                beltMovementLookup.HasComponent(nextItem) &&
                                beltMovementLookup.IsComponentEnabled(nextItem))
                            {
                                float prog = beltMovementLookup[nextItem].Progress;
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
    /// Phase 3 창고(Storage) 버퍼 및 아이템 소유권(ItemOwnership) 양방향 무결성을 검증합니다.
    /// - 1. Stored Item -> Storage Buffer 정방향 검증 (소유자 실존, 버퍼 보유, 버퍼 내 아이템 엔티티 등록 및 타입 일치, 월드 컴포넌트 비활성화)
    /// - 2. Storage Buffer -> Stored Item 역방향 검증 (버퍼 내 아이템 엔티티 실존, Owner 일치, 공간 인덱스 미등록)
    /// - 3. 슬롯 범위(0 <= SlotIndex < SlotCount), 단일 품목 규칙, MaxStack 한도, StorageFilter 준수 검증
    /// </summary>
    private void ValidateStorageInvariants()
    {
        // 1. [정방향 검증] Stored Item -> Storage Buffer
        var itemIdentityLookup = SystemAPI.GetComponentLookup<ItemIdentity>(true);
        var beltMovementLookup = SystemAPI.GetComponentLookup<BeltMovementState>(true);
        var inputDecisionLookup = SystemAPI.GetComponentLookup<BuildingItemInputDecision>(true);
        var storedBufferLookup = SystemAPI.GetBufferLookup<StoredItemElement>(true);
        var productBufferLookup = SystemAPI.GetBufferLookup<ProductItemElement>(true);

        foreach (var (ownership, entity) in
                 SystemAPI.Query<RefRO<ItemOwnership>>()
                          .WithAll<ItemIdentity>()
                          .WithEntityAccess())
        {
            if (ownership.ValueRO.IsWorldItem)
            {
                continue;
            }

            Entity owner = ownership.ValueRO.Owner;

            // A. Owner 엔티티 실존 여부 확인
            if (!SystemAPI.Exists(owner))
            {
                ReportViolation(
                    "StorageInvariant",
                    $"Stored Item ({entity.Index}:{entity.Version}) references non-existent (destroyed) Owner Entity ({owner.Index}:{owner.Version}).",
                    entity
                );
                continue;
            }

            // B. Owner 엔티티의 StoredItemElement 또는 ProductItemElement 버퍼 보유 여부 확인
            bool hasStoredBuffer = storedBufferLookup.HasBuffer(owner);
            bool hasProductBuffer = productBufferLookup.HasBuffer(owner);

            if (!hasStoredBuffer && !hasProductBuffer)
            {
                ReportViolation(
                    "StorageInvariant",
                    $"Stored Item ({entity.Index}:{entity.Version}) references Owner ({owner.Index}:{owner.Version}), but Owner lacks DynamicBuffer<StoredItemElement> or DynamicBuffer<ProductItemElement>.",
                    entity
                );
                continue;
            }

            // C. 버퍼 내에 해당 아이템 엔티티가 실제로 등록되어 있는지 확인
            bool foundInBuffer = false;
            ItemTypeEnum bufferItemType = ItemTypeEnum.None;

            if (hasProductBuffer)
            {
                var prodBuffer = productBufferLookup[owner];
                for (int i = 0; i < prodBuffer.Length; i++)
                {
                    if (prodBuffer[i].ItemEntity == entity)
                    {
                        foundInBuffer = true;
                        bufferItemType = prodBuffer[i].ItemType;
                        break;
                    }
                }
            }
            
            if (!foundInBuffer && hasStoredBuffer)
            {
                var buffer = storedBufferLookup[owner];
                for (int i = 0; i < buffer.Length; i++)
                {
                    if (buffer[i].ItemEntity == entity)
                    {
                        foundInBuffer = true;
                        bufferItemType = buffer[i].ItemType;
                        break;
                    }
                }
            }

            if (!foundInBuffer)
            {
                ReportViolation(
                    "StorageInvariant",
                    $"Stored Item ({entity.Index}:{entity.Version}) specifies Owner ({owner.Index}:{owner.Version}), but is NOT found in the Owner's storage/product buffer.",
                    entity
                );
            }
            else
            {
                // D. 아이템 타입 일치 확인
                if (itemIdentityLookup.HasComponent(entity))
                {
                    var itemType = itemIdentityLookup[entity].Type;
                    if (bufferItemType != itemType)
                    {
                        ReportViolation(
                            "StorageInvariant",
                            $"Item type mismatch: Item entity ({entity.Index}:{entity.Version}) is {itemType}, but buffer in Storage ({owner.Index}:{owner.Version}) records {bufferItemType}.",
                            entity
                        );
                    }
                }
            }

            // E. 보관된 아이템의 월드 시뮬레이션 컴포넌트 비활성화 확인
            if (beltMovementLookup.HasComponent(entity) && beltMovementLookup.IsComponentEnabled(entity))
            {
                ReportViolation(
                    "StorageInvariant",
                    $"Stored Item ({entity.Index}:{entity.Version}) inside Storage ({owner.Index}:{owner.Version}) has enabled BeltMovementState.",
                    entity
                );
            }

            if (inputDecisionLookup.HasComponent(entity) && inputDecisionLookup.IsComponentEnabled(entity))
            {
                ReportViolation(
                    "StorageInvariant",
                    $"Stored Item ({entity.Index}:{entity.Version}) inside Storage ({owner.Index}:{owner.Version}) has enabled BuildingItemInputDecision.",
                    entity
                );
            }
        }

        // 2. [역방향 검증] Storage Buffer -> Stored Item & 슬롯/스택/필터 세부 검증
        ItemRegistry itemRegistry = default;
        bool hasItemRegistry = SystemAPI.HasSingleton<ItemRegistry>();
        if (hasItemRegistry)
        {
            itemRegistry = SystemAPI.GetSingleton<ItemRegistry>();
        }

        bool hasSpatialIndex = SystemAPI.TryGetSingleton<ItemSpatialIndex>(out var itemSpatialIndex);
        var itemOwnershipLookup = SystemAPI.GetComponentLookup<ItemOwnership>(true);
        var storageFilterLookup = SystemAPI.GetComponentLookup<StorageFilter>(true);

        foreach (var (storage, buffer, storageEntity) in
                 SystemAPI.Query<RefRO<Storage>, DynamicBuffer<StoredItemElement>>()
                          .WithEntityAccess())
        {
            int slotCount = storage.ValueRO.SlotCount;
            bool hasFilter = storageFilterLookup.HasComponent(storageEntity);
            StorageFilter filter = hasFilter ? storageFilterLookup[storageEntity] : default;

            var slotTracker = new NativeParallelHashMap<int, int2>(math.max(16, slotCount), Allocator.Temp);

            for (int i = 0; i < buffer.Length; i++)
            {
                StoredItemElement element = buffer[i];
                Entity itemEntity = element.ItemEntity;

                // A. 버퍼 내 아이템 엔티티 실존 여부 확인
                if (!SystemAPI.Exists(itemEntity))
                {
                    ReportViolation(
                        "StorageInvariant",
                        $"Storage ({storageEntity.Index}:{storageEntity.Version}) buffer contains non-existent or destroyed ItemEntity ({itemEntity.Index}:{itemEntity.Version}).",
                        storageEntity
                    );
                    continue;
                }

                // B. 아이템의 ItemOwnership 보유 및 Owner 일치 확인
                if (!itemOwnershipLookup.HasComponent(itemEntity))
                {
                    ReportViolation(
                        "StorageInvariant",
                        $"ItemEntity ({itemEntity.Index}:{itemEntity.Version}) in Storage ({storageEntity.Index}:{storageEntity.Version}) lacks ItemOwnership component.",
                        storageEntity
                    );
                }
                else
                {
                    var itemOwnership = itemOwnershipLookup[itemEntity];
                    if (itemOwnership.IsWorldItem || itemOwnership.Owner != storageEntity)
                    {
                        ReportViolation(
                            "StorageInvariant",
                            $"Ownership mismatch: Item ({itemEntity.Index}:{itemEntity.Version}) in Storage ({storageEntity.Index}:{storageEntity.Version}) has Owner ({itemOwnership.Owner.Index}:{itemOwnership.Owner.Version}, IsWorldItem={itemOwnership.IsWorldItem}).",
                            storageEntity
                        );
                    }
                }

                // C. 보관된 아이템이 ItemSpatialIndex에 잘못 등록되어 있는지 확인
                if (hasSpatialIndex && itemSpatialIndex.Map.IsCreated && SystemAPI.HasComponent<GridPosition>(itemEntity))
                {
                    int2 gridPos = SystemAPI.GetComponent<GridPosition>(itemEntity).Value;
                    if (itemSpatialIndex.TryGetFirstItem(gridPos, out Entity foundItem, out var it))
                    {
                        do
                        {
                            if (foundItem == itemEntity)
                            {
                                ReportViolation(
                                    "StorageInvariant",
                                    $"Stored item ({itemEntity.Index}:{itemEntity.Version}) in Storage ({storageEntity.Index}:{storageEntity.Version}) is erroneously registered in ItemSpatialIndex at ({gridPos.x}, {gridPos.y}).",
                                    storageEntity
                                );
                                break;
                            }
                        } while (itemSpatialIndex.TryGetNextItem(out foundItem, ref it));
                    }
                }

                // D. 슬롯 범위 검증 (0 <= SlotIndex < SlotCount)
                if (element.SlotIndex < 0 || element.SlotIndex >= slotCount)
                {
                    ReportViolation(
                        "StorageInvariant",
                        $"Storage ({storageEntity.Index}:{storageEntity.Version}) element has invalid SlotIndex {element.SlotIndex} (Capacity: {slotCount}).",
                        storageEntity
                    );
                    continue;
                }

                // E. 필터 준수 검증
                if (hasFilter && !filter.IsItemAllowed(element.ItemType))
                {
                    ReportViolation(
                        "StorageInvariant",
                        $"Storage ({storageEntity.Index}:{storageEntity.Version}) with filter {filter.Mode} contains disallowed item type {element.ItemType}.",
                        storageEntity
                    );
                }

                // F. 슬롯 단일 품목 규칙 및 누적 수량 추적
                if (slotTracker.TryGetValue(element.SlotIndex, out int2 trackedData))
                {
                    ItemTypeEnum firstType = (ItemTypeEnum)trackedData.x;
                    int count = trackedData.y + 1;

                    if (firstType != element.ItemType)
                    {
                        ReportViolation(
                            "StorageInvariant",
                            $"Slot pollution in Storage ({storageEntity.Index}:{storageEntity.Version}) Slot {element.SlotIndex}: mixed item types ({firstType} and {element.ItemType}).",
                            storageEntity
                        );
                    }

                    slotTracker[element.SlotIndex] = new int2(trackedData.x, count);
                }
                else
                {
                    slotTracker.Add(element.SlotIndex, new int2((int)element.ItemType, 1));
                }
            }

            // G. 슬롯 스택 상한 초과 검증
            var slotKvpArray = slotTracker.GetKeyValueArrays(Allocator.Temp);
            for (int s = 0; s < slotKvpArray.Length; s++)
            {
                int slotIdx = slotKvpArray.Keys[s];
                int2 data = slotKvpArray.Values[s];
                ItemTypeEnum slotItemType = (ItemTypeEnum)data.x;
                int count = data.y;

                int maxStack = (hasItemRegistry && itemRegistry.Value.IsCreated)
                    ? itemRegistry.Value.Value.GetMaxStack(slotItemType)
                    : 50;

                if (count > maxStack)
                {
                    ReportViolation(
                        "StorageInvariant",
                        $"Slot stack limit exceeded in Storage ({storageEntity.Index}:{storageEntity.Version}) Slot {slotIdx}: {count} items of {slotItemType} (MaxStack: {maxStack}).",
                        storageEntity
                    );
                }
            }
            slotKvpArray.Dispose();
            slotTracker.Dispose();
        }

        // 3. [역방향 검증] Product Buffer -> Product Item & 슬롯/스택 세부 검증
        foreach (var (prodBuffer, producerEntity) in
                 SystemAPI.Query<DynamicBuffer<ProductItemElement>>()
                          .WithEntityAccess())
        {
            var slotTracker = new NativeParallelHashMap<int, int2>(16, Allocator.Temp);

            for (int i = 0; i < prodBuffer.Length; i++)
            {
                ProductItemElement element = prodBuffer[i];
                Entity itemEntity = element.ItemEntity;

                // A. 버퍼 내 아이템 엔티티 실존 여부 확인
                if (!SystemAPI.Exists(itemEntity))
                {
                    ReportViolation(
                        "StorageInvariant",
                        $"Producer ({producerEntity.Index}:{producerEntity.Version}) product buffer contains non-existent or destroyed ItemEntity ({itemEntity.Index}:{itemEntity.Version}).",
                        producerEntity
                    );
                    continue;
                }

                // B. 아이템의 ItemOwnership 보유 및 Owner 일치 확인
                if (!itemOwnershipLookup.HasComponent(itemEntity))
                {
                    ReportViolation(
                        "StorageInvariant",
                        $"ItemEntity ({itemEntity.Index}:{itemEntity.Version}) in Producer ({producerEntity.Index}:{producerEntity.Version}) product buffer lacks ItemOwnership component.",
                        producerEntity
                    );
                }
                else
                {
                    var itemOwnership = itemOwnershipLookup[itemEntity];
                    if (itemOwnership.IsWorldItem || itemOwnership.Owner != producerEntity)
                    {
                        ReportViolation(
                            "StorageInvariant",
                            $"Ownership mismatch: Item ({itemEntity.Index}:{itemEntity.Version}) in Producer ({producerEntity.Index}:{producerEntity.Version}) product buffer has Owner ({itemOwnership.Owner.Index}:{itemOwnership.Owner.Version}, IsWorldItem={itemOwnership.IsWorldItem}).",
                            producerEntity
                        );
                    }
                }

                // C. 보관된 아이템이 ItemSpatialIndex에 잘못 등록되어 있는지 확인
                if (hasSpatialIndex && itemSpatialIndex.Map.IsCreated && SystemAPI.HasComponent<GridPosition>(itemEntity))
                {
                    int2 gridPos = SystemAPI.GetComponent<GridPosition>(itemEntity).Value;
                    if (itemSpatialIndex.TryGetFirstItem(gridPos, out Entity foundItem, out var it))
                    {
                        do
                        {
                            if (foundItem == itemEntity)
                            {
                                ReportViolation(
                                    "StorageInvariant",
                                    $"Stored product item ({itemEntity.Index}:{itemEntity.Version}) in Producer ({producerEntity.Index}:{producerEntity.Version}) is erroneously registered in ItemSpatialIndex at ({gridPos.x}, {gridPos.y}).",
                                    producerEntity
                                );
                                break;
                            }
                        } while (itemSpatialIndex.TryGetNextItem(out foundItem, ref it));
                    }
                }

                // D. 슬롯 인덱스 음수 검증
                if (element.SlotIndex < 0)
                {
                    ReportViolation(
                        "StorageInvariant",
                        $"Producer ({producerEntity.Index}:{producerEntity.Version}) product element has negative SlotIndex {element.SlotIndex}.",
                        producerEntity
                    );
                    continue;
                }

                // E. 슬롯 단일 품목 규칙 및 누적 수량 추적
                if (slotTracker.TryGetValue(element.SlotIndex, out int2 trackedData))
                {
                    ItemTypeEnum firstType = (ItemTypeEnum)trackedData.x;
                    int count = trackedData.y + 1;

                    if (firstType != element.ItemType)
                    {
                        ReportViolation(
                            "StorageInvariant",
                            $"Slot pollution in Producer ({producerEntity.Index}:{producerEntity.Version}) Product Slot {element.SlotIndex}: mixed item types ({firstType} and {element.ItemType}).",
                            producerEntity
                        );
                    }

                    slotTracker[element.SlotIndex] = new int2(trackedData.x, count);
                }
                else
                {
                    slotTracker.Add(element.SlotIndex, new int2((int)element.ItemType, 1));
                }
            }

            // F. 슬롯 스택 상한 초과 검증 (1스택 한도)
            var slotKvpArray = slotTracker.GetKeyValueArrays(Allocator.Temp);
            for (int s = 0; s < slotKvpArray.Length; s++)
            {
                int slotIdx = slotKvpArray.Keys[s];
                int2 data = slotKvpArray.Values[s];
                ItemTypeEnum slotItemType = (ItemTypeEnum)data.x;
                int count = data.y;

                int maxStack = (hasItemRegistry && itemRegistry.Value.IsCreated)
                    ? itemRegistry.Value.Value.GetMaxStack(slotItemType)
                    : 50;

                if (count > maxStack)
                {
                    ReportViolation(
                        "StorageInvariant",
                        $"Product slot stack limit exceeded in Producer ({producerEntity.Index}:{producerEntity.Version}) Slot {slotIdx}: {count} items of {slotItemType} (MaxStack: {maxStack}).",
                        producerEntity
                    );
                }
            }
            slotKvpArray.Dispose();
            slotTracker.Dispose();
        }
    }

    /// <summary>
    /// 프레임 종료 시점(SynchronizationGroup)에 소비되지 않고 잔류한 확정 입출력 Decision을 감시합니다.
    /// StateApplyGroup에서 정상적으로 처리되었어야 할 확정 결정이 남아있으면 상태 전이 누락 버그입니다.
    /// </summary>
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
                    $"BuildingItemInputDecision remained confirmed (CanDeposit=true, TargetSlotIndex={decision.ValueRO.TargetSlotIndex}) at the end of the frame on Item ({entity.Index}:{entity.Version}). Decision was not consumed in StateApplyGroup.",
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
                    $"BuildingItemOutputDecision remained confirmed (CanOutput=true, ItemToOutput={decision.ValueRO.ItemToOutput.Index}:{decision.ValueRO.ItemToOutput.Version}) at the end of the frame on Building ({entity.Index}:{entity.Version}). Decision was not consumed in StateApplyGroup.",
                    entity
                );
            }
        }
    }

    /// <summary>
    /// Phase 4 자원 노드(ResourceNode) 및 자원 공간 인덱스(ResourceSpatialIndex) 정합성을 검증합니다.
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

                if (!SystemAPI.HasComponent<ResourceNode>(resourceEntity) || !SystemAPI.HasComponent<GridPosition>(resourceEntity))
                {
                    ReportViolation(
                        "ResourceSpatial",
                        $"Entity ({resourceEntity.Index}:{resourceEntity.Version}) in ResourceSpatialIndex lacks ResourceNode or GridPosition component.",
                        resourceEntity
                    );
                    continue;
                }

                int2 nodePos = SystemAPI.GetComponent<GridPosition>(resourceEntity).Value;
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
    /// 콘솔 에러 출력 없이 진단 로그 파일을 생성하고 에디터를 일시정지(Debug.Break)합니다.
    /// </summary>
    /// <param name="category">위반 항목 카테고리 (예: Item, Drone, Spatial, Building)</param>
    /// <param name="message">위반 상세 내용 (기대값 vs 실제값 등)</param>
    /// <param name="entity">위반 대상 엔티티 (선택 사항)</param>
    public void ReportViolation(string category, string message, Entity entity = default)
    {
        TotalViolationCount++;

        try
        {
            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"invariant_error_{timestamp}.txt";
            string filePath = Path.Combine(_logDirectory, fileName);

            var sb = new StringBuilder();
            sb.AppendLine("=== PlanetMiner Invariant Violation Report ===");
            sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"Frame Count: {UnityEngine.Time.frameCount}");
            sb.AppendLine($"Category: {category}");
            if (entity != Entity.Null)
            {
                sb.AppendLine($"Entity: Index={entity.Index}, Version={entity.Version}");
            }
            sb.AppendLine("Message:");
            sb.AppendLine(message);
            sb.AppendLine("==============================================");

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            // 파일 쓰기 예외 발생 시 최소한의 디버그 정보 보존을 위한 처리
            System.Diagnostics.Debug.WriteLine($"[InvariantReport Error] Failed to write log file: {ex.Message}");
        }

        // 에디터 일시정지
        Debug.Break();
    }
}
#endif
