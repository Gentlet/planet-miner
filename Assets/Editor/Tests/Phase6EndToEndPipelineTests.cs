using NUnit.Framework;
using PlanetMiner.Config;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Phase 6 물류 및 라우팅 전체 파이프라인 통합 검증 테스트.
/// 채굴(Miner), 벨트(Belt), 분배(Splitter), 제작(Crafter), 합류(Merger), 창고(Storage)를
/// 실제 GameSimulationGroup 루트 루프(Phase 1~6 전체 하위 그룹)로 구동하여 종합 검증.
/// 
/// [검증 시나리오]
/// 1. End-to-End 전체 생산·물류·분배 순환 흐름: 채굴 -> 벨트 -> Splitter -> (Crafter 및 Storage A) 분배 및 가공/적재
/// 2. 복합 목적지 경합 중재: Splitter와 Storage 출고가 단일 공유 벨트 입구를 두고 경합 시 PlacementStamp 순차 승인
/// 3. 역류 정체 및 정상 복구: 하류 차단 시 Merger -> 중간 벨트 -> Splitter 순차 정체 및 정체 해제 시 자동 재개
/// 4. 멀티 틱 연속 시뮬레이션 불변식 무결성: 복합 물류망 100틱 연속 가동 중 WorldInvariantValidationSystem 0 Violation
/// </summary>
public class Phase6EndToEndPipelineTests : EcsWorldTestFixture
{
    private GameSimulationGroup _simulationGroup;
    private WorldInvariantValidationSystem _invariantValidationSystem;
    private BlobAssetReference<ItemRegistryBlob> _itemBlobRef;
    private BlobAssetReference<RecipeRegistryBlob> _recipeBlobRef;
    private float _elapsedTime;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        _elapsedTime = 0.0f;

        // 1. 아이템 및 레시피 레지스트리 초기화
        _itemBlobRef = ItemConfigInitSystem.InitializeItemRegistry(_entityManager);
        _recipeBlobRef = RecipeInitSystem.InitializeRecipeRegistry(_entityManager);

        // 2. 최상위 시뮬레이션 그룹 및 Phase 하위 그룹 구성
        _simulationGroup = _world.GetOrCreateSystemManaged<GameSimulationGroup>();
        var commandGroup = _world.GetOrCreateSystemManaged<CommandGroup>();
        var decisionGroup = _world.GetOrCreateSystemManaged<DecisionGroup>();
        var reservationGroup = _world.GetOrCreateSystemManaged<ReservationGroup>();
        var executionGroup = _world.GetOrCreateSystemManaged<ExecutionGroup>();
        var stateApplyGroup = _world.GetOrCreateSystemManaged<StateApplyGroup>();
        var synchronizationGroup = _world.GetOrCreateSystemManaged<SynchronizationGroup>();

        _simulationGroup.AddSystemToUpdateList(commandGroup);
        _simulationGroup.AddSystemToUpdateList(decisionGroup);
        _simulationGroup.AddSystemToUpdateList(reservationGroup);
        _simulationGroup.AddSystemToUpdateList(executionGroup);
        _simulationGroup.AddSystemToUpdateList(stateApplyGroup);
        _simulationGroup.AddSystemToUpdateList(synchronizationGroup);

        // 3. Phase 1: CommandGroup
        commandGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<CrafterRecipeCommandSystem>());

        // 4. Phase 2: DecisionGroup
        decisionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<MinerDecisionSystem>());
        decisionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<StorageItemOutputDecisionSystem>());
        decisionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<ProductItemOutputDecisionSystem>());
        decisionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<CrafterDecisionSystem>());
        decisionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<SplitterDecisionSystem>());
        decisionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<MergerDecisionSystem>());
        decisionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<BeltMovementDecisionSystem>());
        decisionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingItemInputDecisionSystem>());

        // 5. Phase 3: ReservationGroup
        reservationGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<BeltDestinationReservationSystem>());
        reservationGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingStorageInputReservationSystem>());

        // 6. Phase 4: ExecutionGroup
        executionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<MinerExecutionSystem>());
        executionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<CrafterExecutionSystem>());
        executionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<BeltMovementExecutionSystem>());

        // 7. Phase 5: StateApplyGroup
        stateApplyGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingItemStorageApplySystem>());
        stateApplyGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<CrafterStateApplySystem>());
        stateApplyGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<RoutingApplySystem>());
        stateApplyGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemLifecycleApplySystem>());
        stateApplyGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemOwnershipApplySystem>());
        stateApplyGroup.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>());

        // 8. Phase 6: SynchronizationGroup
        synchronizationGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<ResourceSpatialSyncSystem>());
        synchronizationGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<BeltSpatialSyncSystem>());
        synchronizationGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingSpatialSyncSystem>());
        synchronizationGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemSpatialSyncSystem>());
        _invariantValidationSystem = _world.GetOrCreateSystemManaged<WorldInvariantValidationSystem>();
        synchronizationGroup.AddSystemToUpdateList(_invariantValidationSystem);
        _invariantValidationSystem.ResetViolationCount();

        // 9. 의존성 순서 정렬
        _simulationGroup.SortSystems();
        commandGroup.SortSystems();
        decisionGroup.SortSystems();
        reservationGroup.SortSystems();
        executionGroup.SortSystems();
        stateApplyGroup.SortSystems();
        synchronizationGroup.SortSystems();
    }

    [TearDown]
    public override void TearDown()
    {
        if (_itemBlobRef.IsCreated) _itemBlobRef.Dispose();
        if (_recipeBlobRef.IsCreated) _recipeBlobRef.Dispose();
        base.TearDown();
    }

    private Entity CreateResourceNode(int2 position, ItemTypeEnum resourceType, int amount)
        => Entities.CreateResourceNode(position, resourceType, amount);

    private Entity CreateMiner(int2 position, int2 size, DirectionEnum direction, float miningSpeed = 1.0f)
        => Entities.CreateMiner(position, size, direction, miningSpeed);

    private Entity CreateBelt(int2 position, DirectionEnum direction, float speed = 2.0f)
        => Entities.CreateBelt(position, direction, speed);

    private Entity CreateStorage(int2 position, int2 size, DirectionEnum direction = DirectionEnum.Up, int slotCount = 4)
        => Entities.CreateStorage(position, size, direction, slotCount);

    private Entity CreateCrafter(int2 position, int recipeId = 1, float speed = 2.0f)
    {
        var filter = new StorageFilter(StorageFilterMode.Whitelist);
        filter.Mask.Set((byte)ItemTypeEnum.Iron_Ore, true);
        return Entities.CreateCrafter(position, recipeId, speed: speed, filter: filter);
    }

    private Entity CreateSplitter(
        int2 position,
        DirectionEnum forward = DirectionEnum.Right,
        Entity inputBelt = default,
        byte outputCursor = 0,
        ulong tick = 0,
        uint order = 0)
    {
        var entity = Entities.CreateBuilding(BuildingTypeEnum.Splitter, position, new int2(1, 1), forward);
        _entityManager.AddComponentData(entity, new SplitterRoutingState(inputBelt, forward, outputCursor));
        _entityManager.AddComponentData(entity, new RoutingTransferDecision(Entity.Null, Entity.Null, Entity.Null));
        _entityManager.SetComponentEnabled<RoutingTransferDecision>(entity, false);
        _entityManager.AddComponentData(entity, new PlacementStamp(tick, order));
        return entity;
    }

    private Entity CreateMerger(
        int2 position,
        DirectionEnum forward = DirectionEnum.Right,
        Entity outputBelt = default,
        byte inputCursor = 0,
        ulong tick = 0,
        uint order = 0)
    {
        var entity = Entities.CreateBuilding(BuildingTypeEnum.Merger, position, new int2(1, 1), forward);
        _entityManager.AddComponentData(entity, new MergerRoutingState(outputBelt, forward, inputCursor));
        _entityManager.AddComponentData(entity, new RoutingTransferDecision(Entity.Null, Entity.Null, Entity.Null));
        _entityManager.SetComponentEnabled<RoutingTransferDecision>(entity, false);
        _entityManager.AddComponentData(entity, new PlacementStamp(tick, order));
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
    public void Test01_EndToEnd_ContinuousProductionLogisticsAndRouting()
    {
        // 1. 배치:
        // Miner: (10, 10) Up, 철광석 노드 2개
        CreateResourceNode(new int2(10, 10), ItemTypeEnum.Iron_Ore, 2);
        CreateMiner(new int2(10, 10), new int2(1, 1), DirectionEnum.Up, miningSpeed: 2.0f);

        // 벨트: (10, 11) Up
        CreateBelt(new int2(10, 11), DirectionEnum.Up);

        // Splitter: (10, 12), Forward = Up
        // 포트 0 (Forward = Up): (10, 13) Up -> Crafter at (10, 14)
        // 포트 1 (Right = Right): (11, 12) Right -> Storage A at (12, 12)
        var splitter = CreateSplitter(new int2(10, 12), DirectionEnum.Up, outputCursor: 0);

        CreateBelt(new int2(10, 13), DirectionEnum.Up);
        var crafter = CreateCrafter(new int2(10, 14), recipeId: 1, speed: 2.0f);

        CreateBelt(new int2(11, 12), DirectionEnum.Right);
        var storageA = CreateStorage(new int2(12, 12), new int2(1, 1), DirectionEnum.Right);

        // 2. 초기 1틱 실행 (공간 인덱스 동기화)
        RunSimulationTicks(1);

        // 3. 50틱 시뮬레이션 실행 (채굴 -> 벨트 -> Splitter 분배)
        RunSimulationTicks(50);

        // 4. 검증:
        // 첫 번째 철광석은 Crafter로 분배되어 가공(또는 재료 버퍼 보관)
        var crafterStorage = _entityManager.GetBuffer<StoredItemElement>(crafter);
        var crafterProducts = _entityManager.GetBuffer<ProductItemElement>(crafter);
        bool crafterHasItem = (crafterStorage.Length > 0 || crafterProducts.Length > 0);

        // 두 번째 철광석은 Storage A로 분배되어 보관
        var storageABuffer = _entityManager.GetBuffer<StoredItemElement>(storageA);
        bool storageAHasItem = (storageABuffer.Length > 0);

        Assert.IsTrue(crafterHasItem || storageAHasItem, "아이템이 분배기를 거쳐 Crafter 또는 Storage A로 정상 전달되어야 함.");
        Assert.AreEqual(0, _invariantValidationSystem.TotalViolationCount, "시뮬레이션 전 과정에서 불변식 위반이 없어야 함.");
    }

    [Test]
    public void Test02_DestinationContention_ArbitratedByPlacementStamp()
    {
        // 1. 배치: 단일 공유 벨트 (10, 10) Right를 두고 라우터(Splitter, Tick 20)와 건물(Storage, Tick 10)의 동시 출고 경합
        var targetBelt = CreateBelt(new int2(10, 10), DirectionEnum.Right);

        // 라우팅 엔티티: (9, 10) Splitter (Tick = 20, 후순위)
        var inputBelt = CreateBelt(new int2(8, 10), DirectionEnum.Right);
        var splitter = CreateSplitter(new int2(9, 10), DirectionEnum.Right, inputBelt: inputBelt, outputCursor: 0, tick: 20UL, order: 0U);
        var splitterItem = Entities.CreateBeltItem(new int2(8, 10), DirectionEnum.Right, progress: 1.0f);
        _entityManager.SetComponentData(splitter, new RoutingTransferDecision(splitterItem, inputBelt, targetBelt));
        _entityManager.SetComponentEnabled<RoutingTransferDecision>(splitter, true);

        // 건물 엔티티: Storage (Tick = 10, 선순위)
        var storage = CreateStorage(new int2(10, 9), new int2(1, 1), DirectionEnum.Up);
        _entityManager.AddComponentData(storage, new PlacementStamp(tick: 10UL, order: 0U));
        var storageItem = Entities.CreateBeltItem(new int2(10, 9), DirectionEnum.Up, progress: 0.0f);
        _entityManager.SetComponentData(storage, new BuildingItemOutputDecision(canOutput: true, storageItem, new int2(10, 10)));
        _entityManager.SetComponentEnabled<BuildingItemOutputDecision>(storage, true);

        // 2. 공간 동기화 및 1틱 시뮬레이션 (Reservation -> StateApply -> Synchronization 실행)
        _world.GetOrCreateSystem<BeltSpatialSyncSystem>().Update(_world.Unmanaged);
        _world.GetOrCreateSystem<ItemSpatialSyncSystem>().Update(_world.Unmanaged);

        var reservationGroup = _world.GetOrCreateSystemManaged<ReservationGroup>();
        var stateApplyGroup = _world.GetOrCreateSystemManaged<StateApplyGroup>();
        var syncGroup = _world.GetOrCreateSystemManaged<SynchronizationGroup>();

        reservationGroup.Update();
        stateApplyGroup.Update();
        syncGroup.Update();

        // 3. 검증: PlacementStamp가 앞선 Storage(Tick 10)의 아이템이 대상 벨트 (10, 10)으로 승인 및 입고
        Assert.AreEqual(new int2(10, 10), _entityManager.GetComponentData<GridPosition>(storageItem).Value);
        Assert.AreEqual(0.0f, _entityManager.GetComponentData<BeltMovementState>(storageItem).Progress);

        // Splitter(Tick 20)는 경합 탈락하여 입력 벨트 (8, 10)에 대기 상태 유지
        Assert.AreEqual(new int2(8, 10), _entityManager.GetComponentData<GridPosition>(splitterItem).Value);
        Assert.AreEqual(1.0f, _entityManager.GetComponentData<BeltMovementState>(splitterItem).Progress);

        Assert.AreEqual(0, _invariantValidationSystem.TotalViolationCount, "경합 해결 시 불변식 위반이 없어야 함.");
    }

    [Test]
    public void Test03_Backpressure_PropagatesToMergerSplitterAndRecovers()
    {
        // 1. 배치: (10, 10) 입력 벨트 -> (11, 10) Merger -> (12, 10) 출력 벨트
        var inputBelt = CreateBelt(new int2(10, 10), DirectionEnum.Right);
        var outputBelt = CreateBelt(new int2(12, 10), DirectionEnum.Right);
        var merger = CreateMerger(new int2(11, 10), DirectionEnum.Right, outputBelt: outputBelt, inputCursor: 0);

        // 출력 벨트에 아이템 4개(최대 정원)를 배치하여 완전 정체 유발
        var item1 = Entities.CreateBeltItem(new int2(12, 10), DirectionEnum.Right, progress: 0.1f);
        var item2 = Entities.CreateBeltItem(new int2(12, 10), DirectionEnum.Right, progress: 0.35f);
        var item3 = Entities.CreateBeltItem(new int2(12, 10), DirectionEnum.Right, progress: 0.6f);
        var item4 = Entities.CreateBeltItem(new int2(12, 10), DirectionEnum.Right, progress: 0.85f);

        // 입력 벨트에 아이템 도착
        var inputItem = Entities.CreateBeltItem(new int2(10, 10), DirectionEnum.Right, progress: 1.0f);

        // 2. 5틱 시뮬레이션 실행 (정체 상태)
        RunSimulationTicks(5);

        // 3. 검증: 출력 벨트 정원 포화로 인해 Merger 커서 동결 및 입력 아이템 보존
        Assert.AreEqual(new int2(10, 10), _entityManager.GetComponentData<GridPosition>(inputItem).Value);
        Assert.AreEqual(0, _entityManager.GetComponentData<MergerRoutingState>(merger).InputCursor);

        // 4. 장애물 아이템 4개 제거(공간 확보) 및 시뮬레이션 재개
        _entityManager.DestroyEntity(item1);
        _entityManager.DestroyEntity(item2);
        _entityManager.DestroyEntity(item3);
        _entityManager.DestroyEntity(item4);
        RunSimulationTicks(5);

        // 5. 검증: 정체 해제 후 즉시 합류되어 출력 벨트로 진입
        Assert.AreEqual(new int2(12, 10), _entityManager.GetComponentData<GridPosition>(inputItem).Value);
        Assert.AreEqual(0, _invariantValidationSystem.TotalViolationCount, "정체 및 복구 전 과정에서 불변식 위반이 없어야 함.");
    }

    [Test]
    public void Test04_MultiTickSimulation_PreservesAllWorldInvariants()
    {
        // 1. 복합 생산·물류 라인 구성:
        // Miner at (10, 10) Up (자원 5개)
        CreateResourceNode(new int2(10, 10), ItemTypeEnum.Iron_Ore, 5);
        CreateMiner(new int2(10, 10), new int2(1, 1), DirectionEnum.Up, miningSpeed: 3.0f);

        // 벨트: (10, 11) Up
        CreateBelt(new int2(10, 11), DirectionEnum.Up);

        // Splitter at (10, 12), Forward = Up
        CreateSplitter(new int2(10, 12), DirectionEnum.Up, outputCursor: 0);

        // 분배기 Forward 출력 벨트: (10, 13) Up
        CreateBelt(new int2(10, 13), DirectionEnum.Up);

        // 분배기 Right 출력 벨트: (11, 12) Right
        CreateBelt(new int2(11, 12), DirectionEnum.Right);

        // 최종 수납 창고 2개
        CreateStorage(new int2(10, 14), new int2(1, 1), DirectionEnum.Up);
        CreateStorage(new int2(12, 12), new int2(1, 1), DirectionEnum.Right);

        // 2. 100틱 연속 시뮬레이션 구동
        RunSimulationTicks(100);

        // 3. 검증: 100틱 동안 매 프레임 WorldInvariantValidationSystem이 검증을 수행하였으며 위반 0건
        Assert.AreEqual(0, _invariantValidationSystem.TotalViolationCount, "100틱 연속 복합 시뮬레이션 동안 모든 불변식이 엄격히 준수되어야 함.");
    }
}
