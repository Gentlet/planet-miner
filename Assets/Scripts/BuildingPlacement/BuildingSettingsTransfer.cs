using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

internal sealed class BuildingSettingsTransfer
{
    private struct BuildingSettingsSnapshot
    {
        public BuildingTypeEnum buildingType;
        public ItemTypeEnum crafterRecipe;
    }

    private BuildingSettingsSnapshot? _clipboard;
    private readonly HashSet<Entity> _pasteVisited = new();

    public void CopySettings(
        int2 gridCell,
        EntityManager entityManager,
        ChunkMapSystem chunkMap)
    {
        _clipboard = null;

        if (!chunkMap.TryGetBuilding(gridCell, out Entity building) ||
            !entityManager.HasComponent<Crafter>(building))
        {
            return;
        }

        Crafter crafter = entityManager.GetComponentData<Crafter>(building);
        _clipboard = new BuildingSettingsSnapshot
        {
            buildingType = BuildingTypeEnum.Crafter,
            crafterRecipe = crafter.selectedItemType
        };
    }

    public void PasteSettings(
        int2 gridCell,
        EntityManager entityManager,
        ChunkMapSystem chunkMap)
    {
        if (!_clipboard.HasValue ||
            !chunkMap.TryGetBuilding(gridCell, out Entity building) ||
            !_pasteVisited.Add(building))
        {
            return;
        }

        BuildingSettingsSnapshot snapshot = _clipboard.Value;

        if (snapshot.buildingType != BuildingTypeEnum.Crafter ||
            !entityManager.HasComponent<Crafter>(building))
        {
            return;
        }

        Crafter targetCrafter = entityManager.GetComponentData<Crafter>(building);

        if (targetCrafter.selectedItemType == snapshot.crafterRecipe)
            return;

        Entity request = entityManager.CreateEntity();
        entityManager.AddComponentData(request, new CrafterRecipeChangeRequest
        {
            crafterEntity = building,
            selectedItemType = snapshot.crafterRecipe
        });
    }

    public void ResetPasteVisited()
    {
        _pasteVisited.Clear();
    }
}
