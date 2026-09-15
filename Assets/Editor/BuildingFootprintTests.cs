using System.Collections.Generic;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

public class BuildingFootprintTests : EcsWorldTestFixture
{
    [Test]
    public void RegistrationRejectsMissingFootprint()
    {
        ChunkMapSystem chunkMap = _world.GetOrCreateSystemManaged<ChunkMapSystem>();
        EndSimulationEntityCommandBufferSystem endSimulation = _world
            .GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
        Entity building = _entityManager.CreateEntity(
            typeof(BuildingType), typeof(GridPosition), typeof(Direction),
            typeof(BuildingOccupantRequest));
        LogAssert.Expect(LogType.Error,
            $"Building registration failed because spatial components are missing. Entity: {building}");

        chunkMap.Update();
        endSimulation.Update();

        Assert.That(_entityManager.Exists(building), Is.False);
        Assert.That(chunkMap.TryGetBuilding(int2.zero, out _), Is.False);
    }

    [TestCase(DirectionEnum.Up, -5, 7, new int[] { -5, 7, -4, 7, -5, 8, -4, 8, -5, 9, -4, 9 })]
    [TestCase(DirectionEnum.Right, -5, 7, new int[] { -5, 7, -5, 6, -4, 7, -4, 6, -3, 7, -3, 6 })]
    [TestCase(DirectionEnum.Down, -5, 7, new int[] { -5, 7, -6, 7, -5, 6, -6, 6, -5, 5, -6, 5 })]
    [TestCase(DirectionEnum.Left, -5, 7, new int[] { -5, 7, -5, 8, -6, 7, -6, 8, -7, 7, -7, 8 })]
    public void OccupiedCellsRotateNonSquareFootprintAroundNegativeAnchor(
        DirectionEnum direction,
        int anchorX,
        int anchorY,
        int[] expectedCoordinates)
    {
        List<int2> cells = new();

        BuildingFootprintUtility.GetOccupiedCells(
            new int2(anchorX, anchorY),
            new int2(2, 3),
            direction,
            cells);

        Assert.That(cells, Is.EqualTo(ToCells(expectedCoordinates)));
    }

    [Test]
    public void NormalizeSizeClampsEachDimensionToOne()
    {
        Assert.That(
            BuildingFootprintUtility.NormalizeSize(new int2(0, -4)),
            Is.EqualTo(new int2(1, 1)));
    }

    [Test]
    public void RegistrationAndUnregistrationUseSameRotatedFootprintCells()
    {
        ChunkMapSystem chunkMap = _world.GetOrCreateSystemManaged<ChunkMapSystem>();
        EndSimulationEntityCommandBufferSystem endSimulation = _world
            .GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
        int2 anchor = new(-3, 4);
        DirectionEnum direction = DirectionEnum.Left;
        List<int2> occupiedCells = new();
        BuildingFootprintUtility.GetOccupiedCells(
            anchor,
            new int2(2, 3),
            direction,
            occupiedCells);

        Entity building = _entityManager.CreateEntity(
            typeof(BuildingType),
            typeof(GridPosition),
            typeof(BuildingFootprint),
            typeof(Direction),
            typeof(BuildingOccupantRequest));
        _entityManager.SetComponentData(
            building,
            new BuildingType { type = BuildingTypeEnum.Storage });
        _entityManager.SetComponentData(
            building,
            new GridPosition { gridPosition = anchor });
        _entityManager.SetComponentData(
            building,
            new BuildingFootprint { size = new int2(2, 3) });
        _entityManager.SetComponentData(
            building,
            new Direction { dir = direction });

        for (int i = 0; i < occupiedCells.Count; i++)
            Assert.That(chunkMap.TryReserveBuilding(occupiedCells[i]), Is.True);

        chunkMap.Update();
        endSimulation.Update();

        for (int i = 0; i < occupiedCells.Count; i++)
        {
            Assert.That(
                chunkMap.TryGetBuilding(occupiedCells[i], out Entity owner),
                Is.True);
            Assert.That(owner, Is.EqualTo(building));
            Assert.That(chunkMap.IsBuildingReserved(occupiedCells[i]), Is.False);
        }

        Assert.That(chunkMap.TryUnregisterBuilding(building), Is.True);

        for (int i = 0; i < occupiedCells.Count; i++)
            Assert.That(chunkMap.TryGetBuilding(occupiedCells[i], out _), Is.False);
    }

    private static List<int2> ToCells(int[] coordinates)
    {
        List<int2> cells = new();

        for (int i = 0; i < coordinates.Length; i += 2)
            cells.Add(new int2(coordinates[i], coordinates[i + 1]));

        return cells;
    }
}
