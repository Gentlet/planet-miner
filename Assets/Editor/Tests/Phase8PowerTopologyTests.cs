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
/// Spawn/완공 → 다음 틱 망 참가, 토폴로지·회전 footprint 공급 선택·재연결과 공유 맵 수명을 검증한다.
/// 공급 예산·석탄 전환·생산 진행과 실패 시 도메인 중단을 검증한다. 충전·게임 입력은 후속 범위다.
/// </summary>
public class Phase8PowerTopologyTests : EcsWorldTestFixture
{
    private GameSimulationGroup _simulation;
    private SystemHandle _topology;
    private SystemHandle _connectionSelection;
    private SystemHandle _topologyApply;
    private SystemHandle _sync;
    private EndBuildingEntityCommandBufferSystem _endBuilding;
    private EndPowerEntityCommandBufferSystem _endPower;
    private SystemHandle _frameReset;
    private SystemHandle _demand;
    private SystemHandle _availability;
    private SystemHandle _budget;
    private SystemHandle _supplyExecution;
    private SystemHandle _minerDecision;
    private SystemHandle _crafterDecision;

    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        CreateGameplayPrefabDatabases();
        Entities.PrepareSimulationConfiguration();
        Entities.PrepareBuildingConfiguration(BuildingTypeEnum.PowerPole, BuildingTypeEnum.MainFacility,
            BuildingTypeEnum.Miner, BuildingTypeEnum.Crafter, BuildingTypeEnum.ResearchBuilding,
            BuildingTypeEnum.CoalGenerator, BuildingTypeEnum.DroneStation);
        _topology = _world.GetOrCreateSystem<PowerGridTopologyReservationSystem>();
        _connectionSelection = _world.GetOrCreateSystem<PowerGridConnectionDecisionSystem>();
        _topologyApply = _world.GetOrCreateSystem<PowerGridTopologyApplySystem>();
        _sync = _world.GetOrCreateSystem<PowerGridConnectionSyncSystem>();
        _endBuilding = _world.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>();
        _endPower = _world.GetOrCreateSystemManaged<EndPowerEntityCommandBufferSystem>();
        _frameReset = _world.GetOrCreateSystem<PowerSupplyResetPreparationSystem>();

        _simulation = _world.GetOrCreateSystemManaged<GameSimulationGroup>();
        var command = _world.GetOrCreateSystemManaged<CommandGroup>();
        var preparation = _world.GetOrCreateSystemManaged<SimulationPreparationGroup>();
        var power = _world.GetOrCreateSystemManaged<PowerSimulationGroup>();
        var powerDecision = _world.GetOrCreateSystemManaged<PowerDecisionGroup>();
        var powerReservation = _world.GetOrCreateSystemManaged<PowerReservationGroup>();
        var powerExecution = _world.GetOrCreateSystemManaged<PowerExecutionGroup>();
        var powerApply = _world.GetOrCreateSystemManaged<PowerStateApplyGroup>();
        var building = _world.GetOrCreateSystemManaged<BuildingSimulationGroup>();
        var decision = _world.GetOrCreateSystemManaged<BuildingDecisionGroup>();
        var reservation = _world.GetOrCreateSystemManaged<BuildingReservationGroup>();
        var execution = _world.GetOrCreateSystemManaged<BuildingExecutionGroup>();
        var apply = _world.GetOrCreateSystemManaged<BuildingStateApplyGroup>();
        apply.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<ConfirmedBuildingDemolitionTestSystem>());
        var commit = _world.GetOrCreateSystemManaged<SimulationCommitGroup>();
        var sync = _world.GetOrCreateSystemManaged<SynchronizationGroup>();
        _simulation.AddSystemToUpdateList(command);
        _simulation.AddSystemToUpdateList(preparation);
        _simulation.AddSystemToUpdateList(power);
        _simulation.AddSystemToUpdateList(building);
        preparation.AddSystemToUpdateList(_frameReset);
        power.AddSystemToUpdateList(powerDecision);
        power.AddSystemToUpdateList(powerReservation);
        power.AddSystemToUpdateList(powerExecution);
        power.AddSystemToUpdateList(powerApply);
        power.AddSystemToUpdateList(_endPower);
        _simulation.AddSystemToUpdateList(commit);
        _simulation.AddSystemToUpdateList(sync);
        command.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingDemolitionCommandSystem>());
        command.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<EndCommandEntityCommandBufferSystem>());
        building.AddSystemToUpdateList(decision);
        building.AddSystemToUpdateList(reservation);
        building.AddSystemToUpdateList(execution);
        building.AddSystemToUpdateList(apply);
        building.AddSystemToUpdateList(_endBuilding);
        powerReservation.AddSystemToUpdateList(_topology);
        powerDecision.AddSystemToUpdateList(_connectionSelection);
        powerApply.AddSystemToUpdateList(_topologyApply);
        apply.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingLifecycleApplySystem>());
        apply.AddSystemToUpdateList(_world.GetOrCreateSystem<ConstructionLifecycleApplySystem>());
        commit.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>());
        sync.AddSystemToUpdateList(_sync);
        preparation.SortSystems();
        powerDecision.SortSystems();
        powerReservation.SortSystems();
        powerExecution.SortSystems();
        powerApply.SortSystems();
        power.SortSystems();
        command.SortSystems();
        decision.SortSystems();
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
        Simulation.Playback(_endPower);
        Assert.IsTrue(_entityManager.Exists(removedGrid));
        Assert.AreEqual(Entity.Null, Connection(bridge).Grid);
        Simulation.UpdateAndComplete(_topologyApply);
        Assert.AreEqual(removedGrid, Connection(left).Grid, "Apply의 ECB 기록도 Playback 전에는 공개되지 않는다.");
        Simulation.Playback(_endPower);
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
        Simulation.Playback(_endPower);
        Assert.IsTrue(SimulationFailureUtility.HasFatalError(_entityManager));
        Assert.AreEqual(1, GridCount(), "무효 입력을 새 망 생성으로 보충하지 않아야 한다.");
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
        SubmitParticipant(BuildingTypeEnum.Miner, new int2(2, 0));
        _simulation.Update();
        _simulation.Update();
        Entity pole = PoleAt(new int2(0, 0));
        Entity originalGrid = Connection(pole).Grid;
        Entity selectedClient = ParticipantAt(new int2(2, 0));
        Entity client = CreateConnectionParticipant(pole);
        Entity invalidGrid = _entityManager.CreateEntity();
        _entityManager.SetComponentData(pole, new PowerGridConnection { PowerPole = pole, Grid = invalidGrid });

        LogAssert.Expect(LogType.Error, new Regex("PowerGridTopologyReservationSystem.*Invalid pole"));
        _simulation.Update();

        Assert.IsTrue(SimulationFailureUtility.HasFatalError(_entityManager));
        Assert.IsTrue(_entityManager.Exists(originalGrid));
        Assert.AreEqual(originalGrid, Connection(client).Grid);
        Assert.AreEqual(pole, Connection(client).PowerPole);
        Assert.IsTrue(_entityManager.IsComponentEnabled<PowerGridConnectionDecision>(selectedClient),
            "선택은 토폴로지 준비 전에 완료되지만 후속 토폴로지 실패 시 공개하지 않아야 한다.");
        Assert.AreEqual(originalGrid, Connection(selectedClient).Grid);
        Assert.AreEqual(pole, Connection(selectedClient).PowerPole);
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

    [TestCase(BuildingTypeEnum.Miner, 1, 1, DirectionEnum.Up)]
    [TestCase(BuildingTypeEnum.Crafter, 2, 2, DirectionEnum.Up)]
    [TestCase(BuildingTypeEnum.CoalGenerator, 3, 3, DirectionEnum.Up)]
    [TestCase(BuildingTypeEnum.ResearchBuilding, 2, 3, DirectionEnum.Right)]
    [TestCase(BuildingTypeEnum.DroneStation, 3, 2, DirectionEnum.Left)]
    public void ParticipantSpawnAndCompletion_SelectEntireRotatedFootprintAtBoundary_NextTick(
        BuildingTypeEnum type, int width, int height, DirectionEnum direction)
    {
        int2 size = new int2(width, height);
        SetFootprint(type, size);
        int2 effectiveSize = BuildingFootprintUtility.GetEffectiveSize(size, direction);
        int2 position = new int2(-5 - effectiveSize.x + 1, -3);
        int2 completedPosition = new int2(position.x, 3);
        int2 outsidePosition = new int2(position.x - 1, 0);
        SubmitPole(int2.zero, 1);
        SubmitParticipant(type, position, direction);
        SubmitParticipant(type, outsidePosition, direction);
        Entity site = CreateCompletionSite(type, completedPosition, size, direction);

        _simulation.Update();
        Entity pole = PoleAt(int2.zero);
        Entity participant = ParticipantAt(position);
        Entity completed = ParticipantAt(completedPosition);
        Entity outside = ParticipantAt(outsidePosition);
        Assert.IsFalse(_entityManager.Exists(site));
        AssertDisconnected(participant);
        AssertDisconnected(completed);
        Assert.IsFalse(_entityManager.IsComponentEnabled<PowerGridConnectionDecision>(participant));

        _simulation.Update();
        Assert.AreEqual(pole, Connection(participant).PowerPole);
        Assert.AreEqual(pole, Connection(completed).PowerPole);
        Assert.AreEqual(Connection(pole).Grid, Connection(participant).Grid);
        Assert.AreEqual(Connection(pole).Grid, Connection(completed).Grid);
        Assert.AreEqual(Connection(pole).Grid, PublishedIndex().Map[participant].Grid);
        AssertDisconnected(outside);
        Assert.IsFalse(PublishedIndex().Map.ContainsKey(outside));
        if (effectiveSize.x > 1)
        {
            Assert.IsFalse(PowerRangeUtility.Contains(int2.zero, 5, position),
                "기준 좌표가 범위 밖이어도 마지막 점유 셀이 경계에 닿으면 연결되어야 한다.");
        }
    }

    [Test]
    public void OverlappingSupply_SelectsNearestFootprintCell_UsesStampTie_AndSwitchesValidConnection()
    {
        SetFootprint(BuildingTypeEnum.Crafter, new int2(5, 1));
        SetFootprint(BuildingTypeEnum.ResearchBuilding, new int2(3, 1));
        SubmitPole(int2.zero, 20);
        SubmitPole(new int2(10, 0), 10);
        SubmitParticipant(BuildingTypeEnum.Crafter, new int2(4, 0));
        SubmitParticipant(BuildingTypeEnum.ResearchBuilding, new int2(4, 2));
        _simulation.Update();
        _simulation.Update();
        Entity left = PoleAt(int2.zero);
        Entity right = PoleAt(new int2(10, 0));
        Entity crafter = ParticipantAt(new int2(4, 0));
        Entity research = ParticipantAt(new int2(4, 2));
        Assert.AreNotEqual(Connection(left).Grid, Connection(right).Grid);
        Assert.AreEqual(right, Connection(crafter).PowerPole, "anchor가 아닌 전체 점유 셀의 최소 거리로 선택한다.");
        Assert.AreEqual(right, Connection(research).PowerPole, "거리 동률은 Entity 생성 순서 대신 설치 기록으로 비교한다.");

        SubmitPole(new int2(6, 0), 30);
        _simulation.Update();
        Entity closer = PoleAt(new int2(6, 0));
        Assert.AreEqual(right, Connection(crafter).PowerPole, "EndBuilding에 생성된 전신주는 다음 틱부터 선택한다.");
        Simulation.UpdateAndComplete(_connectionSelection);
        Assert.AreEqual(Entity.Null, Decision(closer), "새 전신주의 토폴로지가 아직 준비되지 않은 상태다.");
        Assert.AreEqual(closer, _entityManager.GetComponentData<PowerGridConnectionDecision>(crafter).PowerPole);
        Assert.AreEqual(right, Connection(crafter).PowerPole);
        Simulation.UpdateAndComplete(_topology);
        Simulation.UpdateAndComplete(_topologyApply);
        Assert.AreEqual(right, Connection(crafter).PowerPole, "Apply 기록과 실제 공개를 구분한다.");
        Simulation.Playback(_endPower);
        Assert.AreEqual(closer, Connection(crafter).PowerPole);
        Assert.AreEqual(closer, Connection(research).PowerPole);
        Assert.AreEqual(Connection(closer).Grid, Connection(crafter).Grid);
        Assert.AreEqual(1, GridCount());
    }

    [Test]
    public void ParticipantDisabled_DoesNotSelectCloserPole_ThenReactivationAndDemolitionReconnect()
    {
        SetFootprint(BuildingTypeEnum.Miner, new int2(1, 1));
        SubmitPole(int2.zero, 1);
        SubmitPole(new int2(10, 0), 2);
        SubmitParticipant(BuildingTypeEnum.Miner, new int2(5, 0));
        _simulation.Update();
        _simulation.Update();
        Entity left = PoleAt(int2.zero);
        Entity right = PoleAt(new int2(10, 0));
        Entity participant = ParticipantAt(new int2(5, 0));
        Assert.AreEqual(left, Connection(participant).PowerPole);
        _entityManager.AddComponent<Disabled>(participant);
        SubmitPole(new int2(6, 0), 3);
        _simulation.Update();
        _simulation.Update();
        Entity closer = PoleAt(new int2(6, 0));
        Assert.AreEqual(left, Connection(participant).PowerPole);
        Assert.IsFalse(_entityManager.IsComponentEnabled<PowerGridConnectionDecision>(participant));
        RequestDemolition(left);
        _simulation.Update();
        AssertDisconnected(participant);
        Assert.IsFalse(_entityManager.Exists(left));

        _entityManager.RemoveComponent<Disabled>(participant);
        _simulation.Update();
        Assert.AreEqual(closer, Connection(participant).PowerPole);
        RequestDemolition(closer);
        _simulation.Update();
        Assert.AreEqual(right, Connection(participant).PowerPole, "철거 승인 틱에 남은 범위 후보로 재연결한다.");
        Assert.AreEqual(Connection(right).Grid, PublishedIndex().Map[participant].Grid);
        RequestDemolition(right);
        _simulation.Update();
        AssertDisconnected(participant);
        Assert.IsFalse(PublishedIndex().Map.ContainsKey(participant));
        Assert.AreEqual(0, GridCount());
    }

    [Test]
    public void InvalidPoleInput_TopologyStopsPublicationAndPreservesPublishedConnection()
    {
        SubmitPole(int2.zero, 1);
        SubmitParticipant(BuildingTypeEnum.Miner, new int2(2, 0));
        _simulation.Update();
        _simulation.Update();
        Entity pole = PoleAt(int2.zero);
        Entity participant = ParticipantAt(new int2(2, 0));
        Entity grid = Connection(pole).Grid;
        _entityManager.RemoveComponent<GridPosition>(pole);

        LogAssert.Expect(LogType.Error, new Regex("PowerGridTopologyReservationSystem.*Invalid pole"));
        _simulation.Update();
        Simulation.Playback(_endPower);

        Assert.IsTrue(SimulationFailureUtility.HasFatalError(_entityManager));
        Assert.IsTrue(_entityManager.Exists(grid));
        Assert.AreEqual(pole, Connection(participant).PowerPole);
        Assert.AreEqual(grid, Connection(participant).Grid);
        Assert.AreEqual(1, GridCount());
    }

    [Test]
    public void PowerBudget_RealWorkDemand_BirthAndClampedTime_PublishesOnlyAtExecution()
    {
        EnablePowerSystems();
        Simulation.SetDeltaTime(10f);
        Entities.CreateResourceNode(new int2(3, 2), ItemTypeEnum.Iron_Ore, 50);
        SubmitPole(int2.zero, 1, BuildingTypeEnum.MainFacility);
        SubmitPole(new int2(4, 0), 2);
        SubmitParticipant(BuildingTypeEnum.Miner, new int2(3, 2));
        SubmitParticipant(BuildingTypeEnum.Crafter, new int2(4, 2));
        _simulation.Update();
        Entity main = PoleAt(int2.zero);
        Entity miner = ParticipantAt(new int2(3, 2));
        Entity crafter = ParticipantAt(new int2(4, 2));
        Assert.AreEqual(0f, Supply(miner).AllocatedEnergy);
        Assert.IsFalse(_entityManager.IsComponentEnabled<PowerDemand>(miner));
        var craftState = new CrafterState(1) { IsCraftingActive = true, Progress = 0.5f, Status = CrafterStatusEnum.Crafting };
        _entityManager.SetComponentData(crafter, craftState);

        PreparePowerInputs();
        Assert.IsTrue(_entityManager.GetComponentData<MinerWorkPreparation>(miner).CanMine);
        Assert.IsTrue(_entityManager.GetComponentData<CrafterWorkPreparation>(crafter).CanAdvance);
        Assert.AreEqual(1f, Demand(miner));
        Assert.AreEqual(2f, Demand(crafter));
        ReservePower();
        Assert.AreEqual(Entity.Null, Connection(main).Grid, "새 망의 공개 Entity 없이 이번 틱 대표로 배분한다.");
        Assert.AreEqual(4f, Generation(main).AvailableEnergy);
        Assert.AreEqual(3f, Generation(main).AllocatedEnergy);
        Assert.AreEqual(0f, Supply(miner).AllocatedEnergy);
        Assert.AreEqual(1f, Reservation(miner).AllocatedEnergy);
        Simulation.UpdateAndComplete(_supplyExecution);
        Assert.AreEqual(1f, Supply(miner).AllocatedEnergy);
        Assert.AreEqual(2f, Supply(crafter).AllocatedEnergy);
        Assert.AreEqual(1f, Supply(miner).SupplyRatio);
        Assert.AreEqual(0f, _entityManager.GetComponentData<MinerState>(miner).Progress);
        Assert.AreEqual(0.5f, _entityManager.GetComponentData<CrafterState>(crafter).Progress);

        // 진행 완료/출력 확정 대기는 수요 0이다. 출력 자격 때문에 정격 수요를 만들지 않는다.
        craftState.Progress = 1f;
        _entityManager.SetComponentData(crafter, craftState);
        _entityManager.SetComponentData(crafter, new CrafterDecision(false, 1));
        _entityManager.SetComponentEnabled<CrafterDecision>(crafter, false);
        PreparePowerInputs();
        Assert.IsFalse(_entityManager.GetComponentData<CrafterDecision>(crafter).CanProduceOutput,
            "Preparation은 실행/출력 결정을 작성하지 않는다.");
        ReservePower();
        Simulation.UpdateAndComplete(_supplyExecution);
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<CrafterDecisionSystem>());
        Assert.IsTrue(_entityManager.GetComponentData<CrafterDecision>(crafter).CanProduceOutput);
        Assert.AreEqual(0f, Demand(crafter));
        Assert.AreEqual(0f, Supply(crafter).AllocatedEnergy);
        Assert.AreEqual(1f, Generation(main).AllocatedEnergy);
    }

    [TestCase(0f, 1f, 2f, 0f)]
    [TestCase(1.5f, 1f, 2f, 0.5f)]
    [TestCase(10f, 1f, 2f, 1f)]
    [TestCase(0f, 0f, 0f, 1f)]
    public void PowerBudget_SharedRatio_UsesRequestedEnergy_AndConsumesAllocatedFuel(
        float fuel, float firstRequest, float secondRequest, float ratio)
    {
        EnablePowerSystems();
        SubmitPole(int2.zero, 1);
        SubmitParticipant(BuildingTypeEnum.CoalGenerator, new int2(1, 0));
        SubmitParticipant(BuildingTypeEnum.DroneStation, new int2(2, 0));
        SubmitParticipant(BuildingTypeEnum.ResearchBuilding, new int2(3, 0));
        _simulation.Update();
        Entity generator = ParticipantAt(new int2(1, 0));
        Entity first = ParticipantAt(new int2(2, 0));
        Entity second = ParticipantAt(new int2(3, 0));
        _entityManager.SetComponentData(generator, new FuelEnergyState { RemainingEnergy = fuel });
        PreparePowerInputs();
        SetDemand(first, firstRequest);
        SetDemand(second, secondRequest);
        ReservePower();
        Simulation.UpdateAndComplete(_supplyExecution);
        Assert.AreEqual(ratio, Supply(first).SupplyRatio, 0.000001f);
        Assert.AreEqual(ratio, Supply(second).SupplyRatio, 0.000001f);
        Assert.AreEqual(firstRequest * ratio, Supply(first).AllocatedEnergy, 0.000001f);
        Assert.AreEqual(secondRequest * ratio, Supply(second).AllocatedEnergy, 0.000001f);
        Assert.AreEqual((firstRequest + secondRequest) * ratio, Generation(generator).AllocatedEnergy, 0.000001f);
        Assert.AreEqual(fuel - Generation(generator).AllocatedEnergy,
            _entityManager.GetComponentData<FuelEnergyState>(generator).RemainingEnergy, 0.000001f);
    }

    [TestCase(1f, 10f, 1, 5.5f, 1f, 4.5f)]
    [TestCase(10f, 10f, 1, 5f, 2.5f, 2.5f)]
    [TestCase(10f, 20f, 2, 6f, 2f, 4f)]
    public void PowerBudget_GeneratorsShareRatedUtilization_AndRedistributeFuelLimitedCapacity(
        float firstFuel, float secondFuel, int secondTier, float request, float firstAllocation, float secondAllocation)
    {
        EnablePowerSystems();
        if (secondTier == 2)
        {
            AddCoalTierTwo();
        }
        SubmitPole(int2.zero, 1);
        SubmitParticipant(BuildingTypeEnum.CoalGenerator, new int2(1, 0));
        BuildingPlacementRequestUtility.SubmitSpawn(_entityManager,
            new SpawnBuildingRequest(BuildingTypeEnum.CoalGenerator, secondTier, new int2(3, 0)));
        SubmitParticipant(BuildingTypeEnum.DroneStation, new int2(2, 0));
        _simulation.Update();
        Entity first = ParticipantAt(new int2(1, 0));
        Entity second = ParticipantAt(new int2(3, 0));
        Entity consumer = ParticipantAt(new int2(2, 0));
        _entityManager.SetComponentData(first, new FuelEnergyState { RemainingEnergy = firstFuel });
        _entityManager.SetComponentData(second, new FuelEnergyState { RemainingEnergy = secondFuel });
        PreparePowerInputs();
        SetDemand(consumer, request);
        ReservePower();
        Simulation.UpdateAndComplete(_supplyExecution);
        Assert.AreEqual(firstAllocation, Generation(first).AllocatedEnergy, 0.000001f);
        Assert.AreEqual(secondAllocation, Generation(second).AllocatedEnergy, 0.000001f);
        Assert.AreEqual(request, Supply(consumer).AllocatedEnergy, 0.000001f);
        Assert.AreEqual(1f, Supply(consumer).SupplyRatio);
    }

    [Test]
    public void PowerBudget_BasePriority_UsesCurrentPreparedGroups_AcrossBirthMergeAndSplit()
    {
        EnablePowerSystems();
        SubmitPole(int2.zero, 1, BuildingTypeEnum.MainFacility);
        SubmitPole(new int2(8, 0), 2);
        SubmitPole(new int2(24, 0), 3);
        SubmitParticipant(BuildingTypeEnum.CoalGenerator, new int2(9, 1));
        SubmitParticipant(BuildingTypeEnum.CoalGenerator, new int2(25, 1));
        SubmitParticipant(BuildingTypeEnum.DroneStation, new int2(10, 1));
        SubmitParticipant(BuildingTypeEnum.DroneStation, new int2(24, 1));
        _simulation.Update();
        Entity main = PoleAt(int2.zero);
        Entity remotePole = PoleAt(new int2(24, 0));
        Entity localGenerator = ParticipantAt(new int2(9, 1));
        Entity remoteGenerator = ParticipantAt(new int2(25, 1));
        Entity local = ParticipantAt(new int2(10, 1));
        Entity remote = ParticipantAt(new int2(24, 1));
        _entityManager.SetComponentData(localGenerator, new FuelEnergyState { RemainingEnergy = 10f });
        _entityManager.SetComponentData(remoteGenerator, new FuelEnergyState { RemainingEnergy = 10f });

        PreparePowerInputs();
        SetDemand(local, 5.5f);
        SetDemand(remote, 2f);
        ReservePower();
        Assert.AreEqual(Entity.Null, Connection(main).Grid);
        Assert.AreEqual(4f, Generation(main).AllocatedEnergy);
        Assert.AreEqual(1.5f, Generation(localGenerator).AllocatedEnergy);
        Assert.AreEqual(2f, Generation(remoteGenerator).AllocatedEnergy);
        Simulation.UpdateAndComplete(_supplyExecution);
        PublishTopology();
        Assert.AreNotEqual(Connection(local).Grid, Connection(remote).Grid);

        SubmitPole(new int2(16, 0), 4);
        _simulation.Update();
        Entity bridge = PoleAt(new int2(16, 0));
        PreparePowerInputs();
        SetDemand(local, 5.5f);
        SetDemand(remote, 2f);
        ReservePower();
        Assert.AreNotEqual(Connection(local).Grid, Connection(remote).Grid,
            "준비된 병합은 아직 공개 연결에 반영되지 않았다.");
        Assert.AreEqual(4f, Generation(main).AllocatedEnergy);
        Assert.AreEqual(1.75f, Generation(localGenerator).AllocatedEnergy);
        Assert.AreEqual(1.75f, Generation(remoteGenerator).AllocatedEnergy);
        Simulation.UpdateAndComplete(_supplyExecution);
        Assert.AreEqual(5.5f, Supply(local).AllocatedEnergy);
        Assert.AreEqual(2f, Supply(remote).AllocatedEnergy);
        PublishTopology();
        Assert.AreEqual(Connection(local).Grid, Connection(remote).Grid);

        RequestDemolition(bridge);
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<BuildingDemolitionCommandSystem>());
        Simulation.Playback(_world.GetOrCreateSystemManaged<EndCommandEntityCommandBufferSystem>());
        PreparePowerInputs();
        SetDemand(local, 5.5f);
        SetDemand(remote, 2f);
        ReservePower();
        Assert.AreEqual(Connection(local).Grid, Connection(remote).Grid,
            "공개 망이 아직 같아도 이번 틱 분리 계획으로 예산을 나눈다.");
        Assert.AreEqual(1.5f, Generation(localGenerator).AllocatedEnergy);
        Assert.AreEqual(2f, Generation(remoteGenerator).AllocatedEnergy);
        Simulation.UpdateAndComplete(_supplyExecution);
        Assert.AreEqual(2f, Supply(remote).AllocatedEnergy);
        PublishTopology();
        Assert.AreNotEqual(Connection(local).Grid, Connection(remote).Grid);
    }

    [Test]
    public void PowerBudget_StoredCoalFundsSupply_AndFloatReservationsStayWithinFunding()
    {
        EnablePowerSystems();
        SubmitPole(int2.zero, 1);
        SubmitParticipant(BuildingTypeEnum.CoalGenerator, new int2(1, 0));
        SubmitParticipant(BuildingTypeEnum.DroneStation, new int2(2, 0));
        SubmitParticipant(BuildingTypeEnum.DroneStation, new int2(3, 0));
        _simulation.Update();
        Entity generator = ParticipantAt(new int2(1, 0));
        Entity first = ParticipantAt(new int2(2, 0));
        Entity second = ParticipantAt(new int2(3, 0));
        Entity coal = _entityManager.CreateEntity(typeof(ItemIdentity), typeof(ItemOwnership), typeof(GridPosition));
        _entityManager.SetComponentData(coal, new ItemIdentity(ItemTypeEnum.Coal));
        _entityManager.SetComponentData(coal, new ItemOwnership(generator));
        _entityManager.GetBuffer<StoredItemElement>(generator).Add(new StoredItemElement(coal, ItemTypeEnum.Coal, 0));
        PreparePowerInputs();
        SetDemand(first, 0.3f);
        SetDemand(second, 0.7f);
        ReservePower();
        Simulation.UpdateAndComplete(_supplyExecution);
        Assert.AreEqual(10f, Generation(generator).AvailableEnergy);
        Assert.AreEqual(0.3f, Supply(first).AllocatedEnergy, 0.000001f);
        Assert.IsTrue(_entityManager.Exists(coal));
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(generator).Length);
        Assert.AreEqual(599f, _entityManager.GetComponentData<FuelEnergyState>(generator).RemainingEnergy, 0.00001f);
        Simulation.Playback(_endPower);
        Assert.IsFalse(_entityManager.Exists(coal));

        _entityManager.SetComponentData(generator, new FuelEnergyState { RemainingEnergy = 0.1f });
        PreparePowerInputs();
        SetDemand(first, 0.3f);
        SetDemand(second, 0.7f);
        ReservePower();
        Simulation.UpdateAndComplete(_supplyExecution);
        Assert.AreEqual(0.1f, Supply(first).SupplyRatio, 0.000001f);
        Assert.AreEqual(Supply(first).SupplyRatio, Supply(second).SupplyRatio);
        double total = (double)Supply(first).AllocatedEnergy + Supply(second).AllocatedEnergy;
        Assert.LessOrEqual(total, (double)Generation(generator).AllocatedEnergy);
        Assert.LessOrEqual(Generation(generator).AllocatedEnergy, 0.1f);
        Assert.IsFalse(SimulationFailureUtility.HasFatalError(_entityManager));
    }

    [Test]
    public void PowerBudget_InsufficientFuelBeforeConsumption_RejectsAllResults()
    {
        EnablePowerSystems();
        SubmitPole(int2.zero, 1);
        SubmitPole(new int2(30, 0), 2);
        SubmitParticipant(BuildingTypeEnum.CoalGenerator, new int2(1, 0));
        SubmitParticipant(BuildingTypeEnum.DroneStation, new int2(2, 0));
        SubmitParticipant(BuildingTypeEnum.DroneStation, new int2(3, 0));
        _simulation.Update();
        Entity generator = ParticipantAt(new int2(1, 0));
        Entity first = ParticipantAt(new int2(2, 0));
        Entity second = ParticipantAt(new int2(3, 0));
        _entityManager.SetComponentData(generator, new FuelEnergyState { RemainingEnergy = 10f });
        PreparePowerInputs();
        SetDemand(first, 1f);
        SetDemand(second, 2f);
        ReservePower();
        _entityManager.SetComponentData(generator, new FuelEnergyState { RemainingEnergy = 1f });
        LogAssert.Expect(LogType.Error, new Regex("PowerSupplyExecutionSystem.*Invalid fuel consumption"));
        Simulation.UpdateAndComplete(_supplyExecution);
        Assert.AreEqual(0f, Supply(first).AllocatedEnergy);
        Assert.AreEqual(0f, Supply(second).AllocatedEnergy);
        Simulation.Playback(_endPower);
        Assert.IsTrue(SimulationFailureUtility.HasFatalError(_entityManager));
    }

    [Test]
    public void PowerBudget_TopologyPreparationFailure_DoesNotPublishOrDiscardExistingConnections()
    {
        EnablePowerSystems();
        Entities.CreateResourceNode(new int2(2, 0), ItemTypeEnum.Iron_Ore, 50);
        SubmitPole(int2.zero, 1, BuildingTypeEnum.MainFacility);
        SubmitPole(new int2(1, 0), 2);
        SubmitParticipant(BuildingTypeEnum.Miner, new int2(2, 0));
        _simulation.Update();
        _simulation.Update();
        Entity main = PoleAt(int2.zero);
        Entity consumer = ParticipantAt(new int2(2, 0));
        Entity grid = Connection(consumer).Grid;
        Assert.AreEqual(1f, Supply(consumer).AllocatedEnergy);
        Entity invalidGrid = _entityManager.CreateEntity();
        _entityManager.SetComponentData(main, new PowerGridConnection { PowerPole = main, Grid = invalidGrid });
        LogAssert.Expect(LogType.Error, new Regex("PowerGridTopologyReservationSystem.*Invalid pole"));
        _simulation.Update();
        Assert.IsTrue(SimulationFailureUtility.HasFatalError(_entityManager));
        Assert.IsFalse(_entityManager.IsComponentEnabled<PowerSupplyReservation>(consumer));
        Assert.AreEqual(0f, Supply(consumer).AllocatedEnergy);
        Assert.AreEqual(grid, Connection(consumer).Grid);
        Assert.IsTrue(_entityManager.Exists(grid));
    }

    [TestCase(true, false)]
    [TestCase(true, true)]
    [TestCase(false, false)]
    [TestCase(false, true)]
    public void PowerBudget_SelectedPoleInvalidBeforeReservation_DoesNotAllocate(
        bool includeGenerator, bool removePoleEntity)
    {
        EnablePowerSystems();
        SubmitPole(int2.zero, 1);
        if (includeGenerator)
        {
            SubmitParticipant(BuildingTypeEnum.CoalGenerator, new int2(1, 0));
        }
        SubmitParticipant(BuildingTypeEnum.DroneStation, new int2(2, 0));
        _simulation.Update();
        _simulation.Update();
        Entity pole = PoleAt(int2.zero);
        Entity consumer = ParticipantAt(new int2(2, 0));
        Entity generator = includeGenerator ? ParticipantAt(new int2(1, 0)) : Entity.Null;
        if (includeGenerator)
        {
            _entityManager.SetComponentData(generator, new FuelEnergyState { RemainingEnergy = 10f });
        }
        PreparePowerInputs();
        SetDemand(consumer, 2f);
        Simulation.UpdateAndComplete(_topology);
        var connectionBefore = Connection(consumer);
        var generationBefore = includeGenerator ? Generation(generator) : default;
        Assert.IsFalse(_entityManager.IsComponentEnabled<PowerSupplyReservation>(consumer));
        if (removePoleEntity)
        {
            _entityManager.DestroyEntity(pole);
        }
        else
        {
            _entityManager.RemoveComponent<PowerGridTopologyDecision>(pole);
        }

        Assert.DoesNotThrow(() => Simulation.UpdateAndComplete(_budget));

        Assert.IsFalse(_entityManager.IsComponentEnabled<PowerSupplyReservation>(consumer));
        Assert.AreEqual(0f, Reservation(consumer).AllocatedEnergy);
        Assert.AreEqual(0f, Supply(consumer).AllocatedEnergy);
        Assert.AreEqual(connectionBefore, Connection(consumer));
        Assert.IsTrue(_entityManager.Exists(connectionBefore.Grid));
        if (includeGenerator)
        {
            Assert.AreEqual(generationBefore, Generation(generator));
            Assert.AreEqual(10f, _entityManager.GetComponentData<FuelEnergyState>(generator).RemainingEnergy);
        }
        Simulation.Playback(_endPower);
        Assert.IsFalse(SimulationFailureUtility.HasFatalError(_entityManager));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void PowerBudget_UnpreparedPlanOrDemand_DoesNotAllocate(bool generatorPlanUnprepared)
    {
        EnablePowerSystems();
        SubmitPole(int2.zero, 1);
        SubmitParticipant(BuildingTypeEnum.CoalGenerator, new int2(1, 0));
        SubmitParticipant(BuildingTypeEnum.DroneStation, new int2(2, 0));
        _simulation.Update();
        _simulation.Update();
        Entity generator = ParticipantAt(new int2(1, 0));
        Entity consumer = ParticipantAt(new int2(2, 0));
        _entityManager.SetComponentData(generator, new FuelEnergyState { RemainingEnergy = 10f });
        PreparePowerInputs();
        SetDemand(consumer, 2f);
        Simulation.UpdateAndComplete(_topology);
        var generationBefore = Generation(generator);
        var connectionBefore = Connection(consumer);
        if (generatorPlanUnprepared)
        {
            _entityManager.SetComponentEnabled<PowerGeneratorSupplyPlan>(generator, false);
        }
        else
        {
            _entityManager.SetComponentEnabled<PowerDemand>(consumer, false);
        }

        Simulation.UpdateAndComplete(_budget);

        Assert.IsFalse(_entityManager.IsComponentEnabled<PowerSupplyReservation>(consumer));
        Assert.AreEqual(generationBefore, Generation(generator));
        Assert.AreEqual(0f, Supply(consumer).AllocatedEnergy);
        Assert.AreEqual(10f, _entityManager.GetComponentData<FuelEnergyState>(generator).RemainingEnergy);
        Assert.AreEqual(connectionBefore, Connection(consumer));
        Simulation.Playback(_endPower);
        Assert.IsFalse(SimulationFailureUtility.HasFatalError(_entityManager));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void PowerExecution_UnpreparedPlanOrReservation_DoesNotConsume(bool generatorUnprepared)
    {
        EnablePowerSystems();
        SubmitPole(int2.zero, 1);
        SubmitParticipant(BuildingTypeEnum.CoalGenerator, new int2(1, 0));
        SubmitParticipant(BuildingTypeEnum.DroneStation, new int2(2, 0));
        _simulation.Update();
        Entity generator = ParticipantAt(new int2(1, 0));
        Entity consumer = ParticipantAt(new int2(2, 0));
        _entityManager.SetComponentData(generator, new FuelEnergyState { RemainingEnergy = 10f });
        PreparePowerInputs();
        SetDemand(consumer, 2f);
        ReservePower();
        if (generatorUnprepared)
        {
            _entityManager.SetComponentEnabled<PowerGeneratorSupplyPlan>(generator, false);
        }
        else
        {
            _entityManager.SetComponentEnabled<PowerSupplyReservation>(consumer, false);
        }

        Simulation.UpdateAndComplete(_supplyExecution);

        Assert.AreEqual(10f, _entityManager.GetComponentData<FuelEnergyState>(generator).RemainingEnergy);
        Assert.AreEqual(0f, Supply(consumer).AllocatedEnergy);
        Simulation.Playback(_endPower);
        Assert.IsFalse(SimulationFailureUtility.HasFatalError(_entityManager));
    }

    [Test]
    public void PowerPreparation_ResetClearsPreviousSupplyAndDemand()
    {
        EnablePowerSystems();
        SubmitPole(int2.zero, 1);
        SubmitParticipant(BuildingTypeEnum.CoalGenerator, new int2(1, 0));
        SubmitParticipant(BuildingTypeEnum.DroneStation, new int2(2, 0));
        _simulation.Update();
        Entity generator = ParticipantAt(new int2(1, 0));
        Entity consumer = ParticipantAt(new int2(2, 0));
        _entityManager.SetComponentData(generator, new FuelEnergyState { RemainingEnergy = 10f });
        PreparePowerInputs();
        SetDemand(consumer, 2f);
        ReservePower();
        Simulation.UpdateAndComplete(_supplyExecution);
        Assert.AreEqual(2f, Supply(consumer).AllocatedEnergy);
        Simulation.UpdateAndComplete(_frameReset);
        Simulation.UpdateAndComplete(_demand);
        Assert.AreEqual(0f, Supply(consumer).AllocatedEnergy);
        Assert.AreEqual(0f, Demand(consumer));
        Assert.IsTrue(_entityManager.IsComponentEnabled<PowerDemand>(consumer));
        Simulation.Playback(_endPower);
        Assert.IsFalse(SimulationFailureUtility.HasFatalError(_entityManager));
    }

    [Test]
    public void PowerBudget_DisabledAndApprovedDemolition_StopSupplyWithoutSuppressingWorkDemand()
    {
        EnablePowerSystems();
        Entities.CreateResourceNode(new int2(2, 0), ItemTypeEnum.Iron_Ore, 50);
        SubmitPole(int2.zero, 1);
        SubmitParticipant(BuildingTypeEnum.CoalGenerator, new int2(1, 0));
        SubmitParticipant(BuildingTypeEnum.Miner, new int2(2, 0));
        _simulation.Update();
        Entity generator = ParticipantAt(new int2(1, 0));
        Entity consumer = ParticipantAt(new int2(2, 0));
        _entityManager.SetComponentData(generator, new FuelEnergyState { RemainingEnergy = 10f });
        _simulation.Update();
        Assert.AreEqual(1f, Demand(consumer));
        Assert.AreEqual(1f, Supply(consumer).AllocatedEnergy);
        _entityManager.AddComponent<Disabled>(consumer);
        _entityManager.AddComponent<Disabled>(generator);
        _simulation.Update();
        Assert.AreEqual(0f, Demand(consumer));
        Assert.AreEqual(0f, Supply(consumer).AllocatedEnergy);
        Assert.IsFalse(_entityManager.IsComponentEnabled<PowerSupplyReservation>(consumer));
        Assert.IsFalse(_entityManager.IsComponentEnabled<PowerGeneratorSupplyPlan>(generator));
        Assert.AreEqual(9f, _entityManager.GetComponentData<FuelEnergyState>(generator).RemainingEnergy,
            "활성 틱의 배정 1은 소비하고, 이후 비활성 소비자는 추가 연료를 쓰지 않는다.");
        _entityManager.RemoveComponent<Disabled>(consumer);
        _entityManager.RemoveComponent<Disabled>(generator);
        _simulation.Update();
        Assert.AreEqual(1f, Supply(consumer).AllocatedEnergy);
        RequestDemolition(generator);
        _simulation.Update();
        Assert.IsFalse(_entityManager.Exists(generator));
        Assert.IsTrue(_entityManager.GetComponentData<MinerWorkPreparation>(consumer).CanMine);
        Assert.AreEqual(1f, Demand(consumer));
        Assert.AreEqual(0f, Supply(consumer).AllocatedEnergy);
    }

    [TestCase(0)]
    [TestCase(50)]
    public void PowerPhase_EndPowerConsumesBeforeBuilding_AndNewCoalStartsNextTick(int previousCoalCount)
    {
        EnablePowerSystems();
        EnableProductionAndStorage();
        Entities.CreateResourceNode(new int2(3, 0), ItemTypeEnum.Iron_Ore, 50);
        SubmitPole(new int2(0, -2), 1);
        SubmitParticipant(BuildingTypeEnum.CoalGenerator, new int2(1, 0));
        SubmitParticipant(BuildingTypeEnum.Miner, new int2(3, 0));
        _simulation.Update();
        Entity generator = ParticipantAt(new int2(1, 0));
        Entity miner = ParticipantAt(new int2(3, 0));
        for (int i = 0; i < previousCoalCount; i++)
        {
            Entities.CreateStoredItem(generator, ItemTypeEnum.Coal);
        }
        Assert.IsTrue(_entityManager.GetComponentData<StorageFilter>(generator).IsItemAllowed(ItemTypeEnum.Coal));
        Assert.IsFalse(_entityManager.GetComponentData<StorageFilter>(generator).IsItemAllowed(ItemTypeEnum.Iron_Ore));
        Entities.CreateBelt(int2.zero, DirectionEnum.Right);
        Entity incoming = Entities.CreateBeltItem(int2.zero, DirectionEnum.Right, 1f, itemType: ItemTypeEnum.Coal);
        SyncStorageIndices();

        _simulation.Update();
        Assert.IsTrue(_entityManager.Exists(incoming));
        Assert.AreEqual(generator, _entityManager.GetComponentData<ItemOwnership>(incoming).Owner);
        Assert.AreEqual(previousCoalCount == 0 ? 1 : 50, _entityManager.GetBuffer<StoredItemElement>(generator).Length);
        Assert.AreEqual(previousCoalCount == 0 ? 0f : 1f, Supply(miner).AllocatedEnergy, 0.00001f);
        Assert.AreEqual(previousCoalCount == 0 ? 0f : 599f, _entityManager.GetComponentData<FuelEnergyState>(generator).RemainingEnergy, 0.00001f);
        Assert.AreEqual(previousCoalCount == 0 ? 0f : 0.1f, _entityManager.GetComponentData<MinerState>(miner).Progress, 0.00001f);

        _simulation.Update();
        Assert.AreEqual(1f, Supply(miner).AllocatedEnergy, 0.00001f);
        Assert.AreEqual(previousCoalCount == 0 ? 599f : 598f, _entityManager.GetComponentData<FuelEnergyState>(generator).RemainingEnergy, 0.00001f);
        Assert.AreEqual(previousCoalCount != 0, _entityManager.Exists(incoming));
        Assert.IsFalse(SimulationFailureUtility.HasFatalError(_entityManager));
    }

    [Test]
    public void PowerPhase_PartialSupplyControlsProduction_AndDoesNotRefundFinishedWork()
    {
        EnablePowerSystems();
        EnableProductionAndStorage();
        Entities.CreateResourceNode(new int2(3, 0), ItemTypeEnum.Iron_Ore, 50);
        SubmitPole(int2.zero, 1);
        SubmitParticipant(BuildingTypeEnum.CoalGenerator, new int2(1, 0));
        SubmitParticipant(BuildingTypeEnum.Miner, new int2(3, 0));
        SubmitParticipant(BuildingTypeEnum.Crafter, new int2(4, 0));
        _simulation.Update();
        Entity generator = ParticipantAt(new int2(1, 0));
        Entity miner = ParticipantAt(new int2(3, 0));
        Entity crafter = ParticipantAt(new int2(4, 0));
        _entityManager.SetComponentData(generator, new FuelEnergyState { RemainingEnergy = 1.5f });
        _entityManager.SetComponentData(crafter, new CrafterState(1)
        {
            IsCraftingActive = true, Progress = 0.99f, Status = CrafterStatusEnum.Crafting
        });
        _simulation.Update();
        Assert.AreEqual(0.5f, Supply(miner).SupplyRatio, 0.00001f);
        Assert.AreEqual(0.05f, _entityManager.GetComponentData<MinerState>(miner).Progress, 0.00001f);
        Assert.AreEqual(1f, _entityManager.GetComponentData<CrafterState>(crafter).Progress);
        Assert.AreEqual(0f, _entityManager.GetComponentData<FuelEnergyState>(generator).RemainingEnergy);
        Assert.AreEqual(1f, Supply(crafter).AllocatedEnergy, 0.00001f, "조기 완료해도 확정 소비는 환급하지 않는다.");

        _simulation.Update();
        Assert.AreEqual(0.05f, _entityManager.GetComponentData<MinerState>(miner).Progress, 0.00001f);
        Assert.AreEqual(0f, Demand(crafter));
        Assert.AreEqual(1, _entityManager.GetBuffer<ProductItemElement>(crafter).Length, "완료 결과 확정은 무전력에도 허용한다.");
        Assert.IsFalse(SimulationFailureUtility.HasFatalError(_entityManager));
    }

    [Test]
    public void PowerPhase_NoPowerPreservesNewCraftIngredients_ThenSupplyStartsCraft()
    {
        EnablePowerSystems();
        EnableProductionAndStorage();
        var command = _world.GetOrCreateSystemManaged<CommandGroup>();
        command.AddSystemToUpdateList(_world.GetOrCreateSystem<CrafterRecipeCommandSystem>());
        command.SortSystems();
        SubmitPole(int2.zero, 1);
        SubmitParticipant(BuildingTypeEnum.CoalGenerator, new int2(1, 0));
        SubmitParticipant(BuildingTypeEnum.Crafter, new int2(3, 0));
        _simulation.Update();
        Entity generator = ParticipantAt(new int2(1, 0));
        Entity crafter = ParticipantAt(new int2(3, 0));
        Entity request = _entityManager.CreateEntity(typeof(ChangeCrafterRecipeRequest));
        _entityManager.SetComponentData(request, new ChangeCrafterRecipeRequest(crafter, 1));
        _simulation.Update();
        Assert.IsFalse(_entityManager.Exists(request));
        // 레시피 변경의 기존 재료 반환 이후 공급한다. 변경 전 버퍼를 새 레시피 재료로 재사용하지 않는다.
        Entity ingredient = Entities.CreateStoredItem(crafter, ItemTypeEnum.Iron_Ore);
        _simulation.Update();
        Assert.IsTrue(_entityManager.IsComponentEnabled<PowerConsumer>(crafter));
        Assert.IsFalse(_entityManager.IsComponentEnabled<PowerGenerator>(generator));
        Assert.AreEqual(2f, Demand(crafter));
        Assert.AreEqual(0f, Supply(crafter).AllocatedEnergy);
        Assert.IsFalse(_entityManager.GetComponentData<CrafterState>(crafter).IsCraftingActive);
        Assert.AreEqual(crafter, _entityManager.GetComponentData<ItemOwnership>(ingredient).Owner);
        Assert.AreEqual(1, _entityManager.GetBuffer<StoredItemElement>(crafter).Length);

        _entityManager.SetComponentData(generator, new FuelEnergyState { RemainingEnergy = 1f });
        _simulation.Update();
        Assert.IsTrue(_entityManager.GetComponentData<CrafterState>(crafter).IsCraftingActive);
        Assert.AreEqual(0.05f, _entityManager.GetComponentData<CrafterState>(crafter).Progress, 0.00001f);
        Assert.IsFalse(_entityManager.Exists(ingredient));
        Assert.AreEqual(0f, _entityManager.GetComponentData<FuelEnergyState>(generator).RemainingEnergy);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PowerPhase_InvalidFuelStopsBuildingAndDrone_WithoutPartialCommit(bool duplicateReference)
    {
        EnablePowerSystems();
        Entity counts = _entityManager.CreateEntity(typeof(PowerPhaseExecutionCounts));
        var buildingExecution = _world.GetOrCreateSystemManaged<BuildingExecutionGroup>();
        buildingExecution.AddSystemToUpdateList(_world.GetOrCreateSystem<PowerPhaseBuildingProbeSystem>());
        buildingExecution.SortSystems();
        var drone = _world.GetOrCreateSystemManaged<DroneSimulationGroup>();
        var droneExecution = _world.GetOrCreateSystemManaged<DroneExecutionGroup>();
        drone.AddSystemToUpdateList(droneExecution);
        droneExecution.AddSystemToUpdateList(_world.GetOrCreateSystem<PowerPhaseDroneProbeSystem>());
        droneExecution.SortSystems();
        drone.SortSystems();
        _simulation.AddSystemToUpdateList(drone);
        _simulation.SortSystems();
        Entities.CreateResourceNode(new int2(2, 0), ItemTypeEnum.Iron_Ore, 50);
        SubmitPole(int2.zero, 1);
        SubmitParticipant(BuildingTypeEnum.CoalGenerator, new int2(1, 0));
        SubmitParticipant(BuildingTypeEnum.CoalGenerator, new int2(3, 0));
        SubmitParticipant(BuildingTypeEnum.Miner, new int2(2, 0));
        _simulation.Update();
        Entity generator = ParticipantAt(new int2(1, 0));
        Entity otherGenerator = ParticipantAt(new int2(3, 0));
        Entity coal = Entities.CreateStoredItem(generator, ItemTypeEnum.Coal);
        _entityManager.SetComponentData(generator, new FuelEnergyState { RemainingEnergy = 0f });
        _entityManager.SetComponentData(otherGenerator, new FuelEnergyState { RemainingEnergy = 2f });
        if (duplicateReference)
        {
            var stored = _entityManager.GetBuffer<StoredItemElement>(generator);
            stored.Add(stored[0]);
        }
        else
        {
            _entityManager.SetComponentData(coal, ItemOwnership.WorldItem);
        }
        LogAssert.Expect(LogType.Error, new Regex("PowerSupplyExecutionSystem.*Invalid fuel consumption"));

        _simulation.Update();
        var result = _entityManager.GetComponentData<PowerPhaseExecutionCounts>(counts);
        Assert.AreEqual(1, result.Building);
        Assert.AreEqual(1, result.Drone);
        Assert.IsTrue(SimulationFailureUtility.HasFatalError(_entityManager));
        Assert.IsTrue(_entityManager.Exists(coal));
        Assert.Greater(Generation(generator).FuelItemCount, 0);
        Assert.AreEqual(0f, _entityManager.GetComponentData<FuelEnergyState>(generator).RemainingEnergy);
        Assert.AreEqual(2f, _entityManager.GetComponentData<FuelEnergyState>(otherGenerator).RemainingEnergy);
        Assert.AreEqual(0, GridCount());
        using var errors = _entityManager.CreateEntityQuery(typeof(SimulationFatalError));
        Assert.AreEqual(SimulationFailureStopScope.RemainingDomains,
            errors.GetSingleton<SimulationFatalError>().StopScope);
        _simulation.Update();
        Assert.AreEqual(result.Building, _entityManager.GetComponentData<PowerPhaseExecutionCounts>(counts).Building);
        Assert.AreEqual(result.Drone, _entityManager.GetComponentData<PowerPhaseExecutionCounts>(counts).Drone);

    }

    [Test]
    public void PowerPhase_DemolitionReturnsOnlyUnconvertedCoal()
    {
        EnablePowerSystems();
        SubmitPole(int2.zero, 1);
        SubmitParticipant(BuildingTypeEnum.CoalGenerator, new int2(1, 0));
        SubmitParticipant(BuildingTypeEnum.DroneStation, new int2(2, 0));
        _simulation.Update();
        Entity generator = ParticipantAt(new int2(1, 0));
        Entity consumer = ParticipantAt(new int2(2, 0));
        Entity remaining = Entities.CreateStoredItem(generator, ItemTypeEnum.Coal);
        Entity converted = Entities.CreateStoredItem(generator, ItemTypeEnum.Coal);
        PreparePowerInputs();
        SetDemand(consumer, 1f);
        ReservePower();
        Simulation.UpdateAndComplete(_supplyExecution);
        PublishTopology();
        Assert.IsFalse(_entityManager.Exists(converted));
        Assert.AreEqual(599f, _entityManager.GetComponentData<FuelEnergyState>(generator).RemainingEnergy);
        RequestDemolition(generator);
        _simulation.Update();
        Assert.IsFalse(_entityManager.Exists(generator));
        Assert.IsTrue(_entityManager.Exists(remaining));
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<ItemOwnership>(remaining).Owner);
        Assert.IsFalse(_entityManager.Exists(converted));
    }

    [Test]
    public void CommonFatal_EndBuildingStillFinishesDrone_AndStopsNextTick()
    {
        Entity counts = _entityManager.CreateEntity(typeof(PowerPhaseExecutionCounts));
        var buildingExecution = _world.GetOrCreateSystemManaged<BuildingExecutionGroup>();
        buildingExecution.AddSystemToUpdateList(_world.GetOrCreateSystem<PowerPhaseBuildingProbeSystem>());
        buildingExecution.SortSystems();
        var drone = _world.GetOrCreateSystemManaged<DroneSimulationGroup>();
        var droneExecution = _world.GetOrCreateSystemManaged<DroneExecutionGroup>();
        drone.AddSystemToUpdateList(droneExecution);
        droneExecution.AddSystemToUpdateList(_world.GetOrCreateSystem<PowerPhaseDroneProbeSystem>());
        droneExecution.SortSystems();
        drone.SortSystems();
        _simulation.AddSystemToUpdateList(drone);
        _simulation.SortSystems();
        LogAssert.Expect(LogType.Error, "[PowerPhaseTest] EndBuilding failure.");
        var failureEcb = _endBuilding.CreateCommandBuffer();
        SimulationFailureUtility.Record(ref failureEcb, "[PowerPhaseTest] EndBuilding failure.");
        Entities.PrepareSimulationConfiguration();
        _simulation.Update();
        var result = _entityManager.GetComponentData<PowerPhaseExecutionCounts>(counts);
        Assert.AreEqual(1, result.Building);
        Assert.AreEqual(1, result.Drone);
        using var errors = _entityManager.CreateEntityQuery(typeof(SimulationFatalError));
        Assert.AreEqual(SimulationFailureStopScope.NextTick, errors.GetSingleton<SimulationFatalError>().StopScope);
        _simulation.Update();
        Assert.AreEqual(result.Building, _entityManager.GetComponentData<PowerPhaseExecutionCounts>(counts).Building);
        Assert.AreEqual(result.Drone, _entityManager.GetComponentData<PowerPhaseExecutionCounts>(counts).Drone);
    }

    [Test]
    public void ApplyWithoutPreparedSelection_WithNoActivePoles_PreservesPublishedGrid()
    {
        SubmitPole(int2.zero, 1);
        SubmitParticipant(BuildingTypeEnum.Miner, new int2(2, 0));
        _simulation.Update();
        _simulation.Update();
        Entity pole = PoleAt(int2.zero);
        Entity participant = ParticipantAt(new int2(2, 0));
        var published = Connection(participant);
        _entityManager.AddComponent<Disabled>(pole);
        _entityManager.SetComponentEnabled<PowerGridConnectionDecision>(participant, false);

        Simulation.UpdateAndComplete(_topologyApply);
        Simulation.Playback(_endPower);

        Assert.AreEqual(published.Grid, Connection(participant).Grid);
        Assert.AreEqual(published.PowerPole, Connection(participant).PowerPole);
        Assert.IsTrue(_entityManager.Exists(published.Grid));
        Assert.IsFalse(SimulationFailureUtility.HasFatalError(_entityManager));
    }

    private void EnableProductionAndStorage()
    {
        var buildingDecision = _world.GetOrCreateSystemManaged<BuildingDecisionGroup>();
        buildingDecision.AddSystemToUpdateList(_world.GetOrCreateSystem<MinerDecisionSystem>());
        buildingDecision.AddSystemToUpdateList(_world.GetOrCreateSystem<CrafterDecisionSystem>());
        buildingDecision.SortSystems();

        var execution = _world.GetOrCreateSystemManaged<BuildingExecutionGroup>();
        execution.AddSystemToUpdateList(_world.GetOrCreateSystem<MinerExecutionSystem>());
        execution.AddSystemToUpdateList(_world.GetOrCreateSystem<CrafterExecutionSystem>());
        var decision = _world.GetOrCreateSystemManaged<BuildingDecisionGroup>();
        decision.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingItemInputDecisionSystem>());
        var reservation = _world.GetOrCreateSystemManaged<BuildingReservationGroup>();
        reservation.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingStorageInputReservationSystem>());
        var apply = _world.GetOrCreateSystemManaged<BuildingStateApplyGroup>();
        apply.AddSystemToUpdateList(_world.GetOrCreateSystemManaged<ConfirmedBuildingDemolitionTestSystem>());
        apply.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingItemStorageApplySystem>());
        apply.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemOwnershipApplySystem>());
        apply.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemLifecycleApplySystem>());
        apply.AddSystemToUpdateList(_world.GetOrCreateSystem<CrafterStateApplySystem>());
        var sync = _world.GetOrCreateSystemManaged<SynchronizationGroup>();
        sync.AddSystemToUpdateList(_world.GetOrCreateSystem<BuildingSpatialSyncSystem>());
        sync.AddSystemToUpdateList(_world.GetOrCreateSystem<BeltSpatialSyncSystem>());
        sync.AddSystemToUpdateList(_world.GetOrCreateSystem<ItemSpatialSyncSystem>());
        decision.SortSystems();
        reservation.SortSystems();
        execution.SortSystems();
        apply.SortSystems();
        sync.SortSystems();
    }

    private void SyncStorageIndices()
    {
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<BuildingSpatialSyncSystem>());
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<BeltSpatialSyncSystem>());
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<ItemSpatialSyncSystem>());
    }

    private void EnablePowerSystems()
    {
        _demand = _world.GetOrCreateSystem<BuildingPowerDemandPreparationSystem>();
        _availability = _world.GetOrCreateSystem<PowerSupplyDecisionSystem>();
        _budget = _world.GetOrCreateSystem<PowerBudgetReservationSystem>();
        _supplyExecution = _world.GetOrCreateSystem<PowerSupplyExecutionSystem>();
        _minerDecision = _world.GetOrCreateSystem<MinerWorkPreparationSystem>();
        _crafterDecision = _world.GetOrCreateSystem<CrafterWorkPreparationSystem>();
        var preparation = _world.GetOrCreateSystemManaged<SimulationPreparationGroup>();
        var decision = _world.GetOrCreateSystemManaged<PowerDecisionGroup>();
        var reservation = _world.GetOrCreateSystemManaged<PowerReservationGroup>();
        var execution = _world.GetOrCreateSystemManaged<PowerExecutionGroup>();
        var sync = _world.GetOrCreateSystemManaged<SynchronizationGroup>();
        preparation.AddSystemToUpdateList(_minerDecision);
        preparation.AddSystemToUpdateList(_crafterDecision);
        preparation.AddSystemToUpdateList(_demand);
        decision.AddSystemToUpdateList(_availability);
        reservation.AddSystemToUpdateList(_budget);
        execution.AddSystemToUpdateList(_supplyExecution);
        sync.AddSystemToUpdateList(_world.GetOrCreateSystem<ResourceSpatialSyncSystem>());
        preparation.SortSystems();
        decision.SortSystems();
        reservation.SortSystems();
        execution.SortSystems();
        sync.SortSystems();
    }

    private void PreparePowerInputs()
    {
        Simulation.UpdateAndComplete(_frameReset);
        Simulation.UpdateAndComplete(_minerDecision);
        Simulation.UpdateAndComplete(_crafterDecision);
        Simulation.UpdateAndComplete(_connectionSelection);
        Simulation.UpdateAndComplete(_demand);
        Simulation.UpdateAndComplete(_availability);
    }

    private void ReservePower()
    {
        Simulation.UpdateAndComplete(_topology);
        Simulation.UpdateAndComplete(_budget);
    }

    private void PublishTopology()
    {
        Simulation.UpdateAndComplete(_topologyApply);
        Simulation.Playback(_endPower);
        Simulation.UpdateAndComplete(_sync);
    }

    private void SetDemand(Entity consumer, float energy)
    {
        // 실제 연구/충전 Producer는 후속이다. 배분 입력만 명시적으로 제공한다.
        _entityManager.SetComponentData(consumer, new PowerDemand { RequestedEnergy = energy });
        _entityManager.SetComponentEnabled<PowerDemand>(consumer, true);
        _entityManager.SetComponentEnabled<PowerConsumer>(consumer, energy > 0f);
    }

    private float Demand(Entity entity) => _entityManager.GetComponentData<PowerDemand>(entity).RequestedEnergy;
    private PowerSupplyResult Supply(Entity entity) => _entityManager.GetComponentData<PowerSupplyResult>(entity);
    private PowerSupplyReservation Reservation(Entity entity) => _entityManager.GetComponentData<PowerSupplyReservation>(entity);
    private PowerGeneratorSupplyPlan Generation(Entity entity) => _entityManager.GetComponentData<PowerGeneratorSupplyPlan>(entity);

    private void AddCoalTierTwo()
    {
        using var configQuery = _entityManager.CreateEntityQuery(typeof(BuildingConfig));
        _entityManager.GetBuffer<BuildingConfigElement>(configQuery.GetSingletonEntity()).Add(
            new BuildingConfigElement(BuildingTypeEnum.CoalGenerator, 2, "CoalTier2", 1f, 1, true,
                footprint: new int2(1, 1), power: new BuildingPowerConfig(maximumGeneration: 200f, coalEnergyPerItem: 600f)));
        using var prefabQuery = _entityManager.CreateEntityQuery(typeof(BuildingPrefabElement));
        var prefabs = _entityManager.GetBuffer<BuildingPrefabElement>(prefabQuery.GetSingletonEntity());
        Assert.IsTrue(PrefabLookupUtility.TryGetBuildingPrefab(prefabs, BuildingTypeEnum.CoalGenerator, 1, out var prefab));
        prefabs.Add(new BuildingPrefabElement(BuildingTypeEnum.CoalGenerator, 2, prefab));
    }

    private void SetFootprint(BuildingTypeEnum type, int2 size)
    {
        using var query = _entityManager.CreateEntityQuery(typeof(BuildingConfig));
        var configs = _entityManager.GetBuffer<BuildingConfigElement>(query.GetSingletonEntity());
        for (int i = 0; i < configs.Length; i++)
        {
            if (configs[i].BuildingType == type && configs[i].Tier == 1)
            {
                var config = configs[i];
                config.Footprint = size;
                configs[i] = config;
                return;
            }
        }
        Assert.Fail($"Missing config for {type}");
    }

    private void SubmitParticipant(BuildingTypeEnum type, int2 position, DirectionEnum direction = DirectionEnum.Up)
    {
        BuildingPlacementRequestUtility.SubmitSpawn(_entityManager,
            new SpawnBuildingRequest(type, 1, position, direction));
    }

    private Entity CreateCompletionSite(BuildingTypeEnum type, int2 position, int2 size, DirectionEnum direction)
    {
        Entity site = _entityManager.CreateEntity(typeof(ConstructionSite), typeof(BuildingType),
            typeof(GridPosition), typeof(Direction), typeof(BuildingFootprint), typeof(PlacementStamp),
            typeof(ConstructionMaterialRequirementElement), typeof(StoredItemElement));
        _entityManager.SetComponentData(site, new ConstructionSite(type, 1));
        _entityManager.SetComponentData(site, new BuildingType(BuildingTypeEnum.ConstructionSite, 0));
        _entityManager.SetComponentData(site, new GridPosition(position));
        _entityManager.SetComponentData(site, new Direction(direction));
        _entityManager.SetComponentData(site, new BuildingFootprint(size));
        _entityManager.SetComponentData(site, new PlacementStamp(11, 0));
        return site;
    }

    private Entity ParticipantAt(int2 position)
    {
        using var query = _entityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<PowerGridConnectionDecision>(), ComponentType.ReadOnly<GridPosition>() },
            Options = EntityQueryOptions.IgnoreComponentEnabledState
        });
        using var participants = query.ToEntityArray(Allocator.Temp);
        foreach (Entity participant in participants)
        {
            if (math.all(_entityManager.GetComponentData<GridPosition>(participant).Value == position))
            {
                return participant;
            }
        }
        Assert.Fail($"Missing participant at {position}");
        return Entity.Null;
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

public struct PowerPhaseExecutionCounts : IComponentData
{
    public int Building;
    public int Drone;
}

[DisableAutoCreation]
public partial struct PowerPhaseBuildingProbeSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        SystemAPI.GetSingletonRW<PowerPhaseExecutionCounts>().ValueRW.Building++;
    }
}

[DisableAutoCreation]
public partial struct PowerPhaseDroneProbeSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        SystemAPI.GetSingletonRW<PowerPhaseExecutionCounts>().ValueRW.Drone++;
    }
}
