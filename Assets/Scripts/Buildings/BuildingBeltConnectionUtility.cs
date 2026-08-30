using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

public readonly struct BuildingBoundaryConnection
{
    public BuildingBoundaryConnection(
        int2 outsideCell,
        DirectionEnum outwardDirection)
    {
        OutsideCell = outsideCell;
        OutwardDirection = outwardDirection;
    }

    public int2 OutsideCell { get; }
    public DirectionEnum OutwardDirection { get; }
}

public static class BuildingBeltConnectionUtility
{
    public static void GetConnectedOutputCells(
        ChunkMapSystem chunkMap,
        EntityManager entityManager,
        int2 anchor,
        int2 size,
        DirectionEnum buildingDirection,
        List<BuildingBoundaryConnection> boundaryConnections,
        List<int2> outputCells)
    {
        GetBoundaryConnections(
            anchor,
            size,
            buildingDirection,
            boundaryConnections);
        outputCells.Clear();

        for (int i = 0; i < boundaryConnections.Count; i++)
        {
            BuildingBoundaryConnection connection = boundaryConnections[i];

            if (!chunkMap.TryGetBelt(
                    connection.OutsideCell,
                    out Entity beltEntity) ||
                !entityManager.Exists(beltEntity) ||
                !entityManager.HasComponent<Direction>(beltEntity) ||
                entityManager.GetComponentData<Direction>(beltEntity).dir !=
                connection.OutwardDirection)
                continue;

            outputCells.Add(connection.OutsideCell);
        }
    }

    public static bool TryOutputItem<TElement>(
        ChunkMapSystem chunkMap,
        EntityManager entityManager,
        ItemStorageSystem itemStorage,
        Entity buildingEntity,
        int2 anchor,
        int2 size,
        DirectionEnum buildingDirection,
        ref BuildingOutputCursor cursor,
        List<BuildingBoundaryConnection> boundaryConnections,
        List<int2> outputCells)
        where TElement : unmanaged, IBufferElementData, IItemStorageElement
    {
        GetConnectedOutputCells(
            chunkMap,
            entityManager,
            anchor,
            size,
            buildingDirection,
            boundaryConnections,
            outputCells);

        if (outputCells.Count == 0)
            return false;

        DynamicBuffer<TElement> storedItems =
            entityManager.GetBuffer<TElement>(buildingEntity, true);
        int storedItemIndex = FindFirstUnreservedItemIndex(
            entityManager,
            storedItems);

        if (storedItemIndex < 0)
            return false;

        int startIndex = 0;

        if (cursor.hasOutput)
        {
            int lastOutputIndex = outputCells.IndexOf(cursor.lastOutputCell);

            if (lastOutputIndex >= 0)
                startIndex = (lastOutputIndex + 1) % outputCells.Count;
        }

        for (int offset = 0; offset < outputCells.Count; offset++)
        {
            int outputIndex = (startIndex + offset) % outputCells.Count;

            if (!itemStorage.TryRestoreItemImmediate<TElement>(
                    buildingEntity,
                    storedItemIndex,
                    outputCells[outputIndex]))
                continue;

            cursor.lastOutputCell = outputCells[outputIndex];
            cursor.hasOutput = true;
            return true;
        }

        return false;
    }

    private static int FindFirstUnreservedItemIndex<TElement>(
        EntityManager entityManager,
        DynamicBuffer<TElement> storedItems)
        where TElement : unmanaged, IBufferElementData, IItemStorageElement
    {
        for (int i = 0; i < storedItems.Length; i++)
        {
            Entity itemEntity = storedItems[i].ItemEntity;

            if (itemEntity == Entity.Null)
                continue;

            if (!entityManager.Exists(itemEntity))
                continue;

            if (entityManager.HasComponent<DroneItemReservation>(itemEntity))
                continue;

            return i;
        }

        return -1;
    }

    private static void GetBoundaryConnections(
        int2 anchor,
        int2 size,
        DirectionEnum buildingDirection,
        List<BuildingBoundaryConnection> results)
    {
        results.Clear();
        int2 normalizedSize = BuildingFootprintUtility.NormalizeSize(size);

        for (int x = 0; x < normalizedSize.x; x++)
        {
            AddConnection(
                anchor,
                new int2(x, normalizedSize.y - 1),
                DirectionEnum.Up,
                buildingDirection,
                results);
        }

        for (int y = normalizedSize.y - 1; y >= 0; y--)
        {
            AddConnection(
                anchor,
                new int2(normalizedSize.x - 1, y),
                DirectionEnum.Right,
                buildingDirection,
                results);
        }

        for (int x = normalizedSize.x - 1; x >= 0; x--)
        {
            AddConnection(
                anchor,
                new int2(x, 0),
                DirectionEnum.Down,
                buildingDirection,
                results);
        }

        for (int y = 0; y < normalizedSize.y; y++)
        {
            AddConnection(
                anchor,
                new int2(0, y),
                DirectionEnum.Left,
                buildingDirection,
                results);
        }
    }

    private static void AddConnection(
        int2 anchor,
        int2 localCell,
        DirectionEnum localOutwardDirection,
        DirectionEnum buildingDirection,
        List<BuildingBoundaryConnection> results)
    {
        int2 buildingCell = anchor + BuildingFootprintUtility.RotateOffset(
            localCell,
            buildingDirection);
        DirectionEnum outwardDirection =
            localOutwardDirection.NextDirection(buildingDirection);
        results.Add(new BuildingBoundaryConnection(
            buildingCell + outwardDirection.ToInt2(),
            outwardDirection));
    }
}
