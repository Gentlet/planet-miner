using PlanetMiner.Config;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// 게임 시작 시(InitializationSystemGroup) BuildingRuntimeConfig.json을 읽어
/// 검증된 건물별 런타임 설정을 BuildingRuntimeConfig 싱글톤 엔티티와 버퍼로 1회 게시하는 시스템.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct BuildingRuntimeConfigLoadSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        if (SystemAPI.HasSingleton<BuildingRuntimeConfig>())
        {
            state.Enabled = false;
            return;
        }

        bool success = BuildingRuntimeConfigLoader.TryLoadConfigFromResources(
            BuildingRuntimeConfigLoader.DefaultResourcePath,
            out var elements);

        if (success && elements != null && elements.Count > 0)
        {
            BuildingRuntimeConfigLoader.PublishConfig(state.EntityManager, elements);
        }
        else
        {
            // All-or-Nothing 원칙: 검증 실패 시 설정을 부분 게시하지 않고 완전 중단
            Debug.LogError("[BuildingRuntimeConfigLoadSystem] Failed to load or validate BuildingRuntimeConfig. Config will not be published.");
        }

        state.Enabled = false;
    }
}
