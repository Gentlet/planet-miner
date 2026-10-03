using System;
using PlanetMiner.Config;
using Unity.Entities;

/// <summary>
/// 게임 시작 시 RecipeRegistry와 레시피/재료/출력 버퍼를 한 번 게시한다.
/// 게시 후 읽기 전용인 설정 버퍼의 수명은 World에 속한다.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class RecipeInitSystem : SystemBase
{
    protected override void OnUpdate()
    {
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
    /// </summary>
    public static Entity InitializeRecipeRegistry(EntityManager entityManager, string jsonOverride = null)
    {
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<RecipeRegistry>());
        if (!query.IsEmptyIgnoreFilter)
        {
            throw new InvalidOperationException("RecipeRegistry is already registered. Runtime replacement is not supported.");
        }

        var config = string.IsNullOrEmpty(jsonOverride)
            ? RecipeConfigLoader.LoadFromResources()
            : RecipeConfigLoader.ParseJson(jsonOverride);
        return RecipeConfigLoader.PublishConfig(entityManager, config);
    }
}
