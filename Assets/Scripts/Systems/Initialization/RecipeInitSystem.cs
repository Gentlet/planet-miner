using System;
using PlanetMiner.Config;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// 역할·목적: Initialization에서 레시피와 재료/출력 목록을 한 번 게시한다.
/// 입력·생성: RecipeConfigLoader의 Resources 설정 또는 사전 등록 API의 JSON을 검증한다.
/// 출력·소유권: RecipeRegistry와 세 설정 버퍼를 즉시 게시한다. 기존 Registry의 필수 버퍼를 확인하고 중복 게시 API 호출은 거부한다.
/// 이용: 레시피 Command, 제작 Decision/Execution과 입력 슬롯/검증 경계가 읽기 전용 설정을 이용한다.
/// 정리·가시화: 성공 후 시스템을 비활성화하고 설정 엔티티는 World 수명 동안 보존한다. ECB와 런타임 설정 교체를 사용하지 않는다.
/// 실패: 파싱/게시 검증 오류는 로그와 즉시 SimulationFatalError로 게임 시뮬레이션을 차단하고 재시도하지 않는다.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class RecipeInitSystem : SystemBase
{
    protected override void OnUpdate()
    {
        using var fatalErrorQuery = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<SimulationFatalError>());
        if (!fatalErrorQuery.IsEmptyIgnoreFilter)
        {
            Enabled = false;
            return;
        }

        using var query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<RecipeRegistry>());
        if (!query.IsEmptyIgnoreFilter)
        {
            Entity registryEntity = query.GetSingletonEntity();
            if (!EntityManager.HasBuffer<RecipeConfigElement>(registryEntity) ||
                !EntityManager.HasBuffer<RecipeIngredientElement>(registryEntity) ||
                !EntityManager.HasBuffer<RecipeOutputElement>(registryEntity))
            {
                throw new InvalidOperationException("The pre-registered RecipeRegistry requires recipe, ingredient and output buffers.");
            }

            Enabled = false;
            return;
        }

        InitializeRecipeRegistry(EntityManager);
        Enabled = false;
    }

    /// <summary>
    /// 읽는 시스템을 실행하기 전 World당 한 번 게시한다. 사전 등록도 이 API를 사용한다.
    /// 기존 Registry가 있으면 입력을 읽거나 변경하기 전에 거부한다. 실행 중 교체/삭제는 지원하지 않는다.
    /// 반환 엔티티와 버퍼는 World가 소유하므로 호출자와 Init 시스템은 따로 해제하지 않는다.
    /// 입력 검증 실패는 설정을 게시하지 않고 중단 오류를 남긴 뒤 Entity.Null을 반환한다.
    /// </summary>
    public static Entity InitializeRecipeRegistry(EntityManager entityManager, string jsonOverride = null)
    {
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<RecipeRegistry>());
        if (!query.IsEmptyIgnoreFilter)
        {
            throw new InvalidOperationException("RecipeRegistry is already registered. Runtime replacement is not supported.");
        }

        try
        {
            var config = string.IsNullOrEmpty(jsonOverride)
                ? RecipeConfigLoader.LoadFromResources()
                : RecipeConfigLoader.ParseJson(jsonOverride);
            return RecipeConfigLoader.PublishConfig(entityManager, config);
        }
        catch (ArgumentException exception)
        {
            Debug.LogError($"[RecipeInitSystem] {exception.Message} Game simulation stopped.");
            Entity error = entityManager.CreateEntity(typeof(SimulationFatalError));
            entityManager.SetComponentData(error, new SimulationFatalError
            {
                Message = new FixedString128Bytes("Recipe initialization failed. See error log.")
            });
            return Entity.Null;
        }
    }
}
