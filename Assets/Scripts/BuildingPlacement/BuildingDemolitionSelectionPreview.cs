using UnityEngine;

public class BuildingDemolitionSelectionPreview : MonoBehaviour
{
    [SerializeField]
    private Color borderColor = new(1f, 0.25f, 0.2f, 0.9f);

    [SerializeField]
    private float lineWidth = 0.06f;

    private LineRenderer _border;
    private Material _material;

    private void Awake()
    {
        Shader shader = Shader.Find("Sprites/Default");

        if (shader != null)
            _material = new Material(shader);

        GameObject borderObject = new("Demolition Selection Border");
        borderObject.transform.SetParent(transform, false);
        _border = borderObject.AddComponent<LineRenderer>();
        _border.useWorldSpace = true;
        _border.loop = false;
        _border.positionCount = 5;
        _border.startWidth = lineWidth;
        _border.endWidth = lineWidth;
        _border.startColor = borderColor;
        _border.endColor = borderColor;
        _border.sortingOrder = 100;
        _border.sharedMaterial = _material;
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
        _border.enabled = true;
    }

    public void Hide()
    {
        if (_border != null)
            _border.enabled = false;
    }
}
