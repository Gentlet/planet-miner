using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

public class DroneTaskCandidateIndexTests : EcsWorldTestFixture
{
    private const int NetworkId = 7;

    [Test]
    public void EmergencyCandidateWinsOverHigherNormalPriority()
    {
        Entity normalEntity = _entityManager.CreateEntity();
        Entity emergencyEntity = _entityManager.CreateEntity();
        DroneTaskCandidateIndex index = new();

        index.BeginGeneration();
        index.AddOrRefresh(CreateCandidate(
            normalEntity,
            new int2(1, 0),
            DroneTaskPriorityClassEnum.Normal,
            DroneTaskPriorityUtility.MinimumNormalPriority,
            1));
        index.AddOrRefresh(CreateCandidate(
            emergencyEntity,
            new int2(4, 0),
            DroneTaskPriorityClassEnum.Emergency,
            DroneTaskPriorityUtility.MaximumNormalPriority,
            2));
        index.FinalizeCandidates();

        bool found = TryFind(index, 100f, out DroneScheduledCandidate result);

        Assert.That(found, Is.True);
        Assert.That(result.candidateEntity, Is.EqualTo(emergencyEntity));
    }

    [Test]
    public void EqualDistanceUsesEarlierCreationOrder()
    {
        Entity laterEntity = _entityManager.CreateEntity();
        Entity earlierEntity = _entityManager.CreateEntity();
        DroneTaskCandidateIndex index = new();

        index.BeginGeneration();
        index.AddOrRefresh(CreateCandidate(
            laterEntity,
            new int2(0, 2),
            DroneTaskPriorityClassEnum.Normal,
            5,
            20));
        index.AddOrRefresh(CreateCandidate(
            earlierEntity,
            new int2(2, 0),
            DroneTaskPriorityClassEnum.Normal,
            5,
            10));
        index.FinalizeCandidates();

        bool found = TryFind(index, 100f, out DroneScheduledCandidate result);

        Assert.That(found, Is.True);
        Assert.That(result.candidateEntity, Is.EqualTo(earlierEntity));
    }

    [Test]
    public void InfeasibleHighPriorityFallsBackToFeasibleLowerPriority()
    {
        Entity highPriorityEntity = _entityManager.CreateEntity();
        Entity feasibleEntity = _entityManager.CreateEntity();
        DroneTaskCandidateIndex index = new();

        index.BeginGeneration();
        index.AddOrRefresh(CreateCandidate(
            highPriorityEntity,
            new int2(10, 0),
            DroneTaskPriorityClassEnum.Normal,
            1,
            1));
        index.AddOrRefresh(CreateCandidate(
            feasibleEntity,
            new int2(2, 0),
            DroneTaskPriorityClassEnum.Normal,
            2,
            2));
        index.FinalizeCandidates();

        bool found = TryFind(index, 5f, out DroneScheduledCandidate result);

        Assert.That(found, Is.True);
        Assert.That(result.candidateEntity, Is.EqualTo(feasibleEntity));
    }

    [Test]
    public void RemovedAndStaleCandidatesCannotBeSelectedAgain()
    {
        Entity removedEntity = _entityManager.CreateEntity();
        Entity staleEntity = _entityManager.CreateEntity();
        DroneTaskCandidateIndex index = new();

        index.BeginGeneration();
        index.AddOrRefresh(CreateCandidate(
            removedEntity,
            new int2(1, 0),
            DroneTaskPriorityClassEnum.Normal,
            1,
            1));
        index.AddOrRefresh(CreateCandidate(
            staleEntity,
            new int2(2, 0),
            DroneTaskPriorityClassEnum.Normal,
            2,
            2));
        index.FinalizeCandidates();
        index.Remove(removedEntity);

        index.BeginGeneration();
        index.RemoveStaleCandidates();
        index.FinalizeCandidates();

        Assert.That(
            TryFind(index, 100f, out _),
            Is.False);
    }

    private static DroneScheduledCandidate CreateCandidate(
        Entity entity,
        int2 workCell,
        DroneTaskPriorityClassEnum priorityClass,
        int normalPriority,
        ulong creationOrder)
    {
        return new DroneScheduledCandidate
        {
            kind = DroneScheduledCandidateKind.DirectTask,
            candidateEntity = entity,
            taskEntity = entity,
            networkId = NetworkId,
            workCell = workCell,
            secondWaypointCell = workCell,
            returnStationCell = workCell,
            quantity = 1,
            priority = new DroneTaskPriority
            {
                priorityClass = priorityClass,
                normalPriority = normalPriority
            },
            creationOrder = creationOrder
        };
    }

    private static bool TryFind(
        DroneTaskCandidateIndex index,
        float currentBattery,
        out DroneScheduledCandidate result)
    {
        return index.TryFindNearestFeasible(
            NetworkId,
            int2.zero,
            new DroneBattery
            {
                current = currentBattery,
                maximum = 100f,
                consumptionPerDistance = 1f
            },
            10,
            out result);
    }
}
