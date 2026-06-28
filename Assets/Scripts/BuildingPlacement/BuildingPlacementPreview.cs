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

    public void ShowPreview(BuildingPlacementOperation bpo, int2 pivot)
    {
        EnsurePreviewObjectCount(bpo.Candidates.Count);

        transform.rotation = Quaternion.Euler(0,0, bpo.GetDirection.ToDegrees());

        for (int i = 0; i < bpo.Candidates.Count; i++)
        {
            BuildingPlacementCandidate candidate = bpo.Candidates[i];
            GameObject previewObject = _previewObjects[i];

            int2 cell = pivot + candidate.gridPosition;

            previewObject.SetActive(true);
            previewObject.transform.position = cell.ToVector2();

            SetSprite(previewObject, candidate.type);
            SetColor(previewObject, candidate.canPlace ? canPlaceColor : blockedColor);
        }

        for (int i = bpo.Candidates.Count; i < _previewObjects.Count; i++)
        {
            _previewObjects[i].SetActive(false);
        }
    }

    private void SetSprite(GameObject previewObject, BuildingTypeEnum type)
    {
        var renderer = previewObject.GetComponent<SpriteRenderer>();

        if (renderer == null)
            return;

        foreach (var entry in previewSprites)
        {
            if (entry.type == type)
            {
                renderer.sprite = entry.sprite;
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