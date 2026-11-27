using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 한 World에서 빌린 Sprite/텍스처와 템플릿으로 셀 형상과 공유 머티리얼을 준비한다.
/// 완성한 메시는 표시 소유자에게 넘긴다. 표시 객체/메시를 해제한 뒤 이 객체가 머티리얼 복제본을 정리한다.
/// </summary>
internal sealed class FloorChunkMeshBuilder : IDisposable
{
    private const int CornersPerSide = ChunkUtility.ChunkSize + 1;

    private sealed class Tile
    {
        public Vector2[] Vertices;
        public Vector2[] Uvs;
        public ushort[] Triangles;
        public int MaterialIndex;
    }

    private readonly Dictionary<FixedString128Bytes, Tile> _tiles = new();
    private readonly List<Material> _materials = new();
    private readonly List<List<int>> _trianglesByMaterial = new();
    private readonly List<Material> _chunkMaterials = new();
    private readonly List<Vector3> _vertices = new();
    private readonly List<Vector2> _uvs = new();
    private readonly bool[] _grassCorners = new bool[CornersPerSide * CornersPerSide];

    public bool TryPrepare(Material template, DynamicBuffer<FloorVariantElement> variants, out string error)
    {
        if (template == null)
        {
            error = "바닥 머티리얼이 지정되지 않았습니다.";
            return false;
        }

        var materialByTexture = new Dictionary<Texture, int>();
        foreach (var variant in variants)
        {
            if (_tiles.ContainsKey(variant.SpriteResourcePath))
            {
                continue;
            }

            Sprite sprite = Resources.Load<Sprite>(variant.SpriteResourcePath.ToString());
            if (sprite == null)
            {
                error = $"바닥 Sprite를 찾을 수 없습니다 ({variant.SpriteResourcePath}).";
                return false;
            }

            Texture texture = sprite.texture;
            if (!materialByTexture.TryGetValue(texture, out int materialIndex))
            {
                materialIndex = _materials.Count;
                materialByTexture.Add(texture, materialIndex);
                _materials.Add(new Material(template) { mainTexture = texture, hideFlags = HideFlags.DontSave });
                _trianglesByMaterial.Add(new List<int>());
            }

            // Sprite가 제공하는 실제 UV와 삼각형을 사용한다. 피벗/PPU가 달라도 한 셀에 맞춘다.
            var vertices = sprite.vertices;
            Vector2 size = sprite.rect.size;
            Vector2 pivot = sprite.pivot;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector2 pixel = vertices[i] * sprite.pixelsPerUnit + pivot;
                vertices[i] = new Vector2(pixel.x / size.x - 0.5f, pixel.y / size.y - 0.5f);
            }
            _tiles.Add(variant.SpriteResourcePath, new Tile
            {
                Vertices = vertices, Uvs = sprite.uv, Triangles = sprite.triangles, MaterialIndex = materialIndex
            });
        }

        error = null;
        return true;
    }

    public Mesh Build(int2 minimumCell, uint seed, FloorGenerationSettings settings,
        DynamicBuffer<FloorBiomeElement> biomes, DynamicBuffer<FloorVariantElement> variants,
        out Material[] materials)
    {
        _vertices.Clear();
        _uvs.Clear();
        _chunkMaterials.Clear();
        foreach (var triangles in _trianglesByMaterial)
        {
            triangles.Clear();
        }

        var cornerTiles = settings.CornerTiles;
        if (cornerTiles.IsConfigured)
        {
            SampleGrassCorners(minimumCell, seed, settings, biomes);
        }

        for (int y = 0; y < ChunkUtility.ChunkSize; y++)
        {
            for (int x = 0; x < ChunkUtility.ChunkSize; x++)
            {
                var cell = minimumCell + new int2(x, y);
                var variant = cornerTiles.IsConfigured
                    ? SelectCornerTile(seed, cornerTiles, biomes, variants, cell, x, y)
                    : FloorBiomeSampler.GetVariant(biomes, variants,
                        FloorBiomeSampler.SelectFloor(seed, settings, biomes, variants, cell));
                AppendTile(_tiles[variant.SpriteResourcePath], x, y);
            }
        }

        var mesh = new Mesh { name = "Floor Chunk Mesh", hideFlags = HideFlags.DontSave };
        try
        {
            if (_vertices.Count > ushort.MaxValue)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }
            mesh.SetVertices(_vertices);
            mesh.SetUVs(0, _uvs);
            mesh.subMeshCount = _materials.Count;
            for (int i = 0; i < _materials.Count; i++)
            {
                var triangles = _trianglesByMaterial[i];
                if (triangles.Count == 0)
                {
                    continue;
                }
                mesh.SetTriangles(triangles, _chunkMaterials.Count, false);
                _chunkMaterials.Add(_materials[i]);
            }
            mesh.subMeshCount = _chunkMaterials.Count;
            mesh.RecalculateBounds();
            materials = _chunkMaterials.ToArray();
            return mesh;
        }
        catch
        {
            UnityObjectLifecycleUtility.DestroyOwned(mesh);
            throw;
        }
    }

    private void SampleGrassCorners(int2 minimumCell, uint seed, in FloorGenerationSettings settings,
        in DynamicBuffer<FloorBiomeElement> biomes)
    {
        int grassBiomeIndex = settings.CornerTiles.GrassBiomeIndex;
        for (int y = 0; y < CornersPerSide; y++)
        {
            for (int x = 0; x < CornersPerSide; x++)
            {
                // 정수 셀 중심 사이의 공통 꼭짓점을 샘플링한다. 월드 좌표 극단에서도
                // 로컬 오프셋을 더한 다음 float로 좁혀 양쪽 청크가 같은 좌표를 읽게 한다.
                var corner = new float2((float)((double)minimumCell.x + x - 0.5d),
                    (float)((double)minimumCell.y + y - 0.5d));
                _grassCorners[y * CornersPerSide + x] =
                    FloorBiomeSampler.SampleRegionBiome(seed, settings, biomes, corner) == grassBiomeIndex;
            }
        }
    }

    private FloorVariantElement SelectCornerTile(uint seed, in FloorBiomeCornerTiles cornerTiles,
        in DynamicBuffer<FloorBiomeElement> biomes, in DynamicBuffer<FloorVariantElement> variants,
        int2 cell, int x, int y)
    {
        int bottomLeft = y * CornersPerSide + x;
        int topLeft = bottomLeft + CornersPerSide;
        int mask = (_grassCorners[bottomLeft] ? 1 : 0) |
            (_grassCorners[bottomLeft + 1] ? 2 : 0) |
            (_grassCorners[topLeft + 1] ? 4 : 0) |
            (_grassCorners[topLeft] ? 8 : 0);
        if (mask > 0 && mask < FloorBiomeCornerTiles.AllGrassMask)
        {
            return variants[cornerTiles.VariantStart + mask - 1];
        }

        int biomeIndex = mask == 0 ? cornerTiles.DirtBiomeIndex : cornerTiles.GrassBiomeIndex;
        return FloorBiomeSampler.SelectBiomeVariant(seed, biomes[biomeIndex], variants, biomeIndex, cell);
    }

    private void AppendTile(Tile tile, int x, int y)
    {
        int start = _vertices.Count;
        foreach (Vector2 vertex in tile.Vertices)
        {
            _vertices.Add(new Vector3(x + vertex.x, y + vertex.y, 0f));
        }
        _uvs.AddRange(tile.Uvs);
        var triangles = _trianglesByMaterial[tile.MaterialIndex];
        foreach (ushort index in tile.Triangles)
        {
            triangles.Add(start + index);
        }
    }

    public void Dispose()
    {
        foreach (var material in _materials)
        {
            UnityObjectLifecycleUtility.DestroyOwned(material);
        }
        _materials.Clear();
        _tiles.Clear();
        _trianglesByMaterial.Clear();
        _chunkMaterials.Clear();
    }
}
