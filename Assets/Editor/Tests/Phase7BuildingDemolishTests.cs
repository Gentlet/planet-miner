using System.Text.RegularExpressions;
using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// 역할·목적: 철거 승인/동작 중단과 반환/환급/삭제에 대한 NUnit EditMode 회귀 검증.
/// 입력·검사: 완공 건물/실물/설정/요청으로 보호·무효·중복·DB 누락·물류 차단·두 ECB 가시화를 검사한다.
/// 수명: EcsWorldTestFixture가 각 사례의 독립 World를 준비하고 종료 시 해제한다.
/// </summary>
public class Phase7BuildingDemolishTests : EcsWorldTestFixture
{
    private SystemHandle _demolitionCommandSystem;
    private EndCommandEntityCommandBufferSystem _endCommand;
    private SystemHandle _lifecycleApplySystem;
    private SystemHandle _spatialSyncSystem;
    private EndStateApplyEntityCommandBufferSystem _ecbSystem;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        CreateGameplayPrefabDatabases();
        _demolitionCommandSystem = _world.GetOrCreateSystem<BuildingDemolitionCommandSystem>();
        _endCommand = _world.GetOrCreateSystemManaged<EndCommandEntityCommandBufferSystem>();
        _lifecycleApplySystem = _world.GetOrCreateSystem<BuildingLifecycleApplySystem>();
        _spatialSyncSystem = _world.GetOrCreateSystem<BuildingSpatialSyncSystem>();
        _ecbSystem = _world.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>();
    }

    private void RunDemolishPhase()
    {
        // Command/EndCommand 승인과 동작 중단 뒤 Lifecycle/EndStateApply가 반환·환급·삭제를 반영한다.
        RunDemolitionCommandPhase();
        Simulation.UpdateAndComplete(_lifecycleApplySystem);
        Simulation.Playback(_ecbSystem);
    }

    private void RunDemolitionCommandPhase()
    {
        Simulation.UpdateAndComplete(_demolitionCommandSystem);
        Simulation.Playback(_endCommand);
    }

    private Entity CreateBuilding(
        BuildingTypeEnum type,
        int2 pos,
        DirectionEnum dir = DirectionEnum.Up,
        int2 size = default,
        PlacementStamp stamp = default)
    {
        if (size.x <= 0 || size.y <= 0)
        {
            size = new int2(1, 1);
        }

        var building = _entityManager.CreateEntity();
        _entityManager.AddComponentData(building, new BuildingType(type));
        _entityManager.AddComponentData(building, new BuildingFootprint(size));
        _entityManager.AddComponentData(building, new GridPosition(pos));
        _entityManager.AddComponentData(building, new Direction(dir));
        _entityManager.AddComponentData(building, stamp);
        _entityManager.AddComponentData(building, LocalTransform.FromPosition(new float3(pos.x, pos.y, 0f)));

        return building;
    }

    private Entity RequestDemolish(Entity targetBuilding)
    {
        var req = _entityManager.CreateEntity();
        _entityManager.AddComponentData(req, new DemolishBuildingRequest(targetBuilding));
        return req;
    }

    private Entity CreateOperatingBelt(int2 position, DirectionEnum direction)
    {
        Entity belt = CreateBuilding(BuildingTypeEnum.Belt, position, direction);
        _entityManager.AddComponentData(belt, new BeltComponent(2f));
        return belt;
    }

    private Entity CreateStoredItem(Entity owner, ItemTypeEnum type)
    {
        var item = _entityManager.CreateEntity();
        _entityManager.AddComponentData(item, new ItemIdentity(type));
        _entityManager.AddComponentData(item, ItemOwnership.Stored(owner));
        _entityManager.AddComponentData(item, new GridPosition(int2.zero));
        _entityManager.AddComponentData(item, LocalTransform.FromPosition(float3.zero));
        _entityManager.AddComponentData(item, new DisableRendering());

        if (!_entityManager.HasBuffer<StoredItemElement>(owner))
        {
            _entityManager.AddBuffer<StoredItemElement>(owner);
        }
        _entityManager.GetBuffer<StoredItemElement>(owner).Add(new StoredItemElement(item, type, 0));

        return item;
    }

    private Entity CreateProductItem(Entity owner, ItemTypeEnum type)
    {
        var item = _entityManager.CreateEntity();
        _entityManager.AddComponentData(item, new ItemIdentity(type));
        _entityManager.AddComponentData(item, ItemOwnership.Stored(owner));
        _entityManager.AddComponentData(item, new GridPosition(int2.zero));
        _entityManager.AddComponentData(item, LocalTransform.FromPosition(float3.zero));
        _entityManager.AddComponentData(item, new DisableRendering());

        if (!_entityManager.HasBuffer<ProductItemElement>(owner))
        {
            _entityManager.AddBuffer<ProductItemElement>(owner);
        }
        _entityManager.GetBuffer<ProductItemElement>(owner).Add(new ProductItemElement(item, type, 0));

        return item;
    }

    private Entity SetupBuildingConfigWithMaterials(BuildingTypeEnum type, ItemTypeEnum materialType, int quantity)
    {
        var configEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(configEntity, new BuildingConfig());
        _entityManager.AddBuffer<BuildingConfigElement>(configEntity);

        var materialBuffer = _entityManager.AddBuffer<BuildingConstructionMaterialElement>(configEntity);
        materialBuffer.Add(new BuildingConstructionMaterialElement(type, materialType, quantity));

        return configEntity;
    }

    [Test]
    public void Test02_DemolishBuilding_WithStoredAndProductItems_EjectsAllAsWorldItems()
    {
        // 1. Crafter 건물 및 내부 Stored/Product 아이템 생성
        int2 pos = new int2(10, 10);
        var building = CreateBuilding(BuildingTypeEnum.Crafter, pos, size: new int2(2, 2));
        var storedItem = CreateStoredItem(building, ItemTypeEnum.Iron);
        var productItem = CreateProductItem(building, ItemTypeEnum.Copper);

        // 2. 철거 요청 및 실행
        var req = RequestDemolish(building);
        RunDemolishPhase();

        // 3. 건물 파괴 확인
        Assert.IsFalse(_entityManager.Exists(building));
        Assert.IsFalse(_entityManager.Exists(req));

        // 4. 내용물 아이템 전수 방출 검증
        Assert.IsTrue(_entityManager.Exists(storedItem), "Stored 아이템은 보존되어야 함");
        Assert.IsTrue(_entityManager.Exists(productItem), "Product 아이템은 보존되어야 함");

        var storedOwnership = _entityManager.GetComponentData<ItemOwnership>(storedItem);
        Assert.AreEqual(ItemOwnership.WorldItem.Owner, storedOwnership.Owner, "Stored 아이템은 WorldItem으로 전환되어야 함");
        Assert.IsFalse(_entityManager.HasComponent<DisableRendering>(storedItem), "렌더링이 활성화되어야 함");
        Assert.AreEqual(pos, _entityManager.GetComponentData<GridPosition>(storedItem).Value, "건물 위치로 배치되어야 함");

        var productOwnership = _entityManager.GetComponentData<ItemOwnership>(productItem);
        Assert.AreEqual(ItemOwnership.WorldItem.Owner, productOwnership.Owner, "Product 아이템은 WorldItem으로 전환되어야 함");
        Assert.IsFalse(_entityManager.HasComponent<DisableRendering>(productItem), "렌더링이 활성화되어야 함");
        Assert.AreEqual(pos, _entityManager.GetComponentData<GridPosition>(productItem).Value, "건물 위치로 배치되어야 함");
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void ApprovedDemolition_BlocksStorageInput_FromPendingSourceOrDestination(
        bool ownershipFirst, bool demolishSourceBelt)
    {
        int2 buildingPosition = new int2(10, 10);
        int2 incomingPosition = new int2(9, 10);
        Entity storage = Entities.CreateStorage(buildingPosition, new int2(1, 1), slotCount: 1);
        Entity sourceBelt = CreateOperatingBelt(incomingPosition, DirectionEnum.Right);
        Entity item = Entities.CreateBeltItem(incomingPosition, DirectionEnum.Right, 1.0f, itemType: ItemTypeEnum.Iron);
        _entityManager.AddComponent<DestroyItemRequest>(item);
        _entityManager.SetComponentEnabled<DestroyItemRequest>(item, false);
        _entityManager.SetComponentData(item, new BuildingItemInputDecision(storage, true, 0));
        _entityManager.SetComponentEnabled<BuildingItemInputDecision>(item, true);

        var beltSpatialSync = _world.GetOrCreateSystem<BeltSpatialSyncSystem>();
        var storageApply = _world.GetOrCreateSystem<BuildingItemStorageApplySystem>();
        var ownershipApply = _world.GetOrCreateSystem<ItemOwnershipApplySystem>();
        var inputDecision = _world.GetOrCreateSystem<BuildingItemInputDecisionSystem>();
        var inputReservation = _world.GetOrCreateSystem<BuildingStorageInputReservationSystem>();
        Simulation.UpdateAndComplete(beltSpatialSync);
        Simulation.UpdateAndComplete(_spatialSyncSystem);

        Entity target = demolishSourceBelt ? sourceBelt : storage;
        Entity demolitionRequest = RequestDemolish(target);
        RunDemolitionCommandPhase();
        Assert.IsFalse(_entityManager.Exists(demolitionRequest), "승인된 철거 요청도 EndCommand에서 소비한다.");
        Assert.IsTrue(_entityManager.HasComponent<PendingBuildingDemolition>(target));
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(storage).Length);

        // 이전 프레임의 활성 입고 결정이 있어도 실제 Decision이 지운다. 예약과 인계 요청은 생성하지 않는다.
        Simulation.UpdateAndComplete(inputDecision);
        Simulation.UpdateAndComplete(inputReservation);
        Assert.IsFalse(_entityManager.IsComponentEnabled<BuildingItemInputDecision>(item));
        Assert.IsFalse(_entityManager.GetComponentData<BuildingItemInputDecision>(item).CanDeposit);
        Simulation.UpdateAndComplete(storageApply);
        var storedItems = _entityManager.GetBuffer<StoredItemElement>(storage);
        Assert.AreEqual(0, storedItems.Length);
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
        Assert.IsFalse(_entityManager.IsComponentEnabled<TransferOwnershipRequest>(item));

        // 후속 Apply의 상대 순서와 무관하게 기존 월드 실물은 입력 벨트 위치에 보존된다.
        if (ownershipFirst)
        {
            Simulation.UpdateAndComplete(ownershipApply);
            Simulation.UpdateAndComplete(_lifecycleApplySystem);
        }
        else
        {
            Simulation.UpdateAndComplete(_lifecycleApplySystem);
            Simulation.UpdateAndComplete(ownershipApply);
        }

        Assert.IsFalse(_entityManager.IsComponentEnabled<TransferOwnershipRequest>(item));
        Simulation.Playback(_ecbSystem);

        Assert.IsFalse(_entityManager.Exists(target));
        Assert.AreEqual(demolishSourceBelt, _entityManager.Exists(storage));
        Assert.IsFalse(_entityManager.Exists(demolitionRequest));
        Assert.IsTrue(_entityManager.Exists(item), "차단된 입고 아이템은 원래 위치에 보존되어야 한다.");
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
        Assert.AreEqual(incomingPosition, _entityManager.GetComponentData<GridPosition>(item).Value);
        Assert.AreEqual(new float3(incomingPosition.x, incomingPosition.y, 0f),
            _entityManager.GetComponentData<LocalTransform>(item).Position);
        Assert.IsFalse(_entityManager.HasComponent<DisableRendering>(item));
        Assert.AreEqual(!demolishSourceBelt, _entityManager.IsComponentEnabled<BeltMovementState>(item));
        Assert.IsFalse(_entityManager.IsComponentEnabled<TransferOwnershipRequest>(item));
    }

    [Test]
    public void Test03_DemolishBuilding_RefundsConstructionMaterials_100Percent()
    {
        // 1. Belt 건설 비용(Iron 3개) 설정 및 Belt 건물 생성
        int2 pos = new int2(3, 4);
        var configEntity = SetupBuildingConfigWithMaterials(BuildingTypeEnum.Belt, ItemTypeEnum.Iron, 3);
        var belt = CreateBuilding(BuildingTypeEnum.Belt, pos);

        // 2. 철거 요청 및 실행
        var req = RequestDemolish(belt);
        RunDemolishPhase();

        // 3. 건물 파괴 및 요청 소비 확인
        Assert.IsFalse(_entityManager.Exists(belt));
        Assert.IsFalse(_entityManager.Exists(req));

        // 4. 건설 자재 3개 전액 환급 스폰 확인
        var itemQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<ItemIdentity>(),
            ComponentType.ReadOnly<ItemOwnership>(),
            ComponentType.ReadOnly<GridPosition>()
        );

        Assert.AreEqual(3, itemQuery.CalculateEntityCount(), "건설 자재 3개가 100% 월드 아이템으로 환급 스폰되어야 함");

        using (var items = itemQuery.ToEntityArray(Allocator.Temp))
        {
            for (int i = 0; i < items.Length; i++)
            {
                var identity = _entityManager.GetComponentData<ItemIdentity>(items[i]);
                var ownership = _entityManager.GetComponentData<ItemOwnership>(items[i]);
                var gridPos = _entityManager.GetComponentData<GridPosition>(items[i]);

                Assert.AreEqual(ItemTypeEnum.Iron, identity.Type);
                Assert.AreEqual(ItemOwnership.WorldItem.Owner, ownership.Owner);
                Assert.AreEqual(pos, gridPos.Value);
                Assert.IsFalse(_entityManager.HasComponent<DisableRendering>(items[i]), "환급된 월드 아이템은 렌더링되어야 함");
            }
        }

        itemQuery.Dispose();
    }

    [Test]
    public void Test04_DemolishBuilding_Indestructible_StrictRejection()
    {
        // 1. IndestructibleBuilding 태그 보유 건물 생성
        var building = CreateBuilding(BuildingTypeEnum.MainFacility, new int2(0, 0), size: new int2(3, 3));
        _entityManager.AddComponentData(building, new IndestructibleBuilding());

        // 2. 철거 요청 및 실행
        var req = RequestDemolish(building);
        RunDemolishPhase();

        // 3. 철거 거부 확인 (건물 온전히 보존, 요청만 소비)
        Assert.IsTrue(_entityManager.Exists(building), "Indestructible 건물은 철거되지 않고 보존되어야 함");
        Assert.IsFalse(_entityManager.Exists(req), "거부된 철거 요청은 안전하게 소비되어야 함");
    }

    [Test]
    public void Test05_DemolishBuilding_InvalidOrConstructionSite_StrictRejection()
    {
        // 1. 공사 현장 생성 (공사 현장은 철거 대상이 아님 -> Cancel로만 취소 가능)
        var site = _entityManager.CreateEntity();
        _entityManager.AddComponentData(site, new BuildingType(BuildingTypeEnum.ConstructionSite));
        _entityManager.AddComponentData(site, new GridPosition(new int2(1, 1)));

        // 2. 공사 현장에 철거 요청 발행
        var req = RequestDemolish(site);
        RunDemolishPhase();

        // 3. 거부 확인 (현장 보존, 요청 소비)
        Assert.IsTrue(_entityManager.Exists(site), "공사 현장은 철거 대상이 아니므로 보존되어야 함");
        Assert.IsFalse(_entityManager.Exists(req), "무효한 요청은 소비되어야 함");
    }

    [Test]
    public void Test06_DemolishBuilding_DuplicateRequests_SameFrame_SafeAndIdempotent()
    {
        // 1. 건물 및 동일 건물 대상 복수 철거 요청 발행
        var building = CreateBuilding(BuildingTypeEnum.PowerPole, new int2(8, 8));
        var req1 = RequestDemolish(building);
        var req2 = RequestDemolish(building);

        // 2. 철거 Phase 실행
        RunDemolishPhase();

        // 3. 건물 파괴 및 두 요청 모두 안전하게 소비 확인
        Assert.IsFalse(_entityManager.Exists(building));
        Assert.IsFalse(_entityManager.Exists(req1));
        Assert.IsFalse(_entityManager.Exists(req2));
    }

    [Test]
    public void Test08_DemolishBelt_WithItemOnBelt_PreservesItemAsStaticWorldItem()
    {
        // 1. ItemSpatialIndex 초기화
        var spatialSync = _world.GetOrCreateSystem<ItemSpatialSyncSystem>();
        Simulation.UpdateAndComplete(spatialSync);

        // 2. 벨트 건물 및 벨트 위 아이템 생성
        int2 beltPos = new int2(20, 20);
        var belt = CreateBuilding(BuildingTypeEnum.Belt, beltPos);

        var item = _entityManager.CreateEntity();
        _entityManager.AddComponentData(item, new ItemIdentity(ItemTypeEnum.Iron));
        _entityManager.AddComponentData(item, ItemOwnership.WorldItem);
        _entityManager.AddComponentData(item, new GridPosition(beltPos));
        _entityManager.AddComponentData(item, LocalTransform.FromPosition(new float3(beltPos.x, beltPos.y, 0f)));
        _entityManager.AddComponentData(item, new BeltMovementState(0.5f));

        // 공간 인덱스 동기화 (아이템을 공간 맵에 등록)
        Simulation.UpdateAndComplete(spatialSync);

        // 3. 벨트 철거 요청 및 실행
        RequestDemolish(belt);
        RunDemolishPhase();

        // 4. 벨트 파괴 확인 및 아이템의 BeltMovementState 비활성화 확인
        Assert.IsFalse(_entityManager.Exists(belt), "벨트는 파괴되어야 함");
        Assert.IsTrue(_entityManager.Exists(item), "아이템은 바닥에 보존되어야 함");

        bool isBeltMoving = _entityManager.IsComponentEnabled<BeltMovementState>(item);
        Assert.IsFalse(isBeltMoving, "벨트가 철거되었으므로 BeltMovementState는 비활성화되어 정적 WorldItem으로 유지되어야 함");
        Assert.AreEqual(ItemOwnership.WorldItem.Owner, _entityManager.GetComponentData<ItemOwnership>(item).Owner);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Test10_DemolishBuilding_WithPrefabDb_MissingPrefab_StrictFailPolicy(bool missingDatabase)
    {
        // 1. 프리팹 DB는 등록되었으나 Iron_Ore만 있고 Iron은 누락된 상태
        LogAssert.Expect(LogType.Error, new Regex(".*Missing prefab for refund item type.*"));

        var mockPrefab = _entityManager.CreateEntity(typeof(Prefab), typeof(ItemIdentity), typeof(LocalTransform));
        _entityManager.SetComponentData(mockPrefab, new ItemIdentity(ItemTypeEnum.Iron_Ore));

        TestPrefabDatabaseFactory.RemoveDatabase<ItemPrefabDatabase>(_entityManager);

        if (!missingDatabase)
        {
            var dbEntity = _entityManager.CreateEntity(typeof(ItemPrefabDatabase));
            var prefabBuffer = _entityManager.AddBuffer<ItemPrefabElement>(dbEntity);
            prefabBuffer.Add(new ItemPrefabElement(ItemTypeEnum.Iron_Ore, mockPrefab));
        }

        // 2. Iron(누락된 프리팹)을 요구하는 건물 생성
        int2 pos = new int2(7, 7);
        SetupBuildingConfigWithMaterials(BuildingTypeEnum.Storage, ItemTypeEnum.Iron, 5);
        var storage = CreateBuilding(BuildingTypeEnum.Storage, pos);

        // 3. 철거 실행
        RequestDemolish(storage);
        RunDemolishPhase();

        // 4. 건물은 정상 파괴되되, Strict Fail에 의해 누락된 프리팹 아이템은 스폰되지 않아야 함 (고스트/투명 엔티티 방지)
        Assert.IsFalse(_entityManager.Exists(storage), "건물 자체는 파괴되어야 함");

        var query = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<ItemIdentity>(),
            ComponentType.ReadOnly<ItemOwnership>(),
            ComponentType.Exclude<Prefab>()
        );
        Assert.AreEqual(0, query.CalculateEntityCount(), "프리팹이 누락된 환급 아이템은 Strict Fail로 인해 스폰되지 않아야 함");
        query.Dispose();
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Test11_DemolishBelt_BlocksMovementOnPendingSourceOrEntryIntoPendingDestination(bool demolishSource)
    {
        int2 posA = new int2(10, 10);
        int2 posB = new int2(11, 10);
        Entity beltA = CreateOperatingBelt(posA, DirectionEnum.Right);
        Entity beltB = CreateOperatingBelt(posB, DirectionEnum.Right);
        Entity item = Entities.CreateBeltItem(posA, DirectionEnum.Right, 0.95f, plannedProgress: 0.2f);
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<BeltSpatialSyncSystem>());
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<ItemSpatialSyncSystem>());

        Entity target = demolishSource ? beltA : beltB;
        RequestDemolish(target);
        RunDemolitionCommandPhase();
        Simulation.SetDeltaTime(0.1f);
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<BeltMovementDecisionSystem>());
        var movement = _entityManager.GetComponentData<BeltMovementDecision>(item);
        Assert.IsTrue(movement.IsBlocked);
        if (demolishSource)
        {
            Assert.AreEqual(0f, movement.PlannedProgress, "철거 승인된 현재 벨트는 이동을 계획하지 않는다.");
        }

        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<BeltMovementExecutionSystem>());
        Assert.AreEqual(posA, _entityManager.GetComponentData<GridPosition>(item).Value,
            "철거 승인된 벨트에서 이동하거나 철거 목적지로 진입하면 안 된다.");
        float progress = _entityManager.GetComponentData<BeltMovementState>(item).Progress;
        if (demolishSource)
        {
            Assert.AreEqual(0.95f, progress);
        }
        else
        {
            Assert.Less(progress, 1f, "철거 목적지의 경계 이전에서 대기한다.");
        }

        Simulation.UpdateAndComplete(_lifecycleApplySystem);
        Simulation.Playback(_ecbSystem);
        Assert.IsFalse(_entityManager.Exists(target));
        Assert.IsTrue(_entityManager.Exists(item));
        Assert.AreEqual(posA, _entityManager.GetComponentData<GridPosition>(item).Value);
        Assert.AreEqual(!demolishSource, _entityManager.IsComponentEnabled<BeltMovementState>(item));
    }

    [TestCase(BuildingTypeEnum.Splitter, false)]
    [TestCase(BuildingTypeEnum.Splitter, true)]
    [TestCase(BuildingTypeEnum.Merger, false)]
    [TestCase(BuildingTypeEnum.Merger, true)]
    public void Test12_ApprovedDemolition_StopsRoutingAndStoredProductOutput(
        BuildingTypeEnum routerType, bool demolishOwners)
    {
        int2 routerPosition = new int2(20, 20);
        int2 sourcePosition = new int2(19, 20);
        Entity inputBelt = CreateOperatingBelt(sourcePosition, DirectionEnum.Right);
        Entity targetBelt = CreateOperatingBelt(new int2(21, 20), DirectionEnum.Right);
        Entity routedItem = Entities.CreateBeltItem(sourcePosition, DirectionEnum.Right, 1f);
        Entity router = CreateBuilding(routerType, routerPosition);
        _entityManager.AddComponentData(router, new RoutingTransferDecision(routedItem, inputBelt, targetBelt));
        if (routerType == BuildingTypeEnum.Splitter)
        {
            _entityManager.AddComponentData(router, new SplitterRoutingState(inputBelt, DirectionEnum.Right));
        }
        else
        {
            _entityManager.AddComponentData(router, new MergerRoutingState(targetBelt, DirectionEnum.Right));
        }

        int2 storagePosition = new int2(30, 30);
        Entity storage = Entities.CreateStorage(storagePosition, new int2(1, 1));
        Entity storageOutputBelt = CreateOperatingBelt(new int2(31, 30), DirectionEnum.Right);
        Entity storedItem = CreateStoredItem(storage, ItemTypeEnum.Iron);
        _entityManager.SetComponentData(storage,
            new BuildingItemOutputDecision(true, storedItem, new int2(31, 30)));
        _entityManager.SetComponentEnabled<BuildingItemOutputDecision>(storage, true);

        int2 productPosition = new int2(40, 40);
        Entity producer = CreateBuilding(BuildingTypeEnum.Crafter, productPosition);
        Entity productOutputBelt = CreateOperatingBelt(new int2(41, 40), DirectionEnum.Right);
        Entity productItem = CreateProductItem(producer, ItemTypeEnum.Copper);
        _entityManager.AddComponentData(producer,
            new BuildingItemOutputDecision(true, productItem, new int2(41, 40)));

        if (demolishOwners)
        {
            // 승인 전 남아 있던 소유권 이전도 정산과 충돌하지 않도록 Command에서 소비한다.
            _entityManager.AddComponentData(storedItem, new TransferOwnershipRequest(Entity.Null));
            _entityManager.AddComponentData(productItem, new TransferOwnershipRequest(Entity.Null));
        }

        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<BeltSpatialSyncSystem>());
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<ItemSpatialSyncSystem>());
        RequestDemolish(demolishOwners ? router : targetBelt);
        RequestDemolish(demolishOwners ? storage : storageOutputBelt);
        RequestDemolish(demolishOwners ? producer : productOutputBelt);
        RunDemolitionCommandPhase();

        var decisionGroup = _world.GetOrCreateSystemManaged<DecisionGroup>();
        decisionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<SplitterDecisionSystem>());
        decisionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<MergerDecisionSystem>());
        decisionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<StorageItemOutputDecisionSystem>());
        decisionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<ProductItemOutputDecisionSystem>());
        decisionGroup.SortSystems();
        decisionGroup.Update();
        _entityManager.CompleteAllTrackedJobs();
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<BeltDestinationReservationSystem>());

        Assert.IsFalse(_entityManager.IsComponentEnabled<RoutingTransferDecision>(router));
        Assert.IsFalse(_entityManager.IsComponentEnabled<BuildingItemOutputDecision>(storage));
        Assert.IsFalse(_entityManager.IsComponentEnabled<BuildingItemOutputDecision>(producer));
        Assert.IsFalse(_entityManager.GetComponentData<BuildingItemOutputDecision>(storage).CanOutput);
        Assert.IsFalse(_entityManager.GetComponentData<BuildingItemOutputDecision>(producer).CanOutput);
        if (demolishOwners)
        {
            Assert.IsFalse(_entityManager.IsComponentEnabled<TransferOwnershipRequest>(storedItem));
            Assert.IsFalse(_entityManager.IsComponentEnabled<TransferOwnershipRequest>(productItem));
        }

        var stateApplyGroup = _world.GetOrCreateSystemManaged<StateApplyGroup>();
        stateApplyGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<RoutingApplySystem>());
        stateApplyGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingItemStorageApplySystem>());
        stateApplyGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemOwnershipApplySystem>());
        stateApplyGroup.AddSystemToUpdateList(_lifecycleApplySystem);
        stateApplyGroup.AddSystemToUpdateList(_ecbSystem);
        stateApplyGroup.SortSystems();
        stateApplyGroup.Update();
        Assert.AreEqual(sourcePosition, _entityManager.GetComponentData<GridPosition>(routedItem).Value);
        Assert.IsTrue(_entityManager.IsComponentEnabled<BeltMovementState>(routedItem));
        Assert.AreEqual(!demolishOwners, _entityManager.Exists(router));
        Assert.AreEqual(demolishOwners, _entityManager.Exists(targetBelt));
        Assert.AreEqual(demolishOwners ? Entity.Null : storage,
            _entityManager.GetComponentData<ItemOwnership>(storedItem).Owner);
        Assert.AreEqual(demolishOwners ? Entity.Null : producer,
            _entityManager.GetComponentData<ItemOwnership>(productItem).Owner);
        Assert.AreEqual(!demolishOwners, _entityManager.HasComponent<DisableRendering>(storedItem));
        Assert.AreEqual(!demolishOwners, _entityManager.HasComponent<DisableRendering>(productItem));
        if (demolishOwners)
        {
            Assert.AreEqual(storagePosition, _entityManager.GetComponentData<GridPosition>(storedItem).Value);
            Assert.AreEqual(productPosition, _entityManager.GetComponentData<GridPosition>(productItem).Value);
        }
        else
        {
            Assert.AreEqual(1, _entityManager.GetBuffer<StoredItemElement>(storage).Length);
            Assert.AreEqual(1, _entityManager.GetBuffer<ProductItemElement>(producer).Length);
        }
    }

    [Test]
    public void Test13_EndCommandECB_Playback_ImmediateAvailabilityForDownstreamPhases()
    {
        // 시나리오: Phase 1 CommandGroup에서 배치 요청을 수행하면,
        // EndCommandEntityCommandBufferSystem에 의해 동일 프레임 Phase 2 진입 전에 물리적 ConstructionSite가 즉시 실체화됨.
        var commandGroup = _world.GetOrCreateSystemManaged<CommandGroup>();
        var placementCommand = _world.GetOrCreateSystem<BuildingPlacementCommandSystem>();
        var endCommandEcb = _world.GetOrCreateSystemManaged<EndCommandEntityCommandBufferSystem>();
        commandGroup.AddSystemToUpdateList(placementCommand);
        commandGroup.AddSystemToUpdateList(endCommandEcb);
        commandGroup.SortSystems();

        // 공간 인덱스 준비
        _world.GetOrCreateSystem<BuildingSpatialSyncSystem>();
        _world.GetOrCreateSystem<ResourceSpatialSyncSystem>();
        _world.GetOrCreateSystem<ItemSpatialSyncSystem>();

        int2 sitePos = new int2(30, 30);
        var reqEntity = _entityManager.CreateEntity(typeof(BuildingPlacementRequest));
        _entityManager.SetComponentData(reqEntity, new BuildingPlacementRequest(PlacementFlags.StrictAllOrNothing));
        var buffer = _entityManager.AddBuffer<PlacementRequestCandidateElement>(reqEntity);
        buffer.Add(new PlacementRequestCandidateElement(BuildingTypeEnum.Storage, new int2(1, 1), sitePos, DirectionEnum.Up));

        // CommandGroup 1회 실행 (BuildingPlacementCommandSystem -> EndCommandEntityCommandBufferSystem)
        commandGroup.Update();

        // 검증:
        // 1. 요청 엔티티는 Phase 1 끝에서 이미 파괴됨
        Assert.IsFalse(_entityManager.Exists(reqEntity), "배치 요청 엔티티는 CommandGroup 끝에서 파괴되어야 함");

        // 2. ConstructionSite 엔티티가 월드에 물리적으로 이미 실체화되어 존재함 (동일 프레임 Phase 2~4에서 즉시 접근 가능)
        var siteQuery = _entityManager.CreateEntityQuery(typeof(ConstructionSite), typeof(GridPosition));
        Assert.AreEqual(1, siteQuery.CalculateEntityCount(), "CommandGroup 종료 직후 동일 프레임에 ConstructionSite가 실체화되어야 함");
        var siteEntity = siteQuery.GetSingletonEntity();
        Assert.AreEqual(sitePos, _entityManager.GetComponentData<GridPosition>(siteEntity).Value);
        siteQuery.Dispose();
    }
}
