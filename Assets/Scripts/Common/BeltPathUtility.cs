using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

internal static class BeltPathUtility
{
    // 공유 경계 양쪽에 간격의 절반씩 남기는 기본 정지선이다.
    public const float HeadOnStopProgress = 1f - GameConstants.ItemSpacing * 0.5f;

    public static bool IsHeadOn(DirectionEnum exit, DirectionEnum nextExit) =>
        exit.ToInt2().Equals(-nextExit.ToInt2());

    public static float GetHeadOnStopProgress(float opposingFront) =>
        math.min(HeadOnStopProgress, 2f - GameConstants.ItemSpacing - opposingFront);

    public static bool HasHeadOnSpacing(float front, float opposingFront) =>
        2f - front - opposingFront >= GameConstants.ItemSpacing - GameConstants.AlignmentEpsilon;

    public static DirectionEnum GetEntryDirection<T>(int2 cell, DirectionEnum exit, in T cells)
        where T : struct, IBeltPathCells
    {
        int2 rear = cell - exit.ToInt2();
        if (cells.PointsToward(rear, exit) || cells.HasBuildingOutput(rear, cell))
        {
            return exit;
        }

        var clockwise = RoutingDirectionUtility.RotateQuarterTurns(exit, RoutingDirectionUtility.RightQuarterTurns);
        var counterclockwise = RoutingDirectionUtility.RotateQuarterTurns(exit, RoutingDirectionUtility.LeftQuarterTurns);
        bool fromClockwise = cells.PointsToward(cell - clockwise.ToInt2(), clockwise);
        bool fromCounterclockwise = cells.PointsToward(cell - counterclockwise.ToInt2(), counterclockwise);
        // 측면 유입이 하나일 때만 코너를 이룬다. 복수 측면 유입은 Merger가 담당한다.
        if (fromClockwise == fromCounterclockwise)
        {
            return exit;
        }
        return fromClockwise ? clockwise : counterclockwise;
    }

    // 유효한 기존 출력은 유지하고, 새 출력이 필요할 때만 설치 순서를 적용한다.
    public static bool TrySelectMergerOutput<T>(int2 router, bool hasPreferred, int2 preferred, in T cells,
        out int2 outputCell, out RoutingBeltCandidate output) where T : struct, IRoutingBeltCells
    {
        if (hasPreferred && cells.TryGetRoutingBelt(preferred, out output) &&
            RoutingDirectionUtility.IsOutgoingBelt(router, preferred, output.Direction))
        {
            outputCell = preferred;
            return true;
        }

        bool found = false;
        outputCell = default;
        output = default;
        for (int i = 0; i < (int)DirectionEnum.Count; i++)
        {
            int2 cell = router + ((DirectionEnum)i).ToInt2();
            if (!cells.TryGetRoutingBelt(cell, out var candidate) ||
                !RoutingDirectionUtility.IsOutgoingBelt(router, cell, candidate.Direction))
            {
                continue;
            }
            if (found && PlacementStamp.Compare(candidate.HasStamp, candidate.Stamp, cell,
                    output.HasStamp, output.Stamp, outputCell) >= 0)
            {
                continue;
            }

            found = true;
            outputCell = cell;
            output = candidate;
        }
        return found;
    }

    // 앞 절반은 들어온 경계에서 중심으로, 뒤 절반은 중심에서 출구 경계로 진행한다.
    public static float3 Position(int2 cell, float progress, DirectionEnum entry, DirectionEnum exit)
    {
        DirectionEnum direction = progress < 0.5f ? entry : exit;
        float2 position = (float2)cell + (float2)direction.ToInt2() * (progress - 0.5f);
        return new float3(position.x, position.y, 0f);
    }
}

internal interface IBeltPathCells
{
    bool PointsToward(int2 cell, DirectionEnum direction);
    bool HasBuildingOutput(int2 source, int2 target);
}

internal interface IRoutingBeltCells
{
    bool TryGetRoutingBelt(int2 cell, out RoutingBeltCandidate candidate);
}

internal struct RoutingBeltCandidate
{
    public Entity Entity;
    public DirectionEnum Direction;
    public PlacementStamp Stamp;
    public bool HasStamp;
}

internal struct RuntimeRoutingBeltCells : IRoutingBeltCells
{
    [ReadOnly] public NativeParallelHashMap<int2, BeltInfo> Belts;
    [ReadOnly] public ComponentLookup<Direction> Directions;
    [ReadOnly] public ComponentLookup<PlacementStamp> PlacementStamps;
    [ReadOnly] public ComponentLookup<PendingBuildingDemolition> PendingDemolitions;

    public bool TryGetBelt(int2 cell, out BeltInfo belt) =>
        BeltEntryUtility.TryGetDirectedBelt(cell, Belts, Directions, out belt);

    public bool TrySelectMergerOutput(int2 router, in MergerRoutingState merger,
        out int2 outputCell, out RoutingBeltCandidate output)
    {
        // 출구 좌표는 벨트 인덱스에서 읽는다. 이동 Job은 아이템의 GridPosition을 직접 쓴다.
        int2 preferred = router + merger.ForwardDirection.ToInt2();
        bool hasPreferred = false;
        if (merger.OutputBelt != Entity.Null)
        {
            hasPreferred = Belts.TryGetValue(preferred, out var belt) && belt.Entity == merger.OutputBelt;
            if (!hasPreferred)
            {
                for (int i = 0; i < (int)DirectionEnum.Count; i++)
                {
                    int2 cell = router + ((DirectionEnum)i).ToInt2();
                    if (Belts.TryGetValue(cell, out belt) && belt.Entity == merger.OutputBelt)
                    {
                        preferred = cell;
                        hasPreferred = true;
                        break;
                    }
                }
            }
        }
        return BeltPathUtility.TrySelectMergerOutput(router, hasPreferred, preferred, this, out outputCell, out output);
    }

    public bool TryGetRoutingBelt(int2 cell, out RoutingBeltCandidate candidate)
    {
        candidate = default;
        if (!TryGetBelt(cell, out var belt) || PendingDemolitions.HasComponent(belt.Entity))
        {
            return false;
        }
        candidate.Entity = belt.Entity;
        candidate.Direction = belt.Direction;
        candidate.HasStamp = PlacementStamps.TryGetComponent(belt.Entity, out candidate.Stamp);
        return true;
    }
}

internal struct RuntimeBeltPathCells : IBeltPathCells
{
    public RuntimeRoutingBeltCells RoutingBelts;
    [ReadOnly] public NativeParallelHashMap<int2, BuildingInfo> Buildings;
    [ReadOnly] public ComponentLookup<BuildingItemOutputDecision> OutputDecisions;
    [ReadOnly] public ComponentLookup<SplitterRoutingState> Splitters;
    [ReadOnly] public ComponentLookup<MergerRoutingState> Mergers;

    public bool TryGetBelt(int2 cell, out BeltInfo belt) => RoutingBelts.TryGetBelt(cell, out belt);

    public bool PointsToward(int2 cell, DirectionEnum direction) =>
        TryGetBelt(cell, out var belt) && belt.Direction == direction;

    public bool HasBuildingOutput(int2 source, int2 target)
    {
        if (!Buildings.IsCreated || !Buildings.TryGetValue(source, out var building))
        {
            return false;
        }
        if (OutputDecisions.HasComponent(building.Entity) || Splitters.HasComponent(building.Entity))
        {
            return true;
        }
        if (!Mergers.TryGetComponent(building.Entity, out var merger) ||
            RoutingBelts.PendingDemolitions.HasComponent(building.Entity))
        {
            return false;
        }

        return RoutingBelts.TrySelectMergerOutput(building.OriginPosition, merger, out var outputCell, out _) &&
            outputCell.Equals(target);
    }
}
