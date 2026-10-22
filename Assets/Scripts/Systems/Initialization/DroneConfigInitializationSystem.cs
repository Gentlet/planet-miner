using System;
using Newtonsoft.Json.Linq;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>필수 드론 초기 설정을 한 번 검증/게시한다. 기존 유효 singleton은 보존하고 실패는 공통 Fatal이다.</summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class DroneConfigInitializationSystem : SystemBase
{
    public const string ResourcePath = "Config/DroneConfig";

    protected override void OnUpdate()
    {
        if (!SimulationFailureUtility.HasFatalError(EntityManager))
        {
            Initialize(EntityManager);
        }
        Enabled = false;
    }

    public static bool Initialize(EntityManager manager, string jsonOverride = null)
    {
        Entity createdCapacity = Entity.Null;
        Entity createdBattery = Entity.Null;
        try
        {
            using var capacities = manager.CreateEntityQuery(typeof(DroneCapacityState));
            using var batteries = manager.CreateEntityQuery(typeof(DroneBatteryConfig));
            int capacityCount = capacities.CalculateEntityCount();
            int batteryCount = batteries.CalculateEntityCount();
            if (capacityCount > 1 || batteryCount > 1)
            {
                throw new InvalidOperationException("Drone configuration has duplicate singleton state.");
            }
            if (capacityCount == 1 && capacities.GetSingleton<DroneCapacityState>().CarryingCapacity <= 0)
            {
                throw new InvalidOperationException("Existing drone carrying capacity must be positive.");
            }
            if (batteryCount == 1)
            {
                float maximum = batteries.GetSingleton<DroneBatteryConfig>().MaximumCharge;
                if (!math.isfinite(maximum) || maximum <= 0f)
                {
                    throw new InvalidOperationException("Existing drone maximum battery must be finite and positive.");
                }
            }
            if (capacityCount == 1 && batteryCount == 1)
            {
                return true;
            }

            string json = jsonOverride;
            if (json == null)
            {
                var asset = Resources.Load<TextAsset>(ResourcePath);
                if (asset == null)
                {
                    throw new InvalidOperationException("Drone configuration resource is missing.");
                }
                json = asset.text;
            }
            var input = JObject.Parse(json);
            JToken capacityToken = input["carryingCapacity"];
            if (capacityToken == null || capacityToken.Type != JTokenType.Integer)
            {
                throw new InvalidOperationException("carryingCapacity must be an explicit positive integer.");
            }
            long capacity = capacityToken.Value<long>();
            if (capacity <= 0 || capacity > int.MaxValue)
            {
                throw new InvalidOperationException("carryingCapacity is outside the positive int range.");
            }
            JToken batteryToken = input["maximumBattery"];
            if (batteryToken == null || (batteryToken.Type != JTokenType.Float && batteryToken.Type != JTokenType.Integer))
            {
                throw new InvalidOperationException("maximumBattery must be an explicit positive number.");
            }
            float maximumBattery = batteryToken.Value<float>();
            if (!math.isfinite(maximumBattery) || maximumBattery <= 0f)
            {
                throw new InvalidOperationException("maximumBattery must be finite and positive.");
            }

            // 모든 입력/기존 상태 검사가 끝나기 전에는 기존 원본이나 새 엔티티를 변경하지 않는다.
            if (capacityCount == 0)
            {
                createdCapacity = manager.CreateEntity(typeof(DroneCapacityState));
                manager.SetComponentData(createdCapacity, new DroneCapacityState { CarryingCapacity = (int)capacity });
            }
            if (batteryCount == 0)
            {
                createdBattery = manager.CreateEntity(typeof(DroneBatteryConfig));
                manager.SetComponentData(createdBattery, new DroneBatteryConfig { MaximumCharge = maximumBattery });
            }
            return true;
        }
        catch (Exception exception)
        {
            if (createdCapacity != Entity.Null && manager.Exists(createdCapacity))
            {
                manager.DestroyEntity(createdCapacity);
            }
            if (createdBattery != Entity.Null && manager.Exists(createdBattery))
            {
                manager.DestroyEntity(createdBattery);
            }
            SimulationFailureUtility.RecordInitializationFailure(manager,
                $"[DroneConfigInitializationSystem] {exception.Message}", "Drone configuration initialization failed.");
            return false;
        }
    }
}
