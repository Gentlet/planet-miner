using Unity.Entities;
using Unity.Mathematics;

public partial class ChunkMapSystem
{
    private bool TryRegisterResource(EntityCommandBuffer ecb, Entity entity)
    {
        if (!EntityManager.HasComponent<ResourceOccupantRequest>(entity))
            return false;
        if (!EntityManager.HasComponent<GridPosition>(entity))
            return false;
        if (!EntityManager.HasComponent<ResourceDeposit>(entity))
            return false;

        GridPosition pos = EntityManager.GetComponentData<GridPosition>(entity);
        ResourceDeposit resource = EntityManager.GetComponentData<ResourceDeposit>(entity);
        ChunkCell cellData = GetOrCreateCellData(pos.gridPosition);

        if (!cellData.TrySetResource(resource.type, entity))
        {
            UnityEngine.Debug.LogError($"Failed ChunkCell.TrySetResource. Type : {resource.type}, Cell : {pos.gridPosition}");
            ecb.DestroyEntity(entity);
            return false;
        }

        ecb.RemoveComponent<ResourceOccupantRequest>(entity);
        ecb.AddComponent<ResourceOccupant>(entity);

        return true;
    }

    public bool TryUnregisterResource(int2 cell, Entity entity)
    {
        if (!TryGetCellData(cell, out ChunkCell cellData))
            return false;

        return cellData.TryRemoveResource(entity);
    }
}
