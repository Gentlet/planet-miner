using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public partial class BuildingSpawnSystem : SystemBase
{
    private const int DefaultStorageCapacity = 10;
    private ChunkMapSystem _chunkMap;

    protected override void OnCreate()
    {
        _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();
        RequireForUpdate<BuildingPrefabElement>();
        RequireForUpdate<BuildingSpawnRequest>();
    }

    protected override void OnUpdate()
    {
        if (_chunkMap == null)
        {
            _chunkMap = World.GetExistingSystemManaged<ChunkMapSystem>();

            if (_chunkMap == null)
                return;
        }

        DynamicBuffer<BuildingPrefabElement> definitions =
            SystemAPI.GetSingletonBuffer<BuildingPrefabElement>(true);
        EntityCommandBuffer ecb = SystemAPI
            .GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(World.Unmanaged);

        foreach (var (request, requestEntity) in
                 SystemAPI.Query<RefRO<BuildingSpawnRequest>>().WithEntityAccess())
        {
            BuildingSpawnRequest spawnRequest = request.ValueRO;

            if (!TryFindDefinition(
                    definitions,
                    spawnRequest.type,
                    out BuildingPrefabElement definition) ||
                definition.prefab == Entity.Null)
            {
                _chunkMap.ReleaseBuildingReservation(
                    spawnRequest.gridPosition,
                    spawnRequest.type,
                    spawnRequest.dir);
                ecb.DestroyEntity(requestEntity);
                continue;
            }

            Entity instance = ecb.Instantiate(definition.prefab);
            int2 anchor = spawnRequest.gridPosition;
            DirectionEnum direction = spawnRequest.dir;
            float2 visualCenterOffset =
                BuildingFootprintUtility.GetVisualCenterOffset(
                    definition.size,
                    direction);

            ecb.SetComponent(
                instance,
                LocalTransform.FromPositionRotation(
                    new float3(
                        anchor.x + visualCenterOffset.x,
                        anchor.y + visualCenterOffset.y,
                        0f),
                    quaternion.RotateZ(
                        Mathf.Deg2Rad * direction.ToDegrees())));
            ecb.AddComponent(instance, new BuildingType { type = spawnRequest.type });
            ecb.AddComponent(instance, new GridPosition { gridPosition = anchor });
            ecb.AddComponent(instance, new Direction { dir = direction });
            ecb.AddComponent(instance, new BuildingOccupantRequest());

            AddBuildingBehavior(ref ecb, instance, spawnRequest, anchor);
            ecb.DestroyEntity(requestEntity);
        }
    }

    private static void AddBuildingBehavior(
        ref EntityCommandBuffer ecb,
        Entity instance,
        BuildingSpawnRequest request,
        int2 anchor)
    {
        switch (request.type)
        {
            case BuildingTypeEnum.Belt:
                ecb.AddComponent(instance, new Belt { speed = 10f });
                break;
            case BuildingTypeEnum.Miner:
                uint randomState = math.hash(anchor);
                ecb.AddComponent(instance, new Miner
                {
                    speed = 0.1f,
                    randomState = randomState == 0 ? 1u : randomState
                });
                ecb.AddComponent(instance, new BuildingOutputCursor());
                ecb.AddBuffer<ProducedItemElement>(instance);
                break;
            case BuildingTypeEnum.Crafter:
                ItemTypeEnum selectedItemType = request.selectedItemType;
                ecb.AddComponent(instance, new Crafter
                {
                    speed = 1f,
                    selectedItemType = selectedItemType,
                    progress = 0f,
                    state = selectedItemType.IsValid()
                        ? CrafterStateEnum.Idle
                        : CrafterStateEnum.NoRecipe
                });
                ecb.AddComponent(instance, new BuildingOutputCursor());
                ecb.AddBuffer<StoredItemElement>(instance);
                ecb.AddBuffer<ProducedItemElement>(instance);
                break;
            case BuildingTypeEnum.Splitter:
                ecb.AddComponent(instance, new Splitter
                {
                    nextOutputDirection = request.dir
                });
                break;
            case BuildingTypeEnum.Merger:
                ecb.AddComponent(instance, new Merger
                {
                    nextInputDirection =
                        request.dir.NextDirection().NextDirection()
                });
                break;
            case BuildingTypeEnum.Storage:
                ecb.AddComponent(instance, new Storage
                {
                    capacity = DefaultStorageCapacity
                });
                ecb.AddComponent(instance, new BuildingOutputCursor());
                ecb.AddBuffer<StoredItemElement>(instance);
                break;
        }
    }

    private static bool TryFindDefinition(
        DynamicBuffer<BuildingPrefabElement> definitions,
        BuildingTypeEnum type,
        out BuildingPrefabElement definition)
    {
        for (int i = 0; i < definitions.Length; i++)
        {
            if (definitions[i].type != type)
                continue;

            definition = definitions[i];
            return true;
        }

        definition = default;
        return false;
    }
}
