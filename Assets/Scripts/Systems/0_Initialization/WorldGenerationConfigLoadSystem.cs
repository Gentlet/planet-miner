using Unity.Entities;
using UnityEngine;

/// <summary>
/// 월드 초기화 시점에 WorldGenerationConfig.json을 로드하여 ResourceGenerationSettings 싱글톤 및 자원 생성 설정을 ECS에 1회 게시하는 부트스트랩 시스템.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct WorldGenerationConfigLoadSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
    }

    public void OnDestroy(ref SystemState state)
    {
    }

    public void OnUpdate(ref SystemState state)
    {
        if (SystemAPI.HasSingleton<ResourceGenerationSettings>())
        {
            state.Enabled = false;
            return;
        }

        bool success = WorldGenerationConfigLoader.TryLoadConfigFromResources(
            WorldGenerationConfigLoader.DefaultResourcePath,
            out uint worldSeed,
            out var elements);

        if (success && elements != null && elements.Count > 0)
        {
            WorldGenerationConfigLoader.PublishConfig(state.EntityManager, worldSeed, elements);
        }
        else
        {
            // All-or-Nothing 원칙: 검증 실패 시 설정을 부분 게시하지 않고 완전 중단
            Debug.LogError("[WorldGenerationConfigLoadSystem] Failed to load or validate WorldGenerationConfig. Config will not be published.");
        }

        state.Enabled = false;
    }
}
