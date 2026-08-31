using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class FloorChunkRenderer : MonoBehaviour
{
    private const string configResourcePath = "Config/FloorGenerationConfig";
    private const float floorDepth = 1f;
    private const int floorSortingOrder = -100;

    [Header("Culling Settings")]
    [SerializeField]
    private int _cullingRadiusInChunks = 6;

    private readonly Dictionary<int2, FloorChunkEntry> _chunkObjects = new();
    private readonly HashSet<int2> _currentlyVisibleChunks = new();
    private readonly List<FloorVisualVariant> _visualVariants = new();
    private readonly List<Vector3> _vertices = new(ChunkUtility.cellCount * 4);
    private readonly List<Vector2> _uvs = new(ChunkUtility.cellCount * 4);
    private List<int>[] _trianglesByVariant;
    private Material[] _materials;
    private FloorGenerationSettings _settings;
    private ChunkMapSystem _chunkMap;
    private Camera _targetCamera;
    private int2 _lastCameraChunkPos;
    private bool _hasCameraChunkPos;

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

        UpdateCulling();

        while (_chunkMap.TryDequeueResourceGeneratedChunk(out Chunk chunk))
        {
            if (_chunkObjects.ContainsKey(chunk.ChunkPosition))
                continue;

            CreateChunkFloor(chunk);
        }
    }

    private void OnDestroy()
    {
        foreach (FloorChunkEntry entry in _chunkObjects.Values)
        {
            if (entry.GameObject == null)
                continue;

            MeshFilter meshFilter = entry.GameObject.GetComponent<MeshFilter>();

            if (meshFilter != null && meshFilter.sharedMesh != null)
                Destroy(meshFilter.sharedMesh);

            Destroy(entry.GameObject);
        }

        _chunkObjects.Clear();
        _currentlyVisibleChunks.Clear();

        for (int i = 0; i < _visualVariants.Count; i++)
            Destroy(_visualVariants[i].Material);
    }

    private void UpdateCulling()
    {
        if (_targetCamera == null)
        {
            _targetCamera = Camera.main;
            if (_targetCamera == null)
            {
                var loader = FindFirstObjectByType<CameraChunkLoader>();
                if (loader != null)
                    _targetCamera = loader.GetComponent<Camera>();
            }

            if (_targetCamera == null)
                return;
        }

        int2 cameraCell = _targetCamera.transform.position.ToGridCell();
        int2 currentCameraChunk = ChunkUtility.ToChunkPosition(cameraCell);

        if (_hasCameraChunkPos && currentCameraChunk.Equals(_lastCameraChunkPos))
            return;

        _lastCameraChunkPos = currentCameraChunk;
        _hasCameraChunkPos = true;

        var newVisibleChunks = new HashSet<int2>();
        for (int y = -_cullingRadiusInChunks; y <= _cullingRadiusInChunks; y++)
        {
            for (int x = -_cullingRadiusInChunks; x <= _cullingRadiusInChunks; x++)
            {
                newVisibleChunks.Add(currentCameraChunk + new int2(x, y));
            }
        }

        foreach (int2 oldChunkPos in _currentlyVisibleChunks)
        {
            if (!newVisibleChunks.Contains(oldChunkPos) && _chunkObjects.TryGetValue(oldChunkPos, out FloorChunkEntry entry))
            {
                if (entry.Renderer != null)
                    entry.Renderer.enabled = false;
            }
        }

        foreach (int2 newChunkPos in newVisibleChunks)
        {
            if (!_currentlyVisibleChunks.Contains(newChunkPos) && _chunkObjects.TryGetValue(newChunkPos, out FloorChunkEntry entry))
            {
                if (entry.Renderer != null)
                    entry.Renderer.enabled = true;
            }
        }

        _currentlyVisibleChunks.Clear();
        foreach (int2 pos in newVisibleChunks)
        {
            _currentlyVisibleChunks.Add(pos);
        }
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

        for (int variantIndex = 0; variantIndex < _settings.TransitionFloorVariants.Count; variantIndex++)
        {
            FloorVariantConfigData variant = _settings.TransitionFloorVariants[variantIndex];
            Sprite sprite = LoadSprite(variant.spriteResourcePath);

            if (sprite == null)
            {
                Debug.LogError($"Transition floor sprite not found. Path : Resources/{variant.spriteResourcePath}");
                DisposeVisualVariants();
                return false;
            }

            var material = new Material(floorShader)
            {
                mainTexture = sprite.texture
            };
            _visualVariants.Add(new FloorVisualVariant(sprite, material));
        }

        InitializeMeshBuffers();
        return true;
    }

    private void InitializeMeshBuffers()
    {
        _trianglesByVariant = new List<int>[_visualVariants.Count];
        int initialTriangleCapacity =
            ChunkUtility.cellCount * 6 / _visualVariants.Count;

        for (int i = 0; i < _trianglesByVariant.Length; i++)
            _trianglesByVariant[i] = new List<int>(initialTriangleCapacity);

        _materials = new Material[_visualVariants.Count];

        for (int i = 0; i < _visualVariants.Count; i++)
            _materials[i] = _visualVariants[i].Material;
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
        meshRenderer.sharedMaterials = _materials;
        meshRenderer.sortingOrder = floorSortingOrder;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = true;

        bool isVisible = !_hasCameraChunkPos || _currentlyVisibleChunks.Contains(chunk.ChunkPosition);
        meshRenderer.enabled = isVisible;

        _chunkObjects.Add(chunk.ChunkPosition, new FloorChunkEntry(chunkObject, meshRenderer));
    }

    private Mesh BuildMesh(Chunk chunk)
    {
        _vertices.Clear();
        _uvs.Clear();

        for (int i = 0; i < _trianglesByVariant.Length; i++)
            _trianglesByVariant[i].Clear();

        for (int y = 0; y < GameConstants.chunkSize; y++)
        {
            for (int x = 0; x < GameConstants.chunkSize; x++)
            {
                int2 worldCell = chunk.ChunkPosition * GameConstants.chunkSize + new int2(x, y);
                FloorTileSelection selection = FloorBiomeSampler.SelectFloor(_settings, worldCell);
                int visualVariantIndex = GetVisualVariantIndex(selection);
                int vertexStart = _vertices.Count;
                float minimumX = worldCell.x - 0.5f;
                float minimumY = worldCell.y - 0.5f;

                _vertices.Add(new Vector3(minimumX, minimumY));
                _vertices.Add(new Vector3(minimumX, minimumY + 1f));
                _vertices.Add(new Vector3(minimumX + 1f, minimumY + 1f));
                _vertices.Add(new Vector3(minimumX + 1f, minimumY));

                FloorVisualVariant visualVariant =
                    _visualVariants[visualVariantIndex];
                _uvs.Add(visualVariant.Uv0);
                _uvs.Add(visualVariant.Uv1);
                _uvs.Add(visualVariant.Uv2);
                _uvs.Add(visualVariant.Uv3);

                List<int> triangles =
                    _trianglesByVariant[visualVariantIndex];
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
        mesh.SetVertices(_vertices);
        mesh.SetUVs(0, _uvs);

        for (int i = 0; i < _trianglesByVariant.Length; i++)
            mesh.SetTriangles(_trianglesByVariant[i], i);

        mesh.RecalculateBounds();
        return mesh;
    }

    private int GetVisualVariantIndex(FloorTileSelection selection)
    {
        if (selection.UsesTransitionVariant)
            return GetBaseVisualVariantCount() + selection.VariantIndex;

        int visualVariantIndex = selection.VariantIndex;

        for (int biomeIndex = 0; biomeIndex < selection.BiomeIndex; biomeIndex++)
            visualVariantIndex += _settings.Biomes[biomeIndex].floorVariants.Count;

        return visualVariantIndex;
    }

    private int GetBaseVisualVariantCount()
    {
        int count = 0;

        for (int biomeIndex = 0; biomeIndex < _settings.Biomes.Count; biomeIndex++)
            count += _settings.Biomes[biomeIndex].floorVariants.Count;

        return count;
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

    private readonly struct FloorChunkEntry
    {
        public FloorChunkEntry(GameObject gameObject, MeshRenderer renderer)
        {
            GameObject = gameObject;
            Renderer = renderer;
        }

        public GameObject GameObject { get; }
        public MeshRenderer Renderer { get; }
    }

    private readonly struct FloorVisualVariant
    {
        public FloorVisualVariant(Sprite sprite, Material material)
        {
            Vector2[] spriteUvs = sprite.uv;
            Material = material;
            Uv0 = spriteUvs[0];
            Uv1 = spriteUvs[1];
            Uv2 = spriteUvs[2];
            Uv3 = spriteUvs[3];
        }

        public Material Material { get; }
        public Vector2 Uv0 { get; }
        public Vector2 Uv1 { get; }
        public Vector2 Uv2 { get; }
        public Vector2 Uv3 { get; }
    }
}
