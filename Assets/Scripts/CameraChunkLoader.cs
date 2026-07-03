using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class CameraChunkLoader : MonoBehaviour
{
    [SerializeField]
    private int _loadRadiusInCells = 100;

    [SerializeField]
    private bool _loadOnStart = true;

    private readonly HashSet<int2> _requestedChunks = new();
    private EntityManager _entityManager;
    private ChunkMapSystem _chunkMap;
    private int2 _lastCenterChunk;
    private bool _hasLastCenterChunk;

    private void Start()
    {
        if (_loadOnStart)
            LoadChunksAroundCamera(true);
    }

    private void Update()
    {
        LoadChunksAroundCamera(false);
    }

    private void LoadChunksAroundCamera(bool force)
    {
        if (!TryInitialize())
            return;

        int2 centerCell = transform.position.ToGridCell();
        int2 centerChunk = ChunkUtility.ToChunkPosition(centerCell);

        if (!force && _hasLastCenterChunk && centerChunk.Equals(_lastCenterChunk))
            return;

        _lastCenterChunk = centerChunk;
        _hasLastCenterChunk = true;

        int radiusInChunks = Mathf.CeilToInt(Mathf.Max(0, _loadRadiusInCells) / (float)ChunkUtility.chunkSize);

        for (int y = -radiusInChunks; y <= radiusInChunks; y++)
        {
            for (int x = -radiusInChunks; x <= radiusInChunks; x++)
            {
                int2 chunkPosition = centerChunk + new int2(x, y);

                if (_requestedChunks.Contains(chunkPosition))
                    continue;
                if (_chunkMap.TryGetChunk(chunkPosition, out Chunk chunk) && chunk.hasGeneratedResources)
                    continue;

                Entity request = _entityManager.CreateEntity();
                _entityManager.AddComponentData(request, new ChunkLoadRequest
                {
                    chunkPosition = chunkPosition
                });
                _requestedChunks.Add(chunkPosition);
            }
        }
    }

    private bool TryInitialize()
    {
        World world = World.DefaultGameObjectInjectionWorld;

        if (world == null || !world.IsCreated)
            return false;

        _entityManager = world.EntityManager;

        if (_chunkMap == null)
            _chunkMap = world.GetExistingSystemManaged<ChunkMapSystem>();

        return _chunkMap != null;
    }
}
