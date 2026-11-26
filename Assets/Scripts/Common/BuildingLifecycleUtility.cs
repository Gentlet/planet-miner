using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// 역할·목적: 직접 생성, 현장 완공, 저장 복원이 완공 건물 초기화를 공유하게 한다.
/// 입력·출력: BuildingId로 설정과 프리팹을 찾고, 설정된 기능으로 런타임 구성을 초기화한다.
/// 이용: 직접 생성, 완공과 저장 복원이 호출한다. 설정이나 프리팹이 누락되면 오류를 기록하고 Null을 반환한다.
/// 수명·가시화: 반환 Entity는 지연 생성 참조다. 시뮬레이션 생성은 종료 ECB, 복원은 공개 전 World의 전용 ECB에서 실체화한다.
/// 요청/현장/자재 소비와 실패 보존 정책은 호출자가 소유한다.
/// </summary>
public static class BuildingLifecycleUtility
{
    internal static Entity CreateConstructionSite(ref EntityCommandBuffer ecb, int2 origin, int2 baseSize,
        DirectionEnum direction, in ConstructionSite state, in PlacementStamp stamp, in PlannedBuildingSettings settings, in FixedString128Bytes displayName)
    {
        Entity site = ecb.CreateEntity();
        RuntimeEntityDebugNames.SetConstructionSite(ecb, site, displayName);
        ecb.AddComponent(site, new BuildingFootprint(baseSize));
        ecb.AddComponent(site, new GridPosition(origin));
        ecb.AddComponent(site, new Direction(direction));
        ecb.AddComponent(site, state);
        ecb.AddComponent(site, stamp);
        ecb.AddComponent(site, LocalTransform.FromPosition(origin.x, origin.y, 0f));
        if (settings.HasValues)
        {
            ecb.AddComponent(site, settings);
        }
        return site;
    }

    internal struct SpawnRegistryReadContext
    {
        internal RegistryBufferReadCache<BuildingConfigElement> Configs;
        internal RegistryBufferReadCache<BuildingPrefabElement> Prefabs;
    }

    internal static Entity SpawnBuilding(
        ref EntityCommandBuffer ecb,
        int buildingId,
        int2 position,
        DirectionEnum direction,
        int2 footprintSize,
        PlacementStamp stamp,
        Entity prefabDbEntity,
        Entity configEntity,
        in BufferLookup<BuildingPrefabElement> prefabBufferLookup,
        in BufferLookup<BuildingConfigElement> configBufferLookup,
        in ComponentLookup<PostTransformMatrix> prefabPostTransforms,
        ref SpawnRegistryReadContext registryRead)
    {
        if (buildingId == 0)
        {
            return Entity.Null;
        }

        if (configEntity == Entity.Null)
        {
            return RejectMissingDefinition(ref ecb, buildingId, (FixedString32Bytes)"configuration");
        }
        if (!registryRead.Configs.TryRead(configBufferLookup, configEntity, out var configBuffer))
        {
            return RejectMissingDefinition(ref ecb, buildingId, (FixedString32Bytes)"configuration");
        }

        if (!BuildingConfigLookupUtility.TryGetConfig(configBuffer, buildingId, out var config))
        {
            return RejectMissingDefinition(ref ecb, buildingId, (FixedString32Bytes)"configuration");
        }

        return SpawnConfiguredBuilding(ref ecb, position, direction, footprintSize,
            stamp, prefabDbEntity, config, prefabBufferLookup, prefabPostTransforms, ref registryRead);
    }

    // 같은 실행에서 검증한 설정의 크기로 생성한다. 별도 승인 크기가 있는 요청은 SpawnBuilding에서 대조한다.
    internal static Entity SpawnConfiguredBuilding(
        ref EntityCommandBuffer ecb,
        int2 position,
        DirectionEnum direction,
        PlacementStamp stamp,
        Entity prefabDbEntity,
        in BuildingConfigElement config,
        in BufferLookup<BuildingPrefabElement> prefabBufferLookup,
        in ComponentLookup<PostTransformMatrix> prefabPostTransforms)
    {
        var registryRead = new SpawnRegistryReadContext();
        return SpawnConfiguredBuilding(ref ecb, position, direction, int2.zero,
            stamp, prefabDbEntity, config, prefabBufferLookup, prefabPostTransforms, ref registryRead);
    }

    private static Entity SpawnConfiguredBuilding(
        ref EntityCommandBuffer ecb,
        int2 position,
        DirectionEnum direction,
        int2 footprintSize,
        PlacementStamp stamp,
        Entity prefabDbEntity,
        in BuildingConfigElement config,
        in BufferLookup<BuildingPrefabElement> prefabBufferLookup,
        in ComponentLookup<PostTransformMatrix> prefabPostTransforms,
        ref SpawnRegistryReadContext registryRead)
    {
        // Config가 유일한 크기 원본이다. 전달 크기는 승인된 현장/요청의 일치 확인용이며 덮어쓰기 입력이 아니다.
        int2 baseFootprint = config.Footprint;
        if (baseFootprint.x <= 0 || baseFootprint.y <= 0)
        {
            SimulationFailureUtility.Record(ref ecb, "[BuildingLifecycleUtility] Invalid configured building footprint. Spawn rejected.");
            return Entity.Null;
        }
        if (!math.all(footprintSize == int2.zero) && !math.all(footprintSize == baseFootprint))
        {
            UnityEngine.Debug.LogError("[BuildingLifecycleUtility] Requested footprint differs from BuildingConfig. Spawn rejected.");
            return Entity.Null;
        }

        if (prefabDbEntity == Entity.Null)
        {
            return RejectMissingDefinition(ref ecb, config.BuildingId, (FixedString32Bytes)"prefab");
        }
        if (!registryRead.Prefabs.TryRead(prefabBufferLookup, prefabDbEntity, out var buffer))
        {
            return RejectMissingDefinition(ref ecb, config.BuildingId, (FixedString32Bytes)"prefab");
        }
        return SpawnConfiguredBuilding(ref ecb, position, direction, stamp, config, buffer, prefabPostTransforms);
    }

    // 준비된 설정과 프리팹으로 생성할 때 역할과 월드 수명의 초기화를 공유한다.
    private static Entity SpawnConfiguredBuilding(ref EntityCommandBuffer ecb, int2 position,
        DirectionEnum direction, PlacementStamp stamp, in BuildingConfigElement config,
        in DynamicBuffer<BuildingPrefabElement> prefabs, in ComponentLookup<PostTransformMatrix> prefabPostTransforms)
    {
        if (!PrefabLookupUtility.TryGetBuildingPrefab(prefabs, config.BuildingId, out Entity prefabEntity,
                out float textureHeightToWidth))
        {
            return RejectMissingDefinition(ref ecb, config.BuildingId, (FixedString32Bytes)"prefab");
        }

        return InstantiateConfiguredBuilding(ref ecb, position, direction, stamp, config, prefabEntity,
            prefabPostTransforms.HasComponent(prefabEntity), textureHeightToWidth);
    }

    internal static Entity InstantiateConfiguredBuilding(ref EntityCommandBuffer ecb, int2 position,
        DirectionEnum direction, PlacementStamp stamp, in BuildingConfigElement config, Entity prefabEntity,
        bool hasAuthoredPostTransform, float textureHeightToWidth)
    {
        float3 spawnPosition = new float3(position.x, position.y, 0f);

        // 프리팹 확보 전에는 건물 생성 명령을 기록하지 않는다. 공사 완료 호출자는 Null을 보고 현장/자재를 보존할 수 있다.
        Entity newBuilding = RuntimePrefabLifecycleUtility.Instantiate(ref ecb, prefabEntity);
        RuntimeEntityDebugNames.SetBuilding(ecb, newBuilding, config);

        ecb.AddComponent(newBuilding, new BuildingIdentity(config.BuildingId));
        ecb.AddComponent(newBuilding, new BuildingFootprint(config.Footprint));
        ecb.AddComponent(newBuilding, new GridPosition(position));
        ecb.AddComponent(newBuilding, new Direction(direction));
        ecb.AddComponent(newBuilding, stamp);
        RecordVisualTransform(ref ecb, newBuilding, LocalTransform.FromPosition(spawnPosition), config.Footprint,
            direction, textureHeightToWidth, fitFootprint: !hasAuthoredPostTransform);

        AttachConfiguredFeatures(ref ecb, newBuilding, config, direction);

        return newBuilding;
    }

    // 생성과 승인된 벨트 방향 변경은 격자 원점을 그대로 두고 같은 표시 변환을 기록한다.
    internal static void RecordVisualTransform(ref EntityCommandBuffer ecb, Entity building, LocalTransform transform,
        int2 footprint, DirectionEnum direction, float textureHeightToWidth, bool fitFootprint)
    {
        transform.Rotation = direction.ToWorldRotation();
        ecb.SetComponent(building, transform);
        bool nonSquareTexture = textureHeightToWidth > 0f && textureHeightToWidth != 1f;
        if (fitFootprint && (math.any(footprint > 1) || nonSquareTexture))
        {
            ecb.AddComponent(building, new PostTransformMatrix
            {
                Value = BuildingFootprintUtility.GetVisualPostTransform(footprint, direction, textureHeightToWidth)
            });
        }
    }

    private static Entity RejectMissingDefinition(ref EntityCommandBuffer ecb, int buildingId, FixedString32Bytes definitionName)
    {
        FixedString128Bytes message = "[BuildingLifecycleUtility] Missing ";
        message.Append(definitionName);
        message.Append((FixedString128Bytes)" for building ID ");
        message.Append(buildingId);
        message.Append((FixedString128Bytes)". Spawn rejected.");
        SimulationFailureUtility.Record(ref ecb, message);
        return Entity.Null;
    }

    internal static void AttachConfiguredFeatures(ref EntityCommandBuffer ecb, Entity building, in BuildingConfigElement config,
        DirectionEnum direction)
    {
        if (config.BeltTransport.IsPresent)
        {
            float beltSpeed = config.BeltTransport.Speed > 0f ? config.BeltTransport.Speed : 2f;
            ecb.AddComponent(building, new BeltComponent(math.min(beltSpeed, GameConstants.MaxBeltSpeed)));
        }
        if (config.Mining.IsPresent)
        {
            ecb.AddComponent(building, new MinerState(config.Mining.Speed > 0f ? config.Mining.Speed : 1f));
            ecb.AddComponent<MinerWorkPreparation>(building);
            ecb.SetComponentEnabled<MinerWorkPreparation>(building, false);
            ecb.AddComponent<MinerDecision>(building);
            ecb.SetComponentEnabled<MinerDecision>(building, false);
            ecb.AddBuffer<ProductSlotElement>(building);
            ecb.AddBuffer<ProductResult>(building);
            ecb.AddComponent<BuildingItemOutputDecision>(building);
            ecb.SetComponentEnabled<BuildingItemOutputDecision>(building, false);
        }
        if (config.Crafting.IsPresent)
        {
            ecb.AddComponent(building, new CrafterState(selectedRecipeId: 0, speed: config.Crafting.Speed > 0f ? config.Crafting.Speed : 1f));
            ecb.AddComponent<CrafterWorkPreparation>(building);
            ecb.SetComponentEnabled<CrafterWorkPreparation>(building, false);
            ecb.AddComponent<CrafterDecision>(building);
            ecb.SetComponentEnabled<CrafterDecision>(building, false);
            ecb.AddComponent(building, new CrafterStateDecision(CrafterStatusEnum.NoRecipe));
            ecb.SetComponentEnabled<CrafterStateDecision>(building, false);
            ecb.AddComponent(building, new Storage(0));
            ecb.AddComponent(building, new StorageFilter(StorageFilterMode.Whitelist));
            ecb.AddBuffer<BuildingInputSlotElement>(building);
            ecb.AddBuffer<StorageSlotElement>(building);
            ecb.AddBuffer<ProductSlotElement>(building);
            ecb.AddBuffer<ProductResult>(building);
            ecb.AddComponent<BuildingItemOutputDecision>(building);
            ecb.SetComponentEnabled<BuildingItemOutputDecision>(building, false);
        }
        if (config.Research.IsPresent)
        {
            ecb.AddComponent<ResearchCycleState>(building);
            ecb.AddComponent<ResearchInputConfigured>(building);
            ecb.SetComponentEnabled<ResearchInputConfigured>(building, false);
            ecb.AddComponent<ResearchWorkPreparation>(building);
            ecb.SetComponentEnabled<ResearchWorkPreparation>(building, false);
            ecb.AddComponent<ResearchDecision>(building);
            ecb.SetComponentEnabled<ResearchDecision>(building, false);
            ecb.AddComponent(building, new Storage(0));
            ecb.AddComponent(building, new StorageFilter(StorageFilterMode.Whitelist));
            ecb.AddBuffer<StorageSlotElement>(building);
            ecb.AddBuffer<BuildingInputSlotElement>(building);
            ecb.AddBuffer<ResearchInputLimitElement>(building);
            ecb.AddBuffer<ResearchRecoveryElement>(building);
            ecb.AddBuffer<ResearchContributionElement>(building);
        }
        if (config.BeltRouting == BeltRoutingMode.Split)
        {
            ecb.AddComponent(building, new SplitterRoutingState(Entity.Null, direction, 0));
            ecb.AddComponent<RoutingTransferDecision>(building);
            ecb.SetComponentEnabled<RoutingTransferDecision>(building, false);
        }
        if (config.BeltRouting == BeltRoutingMode.Merge)
        {
            ecb.AddComponent(building, new MergerRoutingState(Entity.Null, direction, 0));
            ecb.AddComponent<RoutingTransferDecision>(building);
            ecb.SetComponentEnabled<RoutingTransferDecision>(building, false);
        }
        var fuelConfig = config.Power.Generation.FuelStorage;
        int nonFuelSlots = config.ItemStorage.Slots + config.DroneStation.DroneSlots;
        if (config.ItemStorage.IsPresent || config.DroneStation.IsPresent || fuelConfig.IsPresent)
        {
            AttachStorageComponents(ref ecb, building, nonFuelSlots + fuelConfig.Slots, !config.Mining.IsPresent);
        }
        if (config.ItemStorage.IsPresent)
        {
            ecb.AddComponent<GeneralItemStorage>(building);
        }
        if (config.ItemStorage.FilterEditable)
        {
            ecb.AddComponent<EditableStorageFilter>(building);
        }

        if (fuelConfig.IsPresent)
        {
            var fuel = new FuelStorageSlots { Start = nonFuelSlots, Count = fuelConfig.Slots, BeltOutput = fuelConfig.BeltOutput };
            foreach (var definition in fuelConfig.Fuels)
            {
                fuel.AllowedItems.Set((byte)definition.ItemType, true);
            }
            ecb.AddComponent(building, fuel);
        }
        if (config.DroneStation.IsPresent)
        {
            AttachDroneStationComponents(ref ecb, building);
            ecb.AddComponent(building, new DroneStationStorage { DedicatedSlotCount = config.DroneStation.DroneSlots });
            ecb.AddBuffer<DockedDroneElement>(building);
        }
        if (config.DroneCharging)
        {
            ecb.AddBuffer<DroneChargingPreparationElement>(building);
        }
        if (config.Placement.Indestructible)
        {
            ecb.AddComponent<IndestructibleBuilding>(building);
        }

        if (config.Power.Grid.IsPresent)
        {
            AttachPowerPoleComponents(ref ecb, building);
        }
        else if (config.Power.Generation.IsPresent || config.Power.Work.IsPresent || config.DroneCharging)
        {
            AttachPowerParticipantComponents(ref ecb, building);
        }

        if (config.Power.Generation.IsPresent)
        {
            AttachPowerGeneratorComponents(ref ecb, building, fuelConfig.IsPresent);
        }
        if (config.Power.Work.IsPresent || config.DroneCharging)
        {
            AttachPowerConsumerComponents(ref ecb, building);
        }
    }

    private static void AttachDroneStationComponents(ref EntityCommandBuffer ecb, Entity building)
    {
        ecb.AddComponent<DroneStation>(building);
        ecb.AddBuffer<DroneStationReservedSlotElement>(building);
        ecb.AddComponent<DroneStationNetwork>(building);
        ecb.AddComponent<DroneStationNetworkDecision>(building);
        ecb.SetComponentEnabled<DroneStationNetworkDecision>(building, false);
    }

    private static void AttachPowerParticipantComponents(ref EntityCommandBuffer ecb, Entity building)
    {
        ecb.AddComponent<PowerGridConnection>(building);
        ecb.AddComponent<PowerGridConnectionDecision>(building);
        ecb.SetComponentEnabled<PowerGridConnectionDecision>(building, false);
    }

    private static void AttachPowerConsumerComponents(ref EntityCommandBuffer ecb, Entity building)
    {
        ecb.AddComponent<PowerConsumer>(building);
        ecb.SetComponentEnabled<PowerConsumer>(building, false);
        ecb.AddComponent<PowerDemand>(building);
        ecb.SetComponentEnabled<PowerDemand>(building, false);
        ecb.AddComponent<PowerSupplyResult>(building);
        ecb.AddComponent<PowerSupplyReservation>(building);
        ecb.SetComponentEnabled<PowerSupplyReservation>(building, false);
    }

    private static void AttachPowerGeneratorComponents(ref EntityCommandBuffer ecb, Entity building, bool usesFuel)
    {
        ecb.AddComponent<PowerGenerator>(building);
        ecb.SetComponentEnabled<PowerGenerator>(building, false);
        ecb.AddComponent<PowerGeneratorSupplyPlan>(building);
        ecb.SetComponentEnabled<PowerGeneratorSupplyPlan>(building, false);
        if (usesFuel)
        {
            ecb.AddComponent<FuelEnergyState>(building);
            ecb.AddBuffer<PowerFuelSpend>(building);
        }
    }

    private static void AttachPowerPoleComponents(ref EntityCommandBuffer ecb, Entity building)
    {
        ecb.AddComponent<PowerPole>(building);
        ecb.AddComponent<PowerGridTopologyDecision>(building);
        ecb.AddComponent(building, new PowerGridConnection { PowerPole = building, Grid = Entity.Null });
    }

    private static void AttachStorageComponents(ref EntityCommandBuffer ecb, Entity building, int storageCapacity, bool addOutputDecision)
    {
        ecb.AddComponent(building, new Storage(storageCapacity));
        var slots = ecb.AddBuffer<StorageSlotElement>(building);
        slots.Resize(storageCapacity, Unity.Collections.NativeArrayOptions.ClearMemory);
        ecb.AddComponent(building, new StorageFilter());
        if (addOutputDecision)
        {
            ecb.AddComponent<BuildingItemOutputDecision>(building);
            ecb.SetComponentEnabled<BuildingItemOutputDecision>(building, false);
        }
    }
}
