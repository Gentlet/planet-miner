using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Task 7.1 공간 점유·예약의 소유권 계약 및 배치 타당성 검증 단위 테스트.
/// </summary>
public class Phase7ConstructionContractTests : EcsWorldTestFixture
{
    [Test]
    public void Test01_BuildingFootprint_Rotation_And_NegativeCoordinates()
    {
        // 1. 회전 방향에 따른 유효 점유 크기(가로/세로 스왑) 검증
        int2 baseSize = new int2(2, 3);
        Assert.AreEqual(new int2(2, 3), BuildingPlacementValidationUtility.GetEffectiveSize(baseSize, DirectionEnum.Up));
        Assert.AreEqual(new int2(2, 3), BuildingPlacementValidationUtility.GetEffectiveSize(baseSize, DirectionEnum.Down));
        Assert.AreEqual(new int2(3, 2), BuildingPlacementValidationUtility.GetEffectiveSize(baseSize, DirectionEnum.Right));
        Assert.AreEqual(new int2(3, 2), BuildingPlacementValidationUtility.GetEffectiveSize(baseSize, DirectionEnum.Left));

        // 2. 음수 좌표계에서의 점유 영역 계산 검증
        int2 origin = new int2(-10, -5);
        int2 effectiveSize = BuildingPlacementValidationUtility.GetEffectiveSize(baseSize, DirectionEnum.Right); // (3, 2)

        int cellCount = 0;
        for (int y = 0; y < effectiveSize.y; y++)
        {
            for (int x = 0; x < effectiveSize.x; x++)
            {
                int2 cell = origin + new int2(x, y);
                Assert.IsTrue(cell.x >= -10 && cell.x <= -8);
                Assert.IsTrue(cell.y >= -5 && cell.y <= -4);
                cellCount++;
            }
        }
        Assert.AreEqual(6, cellCount);
    }

    [Test]
    public void Test02_SinglePlacement_EmptyTile_And_ResourceRequirement()
    {
        var buildingMap = new NativeParallelHashMap<int2, BuildingInfo>(16, Allocator.Temp);
        var resourceMap = new NativeParallelHashMap<int2, Entity>(16, Allocator.Temp);
        var itemMap = new NativeParallelMultiHashMap<int2, Entity>(16, Allocator.Temp);

        // 1. 빈 타일에 일반 건물(Storage) 배치: 성공
        var res1 = BuildingPlacementValidationUtility.ValidateSinglePlacement(
            BuildingTypeEnum.Storage,
            new int2(1, 1),
            new int2(0, 0),
            DirectionEnum.Up,
            buildingMap.AsReadOnly(),
            resourceMap.AsReadOnly(),
            itemMap.AsReadOnly());
        Assert.AreEqual(PlacementValidationCode.Success, res1.Code);
        Assert.IsTrue(res1.IsValid);

        // 2. 자원이 없는 빈 타일에 채굴기(Miner) 배치: 거부 (자원 필수)
        var res2 = BuildingPlacementValidationUtility.ValidateSinglePlacement(
            BuildingTypeEnum.Miner,
            new int2(2, 2),
            new int2(0, 0),
            DirectionEnum.Up,
            buildingMap.AsReadOnly(),
            resourceMap.AsReadOnly(),
            itemMap.AsReadOnly());
        Assert.AreEqual(PlacementValidationCode.RequiresResourceNode, res2.Code);
        Assert.IsFalse(res2.IsValid);

        // 3. 자원 노드가 존재하는 타일에 채굴기 배치: 성공
        resourceMap.Add(new int2(1, 1), new Entity { Index = 1, Version = 1 });
        var res3 = BuildingPlacementValidationUtility.ValidateSinglePlacement(
            BuildingTypeEnum.Miner,
            new int2(2, 2),
            new int2(0, 0),
            DirectionEnum.Up,
            buildingMap.AsReadOnly(),
            resourceMap.AsReadOnly(),
            itemMap.AsReadOnly());
        Assert.AreEqual(PlacementValidationCode.Success, res3.Code);
        Assert.IsTrue(res3.IsValid);

        buildingMap.Dispose();
        resourceMap.Dispose();
        itemMap.Dispose();
    }

    [Test]
    public void Test03_SinglePlacement_BuildingCollision_And_BeltUpgrade()
    {
        var buildingMap = new NativeParallelHashMap<int2, BuildingInfo>(16, Allocator.Temp);
        var resourceMap = new NativeParallelHashMap<int2, Entity>(16, Allocator.Temp);
        var itemMap = new NativeParallelMultiHashMap<int2, Entity>(16, Allocator.Temp);

        buildingMap.Add(new int2(5, 5), new BuildingInfo(new Entity { Index = 10 }, BuildingTypeEnum.Storage, DirectionEnum.Up));
        buildingMap.Add(new int2(6, 5), new BuildingInfo(new Entity { Index = 11 }, BuildingTypeEnum.ConstructionSite, DirectionEnum.Up));
        buildingMap.Add(new int2(7, 5), new BuildingInfo(new Entity { Index = 12 }, BuildingTypeEnum.Belt, DirectionEnum.Right));

        // 1. 기존 완공 건물과 충돌
        var res1 = BuildingPlacementValidationUtility.ValidateSinglePlacement(
            BuildingTypeEnum.Crafter,
            new int2(1, 1),
            new int2(5, 5),
            DirectionEnum.Up,
            buildingMap.AsReadOnly(),
            resourceMap.AsReadOnly(),
            itemMap.AsReadOnly());
        Assert.AreEqual(PlacementValidationCode.BlockedByBuilding, res1.Code);
        Assert.IsFalse(res1.IsValid);

        // 2. 기존 공사 현장과 충돌
        var res2 = BuildingPlacementValidationUtility.ValidateSinglePlacement(
            BuildingTypeEnum.Crafter,
            new int2(1, 1),
            new int2(6, 5),
            DirectionEnum.Up,
            buildingMap.AsReadOnly(),
            resourceMap.AsReadOnly(),
            itemMap.AsReadOnly());
        Assert.AreEqual(PlacementValidationCode.BlockedByConstructionSite, res2.Code);
        Assert.IsFalse(res2.IsValid);

        // 3. 기존 벨트 위에 새 벨트 설치 (덮어쓰기/업그레이드 허용)
        var res3 = BuildingPlacementValidationUtility.ValidateSinglePlacement(
            BuildingTypeEnum.Belt,
            new int2(1, 1),
            new int2(7, 5),
            DirectionEnum.Up,
            buildingMap.AsReadOnly(),
            resourceMap.AsReadOnly(),
            itemMap.AsReadOnly());
        Assert.AreEqual(PlacementValidationCode.BeltUpgradeAllowed, res3.Code);
        Assert.IsTrue(res3.IsValid);

        // 4. 기존 벨트 위에 일반 건물(창고) 설치 (차단)
        var res4 = BuildingPlacementValidationUtility.ValidateSinglePlacement(
            BuildingTypeEnum.Storage,
            new int2(1, 1),
            new int2(7, 5),
            DirectionEnum.Up,
            buildingMap.AsReadOnly(),
            resourceMap.AsReadOnly(),
            itemMap.AsReadOnly());
        Assert.AreEqual(PlacementValidationCode.BlockedByBuilding, res4.Code);
        Assert.IsFalse(res4.IsValid);

        buildingMap.Dispose();
        resourceMap.Dispose();
        itemMap.Dispose();
    }

    [Test]
    public void Test04_SinglePlacement_NaturalResources_And_GroundItems()
    {
        var buildingMap = new NativeParallelHashMap<int2, BuildingInfo>(16, Allocator.Temp);
        var resourceMap = new NativeParallelHashMap<int2, Entity>(16, Allocator.Temp);
        var itemMap = new NativeParallelMultiHashMap<int2, Entity>(16, Allocator.Temp);

        resourceMap.Add(new int2(2, 2), new Entity { Index = 20 });
        itemMap.Add(new int2(3, 3), new Entity { Index = 30 });

        // 1. 천연 자원 위에 일반 건물(제작기) 배치: 허용
        var res1 = BuildingPlacementValidationUtility.ValidateSinglePlacement(
            BuildingTypeEnum.Crafter,
            new int2(2, 2),
            new int2(1, 1), // (1,1)부터 (2,2)까지 점유하여 자원 노드 포함
            DirectionEnum.Up,
            buildingMap.AsReadOnly(),
            resourceMap.AsReadOnly(),
            itemMap.AsReadOnly());
        Assert.AreEqual(PlacementValidationCode.Success, res1.Code);
        Assert.IsTrue(res1.IsValid);
        Assert.IsFalse(res1.HasGroundItems);

        // 2. 바닥 아이템이 있는 타일에 건물 배치: 허용하되 HasGroundItems 플래그 활성화
        var res2 = BuildingPlacementValidationUtility.ValidateSinglePlacement(
            BuildingTypeEnum.Storage,
            new int2(1, 1),
            new int2(3, 3),
            DirectionEnum.Up,
            buildingMap.AsReadOnly(),
            resourceMap.AsReadOnly(),
            itemMap.AsReadOnly());
        Assert.AreEqual(PlacementValidationCode.Success, res2.Code);
        Assert.IsTrue(res2.IsValid);
        Assert.IsTrue(res2.HasGroundItems);

        buildingMap.Dispose();
        resourceMap.Dispose();
        itemMap.Dispose();
    }

    [Test]
    public void Test05_BatchPlacement_StrictAllOrNothing_RollsBackAll()
    {
        var buildingMap = new NativeParallelHashMap<int2, BuildingInfo>(16, Allocator.Temp);
        var resourceMap = new NativeParallelHashMap<int2, Entity>(16, Allocator.Temp);
        var itemMap = new NativeParallelMultiHashMap<int2, Entity>(16, Allocator.Temp);

        // (1, 0) 타일에 이미 장애물(건물) 존재
        buildingMap.Add(new int2(1, 0), new BuildingInfo(new Entity { Index = 5 }, BuildingTypeEnum.Storage, DirectionEnum.Up));

        var candidates = new NativeArray<PlacementCandidate>(3, Allocator.Temp);
        candidates[0] = new PlacementCandidate(BuildingTypeEnum.Belt, new int2(1, 1), new int2(0, 0)); // 정상
        candidates[1] = new PlacementCandidate(BuildingTypeEnum.Belt, new int2(1, 1), new int2(1, 0)); // 충돌! (Storage와 충돌)
        candidates[2] = new PlacementCandidate(BuildingTypeEnum.Belt, new int2(1, 1), new int2(2, 0)); // 정상

        var results = new NativeArray<PlacementValidationResult>(3, Allocator.Temp);

        // Act: StrictAllOrNothing (기본 정책)
        BuildingPlacementValidationUtility.ValidateBatchPlacement(
            candidates,
            PlacementFlags.StrictAllOrNothing,
            buildingMap.AsReadOnly(),
            resourceMap.AsReadOnly(),
            itemMap.AsReadOnly(),
            results);

        // Assert: 1개가 충돌하여 전체 묶음이 롤백되어야 함
        Assert.AreEqual(PlacementValidationCode.BatchAllOrNothingRolledBack, results[0].Code);
        Assert.AreEqual(PlacementValidationCode.BlockedByBuilding, results[1].Code);
        Assert.AreEqual(PlacementValidationCode.BatchAllOrNothingRolledBack, results[2].Code);
        Assert.IsFalse(results[0].IsValid);
        Assert.IsFalse(results[1].IsValid);
        Assert.IsFalse(results[2].IsValid);

        candidates.Dispose();
        results.Dispose();
        buildingMap.Dispose();
        resourceMap.Dispose();
        itemMap.Dispose();
    }

    [Test]
    public void Test06_BatchPlacement_AllowPartialPlacement_AllowsValidOnes()
    {
        var buildingMap = new NativeParallelHashMap<int2, BuildingInfo>(16, Allocator.Temp);
        var resourceMap = new NativeParallelHashMap<int2, Entity>(16, Allocator.Temp);
        var itemMap = new NativeParallelMultiHashMap<int2, Entity>(16, Allocator.Temp);

        // (1, 0) 타일에 이미 장애물(건물) 존재
        buildingMap.Add(new int2(1, 0), new BuildingInfo(new Entity { Index = 5 }, BuildingTypeEnum.Storage, DirectionEnum.Up));

        var candidates = new NativeArray<PlacementCandidate>(3, Allocator.Temp);
        candidates[0] = new PlacementCandidate(BuildingTypeEnum.Belt, new int2(1, 1), new int2(0, 0)); // 정상
        candidates[1] = new PlacementCandidate(BuildingTypeEnum.Belt, new int2(1, 1), new int2(1, 0)); // 충돌! (Storage와 충돌)
        candidates[2] = new PlacementCandidate(BuildingTypeEnum.Belt, new int2(1, 1), new int2(2, 0)); // 정상

        var results = new NativeArray<PlacementValidationResult>(3, Allocator.Temp);

        // Act: AllowPartialPlacement 활성화
        BuildingPlacementValidationUtility.ValidateBatchPlacement(
            candidates,
            PlacementFlags.AllowPartialPlacement,
            buildingMap.AsReadOnly(),
            resourceMap.AsReadOnly(),
            itemMap.AsReadOnly(),
            results);

        // Assert: 충돌한 후보(1)만 거부되고, 나머지(0, 2)는 정상 승인
        Assert.AreEqual(PlacementValidationCode.Success, results[0].Code);
        Assert.AreEqual(PlacementValidationCode.BlockedByBuilding, results[1].Code);
        Assert.AreEqual(PlacementValidationCode.Success, results[2].Code);
        Assert.IsTrue(results[0].IsValid);
        Assert.IsFalse(results[1].IsValid);
        Assert.IsTrue(results[2].IsValid);

        candidates.Dispose();
        results.Dispose();
        buildingMap.Dispose();
        resourceMap.Dispose();
        itemMap.Dispose();
    }

    [Test]
    public void Test07_BatchPlacement_InternalContention_Arbitration()
    {
        var buildingMap = new NativeParallelHashMap<int2, BuildingInfo>(16, Allocator.Temp);
        var resourceMap = new NativeParallelHashMap<int2, Entity>(16, Allocator.Temp);
        var itemMap = new NativeParallelMultiHashMap<int2, Entity>(16, Allocator.Temp);

        // 동일 묶음 내에서 2개의 후보가 동일한 셀 (0, 0)을 동시 요청
        var candidates = new NativeArray<PlacementCandidate>(2, Allocator.Temp);
        candidates[0] = new PlacementCandidate(BuildingTypeEnum.Storage, new int2(1, 1), new int2(0, 0));
        candidates[1] = new PlacementCandidate(BuildingTypeEnum.Storage, new int2(1, 1), new int2(0, 0));

        var results = new NativeArray<PlacementValidationResult>(2, Allocator.Temp);

        BuildingPlacementValidationUtility.ValidateBatchPlacement(
            candidates,
            PlacementFlags.AllowPartialPlacement,
            buildingMap.AsReadOnly(),
            resourceMap.AsReadOnly(),
            itemMap.AsReadOnly(),
            results);

        // 앞선 후보 0은 승인, 뒤의 후보 1은 내부 선점 충돌로 인해 BlockedByConstructionSite 거부
        Assert.AreEqual(PlacementValidationCode.Success, results[0].Code);
        Assert.AreEqual(PlacementValidationCode.BlockedByConstructionSite, results[1].Code);
        Assert.IsTrue(results[0].IsValid);
        Assert.IsFalse(results[1].IsValid);

        candidates.Dispose();
        results.Dispose();
        buildingMap.Dispose();
        resourceMap.Dispose();
        itemMap.Dispose();
    }
}
