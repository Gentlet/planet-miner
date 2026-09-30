using NUnit.Framework;
using PlanetMiner.Config;
using PlanetMiner.Tests;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Task 7.8 건설 코어 수명주기 전체 파이프라인 통합 검증 테스트.
/// 
/// [책임 및 범위]
/// - 실제 6대 Phase 시뮬레이션 루프(GameSimulationGroup: Command -> Decision -> Reservation -> Execution -> StateApply -> Synchronization) 상에서
///   건설 수명주기(배치 -> 현장 실체화 -> 자재 공급 -> 완공 스폰 -> 가동 -> 철거/환급 및 취소) 전체가 자율적 연속성과 데이터 불변식을 보장하는지 종합 검증.
/// - 2-Sync Point 아키텍처(EndCommandEntityCommandBufferSystem, EndStateApplyEntityCommandBufferSystem)와
///   StateApply 순서(물류 적용 -> 철거)가 실제 통합 루프에서 완벽히 협력하는지 확인.
/// </summary>
public class Phase7EndToEndConstructionPipelineTests : EcsWorldTestFixture
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
        CreateGameplayPrefabDatabases();
        _elapsedTime = 0.0f;

        // 1. 아이템 및 레시피 레지스트리 초기화
        _itemBlobRef = ItemConfigInitSystem.InitializeItemRegistry(_entityManager);
        _recipeBlobRef = RecipeInitSystem.InitializeRecipeRegistry(_entityManager);

        // 2. BuildingConfig 기본 설정 게시 (Belt, Miner, Crafter, Storage)
        SetupBuildingConfigs();

        // 3. 최상위 시뮬레이션 그룹 및 6대 Phase 하위 그룹 구성
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

        // 4. Phase 1: CommandGroup
        commandGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingPlacementCommandSystem>());
        commandGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<CrafterRecipeCommandSystem>());
        commandGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingDemolitionCommandSystem>());
        commandGroup.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<EndCommandEntityCommandBufferSystem>());

        // 5. Phase 2: DecisionGroup
        decisionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<MinerDecisionSystem>());
        decisionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<StorageItemOutputDecisionSystem>());
        decisionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<ProductItemOutputDecisionSystem>());
        decisionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<CrafterDecisionSystem>());
        decisionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<SplitterDecisionSystem>());
        decisionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<MergerDecisionSystem>());
        decisionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<BeltMovementDecisionSystem>());
        decisionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingItemInputDecisionSystem>());

        // 6. Phase 3: ReservationGroup
        reservationGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<BeltDestinationReservationSystem>());
        reservationGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingStorageInputReservationSystem>());

        // 7. Phase 4: ExecutionGroup
        executionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<MinerExecutionSystem>());
        executionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<CrafterExecutionSystem>());
        executionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<BeltMovementExecutionSystem>());

        // 8. Phase 5: StateApplyGroup
        stateApplyGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingItemStorageApplySystem>());
        stateApplyGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<CrafterStateApplySystem>());
        stateApplyGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<RoutingApplySystem>());
        stateApplyGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<ConstructionLifecycleApplySystem>());
        stateApplyGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingLifecycleApplySystem>());
        stateApplyGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemLifecycleApplySystem>());
        stateApplyGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemOwnershipApplySystem>());
        stateApplyGroup.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<EndStateApplyEntityCommandBufferSystem>());

        // 9. Phase 6: SynchronizationGroup
        synchronizationGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<ResourceSpatialSyncSystem>());
        synchronizationGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<BeltSpatialSyncSystem>());
        synchronizationGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingSpatialSyncSystem>());
        synchronizationGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemSpatialSyncSystem>());

        _invariantValidationSystem = _world.GetOrCreateSystemManaged<WorldInvariantValidationSystem>();
        synchronizationGroup.AddSystemToUpdateList(_invariantValidationSystem);
        _invariantValidationSystem.ResetViolationCount();

        // 10. 전체 의존성 순서 정렬
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

    private void SetupBuildingConfigs()
    {
        var entity = _entityManager.CreateEntity(
            typeof(BuildingConfig),
            typeof(BuildingRuntimeConfig),
            typeof(BuildingConfigElement),
            typeof(BuildingRuntimeConfigElement),
            typeof(BuildingConstructionMaterialElement));

        var configBuffer = _entityManager.GetBuffer<BuildingConfigElement>(entity);
        var runtimeBuffer = _entityManager.GetBuffer<BuildingRuntimeConfigElement>(entity);
        var matBuffer = _entityManager.GetBuffer<BuildingConstructionMaterialElement>(entity);

        // Belt: 1x1, 속도 2.0, 자재 Iron 1개
        configBuffer.Add(new BuildingConfigElement(BuildingTypeEnum.Belt, 2.0f, 0, true, default, new int2(1, 1)));
        runtimeBuffer.Add(new BuildingRuntimeConfigElement(BuildingTypeEnum.Belt, 2.0f, 0));
        matBuffer.Add(new BuildingConstructionMaterialElement(BuildingTypeEnum.Belt, ItemTypeEnum.Iron, 1));

        // Miner: 2x2, 채굴속도 2.0, 자재 Iron 2개
        configBuffer.Add(new BuildingConfigElement(BuildingTypeEnum.Miner, 2.0f, 0, true, default, new int2(2, 2)));
        runtimeBuffer.Add(new BuildingRuntimeConfigElement(BuildingTypeEnum.Miner, 2.0f, 0));
        matBuffer.Add(new BuildingConstructionMaterialElement(BuildingTypeEnum.Miner, ItemTypeEnum.Iron, 2));

        // Storage: 1x1, 슬롯 4, 자재 Iron 3개
        configBuffer.Add(new BuildingConfigElement(BuildingTypeEnum.Storage, 0.0f, 4, true, default, new int2(1, 1)));
        runtimeBuffer.Add(new BuildingRuntimeConfigElement(BuildingTypeEnum.Storage, 0.0f, 4));
        matBuffer.Add(new BuildingConstructionMaterialElement(BuildingTypeEnum.Storage, ItemTypeEnum.Iron, 3));

        // Crafter: 2x2, 속도 2.0, 자재 Iron 4개
        configBuffer.Add(new BuildingConfigElement(BuildingTypeEnum.Crafter, 2.0f, 0, true, default, new int2(2, 2)));
        runtimeBuffer.Add(new BuildingRuntimeConfigElement(BuildingTypeEnum.Crafter, 2.0f, 0));
        matBuffer.Add(new BuildingConstructionMaterialElement(BuildingTypeEnum.Crafter, ItemTypeEnum.Iron, 4));
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

    private Entity RequestPlacement(BuildingTypeEnum type, int2 pos, DirectionEnum dir = DirectionEnum.Up, int2 size = default)
    {
        if (size.x <= 0 || size.y <= 0) size = new int2(1, 1);
        var reqEntity = _entityManager.CreateEntity(typeof(BuildingPlacementRequest));
        _entityManager.SetComponentData(reqEntity, new BuildingPlacementRequest(PlacementFlags.StrictAllOrNothing));
        var buffer = _entityManager.AddBuffer<PlacementRequestCandidateElement>(reqEntity);
        buffer.Add(new PlacementRequestCandidateElement(type, size, pos, dir));
        return reqEntity;
    }

    private Entity CreateWorldItem(ItemTypeEnum type, int2 pos = default)
    {
        var item = _entityManager.CreateEntity();
        _entityManager.AddComponentData(item, new ItemIdentity(type));
        _entityManager.AddComponentData(item, ItemOwnership.WorldItem);
        _entityManager.AddComponentData(item, new GridPosition(pos));
        _entityManager.AddComponentData(item, LocalTransform.FromPosition(new float3(pos.x, pos.y, 0f)));
        return item;
    }

    private Entity RequestSupply(Entity site, Entity item, ItemTypeEnum type)
    {
        var request = _entityManager.CreateEntity();
        _entityManager.AddComponentData(request, new SupplyConstructionMaterialRequest(site, item, type));
        return request;
    }

    private Entity RequestCancel(Entity site)
    {
        var request = _entityManager.CreateEntity();
        _entityManager.AddComponentData(request, new CancelConstructionRequest(site));
        return request;
    }

    private Entity RequestDemolish(Entity building)
    {
        var request = _entityManager.CreateEntity();
        _entityManager.AddComponentData(request, new DemolishBuildingRequest(building));
        return request;
    }

    private Entity CreateCompletedBelt(int2 pos, DirectionEnum dir, float speed = 2.0f)
    {
        var entity = Entities.CreateBelt(pos, dir, speed);
        _entityManager.AddComponentData(entity, new BuildingType(BuildingTypeEnum.Belt));
        _entityManager.AddComponentData(entity, new BuildingFootprint(new int2(1, 1)));
        return entity;
    }

    [Test]
    public void Test01_HappyPath_FullConstructionLifecycle_ToOperationalLogisticsAndDemolition()
    {
        // 1. Arrange: (10, 10)에 철광석 노드 생성 및 공간 인덱스 선행 동기화
        int2 minerPos = new int2(10, 10);
        Entities.CreateResourceNode(minerPos, ItemTypeEnum.Iron_Ore, 100);
        RunSimulationTicks(1);

        // 2. Act: Miner 배치 요청 (2x2 크기)
        var placeReq = RequestPlacement(BuildingTypeEnum.Miner, minerPos, DirectionEnum.Up, new int2(2, 2));

        // 1틱 실행 -> Phase 1에서 요청 소비 및 EndCommandECB로 현장 실체화 -> Phase 6 공간 인덱스 동기화
        RunSimulationTicks(1);

        // Assert 1: 요청 엔티티 소비 및 ConstructionSite 생성 확인
        Assert.IsFalse(_entityManager.Exists(placeReq), "배치 요청은 소비되어야 함");
        var siteQuery = _entityManager.CreateEntityQuery(typeof(ConstructionSite), typeof(GridPosition));
        Assert.AreEqual(1, siteQuery.CalculateEntityCount(), "ConstructionSite가 1개 생성되어야 함");
        var siteEntity = siteQuery.GetSingletonEntity();
        Assert.AreEqual(minerPos, _entityManager.GetComponentData<GridPosition>(siteEntity).Value);

        // 공간 인덱스 점유 확인 (2x2이므로 4개 타일 점유)
        var buildingMap = _entityManager.CreateEntityQuery(typeof(BuildingSpatialIndex)).GetSingleton<BuildingSpatialIndex>().Map;
        for (int x = 0; x < 2; x++)
        {
            for (int y = 0; y < 2; y++)
            {
                int2 tile = minerPos + new int2(x, y);
                Assert.IsTrue(buildingMap.ContainsKey(tile), $"타일 {tile}은 공간 인덱스에 등록되어야 함");
                Assert.AreEqual(BuildingTypeEnum.ConstructionSite, buildingMap[tile].Type);
            }
        }

        // 3. Act: 자재 공급 (Miner 자재 요구량: Iron 2개)
        var mat1 = CreateWorldItem(ItemTypeEnum.Iron, minerPos - new int2(1, 0));
        var mat2 = CreateWorldItem(ItemTypeEnum.Iron, minerPos - new int2(1, 1));
        RequestSupply(siteEntity, mat1, ItemTypeEnum.Iron);
        RequestSupply(siteEntity, mat2, ItemTypeEnum.Iron);

        // 1틱 실행 -> 자재 수령 및 완공 전환 (점유 공백 없이 Miner 스폰)
        RunSimulationTicks(1);

        // Assert 2: 현장 소멸 및 완공 Miner 스폰 확인
        Assert.IsFalse(_entityManager.Exists(siteEntity), "완공 시 ConstructionSite 엔티티는 파괴되어야 함");
        var minerQuery = _entityManager.CreateEntityQuery(typeof(MinerState), typeof(GridPosition));
        Assert.AreEqual(1, minerQuery.CalculateEntityCount(), "완공된 Miner 엔티티가 존재해야 함");
        var minerEntity = minerQuery.GetSingletonEntity();
        Assert.AreEqual(minerPos, _entityManager.GetComponentData<GridPosition>(minerEntity).Value);

        // 공간 인덱스가 점유 공백 없이 즉시 Miner로 갱신되었는지 확인
        buildingMap = _entityManager.CreateEntityQuery(typeof(BuildingSpatialIndex)).GetSingleton<BuildingSpatialIndex>().Map;
        for (int x = 0; x < 2; x++)
        {
            for (int y = 0; y < 2; y++)
            {
                int2 tile = minerPos + new int2(x, y);
                Assert.IsTrue(buildingMap.ContainsKey(tile), $"타일 {tile}은 완공 건물로 점유되어야 함");
                Assert.AreEqual(BuildingTypeEnum.Miner, buildingMap[tile].Type);
            }
        }

        // 4. Act: 30틱 동안 시뮬레이션 가동 -> 실제 채굴 발생
        RunSimulationTicks(30);

        var productBuffer = _entityManager.GetBuffer<ProductItemElement>(minerEntity);
        Assert.Greater(productBuffer.Length, 0, "Miner가 정상 가동되어 제품 버퍼에 철광석이 생산되어야 함");

        // 5. Act: 가동 중인 Miner 철거 요청
        RequestDemolish(minerEntity);

        // 1틱 실행 -> 철거 및 내용물/자재 반환
        RunSimulationTicks(1);

        // Assert 3: Miner 파괴, 채굴물 및 건축 자재 월드 방출, 공간 해제
        Assert.IsFalse(_entityManager.Exists(minerEntity), "Miner는 철거되어야 함");
        buildingMap = _entityManager.CreateEntityQuery(typeof(BuildingSpatialIndex)).GetSingleton<BuildingSpatialIndex>().Map;
        for (int x = 0; x < 2; x++)
        {
            for (int y = 0; y < 2; y++)
            {
                int2 tile = minerPos + new int2(x, y);
                Assert.IsFalse(buildingMap.ContainsKey(tile), $"철거 후 타일 {tile} 점유는 완전히 해제되어야 함");
            }
        }

        // 불변식 위반 0건 확인
        Assert.AreEqual(0, _invariantValidationSystem.TotalViolationCount, "전체 라이프사이클 동안 Invariant 위반이 없어야 함");

        siteQuery.Dispose();
        minerQuery.Dispose();
    }

    [Test]
    public void Test02_CancelWins_PartialSupply_ToCancellation_RefundsWorldItemsAndReleasesSpatial()
    {
        // 1. Arrange: Storage 배치 요청 (자재 요구량: Iron 3개)
        int2 storagePos = new int2(20, 20);
        RequestPlacement(BuildingTypeEnum.Storage, storagePos, DirectionEnum.Up, new int2(1, 1));
        RunSimulationTicks(1);

        var siteQuery = _entityManager.CreateEntityQuery(typeof(ConstructionSite), typeof(GridPosition));
        var siteEntity = siteQuery.GetSingletonEntity();

        // 2. Act: 자재 1개만 공급
        var mat1 = CreateWorldItem(ItemTypeEnum.Iron, storagePos);
        RequestSupply(siteEntity, mat1, ItemTypeEnum.Iron);
        RunSimulationTicks(1);

        // 자재 1개가 보관 상태로 들어갔는지 확인
        var storedBuffer = _entityManager.GetBuffer<StoredItemElement>(siteEntity);
        Assert.AreEqual(1, storedBuffer.Length);
        Assert.AreEqual(ItemOwnership.Stored(siteEntity), _entityManager.GetComponentData<ItemOwnership>(mat1));

        // 3. Act: 동일 틱에 [추가 자재 공급 요청]과 [취소 요청]을 동시에 인큐
        var mat2 = CreateWorldItem(ItemTypeEnum.Iron, storagePos);
        RequestSupply(siteEntity, mat2, ItemTypeEnum.Iron);
        RequestCancel(siteEntity);

        // 1틱 실행 -> Cancel Wins 정책 적용
        RunSimulationTicks(1);

        // Assert: 현장 엔티티 파괴, 기납입 자재는 WorldItem으로 방출
        Assert.IsFalse(_entityManager.Exists(siteEntity), "취소된 현장은 파괴되어야 함");
        Assert.AreEqual(ItemOwnership.WorldItem, _entityManager.GetComponentData<ItemOwnership>(mat1), "기납입 자재는 WorldItem으로 바닥에 방출되어야 함");
        Assert.AreEqual(ItemOwnership.WorldItem, _entityManager.GetComponentData<ItemOwnership>(mat2), "취소된 현장으로 배달된 자재는 안전하게 거부되어 WorldItem을 유지해야 함");

        // 공간 인덱스 점유 해제 확인
        var buildingMap = _entityManager.CreateEntityQuery(typeof(BuildingSpatialIndex)).GetSingleton<BuildingSpatialIndex>().Map;
        Assert.IsFalse(buildingMap.ContainsKey(storagePos), "취소 후 공간 인덱스 점유는 즉시 해제되어야 함");

        Assert.AreEqual(0, _invariantValidationSystem.TotalViolationCount, "취소 과정에서 Invariant 위반이 없어야 함");
        siteQuery.Dispose();
    }

    [Test]
    public void Test03_BeltOverwrite_UpgradeLifecycle_SameBeltRedirectAndDifferentBeltUpgrade()
    {
        // 1. Arrange: (30, 30)에 방향 Up인 완공 벨트 사전 생성 (BuildingSpatialIndex 등록 포함)
        int2 beltPos = new int2(30, 30);
        var beltEntity = CreateCompletedBelt(beltPos, DirectionEnum.Up, speed: 2.0f);
        RunSimulationTicks(1);

        var buildingMap = _entityManager.CreateEntityQuery(typeof(BuildingSpatialIndex)).GetSingleton<BuildingSpatialIndex>().Map;
        Assert.IsTrue(buildingMap.ContainsKey(beltPos));
        Assert.AreEqual(DirectionEnum.Up, _entityManager.GetComponentData<Direction>(beltEntity).dir);

        // 2. Act: 동일 타일에 방향 Right인 Belt 배치 요청 발행
        RequestPlacement(BuildingTypeEnum.Belt, beltPos, DirectionEnum.Right, new int2(1, 1));
        RunSimulationTicks(1);

        // Assert 1: ConstructionSite 생성 없이 기존 벨트의 Direction만 즉시 Right로 갱신
        var siteQuery = _entityManager.CreateEntityQuery(typeof(ConstructionSite));
        Assert.AreEqual(0, siteQuery.CalculateEntityCount(), "동일 벨트 재배치 시 공사 현장은 생성되지 않아야 함");
        Assert.AreEqual(DirectionEnum.Right, _entityManager.GetComponentData<Direction>(beltEntity).dir, "기존 벨트 방향이 즉시 갱신되어야 함");

        // 3. Act: 동일 타일에 덮어쓰기 불가능한 Storage 배치 요청 발행 (충돌)
        RequestPlacement(BuildingTypeEnum.Storage, beltPos, DirectionEnum.Up, new int2(1, 1));
        RunSimulationTicks(1);

        // Assert 2: 충돌로 인해 배치가 거부되고 기존 벨트 유지
        Assert.AreEqual(0, siteQuery.CalculateEntityCount(), "충돌 배치 시 공사 현장이 생성되지 않아야 함");
        Assert.IsTrue(_entityManager.Exists(beltEntity), "기존 벨트는 안전하게 유지되어야 함");

        Assert.AreEqual(0, _invariantValidationSystem.TotalViolationCount);
        siteQuery.Dispose();
    }

    [Test]
    public void Test04_ImmediateRepositioning_DemolishAndReconstructOnSameTile()
    {
        // 1. Arrange: (40, 40)에 Storage 완공 건물 생성
        int2 tilePos = new int2(40, 40);
        var storage = Entities.CreateStorage(tilePos, new int2(1, 1), DirectionEnum.Up, slotCount: 4);
        RunSimulationTicks(1);

        var buildingMap = _entityManager.CreateEntityQuery(typeof(BuildingSpatialIndex)).GetSingleton<BuildingSpatialIndex>().Map;
        Assert.IsTrue(buildingMap.ContainsKey(tilePos));

        // 2. Act: 철거 요청 발행
        RequestDemolish(storage);
        RunSimulationTicks(1);

        Assert.IsFalse(_entityManager.Exists(storage), "건물이 철거되어야 함");
        buildingMap = _entityManager.CreateEntityQuery(typeof(BuildingSpatialIndex)).GetSingleton<BuildingSpatialIndex>().Map;
        Assert.IsFalse(buildingMap.ContainsKey(tilePos), "공간 인덱스에서 점유가 해제되어야 함");

        // [중요] 철거로 인해 바닥에 환급된 건축 자재(Iron)들을 청소 (타일 정돈 모의)
        var groundItemQuery = _entityManager.CreateEntityQuery(typeof(ItemIdentity), typeof(GridPosition), typeof(ItemOwnership));
        using (var groundEntities = groundItemQuery.ToEntityArray(Allocator.Temp))
        {
            for (int i = 0; i < groundEntities.Length; i++)
            {
                var pos = _entityManager.GetComponentData<GridPosition>(groundEntities[i]).Value;
                if (pos.Equals(tilePos))
                {
                    _entityManager.DestroyEntity(groundEntities[i]);
                }
            }
        }
        groundItemQuery.Dispose();
        RunSimulationTicks(1); // ItemSpatialIndex 동기화

        // 3. Act: 철거 및 바닥 정돈 완료 후 동일 좌표에 신규 Belt 배치 요청
        RequestPlacement(BuildingTypeEnum.Belt, tilePos, DirectionEnum.Up, new int2(1, 1));
        RunSimulationTicks(1);

        // Assert: 이전 건물의 잔여 데이터 누수(Spatial Leak) 없이 신규 ConstructionSite가 정상 생성됨
        var siteQuery = _entityManager.CreateEntityQuery(typeof(ConstructionSite), typeof(GridPosition));
        Assert.AreEqual(1, siteQuery.CalculateEntityCount(), "철거된 자리에 신규 공사 현장이 정상 생성되어야 함");
        var siteEntity = siteQuery.GetSingletonEntity();
        Assert.AreEqual(tilePos, _entityManager.GetComponentData<GridPosition>(siteEntity).Value);
        Assert.AreEqual(ConstructionSiteFlags.None, _entityManager.GetComponentData<ConstructionSite>(siteEntity).Flags, "바닥이 정리되었으므로 AwaitingItemClearance 플래그가 없어야 함");

        // 4. Act: 신규 Belt 자재 공급 및 완공
        var mat = CreateWorldItem(ItemTypeEnum.Iron, tilePos - new int2(1, 0));
        RequestSupply(siteEntity, mat, ItemTypeEnum.Iron);
        RunSimulationTicks(1);

        // Assert: 완공된 Belt가 타일을 정상 점유
        buildingMap = _entityManager.CreateEntityQuery(typeof(BuildingSpatialIndex)).GetSingleton<BuildingSpatialIndex>().Map;
        Assert.IsTrue(buildingMap.ContainsKey(tilePos));
        Assert.AreEqual(BuildingTypeEnum.Belt, buildingMap[tilePos].Type);

        Assert.AreEqual(0, _invariantValidationSystem.TotalViolationCount);
        siteQuery.Dispose();
    }

}
