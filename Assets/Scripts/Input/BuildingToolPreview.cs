using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

/// <summary>footprint/방향/범위 표시만 소유한다. ECS 건물이나 배치 예약을 만들지 않는다.</summary>
[RequireComponent(typeof(WorldConstructionController))]
public sealed class BuildingToolPreview : MonoBehaviour
{
    [SerializeField] private Color _allowed = new Color(0.2f, 1f, 0.55f, 0.8f);
    [SerializeField] private Color _blocked = new Color(1f, 0.25f, 0.25f, 0.8f);
    private WorldConstructionController _tools;
    private Material _material;
    private readonly List<LineRenderer> _outlines = new List<LineRenderer>();
    private readonly List<LineRenderer> _arrows = new List<LineRenderer>();
    private LineRenderer _selection;

    private void OnEnable()
    {
        _tools = GetComponent<WorldConstructionController>();
        var shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogError("건설 미리보기 셰이더가 없습니다.", this);
            enabled = false;
            return;
        }
        _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        _selection = CreateLine("Selection", true);
        _tools.Changed += Refresh;
    }

    private LineRenderer CreateLine(string name, bool loop)
    {
        var child = new GameObject(name) { hideFlags = HideFlags.DontSave };
        child.transform.SetParent(transform, false);
        var line = child.AddComponent<LineRenderer>();
        line.sharedMaterial = _material;
        line.useWorldSpace = true;
        line.loop = loop;
        line.widthMultiplier = 0.055f;
        line.sortingOrder = 2000;
        return line;
    }

    private void Refresh()
    {
        while (_outlines.Count < _tools.Preview.Count)
        {
            _outlines.Add(CreateLine("Placement footprint", true));
            _arrows.Add(CreateLine("Placement direction", false));
        }
        for (int i = 0; i < _outlines.Count; i++)
        {
            _outlines[i].enabled = false;
            _arrows[i].enabled = false;
            if (i >= _tools.Preview.Count)
            {
                continue;
            }
            var item = _tools.Preview[i];
            var candidate = item.Candidate;
            int2 size = BuildingFootprintUtility.GetEffectiveSize(candidate.FootprintSize, candidate.Direction);
            // 거부된 후보도 목록에 남으므로 끝 셀을 long으로 계산하고 int 범위를 벗어나면 표시하지 않는다.
            if (!BuildingToolGeometry.TryPosition((long)candidate.OriginPosition.x + size.x - 1,
                (long)candidate.OriginPosition.y + size.y - 1, out int2 end))
            {
                continue;
            }
            _outlines[i].enabled = true;
            _arrows[i].enabled = true;
            Color color = !item.Validation.IsValid ? _blocked : item.Validation.HasGroundItems ? Color.yellow : _allowed;
            Rectangle(_outlines[i], candidate.OriginPosition, end, color);
            int2 direction = candidate.Direction.ToInt2();
            Vector3 center = new Vector3(candidate.OriginPosition.x + (size.x - 1) * 0.5f,
                candidate.OriginPosition.y + (size.y - 1) * 0.5f, -0.3f);
            Vector3 along = new Vector3(direction.x, direction.y, 0);
            Vector3 side = new Vector3(-direction.y, direction.x, 0);
            Vector3 tip = center + along * 0.35f;
            _arrows[i].positionCount = 5;
            _arrows[i].SetPositions(new[] { center - along * 0.25f, tip,
                tip - along * 0.2f + side * 0.15f, tip, tip - along * 0.2f - side * 0.15f });
            _arrows[i].startColor = color;
            _arrows[i].endColor = color;
        }
        _selection.enabled = _tools.HasSelectionRectangle;
        if (_selection.enabled)
        {
            Rectangle(_selection, math.min(_tools.SelectionFirst, _tools.SelectionLast),
                math.max(_tools.SelectionFirst, _tools.SelectionLast),
                _tools.Mode == BuildingToolModeEnum.CopyRange ? Color.cyan : _blocked);
        }
    }

    private static void Rectangle(LineRenderer line, int2 min, int2 max, Color color)
    {
        line.positionCount = 4;
        line.SetPositions(new[] { new Vector3(min.x - 0.5f, min.y - 0.5f, -0.25f),
            new Vector3(max.x + 0.5f, min.y - 0.5f, -0.25f),
            new Vector3(max.x + 0.5f, max.y + 0.5f, -0.25f),
            new Vector3(min.x - 0.5f, max.y + 0.5f, -0.25f) });
        line.startColor = color;
        line.endColor = color;
    }

    private void OnDisable()
    {
        if (_tools != null)
        {
            _tools.Changed -= Refresh;
        }
        foreach (var line in _outlines)
        {
            DestroyOwned(line.gameObject);
        }
        foreach (var line in _arrows)
        {
            DestroyOwned(line.gameObject);
        }
        _outlines.Clear();
        _arrows.Clear();
        if (_selection != null)
        {
            DestroyOwned(_selection.gameObject);
        }
        if (_material != null)
        {
            DestroyOwned(_material);
        }
    }

    private static void DestroyOwned(Object value)
    {
        if (Application.isPlaying)
        {
            Destroy(value);
        }
        else
        {
            DestroyImmediate(value);
        }
    }
}
