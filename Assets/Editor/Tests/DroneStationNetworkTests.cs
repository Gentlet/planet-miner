using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using PlanetMiner.Config;
using PlanetMiner.Tests;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>정거장 생성→이번 틱 연결 판단→EndSimulation 공개→범위 조회와 망 분리/제거의 새 핵심 흐름을 검증한다.</summary>
public class DroneStationNetworkTests : EcsWorldTestFixture
{
    private Entity _config;
    private SystemHandle _decision;
    private SystemHandle _apply;
    private SystemHandle _sync;
    private EndSimulationEntityCommandBufferSystem _endSimulation;

    public override void SetUp()
    {
        base.SetUp();
        Assert.IsTrue(BuildingConfigLoader.TryLoadConfigFromResources(BuildingConfigLoader.DefaultResourcePath,
            out var configs, out var materials));
        _config = BuildingConfigLoader.PublishConfig(_entityManager, configs, materials);
        _decision = _world.GetOrCreateSystem<DroneStationNetworkDecisionSystem>();
        _apply = _world.GetOrCreateSystem<DroneStationNetworkApplySystem>();
        _sync = _world.GetOrCreateSystem<DroneStationSpatialSyncSystem>();
        _endSimulation = _world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
    }

    [Test]
    public void EndBuildingBridge_IsUsedBeforeNetworkPublication_AndMergesWithoutPower()
    {
        Entity first = CreateStation(BuildingTypeEnum.MainFacility, int2.zero, 1);
        Entity last = CreateStation(BuildingTypeEnum.DroneStation, new int2(68, 0), 2);
        RunTick();
        Assert.AreEqual(last, Network(last));

        CreateGameplayPrefabDatabases();
        Entity request = _entityManager.CreateEntity(typeof(SpawnBuildingRequest));
        _entityManager.SetComponentData(request, new SpawnBuildingRequest(BuildingTypeEnum.DroneStation, 1,
            new int2(34, 0), stamp: new PlacementStamp(3, 0)));
        Simulation.UpdateAndComplete(_world.GetOrCreateSystem<BuildingLifecycleApplySystem>());
        var endBuilding = _world.GetOrCreateSystemManaged<EndBuildingEntityCommandBufferSystem>();
        endBuilding.Update();
        using var stations = _entityManager.CreateEntityQuery(typeof(DroneStation));
        Assert.AreEqual(3, stations.CalculateEntityCount());

        Simulation.UpdateAndComplete(_decision);
        Assert.AreEqual(first, _entityManager.GetComponentData<DroneStationNetworkDecision>(last).RepresentativeStation);
        Assert.AreEqual(last, Network(last), "Decision must not publish the connection early.");
        Assert.IsFalse(_entityManager.IsComponentEnabled<PowerConsumer>(last), "Unpowered stations remain network participants.");
        PublishAndSync();
        Assert.AreEqual(first, Network(last));
        Assert.IsTrue(Covers(first, new int2(86, 18)));
        Assert.IsFalse(Covers(first, new int2(87, 18)));
    }

    [TestCase("disabled")]
    [TestCase("demolition")]
    [TestCase("removed")]
    public void RemovingBridge_SplitsNetwork_AndRemovingAllStationsClearsIndex(string kind)
    {
        Entity first = CreateStation(BuildingTypeEnum.DroneStation, int2.zero, 1);
        Entity bridge = CreateStation(BuildingTypeEnum.DroneStation, new int2(34, 0), 2);
        Entity last = CreateStation(BuildingTypeEnum.DroneStation, new int2(68, 0), 3);
        RunTick();
        Assert.AreEqual(first, Network(last));
        if (kind == "disabled") _entityManager.AddComponent<Disabled>(bridge);
        else if (kind == "demolition") _entityManager.AddComponent<PendingBuildingDemolition>(bridge);
        else _entityManager.DestroyEntity(bridge);
        RunTick();
        Assert.AreEqual(last, Network(last));
        if (_entityManager.Exists(bridge)) Assert.AreEqual(Entity.Null, Network(bridge));
        Assert.IsFalse(Covers(first, new int2(34, 0)));
        using var stations = _entityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<DroneStation>() }, Options = EntityQueryOptions.IncludeDisabledEntities
        });
        _entityManager.DestroyEntity(stations);
        RunTick();
        Assert.AreEqual(0, ReadIndex().Map.Count());
    }

    [Test]
    public void RepresentativeRemoval_MovementAndRejoin_RecomputeFromCurrentBuildings()
    {
        Entity first = CreateStation(BuildingTypeEnum.DroneStation, int2.zero, 1);
        Entity second = CreateStation(BuildingTypeEnum.DroneStation, new int2(34, 0), 2);
        Entity third = CreateStation(BuildingTypeEnum.DroneStation, new int2(68, 0), 3);
        RunTick();
        _entityManager.DestroyEntity(first);
        RunTick();
        Assert.AreEqual(second, Network(third));
        _entityManager.SetComponentData(second, new GridPosition(new int2(200, 0)));
        RunTick();
        Assert.AreEqual(third, Network(third));
        Assert.IsFalse(Covers(second, new int2(34, 0)));
        _entityManager.SetComponentData(second, new GridPosition(new int2(34, 0)));
        RunTick();
        Assert.AreEqual(second, Network(third));
    }

    [TestCase(2, 4, DirectionEnum.Up, -1, 0)]
    [TestCase(2, 4, DirectionEnum.Right, 0, -1)]
    [TestCase(2, 3, DirectionEnum.Up, 0, 0)]
    public void SquareCoverage_RotatedFootprint_IncludesSharedCellButNotAdjacency(int width, int height,
        DirectionEnum direction, int expectedSquareMinX, int expectedSquareMinY)
    {
        int side = math.max(width, height);
        SetStationConfig(BuildingTypeEnum.DroneStation, 1, new int2(width, height), 1);
        Entity station = CreateStation(BuildingTypeEnum.DroneStation, int2.zero, 1, direction);
        int maxX = expectedSquareMinX + side - 1 + 16;
        Entity boundary = CreateStation(BuildingTypeEnum.MainFacility, new int2(maxX + 16, 0), 2);
        RunTick();
        Assert.AreEqual(station, Network(boundary), "A shared cell connects the ranges.");
        var coverage = ReadIndex().Map[station];
        Assert.AreEqual(expectedSquareMinX - 16, coverage.Bounds.MinX);
        Assert.AreEqual(expectedSquareMinY - 16, coverage.Bounds.MinY);
        Assert.AreEqual(side + 32, coverage.Bounds.MaxX - coverage.Bounds.MinX + 1);
        Assert.AreEqual(side + 32, coverage.Bounds.MaxY - coverage.Bounds.MinY + 1);
        _entityManager.SetComponentData(boundary, new GridPosition(new int2(maxX + 17, 0)));
        RunTick();
        Assert.AreEqual(boundary, Network(boundary), "Adjacent cells without overlap do not connect.");
    }

    [Test]
    public void NetworkCoverage_IsUnionWithHoles_AndIndexGrowsBeyondInitialCapacity()
    {
        Entity first = CreateStation(BuildingTypeEnum.DroneStation, int2.zero, 1);
        CreateStation(BuildingTypeEnum.DroneStation, new int2(34, 0), 2);
        CreateStation(BuildingTypeEnum.DroneStation, new int2(34, 34), 3);
        for (int i = 0; i < 18; i++) CreateStation(BuildingTypeEnum.DroneStation, new int2(200 + i * 40, 0), (ulong)(4 + i));
        RunTick();
        Assert.AreEqual(21, ReadIndex().Map.Count());
        Assert.IsTrue(Covers(first, new int2(50, 50)));
        Assert.IsFalse(Covers(first, new int2(-10, 40)), "A network bounding rectangle must not fill uncovered cells.");
    }

    [Test]
    public void TypeTierRanges_AreIndependent_AndNegativeRangeUsesZeroAtExtremeCoordinates()
    {
        SetStationConfig(BuildingTypeEnum.DroneStation, 2, new int2(2, 4), -1);
        SetStationConfig(BuildingTypeEnum.DroneStation, 3, new int2(2, 4), int.MaxValue);
        Entity large = CreateStation(BuildingTypeEnum.DroneStation, int2.zero, 1, tier: 3);
        Entity small = CreateStation(BuildingTypeEnum.DroneStation, new int2(int.MaxValue - 3, int.MinValue), 2, tier: 2);
        Entity normal = CreateStation(BuildingTypeEnum.MainFacility, new int2(100, 0), 3);
        RunTick();
        var smallBounds = ReadIndex().Map[small].Bounds;
        Assert.AreEqual(4, smallBounds.MaxX - smallBounds.MinX + 1);
        Assert.AreEqual(4, smallBounds.MaxY - smallBounds.MinY + 1);
        Assert.AreEqual(35, ReadIndex().Map[normal].Bounds.MaxX - ReadIndex().Map[normal].Bounds.MinX + 1);
        var largeBounds = ReadIndex().Map[large].Bounds;
        Assert.AreEqual(4L + 32L * int.MaxValue, largeBounds.MaxX - largeBounds.MinX + 1);
        Assert.AreEqual(large, Network(normal), "Large valid ranges must not overflow the overlap test.");
    }

    [TestCase("missing")]
    [TestCase("fraction")]
    [TestCase("axis")]
    [TestCase("overflow")]
    public void InvalidRangeJson_RejectsBeforePublishingAnyRows(string fault)
    {
        var json = JObject.Parse(Resources.Load<TextAsset>(BuildingConfigLoader.DefaultResourcePath).text);
        var station = (JObject)((JArray)json["buildings"]).First(e => (string)e["buildingType"] == "DroneStation");
        if (fault == "missing") station.Remove("droneStationRangeInChunks");
        else if (fault == "fraction") station["droneStationRangeInChunks"] = 1.5;
        else if (fault == "axis") station["droneStationRangeInChunksX"] = 1;
        else station["droneStationRangeInChunks"] = (long)int.MaxValue + 1;
        LogAssert.Expect(LogType.Error, new Regex("BuildingConfigLoader.*(droneStationRangeInChunks|single)"));
        Assert.IsFalse(BuildingConfigLoader.TryParseJson(json.ToString(), out var configs, out var materials));
        Assert.IsNull(configs);
        Assert.IsNull(materials);
    }

    private Entity CreateStation(BuildingTypeEnum type, int2 position, ulong order,
        DirectionEnum direction = DirectionEnum.Up, int tier = 1)
    {
        var configs = _entityManager.GetBuffer<BuildingConfigElement>(_config);
        Assert.IsTrue(BuildingConfigLookupUtility.TryGetConfig(configs, type, tier, out var config));
        Entity entity = Entities.CreateBuilding(type, position, config.Footprint, direction);
        _entityManager.SetComponentData(entity, new BuildingType(type, tier));
        _entityManager.AddComponentData(entity, new PlacementStamp(order, 0));
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        BuildingLifecycleUtility.AttachTypeSpecificComponents(ref ecb, entity, type, direction, config.Speed, config.StorageCapacity);
        ecb.Playback(_entityManager);
        ecb.Dispose();
        return entity;
    }

    private void SetStationConfig(BuildingTypeEnum type, int tier, int2 footprint, int range)
    {
        var configs = _entityManager.GetBuffer<BuildingConfigElement>(_config);
        int index = -1;
        for (int i = 0; i < configs.Length; i++) if (configs[i].BuildingType == type && configs[i].Tier == tier) index = i;
        var row = index < 0
            ? new BuildingConfigElement(type, tier, "Test station", 0, 20, true, footprint: footprint, droneStationStorageCapacity: 5)
            : configs[index];
        row.Footprint = footprint;
        row.DroneStationRangeInChunks = range;
        if (index < 0) configs.Add(row);
        else configs[index] = row;
    }

    private Entity Network(Entity station) => _entityManager.GetComponentData<DroneStationNetwork>(station).RepresentativeStation;
    private void RunTick()
    {
        Simulation.UpdateAndComplete(_decision);
        PublishAndSync();
    }
    private void PublishAndSync()
    {
        Simulation.Update(_apply);
        _endSimulation.Update();
        Simulation.UpdateAndComplete(_sync);
    }
    private DroneStationSpatialIndex ReadIndex()
    {
        using var query = _entityManager.CreateEntityQuery(typeof(DroneStationSpatialIndex));
        Entity entity = query.GetSingletonEntity();
        var fence = _entityManager.GetComponentData<DroneStationSpatialIndexFence>(entity);
        fence.Complete();
        _entityManager.SetComponentData(entity, fence);
        return _entityManager.GetComponentData<DroneStationSpatialIndex>(entity);
    }
    private bool Covers(Entity network, int2 position) => DroneStationSpatialIndex.Contains(ReadIndex().AsReadOnly(), network, position);
}
