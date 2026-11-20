using System;
using Unity.Entities;

/// <summary>
/// 역할/목적: Initialization에서 자원/바닥 설정을 검증하고 새 설정을 함께 게시하거나 사전 등록 원본을 보존한다.
/// 입력·생성: Resources의 WorldGenerationConfig를 읽고 사전 등록도 자원/바닥 전체 구성을 확인한다. 기존 설정을 다시 로드하지 않는다.
/// 출력/소유권: 새 설정은 같은 엔티티에 자원/바닥 구성 전체를 즉시 게시한다. 사전 등록은 검증만 수행하며 원본을 교체하지 않는다.
/// 이용: 초기 청크 부트스트랩, 자원 생성과 바닥 조회·프리팹 준비 검사가 설정을 읽는다.
/// 정리·가시화: 성공/실패 뒤 비활성화하고 실패는 로그·미게시·즉시 Fatal로 처리한다. 자동 재시도·ECB 기록은 없다.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
[UpdateAfter(typeof(ItemConfigInitSystem))]
public partial struct WorldGenerationConfigLoadSystem : ISystem
{

    public void OnUpdate(ref SystemState state)
    {
        if (SimulationFailureUtility.HasFatalError(state.EntityManager))
        {
            state.Enabled = false;
            return;
        }

        try
        {
            using var query = state.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ResourceGenerationSettings>());
            int registered = query.CalculateEntityCount();
            if (registered > 1)
            {
                ReportFailure(state.EntityManager, "Duplicate ResourceGenerationSettings.");
                state.Enabled = false;
                return;
            }
            if (registered == 1)
            {
                Entity existing = query.GetSingletonEntity();
                if (!WorldGenerationConfigLoader.TryValidatePreRegisteredConfig(state.EntityManager, existing, out string error))
                {
                    ReportFailure(state.EntityManager, error);
                }
                state.Enabled = false;
                return;
            }

            var itemIds = ItemConfigInitSystem.ReadRegisteredItemIds(state.EntityManager);
            bool success = WorldGenerationConfigLoader.TryLoadConfigFromResources(
                WorldGenerationConfigLoader.DefaultResourcePath,
                out var config, itemIds);
            if (success)
            {
                WorldGenerationConfigLoader.PublishConfig(state.EntityManager, config);
            }
            else
            {
                ReportFailure(state.EntityManager, "Failed to load or validate WorldGenerationConfig. Config will not be published.");
            }
        }
        catch (Exception exception)
        {
            ReportFailure(state.EntityManager, exception.Message);
        }

        state.Enabled = false;
    }

    private static void ReportFailure(EntityManager entityManager, string reason)
    {
        SimulationFailureUtility.RecordInitializationFailure(entityManager,
            $"[WorldGenerationConfigLoadSystem] {reason} Game simulation stopped.",
            "World configuration initialization failed. See error log.");
    }
}
