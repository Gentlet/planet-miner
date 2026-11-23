using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// 역할·목적: 직접 생성, 현장 완공, 저장 복원이 완공 건물 초기화를 공유하게 한다.
/// 입력·출력: BuildingId로 설정과 프리팹을 찾고, 설정의 역할과 티어로 런타임 구성을 초기화한다.
/// 이용: 직접 생성, 완공과 저장 복원이 호출한다. 설정이나 프리팹이 누락되면 오류를 기록하고 Null을 반환한다.
/// 수명·가시화: 반환 Entity는 지연 생성 참조다. 시뮬레이션 생성은 종료 ECB, 복원은 공개 전 World의 전용 ECB에서 실체화한다.
/// 요청/현장/자재 소비와 실패 보존 정책은 호출자가 소유한다.
/// </summary>
public static class BuildingLifecycleUtility
{
    internal static Entity CreateConstructionSite(ref EntityCommandBuffer ecb, int2 origin, int2 baseSize,
        DirectionEnum direction, in ConstructionSite state, in PlacementStamp stamp, in PlannedBuildingSettings settings)
    {
        Entity site = ecb.CreateEntity();
        ecb.AddComponent(site, new BuildingType(BuildingTypeEnum.ConstructionSite, 0));
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
            stamp, prefabDbEntity, config, prefabBufferLookup, ref registryRead);
    }

    // 같은 실행에서 검증한 설정의 크기로 생성한다. 별도 승인 크기가 있는 요청은 SpawnBuilding에서 대조한다.
    internal static Entity SpawnConfiguredBuilding(
        ref EntityCommandBuffer ecb,
        int2 position,
        DirectionEnum direction,
        PlacementStamp stamp,
        Entity prefabDbEntity,
        in BuildingConfigElement config,
        in BufferLookup<BuildingPrefabElement> prefabBufferLookup)
    {
        var registryRead = new SpawnRegistryReadContext();
        return SpawnConfiguredBuilding(ref ecb, position, direction, int2.zero,
            stamp, prefabDbEntity, config, prefabBufferLookup, ref registryRead);
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
        return SpawnConfiguredBuilding(ref ecb, position, direction, stamp, config, buffer);
    }

    // 준비된 설정과 프리팹으로 생성할 때 역할과 월드 수명의 초기화를 공유한다.
    internal static Entity SpawnConfiguredBuilding(ref EntityCommandBuffer ecb, int2 position,
        DirectionEnum direction, PlacementStamp stamp, in BuildingConfigElement config,
        in DynamicBuffer<BuildingPrefabElement> prefabs)
    {
        if (!PrefabLookupUtility.TryGetBuildingPrefab(prefabs, config.BuildingId, out Entity prefabEntity))
        {
            return RejectMissingDefinition(ref ecb, config.BuildingId, (FixedString32Bytes)"prefab");
        }

        float3 spawnPosition = new float3(position.x, position.y, 0f);

        // 프리팹 확보 전에는 건물 생성 명령을 기록하지 않는다. 공사 완료 호출자는 Null을 보고 현장/자재를 보존할 수 있다.
        Entity newBuilding = RuntimePrefabLifecycleUtility.Instantiate(ref ecb, prefabEntity);

        ecb.AddComponent(newBuilding, new BuildingType(config.BuildingType, config.Tier, config.BuildingId));
        ecb.AddComponent(newBuilding, new BuildingFootprint(config.Footprint));
        ecb.AddComponent(newBuilding, new GridPosition(position));
        ecb.AddComponent(newBuilding, new Direction(direction));
        ecb.AddComponent(newBuilding, stamp);
        ecb.SetComponent(newBuilding, LocalTransform.FromPosition(spawnPosition));

        int totalStorageCapacity = config.BuildingType is BuildingTypeEnum.MainFacility or BuildingTypeEnum.DroneStation
            ? config.StorageCapacity + config.DroneStationStorageCapacity
            : config.StorageCapacity;
        AttachTypeSpecificComponents(ref ecb, newBuilding, config.BuildingType, direction, config.Speed, totalStorageCapacity);
        if (DroneStationRangeUtility.IsStationType(config.BuildingType))
        {
            ecb.AddComponent(newBuilding, new DroneStationStorage { DedicatedSlotCount = config.DroneStationStorageCapacity });
            ecb.AddBuffer<DockedDroneElement>(newBuilding);
            ecb.AddBuffer<DroneChargingPreparationElement>(newBuilding);
        }

        return newBuilding;
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

    public static void AttachTypeSpecificComponents(
        ref EntityCommandBuffer ecb,
        Entity building,
        BuildingTypeEnum type,
        DirectionEnum direction,
        float speed,
        int storageCapacity)
    {
        switch (type)
        {
            case BuildingTypeEnum.Belt:
                float beltSpeed = speed > 0f ? speed : 2.0f;
                ecb.AddComponent(building, new BeltComponent(math.min(beltSpeed, GameConstants.MaxBeltSpeed)));
                break;

            case BuildingTypeEnum.Miner:
                AttachPowerParticipantComponents(ref ecb, building);
                AttachPowerConsumerComponents(ref ecb, building);
                ecb.AddComponent(building, new MinerState(speed > 0f ? speed : 1.0f));
                ecb.AddComponent<MinerWorkPreparation>(building);
                ecb.SetComponentEnabled<MinerWorkPreparation>(building, false);
                ecb.AddComponent<MinerDecision>(building);
                ecb.SetComponentEnabled<MinerDecision>(building, false);
                ecb.AddBuffer<ProductSlotElement>(building);
                ecb.AddBuffer<ProductResult>(building);
                ecb.AddComponent<BuildingItemOutputDecision>(building);
                ecb.SetComponentEnabled<BuildingItemOutputDecision>(building, false);
                break;

            // 레시피가 정해지기 전에는 재료를 받지 않는다. Command의 레시피 변경이 품목별 슬롯을 구성한다.
            case BuildingTypeEnum.Crafter:
                AttachPowerParticipantComponents(ref ecb, building);
                AttachPowerConsumerComponents(ref ecb, building);
                ecb.AddComponent(building, new CrafterState(selectedRecipeId: 0, speed: speed > 0f ? speed : 1.0f));
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
                break;

            case BuildingTypeEnum.Storage:
                AttachStorageComponents(ref ecb, building, storageCapacity);
                break;

            case BuildingTypeEnum.DroneStation:
                AttachDroneStationComponents(ref ecb, building);
                AttachStorageComponents(ref ecb, building, storageCapacity);
                AttachPowerParticipantComponents(ref ecb, building);
                AttachPowerConsumerComponents(ref ecb, building);
                break;

            case BuildingTypeEnum.Splitter:
                ecb.AddComponent(building, new SplitterRoutingState(Entity.Null, direction, 0));
                ecb.AddComponent<RoutingTransferDecision>(building);
                ecb.SetComponentEnabled<RoutingTransferDecision>(building, false);
                break;

            case BuildingTypeEnum.Merger:
                ecb.AddComponent(building, new MergerRoutingState(Entity.Null, direction, 0));
                ecb.AddComponent<RoutingTransferDecision>(building);
                ecb.SetComponentEnabled<RoutingTransferDecision>(building, false);
                break;

            case BuildingTypeEnum.MainFacility:
                AttachDroneStationComponents(ref ecb, building);
                ecb.AddComponent<IndestructibleBuilding>(building);
                AttachStorageComponents(ref ecb, building, storageCapacity);
                AttachPowerPoleComponents(ref ecb, building);
                AttachPowerGeneratorComponents(ref ecb, building, false);
                AttachPowerConsumerComponents(ref ecb, building);
                break;

            case BuildingTypeEnum.PowerPole:
                AttachPowerPoleComponents(ref ecb, building);
                break;

            case BuildingTypeEnum.CoalGenerator:
                AttachStorageComponents(ref ecb, building, storageCapacity);
                var fuelFilter = new StorageFilter(StorageFilterMode.Whitelist);
                fuelFilter.Mask.Set((byte)ItemTypeEnum.Coal, true);
                ecb.SetComponent(building, fuelFilter);
                AttachPowerParticipantComponents(ref ecb, building);
                AttachPowerGeneratorComponents(ref ecb, building, true);
                break;

            case BuildingTypeEnum.ResearchBuilding:
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
                AttachPowerParticipantComponents(ref ecb, building);
                AttachPowerConsumerComponents(ref ecb, building);
                break;
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
        }
    }

    private static void AttachPowerPoleComponents(ref EntityCommandBuffer ecb, Entity building)
    {
        ecb.AddComponent<PowerPole>(building);
        ecb.AddComponent<PowerGridTopologyDecision>(building);
        ecb.AddComponent(building, new PowerGridConnection { PowerPole = building, Grid = Entity.Null });
    }

    private static void AttachStorageComponents(ref EntityCommandBuffer ecb, Entity building, int storageCapacity)
    {
        ecb.AddComponent(building, new Storage(storageCapacity));
        var slots = ecb.AddBuffer<StorageSlotElement>(building);
        slots.Resize(storageCapacity, Unity.Collections.NativeArrayOptions.ClearMemory);
        ecb.AddComponent(building, new StorageFilter());
        ecb.AddComponent<BuildingItemOutputDecision>(building);
        ecb.SetComponentEnabled<BuildingItemOutputDecision>(building, false);
    }
}
