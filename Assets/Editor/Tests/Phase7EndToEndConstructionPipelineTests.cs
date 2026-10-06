using NUnit.Framework;
using PlanetMiner.Config;
using PlanetMiner.Tests;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>
/// 역할·목적: 배치→현장→완공→물류→철거·취소 통합에 대한 NUnit EditMode 회귀 검증.
/// 입력·검사: 정렬된 여섯 phase/두 ECB를 실행하되 도착 실물/수량은 fixture로 준비한다. 자재 운송/실제 장면은 실행하지 않고 같은 틱 철거 뒤 인덱스 갱신 전 재배치 거부를 검사한다.
/// 수명: EcsWorldTestFixture가 각 사례의 독립 World를 준비하고 종료 시 해제한다.
/// </summary>
public class Phase7EndToEndConstructionPipelineTests : EcsWorldTestFixture
{
    private GameSimulationGroup _simulationGroup;
    private WorldInvariantValidationSystem _invariantValidationSystem;
    private float _elapsedTime;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        CreateGameplayPrefabDatabases();
        _elapsedTime = 0.0f;

        // 1. 아이템 및 레시피 레지스트리 초기화
        ItemConfigInitSystem.InitializeItemRegistry(_entityManager);
        RecipeInitSystem.InitializeRecipeRegistry(_entityManager);

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
        commandGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<ConstructionCancelCommandSystem>());
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
        decisionGroup.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemSpawnAdmissionDecisionSystem>());

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

    private Entity PrepareDeliveredMaterial(Entity site, ItemTypeEnum type)
    {
        // 운송 없이 실물/Owner/현장 버퍼·도착량을 함께 준비하는 건설 수명주기 테스트의 입력 경계다.
        int2 position = _entityManager.GetComponentData<GridPosition>(site).Value;
        Entity item = _entityManager.CreateEntity(
            typeof(ItemIdentity), typeof(ItemOwnership), typeof(GridPosition),
            typeof(LocalTransform), typeof(DisableRendering));
        _entityManager.SetComponentData(item, new ItemIdentity(type));
        _entityManager.SetComponentData(item, ItemOwnership.Stored(site));
        _entityManager.SetComponentData(item, new GridPosition(position));
        _entityManager.SetComponentData(item,
            LocalTransform.FromPosition(new float3(position.x, position.y, 0f)));
        _entityManager.GetBuffer<StoredItemElement>(site).Add(new StoredItemElement(item, type, 0));

        var requirements = _entityManager.GetBuffer<ConstructionMaterialRequirementElement>(site);
        int requirementIndex = -1;
        for (int i = 0; i < requirements.Length; i++)
        {
            if (requirements[i].ItemType == type)
            {
                requirementIndex = i;
                break;
            }
        }
        Assert.GreaterOrEqual(requirementIndex, 0, "준비할 자재는 현장 요구 품목이어야 한다.");
        var requirement = requirements[requirementIndex];
        requirement.DeliveredQuantity++;
        requirements[requirementIndex] = requirement;

        return item;
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

        // 3. Arrange: Miner 자재 Iron 2개가 도착한 현장 상태를 준비한다.
        PrepareDeliveredMaterial(siteEntity, ItemTypeEnum.Iron);
        PrepareDeliveredMaterial(siteEntity, ItemTypeEnum.Iron);

        // 1틱 실행 -> 완공 전환 (점유 공백 없이 Miner 스폰)
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
    public void Test02_CancelConstruction_WithPartialMaterials_ReturnsWorldItemsAndReleasesSpatial()
    {
        // 1. Arrange: Storage 배치 요청 (자재 요구량: Iron 3개)
        int2 storagePos = new int2(20, 20);
        RequestPlacement(BuildingTypeEnum.Storage, storagePos, DirectionEnum.Up, new int2(1, 1));
        RunSimulationTicks(1);

        var siteQuery = _entityManager.CreateEntityQuery(typeof(ConstructionSite), typeof(GridPosition));
        var siteEntity = siteQuery.GetSingletonEntity();

        // 2. Arrange: 자재 1개만 도착한 현장 상태를 준비한다.
        Entity mat1 = PrepareDeliveredMaterial(siteEntity, ItemTypeEnum.Iron);
        RunSimulationTicks(1);

        // 요구량 미충족 상태에서는 현장과 보관 자재가 유지된다.
        var storedBuffer = _entityManager.GetBuffer<StoredItemElement>(siteEntity);
        Assert.AreEqual(1, storedBuffer.Length);
        Assert.AreEqual(ItemOwnership.Stored(siteEntity), _entityManager.GetComponentData<ItemOwnership>(mat1));

        // 3. 취소와 같은 위치의 재배치를 함께 요청한다. 배치 검증은 이전 점유를 읽는다.
        Entity cancelRequest = RequestCancel(siteEntity);
        Entity sameTickPlacement = RequestPlacement(BuildingTypeEnum.Storage, storagePos);

        // 1틱 실행 -> 현장 취소 및 자재 반환
        RunSimulationTicks(1);

        // Assert: 현장 엔티티 파괴, 기납입 자재는 WorldItem으로 방출
        Assert.IsFalse(_entityManager.Exists(siteEntity), "취소된 현장은 파괴되어야 함");
        Assert.IsFalse(_entityManager.Exists(cancelRequest));
        Assert.IsFalse(_entityManager.Exists(sameTickPlacement));
        Assert.AreEqual(0, siteQuery.CalculateEntityCount(), "같은 틱의 재배치는 기존 공간 점유로 거부되어야 함");
        Assert.AreEqual(ItemOwnership.WorldItem, _entityManager.GetComponentData<ItemOwnership>(mat1), "기납입 자재는 WorldItem으로 바닥에 방출되어야 함");

        // 공간 인덱스 점유 해제 확인
        var buildingMap = _entityManager.CreateEntityQuery(typeof(BuildingSpatialIndex)).GetSingleton<BuildingSpatialIndex>().Map;
        Assert.IsFalse(buildingMap.ContainsKey(storagePos), "취소 후 공간 인덱스 점유는 즉시 해제되어야 함");

        Assert.AreEqual(0, _invariantValidationSystem.TotalViolationCount, "취소 과정에서 Invariant 위반이 없어야 함");
        // Synchronization 이후 새 요청은 비워진 점유를 사용하고, 반환품 때문에 완공 정리 대기한다.
        Entity nextTickPlacement = RequestPlacement(BuildingTypeEnum.Storage, storagePos);
        RunSimulationTicks(1);
        Assert.IsFalse(_entityManager.Exists(nextTickPlacement));
        Assert.AreEqual(1, siteQuery.CalculateEntityCount());
        Entity replacementSite = siteQuery.GetSingletonEntity();
        Assert.AreNotEqual(siteEntity, replacementSite);
        Assert.AreEqual(ConstructionSiteFlags.AwaitingItemClearance,
            _entityManager.GetComponentData<ConstructionSite>(replacementSite).Flags);
        Assert.AreEqual(ItemOwnership.WorldItem, _entityManager.GetComponentData<ItemOwnership>(mat1));
        Assert.AreEqual(0, _invariantValidationSystem.TotalViolationCount);
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

        // 4. Arrange / Act: 신규 Belt 자재의 도착 상태를 준비한 뒤 완공한다.
        PrepareDeliveredMaterial(siteEntity, ItemTypeEnum.Iron);
        RunSimulationTicks(1);

        // Assert: 완공된 Belt가 타일을 정상 점유
        buildingMap = _entityManager.CreateEntityQuery(typeof(BuildingSpatialIndex)).GetSingleton<BuildingSpatialIndex>().Map;
        Assert.IsTrue(buildingMap.ContainsKey(tilePos));
        Assert.AreEqual(BuildingTypeEnum.Belt, buildingMap[tilePos].Type);

        Assert.AreEqual(0, _invariantValidationSystem.TotalViolationCount);
        siteQuery.Dispose();
    }

}
