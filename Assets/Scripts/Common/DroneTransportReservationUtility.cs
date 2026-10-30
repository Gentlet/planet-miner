using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 작업별 목표량 확보와 실제 납품 정산을 공유한다. 호출자는 기록의 Task에서 Progress를 읽고 함께 저장한다.
/// 실제 인계 소유자만 보관 성공량을 전달하며 행동 번호/버전 중복 검사는 그 소유자가 담당한다.
/// </summary>
public static class DroneTransportReservationUtility
{
    public static int ProjectedRemaining(Entity task, in DroneTransportRequirement requirement,
        in DroneTransportProgress progress, DynamicBuffer<DroneTransportPendingReservationElement> pending,
        int releasingQuantity = 0)
    {
        long remaining = (long)requirement.Quantity - progress.DeliveredQuantity - progress.ReservedQuantity + releasingQuantity;
        for (int i = 0; i < pending.Length; i++)
        {
            if (pending[i].Reservation.Task == task)
            {
                // 다음 Reservation이 반환할 미인계 승인량은 이번 Decision에서 다시 계획할 수 있다.
                remaining += pending[i].Reservation.RemainingQuantity;
            }
        }
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
            bool ending = decisions[i].Kind == DroneTaskInvalidationDecisionKindEnum.InvalidateAssignment ||
                decisions[i].Kind == DroneTaskInvalidationDecisionKindEnum.CancelAssignment;
            if (ending && decisions[i].Target == owner)
            {
                return true;
            }
        }
        return false;
    }

    public static int ReadReleasingQuantity(EntityManager manager, Entity task, NativeArray<Entity> owners,
        DynamicBuffer<DroneTaskInvalidationDecisionElement> decisions)
    {
        int quantity = 0;
        for (int i = 0; i < owners.Length; i++)
        {
            var reservation = manager.GetComponentData<DroneTransportReservation>(owners[i]);
            if (reservation.Task == task && ShouldRelease(owners[i], manager.HasComponent<DroneTaskAssignment>(owners[i]), decisions))
            {
                quantity += reservation.RemainingQuantity;
            }
        }
        return quantity;
    }

    public static int ReadInFlightQuantity(Entity task, in DroneTransportProgress progress,
        DynamicBuffer<DroneTransportPendingReservationElement> pending)
    {
        int quantity = progress.ReservedQuantity;
        for (int i = 0; i < pending.Length; i++)
        {
            if (pending[i].Reservation.Task == task)
            {
                quantity -= pending[i].Reservation.RemainingQuantity;
            }
        }
        return quantity;
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
