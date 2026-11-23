using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>월드 전력 오버레이의 면과 선을 한 메시로 그리며 자신이 생성한 렌더 리소스를 회수한다.</summary>
internal sealed class PowerOverlayMesh
{
    internal const float Depth = -0.16f;
    private readonly List<Vector3> _vertices = new();
    private readonly List<Color32> _colors = new();
    private readonly List<int> _indices = new();
    private readonly GameObject _root;
    private readonly Mesh _mesh;
    private readonly Material _material;
    private readonly MeshRenderer _renderer;

    public PowerOverlayMesh(Shader shader)
    {
        _root = new GameObject("Power overlay") { hideFlags = HideFlags.DontSave, layer = 2 };
        _mesh = new Mesh { name = "Power overlay geometry", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
        _mesh.MarkDynamic();
        _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        _root.AddComponent<MeshFilter>().sharedMesh = _mesh;
        _renderer = _root.AddComponent<MeshRenderer>();
        _renderer.sharedMaterial = _material;
        _renderer.sortingOrder = 1500;
        _renderer.shadowCastingMode = ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _renderer.enabled = false;
    }

    public void Begin()
    {
        _vertices.Clear();
        _colors.Clear();
        _indices.Clear();
    }

    public void Fill(Vector2 min, Vector2 max, Color color)
    {
        Quad(min, new Vector2(max.x, min.y), max, new Vector2(min.x, max.y), color);
    }

    public void Outline(Vector2 min, Vector2 max, Color color, bool dashed = false)
    {
        var bottomRight = new Vector2(max.x, min.y);
        var topLeft = new Vector2(min.x, max.y);
        Line(min, bottomRight, color, dashed);
        Line(bottomRight, max, color, dashed);
        Line(max, topLeft, color, dashed);
        Line(topLeft, min, color, dashed);
    }

    public void Line(Vector2 start, Vector2 end, Color color, bool dashed = false)
    {
        Vector2 offset = end - start;
        float distance = offset.magnitude;
        if (distance < 0.001f)
        {
            return;
        }
        Vector2 normal = new Vector2(-offset.y, offset.x) * (0.03f / distance);
        int segments = dashed ? Mathf.CeilToInt(Mathf.Clamp(distance / 0.55f, 1f, 48f)) : 1;
        for (int i = 0; i < segments; i++)
        {
            Vector2 first = start + offset * (i / (float)segments);
            Vector2 last = start + offset * ((i + (dashed ? 0.6f : 1f)) / segments);
            Quad(first - normal, last - normal, last + normal, first + normal, color);
        }
    }

    private void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 color)
    {
        int first = _vertices.Count;
        _vertices.Add(new Vector3(a.x, a.y, Depth));
        _vertices.Add(new Vector3(b.x, b.y, Depth));
        _vertices.Add(new Vector3(c.x, c.y, Depth));
        _vertices.Add(new Vector3(d.x, d.y, Depth));
        for (int i = 0; i < 4; i++)
        {
            _colors.Add(color);
        }
        _indices.Add(first);
        _indices.Add(first + 2);
        _indices.Add(first + 1);
        _indices.Add(first);
        _indices.Add(first + 3);
        _indices.Add(first + 2);
    }

    public void Publish()
    {
        _mesh.Clear();
        _renderer.enabled = _vertices.Count > 0;
        if (_renderer.enabled)
        {
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_indices, 0);
        }
    }

    public void Hide() => _renderer.enabled = false;

    public void Dispose()
    {
        Hide();
        UnityObjectLifecycleUtility.DestroyOwned(_root);
        UnityObjectLifecycleUtility.DestroyOwned(_mesh);
        UnityObjectLifecycleUtility.DestroyOwned(_material);
    }
}
