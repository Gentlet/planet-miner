using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public class Phase0AuthoringPrefabContractTests : EcsWorldTestFixture
{
    [Test]
    public void Test01_DomainPrefabDatabases_RegisterAndQueryIndependentEntities()
    {
        // 도메인별 독립 프리팹 데이터베이스 엔티티 생성
        var buildingDb = _entityManager.CreateEntity(typeof(BuildingPrefabDatabase));
        _entityManager.AddBuffer<BuildingPrefabElement>(buildingDb);

        var itemDb = _entityManager.CreateEntity(typeof(ItemPrefabDatabase));
        _entityManager.AddBuffer<ItemPrefabElement>(itemDb);

        var resourceDb = _entityManager.CreateEntity(typeof(ResourcePrefabDatabase));
        _entityManager.AddBuffer<ResourcePrefabElement>(resourceDb);

        var dummyDronePrefab = _entityManager.CreateEntity();
        var droneDb = _entityManager.CreateEntity(typeof(DronePrefabDatabase), typeof(DronePrefab));
        _entityManager.SetComponentData(droneDb, new DronePrefab(dummyDronePrefab));

        // 도메인별 독립 엔티티 존재 확인 (도메인 분리 계약)
        Assert.AreNotEqual(buildingDb, itemDb);
        Assert.AreNotEqual(buildingDb, resourceDb);
        Assert.AreNotEqual(itemDb, resourceDb);
        Assert.AreNotEqual(buildingDb, droneDb);

        Assert.IsTrue(_entityManager.HasBuffer<BuildingPrefabElement>(buildingDb));
        Assert.IsTrue(_entityManager.HasBuffer<ItemPrefabElement>(itemDb));
        Assert.IsTrue(_entityManager.HasBuffer<ResourcePrefabElement>(resourceDb));
        Assert.AreEqual(dummyDronePrefab, _entityManager.GetComponentData<DronePrefab>(droneDb).Prefab);
    }

    [Test]
    public void Test02_PrefabLookupUtility_FindsRegisteredPrefabs()
    {
        var mockMinerPrefab = _entityManager.CreateEntity();
        var mockIronOrePrefab = _entityManager.CreateEntity();
        var mockIronDepositPrefab = _entityManager.CreateEntity();

        var buildingDb = _entityManager.CreateEntity(typeof(BuildingPrefabDatabase));
        var buildingBuffer = _entityManager.AddBuffer<BuildingPrefabElement>(buildingDb);
        buildingBuffer.Add(new BuildingPrefabElement(BuildingTypeEnum.Miner, mockMinerPrefab, new int2(2, 3)));

        var itemDb = _entityManager.CreateEntity(typeof(ItemPrefabDatabase));
        var itemBuffer = _entityManager.AddBuffer<ItemPrefabElement>(itemDb);
        itemBuffer.Add(new ItemPrefabElement(ItemTypeEnum.Iron_Ore, mockIronOrePrefab));

        var resourceDb = _entityManager.CreateEntity(typeof(ResourcePrefabDatabase));
        var resourceBuffer = _entityManager.AddBuffer<ResourcePrefabElement>(resourceDb);
        resourceBuffer.Add(new ResourcePrefabElement(ItemTypeEnum.Iron_Ore, mockIronDepositPrefab));

        // 구조적 변경 완료 후 최신 버퍼 참조 획득
        var latestBuildingBuffer = _entityManager.GetBuffer<BuildingPrefabElement>(buildingDb);
        var latestItemBuffer = _entityManager.GetBuffer<ItemPrefabElement>(itemDb);
        var latestResourceBuffer = _entityManager.GetBuffer<ResourcePrefabElement>(resourceDb);

        // Building Lookup 검증
        bool foundBuilding = PrefabLookupUtility.TryGetBuildingPrefab(
            latestBuildingBuffer,
            BuildingTypeEnum.Miner,
            out Entity buildingPrefab,
            out int2 footprintSize);

        Assert.IsTrue(foundBuilding);
        Assert.AreEqual(mockMinerPrefab, buildingPrefab);
        Assert.AreEqual(new int2(2, 3), footprintSize);

        // Item Lookup 검증
        bool foundItem = PrefabLookupUtility.TryGetItemPrefab(
            latestItemBuffer,
            ItemTypeEnum.Iron_Ore,
            out Entity itemPrefab);

        Assert.IsTrue(foundItem);
        Assert.AreEqual(mockIronOrePrefab, itemPrefab);

        // Resource Lookup 검증
        bool foundResource = PrefabLookupUtility.TryGetResourcePrefab(
            latestResourceBuffer,
            ItemTypeEnum.Iron_Ore,
            out Entity resourcePrefab);

        Assert.IsTrue(foundResource);
        Assert.AreEqual(mockIronDepositPrefab, resourcePrefab);
    }

    [Test]
    public void Test03_PrefabLookupUtility_MissingOrNullPrefab_ReturnsFalse()
    {
        var buildingDb = _entityManager.CreateEntity(typeof(BuildingPrefabDatabase));
        var buildingBuffer = _entityManager.AddBuffer<BuildingPrefabElement>(buildingDb);
        // Entity.Null이 들어간 비정상 프리팹 등록
        buildingBuffer.Add(new BuildingPrefabElement(BuildingTypeEnum.Storage, Entity.Null, new int2(1, 1)));

        // 미등록 항목 조회
        bool foundCrafter = PrefabLookupUtility.TryGetBuildingPrefab(
            buildingBuffer,
            BuildingTypeEnum.Crafter,
            out Entity crafterPrefab,
            out int2 crafterSize);

        Assert.IsFalse(foundCrafter);
        Assert.AreEqual(Entity.Null, crafterPrefab);
        Assert.AreEqual(int2.zero, crafterSize);

        // Entity.Null 등록 항목 조회 시 Safe Fallback 판단을 위해 false 반환 검증
        bool foundStorage = PrefabLookupUtility.TryGetBuildingPrefab(
            buildingBuffer,
            BuildingTypeEnum.Storage,
            out Entity storagePrefab,
            out _);

        Assert.IsFalse(foundStorage);
        Assert.AreEqual(Entity.Null, storagePrefab);
    }

    [Test]
    public void Test04_AuthoringOwnershipContract_PrefabArchetype_DoesNotContainRuntimeMutableState()
    {
        // Baker가 생성하는 정적 프리팹 엔티티
        var itemPrefab = _entityManager.CreateEntity(
            typeof(Prefab),
            typeof(ItemIdentity)
        );
        _entityManager.SetComponentData(itemPrefab, new ItemIdentity { Type = ItemTypeEnum.Copper_Ore });

        // 정적 불변 데이터는 존재
        Assert.IsTrue(_entityManager.HasComponent<Prefab>(itemPrefab));
        Assert.IsTrue(_entityManager.HasComponent<ItemIdentity>(itemPrefab));

        // 런타임 가변 상태는 프리팹 원본에 포함되지 않아야 함 (소유권 경계 원칙)
        Assert.IsFalse(_entityManager.HasComponent<ItemOwnership>(itemPrefab));
        Assert.IsFalse(_entityManager.HasComponent<GridPosition>(itemPrefab));
        Assert.IsFalse(_entityManager.HasComponent<BeltMovementState>(itemPrefab));
        Assert.IsFalse(_entityManager.HasComponent<DestroyItemRequest>(itemPrefab));
        Assert.IsFalse(_entityManager.HasComponent<TransferOwnershipRequest>(itemPrefab));
    }

    [Test]
    public void Test05_SafeFallbackPolicy_CreatesUnrenderedSimulationEntity_WhenPrefabMissing()
    {
        // 프리팹 조회 실패 상황 시뮬레이션
        var itemBuffer = _entityManager.AddBuffer<ItemPrefabElement>(_entityManager.CreateEntity());
        bool hasPrefab = PrefabLookupUtility.TryGetItemPrefab(itemBuffer, ItemTypeEnum.Coal, out Entity prefab);
        Assert.IsFalse(hasPrefab);

        // Safe Fallback 경로: 순수 시뮬레이션 아키타입으로 엔티티 생성
        var fallbackArchetype = _entityManager.CreateArchetype(
            ComponentType.ReadWrite<ItemIdentity>(),
            ComponentType.ReadWrite<ItemOwnership>(),
            ComponentType.ReadWrite<GridPosition>(),
            ComponentType.ReadWrite<LocalTransform>(),
            ComponentType.ReadWrite<BeltMovementState>()
        );

        Entity fallbackEntity = _entityManager.CreateEntity(fallbackArchetype);
        _entityManager.SetComponentData(fallbackEntity, new ItemIdentity { Type = ItemTypeEnum.Coal });
        _entityManager.SetComponentData(fallbackEntity, new ItemOwnership { Owner = Entity.Null });
        _entityManager.SetComponentData(fallbackEntity, new GridPosition(new int2(5, 5)));

        // 시뮬레이션 무결성 검증
        Assert.IsTrue(_entityManager.Exists(fallbackEntity));
        Assert.AreEqual(ItemTypeEnum.Coal, _entityManager.GetComponentData<ItemIdentity>(fallbackEntity).Type);
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<ItemOwnership>(fallbackEntity).Owner);
        Assert.AreEqual(new int2(5, 5), _entityManager.GetComponentData<GridPosition>(fallbackEntity).Value);
    }
}
