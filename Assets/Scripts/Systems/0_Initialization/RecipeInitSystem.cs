using PlanetMiner.Config;
using Unity.Entities;

/// <summary>
/// 게임 시작 시(InitializationSystemGroup) RecipeRegistryBlob을 로드하여
/// RecipeRegistry 싱글톤 엔티티를 1회 초기화하는 시스템.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class RecipeInitSystem : SystemBase
{
    private BlobAssetReference<RecipeRegistryBlob> _blobReference;

    protected override void OnCreate()
    {
        base.OnCreate();
    }

    protected override void OnUpdate()
    {
        if (SystemAPI.HasSingleton<RecipeRegistry>())
        {
            Enabled = false;
            return;
        }

        // 싱글톤 1회 생성 및 초기화
        _blobReference = InitializeRecipeRegistry(EntityManager);
        Enabled = false;
    }

    protected override void OnDestroy()
    {
        if (_blobReference.IsCreated)
        {
            _blobReference.Dispose();
        }
        base.OnDestroy();
    }

    /// <summary>
    /// RecipeRegistry 싱글톤을 초기화.
    /// jsonOverride가 주어지면 해당 JSON을 파싱하고, 없으면 Resources/Config/CrafterRecipeConfig를 사용.
    /// </summary>
    public static BlobAssetReference<RecipeRegistryBlob> InitializeRecipeRegistry(EntityManager entityManager, string jsonOverride = null)
    {
        var query = entityManager.CreateEntityQuery(typeof(RecipeRegistry));
        Entity singletonEntity;

        BlobAssetReference<RecipeRegistryBlob> blobRef;
        if (!string.IsNullOrEmpty(jsonOverride))
        {
            blobRef = RecipeConfigLoader.BuildBlobAssetFromJson(jsonOverride);
        }
        else
        {
            blobRef = RecipeConfigLoader.LoadBlobAssetFromResources();
        }

        if (query.CalculateEntityCount() > 0)
        {
            singletonEntity = query.GetSingletonEntity();
            entityManager.SetComponentData(singletonEntity, new RecipeRegistry(blobRef));
        }
        else
        {
            singletonEntity = entityManager.CreateEntity(typeof(RecipeRegistry));
            entityManager.SetComponentData(singletonEntity, new RecipeRegistry(blobRef));
        }

        return blobRef;
    }
}
