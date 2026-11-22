using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 작업별 목표량 확보와 실제 납품 정산을 공유한다. 호출자는 기록의 Task에서 Progress를 읽고 함께 저장한다.
/// 실제 인계 소유자만 보관 성공량을 전달하며 행동 번호/버전 중복 검사는 그 소유자가 담당한다.
/// </summary>
public static class DroneTransportReservationUtility
{
    public static int ProjectedRemaining(in DroneTransportRequirement requirement,
        in DroneTransportProgress progress, long returningQuantity)
    {
        long remaining = (long)requirement.Quantity - progress.DeliveredQuantity - progress.ReservedQuantity + returningQuantity;
        return (int)math.max(0L, remaining);
    }

    public static bool IsCompleted(in DroneTransportRequirement requirement, in DroneTransportProgress progress)
    {
        return progress.DeliveredQuantity == requirement.Quantity && progress.ReservedQuantity == 0;
    }

    public static bool ShouldRelease(Entity owner, bool hasAssignment,
        DynamicBuffer<DroneTaskInvalidationDecisionElement> decisions)
    {
        if (!hasAssignment)
        {
            return true;
        }
        for (int i = 0; i < decisions.Length; i++)
        {
            if (IsReleaseDecision(decisions[i].Kind) && decisions[i].Target == owner)
            {
                return true;
            }
        }
        return false;
    }

    // 조회 단계의 반환 예정량이다. 배정과 예약의 원본은 Reservation Job이 정산한다.
    public static void CollectReturningQuantities(EntityManager manager, NativeArray<Entity> owners,
        DynamicBuffer<DroneTransportPendingReservationElement> pending, DynamicBuffer<DroneTaskInvalidationDecisionElement> decisions,
        NativeHashMap<Entity, long> quantities)
    {
        using var endingOwners = new NativeHashSet<Entity>(decisions.Length, Allocator.Temp);
        foreach (var decision in decisions)
        {
            if (IsReleaseDecision(decision.Kind)) endingOwners.Add(decision.Target);
        }

        foreach (Entity owner in owners)
        {
            var reservation = manager.GetComponentData<DroneTransportReservation>(owner);
            if (reservation.RemainingQuantity == 0) continue;
            if (endingOwners.Contains(owner) || !manager.HasComponent<DroneTaskAssignment>(owner))
                AddReturningQuantity(quantities, reservation);
        }

        // 이전에 확보했으나 인계하지 못한 수량도 이번 선택에 다시 사용할 수 있다.
        foreach (var approval in pending)
        {
            if (approval.Reservation.RemainingQuantity != 0)
                AddReturningQuantity(quantities, approval.Reservation);
        }
    }

    private static bool IsReleaseDecision(DroneTaskInvalidationDecisionKindEnum kind) =>
        kind == DroneTaskInvalidationDecisionKindEnum.InvalidateAssignment ||
        kind == DroneTaskInvalidationDecisionKindEnum.CancelAssignment;

    private static void AddReturningQuantity(NativeHashMap<Entity, long> quantities, in DroneTransportReservation reservation)
    {
        quantities.TryGetValue(reservation.Task, out long previouslyReturning);
        quantities[reservation.Task] = previouslyReturning + reservation.RemainingQuantity;
    }

    public static bool TryReserve(Entity task, in DroneTransportRequirement requirement,
        ref DroneTransportProgress progress, int maximumQuantity, out DroneTransportReservation reservation)
    {
        reservation = default;
        long remaining = (long)requirement.Quantity - progress.DeliveredQuantity - progress.ReservedQuantity;
        int quantity = (int)math.min((long)maximumQuantity, remaining);
        if (quantity <= 0)
        {
            return false;
        }
        progress.ReservedQuantity += quantity;
        reservation = new DroneTransportReservation { Task = task, RemainingQuantity = quantity };
        return true;
    }

    public static void Release(ref DroneTransportProgress progress, ref DroneTransportReservation reservation)
    {
        progress.ReservedQuantity -= reservation.RemainingQuantity;
        reservation.RemainingQuantity = 0;
    }

    public static bool TryReduce(ref DroneTransportProgress progress, ref DroneTransportReservation reservation,
        int retainedQuantity)
    {
        if (retainedQuantity < 0 || retainedQuantity > reservation.RemainingQuantity)
        {
            return false;
        }
        progress.ReservedQuantity -= reservation.RemainingQuantity - retainedQuantity;
        reservation.RemainingQuantity = retainedQuantity;
        return true;
    }

    public static bool TryRecordDelivery(ref DroneTransportProgress progress, ref DroneTransportReservation reservation,
        int deliveredQuantity)
    {
        if (deliveredQuantity < 0 || deliveredQuantity > reservation.RemainingQuantity)
        {
            return false;
        }
        progress.DeliveredQuantity += deliveredQuantity;
        progress.ReservedQuantity -= deliveredQuantity;
        reservation.RemainingQuantity -= deliveredQuantity;
        return true;
    }

    public static DroneTransportReservation Take(ref DroneTransportPendingReservationElement pending)
    {
        var reservation = pending.Reservation;
        pending.Reservation = default;
        return reservation;
    }
}
