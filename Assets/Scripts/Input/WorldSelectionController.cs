using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>화면 선택 문맥만 소유한다. 현장 완공 연결은 위치와 설치 식별을 함께 확인하며 실물 상태는 변경하지 않는다.</summary>
[DisallowMultipleComponent]
public sealed class WorldSelectionController : MonoBehaviour
{
    [SerializeField, Min(0f)]
    private float _droneSelectionRadius = 0.5f;
    private World _world;
    private int2 _sitePosition;
    private BuildingTypeEnum _siteType;
    private int _siteTier;
    private PlacementStamp _siteStamp;
    private bool _wasSite;
    private bool _wasWorker;
    private bool _wasWorldItem;
    public World SelectedWorld => _world;
    public Entity Selected { get; private set; }

    public event Action Changed;
    private void OnDisable()
    {
        Clear();
        _world = null;
    }

    public void Clear()
    {
        Selected = Entity.Null;
        _wasSite = false;
        _wasWorker = false;
        _wasWorldItem = false;
        Changed?.Invoke();
    }

    public void SelectAt(Vector3 position, int2 cell)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        _world = World.DefaultGameObjectInjectionWorld;
        if (_world == null)
        {
            Clear();
            return;
        }

        if (!_world.IsCreated)
        {
            Clear();
            return;
        }

        var manager = _world.EntityManager;
        Entity selected = Entity.Null;
        float distance = _droneSelectionRadius * _droneSelectionRadius;
        using (var query = manager.CreateEntityQuery(typeof(DroneWorker), typeof(LocalTransform)))
        using (var workers = query.ToEntityArray(Allocator.Temp))
        {
            foreach (Entity worker in workers)
            {
                float next = math.distancesq(manager.GetComponentData<LocalTransform>(worker).Position.xy, new float2(position.x, position.y));
                if (next < distance || (next == distance && (selected == Entity.Null || worker.Index < selected.Index)))
                {
                    selected = worker;
                    distance = next;
                }
            }
        }

        if (selected == Entity.Null)
        {
            using var query = manager.CreateEntityQuery(typeof(BuildingType), typeof(GridPosition), typeof(BuildingFootprint), typeof(Direction));
            using var buildings = query.ToEntityArray(Allocator.Temp);
            foreach (Entity building in buildings)
            {
                if (manager.HasComponent<ConstructionSite>(building) && (manager.GetComponentData<ConstructionSite>(building).Flags & ConstructionSiteFlags.Cancelled) != 0)
                {
                    continue;
                }

                var origin = manager.GetComponentData<GridPosition>(building).Value;
                var size = manager.GetComponentData<BuildingFootprint>(building).Size;
                var direction = manager.GetComponentData<Direction>(building).dir;
                if (!BuildingToolGeometry.Overlaps(origin, size, direction, cell, cell))
                {
                    continue;
                }

                bool site = manager.HasComponent<ConstructionSite>(building);
                if (selected == Entity.Null || (site && !manager.HasComponent<ConstructionSite>(selected)))
                {
                    selected = building;
                }
            }
        }

        Select(manager, selected);
    }

    public bool SelectEntity(World world, Entity entity)
    {
        if (!isActiveAndEnabled)
        {
            return false;
        }
        if (world == null)
        {
            return false;
        }
        if (!world.IsCreated)
        {
            return false;
        }
        if (!ReferenceEquals(world, World.DefaultGameObjectInjectionWorld))
        {
            return false;
        }
        var manager = world.EntityManager;
        if (!DroneSchedulingUtility.IsActiveEntity(manager, entity))
        {
            return false;
        }
        bool item = manager.HasComponent<ItemIdentity>(entity) && manager.HasComponent<ItemOwnership>(entity) &&
            manager.GetComponentData<ItemOwnership>(entity).IsWorldItem;
        if (!manager.HasComponent<BuildingType>(entity) && !manager.HasComponent<DroneWorker>(entity) && !item)
        {
            return false;
        }
        if (manager.HasComponent<ConstructionSite>(entity) &&
            (manager.GetComponentData<ConstructionSite>(entity).Flags & ConstructionSiteFlags.Cancelled) != 0)
        {
            return false;
        }
        _world = world;
        Select(manager, entity);
        return true;
    }

    private void Select(EntityManager manager, Entity selected)
    {
        Selected = selected;
        _wasWorker = selected != Entity.Null && manager.HasComponent<DroneWorker>(selected);
        _wasWorldItem = selected != Entity.Null && manager.HasComponent<ItemIdentity>(selected);
        _wasSite = selected != Entity.Null && manager.HasComponent<ConstructionSite>(selected);
        if (_wasSite)
        {
            var site = manager.GetComponentData<ConstructionSite>(selected);
            _sitePosition = manager.GetComponentData<GridPosition>(selected).Value;
            _siteType = site.TargetBuildingType;
            _siteTier = site.TargetTier;
            _siteStamp = manager.GetComponentData<PlacementStamp>(selected);
        }

        Changed?.Invoke();
    }

    private void LateUpdate()
    {
        World current = World.DefaultGameObjectInjectionWorld;
        if (!ReferenceEquals(_world, current))
        {
            _world = current;
            Clear();
            return;
        }

        if (_world == null)
        {
            Clear();
            return;
        }

        if (!_world.IsCreated)
        {
            Clear();
            return;
        }

        if (Selected == Entity.Null)
        {
            return;
        }

        var manager = _world.EntityManager;
        if (manager.Exists(Selected))
        {
            if (_wasWorker && !manager.HasComponent<DroneWorker>(Selected))
            {
                Clear();
            }
            else if (_wasWorldItem && (!manager.HasComponent<ItemOwnership>(Selected) ||
                !manager.GetComponentData<ItemOwnership>(Selected).IsWorldItem))
            {
                Clear();
            }
            else if (_wasSite && (manager.GetComponentData<ConstructionSite>(Selected).Flags & ConstructionSiteFlags.Cancelled) != 0)
            {
                Clear();
            }

            return;
        }

        if (_wasSite)
        {
            using var query = manager.CreateEntityQuery(typeof(BuildingType), typeof(GridPosition), typeof(PlacementStamp));
            using var buildings = query.ToEntityArray(Allocator.Temp);
            foreach (Entity building in buildings)
            {
                var type = manager.GetComponentData<BuildingType>(building);
                if (type.Type == _siteType && type.Tier == _siteTier && manager.GetComponentData<GridPosition>(building).Value.Equals(_sitePosition) && manager.GetComponentData<PlacementStamp>(building).Equals(_siteStamp))
                {
                    Select(manager, building);
                    return;
                }
            }
        }

        Clear();
    }
}
