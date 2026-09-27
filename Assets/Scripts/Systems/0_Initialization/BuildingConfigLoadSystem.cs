using Unity.Burst;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// 게임 시작 시(InitializationSystemGroup) BuildingConfig.json을 읽어
/// 검증된 건물별 통합 설정(스펙, 해금 상태, 건설 자재 요구량)을
/// BuildingConfig 싱글톤 엔티티와 버퍼로 1회 게시하는 시스템.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct BuildingConfigLoadSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BuildingConfig>();
    }

    public void OnUpdate(ref SystemState state)
    {
        // 1회 실행 후 비활성화
        state.Enabled = false;
    }
}

/// <summary>
/// Managed 진입점에서 BuildingConfig.json을 로드하는 초기화 시스템.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class BuildingConfigInitSystem : SystemBase
{
    protected override void OnCreate()
    {
        base.OnCreate();
        LoadAndPublish();
    }

    protected override void OnUpdate()
    {
        Enabled = false;
    }

    private void LoadAndPublish()
    {
        if (SystemAPI.HasSingleton<BuildingConfig>())
        {
            return;
        }

        bool success = BuildingConfigLoader.TryLoadConfigFromResources(
            BuildingConfigLoader.DefaultResourcePath,
            out var configs,
            out var materials);

        if (success)
        {
            BuildingConfigLoader.PublishConfig(EntityManager, configs, materials);
        }
        else
        {
            Debug.LogError("[BuildingConfigInitSystem] Failed to load or validate BuildingConfig. Config will not be published.");
        }
    }
}
