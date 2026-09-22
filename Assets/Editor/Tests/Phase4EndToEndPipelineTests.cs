using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Task 4.3: 자원 채굴 → 스폰 → 벨트 이송 → 창고 적재 End-to-End 전체 파이프라인 통합 검증 테스트.
/// 시스템 간 직접 호출 없이 오직 GameSimulationGroup(Phase 1~6)의 데이터 파이프라인만으로 전체 게임 루프를 완주합니다.
/// 
/// [검증 시나리오]
/// 1. 단일 광물 완주: 채굴(1.0s) -> 채굴기 버퍼 -> 외향 벨트 방출 -> 벨트 3칸 이동 -> 창고 입고 및 슬롯 보관 완료.
/// 2. 연속 채굴 스트림 흐름: 지속적 생산 -> 벨트 위 복수 광물 줄지어 이송(ItemSpacing 간격 유지) -> 창고 순차 적재.
/// 3. 창고 만석 역류 정체(Backpressure): 창고 만석 -> 벨트 종단 정체 -> 후속 벨트 아이템 연쇄 정체 -> 채굴기 버퍼 적재 후 채굴 중단.
/// </summary>
public class Phase4EndToEndPipelineTests : EcsWorldTestFixture
{
    private GameSimulationGroup _simulationGroup;
    private WorldInvariantValidationSystem _invariantValidationSystem;
    private BlobAssetReference<ItemRegistryBlob> _itemBlobRef;
    private float _elapsedTime;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        _elapsedTime = 0.0f;

        // ItemRegistry 싱글톤 초기화 (스택 상한 및 아이템 불변 Blob 설정 제공)
        _itemBlobRef = ItemConfigInitSystem.InitializeItemRegistry(_entityManager);

        // 1. 최상위 시뮬레이션 그룹 및 Phase 그룹 구성
        _simulationGroup = _world.GetOrCreateSystemManaged<GameSimulationGroup>();
        var decisionGroup = _world.GetOrCreateSystemManaged<DecisionGroup>();
        var reservationGroup = _world.GetOrCreateSystemManaged<ReservationGroup>();
        var executionGroup = _world.GetOrCreateSystemManaged<ExecutionGroup>();
        var stateApplyGroup = _world.GetOrCreateSystemManaged<StateApplyGroup>();
        var synchronizationGroup = _world.GetOrCreateSystemManaged<SynchronizationGroup>();

        _simulationGroup.AddSystemToUpdateList(decisionGroup);
        _simulationGroup.AddSystemToUpdateList(reservationGroup);
        _simulationGroup.AddSystemToUpdateList(executionGroup);
        _simulationGroup.AddSystemToUpdateList(stateApplyGroup);
        _simulationGroup.AddSystemToUpdateList(synchronizationGroup);

        // 2. Phase 2: DecisionGroup 시스템 등록
        decisionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<MinerDecisionSystem>());
        decisionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<StorageItemOutputDecisionSystem>());
        decisionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<ProductItemOutputDecisionSystem>());
        decisionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<BeltMovementDecisionSystem>());
        decisionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingItemInputDecisionSystem>());

        // 3. Phase 3: ReservationGroup 시스템 등록
        reservationGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingStorageInputReservationSystem>());

        // 4. Phase 4: ExecutionGroup 시스템 등록
        executionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<MinerExecutionSystem>());
        executionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<BeltMovementExecutionSystem>());

        // 5. Phase 5: StateApplyGroup 시스템 등록
        stateApplyGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingItemStorageApplySystem>());
        stateApplyGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemLifecycleApplySystem>());
        stateApplyGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemOwnershipApplySystem>());
        stateApplyGroup.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>());

        // 6. Phase 6: SynchronizationGroup 시스템 등록
        synchronizationGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<ResourceSpatialSyncSystem>());
        synchronizationGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<BeltSpatialSyncSystem>());
        synchronizationGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingSpatialSyncSystem>());
        synchronizationGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemSpatialSyncSystem>());
        _invariantValidationSystem = _world.GetOrCreateSystemManaged<WorldInvariantValidationSystem>();
        synchronizationGroup.AddSystemToUpdateList(_invariantValidationSystem);
        _invariantValidationSystem.ResetViolationCount();

        // 7. 의존성 순서 정렬
        _simulationGroup.SortSystems();
        decisionGroup.SortSystems();
        reservationGroup.SortSystems();
        executionGroup.SortSystems();
        stateApplyGroup.SortSystems();
        synchronizationGroup.SortSystems();
    }

    [TearDown]
    public override void TearDown()
    {
        if (_itemBlobRef.IsCreated)
        {
            _itemBlobRef.Dispose();
        }
        base.TearDown();
    }

    private Entity CreateResourceNode(int2 position, ItemTypeEnum resourceType, int amount)
    {
        var entity = _entityManager.CreateEntity(
            typeof(ResourceNode),
            typeof(GridPosition));

        _entityManager.SetComponentData(entity, new ResourceNode(resourceType, amount));
        _entityManager.SetComponentData(entity, new GridPosition(position));
        return entity;
    }

    private Entity CreateMiner(int2 position, int2 size, DirectionEnum direction, float miningSpeed = 1.0f)
    {
        var entity = _entityManager.CreateEntity(
            typeof(BuildingType),
            typeof(BuildingFootprint),
            typeof(GridPosition),
            typeof(Direction),
            typeof(BuildingItemOutputDecision),
            typeof(MinerState),
            typeof(MinerDecision));

        _entityManager.SetComponentData(entity, new BuildingType(BuildingTypeEnum.Miner));
        _entityManager.SetComponentData(entity, new BuildingFootprint(size));
        _entityManager.SetComponentData(entity, new GridPosition(position));
        _entityManager.SetComponentData(entity, new Direction(direction));
        _entityManager.SetComponentData(entity, new BuildingItemOutputDecision(false, Entity.Null, int2.zero));
        _entityManager.SetComponentEnabled<BuildingItemOutputDecision>(entity, false);
        _entityManager.SetComponentData(entity, new MinerState(miningSpeed, 0.0f));
        _entityManager.SetComponentData(entity, new MinerDecision(false, Entity.Null));
        _entityManager.SetComponentEnabled<MinerDecision>(entity, false);

        _entityManager.AddBuffer<ProductItemElement>(entity);
        _entityManager.AddBuffer<ProductResult>(entity);

        return entity;
    }

    private Entity CreateBelt(int2 position, DirectionEnum direction, float speed = 2.0f)
    {
        var entity = _entityManager.CreateEntity(
            typeof(GridPosition),
            typeof(Direction),
            typeof(BeltComponent));

        _entityManager.SetComponentData(entity, new GridPosition(position));
        _entityManager.SetComponentData(entity, new Direction(direction));
        _entityManager.SetComponentData(entity, new BeltComponent(speed));
        return entity;
    }

    private Entity CreateStorage(int2 position, int2 size, DirectionEnum direction = DirectionEnum.Up, int slotCount = 4)
    {
        var entity = _entityManager.CreateEntity(
            typeof(BuildingType),
            typeof(BuildingFootprint),
            typeof(GridPosition),
            typeof(Direction),
            typeof(Storage),
            typeof(BuildingItemOutputDecision));

        _entityManager.SetComponentData(entity, new BuildingType(BuildingTypeEnum.Storage));
        _entityManager.SetComponentData(entity, new BuildingFootprint(size));
        _entityManager.SetComponentData(entity, new GridPosition(position));
        _entityManager.SetComponentData(entity, new Direction(direction));
        _entityManager.SetComponentData(entity, new Storage(slotCount));
        _entityManager.SetComponentData(entity, new BuildingItemOutputDecision(false, Entity.Null, int2.zero));
        _entityManager.SetComponentEnabled<BuildingItemOutputDecision>(entity, false);

        _entityManager.AddBuffer<StoredItemElement>(entity);

        return entity;
    }

    private void RunSimulationTicks(int tickCount, float deltaTime = 0.05f)
    {
        for (int i = 0; i < tickCount; i++)
        {
            _elapsedTime += deltaTime;
            _world.SetTime(new Unity.Core.TimeData(_elapsedTime, deltaTime));
            _simulationGroup.Update();
        }
    }

    [Test]
    public void Test01_SingleItem_MiningToStorage_CompleteLoop()
    {
        // 1. 배치: (10, 10) 철광석 1개 -> (10, 10) 채굴기 -> (10, 11)~(10, 13) 벨트 3칸 -> (10, 14) 창고
        var resEntity = CreateResourceNode(new int2(10, 10), ItemTypeEnum.Iron_Ore, 1);
        var minerEntity = CreateMiner(new int2(10, 10), new int2(1, 1), DirectionEnum.Up, miningSpeed: 1.0f);
        CreateBelt(new int2(10, 11), DirectionEnum.Up, speed: 2.0f);
        CreateBelt(new int2(10, 12), DirectionEnum.Up, speed: 2.0f);
        CreateBelt(new int2(10, 13), DirectionEnum.Up, speed: 2.0f);
        var storageEntity = CreateStorage(new int2(10, 14), new int2(1, 1), DirectionEnum.Up, slotCount: 4);

        // 최초 1틱: 초기 공간 색인 구축
        RunSimulationTicks(1, 0.05f);

        // 2. 실행: 60틱 (3.0초) 시뮬레이션
        // T=0~1.0s: 1개 채굴 누적 -> T=1.0s: 버퍼 적재 -> T=1.05s: 벨트 출고 -> T=1.1~2.6s: 벨트 3칸 이동 -> T=2.65s: 창고 입고
        RunSimulationTicks(60, 0.05f);

        // 3. 검증: 자원 채굴 -> 벨트 이동 -> 창고 보관 완료 루프 검증
        var minerBuffer = _entityManager.GetBuffer<ProductItemElement>(minerEntity);
        var storageBuffer = _entityManager.GetBuffer<StoredItemElement>(storageEntity);

        // ① 자원 노드에서 1개 채굴되어 완전 고갈(Depleted)로 인해 엔티티가 파괴되었는지 확인
        Assert.IsFalse(_entityManager.Exists(resEntity), "ResourceNode entity should be destroyed upon depletion.");

        // ② 채굴기 내부 버퍼는 외향 벨트로 성공적으로 방출되어 비어있어야 함
        Assert.AreEqual(0, minerBuffer.Length, "Miner buffer should be empty after releasing item to belt.");

        // ③ 창고에 정확히 1개의 아이템이 입고되어 보관되어야 함
        Assert.AreEqual(1, storageBuffer.Length, "Storage must have received and stored 1 item.");
        Assert.AreEqual(ItemTypeEnum.Iron_Ore, storageBuffer[0].ItemType);

        // ④ 보관된 아이템의 소유권(ItemOwnership) 확인
        Entity storedItemEntity = storageBuffer[0].ItemEntity;
        var ownership = _entityManager.GetComponentData<ItemOwnership>(storedItemEntity);
        Assert.IsTrue(ownership.IsStored, "Item must have IsStored = true.");
        Assert.AreEqual(storageEntity, ownership.Owner, "Item owner must be storageEntity.");

        // ⑤ 벨트 위 월드 아이템은 0개여야 함 (창고로 완주 입고됨)
        var itemSpatialIndex = _entityManager.CreateEntityQuery(typeof(ItemSpatialIndex)).GetSingleton<ItemSpatialIndex>();
        Assert.IsFalse(itemSpatialIndex.HasItemAt(new int2(10, 11)));
        Assert.IsFalse(itemSpatialIndex.HasItemAt(new int2(10, 12)));
        Assert.IsFalse(itemSpatialIndex.HasItemAt(new int2(10, 13)));

        // ⑥ 시뮬레이션 동안 충돌이나 불변식 위반이 0건이어야 함
        Assert.AreEqual(0, _invariantValidationSystem.TotalViolationCount, "No invariant violations during complete loop.");
    }

    [Test]
    public void Test02_ContinuousMining_MultipleItemsStreamToStorage()
    {
        // 1. 배치: (10, 10) 철광석 -> 채굴기(속도 2.0f, 0.5초마다 생산) -> 벨트 5칸 -> 창고(슬롯 10칸)
        var resEntity = CreateResourceNode(new int2(10, 10), ItemTypeEnum.Iron_Ore, 50);
        var minerEntity = CreateMiner(new int2(10, 10), new int2(1, 1), DirectionEnum.Up, miningSpeed: 2.0f);
        for (int y = 11; y <= 15; y++)
        {
            CreateBelt(new int2(10, y), DirectionEnum.Up, speed: 2.0f);
        }
        var storageEntity = CreateStorage(new int2(10, 16), new int2(1, 1), DirectionEnum.Up, slotCount: 10);

        // 최초 1틱: 공간 색인 구축
        RunSimulationTicks(1, 0.05f);

        // 2. 실행: 100틱 (5.0초) 시뮬레이션: 복수 아이템이 벨트를 타고 흘러 창고에 순차 도착
        RunSimulationTicks(100, 0.05f);

        // 3. 검증: 지속적인 채굴로 창고에 여러 개(3개 이상)의 광물이 순차 적재 완료되었는지 확인
        var storageBuffer = _entityManager.GetBuffer<StoredItemElement>(storageEntity);
        Assert.GreaterOrEqual(storageBuffer.Length, 3, "Storage should receive at least 3 items during 5.0s of continuous mining.");

        for (int i = 0; i < storageBuffer.Length; i++)
        {
            Assert.AreEqual(ItemTypeEnum.Iron_Ore, storageBuffer[i].ItemType);
            var itemOwnership = _entityManager.GetComponentData<ItemOwnership>(storageBuffer[i].ItemEntity);
            Assert.IsTrue(itemOwnership.IsStored);
            Assert.AreEqual(storageEntity, itemOwnership.Owner);
        }

        // 전체 시뮬레이션 동안 충돌 및 불변식 위반이 0건이어야 함
        Assert.AreEqual(0, _invariantValidationSystem.TotalViolationCount, "No invariant violations during continuous streaming.");
    }

    [Test]
    public void Test03_StorageFull_TriggersBackpressure_StallsBeltAndMiner()
    {
        // 1. 배치: 슬롯 1칸짜리 작은 창고, 벨트 2칸, 채굴기(속도 5.0f, 0.2초마다 생산)
        var resEntity = CreateResourceNode(new int2(10, 10), ItemTypeEnum.Iron_Ore, 100);
        var minerEntity = CreateMiner(new int2(10, 10), new int2(1, 1), DirectionEnum.Up, miningSpeed: 5.0f);
        CreateBelt(new int2(10, 11), DirectionEnum.Up, speed: 2.0f);
        CreateBelt(new int2(10, 12), DirectionEnum.Up, speed: 2.0f);
        var storageEntity = CreateStorage(new int2(10, 13), new int2(1, 1), DirectionEnum.Up, slotCount: 1);

        // 창고의 1번 슬롯(Slot 0)을 Copper_Ore로 미리 채워 만석(Full)으로 설정 -> Iron_Ore 입고 차단 유도
        var dummyCopperItem = _entityManager.CreateEntity(typeof(ItemIdentity), typeof(ItemOwnership));
        _entityManager.SetComponentData(dummyCopperItem, new ItemIdentity(ItemTypeEnum.Copper_Ore));
        _entityManager.SetComponentData(dummyCopperItem, ItemOwnership.Stored(storageEntity));
        var initialStorageBuffer = _entityManager.GetBuffer<StoredItemElement>(storageEntity);
        initialStorageBuffer.Add(new StoredItemElement(dummyCopperItem, ItemTypeEnum.Copper_Ore, 0));

        // 최초 1틱: 공간 색인 구축
        RunSimulationTicks(1, 0.05f);

        // 2. 실행: 260틱 (13.0초) 시뮬레이션
        // - 1~35틱: 벨트 2칸에 광물 정체 (외향 벨트 출고 차단)
        // - 36~235틱: 채굴기 내부 출력 버퍼(ProductItemElement)에 50개(1스택)까지 적재
        // - 버퍼 50개 도달 시 CanMine = false로 채굴 중단(Stall)
        // - 236~260틱: 백프레셔 정체 유지
        RunSimulationTicks(260, 0.05f);

        // 3. 검증: 공장 전체 백프레셔(Backpressure) 상태 확인
        // ① 창고 만석 상태 유지 확인 (시뮬레이션 후 버퍼 재취득)
        var storageBuffer = _entityManager.GetBuffer<StoredItemElement>(storageEntity);
        Assert.AreEqual(1, storageBuffer.Length, "Storage must remain full with the initial copper item.");
        Assert.AreEqual(ItemTypeEnum.Copper_Ore, storageBuffer[0].ItemType);

        // ② 벨트 위 아이템 연쇄 정체 확인
        var spatialIndex = _entityManager.CreateEntityQuery(typeof(ItemSpatialIndex)).GetSingleton<ItemSpatialIndex>();
        Assert.IsTrue(spatialIndex.HasItemAt(new int2(10, 12)), "Belt tile (10, 12) must have a stalled item waiting for storage.");
        Assert.IsTrue(spatialIndex.HasItemAt(new int2(10, 11)), "Belt tile (10, 11) must have a stalled item waiting for front belt tile.");

        // ③ 채굴기 내부 버퍼 적재 확인 (50개 1스택 만석)
        var minerBuffer = _entityManager.GetBuffer<ProductItemElement>(minerEntity);
        var resNode = _entityManager.GetComponentData<ResourceNode>(resEntity);
        var decision = _entityManager.GetComponentData<MinerDecision>(minerEntity);
        bool isDecisionEnabled = _entityManager.IsComponentEnabled<MinerDecision>(minerEntity);

        Assert.AreEqual(50, minerBuffer.Length, 
            $"MinerBuffer Length={minerBuffer.Length}, ResAmountLeft={resNode.Amount}, CanMine={decision.CanMine}, DecisionEnabled={isDecisionEnabled}");

        // ④ 채굴기 의사결정 비활성화 확인 (버퍼 만석으로 인한 채굴 중단)
        Assert.IsFalse(isDecisionEnabled, "MinerDecision must be disabled when buffer is full.");
        Assert.IsFalse(decision.CanMine, "CanMine must be false under backpressure stall.");

        // ⑤ 역류 정체 중에도 아이템 겹침이나 불변식 위반이 0건이어야 함
        Assert.AreEqual(0, _invariantValidationSystem.TotalViolationCount, "No invariant violations during backpressure stall.");
    }
}
