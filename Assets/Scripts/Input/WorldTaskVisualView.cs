using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

public enum WorldTaskVisualKindEnum : byte
{
    Construction,
    Demolition,
    Recovery
}

public readonly struct WorldTaskVisualRecord
{
    public readonly Entity Target;
    public readonly WorldTaskVisualKindEnum Kind;
    public readonly int2 Position;
    public readonly int2 Footprint;
    public readonly DirectionEnum Direction;
    public readonly BuildingTypeEnum BuildingType;
    public readonly int Tier;
    public WorldTaskVisualRecord(Entity target, WorldTaskVisualKindEnum kind, int2 position, int2 footprint,
        DirectionEnum direction, BuildingTypeEnum buildingType = default, int tier = 0)
    {
        Target = target;
        Kind = kind;
        Position = position;
        Footprint = footprint;
        Direction = direction;
        BuildingType = buildingType;
        Tier = tier;
    }
}

/// <summary>
/// 현장/작업 원본의 관찰자다. 현재 World의 Mesh 참조는 빌려 쓰고, 표시 GameObject/재질은 뷰가 소유한다.
/// 남는 풀 객체는 숨겨 재사용하며 World 교체/뷰 종료에만 소유 객체를 해제한다. ECS 실물/점유/요청에는 참여하지 않는다.
/// </summary>
public sealed class WorldTaskVisualView : MonoBehaviour
{
    private sealed class GhostTemplate
    {
        public readonly List<Mesh> Meshes = new List<Mesh>();
        public readonly List<Matrix4x4> Transforms = new List<Matrix4x4>();
    }
    private sealed class Visual
    {
        public GameObject Root;
        public readonly List<LineRenderer> Lines = new List<LineRenderer>();
        public readonly List<GameObject> Parts = new List<GameObject>();
        public GhostTemplate Template;
    }
    private World _world;
    private float _nextRefresh;
    private Material _lineMaterial;
    private Material _ghostMaterial;
    private readonly List<Visual> _pool = new List<Visual>();
    private readonly Dictionary<(BuildingTypeEnum, int), GhostTemplate> _templates = new Dictionary<(BuildingTypeEnum, int), GhostTemplate>();
    private readonly List<WorldTaskVisualRecord> _records = new List<WorldTaskVisualRecord>();
    public IReadOnlyList<WorldTaskVisualRecord> Records => _records;

    private void OnEnable()
    {
        Shader shader = Shader.Find("Sprites/Default");
        _lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        Shader ghostShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (ghostShader == null)
        {
            ghostShader = shader;
        }
        _ghostMaterial = new Material(ghostShader) { hideFlags = HideFlags.HideAndDontSave };
        _ghostMaterial.SetColor("_BaseColor", new Color(0.12f, 0.55f, 1f, 0.3f));
        _ghostMaterial.color = new Color(0.12f, 0.55f, 1f, 0.3f);
        _ghostMaterial.SetFloat("_Surface", 1f);
        _ghostMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        _ghostMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        _ghostMaterial.SetFloat("_ZWrite", 0f);
        _ghostMaterial.SetFloat("_Cull", (float)CullMode.Off);
        _ghostMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        _ghostMaterial.renderQueue = (int)RenderQueue.Transparent;
        Refresh();
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime < _nextRefresh)
        {
            return;
        }
        _nextRefresh = Time.unscaledTime + 0.2f;
        Refresh();
    }

    public static void ReadRecords(EntityManager manager, List<WorldTaskVisualRecord> records)
    {
        records.Clear();
        using (var query = manager.CreateEntityQuery(typeof(ConstructionSite), typeof(GridPosition), typeof(BuildingFootprint), typeof(Direction)))
        using (var sites = query.ToEntityArray(Allocator.Temp))
        {
            foreach (Entity entity in sites)
            {
                var site = manager.GetComponentData<ConstructionSite>(entity);
                if ((site.Flags & ConstructionSiteFlags.Cancelled) != 0)
                {
                    continue;
                }
                records.Add(new WorldTaskVisualRecord(entity, WorldTaskVisualKindEnum.Construction,
                    manager.GetComponentData<GridPosition>(entity).Value, manager.GetComponentData<BuildingFootprint>(entity).Size,
                    manager.GetComponentData<Direction>(entity).dir, site.TargetBuildingType, site.TargetTier));
            }
        }
        var marked = new HashSet<(Entity, WorldTaskVisualKindEnum)>();
        using var tasks = manager.CreateEntityQuery(typeof(DroneLogisticsTask));
        using var roots = tasks.ToComponentDataArray<DroneLogisticsTask>(Allocator.Temp);
        foreach (var task in roots)
        {
            if (task.State != DroneLogisticsTaskStateEnum.Open && task.State != DroneLogisticsTaskStateEnum.ExecutionConfirmed)
            {
                continue;
            }
            if (!manager.HasComponent<GridPosition>(task.Target))
            {
                continue;
            }
            if (task.Kind == DroneLogisticsTaskKindEnum.BuildingDemolition)
            {
                if (!manager.HasComponent<BuildingFootprint>(task.Target) || !manager.HasComponent<Direction>(task.Target))
                {
                    continue;
                }
                if (!marked.Add((task.Target, WorldTaskVisualKindEnum.Demolition)))
                {
                    continue;
                }
                records.Add(new WorldTaskVisualRecord(task.Target, WorldTaskVisualKindEnum.Demolition,
                    manager.GetComponentData<GridPosition>(task.Target).Value, manager.GetComponentData<BuildingFootprint>(task.Target).Size,
                    manager.GetComponentData<Direction>(task.Target).dir));
            }
            if (task.Kind == DroneLogisticsTaskKindEnum.WorldItemRecovery)
            {
                if (!DroneSchedulingUtility.IsWorldItem(manager, task.Target, task.ItemType))
                {
                    continue;
                }
                if (marked.Add((task.Target, WorldTaskVisualKindEnum.Recovery)))
                {
                    records.Add(new WorldTaskVisualRecord(task.Target, WorldTaskVisualKindEnum.Recovery,
                        manager.GetComponentData<GridPosition>(task.Target).Value, new int2(1), DirectionEnum.Up));
                }
            }
        }
    }

    public void Refresh()
    {
        World current = World.DefaultGameObjectInjectionWorld;
        if (!ReferenceEquals(current, _world))
        {
            ClearVisuals();
            _templates.Clear();
            _world = current;
        }
        if (_world == null)
        {
            HideAll();
            return;
        }
        if (!_world.IsCreated)
        {
            HideAll();
            return;
        }
        ReadRecords(_world.EntityManager, _records);
        while (_pool.Count < _records.Count)
        {
            _pool.Add(CreateVisual());
        }
        for (int i = 0; i < _pool.Count; i++)
        {
            _pool[i].Root.SetActive(i < _records.Count);
            if (i < _records.Count)
            {
                UpdateVisual(_pool[i], _records[i]);
            }
        }
    }

    private Visual CreateVisual()
    {
        var visual = new Visual { Root = new GameObject("World task display") { hideFlags = HideFlags.DontSave, layer = 2 } };
        for (int i = 0; i < 3; i++)
        {
            var child = new GameObject("Marker") { hideFlags = HideFlags.DontSave, layer = 2 };
            child.transform.SetParent(visual.Root.transform, false);
            var line = child.AddComponent<LineRenderer>();
            line.sharedMaterial = _lineMaterial;
            line.useWorldSpace = true;
            line.startWidth = 0.065f;
            line.endWidth = 0.065f;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            visual.Lines.Add(line);
        }
        return visual;
    }

    private void UpdateVisual(Visual visual, WorldTaskVisualRecord record)
    {
        int2 size = new BuildingFootprint(record.Footprint).GetEffectiveSize(record.Direction);
        var min = new Vector3(record.Position.x - 0.5f, record.Position.y - 0.5f, -0.3f);
        var max = new Vector3((float)((long)record.Position.x + size.x) - 0.5f, (float)((long)record.Position.y + size.y) - 0.5f, -0.3f);
        Color color = record.Kind == WorldTaskVisualKindEnum.Construction ? new Color(0.2f, 0.65f, 1f, 0.8f) :
            (record.Kind == WorldTaskVisualKindEnum.Demolition ? Color.red : Color.yellow);
        var outline = visual.Lines[0];
        outline.loop = true;
        outline.positionCount = 4;
        outline.SetPositions(new[] { min, new Vector3(max.x, min.y, min.z), max, new Vector3(min.x, max.y, min.z) });
        foreach (var line in visual.Lines)
        {
            line.startColor = color;
            line.endColor = color;
        }
        bool construction = record.Kind == WorldTaskVisualKindEnum.Construction;
        visual.Lines[1].enabled = !construction;
        visual.Lines[2].enabled = !construction;
        if (!construction)
        {
            visual.Lines[1].positionCount = 2;
            visual.Lines[2].positionCount = 2;
            if (record.Kind == WorldTaskVisualKindEnum.Demolition)
            {
                visual.Lines[1].SetPositions(new[] { min, max });
                visual.Lines[2].SetPositions(new[] { new Vector3(min.x, max.y, min.z), new Vector3(max.x, min.y, min.z) });
            }
            else
            {
                Vector3 center = (min + max) * 0.5f;
                visual.Lines[1].SetPositions(new[] { new Vector3(min.x, center.y, min.z), new Vector3(max.x, center.y, min.z) });
                visual.Lines[2].SetPositions(new[] { new Vector3(center.x, min.y, min.z), new Vector3(center.x, max.y, min.z) });
            }
        }
        var template = construction ? ReadTemplate(record.BuildingType, record.Tier) : null;
        if (!ReferenceEquals(template, visual.Template))
        {
            foreach (var part in visual.Parts)
            {
                Release(part);
            }
            visual.Parts.Clear();
            visual.Template = template;
            if (template != null)
            {
                for (int i = 0; i < template.Meshes.Count; i++)
                {
                    var part = new GameObject("Planned building mesh") { hideFlags = HideFlags.DontSave, layer = 2 };
                    part.transform.SetParent(visual.Root.transform, false);
                    part.AddComponent<MeshFilter>().sharedMesh = template.Meshes[i];
                    var renderer = part.AddComponent<MeshRenderer>();
                    var materials = new Material[Mathf.Max(1, template.Meshes[i].subMeshCount)];
                    for (int material = 0; material < materials.Length; material++)
                    {
                        materials[material] = _ghostMaterial;
                    }
                    renderer.sharedMaterials = materials;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    visual.Parts.Add(part);
                }
            }
        }
        if (template == null)
        {
            return;
        }
        if (template.Meshes.Count == 0)
        {
            return;
        }
        // 공통 건물 생성은 셀 원점과 기본 회전/크기를 사용한다. 그래픽은 표시 깊이만 앞당기고 방향/영역은 윤곽에 반영한다.
        Matrix4x4 root = Matrix4x4.Translate(new Vector3(record.Position.x, record.Position.y, -0.3f));
        for (int i = 0; i < visual.Parts.Count; i++)
        {
            Matrix4x4 matrix = root * template.Transforms[i];
            var target = visual.Parts[i].transform;
            target.position = matrix.GetColumn(3);
            target.rotation = matrix.rotation;
            target.localScale = matrix.lossyScale;
        }
    }

    private GhostTemplate ReadTemplate(BuildingTypeEnum type, int tier)
    {
        if (_templates.TryGetValue((type, tier), out var known))
        {
            return known;
        }
        var manager = _world.EntityManager;
        using var database = manager.CreateEntityQuery(typeof(BuildingPrefabDatabase), typeof(BuildingPrefabElement));
        if (database.CalculateEntityCount() != 1)
        {
            return null;
        }
        if (!PrefabLookupUtility.TryGetBuildingPrefab(manager.GetBuffer<BuildingPrefabElement>(database.GetSingletonEntity(), true), type, tier, out Entity prefab))
        {
            return null;
        }
        var template = new GhostTemplate();
        var entities = new List<Entity> { prefab };
        if (manager.HasBuffer<LinkedEntityGroup>(prefab))
        {
            foreach (var entry in manager.GetBuffer<LinkedEntityGroup>(prefab, true))
            {
                if (entry.Value != prefab)
                {
                    entities.Add(entry.Value);
                }
            }
        }
        foreach (Entity entity in entities)
        {
            if (!manager.HasComponent<RenderMeshArray>(entity) || !manager.HasComponent<MaterialMeshInfo>(entity))
            {
                continue;
            }
            var meshes = manager.GetSharedComponent<RenderMeshArray>(entity);
            var info = manager.GetComponentData<MaterialMeshInfo>(entity);
            Mesh mesh = meshes.GetMesh(info);
            if (mesh == null)
            {
                continue;
            }
            Matrix4x4 relative = Matrix4x4.identity;
            Entity current = entity;
            if (manager.HasComponent<LocalToWorld>(entity) && manager.HasComponent<LocalToWorld>(prefab))
            {
                relative = (Matrix4x4)math.mul(math.inverse(manager.GetComponentData<LocalToWorld>(prefab).Value),
                    manager.GetComponentData<LocalToWorld>(entity).Value);
                current = prefab;
            }
            while (current != prefab)
            {
                if (!manager.HasComponent<LocalTransform>(current) || !manager.HasComponent<Parent>(current))
                {
                    break;
                }
                var local = manager.GetComponentData<LocalTransform>(current);
                var matrix = Matrix4x4.TRS(local.Position, local.Rotation, Vector3.one * local.Scale);
                if (manager.HasComponent<PostTransformMatrix>(current))
                {
                    matrix *= (Matrix4x4)manager.GetComponentData<PostTransformMatrix>(current).Value;
                }
                relative = matrix * relative;
                current = manager.GetComponentData<Parent>(current).Value;
            }
            if (manager.HasComponent<PostTransformMatrix>(prefab))
            {
                relative = (Matrix4x4)manager.GetComponentData<PostTransformMatrix>(prefab).Value * relative;
            }
            template.Meshes.Add(mesh);
            template.Transforms.Add(relative);
        }
        // 그래픽이 없는 유효 프리팹도 현장 윤곽은 남긴다. 게임 생성/승인에 대체 프리팹을 공급하지 않는다.
        _templates.Add((type, tier), template);
        return template;
    }

    private void HideAll()
    {
        _records.Clear();
        foreach (var visual in _pool)
        {
            visual.Root.SetActive(false);
        }
    }

    private void ClearVisuals()
    {
        foreach (var visual in _pool)
        {
            Release(visual.Root);
        }
        _pool.Clear();
        _records.Clear();
    }

    private void OnDisable()
    {
        ClearVisuals();
        _templates.Clear();
        _world = null;
        if (_lineMaterial != null)
        {
            Release(_lineMaterial);
        }
        if (_ghostMaterial != null)
        {
            Release(_ghostMaterial);
        }
    }

    private static void Release(Object owned)
    {
        if (Application.isPlaying)
        {
            Destroy(owned);
        }
        else
        {
            DestroyImmediate(owned);
        }
    }
}
