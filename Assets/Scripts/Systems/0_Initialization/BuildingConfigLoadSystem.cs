using Unity.Burst;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// 역할·목적: BuildingConfig 게시를 기다리는 기존 초기화 진입점이다. 파일 로드와 설정 게시 자체는 하지 않는다.
/// 처리 단계·입력: Initialization에서 BuildingConfig가 존재할 때 한 번 실행한다. 게시자는 같은 파일의 BuildingConfigInitSystem이다.
/// 출력·정리: 설정을 읽거나 바꾸지 않고 자신을 비활성화한다. 설정 엔티티·버퍼 수명은 World에 속하며 ECB 기록은 없다.
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
/// 역할·목적: managed 초기화 진입점에서 건물 스펙·해금 상태·자재 요구 설정을 한 번 게시한다.
/// 입력·생성: OnCreate에서 Resources의 BuildingConfigLoader.DefaultResourcePath를 읽고 Loader가 검증한 설정을 이용한다.
/// 출력·소유권: BuildingConfig와 스펙/자재 버퍼를 즉시 생성한다. 기존 singleton이 있으면 덮어쓰지 않는다.
/// 이용: 배치 검증, 공사 요구량 구성과 완공 건물/철거 환급 생성이 게시 버퍼를 읽는다.
/// 정리·가시화: 로드 실패는 오류를 기록하고 게시하지 않는다. OnUpdate 후 비활성화하며 설정은 World 수명 동안 남고 ECB는 사용하지 않는다.
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
