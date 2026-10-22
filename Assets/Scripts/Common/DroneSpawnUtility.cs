using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// 준비된 DB로 드론을 생성하거나 기존 Entity에 수행자 초기 구성을 기록한다.
/// 호출자는 EndSimulation ECB와 원본 아이템/수납 관계의 전환·요청 중재·오류 결과를 소유한다.
/// 이 유틸리티는 아이템 역할을 제거하거나 예약/수용량을 정산하지 않는다.
/// </summary>
public static class DroneSpawnUtility
{
    public static bool TrySpawnActiveDrone(EntityManager manager, ref EntityCommandBuffer ecb,
        float3 position, float maximumBatteryCharge, out Entity drone, out string error)
    {
        drone = Entity.Null;
        if (!HasReadyDatabase(manager, out error))
        {
            return false;
        }
        if (!ValidateInitialInput(position, maximumBatteryCharge, out int2 cell, out error))
        {
            return false;
        }
        // Ready의 단독 검증 Writer 이후 DB와 원형은 불변이다. 같은 원형의 정합성을 반복 검사하지 않는다.
        using var databases = manager.CreateEntityQuery(ComponentType.ReadOnly<DronePrefabDatabase>(),
            ComponentType.ReadOnly<DronePrefab>());
        Entity database = databases.GetSingletonEntity();
        Entity prefab = manager.GetComponentData<DronePrefab>(database).Prefab;
        LocalTransform transform = manager.GetComponentData<LocalTransform>(prefab);
        bool hasGridPosition = manager.HasComponent<GridPosition>(prefab);
        bool hasLocalToWorld = manager.HasComponent<LocalToWorld>(prefab);
        float4x4 postTransform = ReadPostTransform(manager, prefab);
        drone = ecb.Instantiate(prefab);
        RecordInitialState(ref ecb, drone, transform, position, cell, maximumBatteryCharge,
            hasGridPosition, hasLocalToWorld, false, postTransform);
        return true;
    }

    /// <summary>
    /// 기존 논리 Entity를 재사용하기 위한 초기 구성 경계다. 같은 ECB에서 한 번만 호출하는 것은 호출자 계약이다.
    /// 이미 공개된 수행자/화물 구성이 있으면 덮어쓰지 않는다. 아이템↔드론 전환 자체는 Task 9.3 소유다.
    /// </summary>
    public static bool TryInitializeWorkerRole(EntityManager manager, ref EntityCommandBuffer ecb,
        Entity entity, float3 position, float maximumBatteryCharge, out string error)
    {
        if (!HasReadyDatabase(manager, out error))
        {
            return false;
        }
        if (!ValidateInitialInput(position, maximumBatteryCharge, out int2 cell, out error))
        {
            return false;
        }
        if (entity == Entity.Null)
        {
            error = "Drone root is Null.";
            return false;
        }
        if (!manager.Exists(entity))
        {
            error = "Drone root no longer exists.";
            return false;
        }
        if (manager.HasComponent<Prefab>(entity) || manager.HasComponent<Disabled>(entity))
        {
            error = "Drone root must be a live, enabled instance.";
            return false;
        }
        if (!manager.HasComponent<LocalTransform>(entity))
        {
            error = "Drone root requires LocalTransform.";
            return false;
        }
        if (HasWorkerComponents(manager, entity))
        {
            error = "Drone root already has worker or cargo components.";
            return false;
        }

        bool hasBattery = manager.HasComponent<DroneBatteryState>(entity);
        float charge = hasBattery ? manager.GetComponentData<DroneBatteryState>(entity).CurrentCharge : maximumBatteryCharge;
        if (!math.isfinite(charge) || charge < 0f || charge > maximumBatteryCharge)
        {
            error = "Existing drone battery is outside the configured range.";
            return false;
        }
        RecordInitialState(ref ecb, entity, manager.GetComponentData<LocalTransform>(entity), position, cell,
            charge, manager.HasComponent<GridPosition>(entity), manager.HasComponent<LocalToWorld>(entity), hasBattery,
            ReadPostTransform(manager, entity));
        return true;
    }

    internal static bool HasWorkerComponents(EntityManager manager, Entity entity)
    {
        return manager.HasComponent<DroneWorker>(entity) || manager.HasComponent<DroneWorkerObservation>(entity) ||
            manager.HasComponent<DroneWorkerAssignment>(entity) || manager.HasComponent<DroneCargoState>(entity) ||
            manager.HasBuffer<StoredItemElement>(entity);
    }

    private static bool HasReadyDatabase(EntityManager manager, out string error)
    {
        using var ready = manager.CreateEntityQuery(ComponentType.ReadOnly<PrefabDatabaseReady>());
        error = null;
        if (ready.IsEmptyIgnoreFilter)
        {
            error = "Prefab databases are not ready.";
            return false;
        }
        return true;
    }

    private static bool ValidateInitialInput(float3 position, float maximumBatteryCharge, out int2 cell, out string error)
    {
        cell = default;
        error = null;
        if (!math.all(math.isfinite(position)))
        {
            error = "Drone position must be finite.";
            return false;
        }
        double cellX = Math.Floor(position.x);
        double cellY = Math.Floor(position.y);
        if (cellX < int.MinValue || cellX > int.MaxValue || cellY < int.MinValue || cellY > int.MaxValue)
        {
            error = "Drone position is outside the supported grid coordinate range.";
            return false;
        }
        if (!math.isfinite(maximumBatteryCharge) || maximumBatteryCharge <= 0f)
        {
            error = "Maximum drone battery must be finite and positive.";
            return false;
        }
        cell = new int2((int)cellX, (int)cellY);
        return true;
    }

    private static float4x4 ReadPostTransform(EntityManager manager, Entity entity)
    {
        return manager.HasComponent<PostTransformMatrix>(entity)
            ? manager.GetComponentData<PostTransformMatrix>(entity).Value
            : float4x4.identity;
    }

    private static void RecordInitialState(ref EntityCommandBuffer ecb, Entity drone, LocalTransform transform,
        float3 position, int2 cell, float charge, bool hasGridPosition, bool hasLocalToWorld, bool hasBattery,
        float4x4 postTransform)
    {
        transform.Position = position;
        ecb.SetComponent(drone, transform);
        if (hasGridPosition)
        {
            ecb.SetComponent(drone, new GridPosition(cell));
        }
        else
        {
            ecb.AddComponent(drone, new GridPosition(cell));
        }
        var localToWorld = new LocalToWorld { Value = math.mul(transform.ToMatrix(), postTransform) };
        if (hasLocalToWorld)
        {
            ecb.SetComponent(drone, localToWorld);
        }
        else
        {
            ecb.AddComponent(drone, localToWorld);
        }
        ecb.AddComponent<DroneWorker>(drone);
        ecb.AddComponent(drone, new DroneWorkerObservation { Position = position, Revision = 1, CanAcceptTask = false });
        ecb.AddComponent<DroneWorkerAssignment>(drone);
        ecb.AddComponent<DroneCargoState>(drone);
        ecb.AddBuffer<StoredItemElement>(drone);
        if (!hasBattery)
        {
            ecb.AddComponent(drone, new DroneBatteryState { CurrentCharge = charge });
        }
    }
}
