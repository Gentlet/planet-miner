using System;
using Unity.Entities;

/// <summary>
/// 역할·목적: BuildingConfig 게시를 기다리는 기존 초기화 진입점이다. 파일 로드와 설정 게시 자체는 하지 않는다.
/// 처리 단계·입력: Initialization에서 BuildingConfig가 존재할 때 한 번 실행한다. 게시자는 같은 파일의 BuildingConfigInitSystem이다.
/// 출력·정리: 설정을 읽거나 바꾸지 않고 자신을 비활성화한다. 설정 엔티티·버퍼 수명은 World에 속하며 ECB 기록은 없다.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
[UpdateAfter(typeof(BuildingConfigInitSystem))]
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
/// 역할·목적: managed 초기화 진입점에서 건물 스펙·해금 상태·자재 요구 설정을 한 번 게시한다.
/// 입력·생성: ItemConfig 게시 뒤 건물·자재 JSON을 읽고 같은 World의 품목 ID로 해석한다.
/// 출력·소유권: BuildingConfig와 스펙/자재 버퍼를 즉시 생성한다. 기존 singleton이 있으면 덮어쓰지 않는다.
/// 이용: 배치 검증, 공사 요구량 구성과 완공 건물/철거 환급 생성이 게시 버퍼를 읽는다.
/// 정리·가시화: 생성 JSON 로드·해석·연결, 게시 또는 사전 등록 검증에 실패하면 즉시 Fatal을 기록한다. 설정은 World 수명 동안 남고 ECB는 사용하지 않는다.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
[UpdateAfter(typeof(ItemConfigInitSystem))]
public partial class BuildingConfigInitSystem : SystemBase
{
    protected override void OnUpdate()
    {
        LoadAndPublish();
        Enabled = false;
    }

    private void LoadAndPublish()
    {
        if (SimulationFailureUtility.HasFatalError(EntityManager))
        {
            return;
        }

        try
        {
            if (SystemAPI.TryGetSingletonEntity<BuildingConfig>(out var entity))
            {
                if (!BuildingConfigLoader.ValidatePublishedConfig(EntityManager, entity))
                {
                    ReportFailure("Pre-registered BuildingConfig is incomplete.");
                    return;
                }
                // 사전 등록 스펙은 보존하고 아직 없는 연구 효과의 초기 상태만 준비한다.
                if (!EntityManager.HasComponent<ResearchStatBonuses>(entity))
                {
                    EntityManager.AddComponent<ResearchStatBonuses>(entity);
                }
                return;
            }

            var itemIds = ItemConfigInitSystem.ReadRegisteredItemIds(EntityManager);
            if (!BuildingConfigLoader.TryLoadConfigFromResources(
                    BuildingConfigLoader.DefaultResourcePath, out var configs, out var materials, itemIds))
            {
                ReportFailure("Failed to load generated BuildingConfig. Config will not be published.");
                return;
            }

            if (BuildingConfigLoader.PublishValidatedConfig(EntityManager, configs, materials) == Entity.Null)
            {
                ReportFailure("Failed to publish BuildingConfig.");
            }
        }
        catch (Exception exception)
        {
            ReportFailure(exception.Message);
        }
    }

    private void ReportFailure(string reason)
    {
        SimulationFailureUtility.RecordInitializationFailure(EntityManager,
            $"[BuildingConfigInitSystem] {reason} Game simulation stopped.",
            "Building configuration initialization failed. See error log.");
    }
}
