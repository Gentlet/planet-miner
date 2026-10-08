using System;
using Unity.Entities;

/// <summary>
/// 역할·목적: Initialization에서 자원·바닥 생성 설정을 한 번 검증하고 함께 게시한다.
/// 입력·생성: Resources의 WorldGenerationConfig를 읽고 사전 등록도 자원/바닥 전체 구성을 확인한다. 기존 설정을 다시 로드하지 않는다.
/// 출력·소유권: 같은 설정 엔티티에 자원 설정/버퍼와 바닥 설정/버퍼를 즉시 게시하며 부분 게시하지 않는다.
/// 이용: 초기 청크 부트스트랩, 자원 생성과 바닥 조회·프리팹 준비 검사가 설정을 읽는다.
/// 정리·가시화: 성공/실패 뒤 비활성화하고 실패는 로그·미게시·즉시 Fatal로 처리한다. 자동 재시도·ECB 기록은 없다.
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
        if (SimulationFailureUtility.HasFatalError(state.EntityManager))
        {
            state.Enabled = false;
            return;
        }

        try
        {
            if (SystemAPI.TryGetSingletonEntity<ResourceGenerationSettings>(out var existing))
            {
                if (!WorldGenerationConfigLoader.HasCompleteConfig(state.EntityManager, existing))
                {
                    ReportFailure(state.EntityManager, "Pre-registered world configuration is missing resource or floor data.");
                }
                state.Enabled = false;
                return;
            }

            bool success = WorldGenerationConfigLoader.TryLoadConfigFromResources(
                WorldGenerationConfigLoader.DefaultResourcePath,
                out uint worldSeed, out int initialChunkSize, out var elements, out var floor);
            if (success)
            {
                WorldGenerationConfigLoader.PublishConfig(state.EntityManager, worldSeed, initialChunkSize, elements, floor);
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
