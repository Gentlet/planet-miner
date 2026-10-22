using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>드론 생성의 공개/초기 상태와 실패 보존을 검증한다. 준비 태그는 격리 입력이며 실제 Baker 검증은 별도다.</summary>
public class DronePrefabAndSpawnTests : EcsWorldTestFixture
{
    private Entity _ready;
    private Entity _prefab;
    private EndSimulationEntityCommandBufferSystem _endSimulation;

    public override void SetUp()
    {
        base.SetUp();
        Entity database = TestPrefabDatabaseFactory.CreateDrones(_entityManager);
        _prefab = _entityManager.GetComponentData<DronePrefab>(database).Prefab;
        _entityManager.SetComponentData(_prefab, LocalTransform.FromPositionRotationScale(float3.zero,
            quaternion.RotateZ(0.2f), 0.5f));
        _entityManager.AddComponentData(_prefab, new PostTransformMatrix
        {
            Value = float4x4.Scale(new float3(1f, 2f, 1f))
        });
        _ready = _entityManager.CreateEntity(typeof(PrefabDatabaseReady));
        _endSimulation = _world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
    }

    [Test]
    public void Spawn_PublishesOnlyAtEndSimulation_WithExplicitInitialState()
    {
        var commands = _endSimulation.CreateCommandBuffer();
        var position = new float3(-2.25f, 4.5f, -0.2f);
        Assert.IsTrue(DroneSpawnUtility.TrySpawnActiveDrone(_entityManager, ref commands,
            position, 100f, out Entity recordedDrone, out string error), error);
        Assert.AreNotEqual(Entity.Null, recordedDrone);
        using var workers = _entityManager.CreateEntityQuery(typeof(DroneWorker));
        Assert.AreEqual(0, workers.CalculateEntityCount());
        _endSimulation.Update();

        Entity drone = workers.GetSingletonEntity();
        Assert.IsFalse(_entityManager.HasComponent<Prefab>(drone));
        Assert.IsFalse(_entityManager.HasComponent<ItemIdentity>(drone));
        var transform = _entityManager.GetComponentData<LocalTransform>(drone);
        Assert.AreEqual(position, transform.Position);
        Assert.AreEqual(0.5f, transform.Scale, "The baked visual scale must not be reset by runtime position initialization.");
        Assert.AreEqual(_entityManager.GetComponentData<LocalTransform>(_prefab).Rotation, transform.Rotation);
        var worldTransform = _entityManager.GetComponentData<LocalToWorld>(drone).Value;
        Assert.That(math.length(worldTransform.c1.xyz), Is.EqualTo(1f).Within(0.00001f),
            "The initial world matrix must preserve the visual prefab's nonuniform post-transform scale.");
        Assert.AreEqual(new int2(-3, 4), _entityManager.GetComponentData<GridPosition>(drone).Value);
        var observation = _entityManager.GetComponentData<DroneWorkerObservation>(drone);
        Assert.AreEqual(position, observation.Position);
        Assert.AreEqual(1, observation.Revision);
        Assert.IsFalse(observation.CanAcceptTask, "No movement/route producer has made this worker available yet.");
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<DroneWorkerAssignment>(drone).Assignment);
        Assert.AreEqual(DroneCargoOriginEnum.None, _entityManager.GetComponentData<DroneCargoState>(drone).Origin);
        Assert.IsTrue(_entityManager.GetBuffer<StoredItemElement>(drone).IsEmpty);
        Assert.AreEqual(100f, _entityManager.GetComponentData<DroneBatteryState>(drone).CurrentCharge);
        using var capacities = _entityManager.CreateEntityQuery(typeof(DroneCapacityState));
        Assert.AreEqual(0, capacities.CalculateEntityCount(), "Task9.3 capacity initialization is not part of this factory.");
    }

    [Test]
    public void ExistingRoot_PreservesEntityAndBattery_AndRejectsRepeatedInitialization()
    {
        Entity root = _entityManager.CreateEntity(typeof(LocalTransform), typeof(DroneBatteryState));
        _entityManager.SetComponentData(root, LocalTransform.Identity);
        _entityManager.SetComponentData(root, new DroneBatteryState { CurrentCharge = 25f });
        var commands = _endSimulation.CreateCommandBuffer();
        Assert.IsTrue(DroneSpawnUtility.TryInitializeWorkerRole(_entityManager, ref commands,
            root, new float3(3, 4, 0), 100f, out string error), error);
        _endSimulation.Update();
        Assert.AreEqual(25f, _entityManager.GetComponentData<DroneBatteryState>(root).CurrentCharge);

        Entity item = Entities.CreateStoredItem(root, ItemTypeEnum.Iron, 0);
        var repeat = _endSimulation.CreateCommandBuffer();
        Assert.IsFalse(DroneSpawnUtility.TryInitializeWorkerRole(_entityManager, ref repeat,
            root, new float3(99, 99, 0), 100f, out error));
        Assert.IsNotEmpty(error);
        _endSimulation.Update();
        Assert.AreEqual(new float3(3, 4, 0), _entityManager.GetComponentData<LocalTransform>(root).Position);
        Assert.AreEqual(25f, _entityManager.GetComponentData<DroneBatteryState>(root).CurrentCharge);
        Assert.AreEqual(item, _entityManager.GetBuffer<StoredItemElement>(root)[0].ItemEntity);
        using var workers = _entityManager.CreateEntityQuery(typeof(DroneWorker));
        Assert.AreEqual(root, workers.GetSingletonEntity());
    }

    [TestCase("notReady")]
    [TestCase("invalidPosition")]
    [TestCase("gridOverflow")]
    [TestCase("invalidBattery")]
    public void RejectedCreation_RecordsNoCommands_AndPreservesExistingItem(string fault)
    {
        Entity source = _entityManager.CreateEntity(typeof(ItemIdentity), typeof(ItemOwnership), typeof(LocalTransform));
        _entityManager.SetComponentData(source, new ItemIdentity(ItemTypeEnum.Drone));
        _entityManager.SetComponentData(source, ItemOwnership.WorldItem);
        _entityManager.SetComponentData(source, LocalTransform.Identity);
        float3 position = float3.zero;
        float maximumBattery = 100f;
        if (fault == "notReady")
        {
            _entityManager.DestroyEntity(_ready);
        }
        else if (fault == "invalidPosition")
        {
            position.x = float.NaN;
        }
        else if (fault == "gridOverflow")
        {
            position.x = float.MaxValue;
        }
        else
        {
            maximumBattery = 0f;
        }
        var commands = _endSimulation.CreateCommandBuffer();
        Assert.IsFalse(DroneSpawnUtility.TrySpawnActiveDrone(_entityManager, ref commands,
            position, maximumBattery, out Entity drone, out string spawnError));
        Assert.AreEqual(Entity.Null, drone);
        Assert.IsNotEmpty(spawnError);
        Assert.IsFalse(DroneSpawnUtility.TryInitializeWorkerRole(_entityManager, ref commands,
            source, position, maximumBattery, out string roleError));
        Assert.IsNotEmpty(roleError);
        _endSimulation.Update();
        Assert.IsTrue(_entityManager.Exists(source));
        Assert.AreEqual(ItemTypeEnum.Drone, _entityManager.GetComponentData<ItemIdentity>(source).Type);
        Assert.AreEqual(Entity.Null, _entityManager.GetComponentData<ItemOwnership>(source).Owner);
        Assert.AreEqual(float3.zero, _entityManager.GetComponentData<LocalTransform>(source).Position);
        using var workers = _entityManager.CreateEntityQuery(typeof(DroneWorker));
        Assert.AreEqual(0, workers.CalculateEntityCount());
    }
}
