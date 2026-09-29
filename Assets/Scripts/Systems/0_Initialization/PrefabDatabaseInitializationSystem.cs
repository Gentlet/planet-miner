using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Scenes;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// 요청된 SubScene 로딩 후 필수 프리팹 계약을 검증한다. 성공 전/실패 후에는 게임 그룹을 실행하지 않는다.
/// 실패는 영구 중단이며 자동 재시도하지 않는다. Ready 이후 DB는 월드 수명 동안 불변이어야 한다.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup), OrderLast = true)]
[UpdateAfter(typeof(SceneSystemGroup))]
[UpdateAfter(typeof(WorldGenerationConfigLoadSystem))]
public partial class PrefabDatabaseInitializationSystem : SystemBase
{
    private EntityQuery _sceneQuery;

    protected override void OnCreate()
    {
        _sceneQuery = GetEntityQuery(ComponentType.ReadOnly<SceneReference>(), ComponentType.ReadOnly<RequestSceneLoaded>());
    }

    protected override void OnUpdate()
    {
        using var scenes = _sceneQuery.ToEntityArray(Allocator.Temp);
        bool loading = false;
        foreach (Entity scene in scenes)
        {
            var status = SceneSystem.GetSceneStreamingState(World.Unmanaged, scene);
            if (status == SceneSystem.SceneStreamingState.FailedLoadingSceneHeader ||
                status == SceneSystem.SceneStreamingState.LoadedWithSectionErrors)
            {
                Fail("SubScene loading failed.");
                return;
            }
            if (status != SceneSystem.SceneStreamingState.LoadedSuccessfully)
            {
                loading = true;
            }
        }
        if (loading)
        {
            return;
        }

        if (!ValidateBuildings(out string error) || !ValidateItems(out error) || !ValidateResources(out error))
        {
            Fail(error);
            return;
        }

        EntityManager.CreateEntity(typeof(PrefabDatabaseReady));
        Enabled = false;
    }

    private void Fail(string reason)
    {
        Debug.LogError($"[PrefabDatabaseInitializationSystem] {reason} Game simulation stopped.");
        Entity error = EntityManager.CreateEntity(typeof(SimulationFatalError));
        EntityManager.SetComponentData(error, new SimulationFatalError { Message = new FixedString128Bytes("Prefab database initialization failed. See error log.") });
        Enabled = false;
    }

    private bool TryGetDatabase<TTag, TElement>(out Entity database, out string error)
        where TTag : unmanaged, IComponentData
        where TElement : unmanaged, IBufferElementData
    {
        using var tags = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TTag>());
        using var buffers = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TElement>());
        database = Entity.Null;
        error = null;
        if (tags.CalculateEntityCount() != 1 || buffers.CalculateEntityCount() != 1)
        {
            error = $"Expected exactly one {typeof(TTag).Name} and its mapping buffer.";
            return false;
        }
        database = tags.GetSingletonEntity();
        if (!EntityManager.HasBuffer<TElement>(database))
        {
            error = $"{typeof(TTag).Name} has no mapping buffer.";
            return false;
        }
        return true;
    }

    private bool ValidatePrefab(Entity prefab, string label, out string error)
    {
        error = null;
        if (prefab == Entity.Null)
        {
            error = $"{label}: Null prefab.";
            return false;
        }
        if (!EntityManager.Exists(prefab))
        {
            error = $"{label}: prefab entity no longer exists.";
            return false;
        }
        if (!EntityManager.HasComponent<Prefab>(prefab) || !EntityManager.HasComponent<LocalTransform>(prefab))
        {
            error = $"{label}: Prefab and LocalTransform are required.";
            return false;
        }
        return true;
    }

    private bool ValidateBuildings(out string error)
    {
        if (!TryGetDatabase<BuildingPrefabDatabase, BuildingPrefabElement>(out Entity database, out error))
        {
            return false;
        }
        var entries = EntityManager.GetBuffer<BuildingPrefabElement>(database, true);
        var types = new HashSet<BuildingTypeEnum>();
        foreach (var entry in entries)
        {
            if (entry.Type <= BuildingTypeEnum.None || entry.Type >= BuildingTypeEnum.Count ||
                entry.Type == BuildingTypeEnum.ConstructionSite || !types.Add(entry.Type))
            {
                error = $"Invalid or duplicate building prefab type: {entry.Type}.";
                return false;
            }
            if (!ValidatePrefab(entry.Prefab, $"Building {entry.Type}", out error))
            {
                return false;
            }
        }
        foreach (BuildingTypeEnum type in Enum.GetValues(typeof(BuildingTypeEnum)))
        {
            if (type == BuildingTypeEnum.None || type == BuildingTypeEnum.Count || type == BuildingTypeEnum.ConstructionSite)
            {
                continue;
            }
            if (!types.Contains(type))
            {
                error = $"Missing building prefab: {type}.";
                return false;
            }
        }
        return true;
    }

    private bool ValidateItems(out string error)
    {
        if (!TryGetDatabase<ItemPrefabDatabase, ItemPrefabElement>(out Entity database, out error))
        {
            return false;
        }
        var entries = EntityManager.GetBuffer<ItemPrefabElement>(database, true);
        var types = new HashSet<ItemTypeEnum>();
        foreach (var entry in entries)
        {
            if (entry.Type == ItemTypeEnum.None || !Enum.IsDefined(typeof(ItemTypeEnum), entry.Type) || !types.Add(entry.Type))
            {
                error = $"Invalid or duplicate item prefab type: {entry.Type}.";
                return false;
            }
            if (!ValidatePrefab(entry.Prefab, $"Item {entry.Type}", out error))
            {
                return false;
            }
            if (!EntityManager.HasComponent<ItemIdentity>(entry.Prefab))
            {
                error = $"Item {entry.Type}: ItemIdentity is required.";
                return false;
            }
            if (EntityManager.GetComponentData<ItemIdentity>(entry.Prefab).Type != entry.Type)
            {
                error = $"Item {entry.Type}: ItemIdentity does not match the DB entry.";
                return false;
            }
        }
        foreach (ItemTypeEnum type in Enum.GetValues(typeof(ItemTypeEnum)))
        {
            if (type != ItemTypeEnum.None && !types.Contains(type))
            {
                error = $"Missing item prefab: {type}.";
                return false;
            }
        }
        return true;
    }

    private bool ValidateResources(out string error)
    {
        if (!TryGetDatabase<ResourcePrefabDatabase, ResourcePrefabElement>(out Entity database, out error))
        {
            return false;
        }
        var entries = EntityManager.GetBuffer<ResourcePrefabElement>(database, true);
        if (entries.IsEmpty)
        {
            error = "Resource prefab database is empty.";
            return false;
        }
        var types = new HashSet<ItemTypeEnum>();
        foreach (var entry in entries)
        {
            if (entry.ResourceType == ItemTypeEnum.None || !Enum.IsDefined(typeof(ItemTypeEnum), entry.ResourceType) || !types.Add(entry.ResourceType))
            {
                error = $"Invalid or duplicate resource prefab type: {entry.ResourceType}.";
                return false;
            }
            if (!ValidatePrefab(entry.Prefab, $"Resource {entry.ResourceType}", out error))
            {
                return false;
            }
        }
        using var configs = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ResourceGenerationSettings>(), ComponentType.ReadOnly<ResourceGenerationConfigElement>());
        if (configs.CalculateEntityCount() != 1)
        {
            error = "Resource generation settings must be published before prefab validation.";
            return false;
        }
        var requirements = EntityManager.GetBuffer<ResourceGenerationConfigElement>(configs.GetSingletonEntity(), true);
        foreach (var requirement in requirements)
        {
            if (ResourceGenerationUtility.IsValidConfig(requirement) && !types.Contains(requirement.ResourceType))
            {
                error = $"Missing resource prefab: {requirement.ResourceType}.";
                return false;
            }
        }
        return true;
    }
}
