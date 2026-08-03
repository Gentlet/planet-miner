using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class BuildingPlacementOperation
{
    private DirectionEnum _direction = DirectionEnum.Up;
    private readonly List<BuildingPlacementCandidate> _candidates;

    private bool _canPlace = false;

    public BuildingPlacementOperation(List<BuildingPlacementCandidate> candidates)
    {
        _candidates = candidates;
    }

    public void EvaluatePlacement(ChunkMapSystem chunkMap)
    {
        if (_candidates.Count == 0)
        {
            _canPlace = false;
            return;
        }

        _canPlace = true;
        foreach (var candidate in _candidates)
        {
            candidate.canPlace = !chunkMap.IsBuildingOccupiedOrReserved(candidate.position) && CanBuild(candidate, chunkMap);

            if (candidate.canPlace == false)
                _canPlace = false;
        }
    }
    public void Rotate()
    {
        _direction = _direction.NextDirection();
        _canPlace = false;
    }

    private bool CanBuild(BuildingPlacementCandidate candidate, ChunkMapSystem chunkMap)
    {
        if (candidate.type == BuildingTypeEnum.Miner && chunkMap.GetFloor(candidate.position) != FloorTypeEnum.PlacedResource)
        {
            return false;
        }

        return true;
    }

    public List<BuildingPlacementCandidate> Candidates => _candidates;
    public DirectionEnum Direction => _direction;
    public bool CanPlace => _canPlace;
}
