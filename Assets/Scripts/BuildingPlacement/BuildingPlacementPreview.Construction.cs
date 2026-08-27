using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public partial class BuildingPlacementPreview
{
    [SerializeField]
    private Color constructionSiteColor = new(0.25f, 0.65f, 1f, 0.45f);

    private readonly Dictionary<Entity, GameObject> _constructionSitePreviews = new();
    private readonly List<Entity> _constructionSitePreviewRemovals = new();

    private ChunkMapSystem _chunkMap;
    private EntityQuery _constructionSiteQuery;

    private void InitializeConstructionSitePresentation()
    {
        World world = World.DefaultGameObjectInjectionWorld;

        if (world == null)
            return;

        _entityManager = world.EntityManager;
        _chunkMap = world.GetExistingSystemManaged<ChunkMapSystem>();
        _constructionSiteQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<ConstructionSite>(),
            ComponentType.ReadOnly<GridPosition>());
    }

    private void LateUpdate()
    {
        UpdateConstructionSitePreviews();
    }

    private void UpdateConstructionSitePreviews()
    {
        if (_chunkMap == null)
            return;

        _constructionSitePreviewRemovals.Clear();

        foreach (Entity siteEntity in _constructionSitePreviews.Keys)
        {
            if (!_entityManager.Exists(siteEntity) ||
                !_entityManager.HasComponent<ConstructionSite>(siteEntity))
            {
                _constructionSitePreviewRemovals.Add(siteEntity);
            }
        }

        for (int index = 0; index < _constructionSitePreviewRemovals.Count; index++)
            DestroyConstructionSitePreview(_constructionSitePreviewRemovals[index]);

        using NativeArray<Entity> sites =
            _constructionSiteQuery.ToEntityArray(Allocator.Temp);

        for (int index = 0; index < sites.Length; index++)
            UpdateConstructionSitePreview(sites[index]);
    }

    private void UpdateConstructionSitePreview(Entity siteEntity)
    {
        ConstructionSite site = _entityManager.GetComponentData<ConstructionSite>(siteEntity);
        GridPosition gridPosition = _entityManager.GetComponentData<GridPosition>(siteEntity);
        int2 size = _chunkMap.GetBuildingSize(site.type);

        if (!_constructionSitePreviews.TryGetValue(siteEntity, out GameObject previewObject))
        {
            previewObject = Instantiate(previewPrefab);
            _constructionSitePreviews.Add(siteEntity, previewObject);
        }

        previewObject.SetActive(true);
        ApplyConstructionSiteAppearance(
            previewObject,
            site.type,
            gridPosition.gridPosition,
            size,
            site.direction);
    }

    private void ApplyConstructionSiteAppearance(
        GameObject previewObject,
        BuildingTypeEnum buildingType,
        int2 anchor,
        int2 size,
        DirectionEnum direction)
    {
        SpriteRenderer renderer = previewObject.GetComponent<SpriteRenderer>();

        if (renderer == null)
            return;

        if (!TryGetPreviewSprite(buildingType, out Sprite sprite))
        {
            previewObject.SetActive(false);
            return;
        }

        float2 visualCenterOffset =
            BuildingFootprintUtility.GetVisualCenterOffset(size, direction);
        int2 visualSize = BuildingFootprintUtility.NormalizeSize(size);
        Vector2 spriteSize = sprite.bounds.size;

        previewObject.transform.SetPositionAndRotation(
            new Vector3(
                anchor.x + visualCenterOffset.x,
                anchor.y + visualCenterOffset.y,
                0f),
            Quaternion.Euler(0f, 0f, direction.ToDegrees()));
        previewObject.transform.localScale = new Vector3(
            spriteSize.x > 0f ? visualSize.x / spriteSize.x : 1f,
            spriteSize.y > 0f ? visualSize.y / spriteSize.y : 1f,
            1f);
        renderer.sprite = sprite;
        renderer.color = constructionSiteColor;
    }

    private bool TryGetPreviewSprite(
        BuildingTypeEnum buildingType,
        out Sprite sprite)
    {
        for (int index = 0; index < previewSprites.Count; index++)
        {
            if (previewSprites[index].type != buildingType)
                continue;

            sprite = previewSprites[index].sprite;
            return sprite != null;
        }

        sprite = null;
        return false;
    }

    private void DestroyConstructionSitePreview(Entity siteEntity)
    {
        if (!_constructionSitePreviews.Remove(siteEntity, out GameObject previewObject))
            return;

        if (previewObject != null)
            Destroy(previewObject);
    }

    private void DisposeConstructionSitePresentation()
    {
        foreach (GameObject previewObject in _constructionSitePreviews.Values)
        {
            if (previewObject != null)
                Destroy(previewObject);
        }

        _constructionSitePreviews.Clear();
        _constructionSitePreviewRemovals.Clear();
    }
}
