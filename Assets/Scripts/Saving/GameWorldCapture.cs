using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>완료 틱의 원본 또는 이전 도구가 복원한 World를 파일용 값으로 복사한다.</summary>
internal static class GameWorldCapture
{
    public static GameWorldSnapshot Capture(EntityManager manager)
    {
        manager.CompleteAllTrackedJobs();
        ValidateSupportedState(manager);

        using var itemRegistry = manager.CreateEntityQuery(typeof(ItemRegistry), typeof(ItemConfigElement));
        var items = manager.GetBuffer<ItemConfigElement>(itemRegistry.GetSingletonEntity(), true);
        var snapshot = new GameWorldSnapshot();
        CaptureWorld(manager, snapshot.World, items);
        snapshot.Research = CaptureResearch(manager);

        using var occupiedQuery = new EntityQueryBuilder(Allocator.Temp);
        using var buildings = occupiedQuery.WithAny<BuildingIdentity, ConstructionSite>()
            .WithOptions(EntityQueryOptions.IncludeDisabledEntities).Build(manager);
        using var buildingEntities = buildings.ToEntityArray(Allocator.Temp);
        var savedIds = new Dictionary<Entity, string>(buildingEntities.Length);
        var activeBelts = new Dictionary<int2, DirectionEnum>();
        int buildingOrdinal = 0;
        int siteOrdinal = 0;
        foreach (Entity entity in buildingEntities)
        {
            if (manager.HasComponent<ConstructionSite>(entity))
            {
                savedIds.Add(entity, SaveId("site-", siteOrdinal++));
                continue;
            }
            savedIds.Add(entity, SaveId("building-", buildingOrdinal++));
            if (manager.HasComponent<BeltComponent>(entity) && !manager.HasComponent<Disabled>(entity))
            {
                activeBelts.Add(manager.GetComponentData<GridPosition>(entity).Value, manager.GetComponentData<Direction>(entity).dir);
            }
        }

        using var bootstrap = manager.CreateEntityQuery(typeof(GameBootstrapState));
        if (!bootstrap.TryGetSingleton(out GameBootstrapState start) ||
            !savedIds.TryGetValue(start.MainFacility, out snapshot.World.MainFacilityId))
        {
            throw new InvalidOperationException("주 시설 초기화가 끝난 뒤 저장할 수 있습니다.");
        }

        foreach (Entity entity in buildingEntities)
        {
            if (manager.HasComponent<ConstructionSite>(entity))
            {
                snapshot.ConstructionSites.Add(GameConstructionSave.Capture(manager, entity, savedIds[entity], items));
            }
            else
            {
                snapshot.Buildings.Add(CaptureBuilding(manager, entity, savedIds, items));
            }
        }

        using var worldItems = RuntimeQuery<ItemIdentity>(manager);
        using var worldItemEntities = worldItems.ToEntityArray(Allocator.Temp);
        foreach (Entity entity in worldItemEntities)
        {
            if (manager.HasComponent<Disabled>(entity) || manager.IsComponentEnabled<DestroyItemRequest>(entity))
            {
                throw new InvalidOperationException("비활성 또는 삭제 대기 중인 아이템이 있어 저장할 수 없습니다.");
            }
            var movement = manager.GetComponentData<BeltMovementState>(entity);
            int2 cell = manager.GetComponentData<GridPosition>(entity).Value;
            bool isOnBelt = manager.IsComponentEnabled<BeltMovementState>(entity);
            string id = SaveId("item-", snapshot.WorldItems.Count);
            savedIds.Add(entity, id);
            snapshot.WorldItems.Add(new SavedWorldItem
            {
                Id = id,
                Cell = GameWorldSnapshot.WriteCell(cell),
                ItemId = ReadItemId(items, manager.GetComponentData<ItemIdentity>(entity).Type),
                BeltProgress = isOnBelt ? movement.Progress : null,
                Direction = manager.HasComponent<Direction>(entity) ? manager.GetComponentData<Direction>(entity).dir : null
            });
        }
        GameWorldSnapshot.ValidateBeltSpacing(activeBelts, snapshot.WorldItems);
        snapshot.DroneLogistics = GameDroneSave.Capture(manager, savedIds);
        return snapshot;
    }

    private static void CaptureWorld(EntityManager manager, SavedWorld world, DynamicBuffer<ItemConfigElement> items)
    {
        using var generation = manager.CreateEntityQuery(typeof(ResourceGenerationSettings));
        world.Seed = generation.GetSingleton<ResourceGenerationSettings>().WorldSeed;
        using var placementSequence = manager.CreateEntityQuery(typeof(BuildingPlacementReceiptSequence));
        var sequence = placementSequence.GetSingleton<BuildingPlacementReceiptSequence>();
        world.PlacementTick = sequence.CurrentTick;
        world.NextPlacementReceipt = sequence.NextValue;
        using var taskSequence = manager.CreateEntityQuery(typeof(DroneTaskSequence));
        world.NextDroneTaskSequence = taskSequence.GetSingleton<DroneTaskSequence>().NextValue;

        using var chunks = manager.CreateEntityQuery(typeof(GeneratedChunkTracker));
        Entity trackerEntity = chunks.GetSingletonEntity();
        var tracker = manager.GetComponentData<GeneratedChunkTracker>(trackerEntity);
        var generated = new HashSet<int2>();
        foreach (int2 coordinate in tracker.Map)
        {
            generated.Add(coordinate);
        }
        // 자원과 같은 EndCommand에서 공개된 완료 알림은 Tracker Map에 반영되기 전에도 완료다.
        foreach (var completed in manager.GetBuffer<GeneratedChunkCompletedElement>(trackerEntity, true))
        {
            generated.Add(completed.ChunkCoord);
        }
        foreach (int2 coordinate in generated)
        {
            world.GeneratedChunks.Add(GameWorldSnapshot.WriteCell(coordinate));
        }

        var pending = new HashSet<int2>();
        foreach (int2 coordinate in tracker.Pending)
        {
            if (!generated.Contains(coordinate))
            {
                pending.Add(coordinate);
            }
        }
        using var queue = manager.CreateEntityQuery(typeof(ChunkLoadRequestQueue));
        if (queue.TryGetSingletonEntity<ChunkLoadRequestQueue>(out Entity queueEntity))
        {
            foreach (var request in manager.GetBuffer<ChunkLoadRequestElement>(queueEntity, true))
            {
                if (!generated.Contains(request.ChunkCoord))
                {
                    pending.Add(request.ChunkCoord);
                }
            }
        }
        foreach (int2 coordinate in pending)
        {
            world.PendingChunks.Add(GameWorldSnapshot.WriteCell(coordinate));
        }

        using var resources = RuntimeQuery<ResourceNode>(manager);
        using var resourceEntities = resources.ToEntityArray(Allocator.Temp);
        foreach (Entity entity in resourceEntities)
        {
            if (manager.HasComponent<Disabled>(entity))
            {
                throw new InvalidOperationException("비활성 자원 노드가 있어 저장할 수 없습니다.");
            }
            var resource = manager.GetComponentData<ResourceNode>(entity);
            world.ResourceNodes.Add(new SavedResourceNode
            {
                Cell = GameWorldSnapshot.WriteCell(manager.GetComponentData<GridPosition>(entity).Value),
                ItemId = ReadItemId(items, resource.ResourceType),
                RemainingAmount = resource.Amount
            });
        }
    }

    private static SavedBuilding CaptureBuilding(EntityManager manager, Entity entity,
        Dictionary<Entity, string> savedIds, DynamicBuffer<ItemConfigElement> items)
    {
        var stamp = manager.GetComponentData<PlacementStamp>(entity);
        var building = new SavedBuilding
        {
            Id = savedIds[entity],
            BuildingTypeId = manager.GetComponentData<BuildingIdentity>(entity).BuildingId,
            Origin = GameWorldSnapshot.WriteCell(manager.GetComponentData<GridPosition>(entity).Value),
            Direction = manager.GetComponentData<Direction>(entity).dir,
            Stamp = new SavedPlacementStamp { Tick = stamp.Tick, Receipt = stamp.ReceiptSequence, Order = stamp.Order },
            Disabled = manager.HasComponent<Disabled>(entity),
            Production = CaptureProduction(manager, entity, items)
        };
        if (manager.HasComponent<Storage>(entity))
        {
            building.Storage = CaptureStorage(manager, entity, items);
        }
        if (manager.HasComponent<SplitterRoutingState>(entity))
        {
            var state = manager.GetComponentData<SplitterRoutingState>(entity);
            savedIds.TryGetValue(state.InputBelt, out string beltId);
            building.Routing = new SavedRouting { BeltId = beltId, ForwardDirection = state.ForwardDirection, Cursor = state.OutputCursor };
        }
        if (manager.HasComponent<MergerRoutingState>(entity))
        {
            var state = manager.GetComponentData<MergerRoutingState>(entity);
            savedIds.TryGetValue(state.OutputBelt, out string beltId);
            building.Routing = new SavedRouting { BeltId = beltId, ForwardDirection = state.ForwardDirection, Cursor = state.InputCursor };
        }
        return building;
    }

    private static SavedStorage CaptureStorage(EntityManager manager, Entity entity, DynamicBuffer<ItemConfigElement> items)
    {
        var storage = new SavedStorage();
        CaptureFilter(manager.GetComponentData<StorageFilter>(entity), storage, items);
        var slots = manager.GetBuffer<StorageSlotElement>(entity, true);
        for (int index = 0; index < slots.Length; index++)
        {
            var slot = slots[index];
            if (slot.Quantity == 0)
            {
                continue;
            }
            storage.Slots.Add(new SavedStorageSlot
            {
                Slot = index,
                ItemId = ReadItemId(items, slot.ItemType),
                Quantity = slot.Quantity
            });
        }
        return storage;
    }

    internal static void CaptureFilter(StorageFilter filter, SavedStorageFilter saved, DynamicBuffer<ItemConfigElement> items)
    {
        saved.FilterMode = filter.Mode;
        for (int index = 0; index < FixedBitSet.Capacity; index++)
        {
            if (filter.Mask.IsSet((byte)index))
            {
                saved.FilterItemIds.Add(ReadItemId(items, (ItemTypeEnum)index));
            }
        }
    }

    private static SavedProduction CaptureProduction(EntityManager manager, Entity entity, DynamicBuffer<ItemConfigElement> items)
    {
        var production = new SavedProduction();
        if (manager.HasComponent<MinerState>(entity))
        {
            production.MiningProgress = manager.GetComponentData<MinerState>(entity).Progress;
        }
        if (manager.HasComponent<CrafterState>(entity))
        {
            var state = manager.GetComponentData<CrafterState>(entity);
            production.Crafting = new SavedCrafting
            {
                RecipeId = state.SelectedRecipeId, Progress = state.Progress,
                IsActive = state.IsCraftingActive, Status = state.Status
            };
        }
        if (manager.HasComponent<FuelEnergyState>(entity))
        {
            var fuel = manager.GetComponentData<FuelEnergyState>(entity);
            production.RemainingFuelEnergy = fuel.RemainingEnergy;
            production.ActiveFuelCapacity = fuel.ActiveFuelCapacity;
        }
        if (manager.HasComponent<ResearchCycleState>(entity))
        {
            var cycle = manager.GetComponentData<ResearchCycleState>(entity);
            var savedCycle = new SavedResearchCycle
            {
                ResearchId = cycle.ResearchId, ElapsedTime = cycle.ElapsedTime,
                InputConfigured = manager.IsComponentEnabled<ResearchInputConfigured>(entity)
            };
            foreach (var recovery in manager.GetBuffer<ResearchRecoveryElement>(entity, true))
            {
                savedCycle.Recovery.Add(new SavedStorageSlot
                {
                    Slot = recovery.SlotIndex, ItemId = ReadItemId(items, recovery.ItemType), Quantity = recovery.Quantity
                });
            }
            production.ResearchCycle = savedCycle;
        }
        if (manager.HasBuffer<ProductSlotElement>(entity))
        {
            production.Products = new List<SavedStorageSlot>();
            foreach (var product in manager.GetBuffer<ProductSlotElement>(entity, true))
            {
                production.Products.Add(new SavedStorageSlot
                {
                    Slot = product.SlotIndex, ItemId = ReadItemId(items, product.ItemType), Quantity = product.Quantity
                });
            }
        }
        return production.MiningProgress.HasValue || production.Crafting != null || production.RemainingFuelEnergy.HasValue ||
            production.ResearchCycle != null || production.Products != null ? production : null;
    }

    private static SavedResearch CaptureResearch(EntityManager manager)
    {
        using var research = manager.CreateEntityQuery(typeof(ResearchState), typeof(ResearchProgressElement));
        Entity registry = research.GetSingletonEntity();
        var saved = new SavedResearch { ActiveResearchId = manager.GetComponentData<ResearchState>(registry).ActiveResearchId };
        foreach (var entry in manager.GetBuffer<ResearchProgressElement>(registry, true))
        {
            saved.Progress.Add(new SavedResearchProgress
            {
                ResearchId = entry.ResearchId, Progress = entry.Progress, IsCompleted = entry.IsCompleted
            });
        }
        return saved;
    }

    private static void ValidateSupportedState(EntityManager manager)
    {
        if (SimulationFailureUtility.HasFatalError(manager))
        {
            throw new InvalidOperationException("게임 틱이 중단되어 저장할 수 없습니다.");
        }
        RejectNonemptyBuffer<ProductResult>(manager);
        RejectNonemptyBuffer<ResearchContributionElement>(manager);

        using var pendingSettings = manager.CreateEntityQuery(new EntityQueryDesc
        {
            Any = new ComponentType[] { typeof(ChangeCrafterRecipeRequest), typeof(ChangeStorageFilterRequest), typeof(ResearchSelectionRequest) }
        });
        if (!pendingSettings.IsEmptyIgnoreFilter)
        {
            throw new InvalidOperationException("설정 변경이 적용된 뒤 저장할 수 있습니다.");
        }

    }

    private static void RejectNonemptyBuffer<T>(EntityManager manager) where T : unmanaged, IBufferElementData
    {
        using var query = RuntimeQuery<T>(manager);
        using var entities = query.ToEntityArray(Allocator.Temp);
        foreach (Entity entity in entities)
        {
            if (manager.GetBuffer<T>(entity, true).Length != 0)
            {
                throw new InvalidOperationException("정산 또는 생산 상태가 남아 있어 현재 저장 범위에서 저장할 수 없습니다.");
            }
        }
    }

    private static EntityQuery RuntimeQuery<T>(EntityManager manager) where T : unmanaged
    {
        using var builder = new EntityQueryBuilder(Allocator.Temp);
        return builder.WithAll<T>().WithOptions(EntityQueryOptions.IncludeDisabledEntities).Build(manager);
    }

    internal static int ReadItemId(DynamicBuffer<ItemConfigElement> items, ItemTypeEnum itemType)
    {
        if (ItemRegistry.GetMaxStack(items, itemType) <= 0)
        {
            throw new InvalidOperationException("등록되지 않은 품목을 저장할 수 없습니다.");
        }
        return items[(int)itemType].ItemId;
    }

    private static string SaveId(string prefix, int ordinal) => prefix + (ordinal + 1).ToString(CultureInfo.InvariantCulture);
}
