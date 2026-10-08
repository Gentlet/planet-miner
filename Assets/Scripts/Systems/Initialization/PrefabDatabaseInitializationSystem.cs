using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Scenes;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// 역할·목적: Initialization 마지막에서 요청된 SubScene 로딩과 필수 프리팹 DB 계약을 검사해 게임 시작을 허용한다.
/// 입력·생성자: DB Authoring이 베이킹한 건물/아이템/자원 매핑, SceneSystem 로드 상태와 게시된 자원 생성 설정.
/// 출력·소유권: 전체 검증 성공 시 PrefabDatabaseReady, 실패 시 SimulationFatalError를 즉시 생성한다. DB 원본은 변경하지 않는다.
/// 이용: GameSimulationGroup이 Ready 존재/중단 오류 부재를 확인하여 여섯 실행 phase를 허용한다.
/// 정리·가시화: 로딩 중은 다음 Initialization에서 기다리며 성공/실패 뒤 비활성화한다. 실패 자동 재시도·ECB 기록은 없고 준비 후 DB는 불변 계약이다.
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
        if (SimulationFailureUtility.HasFatalError(EntityManager))
        {
            Enabled = false;
            return;
        }

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
        // 요청된 장면이 모두 준비되기 전에는 DB 부재를 계약 실패로 확정하지 않는다.
        if (loading)
        {
            return;
        }

        // 개별 DB의 유일성·원형 생존과 필수 타입을 모두 확인한 뒤 한 번만 게임 실행을 허용한다.
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
        SimulationFailureUtility.RecordInitializationFailure(EntityManager,
            $"[PrefabDatabaseInitializationSystem] {reason} Game simulation stopped.",
            "Prefab database initialization failed. See error log.");
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
