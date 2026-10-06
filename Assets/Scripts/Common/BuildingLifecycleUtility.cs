using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// 역할·목적: 직접 생성과 현장 완공이 같은 완공 건물 초기화 경계를 사용하게 한다.
/// 입력·출력: 종류·위치·기본 크기·설정/DB Lookup을 받아 호출자의 ECB에 공통/종류별 런타임 구성을 기록한다.
/// 이용: BuildingLifecycleApplySystem과 ConstructionLifecycleApplySystem이 호출한다. DB/항목 누락은 오류 기록 후 Null을 반환하며 대체 엔티티를 만들지 않는다.
/// 수명·가시화: 반환 Entity는 지연 생성 참조이며 실제 건물은 EndStateApply에 실체화한다. 요청/현장/자재 소비와 실패 보존 정책은 호출자가 소유한다.
/// </summary>
public static class BuildingLifecycleUtility
{
    public static Entity SpawnBuilding(
        ref EntityCommandBuffer ecb,
        BuildingTypeEnum targetType,
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

        int2 baseFootprint = footprintSize;
        float speed = (targetType == BuildingTypeEnum.Belt) ? 2.0f : 1.0f;
        int storageCapacity = 20;

        if (hasConfig && configEntity != Entity.Null && configBufferLookup.HasBuffer(configEntity))
        {
            var configBuffer = configBufferLookup[configEntity];
            if (BuildingConfigLookupUtility.TryGetConfig(configBuffer, targetType, out var config))
            {
                speed = config.Speed;
                storageCapacity = config.StorageCapacity;
                if (baseFootprint.x <= 0 || baseFootprint.y <= 0)
                {
                    baseFootprint = config.Footprint;
                }
            }
        }

        if (baseFootprint.x <= 0 || baseFootprint.y <= 0)
        {
            baseFootprint = GetDefaultFootprint(targetType);
        }

        float3 spawnPosition = new float3(position.x, position.y, 0f);

        Entity newBuilding;

        Entity prefabEntity = Entity.Null;
        int2 dbFootprint = int2.zero;

        if (hasPrefabDb && prefabDbEntity != Entity.Null && prefabBufferLookup.HasBuffer(prefabDbEntity))
        {
            var buffer = prefabBufferLookup[prefabDbEntity];
            PrefabLookupUtility.TryGetBuildingPrefab(buffer, targetType, out prefabEntity, out dbFootprint);
        }

        if (prefabEntity == Entity.Null)
        {
            FixedString128Bytes msg = default;
            msg.Append((FixedString128Bytes)"[BuildingLifecycleUtility] Missing prefab for building type '");
            msg.Append(targetType.ToFixedString());
            msg.Append((FixedString128Bytes)"'. Spawn rejected.");
            SimulationFailureUtility.Record(ref ecb, msg);
            return Entity.Null;
        }

        // 프리팹 확보 전에는 건물 생성 명령을 기록하지 않는다. 공사 완료 호출자는 Null을 보고 현장/자재를 보존할 수 있다.
        newBuilding = ecb.Instantiate(prefabEntity);

        ecb.AddComponent(newBuilding, new BuildingType(targetType));
        ecb.AddComponent(newBuilding, new BuildingFootprint(baseFootprint));
        ecb.AddComponent(newBuilding, new GridPosition(position));
        ecb.AddComponent(newBuilding, new Direction(direction));
        ecb.AddComponent(newBuilding, stamp);
        ecb.SetComponent(newBuilding, LocalTransform.FromPosition(spawnPosition));

        AttachTypeSpecificComponents(ref ecb, newBuilding, targetType, direction, speed, storageCapacity);

        return newBuilding;
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
                ecb.AddComponent(building, new MinerState(speed > 0f ? speed : 1.0f));
                ecb.AddComponent<MinerDecision>(building);
                ecb.SetComponentEnabled<MinerDecision>(building, false);
                ecb.AddBuffer<ProductItemElement>(building);
                ecb.AddBuffer<ProductResult>(building);
                ecb.AddComponent<BuildingItemOutputDecision>(building);
                ecb.SetComponentEnabled<BuildingItemOutputDecision>(building, false);
                break;

            // 레시피가 정해지기 전에는 재료를 받지 않는다. Command의 레시피 변경이 품목별 슬롯을 구성한다.
            case BuildingTypeEnum.Crafter:
                ecb.AddComponent(building, new CrafterState(selectedRecipeId: 0, speed: speed > 0f ? speed : 1.0f));
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
            case BuildingTypeEnum.DroneStation:
                AttachStorageComponents(ref ecb, building, storageCapacity);
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
                ecb.AddComponent<IndestructibleBuilding>(building);
                AttachStorageComponents(ref ecb, building, storageCapacity);
                break;

            case BuildingTypeEnum.PowerPole:
            case BuildingTypeEnum.CoalGenerator:
            case BuildingTypeEnum.ResearchBuilding:
                // 전력/드론/연구 도메인 컴포넌트는 해당 Phase(Phase 8, 9, 10)에서 확장
                break;
        }
    }

    private static void AttachStorageComponents(ref EntityCommandBuffer ecb, Entity building, int storageCapacity)
    {
        ecb.AddComponent(building, new Storage(storageCapacity > 0 ? storageCapacity : 20));
        ecb.AddBuffer<StoredItemElement>(building);
        ecb.AddComponent(building, new StorageFilter());
        ecb.AddComponent<BuildingItemOutputDecision>(building);
        ecb.SetComponentEnabled<BuildingItemOutputDecision>(building, false);
    }

    public static int2 GetDefaultFootprint(BuildingTypeEnum type)
    {
        switch (type)
        {
            case BuildingTypeEnum.Belt:
            case BuildingTypeEnum.Splitter:
            case BuildingTypeEnum.Merger:
            case BuildingTypeEnum.PowerPole:
            case BuildingTypeEnum.Storage:
                return new int2(1, 1);

            case BuildingTypeEnum.Miner:
            case BuildingTypeEnum.Crafter:
                return new int2(2, 2);

            case BuildingTypeEnum.CoalGenerator:
            case BuildingTypeEnum.DroneStation:
            case BuildingTypeEnum.ResearchBuilding:
            case BuildingTypeEnum.MainFacility:
                return new int2(3, 3);

            default:
                return new int2(1, 1);
        }
    }
}
