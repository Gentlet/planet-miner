using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 작업별 목표량 확보와 실제 납품 정산을 공유한다. 호출자는 기록의 Task에서 Progress를 읽고 함께 저장한다.
/// 실제 인계 소유자만 보관 성공량을 전달하며 행동 번호/버전 중복 검사는 그 소유자가 담당한다.
/// </summary>
public static class DroneTransportReservationUtility
{
    public static int ProjectedRemaining(Entity task, in DroneTransportRequirement requirement,
        in DroneTransportProgress progress, DynamicBuffer<DroneTransportPendingReservationElement> pending)
    {
        long remaining = (long)requirement.Quantity - progress.DeliveredQuantity - progress.ReservedQuantity;
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
