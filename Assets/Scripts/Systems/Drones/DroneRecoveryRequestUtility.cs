using Unity.Entities;

public static class DroneRecoveryRequestUtility
{
    public static void Request(
        EntityManager entityManager,
        Entity droneEntity,
        DroneRecoveryReasonEnum reason)
    {
        if (entityManager.HasComponent<DroneRecoveryRequest>(droneEntity))
        {
            entityManager.SetComponentData(
                droneEntity,
                new DroneRecoveryRequest { reason = reason });
            return;
        }

        entityManager.AddComponentData(
            droneEntity,
            new DroneRecoveryRequest { reason = reason });
    }
}
