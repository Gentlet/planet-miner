using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// 완공 건물 엔티티 생성 및 컴포넌트 초기화를 공통으로 처리하는 유틸리티 (Unmanaged / Burst 호환).
/// 
/// [책임]
/// - BuildingLifecycleApplySystem(SpawnBuildingRequest 소비) 및 ConstructionLifecycleApplySystem(현장 완공 전환)에서 공유.
/// - 프리팹 DB 인스턴스화 또는 Fallback 아키타입 생성을 일관되게 수행.
/// - 공통 컴포넌트(BuildingType, Footprint, GridPosition, Direction, Stamp, LocalTransform) 및 타입별 필수 컴포넌트 원자적 주입.
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
        EntityArchetype fallbackArchetype,
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

        BuildingFootprint footprint = new BuildingFootprint(baseFootprint);
        int2 effectiveSize = footprint.GetEffectiveSize(direction);
        float3 spawnPosition = new float3(position.x, position.y, 0f);

        Entity newBuilding;

        if (hasPrefabDb)
        {
            Entity prefabEntity = Entity.Null;
            int2 dbFootprint = int2.zero;

            if (prefabDbEntity != Entity.Null && prefabBufferLookup.HasBuffer(prefabDbEntity))
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
                UnityEngine.Debug.LogError(msg);
                return Entity.Null;
            }

            newBuilding = ecb.Instantiate(prefabEntity);

            ecb.AddComponent(newBuilding, new BuildingType(targetType));
            ecb.AddComponent(newBuilding, new BuildingFootprint(effectiveSize));
            ecb.AddComponent(newBuilding, new GridPosition(position));
            ecb.AddComponent(newBuilding, new Direction(direction));
            ecb.AddComponent(newBuilding, stamp);
            ecb.SetComponent(newBuilding, LocalTransform.FromPosition(spawnPosition));

            AttachTypeSpecificComponents(ref ecb, newBuilding, targetType, direction, speed, storageCapacity);
        }
        else
        {
            newBuilding = ecb.CreateEntity(fallbackArchetype);

            ecb.SetComponent(newBuilding, new BuildingType(targetType));
            ecb.SetComponent(newBuilding, new BuildingFootprint(effectiveSize));
            ecb.SetComponent(newBuilding, new GridPosition(position));
            ecb.SetComponent(newBuilding, new Direction(direction));
            ecb.SetComponent(newBuilding, stamp);
            ecb.SetComponent(newBuilding, LocalTransform.FromPosition(spawnPosition));

            AttachTypeSpecificComponents(ref ecb, newBuilding, targetType, direction, speed, storageCapacity);
        }

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
                ecb.AddComponent(building, new BeltComponent(speed > 0f ? speed : 2.0f));
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

            case BuildingTypeEnum.Crafter:
                ecb.AddComponent(building, new CrafterState(selectedRecipeId: 0, speed: speed > 0f ? speed : 1.0f));
                ecb.AddComponent<CrafterDecision>(building);
                ecb.SetComponentEnabled<CrafterDecision>(building, false);
                ecb.AddBuffer<StoredItemElement>(building);
                ecb.AddBuffer<ProductItemElement>(building);
                ecb.AddBuffer<ProductResult>(building);
                ecb.AddComponent<BuildingItemOutputDecision>(building);
                ecb.SetComponentEnabled<BuildingItemOutputDecision>(building, false);
                break;

            case BuildingTypeEnum.Storage:
                ecb.AddComponent(building, new Storage(storageCapacity > 0 ? storageCapacity : 20));
                ecb.AddBuffer<StoredItemElement>(building);
                ecb.AddComponent(building, new StorageFilter());
                ecb.AddComponent<BuildingItemOutputDecision>(building);
                ecb.SetComponentEnabled<BuildingItemOutputDecision>(building, false);
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
                if (storageCapacity > 0)
                {
                    ecb.AddComponent(building, new Storage(storageCapacity));
                    ecb.AddBuffer<StoredItemElement>(building);
                    ecb.AddComponent(building, new StorageFilter());
                    ecb.AddComponent<BuildingItemOutputDecision>(building);
                    ecb.SetComponentEnabled<BuildingItemOutputDecision>(building, false);
                }
                break;

            case BuildingTypeEnum.PowerPole:
            case BuildingTypeEnum.CoalGenerator:
            case BuildingTypeEnum.DroneStation:
            case BuildingTypeEnum.ResearchBuilding:
                // 전력/드론/연구 도메인 컴포넌트는 해당 Phase(Phase 8, 9, 10)에서 확장
                break;
        }
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
