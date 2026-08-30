using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

public static class DemolitionAreaRequestUtility
{
    public static void CreateRequests(
        EntityManager entityManager,
        ChunkMapSystem chunkMap,
        GridBounds bounds,
        int normalPriority)
    {
        List<Entity> buildings = new();
        List<Entity> items = new();
        HashSet<Entity> constructionSites = new();
        List<int2> cells = new();

        chunkMap.GetBuildingsInBounds(bounds, buildings);

        for (int i = 0; i < buildings.Count; i++)
            CreateDemolitionRequest(entityManager, buildings[i], normalPriority);

        bounds.GetCells(cells);

        for (int i = 0; i < cells.Count; i++)
        {
            int2 cell = cells[i];

            if (chunkMap.TryGetConstructionSite(cell, out Entity siteEntity))
                constructionSites.Add(siteEntity);

            chunkMap.GetItems(cell, items);

            for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                DroneWorldItemRecoveryTaskUtility.TryCreateRequest(
                    entityManager,
                    items[itemIndex],
                    normalPriority);
            }
        }

        foreach (Entity siteEntity in constructionSites)
            CreateConstructionCancelRequest(entityManager, siteEntity, chunkMap);
    }

    private static void CreateDemolitionRequest(
        EntityManager entityManager,
        Entity buildingEntity,
        int normalPriority)
    {
        if (!entityManager.Exists(buildingEntity))
            return;

        if (!entityManager.HasComponent<GridPosition>(buildingEntity))
            return;

        int2 gridPosition = entityManager.GetComponentData<GridPosition>(buildingEntity)
            .gridPosition;
        Entity requestEntity = entityManager.CreateEntity();
        entityManager.AddComponentData(requestEntity, new DroneDemolitionRequest
        {
            gridPosition = gridPosition,
            normalPriority = normalPriority
        });
    }

    private static void CreateConstructionCancelRequest(
        EntityManager entityManager,
        Entity constructionSite,
        ChunkMapSystem chunkMap)
    {
        if (!entityManager.Exists(constructionSite))
            return;

        if (!entityManager.HasComponent<GridPosition>(constructionSite))
            return;

        int2 gridPosition = entityManager
            .GetComponentData<GridPosition>(constructionSite)
            .gridPosition;

        if (!chunkMap.TryGetConstructionSite(gridPosition, out Entity currentSite) ||
            currentSite != constructionSite)
            return;

        Entity requestEntity = entityManager.CreateEntity();
        entityManager.AddComponentData(requestEntity, new ConstructionCancelRequest
        {
            gridPosition = gridPosition
        });
    }
}
