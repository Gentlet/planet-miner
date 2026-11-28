using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>게임 틱을 대기 중인 복원 World에 원본을 설치한다. 호출자가 성공한 World만 공개한다.</summary>
internal static class GameWorldRestore
{
    public static void Apply(World world, GameWorldSnapshot snapshot)
    {
        var manager = world.EntityManager;
        using var gate = manager.CreateEntityQuery(typeof(GameSessionStartPending));
        using var bootstrap = manager.CreateEntityQuery(typeof(GameBootstrapState));
        if (gate.IsEmptyIgnoreFilter || !bootstrap.IsEmptyIgnoreFilter)
        {
            throw new InvalidOperationException("복원은 시작 대기 중인 빈 World에서 한 번 적용해야 합니다.");
        }

        var savedWorld = snapshot.World;
        using var generation = manager.CreateEntityQuery(typeof(ResourceGenerationSettings));
        var generationSettings = generation.GetSingleton<ResourceGenerationSettings>();
        if (!WorldGenerationConfigLoader.TryValidateWorldSettings(savedWorld.Seed, generationSettings.InitialChunkSize, out string error))
        {
            throw new InvalidDataException(error);
        }
        if (savedWorld.PlacementTick == 0 || savedWorld.PlacementTick == ulong.MaxValue ||
            savedWorld.NextPlacementReceipt == 0 || savedWorld.NextPlacementReceipt == ulong.MaxValue ||
            savedWorld.NextDroneTaskSequence == 0 || savedWorld.NextDroneTaskSequence == ulong.MaxValue)
        {
            throw new InvalidDataException("저장 파일의 순번이 유효하지 않습니다.");
        }

        var generatedChunks = ReadChunks(savedWorld.GeneratedChunks);
        var pendingChunks = ReadChunks(savedWorld.PendingChunks);
        if (generatedChunks.Overlaps(pendingChunks))
        {
            throw new InvalidDataException("이미 완료된 청크가 생성 대기에도 있습니다.");
        }

        var itemTypes = ItemConfigInitSystem.ReadRegisteredItemIds(manager);
        // 해금 원본을 복원해야 저장한 레시피와 현장 설정의 자격을 검사할 수 있다.
        GameProductionRestore.RestoreResearchProgress(manager, snapshot.Research, snapshot.IsLegacyFormat);
        var occupied = new HashSet<int2>();
        var taskTargets = new Dictionary<string, int>(StringComparer.Ordinal);
        Dictionary<string, SavedBuilding> savedBuildings;
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        try
        {
            savedBuildings = RecordBuildings(ref ecb, manager, snapshot, occupied, taskTargets);
            GameConstructionSave.Record(ref ecb, manager, snapshot.ConstructionSites, occupied, itemTypes, taskTargets);
            RecordResources(ref ecb, manager, savedWorld.ResourceNodes, generatedChunks, itemTypes);
            RecordWorldItems(ref ecb, manager, snapshot, itemTypes, taskTargets);
            ecb.Playback(manager);
        }
        finally
        {
            ecb.Dispose();
        }

        RestoreBuildingState(manager, savedBuildings, itemTypes, savedWorld.MainFacilityId, snapshot.IsLegacyFormat);
        var restoredTargets = ResolveTargetsAndRemoveIndices(manager, taskTargets);
        using var droneCommands = new EntityCommandBuffer(Allocator.Temp);
        if (snapshot.PassiveDroneTasks != null)
        {
            GamePassiveDroneTaskSave.Record(manager, droneCommands, snapshot.PassiveDroneTasks,
                restoredTargets, itemTypes, savedWorld.NextDroneTaskSequence);
        }
        if (snapshot.DroneLogistics != null)
        {
            GameDroneSave.Record(manager, droneCommands, snapshot.DroneLogistics, restoredTargets);
        }
        droneCommands.Playback(manager);

        // 프리팹을 읽은 SubScene 수명에서 실물을 분리한 뒤 첫 틱이 읽을 인덱스를 만든다.
        using var pendingDetach = manager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<PendingPrefabSceneDetach>() },
            Options = EntityQueryOptions.IncludeDisabledEntities
        });
        RuntimePrefabLifecycleUtility.DetachSceneOwnership(manager, pendingDetach);
        generationSettings.WorldSeed = savedWorld.Seed;
        manager.SetComponentData(generation.GetSingletonEntity(), generationSettings);
        RestoreChunkProgress(manager, generatedChunks, pendingChunks);
        RestoreSequences(manager, savedWorld);
        GameDroneSaveValidation.Validate(manager);
        world.GetExistingSystemManaged<SynchronizationGroup>().Update();
        manager.CompleteAllTrackedJobs();
    }

    // 첫 재생 뒤 실제 컴포넌트로 드론 참조를 검사할 수 있도록 복원 중에만 순번을 부착한다.
    private struct RestoreTargetIndex : IComponentData
    {
        public int Value;
    }

    internal static bool TryRecordTarget(EntityCommandBuffer ecb, Dictionary<string, int> targetIndices, string id, Entity entity)
    {
        int index = targetIndices.Count;
        if (!targetIndices.TryAdd(id, index))
        {
            return false;
        }

        ecb.AddComponent(entity, new RestoreTargetIndex { Value = index });
        return true;
    }

    private static Dictionary<string, Entity> ResolveTargetsAndRemoveIndices(EntityManager manager, Dictionary<string, int> targetIndices)
    {
        using var query = manager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<RestoreTargetIndex>() },
            Options = EntityQueryOptions.IncludeDisabledEntities
        });
        using var entities = query.ToEntityArray(Allocator.Temp);
        var byIndex = new Entity[targetIndices.Count];
        foreach (var entity in entities)
        {
            byIndex[manager.GetComponentData<RestoreTargetIndex>(entity).Value] = entity;
        }

        var targets = new Dictionary<string, Entity>(targetIndices.Count, StringComparer.Ordinal);
        foreach (var entry in targetIndices)
        {
            targets.Add(entry.Key, byIndex[entry.Value]);
        }

        manager.RemoveComponent<RestoreTargetIndex>(query);
        return targets;
    }

    private static HashSet<int2> ReadChunks(List<int[]> coordinates)
    {
        var result = new HashSet<int2>();
        foreach (int[] entry in coordinates)
        {
            int2 cell = GameWorldSnapshot.ReadCell(entry);
            if (!ChunkUtility.IsChunkCoordinateRepresentable(cell) || !result.Add(cell))
            {
                throw new InvalidDataException("청크 위치가 범위를 벗어나거나 중복됩니다.");
            }
        }
        return result;
    }

    private static Dictionary<string, SavedBuilding> RecordBuildings(ref EntityCommandBuffer ecb,
        EntityManager manager, GameWorldSnapshot snapshot, HashSet<int2> occupied,
        Dictionary<string, int> taskTargets)
    {
        using var configQuery = manager.CreateEntityQuery(typeof(BuildingConfig), typeof(BuildingConfigElement));
        var configs = manager.GetBuffer<BuildingConfigElement>(configQuery.GetSingletonEntity(), true);
        using var prefabQuery = manager.CreateEntityQuery(typeof(BuildingPrefabDatabase), typeof(BuildingPrefabElement));
        var prefabs = manager.GetBuffer<BuildingPrefabElement>(prefabQuery.GetSingletonEntity(), true);
        var savedBuildings = new Dictionary<string, SavedBuilding>(StringComparer.Ordinal);
        bool hasMainFacility = false;

        foreach (var building in snapshot.Buildings)
        {
            if (building == null)
            {
                throw new InvalidDataException("건물 기록이 비어 있습니다.");
            }
            if (string.IsNullOrEmpty(building.Id))
            {
                throw new InvalidDataException("건물 저장 ID가 없습니다.");
            }
            if (!savedBuildings.TryAdd(building.Id, building))
            {
                throw new InvalidDataException("건물 저장 ID가 중복됩니다.");
            }
            if (!BuildingConfigLookupUtility.TryGetConfig(configs, building.BuildingTypeId, out var config))
            {
                throw new InvalidDataException("저장 파일에 지원하지 않는 건물 정의가 있습니다.");
            }
            if (building.Direction < DirectionEnum.Up || building.Direction >= DirectionEnum.Count)
            {
                throw new InvalidDataException("건물 방향이 유효하지 않습니다.");
            }

            int2 origin = GameWorldSnapshot.ReadCell(building.Origin);
            if (config.Placement.BootstrapOnly)
            {
                if (hasMainFacility || building.Id != snapshot.World.MainFacilityId || config.Tier != 1 ||
                    !origin.Equals(int2.zero) || building.Direction != DirectionEnum.Up)
                {
                    throw new InvalidDataException("주 시설 기록이 올바르지 않습니다.");
                }
                hasMainFacility = true;
            }
            AddFootprint(occupied, origin, BuildingFootprintUtility.GetEffectiveSize(config.Footprint, building.Direction));

            var stamp = new PlacementStamp(building.Stamp.Tick, building.Stamp.Order, building.Stamp.Receipt);
            if (!PrefabLookupUtility.TryGetBuildingPrefab(prefabs, config.BuildingId, out Entity prefab,
                    out float textureHeightToWidth))
            {
                throw new InvalidDataException("복원할 건물 프리팹을 찾을 수 없습니다.");
            }
            Entity instance = BuildingLifecycleUtility.InstantiateConfiguredBuilding(ref ecb, origin, building.Direction,
                stamp, config, prefab, manager.HasComponent<PostTransformMatrix>(prefab), textureHeightToWidth);
            if (!TryRecordTarget(ecb, taskTargets, building.Id, instance))
            {
                throw new InvalidDataException("건물 저장 ID가 중복됩니다.");
            }
            if (building.Disabled)
            {
                ecb.AddComponent<Disabled>(instance);
            }
        }

        if (!hasMainFacility)
        {
            throw new InvalidDataException("저장 파일에 주 시설이 없습니다.");
        }
        return savedBuildings;
    }

    internal static void AddFootprint(HashSet<int2> occupied, int2 origin, int2 size)
    {
        if ((long)origin.x + size.x - 1 > int.MaxValue || (long)origin.y + size.y - 1 > int.MaxValue)
        {
            throw new InvalidDataException("건물 위치가 격자 범위를 벗어납니다.");
        }
        for (int y = 0; y < size.y; y++)
        {
            for (int x = 0; x < size.x; x++)
            {
                if (!occupied.Add(origin + new int2(x, y)))
                {
                    throw new InvalidDataException("저장 파일에 위치가 겹치는 건물이 있습니다.");
                }
            }
        }
    }

    private static void RecordResources(ref EntityCommandBuffer ecb, EntityManager manager,
        List<SavedResourceNode> resources, HashSet<int2> generatedChunks, IReadOnlyDictionary<int, ItemTypeEnum> itemTypes)
    {
        using var prefabQuery = manager.CreateEntityQuery(typeof(ResourcePrefabDatabase), typeof(ResourcePrefabElement));
        var prefabs = manager.GetBuffer<ResourcePrefabElement>(prefabQuery.GetSingletonEntity(), true);
        var occupied = new HashSet<int2>();
        foreach (var resource in resources)
        {
            if (resource == null)
            {
                throw new InvalidDataException("자원 기록이 비어 있습니다.");
            }
            int2 cell = GameWorldSnapshot.ReadCell(resource.Cell);
            ItemTypeEnum type = ResolveItem(resource.ItemId, itemTypes);
            if (!generatedChunks.Contains(ChunkUtility.ToChunkPosition(cell)) || !occupied.Add(cell) ||
                resource.RemainingAmount <= 0 || !PrefabLookupUtility.TryGetResourcePrefab(prefabs, type, out Entity prefab))
            {
                throw new InvalidDataException("자원 노드의 청크, 매장량 또는 원형이 유효하지 않습니다.");
            }
            ResourceGenerationUtility.SpawnResourceNode(ref ecb, prefab, cell, type, resource.RemainingAmount);
        }
    }

    private static void RecordWorldItems(ref EntityCommandBuffer ecb, EntityManager manager,
        GameWorldSnapshot snapshot, IReadOnlyDictionary<int, ItemTypeEnum> itemTypes, Dictionary<string, int> taskTargets)
    {
        using var prefabQuery = manager.CreateEntityQuery(typeof(ItemPrefabDatabase), typeof(ItemPrefabElement));
        var prefabs = manager.GetBuffer<ItemPrefabElement>(prefabQuery.GetSingletonEntity(), true);
        var corrections = new Dictionary<SavedWorldItem, float>();
        var beltCells = SavedBeltPathCells.FromSnapshot(manager, snapshot);
        GameWorldSnapshot.ValidateBeltSpacing(beltCells, snapshot.WorldItems, corrections,
            snapshot.AllowsLegacyBeltSpacing, snapshot.RecordsBeltEntryDirections);
        foreach (var item in snapshot.WorldItems)
        {
            if (item == null)
            {
                throw new InvalidDataException("월드 아이템 기록이 비어 있습니다.");
            }
            int2 cell = GameWorldSnapshot.ReadCell(item.Cell);
            ItemTypeEnum type = ResolveItem(item.ItemId, itemTypes);
            if (string.IsNullOrEmpty(item.Id))
            {
                throw new InvalidDataException("월드 아이템의 저장 ID가 없습니다.");
            }
            if (!PrefabLookupUtility.TryGetItemPrefab(prefabs, type, out Entity prefab))
            {
                throw new InvalidDataException("월드 아이템의 원형이 유효하지 않습니다.");
            }
            Entity instance = ItemLifecycleUtility.SpawnPrefabItem(ref ecb, prefab, type, cell);
            if (!TryRecordTarget(ecb, taskTargets, item.Id, instance))
            {
                throw new InvalidDataException("월드 아이템의 저장 ID가 중복됩니다.");
            }
            if (item.Direction.HasValue)
            {
                DirectionEnum itemDirection = item.Direction.Value;
                if (itemDirection < DirectionEnum.Up || itemDirection >= DirectionEnum.Count)
                {
                    throw new InvalidDataException("저장한 아이템의 방향이 유효하지 않습니다.");
                }
                ecb.AddComponent(instance, new Direction(itemDirection));
            }
            if (!item.BeltProgress.HasValue)
            {
                continue;
            }
            float progress = item.BeltProgress.Value;
            if (corrections.TryGetValue(item, out float corrected))
            {
                progress = corrected;
            }
            DirectionEnum direction = beltCells.Belts[cell];
            DirectionEnum entry = snapshot.RecordsBeltEntryDirections && item.Direction.HasValue
                ? item.Direction.Value
                : BeltPathUtility.GetEntryDirection(cell, direction, beltCells);
            ecb.SetComponent(instance, new BeltMovementState(progress, entry));
            ecb.SetComponentEnabled<BeltMovementState>(instance, true);
            ecb.SetComponent(instance, LocalTransform.FromPosition(BeltPathUtility.Position(cell, progress, entry, direction)));
        }
    }

    private static void RestoreBuildingState(EntityManager manager, Dictionary<string, SavedBuilding> savedBuildings,
        IReadOnlyDictionary<int, ItemTypeEnum> itemTypes, string mainFacilityId, bool isLegacyFormat)
    {
        using var buildings = manager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<BuildingIdentity>(), ComponentType.ReadOnly<GridPosition>() },
            None = new[] { ComponentType.ReadOnly<ConstructionSite>() },
            Options = EntityQueryOptions.IncludeDisabledEntities
        });
        using var entities = buildings.ToEntityArray(Allocator.Temp);
        var byOrigin = new Dictionary<int2, Entity>(entities.Length);
        foreach (Entity entity in entities)
        {
            byOrigin.Add(manager.GetComponentData<GridPosition>(entity).Value, entity);
        }
        var byId = new Dictionary<string, Entity>(savedBuildings.Count, StringComparer.Ordinal);
        foreach (var entry in savedBuildings)
        {
            byId.Add(entry.Key, byOrigin[GameWorldSnapshot.ReadCell(entry.Value.Origin)]);
        }

        using var itemRegistry = manager.CreateEntityQuery(typeof(ItemRegistry), typeof(ItemConfigElement));
        var itemConfigs = manager.GetBuffer<ItemConfigElement>(itemRegistry.GetSingletonEntity(), true);
        var production = new GameProductionRestore(manager, itemConfigs, itemTypes);
        foreach (var entry in savedBuildings)
        {
            Entity building = byId[entry.Key];
            production.RestoreBeforeStorage(building, entry.Value.Production, isLegacyFormat);
            RestoreStorage(manager, building, entry.Value.Storage, itemTypes, itemConfigs);
            production.RestoreResearchRecovery(building, entry.Value.Production?.ResearchCycle);
            RestoreRouting(manager, building, entry.Value, byId);
        }
        Entity bootstrap = manager.CreateEntity(typeof(GameBootstrapState));
        manager.SetComponentData(bootstrap, new GameBootstrapState { MainFacility = byId[mainFacilityId] });
    }

    private static void RestoreStorage(EntityManager manager, Entity building, SavedStorage saved,
        IReadOnlyDictionary<int, ItemTypeEnum> itemTypes, DynamicBuffer<ItemConfigElement> items)
    {
        bool hasStorage = manager.HasComponent<Storage>(building);
        if (saved == null)
        {
            if (hasStorage)
            {
                throw new InvalidDataException("건물의 저장 슬롯 기록이 없습니다.");
            }
            return;
        }
        if (!hasStorage)
        {
            throw new InvalidDataException("저장 슬롯의 건물 역할이 유효하지 않습니다.");
        }
        var slots = manager.GetBuffer<StorageSlotElement>(building);
        bool hasFixedInput = manager.HasBuffer<BuildingInputSlotElement>(building);
        var inputSlots = hasFixedInput ? manager.GetBuffer<BuildingInputSlotElement>(building, true) : default;
        int dedicated = StationStorageUtility.ReadDedicatedCount(manager, building);
        int nonFuelCount = StationStorageUtility.NonFuelSlotCount(manager, building, slots.Length);
        bool hasFuel = manager.HasComponent<FuelStorageSlots>(building);
        var fuel = hasFuel ? manager.GetComponentData<FuelStorageSlots>(building) : default;
        foreach (var entry in saved.Slots)
        {
            if (entry == null)
            {
                throw new InvalidDataException("저장 슬롯 기록이 비어 있습니다.");
            }
            ItemTypeEnum type = ResolveItem(entry.ItemId, itemTypes);
            if ((uint)entry.Slot >= (uint)slots.Length || slots[entry.Slot].Quantity != 0 ||
                (hasFixedInput && inputSlots[entry.Slot].ItemType != type) ||
                !(entry.Slot >= nonFuelCount
                    ? fuel.Allows(type)
                    : StationStorageUtility.IsItemSlotAllowed(entry.Slot, nonFuelCount, dedicated, type)))
            {
                throw new InvalidDataException("저장 슬롯의 번호 또는 품목이 유효하지 않습니다.");
            }
            if (!InventorySlotUtility.TryAdd(slots, entry.Slot, type, entry.Quantity, ItemRegistry.GetMaxStack(items, type)))
            {
                throw new InvalidDataException("저장 슬롯의 수량이 유효하지 않습니다.");
            }
        }
        if (nonFuelCount > 0 || !hasFuel)
        {
            manager.SetComponentData(building, ReadFilter(saved, itemTypes));
        }
    }

    internal static StorageFilter ReadFilter(SavedStorageFilter saved, IReadOnlyDictionary<int, ItemTypeEnum> itemTypes)
    {
        if (saved.FilterMode > StorageFilterMode.Blacklist)
        {
            throw new InvalidDataException("저장한 필터 방식이 유효하지 않습니다.");
        }
        var filter = new StorageFilter(saved.FilterMode);
        foreach (int itemId in saved.FilterItemIds)
        {
            byte type = (byte)ResolveItem(itemId, itemTypes);
            if (type >= FixedBitSet.Capacity || filter.Mask.IsSet(type))
            {
                throw new InvalidDataException("저장 필터의 품목이 유효하지 않거나 중복됩니다.");
            }
            filter.Mask.Set(type, true);
        }
        return filter;
    }

    private static void RestoreRouting(EntityManager manager, Entity building, SavedBuilding saved,
        Dictionary<string, Entity> buildings)
    {
        bool splitter = manager.HasComponent<SplitterRoutingState>(building);
        bool merger = manager.HasComponent<MergerRoutingState>(building);
        if (saved.Routing == null)
        {
            if (splitter || merger)
            {
                throw new InvalidDataException("분배기 또는 합류기의 순환 기록이 없습니다.");
            }
            return;
        }
        if ((!splitter && !merger) || saved.Routing.Cursor >= 3 ||
            saved.Routing.ForwardDirection < DirectionEnum.Up || saved.Routing.ForwardDirection >= DirectionEnum.Count)
        {
            throw new InvalidDataException("분배기 또는 합류기의 순환 기록이 유효하지 않습니다.");
        }
        Entity belt = Entity.Null;
        if (saved.Routing.BeltId != null)
        {
            if (!buildings.TryGetValue(saved.Routing.BeltId, out belt) || !manager.HasComponent<BeltComponent>(belt))
            {
                throw new InvalidDataException("분배기 또는 합류기의 기준 벨트가 없습니다.");
            }
        }
        if (splitter)
        {
            manager.SetComponentData(building, new SplitterRoutingState(belt, saved.Routing.ForwardDirection, saved.Routing.Cursor));
        }
        else
        {
            manager.SetComponentData(building, new MergerRoutingState(belt, saved.Routing.ForwardDirection, saved.Routing.Cursor));
        }
    }

    private static void RestoreChunkProgress(EntityManager manager, HashSet<int2> generated, HashSet<int2> pending)
    {
        using var chunks = manager.CreateEntityQuery(typeof(GeneratedChunkTracker));
        var tracker = chunks.GetSingleton<GeneratedChunkTracker>();
        foreach (int2 coordinate in generated)
        {
            tracker.Map.Add(coordinate);
        }

        // 대기 좌표는 정상 Command에 돌려줘 중복 제거와 실제 생성 Writer를 유지한다.
        Entity queue = manager.CreateEntity(typeof(ChunkLoadRequestQueue));
        var requests = manager.AddBuffer<ChunkLoadRequestElement>(queue);
        foreach (int2 coordinate in pending)
        {
            requests.Add(new ChunkLoadRequestElement(coordinate));
        }
    }

    private static void RestoreSequences(EntityManager manager, SavedWorld world)
    {
        using var placement = manager.CreateEntityQuery(typeof(BuildingPlacementReceiptSequence));
        manager.SetComponentData(placement.GetSingletonEntity(), new BuildingPlacementReceiptSequence
        {
            CurrentTick = world.PlacementTick,
            NextValue = world.NextPlacementReceipt
        });
        using var tasks = manager.CreateEntityQuery(typeof(DroneTaskSequence));
        manager.SetComponentData(tasks.GetSingletonEntity(), new DroneTaskSequence { NextValue = world.NextDroneTaskSequence });
    }

    internal static ItemTypeEnum ResolveItem(int id, IReadOnlyDictionary<int, ItemTypeEnum> itemTypes)
    {
        if (!itemTypes.TryGetValue(id, out var type))
        {
            throw new InvalidDataException("저장 파일에 등록되지 않은 품목이 있습니다.");
        }
        return type;
    }
}
