using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;

/// <summary>제품 요청/중재/EndSimulation 경계를 사용한 Identity/수용량 핵심 흐름. 실제 비행/충전 검증은 아니다.</summary>
public class DroneStationLifecycleTests : EcsWorldTestFixture
{
    private Entity _station;
    private EndSimulationEntityCommandBufferSystem _end;
    private Mesh _workerMesh;
    private Mesh _itemMesh;

    public override void SetUp()
    {
        base.SetUp();
        Entity db = TestPrefabDatabaseFactory.CreateDrones(_entityManager);
        Entity prefab = _entityManager.GetComponentData<DronePrefab>(db).Prefab;
        _workerMesh = new Mesh { name = "Station test worker" };
        _entityManager.AddSharedComponentManaged(prefab, new RenderMeshArray(System.Array.Empty<Material>(), new[] { _workerMesh }));
        _entityManager.SetComponentData(prefab, LocalTransform.FromPositionRotationScale(float3.zero, quaternion.identity, 0.5f));
        Entity items = TestPrefabDatabaseFactory.CreateItems(_entityManager);
        var itemPrefabs = _entityManager.GetBuffer<ItemPrefabElement>(items, true);
        PrefabLookupUtility.TryGetItemPrefab(itemPrefabs, ItemTypeEnum.Drone, out Entity itemPrefab);
        _itemMesh = new Mesh { name = "Station test item" };
        _entityManager.AddSharedComponentManaged(itemPrefab, new RenderMeshArray(System.Array.Empty<Material>(), new[] { _itemMesh }));
        _entityManager.SetComponentData(itemPrefab, LocalTransform.FromPositionRotationScale(float3.zero, quaternion.identity, 0.2f));
        _entityManager.CreateEntity(typeof(PrefabDatabaseReady));
        Assert.IsTrue(DroneConfigInitializationSystem.Initialize(_entityManager));
        ItemConfigInitSystem.InitializeItemRegistry(_entityManager);
        _end = _world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
        _station = CreateStation(2, 1);
    }

    public override void TearDown()
    {
        try
        {
            base.TearDown();
        }
        finally
        {
            Object.DestroyImmediate(_workerMesh);
            Object.DestroyImmediate(_itemMesh);
        }
    }

    private Entity CreateStation(int general, int dedicated)
    {
        Entity station = Entities.CreateStorage(int2.zero, new int2(1, 1), slotCount: general + dedicated);
        _entityManager.SetComponentData(station, new BuildingType(BuildingTypeEnum.DroneStation, 1));
        _entityManager.AddComponentData(station, new DroneStationStorage { DedicatedSlotCount = dedicated });
        _entityManager.AddBuffer<DockedDroneElement>(station);
        return station;
    }

    private Entity CreateWorker()
    {
        Entity root = _entityManager.CreateEntity(typeof(LocalTransform));
        _entityManager.SetComponentData(root, LocalTransform.Identity);
        var ecb = _end.CreateCommandBuffer();
        Assert.IsTrue(DroneSpawnUtility.TryInitializeWorkerRole(_entityManager, ref ecb, root, float3.zero, 100f, out string error), error);
        _end.Update();
        return root;
    }

    private Entity Submit(Entity drone, DroneStationOperation operation, Entity station = default)
        => DroneStationRequestUtility.Submit(_entityManager, drone, station == Entity.Null ? _station : station, operation);

    private void RunStages(bool playback = true)
    {
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<DroneStationLifecycleDecisionSystem>());
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<DroneStationLifecycleReservationSystem>());
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<DroneStationLifecycleApplySystem>());
        if (playback) _end.Update();
    }

    private DroneStationRequestStatus Result(Entity request)
    {
        Assert.IsTrue(DroneStationRequestUtility.TryConsumeResult(_entityManager, request, out var result));
        Assert.IsFalse(DroneStationRequestUtility.TryConsumeResult(_entityManager, request, out _));
        return result.Status;
    }

    [Test]
    public void ItemWorkerRoundTrip_IsAtomicAndKeepsEntityBatteryAndVisualTemplate()
    {
        Entity drone = Entities.CreateStoredItem(_station, ItemTypeEnum.Drone, 2);
        _entityManager.AddComponentData(drone, new DroneBatteryState { CurrentCharge = 25f });
        Entity first = Submit(drone, DroneStationOperation.Activate);
        Entity duplicate = Submit(drone, DroneStationOperation.Activate);
        RunStages(false);
        Assert.IsTrue(_entityManager.HasComponent<ItemIdentity>(drone));
        Assert.AreEqual(1, _entityManager.GetBuffer<StoredItemElement>(_station).Length);
        Assert.IsFalse(_entityManager.HasComponent<DroneStationResult>(first));
        Assert.IsFalse(_entityManager.HasComponent<RenderMeshArray>(drone));
        _end.Update();
        Assert.AreEqual(DroneStationRequestStatus.Applied, Result(first));
        Assert.AreEqual(DroneStationRequestStatus.Duplicate, Result(duplicate));
        Assert.IsFalse(_entityManager.HasComponent<ItemIdentity>(drone));
        Assert.IsFalse(_entityManager.HasComponent<ItemOwnership>(drone));
        Assert.IsTrue(_entityManager.HasComponent<DroneWorker>(drone));
        Assert.AreEqual(25f, _entityManager.GetComponentData<DroneBatteryState>(drone).CurrentCharge);
        Assert.AreEqual(0.5f, _entityManager.GetComponentData<LocalTransform>(drone).Scale);
        Assert.AreEqual(_workerMesh, _entityManager.GetSharedComponentManaged<RenderMeshArray>(drone).MeshReferences[0].Value);
        Assert.IsFalse(_entityManager.GetComponentData<DroneWorkerObservation>(drone).CanAcceptTask);
        Assert.IsEmpty(_entityManager.GetBuffer<StoredItemElement>(_station).ToNativeArray(Allocator.Temp).ToArray());

        _entityManager.SetComponentData(drone, new DroneBatteryState { CurrentCharge = 100f });
        Entity store = Submit(drone, DroneStationOperation.Store);
        RunStages(false);
        Assert.IsTrue(_entityManager.HasComponent<DroneWorker>(drone));
        Assert.AreEqual(_workerMesh, _entityManager.GetSharedComponentManaged<RenderMeshArray>(drone).MeshReferences[0].Value);
        _end.Update();
        Assert.AreEqual(DroneStationRequestStatus.Applied, Result(store));
        Assert.IsFalse(_entityManager.HasComponent<DroneWorker>(drone));
        Assert.AreEqual(ItemTypeEnum.Drone, _entityManager.GetComponentData<ItemIdentity>(drone).Type);
        Assert.AreEqual(_station, _entityManager.GetComponentData<ItemOwnership>(drone).Owner);
        Assert.IsTrue(_entityManager.HasComponent<DisableRendering>(drone));
        Assert.AreEqual(0.2f, _entityManager.GetComponentData<LocalTransform>(drone).Scale);
        Assert.AreEqual(_itemMesh, _entityManager.GetSharedComponentManaged<RenderMeshArray>(drone).MeshReferences[0].Value);
        Assert.AreEqual(drone, _entityManager.GetBuffer<StoredItemElement>(_station)[0].ItemEntity);
        Entity second = Submit(drone, DroneStationOperation.Activate);
        RunStages();
        Assert.AreEqual(DroneStationRequestStatus.Applied, Result(second));
        Assert.AreEqual(100f, _entityManager.GetComponentData<DroneBatteryState>(drone).CurrentCharge);
        Assert.IsEmpty(_entityManager.GetBuffer<StoredItemElement>(drone).ToNativeArray(Allocator.Temp).ToArray());
    }

    [Test]
    public void BatchedStores_UseSubmissionOrderAcrossChunks_AndPublishOnlyAtEndSimulation()
    {
        const int requestCount = 180;
        const int acceptedCount = 100; // General and dedicated slots each hold 50 Drone items.
        Entity station = CreateStation(1, 1);
        var workers = new Entity[requestCount];
        var requests = new Entity[requestCount];
        for (int i = 0; i < requestCount; i++)
        {
            workers[i] = CreateWorker();
            requests[i] = Submit(workers[i], DroneStationOperation.Store, station);
            // Alternate archetypes so query/chunk order differs from submission order.
            if (i % 2 == 0)
                _entityManager.AddComponentData(requests[i], new GridPosition { Value = int2.zero });
        }

        RunStages(false);
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(station).Length);
        for (int i = 0; i < requestCount; i++)
        {
            Assert.IsTrue(_entityManager.HasComponent<DroneWorker>(workers[i]));
            Assert.IsFalse(_entityManager.HasComponent<DroneStationResult>(requests[i]));
        }

        _end.Update();
        for (int i = 0; i < requestCount; i++)
        {
            Assert.AreEqual(i < acceptedCount ? DroneStationRequestStatus.Applied : DroneStationRequestStatus.NoSpace,
                Result(requests[i]), $"Request {i}");
            Assert.AreEqual(i >= acceptedCount, _entityManager.HasComponent<DroneWorker>(workers[i]));
        }

        var inventory = _entityManager.GetBuffer<StoredItemElement>(station, true);
        Assert.AreEqual(acceptedCount, inventory.Length);
        for (int i = 0; i < acceptedCount; i++)
        {
            Assert.AreEqual(workers[i], inventory[i].ItemEntity);
            Assert.AreEqual(i < 50 ? 1 : 0, inventory[i].SlotIndex);
        }
    }

    [TestCase("battery")]
    [TestCase("assignment")]
    [TestCase("cargo")]
    [TestCase("moving")]
    [TestCase("charging")]
    [TestCase("waiting")]
    [TestCase("position")]
    [TestCase("demolition")]
    [TestCase("transport-selection")]
    [TestCase("transport-publication")]
    public void StoreRejectsUnqualifiedWorker_AndPreservesOriginal(string condition)
    {
        Entity drone = CreateWorker();
        if (condition == "battery") _entityManager.SetComponentData(drone, new DroneBatteryState { CurrentCharge = 99f });
        if (condition == "assignment") _entityManager.SetComponentData(drone, new DroneWorkerAssignment { Assignment = _entityManager.CreateEntity() });
        if (condition == "cargo") Entities.CreateStoredItem(drone, ItemTypeEnum.Iron);
        if (condition == "moving" || condition == "charging" || condition == "waiting")
        {
            var activity = condition == "moving" ? DroneActivityKind.Moving : condition == "charging" ? DroneActivityKind.Charging : DroneActivityKind.WaitingForCharge;
            _entityManager.SetComponentData(drone, new DroneActivityState { Activity = activity });
        }
        if (condition == "position") _entityManager.SetComponentData(drone, LocalTransform.FromPosition(new float3(10, 10, 0)));
        if (condition == "demolition") _entityManager.AddComponent<PendingBuildingDemolition>(_station);
        if (condition == "transport-selection" || condition == "transport-publication")
        {
            Entity candidates = DroneSchedulingUtility.GetOrCreateCandidates(_entityManager);
            bool publicationQueued = condition == "transport-publication";
            _entityManager.GetBuffer<DroneTransportPendingReservationElement>(candidates).Add(
                new DroneTransportPendingReservationElement
                {
                    Candidate = new DroneTaskCandidateDecisionElement { Worker = drone },
                    Reservation = new DroneTransportReservation { RemainingQuantity = publicationQueued ? 0 : 3 },
                    PublicationQueued = publicationQueued
                });
        }
        Entity request = Submit(drone, DroneStationOperation.Store);
        RunStages();
        Assert.AreNotEqual(DroneStationRequestStatus.Applied, Result(request));
        Assert.IsTrue(_entityManager.HasComponent<DroneWorker>(drone));
        Assert.IsFalse(_entityManager.HasComponent<ItemIdentity>(drone));
        Assert.AreEqual(0, _entityManager.GetBuffer<StoredItemElement>(_station).Length);
        Assert.AreEqual(condition == "cargo" ? 1 : 0, _entityManager.GetBuffer<StoredItemElement>(drone).Length);
    }

    [Test]
    public void ReturningDrones_ShareStackThenGeneralSpace_WithoutOverbooking()
    {
        _entityManager.SetComponentData(_station, new Storage(2));
        for (int i = 0; i < 49; i++) Entities.CreateStoredItem(_station, ItemTypeEnum.Drone, 1);
        Entity a = CreateWorker(); Entity b = CreateWorker(); Entity c = CreateWorker();
        Entity ra = Submit(a, DroneStationOperation.Store);
        Entity rb = Submit(b, DroneStationOperation.Store);
        RunStages();
        Assert.AreEqual(DroneStationRequestStatus.Applied, Result(ra));
        Assert.AreEqual(DroneStationRequestStatus.Applied, Result(rb));
        var inventory = _entityManager.GetBuffer<StoredItemElement>(_station);
        Assert.AreEqual(1, inventory[inventory.Length - 2].SlotIndex);
        Assert.AreEqual(0, inventory[inventory.Length - 1].SlotIndex);
        for (int i = 0; i < 49; i++) Entities.CreateStoredItem(_station, ItemTypeEnum.Drone, 0);
        Entity rc = Submit(c, DroneStationOperation.Store);
        RunStages();
        Assert.AreEqual(DroneStationRequestStatus.NoSpace, Result(rc));
        Assert.IsTrue(_entityManager.HasComponent<DroneWorker>(c));
        Assert.AreEqual(100, _entityManager.GetBuffer<StoredItemElement>(_station).Length);
    }

    [Test]
    public void DockingUsesWholeSlot_StoreKeepsOwnSlot_ReleasePreservesPartialBattery()
    {
        Entity drone = CreateWorker();
        _entityManager.SetComponentData(drone, new DroneBatteryState { CurrentCharge = 10f });
        Entity dock = Submit(drone, DroneStationOperation.Dock);
        RunStages();
        Assert.AreEqual(DroneStationRequestStatus.Applied, Result(dock));
        Assert.AreEqual(2, _entityManager.GetComponentData<DroneDocked>(drone).SlotIndex);
        Assert.AreEqual(1, _entityManager.GetBuffer<DockedDroneElement>(_station).Length);
        Assert.IsFalse(_entityManager.HasComponent<DisableRendering>(drone));
        Assert.IsFalse(DroneSchedulingUtility.IsIdleWorker(_entityManager, drone, 3));
        Entity release = Submit(drone, DroneStationOperation.Activate);
        RunStages();
        Assert.AreEqual(DroneStationRequestStatus.Applied, Result(release));
        Assert.IsFalse(_entityManager.HasComponent<DroneDocked>(drone));
        Assert.AreEqual(10f, _entityManager.GetComponentData<DroneBatteryState>(drone).CurrentCharge);
        Entity dockAgain = Submit(drone, DroneStationOperation.Dock);
        RunStages();
        Assert.AreEqual(DroneStationRequestStatus.Applied, Result(dockAgain));
        _entityManager.SetComponentData(drone, new DroneBatteryState { CurrentCharge = 100f });
        Entity store = Submit(drone, DroneStationOperation.Store);
        RunStages();
        Assert.AreEqual(DroneStationRequestStatus.Applied, Result(store));
        Assert.IsEmpty(_entityManager.GetBuffer<DockedDroneElement>(_station).ToNativeArray(Allocator.Temp).ToArray());
        Assert.AreEqual(2, _entityManager.GetBuffer<StoredItemElement>(_station)[0].SlotIndex);
    }

    [Test]
    public void DockedActorBlocksDemolitionUntilRelocationOwnerExists()
    {
        Entity drone = CreateWorker();
        Entity dock = Submit(drone, DroneStationOperation.Dock);
        RunStages();
        Assert.AreEqual(DroneStationRequestStatus.Applied, Result(dock));
        Entity request = _entityManager.CreateEntity(typeof(DemolishBuildingRequest));
        _entityManager.SetComponentData(request, new DemolishBuildingRequest { TargetBuilding = _station });
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<BuildingDemolitionCommandSystem>());
        _world.GetOrCreateSystemManaged<EndCommandEntityCommandBufferSystem>().Update();
        Assert.IsFalse(_entityManager.HasComponent<PendingBuildingDemolition>(_station));
        Assert.IsTrue(_entityManager.Exists(drone));
        Assert.AreEqual(_station, _entityManager.GetComponentData<DroneDocked>(drone).Station);
        Assert.AreEqual(drone, _entityManager.GetBuffer<DockedDroneElement>(_station)[0].Drone);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void StoreRechecksLateCargoOrOccupiedSlot_WithoutExpandingReservedSpace(bool spaceUsed)
    {
        Entity drone = CreateWorker();
        Entity request = Submit(drone, DroneStationOperation.Store);
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<DroneStationLifecycleDecisionSystem>());
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<DroneStationLifecycleReservationSystem>());
        Assert.AreEqual(2, _entityManager.GetComponentData<DroneStationDecision>(request).SlotIndex);
        if (spaceUsed)
        {
            for (int i = 0; i < 50; i++) Entities.CreateStoredItem(_station, ItemTypeEnum.Drone, 2);
        }
        else
        {
            Entities.CreateStoredItem(drone, ItemTypeEnum.Iron);
        }
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<DroneStationLifecycleApplySystem>());
        _end.Update();
        Assert.AreEqual(spaceUsed ? DroneStationRequestStatus.NoSpace : DroneStationRequestStatus.Busy, Result(request));
        Assert.IsTrue(_entityManager.HasComponent<DroneWorker>(drone));
        Assert.IsFalse(_entityManager.HasComponent<ItemIdentity>(drone));
        Assert.AreEqual(spaceUsed ? 50 : 0, _entityManager.GetBuffer<StoredItemElement>(_station).Length);
        Assert.AreEqual(spaceUsed ? 0 : 1, _entityManager.GetBuffer<StoredItemElement>(drone).Length);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingDockBuffer_IsNotTreatedAsEmptyAtDemolition(bool hasActor)
    {
        Entity drone = Entity.Null;
        if (hasActor)
        {
            drone = CreateWorker();
            Entity dock = Submit(drone, DroneStationOperation.Dock);
            RunStages();
            Assert.AreEqual(DroneStationRequestStatus.Applied, Result(dock));
        }
        // 정상 Writer가 만드는 상태가 아니라 필수 버퍼 누락을 주입한 내부 정합성 진단 사례다.
        _entityManager.RemoveComponent<DockedDroneElement>(_station);
        Entity request = _entityManager.CreateEntity(typeof(DemolishBuildingRequest));
        _entityManager.SetComponentData(request, new DemolishBuildingRequest(_station));
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<BuildingDemolitionCommandSystem>());
        _world.GetOrCreateSystemManaged<EndCommandEntityCommandBufferSystem>().Update();
        Assert.IsFalse(_entityManager.HasComponent<PendingBuildingDemolition>(_station));
        Assert.IsTrue(_entityManager.Exists(_station));
        if (hasActor)
        {
            Assert.IsTrue(_entityManager.Exists(drone));
            Assert.AreEqual(_station, _entityManager.GetComponentData<DroneDocked>(drone).Station);
        }
    }

    [Test]
    public void DisabledStation_PreservesValidDockOwnershipAtFinalSynchronization()
    {
        Entity drone = CreateWorker();
        Entity dock = Submit(drone, DroneStationOperation.Dock);
        RunStages();
        Assert.AreEqual(DroneStationRequestStatus.Applied, Result(dock));
        _entityManager.AddComponent<Disabled>(_station);
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<BeltSpatialSyncSystem>());
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<BuildingSpatialSyncSystem>());
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<ItemSpatialSyncSystem>());
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<ResourceSpatialSyncSystem>());
        var validation = _world.GetOrCreateSystemManaged<WorldInvariantValidationSystem>();
        validation.SetLogDirectory(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Temp", "Task93", "InvariantTests")));
        bool paused = UnityEditor.EditorApplication.isPaused;
        try
        {
            validation.Update();
            Assert.AreEqual(0, validation.TotalViolationCount);
            Assert.AreEqual(_station, _entityManager.GetComponentData<DroneDocked>(drone).Station);
            Assert.AreEqual(drone, _entityManager.GetBuffer<DockedDroneElement>(_station)[0].Drone);
        }
        finally
        {
            UnityEditor.EditorApplication.isPaused = paused;
        }
    }
}
