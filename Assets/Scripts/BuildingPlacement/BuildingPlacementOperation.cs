using System.Collections.Generic;
using Unity.Mathematics;

public class BuildingPlacementOperation
{
    private DirectionEnum _direction = DirectionEnum.Up;
    private readonly List<BuildingPlacementCandidate> _candidates;
    private readonly List<int2> _footprintCells = new();
    private readonly Dictionary<int2, BuildingPlacementCandidate> _candidateByCell = new();
    private bool _canPlace;

    public BuildingPlacementOperation(List<BuildingPlacementCandidate> candidates)
    {
        _candidates = candidates;
    }

    public void PrepareCandidates(int2 pivot, ChunkMapSystem chunkMap)
    {
        for (int i = 0; i < _candidates.Count; i++)
        {
            BuildingPlacementCandidate candidate = _candidates[i];
            candidate.size = chunkMap.GetBuildingSize(candidate.type);
            candidate.position = pivot + BuildingFootprintUtility.RotateOffset(
                candidate.gridPosition,
                _direction);
        }
    }

    public void EvaluatePlacement(ChunkMapSystem chunkMap)
    {
        if (_candidates.Count == 0)
        {
            _canPlace = false;
            return;
        }

        _candidateByCell.Clear();

        for (int candidateIndex = 0;
             candidateIndex < _candidates.Count;
             candidateIndex++)
        {
            BuildingPlacementCandidate candidate = _candidates[candidateIndex];
            BuildingFootprintUtility.GetOccupiedCells(
                candidate.position,
                candidate.size,
                GetDirection(candidate),
                _footprintCells);
            candidate.canPlace = CanBuild(candidate, chunkMap, _footprintCells);

            for (int cellIndex = 0;
                 cellIndex < _footprintCells.Count;
                 cellIndex++)
            {
                int2 cell = _footprintCells[cellIndex];

                if (chunkMap.IsBuildingOccupiedOrReserved(cell))
                    candidate.canPlace = false;

                if (_candidateByCell.TryGetValue(
                        cell,
                        out BuildingPlacementCandidate overlappingCandidate))
                {
                    candidate.canPlace = false;
                    overlappingCandidate.canPlace = false;
                }
                else
                {
                    _candidateByCell.Add(cell, candidate);
                }
            }
        }

        _canPlace = true;

        for (int i = 0; i < _candidates.Count; i++)
        {
            if (!_candidates[i].canPlace)
                _canPlace = false;
        }
    }

    public void Rotate()
    {
        _direction = _direction.NextDirection();
        _canPlace = false;
    }

    public DirectionEnum GetDirection(BuildingPlacementCandidate candidate)
    {
        return candidate.dir.NextDirection(_direction);
    }

    private static bool CanBuild(
        BuildingPlacementCandidate candidate,
        ChunkMapSystem chunkMap,
        List<int2> footprintCells)
    {
        if (candidate.type != BuildingTypeEnum.Miner)
            return true;

        for (int i = 0; i < footprintCells.Count; i++)
        {
            if (chunkMap.TryGetCellData(
                    footprintCells[i],
                    out ChunkCell cellData) &&
                cellData.HasResource)
                return true;
        }

        return false;
    }

    public List<BuildingPlacementCandidate> Candidates => _candidates;
    public DirectionEnum Direction => _direction;
    public bool CanPlace => _canPlace;
}
