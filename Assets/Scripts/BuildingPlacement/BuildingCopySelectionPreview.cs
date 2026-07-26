using UnityEngine;

public class BuildingCopySelectionPreview : MonoBehaviour
{
    [SerializeField]
    private Color borderColor = new(0.1f, 0.85f, 1f, 0.9f);

    [SerializeField]
    private Color pivotColor = new(1f, 0.85f, 0.1f, 1f);

    [SerializeField]
    private float lineWidth = 0.06f;

    private LineRenderer _border;
    private LineRenderer _pivot;
    private Material _material;

    private void Awake()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null)
            _material = new Material(shader);

        _border = CreateLineRenderer("Copy Selection Border", borderColor, 5);
        _pivot = CreateLineRenderer("Copy Selection Pivot", pivotColor, 5);
        Hide();
    }

    private void OnDisable()
    {
        Hide();
    }

    private void OnDestroy()
    {
        if (_material != null)
            Destroy(_material);
    }

    public void Show(GridBounds bounds)
    {
        float left = bounds.Min.x - 0.5f;
        float right = bounds.Max.x + 0.5f;
        float bottom = bounds.Min.y - 0.5f;
        float top = bounds.Max.y + 0.5f;

        _border.SetPositions(new[]
        {
            new Vector3(left, bottom, 0f),
            new Vector3(right, bottom, 0f),
            new Vector3(right, top, 0f),
            new Vector3(left, top, 0f),
            new Vector3(left, bottom, 0f)
        });

        const float pivotMarkerHalfSize = 0.2f;
        _pivot.SetPositions(new[]
        {
            new Vector3(bounds.Min.x - pivotMarkerHalfSize, bounds.Min.y, 0f),
            new Vector3(bounds.Min.x + pivotMarkerHalfSize, bounds.Min.y, 0f),
            new Vector3(bounds.Min.x, bounds.Min.y, 0f),
            new Vector3(bounds.Min.x, bounds.Min.y - pivotMarkerHalfSize, 0f),
            new Vector3(bounds.Min.x, bounds.Min.y + pivotMarkerHalfSize, 0f)
        });

        _border.enabled = true;
        _pivot.enabled = true;
    }

    public void Hide()
    {
        if (_border != null)
            _border.enabled = false;

        if (_pivot != null)
            _pivot.enabled = false;
    }

    private LineRenderer CreateLineRenderer(
        string objectName,
        Color color,
        int positionCount)
    {
        GameObject lineObject = new(objectName);
        lineObject.transform.SetParent(transform, false);

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = false;
        line.positionCount = positionCount;
        line.startWidth = lineWidth;
        line.endWidth = lineWidth;
        line.startColor = color;
        line.endColor = color;
        line.sortingOrder = 100;

        if (_material != null)
            line.sharedMaterial = _material;

        return line;
    }
}
