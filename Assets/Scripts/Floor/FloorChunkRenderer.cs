using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public sealed class FloorChunkRenderer : MonoBehaviour
{
    private const string configResourcePath = "Config/FloorGenerationConfig";
    private const float floorDepth = 1f;
    private const int floorSortingOrder = -100;

    private readonly Dictionary<int2, GameObject> _chunkObjects = new();
    private readonly List<FloorVisualVariant> _visualVariants = new();
    private FloorGenerationSettings _settings;
    private ChunkMapSystem _chunkMap;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateRuntimeRenderer()
    {
        if (FindFirstObjectByType<FloorChunkRenderer>() != null)
            return;

        var root = new GameObject("Floor Chunk Renderer");
        root.AddComponent<FloorChunkRenderer>();
        DontDestroyOnLoad(root);
    }

    private void Awake()
    {
        if (!FloorGenerationConfigLoader.TryLoad(configResourcePath, out _settings))
        {
            enabled = false;
            return;
        }

        if (!TryCreateVisualVariants())
            enabled = false;
    }

    private void Update()
    {
        if (!TryInitializeChunkMap())
            return;

        foreach (Chunk chunk in _chunkMap.GetChunks())
        {
            if (!chunk.HasGeneratedResources || _chunkObjects.ContainsKey(chunk.ChunkPosition))
                continue;

            CreateChunkFloor(chunk);
        }
    }

    private void OnDestroy()
    {
        foreach (GameObject chunkObject in _chunkObjects.Values)
        {
            if (chunkObject == null)
                continue;

            MeshFilter meshFilter = chunkObject.GetComponent<MeshFilter>();

            if (meshFilter != null && meshFilter.sharedMesh != null)
                Destroy(meshFilter.sharedMesh);

            Destroy(chunkObject);
        }

        for (int i = 0; i < _visualVariants.Count; i++)
            Destroy(_visualVariants[i].Material);
    }

    private bool TryInitializeChunkMap()
    {
        if (_chunkMap != null)
            return true;

        World world = World.DefaultGameObjectInjectionWorld;

        if (world == null || !world.IsCreated)
            return false;

        _chunkMap = world.GetExistingSystemManaged<ChunkMapSystem>();
        return _chunkMap != null;
    }

    private bool TryCreateVisualVariants()
    {
        Shader floorShader = Shader.Find("Sprites/Default");

        if (floorShader == null)
        {
            Debug.LogError("Failed to find the Sprites/Default shader for floor rendering.");
            return false;
        }

        for (int biomeIndex = 0; biomeIndex < _settings.Biomes.Count; biomeIndex++)
        {
            FloorBiomeConfigData biome = _settings.Biomes[biomeIndex];

            for (int variantIndex = 0; variantIndex < biome.floorVariants.Count; variantIndex++)
            {
                FloorVariantConfigData variant = biome.floorVariants[variantIndex];
                Sprite sprite = LoadSprite(variant.spriteResourcePath);

                if (sprite == null)
                {
                    Debug.LogError($"Floor sprite not found. Biome : {biome.id}, Path : Resources/{variant.spriteResourcePath}");
                    DisposeVisualVariants();
                    return false;
                }

                var material = new Material(floorShader)
                {
                    mainTexture = sprite.texture
                };
                _visualVariants.Add(new FloorVisualVariant(sprite, material));
            }
        }

        return true;
    }

    private void CreateChunkFloor(Chunk chunk)
    {
        Mesh mesh = BuildMesh(chunk);
        var chunkObject = new GameObject($"Floor Chunk ({chunk.ChunkPosition.x}, {chunk.ChunkPosition.y})");
        chunkObject.transform.SetParent(transform, false);
        chunkObject.transform.position = new Vector3(0f, 0f, floorDepth);

        MeshFilter meshFilter = chunkObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh;

        MeshRenderer meshRenderer = chunkObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterials = GetMaterials();
        meshRenderer.sortingOrder = floorSortingOrder;
        _chunkObjects.Add(chunk.ChunkPosition, chunkObject);
    }

    private Mesh BuildMesh(Chunk chunk)
    {
        int cellCount = ChunkUtility.cellCount;
        var vertices = new List<Vector3>(cellCount * 4);
        var uvs = new List<Vector2>(cellCount * 4);
        var trianglesByVariant = new List<int>[_visualVariants.Count];

        for (int i = 0; i < trianglesByVariant.Length; i++)
            trianglesByVariant[i] = new List<int>();

        for (int y = 0; y < GameConstants.chunkSize; y++)
        {
            for (int x = 0; x < GameConstants.chunkSize; x++)
            {
                int2 worldCell = chunk.ChunkPosition * GameConstants.chunkSize + new int2(x, y);
                FloorTileSelection selection = FloorBiomeSampler.SelectFloor(_settings, worldCell);
                int visualVariantIndex = GetVisualVariantIndex(selection);
                int vertexStart = vertices.Count;
                float minimumX = worldCell.x - 0.5f;
                float minimumY = worldCell.y - 0.5f;

                vertices.Add(new Vector3(minimumX, minimumY));
                vertices.Add(new Vector3(minimumX, minimumY + 1f));
                vertices.Add(new Vector3(minimumX + 1f, minimumY + 1f));
                vertices.Add(new Vector3(minimumX + 1f, minimumY));

                Vector2[] spriteUvs = _visualVariants[visualVariantIndex].Sprite.uv;
                uvs.Add(spriteUvs[0]);
                uvs.Add(spriteUvs[1]);
                uvs.Add(spriteUvs[2]);
                uvs.Add(spriteUvs[3]);

                List<int> triangles = trianglesByVariant[visualVariantIndex];
                triangles.Add(vertexStart);
                triangles.Add(vertexStart + 1);
                triangles.Add(vertexStart + 2);
                triangles.Add(vertexStart);
                triangles.Add(vertexStart + 2);
                triangles.Add(vertexStart + 3);
            }
        }

        var mesh = new Mesh
        {
            name = $"Floor Chunk Mesh ({chunk.ChunkPosition.x}, {chunk.ChunkPosition.y})",
            subMeshCount = _visualVariants.Count
        };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);

        for (int i = 0; i < trianglesByVariant.Length; i++)
            mesh.SetTriangles(trianglesByVariant[i], i);

        mesh.RecalculateBounds();
        return mesh;
    }

    private int GetVisualVariantIndex(FloorTileSelection selection)
    {
        int visualVariantIndex = selection.VariantIndex;

        for (int biomeIndex = 0; biomeIndex < selection.BiomeIndex; biomeIndex++)
            visualVariantIndex += _settings.Biomes[biomeIndex].floorVariants.Count;

        return visualVariantIndex;
    }

    private Material[] GetMaterials()
    {
        var materials = new Material[_visualVariants.Count];

        for (int i = 0; i < _visualVariants.Count; i++)
            materials[i] = _visualVariants[i].Material;

        return materials;
    }

    private static Sprite LoadSprite(string resourcePath)
    {
        Sprite sprite = Resources.Load<Sprite>(resourcePath);

        if (sprite != null)
            return sprite;

        Sprite[] sprites = Resources.LoadAll<Sprite>(resourcePath);
        return sprites.Length > 0 ? sprites[0] : null;
    }

    private void DisposeVisualVariants()
    {
        for (int i = 0; i < _visualVariants.Count; i++)
            Destroy(_visualVariants[i].Material);

        _visualVariants.Clear();
    }

    private readonly struct FloorVisualVariant
    {
        public FloorVisualVariant(Sprite sprite, Material material)
        {
            Sprite = sprite;
            Material = material;
        }

        public Sprite Sprite { get; }
        public Material Material { get; }
    }
}
