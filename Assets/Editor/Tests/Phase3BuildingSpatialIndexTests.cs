using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// BuildingSpatialIndex 및 다중 타일 점유/회전 스왑 단위 테스트.
/// </summary>
public class Phase3BuildingSpatialIndexTests : EcsWorldTestFixture
{
    private SystemHandle _buildingSpatialSyncHandle;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        _buildingSpatialSyncHandle = _world.GetOrCreateSystem(typeof(BuildingSpatialSyncSystem));
    }

    private Entity CreateBuilding(BuildingTypeEnum type, int2 position, int2 size, DirectionEnum direction = DirectionEnum.Up)
        => Entities.CreateBuilding(type, position, size, direction);

    private BuildingSpatialIndex SyncBuildingSpatialIndex()
    {
        _buildingSpatialSyncHandle.Update(_world.Unmanaged);
        var fence = _world.EntityManager.CreateEntityQuery(typeof(BuildingSpatialIndexFence)).GetSingletonRW<BuildingSpatialIndexFence>();
        fence.ValueRW.Complete();
        return _world.EntityManager.CreateEntityQuery(typeof(BuildingSpatialIndex)).GetSingleton<BuildingSpatialIndex>();
    }

    [Test]
    public void Test01_SingleTileBuilding_RegistersAndQueriesAccurately()
    {
        // Arrange: 1x1 Storage building at (5, 5)
        var building = CreateBuilding(BuildingTypeEnum.Storage, new int2(5, 5), new int2(1, 1), DirectionEnum.Up);

        // Act: Sync
        var spatialIndex = SyncBuildingSpatialIndex();

        // Assert
        Assert.IsTrue(spatialIndex.HasBuildingAt(new int2(5, 5)));
        Assert.IsTrue(spatialIndex.TryGetBuilding(new int2(5, 5), out BuildingInfo info));
        Assert.AreEqual(building, info.Entity);
        Assert.AreEqual(BuildingTypeEnum.Storage, info.Type);
        Assert.AreEqual(DirectionEnum.Up, info.Direction);

        // Outside cell
        Assert.IsFalse(spatialIndex.HasBuildingAt(new int2(5, 6)));
        Assert.IsFalse(spatialIndex.HasBuildingAt(new int2(4, 5)));
    }

    [Test]
    public void Test02_MultiTileBuilding_RegistersAllOccupiedCells()
    {
        // Arrange: 2x2 Storage building at anchor (10, 10)
        var building = CreateBuilding(BuildingTypeEnum.Storage, new int2(10, 10), new int2(2, 2), DirectionEnum.Up);

        // Act: Sync
        var spatialIndex = SyncBuildingSpatialIndex();

        // Assert: All 4 cells must be occupied by the same building
        int2[] expectedCells = new[]
        {
            new int2(10, 10),
            new int2(11, 10),
            new int2(10, 11),
            new int2(11, 11)
        };

        foreach (var cell in expectedCells)
        {
            Assert.IsTrue(spatialIndex.HasBuildingAt(cell), $"Cell {cell} should be occupied.");
            Assert.IsTrue(spatialIndex.TryGetBuilding(cell, out BuildingInfo info));
            Assert.AreEqual(building, info.Entity);
            Assert.AreEqual(BuildingTypeEnum.Storage, info.Type);
        }

        // Boundary cells outside the 2x2 area
        Assert.IsFalse(spatialIndex.HasBuildingAt(new int2(9, 10)));
        Assert.IsFalse(spatialIndex.HasBuildingAt(new int2(12, 10)));
        Assert.IsFalse(spatialIndex.HasBuildingAt(new int2(10, 12)));
    }

    [Test]
    public void Test03_RotatedBuilding_SwapsWidthAndHeight()
    {
        // Arrange: 2x3 building at (0, 0) rotated Right
        // Width=2, Height=3 -> Right rotation swaps to Effective Width=3, Effective Height=2
        // Occupied cells: x in [0, 2], y in [0, 1]
        var building = CreateBuilding(BuildingTypeEnum.Miner, new int2(0, 0), new int2(2, 3), DirectionEnum.Right);

        // Act: Sync
        var spatialIndex = SyncBuildingSpatialIndex();

        // Assert: (2, 1) is occupied because width=3 (0..2), height=2 (0..1)
        Assert.IsTrue(spatialIndex.HasBuildingAt(new int2(2, 1)));
        Assert.IsTrue(spatialIndex.TryGetBuilding(new int2(2, 1), out BuildingInfo info));
        Assert.AreEqual(building, info.Entity);
        Assert.AreEqual(DirectionEnum.Right, info.Direction);

        // (1, 2) is NOT occupied (it would have been occupied if unrotated, but height is now 2)
        Assert.IsFalse(spatialIndex.HasBuildingAt(new int2(1, 2)), "Cell (1, 2) should not be occupied after 90 deg rotation.");
        Assert.IsFalse(spatialIndex.HasBuildingAt(new int2(3, 0)), "Cell (3, 0) is outside width.");
    }

    [Test]
    public void Test04_ClearAndRebuild_ReflectsBuildingRemoval()
    {
        // Arrange
        var building = CreateBuilding(BuildingTypeEnum.Crafter, new int2(20, 20), new int2(2, 2), DirectionEnum.Up);
        var spatialIndex = SyncBuildingSpatialIndex();
        Assert.IsTrue(spatialIndex.HasBuildingAt(new int2(20, 20)));

        // Act: Destroy building and re-sync
        _entityManager.DestroyEntity(building);
        spatialIndex = SyncBuildingSpatialIndex();

        // Assert: Index is cleared and no longer contains the building
        Assert.IsFalse(spatialIndex.HasBuildingAt(new int2(20, 20)));
        Assert.IsFalse(spatialIndex.TryGetBuilding(new int2(20, 20), out _));
    }

    [Test]
    public void Test05_BuildingSpatialIndex_LargeMultiTileBuildings_ExpandsCapacitySafely()
    {
        // Arrange: 기본 용량(1024)을 초과하는 3x3 건물 150개 생성 (총 점유 타일 수 = 150 * 9 = 1350)
        const int buildingCount = 150;
        var buildingEntities = new Entity[buildingCount];
        for (int i = 0; i < buildingCount; i++)
        {
            // 각 건물이 겹치지 않도록 4타일 간격으로 배치
            int2 pos = new int2((i % 20) * 4, (i / 20) * 4);
            buildingEntities[i] = CreateBuilding(BuildingTypeEnum.Crafter, pos, new int2(3, 3), DirectionEnum.Up);
        }

        // Act: 공간 인덱스 동기화 수행
        var spatialIndex = SyncBuildingSpatialIndex();

        // Assert: 총 점유 타일 수(1350) 이상으로 맵 용량이 확장되었는지 확인
        Assert.GreaterOrEqual(spatialIndex.Map.Capacity, 1350, "맵 용량이 총 점유 타일 수(1350) 이상으로 확장되어야 합니다.");

        // 모든 건물의 3x3 점유 타일이 누락 없이 인덱스에 등록되었는지 전수 확인
        for (int i = 0; i < buildingCount; i++)
        {
            int2 origin = new int2((i % 20) * 4, (i / 20) * 4);
            Entity expectedEntity = buildingEntities[i];

            for (int dy = 0; dy < 3; dy++)
            {
                for (int dx = 0; dx < 3; dx++)
                {
                    int2 tile = origin + new int2(dx, dy);
                    Assert.IsTrue(spatialIndex.HasBuildingAt(tile), $"타일 {tile}에 건물이 존재해야 합니다.");
                    Assert.IsTrue(spatialIndex.TryGetBuilding(tile, out BuildingInfo info), $"타일 {tile}의 건물 정보를 가져올 수 있어야 합니다.");
                    Assert.AreEqual(expectedEntity, info.Entity, $"타일 {tile}의 엔티티가 일치해야 합니다.");
                    Assert.AreEqual(BuildingTypeEnum.Crafter, info.Type);
                }
            }
        }
    }
}
