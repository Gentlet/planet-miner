using System.Text.RegularExpressions;
using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// 기존 테스트에 없던 Spawn/완공 → 다음 틱 망 참가, 기존 망 유지·병합 공개, 무효 참조와 공유 맵 수명을 검증한다.
/// 제품의 공급/연료/생산/충전·실제 입력은 실행하지 않는다.
/// </summary>
public class Phase8PowerTopologyTests : EcsWorldTestFixture
{
    private GameSimulationGroup _simulation;
    private SystemHandle _topology;
    private SystemHandle _topologyApply;
    private SystemHandle _sync;
    private EndBuildingEntityCommandBufferSystem _endBuilding;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        CreateGameplayPrefabDatabases();
        Entities.PrepareSimulationConfiguration();
        Entities.PrepareBuildingConfiguration(BuildingTypeEnum.PowerPole, BuildingTypeEnum.MainFacility);
        _topology = _world.GetOrCreateSystem<PowerGridTopologyReservationSystem>();
        _topologyApply = _world.GetOrCreateSystem<PowerGridTopologyApplySystem>();
        _sync = _world.GetOrCreateSystem<PowerGridConnectionSyncSystem>();
        _endBuilding = _world.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>();

        _simulation = _world.GetOrCreateSystemManaged<GameSimulationGroup>();
        var command = _world.GetOrCreateSystemManaged<CommandGroup>();
        var building = _world.GetOrCreateSystemManaged<BuildingSimulationGroup>();
        var decision = _world.GetOrCreateSystemManaged<BuildingDecisionGroup>();
        var reservation = _world.GetOrCreateSystemManaged<BuildingReservationGroup>();
        var execution = _world.GetOrCreateSystemManaged<BuildingExecutionGroup>();
        var apply = _world.GetOrCreateSystemManaged<BuildingStateApplyGroup>();
        var commit = _world.GetOrCreateSystemManaged<SimulationCommitGroup>();
        var sync = _world.GetOrCreateSystemManaged<SynchronizationGroup>();
        _simulation.AddSystemToUpdateList(command);
        _simulation.AddSystemToUpdateList(building);
        _simulation.AddSystemToUpdateList(commit);
        _simulation.AddSystemToUpdateList(sync);
        command.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingDemolitionCommandSystem>());
        command.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<EndCommandEntityCommandBufferSystem>());
        building.AddSystemToUpdateList(decision);
        building.AddSystemToUpdateList(reservation);
        building.AddSystemToUpdateList(execution);
        building.AddSystemToUpdateList(apply);
        building.AddSystemToUpdateList(_endBuilding);
        reservation.AddSystemToUpdateList(_topology);
        apply.AddSystemToUpdateList(_topologyApply);
        apply.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingLifecycleApplySystem>());
        apply.AddSystemToUpdateList(_world.GetOrCreateSystem<ConstructionLifecycleApplySystem>());
        commit.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>());
        sync.AddSystemToUpdateList(_sync);
        command.SortSystems();
        reservation.SortSystems();
        apply.SortSystems();
        building.SortSystems();
        commit.SortSystems();
        sync.SortSystems();
        _simulation.SortSystems();
        Simulation.SetDeltaTime(0.1f);
    }

    [Test]
    public void SpawnAndCompletion_JoinNextTick_AndRepeatedTicksKeepOneGrid()
    {
        SubmitPole(new int2(0, 0), 3, BuildingTypeEnum.MainFacility);
        Entity site = _entityManager.CreateEntity(typeof(ConstructionSite), typeof(BuildingType),
            typeof(GridPosition), typeof(Direction), typeof(BuildingFootprint), typeof(PlacementStamp),
            typeof(ConstructionMaterialRequirementElement), typeof(StoredItemElement));
        _entityManager.SetComponentData(site, new ConstructionSite(BuildingTypeEnum.PowerPole, 1));
        _entityManager.SetComponentData(site, new BuildingType(BuildingTypeEnum.ConstructionSite, 0));
        _entityManager.SetComponentData(site, new GridPosition(new int2(8, 0)));
        _entityManager.SetComponentData(site, new BuildingFootprint(new int2(1, 1)));
        _entityManager.SetComponentData(site, new Direction(DirectionEnum.Up));
        _entityManager.SetComponentData(site, new PlacementStamp(4, 0));

        _simulation.Update();
        Entity main = PoleAt(new int2(0, 0));
        Entity pole = PoleAt(new int2(8, 0));
        Assert.IsFalse(_entityManager.Exists(site));
        Assert.AreEqual(Entity.Null, Connection(main).Grid);
        Assert.AreEqual(Entity.Null, Connection(pole).Grid);
        Assert.AreEqual(Entity.Null, Decision(pole));
        Assert.AreEqual(0, GridCount());
        Assert.AreEqual(0, PublishedIndex().Map.Count());

        _simulation.Update();
        Entity grid = Connection(main).Grid;
        Assert.AreNotEqual(Entity.Null, grid);
        Assert.AreNotEqual(main, grid);
        Assert.AreEqual(grid, Connection(pole).Grid);
        Assert.AreEqual(main, Decision(pole));
        Assert.AreEqual(grid, PublishedIndex().Map[pole].Grid);
        _simulation.Update();
        Assert.AreEqual(grid, Connection(main).Grid);
        Assert.AreEqual(1, GridCount(), "반복 계산이 중복 망을 생성하지 않아야 한다.");
    }

    [Test]
    public void Bridge_WithEarlierNewStamp_KeepsEarliestExistingGrid_AtEndBuilding()
    {
        SubmitPole(new int2(0, 0), 20);
        SubmitPole(new int2(16, 0), 10);
        _simulation.Update();
        _simulation.Update();
        Entity left = PoleAt(new int2(0, 0));
        Entity right = PoleAt(new int2(16, 0));
        Entity removedGrid = Connection(left).Grid;
        Entity retainedGrid = Connection(right).Grid;
        Assert.AreNotEqual(removedGrid, retainedGrid);

        SubmitPole(new int2(8, 0), 1);
        _simulation.Update();
        Entity bridge = PoleAt(new int2(8, 0));
        Simulation.UpdateAndComplete(_topology);
        Assert.AreEqual(right, Decision(bridge));
        Assert.AreEqual(Entity.Null, Connection(bridge).Grid, "ECB 기록만으로 공개되면 안 된다.");
        Assert.AreEqual(removedGrid, Connection(left).Grid);
        Assert.IsTrue(_entityManager.Exists(removedGrid));

        // Reservation은 결정만 작성한다. EndBuilding을 실행해도 망 변경 명령이 없어야 한다.
        Simulation.Playback(_endBuilding);
        Assert.IsTrue(_entityManager.Exists(removedGrid));
        Assert.AreEqual(Entity.Null, Connection(bridge).Grid);
        Simulation.UpdateAndComplete(_topologyApply);
        Assert.AreEqual(removedGrid, Connection(left).Grid, "Apply의 ECB 기록도 Playback 전에는 공개되지 않는다.");
        Simulation.Playback(_endBuilding);
        Assert.IsFalse(_entityManager.Exists(removedGrid));
        Assert.AreEqual(retainedGrid, Connection(left).Grid);
        Assert.AreEqual(retainedGrid, Connection(bridge).Grid);
        Assert.AreEqual(retainedGrid, Connection(right).Grid);
        Assert.AreEqual(1, GridCount());
        Simulation.UpdateAndComplete(_sync);
        Assert.AreEqual(retainedGrid, PublishedIndex().Map[left].Grid);
    }

    [Test]
    public void DifferentTier_UsesEitherConnectionRange_AndDoesNotMergeSupplyOverlap()
    {
        using (var query = _entityManager.CreateEntityQuery(typeof(BuildingConfig)))
        {
            var configs = _entityManager.GetBuffer<BuildingConfigElement>(query.GetSingletonEntity());
            configs.Add(new BuildingConfigElement(BuildingTypeEnum.PowerPole, 2, "Tier2", 1f, 0, true,
                footprint: new int2(1, 1), power: new BuildingPowerConfig(supplyRange: 20, connectionRange: 0)));
        }
        SubmitPole(new int2(0, 0), 1);
        SubmitPole(new int2(8, 0), 2, tier: 2);
        SubmitPole(new int2(9, 0), 3, tier: 2);
        _simulation.Update();
        _simulation.Update();
        Assert.AreEqual(Connection(PoleAt(new int2(0, 0))).Grid, Connection(PoleAt(new int2(8, 0))).Grid);
        Assert.AreNotEqual(Connection(PoleAt(new int2(0, 0))).Grid, Connection(PoleAt(new int2(9, 0))).Grid);
        Assert.AreEqual(2, GridCount());
    }

    [Test]
    public void InvalidPublishedGrid_ClearsPreparedDecisions_AndPublishesFatal()
    {
        SubmitPole(new int2(0, 0), 1);
        _simulation.Update();
        _simulation.Update();
        Entity pole = PoleAt(new int2(0, 0));
        Entity invalid = _entityManager.CreateEntity();
        _entityManager.SetComponentData(pole, new PowerGridConnection { PowerPole = pole, Grid = invalid });
        LogAssert.Expect(LogType.Error, new Regex("PowerGridTopologyReservationSystem.*Invalid pole"));
        Simulation.UpdateAndComplete(_topology);
        Assert.AreEqual(Entity.Null, Decision(pole));
        Assert.IsFalse(SimulationFailureUtility.HasFatalError(_entityManager));
        Simulation.Playback(_endBuilding);
        Assert.IsTrue(SimulationFailureUtility.HasFatalError(_entityManager));
        Assert.AreEqual(1, GridCount(), "무효 입력을 새 망 생성으로 보충하지 않아야 한다.");
    }

    [Test]
    public void PublishedConnectionChangedAfterReservation_RejectsApplyWithoutCreatingOrDeletingGrids()
    {
        SubmitPole(new int2(0, 0), 1);
        _simulation.Update();
        _simulation.Update();
        Entity pole = PoleAt(new int2(0, 0));
        Entity expectedGrid = Connection(pole).Grid;
        Simulation.UpdateAndComplete(_topology);

        Entity changedGrid = _entityManager.CreateEntity(typeof(PowerGrid));
        _entityManager.SetComponentData(pole, new PowerGridConnection { PowerPole = pole, Grid = changedGrid });
        LogAssert.Expect(LogType.Error, new Regex("PowerGridTopologyApplySystem.*changed before apply"));
        Simulation.UpdateAndComplete(_topologyApply);
        Simulation.Playback(_endBuilding);

        Assert.IsTrue(SimulationFailureUtility.HasFatalError(_entityManager));
        Assert.IsTrue(_entityManager.Exists(expectedGrid));
        Assert.IsTrue(_entityManager.Exists(changedGrid));
        Assert.AreEqual(changedGrid, Connection(pole).Grid);
        Assert.AreEqual(2, GridCount(), "무효화된 계획으로 망을 생성/삭제하거나 연결을 덮어쓰지 않아야 한다.");
    }

    [TestCase(0, false)]
    [TestCase(1, false)]
    [TestCase(1, true)]
    public void Demolition_RebuildsSurvivingSets_AndUpdatesParticipantReferences(int removedIndex, bool bridgeWasEarliest)
    {
        CreatePoleChain(bridgeWasEarliest);
        Entity left = PoleAt(new int2(0, 0));
        Entity bridge = PoleAt(new int2(8, 0));
        Entity right = PoleAt(new int2(16, 0));
        Entity originalGrid = Connection(left).Grid;
        Entity removed = removedIndex == 0 ? left : bridge;
        Entity removedClient = CreateConnectionParticipant(removed);
        Entity rightClient = CreateConnectionParticipant(right);
        Entity disabledClient = CreateConnectionParticipant(right, disabled: true);

        RequestDemolition(removed);
        _simulation.Update();

        Assert.IsFalse(_entityManager.Exists(removed));
        AssertDisconnected(removedClient);
        Assert.AreEqual(right, Connection(rightClient).PowerPole);
        Assert.AreEqual(Connection(right).Grid, Connection(rightClient).Grid);
        Assert.AreEqual(Connection(right).Grid, Connection(disabledClient).Grid);
        if (removedIndex == 0)
        {
            Assert.AreEqual(originalGrid, Connection(bridge).Grid);
            Assert.AreEqual(originalGrid, Connection(right).Grid);
            Assert.AreEqual(1, GridCount());
        }
        else
        {
            Entity retainedPole = bridgeWasEarliest ? right : left;
            Entity newGridPole = bridgeWasEarliest ? left : right;
            Assert.AreEqual(originalGrid, Connection(retainedPole).Grid);
            Assert.AreNotEqual(originalGrid, Connection(newGridPole).Grid);
            Assert.IsTrue(_entityManager.Exists(Connection(newGridPole).Grid));
            Assert.AreEqual(2, GridCount());
        }
        var index = PublishedIndex();
        Assert.IsFalse(index.Map.ContainsKey(removed));
        Assert.IsFalse(index.Map.ContainsKey(removedClient));
        Assert.AreEqual(Connection(right).Grid, index.Map[rightClient].Grid);
        Assert.IsFalse(SimulationFailureUtility.HasFatalError(_entityManager));
    }

    [Test]
    public void LastPole_DuplicateRemovalAndReinstallation_CleansAllRuntimeReferencesAndOrphanGrids()
    {
        SubmitPole(new int2(0, 0), 1);
        _simulation.Update();
        _simulation.Update();
        Entity pole = PoleAt(new int2(0, 0));
        Entity oldGrid = Connection(pole).Grid;
        Entity client = CreateConnectionParticipant(pole);
        Entity disabledClient = CreateConnectionParticipant(pole, disabled: true);
        Entity orphanGrid = _entityManager.CreateEntity(typeof(PowerGrid));

        RequestDemolition(pole);
        RequestDemolition(pole);
        _simulation.Update();

        Assert.IsFalse(_entityManager.Exists(pole));
        Assert.IsFalse(_entityManager.Exists(oldGrid));
        Assert.IsFalse(_entityManager.Exists(orphanGrid));
        AssertDisconnected(client);
        AssertDisconnected(disabledClient);
        Assert.AreEqual(0, GridCount());
        Assert.AreEqual(0, PublishedIndex().Map.Count());

        RequestDemolition(pole);
        _simulation.Update();
        SubmitPole(new int2(0, 0), 2);
        _simulation.Update();
        Entity replacement = PoleAt(new int2(0, 0));
        Assert.AreNotEqual(pole, replacement);
        Assert.AreEqual(Entity.Null, Connection(replacement).Grid);
        _simulation.Update();
        Assert.AreNotEqual(oldGrid, Connection(replacement).Grid);
        Assert.IsTrue(_entityManager.Exists(Connection(replacement).Grid));
        AssertDisconnected(client);
        Assert.AreEqual(1, GridCount());
        Assert.IsFalse(SimulationFailureUtility.HasFatalError(_entityManager));
    }

    [Test]
    public void DisabledBridge_ReleasesItsGridAndClients_ThenRejoinsWithoutRestoringOldMembership()
    {
        CreatePoleChain(false);
        Entity left = PoleAt(new int2(0, 0));
        Entity bridge = PoleAt(new int2(8, 0));
        Entity right = PoleAt(new int2(16, 0));
        Entity oldGrid = Connection(left).Grid;
        var stamp = _entityManager.GetComponentData<PlacementStamp>(bridge);
        Entity client = CreateConnectionParticipant(bridge);

        _entityManager.AddComponent<Disabled>(bridge);
        _simulation.Update();
        Assert.AreEqual(bridge, Connection(bridge).PowerPole);
        Assert.AreEqual(Entity.Null, Connection(bridge).Grid);
        Assert.AreEqual(Entity.Null, Decision(bridge));
        Assert.AreEqual(stamp, _entityManager.GetComponentData<PlacementStamp>(bridge));
        AssertDisconnected(client);
        Assert.AreEqual(oldGrid, Connection(left).Grid);
        Entity splitGrid = Connection(right).Grid;
        Assert.AreNotEqual(oldGrid, splitGrid);
        Assert.AreEqual(2, GridCount());

        _entityManager.RemoveComponent<Disabled>(bridge);
        _simulation.Update();
        Assert.AreEqual(oldGrid, Connection(bridge).Grid);
        Assert.AreEqual(oldGrid, Connection(right).Grid);
        Assert.IsFalse(_entityManager.Exists(splitGrid));
        AssertDisconnected(client);
        Assert.AreEqual(1, GridCount());
        Assert.IsFalse(SimulationFailureUtility.HasFatalError(_entityManager));
    }

    [Test]
    public void SimultaneousSplitAndMerge_AssignsOldGridOwnershipBeforeChoosingMergeSurvivors()
    {
        // 두 기존 망이 각각 분리되고 양쪽 조각끼리 새 전신주로 병합된다.
        SubmitPole(new int2(0, 0), 1);
        SubmitPole(new int2(8, 0), 50);
        SubmitPole(new int2(16, 0), 3);
        SubmitPole(new int2(0, 16), 2);
        SubmitPole(new int2(8, 16), 60);
        SubmitPole(new int2(16, 16), 4);
        _simulation.Update();
        _simulation.Update();
        Entity lowerLeft = PoleAt(new int2(0, 0));
        Entity upperLeft = PoleAt(new int2(0, 16));
        Entity lowerRight = PoleAt(new int2(16, 0));
        Entity upperRight = PoleAt(new int2(16, 16));
        Entity lowerGrid = Connection(lowerLeft).Grid;
        Entity upperGrid = Connection(upperLeft).Grid;
        Assert.AreNotEqual(lowerGrid, upperGrid);
        Entity upperClient = CreateConnectionParticipant(upperLeft);
        Entity rightClient = CreateConnectionParticipant(upperRight);

        SubmitPole(new int2(0, 8), 100);
        SubmitPole(new int2(16, 8), 101);
        _simulation.Update();
        RequestDemolition(PoleAt(new int2(8, 0)));
        RequestDemolition(PoleAt(new int2(8, 16)));
        _simulation.Update();

        Assert.AreEqual(lowerGrid, Connection(lowerLeft).Grid);
        Assert.AreEqual(lowerGrid, Connection(upperLeft).Grid);
        Entity newRightGrid = Connection(lowerRight).Grid;
        Assert.AreNotEqual(lowerGrid, newRightGrid);
        Assert.AreNotEqual(upperGrid, newRightGrid);
        Assert.AreEqual(newRightGrid, Connection(upperRight).Grid);
        Assert.IsFalse(_entityManager.Exists(upperGrid));
        Assert.AreEqual(lowerGrid, Connection(upperClient).Grid);
        Assert.AreEqual(newRightGrid, Connection(rightClient).Grid);
        Assert.AreEqual(2, GridCount());
        Assert.IsFalse(SimulationFailureUtility.HasFatalError(_entityManager));
    }

    [Test]
    public void ReservationFailure_DoesNotDeleteExistingGridsOrClearClientReferences()
    {
        SubmitPole(new int2(0, 0), 1);
        _simulation.Update();
        _simulation.Update();
        Entity pole = PoleAt(new int2(0, 0));
        Entity originalGrid = Connection(pole).Grid;
        Entity client = CreateConnectionParticipant(pole);
        Entity invalidGrid = _entityManager.CreateEntity();
        _entityManager.SetComponentData(pole, new PowerGridConnection { PowerPole = pole, Grid = invalidGrid });

        LogAssert.Expect(LogType.Error, new Regex("PowerGridTopologyReservationSystem.*Invalid pole"));
        _simulation.Update();

        Assert.IsTrue(SimulationFailureUtility.HasFatalError(_entityManager));
        Assert.IsTrue(_entityManager.Exists(originalGrid));
        Assert.AreEqual(originalGrid, Connection(client).Grid);
        Assert.AreEqual(pole, Connection(client).PowerPole);
        Assert.AreEqual(1, GridCount());
    }

    private void CreatePoleChain(bool bridgeWasEarliest)
    {
        SubmitPole(new int2(0, 0), bridgeWasEarliest ? 20UL : 10UL);
        SubmitPole(new int2(8, 0), bridgeWasEarliest ? 1UL : 20UL);
        SubmitPole(new int2(16, 0), bridgeWasEarliest ? 10UL : 30UL);
        _simulation.Update();
        _simulation.Update();
        Assert.AreEqual(1, GridCount());
    }

    private Entity CreateConnectionParticipant(Entity pole, bool disabled = false)
    {
        // 일반 참가자의 등록 Producer는 8.4/8.7 후속이다. 여기서는 참조 정리 계약의 입력만 주입한다.
        Entity client = _entityManager.CreateEntity(typeof(PowerConsumer), typeof(PowerGridConnection));
        _entityManager.SetComponentData(client, Connection(pole));
        if (disabled)
        {
            _entityManager.AddComponent<Disabled>(client);
        }
        return client;
    }

    private void RequestDemolition(Entity pole)
    {
        Entity request = _entityManager.CreateEntity(typeof(DemolishBuildingRequest));
        _entityManager.SetComponentData(request, new DemolishBuildingRequest(pole));
    }

    private void AssertDisconnected(Entity entity)
    {
        Assert.AreEqual(Entity.Null, Connection(entity).PowerPole);
        Assert.AreEqual(Entity.Null, Connection(entity).Grid);
    }

    [Test]
    public void PublishedIndex_WaitsForReaderBeforeResize_AndClearsAfterLastConnection()
    {
        SubmitPole(new int2(0, 0), 1);
        _simulation.Update();
        _simulation.Update();
        Entity pole = PoleAt(new int2(0, 0));
        var index = PublishedIndex();
        using var fenceQuery = _entityManager.CreateEntityQuery(typeof(PowerGridConnectionIndexFence));
        Entity indexEntity = fenceQuery.GetSingletonEntity();
        var fence = _entityManager.GetComponentData<PowerGridConnectionIndexFence>(indexEntity);
        using var observed = new NativeArray<int>(1, Allocator.TempJob);
        var reader = new ReadConnectionIndexJob { Map = index.AsReadOnly(), Observed = observed }
            .Schedule(fence.GetReaderDependency());
        fence.AddReader(reader);
        _entityManager.SetComponentData(indexEntity, fence);
        using var extra = new NativeArray<Entity>(1025, Allocator.Temp);
        _entityManager.CreateEntity(_entityManager.CreateArchetype(typeof(PowerGridConnection)), extra);
        foreach (var entity in extra)
        {
            _entityManager.SetComponentData(entity, Connection(pole));
        }
        Simulation.UpdateAndComplete(_sync);
        reader.Complete();
        Assert.AreEqual(1, observed[0]);
        Assert.AreEqual(1026, PublishedIndex().Map.Count());
        _entityManager.DestroyEntity(extra);
        _entityManager.RemoveComponent<PowerGridConnection>(pole);
        Simulation.UpdateAndComplete(_sync);
        Assert.AreEqual(0, PublishedIndex().Map.Count());
    }

    private void SubmitPole(int2 position, ulong tick, BuildingTypeEnum type = BuildingTypeEnum.PowerPole, int tier = 1)
    {
        BuildingPlacementRequestUtility.SubmitSpawn(_entityManager,
            new SpawnBuildingRequest(type, tier, position, stamp: new PlacementStamp(tick, 0)));
        if (tier == 2)
        {
            using var query = _entityManager.CreateEntityQuery(typeof(BuildingPrefabElement));
            var prefabs = _entityManager.GetBuffer<BuildingPrefabElement>(query.GetSingletonEntity());
            if (!PrefabLookupUtility.TryGetBuildingPrefab(prefabs, type, tier, out _))
            {
                Assert.IsTrue(PrefabLookupUtility.TryGetBuildingPrefab(prefabs, type, 1, out var prefab));
                prefabs.Add(new BuildingPrefabElement(type, tier, prefab));
            }
        }
    }

    private Entity PoleAt(int2 position)
    {
        using var query = _entityManager.CreateEntityQuery(typeof(PowerPole), typeof(GridPosition));
        using var poles = query.ToEntityArray(Allocator.Temp);
        foreach (var pole in poles)
        {
            if (math.all(_entityManager.GetComponentData<GridPosition>(pole).Value == position)) return pole;
        }
        Assert.Fail($"Missing pole at {position}");
        return Entity.Null;
    }

    private PowerGridConnection Connection(Entity entity) => _entityManager.GetComponentData<PowerGridConnection>(entity);
    private Entity Decision(Entity entity) => _entityManager.GetComponentData<PowerGridTopologyDecision>(entity).RepresentativePole;
    private int GridCount()
    {
        using var query = _entityManager.CreateEntityQuery(typeof(PowerGrid));
        return query.CalculateEntityCount();
    }

    private PowerGridConnectionIndex PublishedIndex()
    {
        using var query = _entityManager.CreateEntityQuery(typeof(PowerGridConnectionIndex));
        using var fenceQuery = _entityManager.CreateEntityQuery(typeof(PowerGridConnectionIndexFence));
        _entityManager.GetComponentData<PowerGridConnectionIndexFence>(fenceQuery.GetSingletonEntity())
            .GetReaderDependency().Complete();
        return query.GetSingleton<PowerGridConnectionIndex>();
    }

    private struct ReadConnectionIndexJob : IJob
    {
        [ReadOnly] public NativeParallelHashMap<Entity, PowerGridConnection>.ReadOnly Map;
        public NativeArray<int> Observed;
        public void Execute() => Observed[0] = Map.Count();
    }
}
