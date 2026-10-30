using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// 역할·목적: 직접 생성과 현장 완공이 같은 완공 건물 초기화 경계를 사용하게 한다.
/// 입력·출력: 종류·위치·기본 크기·설정/DB Lookup을 받아 호출자의 ECB에 공통/종류별 런타임 구성을 기록한다.
/// 이용: 직접 생성과 완공이 호출한다. 설정/대상 종류/프리팹 누락은 오류 기록 후 Null을 반환하며 대체 엔티티를 만들지 않는다.
/// 수명·가시화: 반환 Entity는 지연 생성 참조이며 실제 건물은 호출자가 지정한 종료 ECB에서 실체화한다. 요청/현장/자재 소비와 실패 보존 정책은 호출자가 소유한다.
/// </summary>
public static class BuildingLifecycleUtility
{
    public static Entity SpawnBuilding(
        ref EntityCommandBuffer ecb,
        BuildingTypeEnum targetType,
        int targetTier,
        int2 position,
        DirectionEnum direction,
        int2 footprintSize,
        PlacementStamp stamp,
        bool hasPrefabDb,
        Entity prefabDbEntity,
        bool hasConfig,
        Entity configEntity,
        in BufferLookup<BuildingPrefabElement> prefabBufferLookup,
        in BufferLookup<BuildingConfigElement> configBufferLookup)
    {
        if (targetType == BuildingTypeEnum.None || targetType == BuildingTypeEnum.ConstructionSite)
        {
            return Entity.Null;
        }

        if (targetTier <= 0)
        {
            return RejectMissingConfiguration(ref ecb, targetType, targetTier);
        }
        if (!hasConfig)
        {
            return RejectMissingConfiguration(ref ecb, targetType, targetTier);
        }
        if (configEntity == Entity.Null)
        {
            return RejectMissingConfiguration(ref ecb, targetType, targetTier);
        }
        if (!configBufferLookup.HasBuffer(configEntity))
        {
            return RejectMissingConfiguration(ref ecb, targetType, targetTier);
        }

        var configBuffer = configBufferLookup[configEntity];
        if (!BuildingConfigLookupUtility.TryGetConfig(configBuffer, targetType, targetTier, out var config))
        {
            return RejectMissingConfiguration(ref ecb, targetType, targetTier);
        }

        // Config가 유일한 크기 원본이다. 전달 크기는 승인된 현장/요청의 일치 확인용이며 덮어쓰기 입력이 아니다.
        int2 baseFootprint = config.Footprint;
        float speed = config.Speed;
        int storageCapacity = config.StorageCapacity;
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

        float3 spawnPosition = new float3(position.x, position.y, 0f);

        Entity newBuilding;

        Entity prefabEntity = Entity.Null;

        if (hasPrefabDb && prefabDbEntity != Entity.Null && prefabBufferLookup.HasBuffer(prefabDbEntity))
        {
            var buffer = prefabBufferLookup[prefabDbEntity];
            PrefabLookupUtility.TryGetBuildingPrefab(buffer, targetType, targetTier, out prefabEntity);
        }

        if (prefabEntity == Entity.Null)
        {
            FixedString128Bytes msg = default;
            msg.Append((FixedString128Bytes)"[BuildingLifecycleUtility] Missing prefab for building type '");
            msg.Append(targetType.ToFixedString());
            msg.Append((FixedString128Bytes)"/Tier ");
            msg.Append(targetTier);
            msg.Append((FixedString128Bytes)"'. Spawn rejected.");
            SimulationFailureUtility.Record(ref ecb, msg);
            return Entity.Null;
        }

        // 프리팹 확보 전에는 건물 생성 명령을 기록하지 않는다. 공사 완료 호출자는 Null을 보고 현장/자재를 보존할 수 있다.
        newBuilding = ecb.Instantiate(prefabEntity);

        ecb.AddComponent(newBuilding, new BuildingType(targetType, targetTier));
        ecb.AddComponent(newBuilding, new BuildingFootprint(baseFootprint));
        ecb.AddComponent(newBuilding, new GridPosition(position));
        ecb.AddComponent(newBuilding, new Direction(direction));
        ecb.AddComponent(newBuilding, stamp);
        ecb.SetComponent(newBuilding, LocalTransform.FromPosition(spawnPosition));

        AttachTypeSpecificComponents(ref ecb, newBuilding, targetType, direction, speed, storageCapacity);
        if (DroneStationRangeUtility.IsStationType(targetType))
        {
            ecb.SetComponent(newBuilding, new Storage(storageCapacity + config.DroneStationStorageCapacity));
            ecb.AddComponent(newBuilding, new DroneStationStorage { DedicatedSlotCount = config.DroneStationStorageCapacity });
            ecb.AddBuffer<DockedDroneElement>(newBuilding);
            ecb.AddBuffer<DroneChargingPreparationElement>(newBuilding);
        }

        return newBuilding;
    }

    private static Entity RejectMissingConfiguration(ref EntityCommandBuffer ecb, BuildingTypeEnum type, int tier)
    {
        FixedString128Bytes message = "[BuildingLifecycleUtility] Missing configuration for building type '";
        message.Append(type.ToFixedString());
        message.Append((FixedString128Bytes)"/Tier ");
        message.Append(tier);
        message.Append((FixedString128Bytes)"'. Spawn rejected.");
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
                ecb.AddBuffer<ProductItemElement>(building);
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
                ecb.AddBuffer<StoredItemElement>(building);
                ecb.AddBuffer<ProductItemElement>(building);
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
                AttachPowerParticipantComponents(ref ecb, building);
                AttachPowerConsumerComponents(ref ecb, building);
                break;
        }
    }

    private static void AttachDroneStationComponents(ref EntityCommandBuffer ecb, Entity building)
    {
        ecb.AddComponent<DroneStation>(building);
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
        ecb.AddBuffer<StoredItemElement>(building);
        ecb.AddComponent(building, new StorageFilter());
        ecb.AddComponent<BuildingItemOutputDecision>(building);
        ecb.SetComponentEnabled<BuildingItemOutputDecision>(building, false);
    }
}
