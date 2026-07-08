using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class BuildingPlacementOperation
{
    private DirectionEnum _dir = DirectionEnum.Up;
    private List<BuildingPlacementCandidate> _candidates;

    private bool _canPlace = false;

    public BuildingPlacementOperation(List<BuildingPlacementCandidate> candidates)
    {
        _candidates = candidates;
    }

    public void EvaluatePlacement(ChunkMapSystem chunkMap)
    {
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
        _dir = _dir.NextDirection();
        _canPlace = false;
    }

    private bool CanBuild(BuildingPlacementCandidate candidate, ChunkMapSystem chunkMap)
    {
        if(candidate.type == BuildingTypeEnum.Miner && chunkMap.GetFloor(candidate.position) != FloorTypeEnum.PlacedResource)
        {
            return false;
        }

        return true;
    }

    #region Properties
    public List<BuildingPlacementCandidate> Candidates { get => _candidates; }
    public DirectionEnum GetDirection { get => _dir; }
    public bool GetCanPlace { get => _canPlace; }
    #endregion
}
