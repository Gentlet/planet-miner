using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class BuildingPlacementPreview : MonoBehaviour
{
    [Serializable]
    private struct PreviewSpriteEntry
    {
        public BuildingTypeEnum type;
        public Sprite sprite;
    }

    [SerializeField]
    private GameObject previewPrefab;

    [SerializeField]
    private List<PreviewSpriteEntry> previewSprites;

    [SerializeField]
    private Color canPlaceColor = new Color(0f, 1f, 0f, 0.5f);

    [SerializeField]
    private Color blockedColor = new Color(1f, 0f, 0f, 0.5f);

    private readonly List<GameObject> _previewObjects = new();

    private void OnDisable()
    {
        HidePreview();
    }

    public void UpdatePreviewPositions(BuildingPlacementOperation bpo, int2 pivot)
    {
        EnsurePreviewObjectCount(bpo.Candidates.Count);

        transform.rotation = Quaternion.Euler(0, 0, bpo.Direction.ToDegrees());
        transform.position = pivot.ToVector2();

        for (int i = 0; i < bpo.Candidates.Count; i++)
        {
            BuildingPlacementCandidate candidate = bpo.Candidates[i];
            GameObject previewObject = _previewObjects[i];

            previewObject.SetActive(true);

            float2 visualCenterOffset =
                BuildingFootprintUtility.GetVisualCenterOffset(
                    candidate.size,
                    candidate.dir);
            previewObject.transform.localPosition =
                candidate.gridPosition.ToVector2() +
                new Vector2(visualCenterOffset.x, visualCenterOffset.y);
            previewObject.transform.localRotation =
                Quaternion.Euler(0f, 0f, candidate.dir.ToDegrees());

            SetSpriteAndSize(previewObject, candidate.type, candidate.size);
        }

        for (int i = bpo.Candidates.Count; i < _previewObjects.Count; i++)
        {
            _previewObjects[i].SetActive(false);
        }
    }

    public void UpdatePreviewColors(BuildingPlacementOperation bpo)
    {
        for (int i = 0; i < bpo.Candidates.Count; i++)
        {
            SetColor(
                _previewObjects[i],
                bpo.Candidates[i].canPlace ? canPlaceColor : blockedColor);
        }
    }

    public void HidePreview()
    {
        foreach (GameObject previewObject in _previewObjects)
            previewObject.SetActive(false);
    }

    private void SetSpriteAndSize(
        GameObject previewObject,
        BuildingTypeEnum type,
        int2 size)
    {
        var renderer = previewObject.GetComponent<SpriteRenderer>();

        if (renderer == null)
            return;

        foreach (var entry in previewSprites)
        {
            if (entry.type == type)
            {
                renderer.sprite = entry.sprite;
                Vector2 spriteSize = entry.sprite.bounds.size;
                previewObject.transform.localScale = new Vector3(
                    spriteSize.x > 0f ? size.x / spriteSize.x : 1f,
                    spriteSize.y > 0f ? size.y / spriteSize.y : 1f,
                    1f);
                return;
            }
        }

        Debug.LogWarning($"Preview sprite not found. Type: {type}");
    }

    private void SetColor(GameObject previewObject, Color color)
    {
        var renderer = previewObject.GetComponent<SpriteRenderer>();

        if (renderer != null)
            renderer.color = color;
    }

    private void EnsurePreviewObjectCount(int count)
    {
        while (_previewObjects.Count < count)
        {
            GameObject previewObject = Instantiate(previewPrefab, transform);
            previewObject.SetActive(false);
            _previewObjects.Add(previewObject);
        }
    }
}
