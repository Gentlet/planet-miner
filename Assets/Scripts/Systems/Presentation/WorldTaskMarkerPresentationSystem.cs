using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

[UpdateInGroup(typeof(StructuralChangePresentationSystemGroup))]
public partial class WorldTaskMarkerPresentationSystem : SystemBase
{
    private const int markerRenderQueue = 3016;
    private const float markerExtent = 0.38f;
    private const float markerLineWidth = 0.12f;
    private const float itemRecoveryMarkerScale = 0.6f;

    private static readonly Color markerColor =
        new(1f, 0.15f, 0.15f, 0.9f);

    private readonly Dictionary<Entity, Entity> _markerBySourceTask = new();
    private readonly HashSet<Entity> _activeSourceTasks = new();

    private EntityQuery _demolitionTaskQuery;
    private EntityQuery _worldItemRecoveryTaskQuery;
    private EntityQuery _markerQuery;
    private Mesh _markerMesh;
    private Material _markerMaterial;
    private RenderMeshArray _renderMeshArray;
    private RenderMeshDescription _renderMeshDescription;
    private MaterialMeshInfo _materialMeshInfo;

    protected override void OnCreate()
    {
        _demolitionTaskQuery = GetEntityQuery(
            ComponentType.ReadOnly<DroneTask>(),
            ComponentType.ReadOnly<DroneTaskStatus>(),
            ComponentType.ReadOnly<DroneDemolitionTaskData>());
        _worldItemRecoveryTaskQuery = GetEntityQuery(
            ComponentType.ReadOnly<DroneTask>(),
            ComponentType.ReadOnly<DroneTaskStatus>(),
            ComponentType.ReadOnly<DroneWorldItemRecoveryTaskData>());
        _markerQuery = GetEntityQuery(
            ComponentType.ReadOnly<WorldTaskMarker>());

        if (!TryCreateRenderingResources())
            Enabled = false;
    }

    protected override void OnUpdate()
    {
        CollectExistingMarkers();
        _activeSourceTasks.Clear();

        using NativeArray<Entity> demolitionTasks =
            _demolitionTaskQuery.ToEntityArray(Allocator.Temp);

        for (int index = 0; index < demolitionTasks.Length; index++)
            UpdateDemolitionTaskMarker(demolitionTasks[index]);

        using NativeArray<Entity> worldItemRecoveryTasks =
            _worldItemRecoveryTaskQuery.ToEntityArray(Allocator.Temp);

        for (int index = 0; index < worldItemRecoveryTasks.Length; index++)
            UpdateWorldItemRecoveryTaskMarker(worldItemRecoveryTasks[index]);

        RemoveInactiveMarkers();
    }

    protected override void OnDestroy()
    {
        if (_markerMesh != null)
            DestroyRenderingObject(_markerMesh);

        if (_markerMaterial != null)
            DestroyRenderingObject(_markerMaterial);
    }

    private void CollectExistingMarkers()
    {
        _markerBySourceTask.Clear();

        using NativeArray<Entity> markerEntities =
            _markerQuery.ToEntityArray(Allocator.Temp);

        for (int index = 0; index < markerEntities.Length; index++)
        {
            Entity markerEntity = markerEntities[index];
            WorldTaskMarker marker = EntityManager
                .GetComponentData<WorldTaskMarker>(markerEntity);

            if (marker.sourceTask == Entity.Null)
            {
                EntityManager.DestroyEntity(markerEntity);
                continue;
            }

            if (_markerBySourceTask.TryAdd(marker.sourceTask, markerEntity))
                continue;

            EntityManager.DestroyEntity(markerEntity);
        }
    }

    private void UpdateDemolitionTaskMarker(Entity taskEntity)
    {
        DroneTaskStatus status = EntityManager
            .GetComponentData<DroneTaskStatus>(taskEntity);

        if (IsTerminalState(status.state))
            return;

        DroneDemolitionTaskData taskData = EntityManager
            .GetComponentData<DroneDemolitionTaskData>(taskEntity);
        Entity targetBuilding = taskData.targetBuilding;

        if (!CanDisplayDemolitionMarker(targetBuilding))
            return;

        _activeSourceTasks.Add(taskEntity);

        if (!_markerBySourceTask.TryGetValue(
                taskEntity,
                out Entity markerEntity))
        {
            markerEntity = CreateMarkerEntity(
                taskEntity,
                targetBuilding,
                WorldTaskMarkerTypeEnum.Demolition);
            _markerBySourceTask.Add(taskEntity, markerEntity);
        }

        UpdateDemolitionMarkerTransform(markerEntity, targetBuilding);
    }

    private void UpdateWorldItemRecoveryTaskMarker(Entity taskEntity)
    {
        DroneTaskStatus status = EntityManager
            .GetComponentData<DroneTaskStatus>(taskEntity);

        if (IsTerminalState(status.state))
            return;

        DroneWorldItemRecoveryTaskData taskData = EntityManager
            .GetComponentData<DroneWorldItemRecoveryTaskData>(taskEntity);
        Entity targetItem = taskData.itemEntity;

        if (!CanDisplayWorldItemRecoveryMarker(targetItem))
            return;

        _activeSourceTasks.Add(taskEntity);

        if (!_markerBySourceTask.TryGetValue(
                taskEntity,
                out Entity markerEntity))
        {
            markerEntity = CreateMarkerEntity(
                taskEntity,
                targetItem,
                WorldTaskMarkerTypeEnum.ItemRecovery);
            _markerBySourceTask.Add(taskEntity, markerEntity);
        }

        UpdateWorldItemRecoveryMarkerTransform(markerEntity, targetItem);
    }

    private bool CanDisplayDemolitionMarker(Entity buildingEntity)
    {
        if (buildingEntity == Entity.Null)
            return false;

        if (!EntityManager.Exists(buildingEntity))
            return false;

        return EntityManager.HasComponent<BuildingOccupant>(buildingEntity) &&
               EntityManager.HasComponent<BuildingType>(buildingEntity) &&
               EntityManager.HasComponent<GridPosition>(buildingEntity) &&
               EntityManager.HasComponent<BuildingFootprint>(buildingEntity) &&
               EntityManager.HasComponent<Direction>(buildingEntity);
    }

    private bool CanDisplayWorldItemRecoveryMarker(Entity itemEntity)
    {
        if (itemEntity == Entity.Null)
            return false;

        if (!EntityManager.Exists(itemEntity))
            return false;

        if (!EntityManager.HasComponent<Item>(itemEntity))
            return false;

        if (!EntityManager.HasComponent<GridPosition>(itemEntity))
            return false;

        if (!EntityManager.HasComponent<LocalTransform>(itemEntity))
            return false;

        if (EntityManager.HasComponent<StoredItem>(itemEntity))
            return false;

        return !EntityManager.HasComponent<Disabled>(itemEntity);
    }

    private Entity CreateMarkerEntity(
        Entity sourceTask,
        Entity targetEntity,
        WorldTaskMarkerTypeEnum markerType)
    {
        Entity markerEntity = EntityManager.CreateEntity(
            typeof(WorldTaskMarker),
            typeof(LocalTransform),
            typeof(PostTransformMatrix));
        EntityManager.SetComponentData(markerEntity, new WorldTaskMarker
        {
            sourceTask = sourceTask,
            targetEntity = targetEntity,
            type = markerType
        });
        RenderMeshUtility.AddComponents(
            markerEntity,
            EntityManager,
            _renderMeshDescription,
            _renderMeshArray,
            _materialMeshInfo);
        return markerEntity;
    }

    private void UpdateDemolitionMarkerTransform(
        Entity markerEntity,
        Entity buildingEntity)
    {
        GridPosition gridPosition = EntityManager
            .GetComponentData<GridPosition>(buildingEntity);
        BuildingFootprint footprint = EntityManager
            .GetComponentData<BuildingFootprint>(buildingEntity);
        Direction direction = EntityManager
            .GetComponentData<Direction>(buildingEntity);
        int2 size = footprint.size;
        float2 centerOffset = BuildingFootprintUtility.GetVisualCenterOffset(
            size,
            direction.dir);
        int2 visualSize = BuildingFootprintUtility.NormalizeSize(size);
        LocalTransform localTransform = new()
        {
            Position = new float3(
                gridPosition.gridPosition.x + centerOffset.x,
                gridPosition.gridPosition.y + centerOffset.y,
                0f),
            Rotation = quaternion.RotateZ(
                math.radians(direction.dir.ToDegrees())),
            Scale = 1f
        };
        PostTransformMatrix postTransform = new()
        {
            Value = float4x4.Scale(new float3(
                visualSize.x,
                visualSize.y,
                1f))
        };
        LocalToWorld localToWorld = new()
        {
            Value = math.mul(localTransform.ToMatrix(), postTransform.Value)
        };

        EntityManager.SetComponentData(markerEntity, localTransform);
        EntityManager.SetComponentData(markerEntity, postTransform);
        EntityManager.SetComponentData(markerEntity, localToWorld);
    }

    private void UpdateWorldItemRecoveryMarkerTransform(
        Entity markerEntity,
        Entity itemEntity)
    {
        LocalTransform itemTransform = EntityManager
            .GetComponentData<LocalTransform>(itemEntity);
        LocalTransform markerTransform = LocalTransform.FromPosition(
            new float3(
                itemTransform.Position.x,
                itemTransform.Position.y,
                0f));
        PostTransformMatrix postTransform = new()
        {
            Value = float4x4.Scale(itemRecoveryMarkerScale)
        };
        LocalToWorld localToWorld = new()
        {
            Value = math.mul(markerTransform.ToMatrix(), postTransform.Value)
        };

        EntityManager.SetComponentData(markerEntity, markerTransform);
        EntityManager.SetComponentData(markerEntity, postTransform);
        EntityManager.SetComponentData(markerEntity, localToWorld);
    }

    private void RemoveInactiveMarkers()
    {
        foreach (KeyValuePair<Entity, Entity> pair in _markerBySourceTask)
        {
            if (_activeSourceTasks.Contains(pair.Key))
                continue;

            if (EntityManager.Exists(pair.Value))
                EntityManager.DestroyEntity(pair.Value);
        }
    }

    private bool TryCreateRenderingResources()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
        {
            Debug.LogError(
                "Failed to find the URP Unlit shader for world-task markers.");
            return false;
        }

        _markerMesh = CreateMarkerMesh();
        _markerMaterial = CreateMarkerMaterial(shader);
        _renderMeshArray = new RenderMeshArray(
            new[] { _markerMaterial },
            new[] { _markerMesh });
        _renderMeshDescription = new RenderMeshDescription(
            ShadowCastingMode.Off,
            receiveShadows: false,
            motionVectorGenerationMode: MotionVectorGenerationMode.ForceNoMotion,
            layer: 0,
            renderingLayerMask: 1,
            lightProbeUsage: LightProbeUsage.Off);
        _materialMeshInfo = MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0);
        return true;
    }

    private static Mesh CreateMarkerMesh()
    {
        var vertices = new List<Vector3>(8);
        var triangles = new List<int>(12);
        AddLineQuad(
            vertices,
            triangles,
            new Vector2(-markerExtent, -markerExtent),
            new Vector2(markerExtent, markerExtent));
        AddLineQuad(
            vertices,
            triangles,
            new Vector2(-markerExtent, markerExtent),
            new Vector2(markerExtent, -markerExtent));

        var mesh = new Mesh { name = "World Task Marker Mesh" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddLineQuad(
        List<Vector3> vertices,
        List<int> triangles,
        Vector2 start,
        Vector2 end)
    {
        Vector2 direction = (end - start).normalized;
        Vector2 perpendicular = new(-direction.y, direction.x);
        Vector2 halfWidth = perpendicular * (markerLineWidth * 0.5f);
        int vertexStart = vertices.Count;

        vertices.Add(start - halfWidth);
        vertices.Add(start + halfWidth);
        vertices.Add(end + halfWidth);
        vertices.Add(end - halfWidth);
        triangles.Add(vertexStart);
        triangles.Add(vertexStart + 1);
        triangles.Add(vertexStart + 2);
        triangles.Add(vertexStart);
        triangles.Add(vertexStart + 2);
        triangles.Add(vertexStart + 3);
    }

    private static Material CreateMarkerMaterial(Shader shader)
    {
        var material = new Material(shader)
        {
            name = "World Task Marker Material",
            renderQueue = markerRenderQueue
        };
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_SrcBlend", 5f);
        material.SetFloat("_DstBlend", 10f);
        material.SetFloat("_ZWrite", 0f);
        material.SetColor("_BaseColor", markerColor);
        material.SetTexture("_BaseMap", Texture2D.whiteTexture);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        return material;
    }

    private static bool IsTerminalState(DroneTaskStateEnum state)
    {
        return state == DroneTaskStateEnum.Completed ||
               state == DroneTaskStateEnum.Cancelled;
    }

    private static void DestroyRenderingObject(Object renderingObject)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Object.DestroyImmediate(renderingObject);
            return;
        }
#endif
        Object.Destroy(renderingObject);
    }
}
