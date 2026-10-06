using Unity.Entities;
using UnityEngine;

/// <summary>
/// 역할·목적: Initialization에서 자원·바닥 생성 설정을 한 번 검증하고 함께 게시한다.
/// 입력·생성: Resources의 WorldGenerationConfig를 Loader로 읽는다. 기존 ResourceGenerationSettings가 있으면 다시 로드하지 않는다.
/// 출력·소유권: 같은 설정 엔티티에 자원 설정/버퍼와 바닥 설정/버퍼를 즉시 게시하며 부분 게시하지 않는다.
/// 이용: 초기 청크 부트스트랩, 자원 생성과 바닥 조회·프리팹 준비 검사가 설정을 읽는다.
/// 정리·가시화: 성공/실패 뒤 시스템을 비활성화하고 실패는 오류만 기록한다. 게시 데이터는 World에 남으며 ECB 기록은 없다.
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
            out int initialChunkSize,
            out var elements,
            out var floor);

        if (success && elements != null && elements.Count > 0 && floor != null)
        {
            WorldGenerationConfigLoader.PublishConfig(state.EntityManager, worldSeed, initialChunkSize, elements, floor);
        }
        else
        {
            // 검증 실패 시 설정을 부분 게시하지 않는다. 오류를 기록하고 이 로더의 자동 재시도를 종료한다.
            Debug.LogError("[WorldGenerationConfigLoadSystem] Failed to load or validate WorldGenerationConfig. Config will not be published.");
        }

        state.Enabled = false;
    }
}
