using System.Text.RegularExpressions;
using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Scenes;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// 역할·목적: 프리팹 DB 준비와 Simulation 실행 게이트에 대한 NUnit EditMode 회귀 검증.
/// 입력·검사: ECS DB/프리팹/로딩 상태로 Initialization/Simulation·Probe를 검사한다. 로딩 상태 fixture는 실제 SubScene 로딩/베이킹의 증거가 아니다.
/// 수명: EcsWorldTestFixture가 각 사례의 독립 World를 준비하고 종료 시 해제한다.
/// </summary>
public class PrefabDatabaseInitializationTests : EcsWorldTestFixture
{
    private PrefabDatabaseInitializationSystem _initialization;
    private GameSimulationGroup _simulation;
    private PrefabInitializationProbeSystem _probe;
    private Entity _buildings;
    private Entity _items;
    private Entity _resources;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        _initialization = _world.GetOrCreateSystemManaged<PrefabDatabaseInitializationSystem>();
        _simulation = _world.GetOrCreateSystemManaged<GameSimulationGroup>();
        _probe = _world.GetOrCreateSystemManaged<PrefabInitializationProbeSystem>();
        _simulation.AddSystemToUpdateList(_probe);
    }

    private void CreateValidDatabases()
    {
        // 유효 DB를 먼저 준비하고 사례마다 조건을 훼손해 시작 게이트의 실패 원인을 분리한다.
        _buildings = TestPrefabDatabaseFactory.CreateBuildings(_entityManager);
        _items = TestPrefabDatabaseFactory.CreateItems(_entityManager);
        _resources = TestPrefabDatabaseFactory.CreateResources(_entityManager);
        Entity config = _entityManager.CreateEntity(typeof(ResourceGenerationSettings));
        _entityManager.AddBuffer<ResourceGenerationConfigElement>(config).Add(
            new ResourceGenerationConfigElement(ItemTypeEnum.Iron_Ore, 1, 1, 1, 1, 1, 1));
        Entities.PrepareSimulationConfiguration();
    }

    private void AssertFailed()
    {
        LogAssert.Expect(LogType.Error, new Regex(".*PrefabDatabaseInitializationSystem.*Game simulation stopped.*"));
        _initialization.Update();
        _simulation.Update();
        Assert.AreEqual(0, _probe.UpdateCount);
        Assert.IsFalse(_initialization.Enabled);
        using var ready = _entityManager.CreateEntityQuery(typeof(PrefabDatabaseReady));
        using var error = _entityManager.CreateEntityQuery(typeof(SimulationFatalError));
        Assert.AreEqual(0, ready.CalculateEntityCount());
        Assert.AreEqual(1, error.CalculateEntityCount());
    }

    [Test]
    public void GameGroup_WaitsForValidation_ThenRuns()
    {
        CreateValidDatabases();
        _simulation.Update();
        Assert.AreEqual(0, _probe.UpdateCount);
        _initialization.Update();
        _simulation.Update();
        Assert.AreEqual(1, _probe.UpdateCount);
        Assert.IsFalse(_initialization.Enabled);
    }

    [Test]
    public void RequestedSceneLoading_DoesNotTreatAbsentDatabasesAsFailure()
    {
        Entity scene = _entityManager.CreateEntity(typeof(SceneReference), typeof(RequestSceneLoaded));
        _initialization.Update();
        _simulation.Update();
        Assert.AreEqual(0, _probe.UpdateCount);
        Assert.IsTrue(_initialization.Enabled);
        using var errors = _entityManager.CreateEntityQuery(typeof(SimulationFatalError));
        Assert.AreEqual(0, errors.CalculateEntityCount());
        // 대기 입력 제거 후 DB 게시에 따른 초기화만 검증한다. 실제 SubScene 로딩 검증은 아니다.
        _entityManager.DestroyEntity(scene);
        CreateValidDatabases();
        _initialization.Update();
        _simulation.Update();
        Assert.AreEqual(1, _probe.UpdateCount);
    }

    [Test]
    public void FailedSceneHeader_StopsWithoutStartingSimulation()
    {
        Entity scene = _entityManager.CreateEntity(typeof(SceneReference), typeof(RequestSceneLoaded));
        _entityManager.AddBuffer<ResolvedSectionEntity>(scene);
        AssertFailed();
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void MissingDatabase_StopsWithoutFallback(int domain)
    {
        CreateValidDatabases();
        _entityManager.DestroyEntity(domain == 0 ? _buildings : domain == 1 ? _items : _resources);
        AssertFailed();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingOrNullBuildingEntry_Stops(bool nullEntry)
    {
        CreateValidDatabases();
        var entries = _entityManager.GetBuffer<BuildingPrefabElement>(_buildings);
        if (nullEntry)
        {
            var entry = entries[0];
            entry.Prefab = Entity.Null;
            entries[0] = entry;
        }
        else
        {
            entries.RemoveAt(0);
        }
        AssertFailed();
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void MalformedPrefab_Stops(int defect)
    {
        CreateValidDatabases();
        Entity prefab = _entityManager.GetBuffer<ItemPrefabElement>(_items)[0].Prefab;
        if (defect == 0) _entityManager.DestroyEntity(prefab);
        if (defect == 1) _entityManager.RemoveComponent<LocalTransform>(prefab);
        if (defect == 2) _entityManager.SetComponentData(prefab, new ItemIdentity(ItemTypeEnum.None));
        AssertFailed();
    }

    [Test]
    public void DuplicateDatabase_Stops()
    {
        CreateValidDatabases();
        TestPrefabDatabaseFactory.CreateBuildings(_entityManager);
        AssertFailed();
    }

    [Test]
    public void DuplicateMapping_Stops()
    {
        CreateValidDatabases();
        var entries = _entityManager.GetBuffer<BuildingPrefabElement>(_buildings);
        entries.Add(entries[0]);
        AssertFailed();
    }

    [Test]
    public void MissingRequiredResource_Stops()
    {
        CreateValidDatabases();
        _entityManager.GetBuffer<ResourcePrefabElement>(_resources).RemoveAt(0);
        AssertFailed();
    }

    [Test]
    public void RuntimeFatalError_StopsFollowingTicks_EvenWithReadyTag()
    {
        CreateValidDatabases();
        _initialization.Update();
        _simulation.Update();
        _entityManager.CreateEntity(typeof(SimulationFatalError));
        _simulation.Update();
        Assert.AreEqual(1, _probe.UpdateCount);
    }
}

/// <summary>
/// 역할·목적: 테스트 GameSimulationGroup의 실행 여부를 UpdateCount로 관측한다.
/// DisableAutoCreation 상태에서 fixture가 등록하며 로컬 카운터만 증가시키고 제품 상태는 변경하지 않는다.
/// 시스템/카운터는 사례별 World에 한정하며 World Dispose 때 해제된다.
/// </summary>
[DisableAutoCreation]
public partial class PrefabInitializationProbeSystem : SystemBase
{
    public int UpdateCount;
    protected override void OnUpdate() => UpdateCount++;
}
