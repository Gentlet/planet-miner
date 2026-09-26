using NUnit.Framework;
using PlanetMiner.Tests;
using Unity.Entities;
using Unity.Mathematics;

public class Phase6RoutingContractTests : EcsWorldTestFixture
{
    [Test]
    public void Test01_BuildingTypes_IncludeSplitterAndMerger()
    {
        Assert.AreNotEqual(BuildingTypeEnum.None, BuildingTypeEnum.Splitter);
        Assert.AreNotEqual(BuildingTypeEnum.None, BuildingTypeEnum.Merger);
        Assert.Less((byte)BuildingTypeEnum.Splitter, (byte)BuildingTypeEnum.Count);
        Assert.Less((byte)BuildingTypeEnum.Merger, (byte)BuildingTypeEnum.Count);
    }

    [TestCase(DirectionEnum.Up, DirectionEnum.Up, DirectionEnum.Right, DirectionEnum.Left)]
    [TestCase(DirectionEnum.Right, DirectionEnum.Right, DirectionEnum.Down, DirectionEnum.Up)]
    [TestCase(DirectionEnum.Down, DirectionEnum.Down, DirectionEnum.Left, DirectionEnum.Right)]
    [TestCase(DirectionEnum.Left, DirectionEnum.Left, DirectionEnum.Up, DirectionEnum.Down)]
    public void Test02_SplitterPortOrder_IsForwardRightLeft(
        DirectionEnum forward,
        DirectionEnum expected0,
        DirectionEnum expected1,
        DirectionEnum expected2)
    {
        Assert.AreEqual(expected0, RoutingDirectionUtility.GetSplitterOutput(forward, 0));
        Assert.AreEqual(expected1, RoutingDirectionUtility.GetSplitterOutput(forward, 1));
        Assert.AreEqual(expected2, RoutingDirectionUtility.GetSplitterOutput(forward, 2));
        Assert.AreEqual(expected0, RoutingDirectionUtility.GetSplitterOutput(forward, 3));
    }

    [TestCase(DirectionEnum.Up, DirectionEnum.Down, DirectionEnum.Left, DirectionEnum.Right)]
    [TestCase(DirectionEnum.Right, DirectionEnum.Left, DirectionEnum.Up, DirectionEnum.Down)]
    [TestCase(DirectionEnum.Down, DirectionEnum.Up, DirectionEnum.Right, DirectionEnum.Left)]
    [TestCase(DirectionEnum.Left, DirectionEnum.Right, DirectionEnum.Down, DirectionEnum.Up)]
    public void Test03_MergerPortOrder_IsBackLeftRight(
        DirectionEnum forward,
        DirectionEnum expected0,
        DirectionEnum expected1,
        DirectionEnum expected2)
    {
        Assert.AreEqual(expected0, RoutingDirectionUtility.GetMergerInput(forward, 0));
        Assert.AreEqual(expected1, RoutingDirectionUtility.GetMergerInput(forward, 1));
        Assert.AreEqual(expected2, RoutingDirectionUtility.GetMergerInput(forward, 2));
        Assert.AreEqual(expected0, RoutingDirectionUtility.GetMergerInput(forward, 3));
    }

    [Test]
    public void Test04_BeltConnectionDirection_DistinguishesInputAndOutput()
    {
        var router = int2.zero;

        Assert.IsTrue(RoutingDirectionUtility.IsIncomingBelt(
            router,
            new int2(-1, 0),
            DirectionEnum.Right));
        Assert.IsFalse(RoutingDirectionUtility.IsOutgoingBelt(
            router,
            new int2(-1, 0),
            DirectionEnum.Right));

        Assert.IsTrue(RoutingDirectionUtility.IsOutgoingBelt(
            router,
            new int2(1, 0),
            DirectionEnum.Right));
        Assert.IsFalse(RoutingDirectionUtility.IsIncomingBelt(
            router,
            new int2(1, 0),
            DirectionEnum.Right));
    }

    [Test]
    public void Test05_RoutingState_PreservesAnchorDirectionAndCursor()
    {
        var inputBelt = _entityManager.CreateEntity();
        var outputBelt = _entityManager.CreateEntity();

        var splitter = new SplitterRoutingState(inputBelt, DirectionEnum.Right, outputCursor: 2);
        var merger = new MergerRoutingState(outputBelt, DirectionEnum.Up, inputCursor: 1);

        Assert.AreEqual(inputBelt, splitter.InputBelt);
        Assert.AreEqual(DirectionEnum.Right, splitter.ForwardDirection);
        Assert.AreEqual(2, splitter.OutputCursor);

        Assert.AreEqual(outputBelt, merger.OutputBelt);
        Assert.AreEqual(DirectionEnum.Up, merger.ForwardDirection);
        Assert.AreEqual(1, merger.InputCursor);

        Assert.AreEqual(0, RoutingDirectionUtility.AdvanceCursor(2));
    }

    [Test]
    public void Test06_RoutingTransferDecision_EnableableFrameContract()
    {
        var router = _entityManager.CreateEntity(typeof(RoutingTransferDecision));
        var item = _entityManager.CreateEntity();
        var source = _entityManager.CreateEntity();
        var target = _entityManager.CreateEntity();

        _entityManager.SetComponentData(
            router,
            new RoutingTransferDecision(item, source, target));

        var decision = _entityManager.GetComponentData<RoutingTransferDecision>(router);
        Assert.AreEqual(item, decision.Item);
        Assert.AreEqual(source, decision.SourceBelt);
        Assert.AreEqual(target, decision.TargetBelt);

        _entityManager.SetComponentEnabled<RoutingTransferDecision>(router, false);
        var enabledQuery = _entityManager.CreateEntityQuery(typeof(RoutingTransferDecision));
        Assert.AreEqual(0, enabledQuery.CalculateEntityCount());

        _entityManager.SetComponentEnabled<RoutingTransferDecision>(router, true);
        Assert.AreEqual(1, enabledQuery.CalculateEntityCount());
    }

    [Test]
    public void Test07_PortIndexReverseCalculation_CalculatesPortFromDirection()
    {
        // Splitter (Forward = Right): Forward(Right)=0, Right(Down)=1, Left(Up)=2
        Assert.AreEqual(0, RoutingDirectionUtility.GetSplitterPortIndex(DirectionEnum.Right, DirectionEnum.Right));
        Assert.AreEqual(1, RoutingDirectionUtility.GetSplitterPortIndex(DirectionEnum.Right, DirectionEnum.Down));
        Assert.AreEqual(2, RoutingDirectionUtility.GetSplitterPortIndex(DirectionEnum.Right, DirectionEnum.Up));

        // Merger (Forward = Right): Back(Left)=0, Left(Up)=1, Right(Down)=2
        Assert.AreEqual(0, RoutingDirectionUtility.GetMergerPortIndex(DirectionEnum.Right, DirectionEnum.Left));
        Assert.AreEqual(1, RoutingDirectionUtility.GetMergerPortIndex(DirectionEnum.Right, DirectionEnum.Up));
        Assert.AreEqual(2, RoutingDirectionUtility.GetMergerPortIndex(DirectionEnum.Right, DirectionEnum.Down));

        // TryGetDirection 검증
        Assert.IsTrue(RoutingDirectionUtility.TryGetDirection(new int2(1, 1), new int2(2, 1), out var dirRight));
        Assert.AreEqual(DirectionEnum.Right, dirRight);

        Assert.IsTrue(RoutingDirectionUtility.TryGetDirection(new int2(1, 1), new int2(1, 2), out var dirUp));
        Assert.AreEqual(DirectionEnum.Up, dirUp);
    }

    [Test]
    public void Test08_PlacementStamp_OrdersByTickThenRequestOrder()
    {
        var firstTickFirst = new PlacementStamp(10, 0);
        var firstTickSecond = new PlacementStamp(10, 1);
        var laterTick = new PlacementStamp(11, 0);

        Assert.IsTrue(RoutingDirectionUtility.IsEarlierPlacement(firstTickFirst, firstTickSecond));
        Assert.IsTrue(RoutingDirectionUtility.IsEarlierPlacement(firstTickSecond, laterTick));
        Assert.IsFalse(RoutingDirectionUtility.IsEarlierPlacement(laterTick, firstTickFirst));
        Assert.IsFalse(RoutingDirectionUtility.IsEarlierPlacement(firstTickSecond, firstTickFirst));
    }
}
