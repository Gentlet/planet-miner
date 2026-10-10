using System.Text.RegularExpressions;
using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// 역할·목적: 직접 건물 스폰 요청 소비와 런타임 초기화에 대한 NUnit EditMode 회귀 검증.
/// 입력·검사: 테스트가 만든 ECS 프리팹/설정/요청으로 컴포넌트·버퍼·기본 footprint·실패 소비를 검사한다. 런타임 UI/자재 운송 Producer는 실행하지 않는다.
/// 수명: EcsWorldTestFixture가 각 사례의 독립 World를 준비하고 종료 시 해제한다.
/// </summary>
public class Phase7BuildingLifecycleTests : EcsWorldTestFixture
{
    private SystemHandle _lifecycleSystem;
    private EndBuildingEntityCommandBufferSystem _ecbSystem;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        CreateGameplayPrefabDatabases();
        _lifecycleSystem = _world.GetOrCreateSystem<BuildingLifecycleApplySystem>();
        _ecbSystem = _world.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>();
    }

    private void RunLifecyclePhase()
    {
        Entities.PrepareBuildingConfiguration(BuildingTypeEnum.Belt, BuildingTypeEnum.Miner, BuildingTypeEnum.Crafter,
            BuildingTypeEnum.Storage, BuildingTypeEnum.MainFacility, BuildingTypeEnum.Splitter, BuildingTypeEnum.Merger);
        // 요청 소비/인스턴스화의 같은 ECB를 재생한 뒤 공통/종류별 초기 상태를 검사한다.
        Simulation.UpdateAndComplete(_lifecycleSystem);
        Simulation.Playback(_ecbSystem);
    }

    [Test]
    public void Test01_SpawnBuilding_Belt_Prefab_InitializesAllComponents()
    {
        // 1. 명시적인 테스트 프리팹 DB 환경: Belt 스폰 요청 발행
        var reqEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(reqEntity, new SpawnBuildingRequest(
            BuildingTypeEnum.Belt,
            new int2(5, 7),
            DirectionEnum.Right,
            default,
            new PlacementStamp(10, 1, 17)
        ));

        // 2. 시스템 실행 및 ECB Playback
        RunLifecyclePhase();

        // 3. 요청 엔티티 파괴 확인 (Consume-on-Apply)
        Assert.IsFalse(_entityManager.Exists(reqEntity), "스폰 요청 엔티티는 단일 프레임 내에 파괴되어야 함");

        // 4. 생성된 건물 엔티티 검증
        var query = _entityManager.CreateEntityQuery(typeof(BuildingType), typeof(GridPosition), typeof(BeltComponent));
        Assert.AreEqual(1, query.CalculateEntityCount(), "Belt 건물 엔티티가 1개 생성되어야 함");

        var building = query.GetSingletonEntity();
        Assert.AreEqual(BuildingTypeEnum.Belt, _entityManager.GetComponentData<BuildingType>(building).Type);
        Assert.AreEqual(new int2(1, 1), _entityManager.GetComponentData<BuildingFootprint>(building).Size);
        Assert.AreEqual(new int2(5, 7), _entityManager.GetComponentData<GridPosition>(building).Value);
        Assert.AreEqual(DirectionEnum.Right, _entityManager.GetComponentData<Direction>(building).dir);
        Assert.AreEqual(new PlacementStamp(10, 1, 17), _entityManager.GetComponentData<PlacementStamp>(building));
        Assert.AreEqual(2.0f, _entityManager.GetComponentData<BeltComponent>(building).Speed);
        Assert.AreEqual(new float3(5, 7, 0), _entityManager.GetComponentData<LocalTransform>(building).Position);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Test11_UnstampedSpawn_IsNewInstallation_AndExplicitZeroIsPreserved(bool rawRequests)
    {
        var firstRequest = new SpawnBuildingRequest(BuildingTypeEnum.Storage, new int2(5, 0));
        var laterRequest = new SpawnBuildingRequest(BuildingTypeEnum.Storage, new int2(-5, 0));
        if (rawRequests)
        {
            _entityManager.AddComponentData(_entityManager.CreateEntity(), firstRequest);
            _entityManager.AddComponentData(_entityManager.CreateEntity(), laterRequest);
        }
        else
        {
            BuildingPlacementRequestUtility.SubmitSpawn(_entityManager, firstRequest);
            BuildingPlacementRequestUtility.SubmitSpawn(_entityManager, laterRequest);
        }
        BuildingPlacementRequestUtility.SubmitSpawn(_entityManager,
            new SpawnBuildingRequest(BuildingTypeEnum.Belt, new int2(0, 5), stamp: new PlacementStamp(0, 0)));
        RunLifecyclePhase();

        Entity first = Entity.Null;
        Entity later = Entity.Null;
        using var query = _entityManager.CreateEntityQuery(typeof(BuildingType), typeof(GridPosition));
        using var buildings = query.ToEntityArray(Unity.Collections.Allocator.Temp);
        foreach (Entity building in buildings)
        {
            int2 position = _entityManager.GetComponentData<GridPosition>(building).Value;
            if (position.Equals(new int2(5, 0))) first = building;
            if (position.Equals(new int2(-5, 0))) later = building;
            if (position.Equals(new int2(0, 5)))
                Assert.AreEqual(new PlacementStamp(0, 0), _entityManager.GetComponentData<PlacementStamp>(building));
        }
        Assert.AreNotEqual(Entity.Null, first);
        Assert.AreNotEqual(Entity.Null, later);
        Assert.Greater(_entityManager.GetComponentData<PlacementStamp>(first).Tick, 0UL);
        Assert.Greater(_entityManager.GetComponentData<PlacementStamp>(first).ReceiptSequence, 0UL);
        Assert.AreEqual(!rawRequests, DroneSchedulingUtility.ComparePlacement(_entityManager, first, later) < 0,
            "SubmitSpawn은 실제 접수 순서, 원시 생략 요청의 동일 준비 묶음은 좌표 순서로 비교한다.");
    }

    [Test]
    public void Test02_SpawnBuilding_Miner_InitializesStateDecisionAndBuffers()
    {
        // 1. Miner 스폰 요청 발행
        var reqEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(reqEntity, new SpawnBuildingRequest(
            BuildingTypeEnum.Miner,
            new int2(2, 4),
            DirectionEnum.Up,
            default,
            new PlacementStamp(5, 0)
        ));

        RunLifecyclePhase();

        // 2. Miner 엔티티 검증
        var query = _entityManager.CreateEntityQuery(typeof(BuildingType), typeof(MinerState));
        Assert.AreEqual(1, query.CalculateEntityCount(), "Miner 건물 엔티티가 1개 생성되어야 함");

        var miner = query.GetSingletonEntity();
        Assert.AreEqual(BuildingTypeEnum.Miner, _entityManager.GetComponentData<BuildingType>(miner).Type);
        Assert.AreEqual(new int2(2, 2), _entityManager.GetComponentData<BuildingFootprint>(miner).Size);
        Assert.AreEqual(1.0f, _entityManager.GetComponentData<MinerState>(miner).MiningSpeed);
        Assert.AreEqual(0.0f, _entityManager.GetComponentData<MinerState>(miner).Progress);

        // 의사결정 컴포넌트 존재 및 기본 비활성화 확인
        Assert.IsTrue(_entityManager.HasComponent<MinerDecision>(miner));
        Assert.IsFalse(_entityManager.IsComponentEnabled<MinerDecision>(miner), "MinerDecision은 기본 비활성화 상태여야 함");

        Assert.IsTrue(_entityManager.HasComponent<BuildingItemOutputDecision>(miner));
        Assert.IsFalse(_entityManager.IsComponentEnabled<BuildingItemOutputDecision>(miner), "BuildingItemOutputDecision은 기본 비활성화 상태여야 함");

        // 생산 관련 버퍼 확인
        Assert.IsTrue(_entityManager.HasBuffer<ProductItemElement>(miner), "ProductItemElement 버퍼가 있어야 함");
        Assert.IsTrue(_entityManager.HasBuffer<ProductResult>(miner), "ProductResult 버퍼가 있어야 함");
    }

    [Test]
    public void Test03_SpawnBuilding_Crafter_InitializesStateDecisionAndBuffers()
    {
        // 1. Crafter 스폰 요청 발행
        var reqEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(reqEntity, new SpawnBuildingRequest(
            BuildingTypeEnum.Crafter,
            new int2(10, 10),
            DirectionEnum.Left
        ));

        RunLifecyclePhase();

        var query = _entityManager.CreateEntityQuery(typeof(BuildingType), typeof(CrafterState));
        Assert.AreEqual(1, query.CalculateEntityCount(), "Crafter 건물 엔티티가 1개 생성되어야 함");

        var crafter = query.GetSingletonEntity();
        Assert.AreEqual(BuildingTypeEnum.Crafter, _entityManager.GetComponentData<BuildingType>(crafter).Type);
        Assert.AreEqual(new int2(2, 2), _entityManager.GetComponentData<BuildingFootprint>(crafter).Size);
        Assert.AreEqual(0, _entityManager.GetComponentData<CrafterState>(crafter).SelectedRecipeId);
        Assert.AreEqual(CrafterStatusEnum.NoRecipe, _entityManager.GetComponentData<CrafterState>(crafter).Status);

        Assert.IsTrue(_entityManager.HasComponent<CrafterDecision>(crafter));
        Assert.IsFalse(_entityManager.IsComponentEnabled<CrafterDecision>(crafter));

        Assert.IsTrue(_entityManager.HasComponent<CrafterStateDecision>(crafter));
        Assert.IsFalse(_entityManager.IsComponentEnabled<CrafterStateDecision>(crafter));
        Assert.AreEqual(0, _entityManager.GetComponentData<Storage>(crafter).SlotCount);
        Assert.AreEqual(0, _entityManager.GetBuffer<BuildingInputSlotElement>(crafter).Length);
        var filter = _entityManager.GetComponentData<StorageFilter>(crafter);
        Assert.AreEqual(StorageFilterMode.Whitelist, filter.Mode);
        Assert.IsFalse(filter.IsItemAllowed(ItemTypeEnum.Iron_Ore));

        // 입력 보관 버퍼 및 출력 버퍼 모두 확인
        Assert.IsTrue(_entityManager.HasBuffer<StoredItemElement>(crafter), "재료 보관용 StoredItemElement 버퍼가 있어야 함");
        Assert.IsTrue(_entityManager.HasBuffer<ProductItemElement>(crafter), "완성품용 ProductItemElement 버퍼가 있어야 함");
        Assert.IsTrue(_entityManager.HasBuffer<ProductResult>(crafter), "생산 결과용 ProductResult 버퍼가 있어야 함");
        Assert.IsTrue(_entityManager.HasComponent<BuildingItemOutputDecision>(crafter));
    }

    [Test]
    public void Test04_SpawnBuilding_Storage_InitializesStorageAndFilter()
    {
        // 1. Storage 스폰 요청 발행
        var reqEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(reqEntity, new SpawnBuildingRequest(
            BuildingTypeEnum.Storage,
            new int2(0, 0)
        ));

        RunLifecyclePhase();

        var query = _entityManager.CreateEntityQuery(typeof(BuildingType), typeof(Storage));
        Assert.AreEqual(1, query.CalculateEntityCount(), "Storage 건물 엔티티가 1개 생성되어야 함");

        var storage = query.GetSingletonEntity();
        Assert.AreEqual(BuildingTypeEnum.Storage, _entityManager.GetComponentData<BuildingType>(storage).Type);
        Assert.AreEqual(new int2(1, 1), _entityManager.GetComponentData<BuildingFootprint>(storage).Size);
        Assert.AreEqual(20, _entityManager.GetComponentData<Storage>(storage).SlotCount, "기본 슬롯 수는 20이어야 함");
        Assert.IsTrue(_entityManager.HasBuffer<StoredItemElement>(storage));
        Assert.IsTrue(_entityManager.HasComponent<StorageFilter>(storage));
        Assert.IsTrue(_entityManager.HasComponent<BuildingItemOutputDecision>(storage));
    }

    [Test]
    public void Test05_SpawnBuilding_SplitterAndMerger_InitializesRoutingStates()
    {
        // 1. Splitter 및 Merger 스폰 요청 발행
        var reqSplitter = _entityManager.CreateEntity();
        _entityManager.AddComponentData(reqSplitter, new SpawnBuildingRequest(
            BuildingTypeEnum.Splitter,
            new int2(1, 1),
            DirectionEnum.Down
        ));

        var reqMerger = _entityManager.CreateEntity();
        _entityManager.AddComponentData(reqMerger, new SpawnBuildingRequest(
            BuildingTypeEnum.Merger,
            new int2(3, 3),
            DirectionEnum.Left
        ));

        RunLifecyclePhase();

        // 2. Splitter 검증
        var querySplitter = _entityManager.CreateEntityQuery(typeof(SplitterRoutingState));
        Assert.AreEqual(1, querySplitter.CalculateEntityCount());
        var splitter = querySplitter.GetSingletonEntity();
        Assert.AreEqual(DirectionEnum.Down, _entityManager.GetComponentData<SplitterRoutingState>(splitter).ForwardDirection);
        Assert.IsTrue(_entityManager.HasComponent<RoutingTransferDecision>(splitter));
        Assert.IsFalse(_entityManager.IsComponentEnabled<RoutingTransferDecision>(splitter));

        // 3. Merger 검증
        var queryMerger = _entityManager.CreateEntityQuery(typeof(MergerRoutingState));
        Assert.AreEqual(1, queryMerger.CalculateEntityCount());
        var merger = queryMerger.GetSingletonEntity();
        Assert.AreEqual(DirectionEnum.Left, _entityManager.GetComponentData<MergerRoutingState>(merger).ForwardDirection);
        Assert.IsTrue(_entityManager.HasComponent<RoutingTransferDecision>(merger));
        Assert.IsFalse(_entityManager.IsComponentEnabled<RoutingTransferDecision>(merger));
    }

    [Test]
    public void Test06_SpawnBuilding_WithBuildingConfig_ReflectsConfigValues()
    {
        // 1. BuildingConfig 싱글톤 엔티티 및 버퍼 설정
        var configEntity = _entityManager.CreateEntity(typeof(BuildingConfig));
        var configBuffer = _entityManager.AddBuffer<BuildingConfigElement>(configEntity);
        configBuffer.Add(new BuildingConfigElement(
            BuildingTypeEnum.Miner,
            speed: 3.5f,
            storageCapacity: 0,
            footprint: new int2(2, 2),
            isUnlocked: true
        ));
        configBuffer.Add(new BuildingConfigElement(
            BuildingTypeEnum.Storage,
            speed: 1.0f,
            storageCapacity: 64,
            footprint: new int2(1, 1),
            isUnlocked: true
        ));

        // 2. Miner 및 Storage 스폰 요청
        var reqMiner = _entityManager.CreateEntity();
        _entityManager.AddComponentData(reqMiner, new SpawnBuildingRequest(BuildingTypeEnum.Miner, new int2(0, 0)));

        var reqStorage = _entityManager.CreateEntity();
        _entityManager.AddComponentData(reqStorage, new SpawnBuildingRequest(BuildingTypeEnum.Storage, new int2(10, 10)));

        RunLifecyclePhase();

        // 3. 검증: Config의 속도와 용량이 정확히 주입되었는지 확인
        var queryMiner = _entityManager.CreateEntityQuery(typeof(MinerState));
        var miner = queryMiner.GetSingletonEntity();
        Assert.AreEqual(3.5f, _entityManager.GetComponentData<MinerState>(miner).MiningSpeed, 0.001f);

        var queryStorage = _entityManager.CreateEntityQuery(typeof(Storage));
        var storage = queryStorage.GetSingletonEntity();
        Assert.AreEqual(64, _entityManager.GetComponentData<Storage>(storage).SlotCount);
    }

    [Test]
    public void Test08_SpawnBuilding_WithPrefabDb_MissingPrefab_StrictFail_RejectsSpawning()
    {
        // 1. 프리팹 DB 엔티티가 활성화되어 있으나 Miner 프리팹은 등록되지 않은 상태
        var mockBeltPrefab = _entityManager.CreateEntity(typeof(Prefab), typeof(LocalTransform));
        TestPrefabDatabaseFactory.RemoveDatabase<BuildingPrefabDatabase>(_entityManager);
        var dbEntity = _entityManager.CreateEntity(typeof(BuildingPrefabDatabase));
        var dbBuffer = _entityManager.AddBuffer<BuildingPrefabElement>(dbEntity);
        dbBuffer.Add(new BuildingPrefabElement(BuildingTypeEnum.Belt, mockBeltPrefab, new int2(1, 1)));

        // 2. 등록되지 않은 Miner 스폰 요청
        var reqEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(reqEntity, new SpawnBuildingRequest(
            BuildingTypeEnum.Miner,
            new int2(5, 5)
        ));

        // 3. Strict Fail 에러 로그 기대
        LogAssert.Expect(LogType.Error, new Regex(".*Missing prefab for building type.*"));

        RunLifecyclePhase();

        // 4. 검증: 스폰이 엄격히 거부되어 건물이 생성되지 않음
        var query = _entityManager.CreateEntityQuery(typeof(BuildingType));
        Assert.AreEqual(0, query.CalculateEntityCount(), "미등록 프리팹 요청 시 건물이 생성되지 않아야 함 (Strict Fail)");
        Assert.IsFalse(_entityManager.Exists(reqEntity), "요청 엔티티는 소비되어 파괴되어야 함");
    }

    [Test]
    public void Test09_SpawnBuilding_FootprintRotation_PreservesBaseSize()
    {
        // 1. 가로 2, 세로 3인 비대칭 크기를 지정하여 90도 회전(Right) 스폰
        var reqEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(reqEntity, new SpawnBuildingRequest(
            BuildingTypeEnum.Crafter,
            new int2(0, 0),
            DirectionEnum.Right,
            new int2(2, 3)
        ));

        RunLifecyclePhase();

        var query = _entityManager.CreateEntityQuery(typeof(BuildingFootprint));
        var building = query.GetSingletonEntity();
        Assert.AreEqual(new int2(2, 3), _entityManager.GetComponentData<BuildingFootprint>(building).Size, "방향은 점유 조회 시 적용하고 컴포넌트에는 기본 크기 2x3을 유지해야 함");
    }

    [Test]
    public void Test10_SpawnBuilding_InvalidType_IgnoredAndConsumed()
    {
        // 1. 무효한 타입(None 및 ConstructionSite)으로 스폰 요청
        var reqNone = _entityManager.CreateEntity();
        _entityManager.AddComponentData(reqNone, new SpawnBuildingRequest(BuildingTypeEnum.None, int2.zero));

        var reqSite = _entityManager.CreateEntity();
        _entityManager.AddComponentData(reqSite, new SpawnBuildingRequest(BuildingTypeEnum.ConstructionSite, int2.zero));

        RunLifecyclePhase();

        // 2. 검증: 건물 생성 없이 요청만 소비
        var query = _entityManager.CreateEntityQuery(typeof(BuildingType));
        Assert.AreEqual(0, query.CalculateEntityCount());
        Assert.IsFalse(_entityManager.Exists(reqNone));
        Assert.IsFalse(_entityManager.Exists(reqSite));
    }
}
