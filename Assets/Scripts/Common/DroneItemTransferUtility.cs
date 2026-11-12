using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할/목적: 드론 수집 상한, 적재 품목 검사, 현장 도착/개별 예약 수량 정산을 공유한다.
/// 이용: DroneTaskLifecycleApplySystem은 성공한 실물 수량으로 RecordDelivered/ReduceReservation을 호출한다. 실물 Owner/버퍼 이전은 Ownership 소유자가 담당한다.
/// 수명·경계: 새 요청·캐시·엔티티를 만들지 않는다. 현장 합계와 배정의 개별 기록은 같이 줄이고 예약량만으로 도착한 것으로 기록하지 않는다.
/// </summary>
public static class DroneItemTransferUtility
{
    public static int CollectionLimit(EntityManager manager, Entity entity, in DroneTaskAssignment assignment)
    {
        if (!DroneTransportEndpointUtility.IsArea(manager, assignment.Source, assignment.SourceArea))
        {
            return 0;
        }
        if (manager.HasComponent<DroneTransportReservation>(entity))
        {
            if (!DroneTransportEndpointUtility.IsArea(manager, assignment.Destination, assignment.DestinationArea))
            {
                return 0;
            }
            return math.min(assignment.AssignedQuantity,
                manager.GetComponentData<DroneTransportReservation>(entity).RemainingQuantity);
        }
        if (!DroneSchedulingUtility.IsSite(manager, assignment.Destination))
        {
            return 0;
        }
        var reservation = manager.GetComponentData<ConstructionSupplyReservation>(entity);
        int limit = math.min(assignment.AssignedQuantity, reservation.RemainingQuantity);
        return math.min(limit, ConstructionSupplyReservationUtility.RemainingIncludingOwn(manager,
            assignment.Destination, assignment.ItemType, reservation.RemainingQuantity));
    }

    public static bool CargoHasSingleType(EntityManager manager, Entity worker, ItemTypeEnum type)
    {
        var cargo = manager.GetBuffer<StoredItemElement>(worker, true);
        for (int i = 0; i < cargo.Length; i++)
        {
            if (cargo[i].ItemType != type) return false;
            Entity item = cargo[i].ItemEntity;
            if (!manager.Exists(item)) return false;
            if (!manager.HasComponent<ItemIdentity>(item)) return false;
            if (manager.GetComponentData<ItemIdentity>(item).Type != type) return false;
        }
        return true;
    }

    public static void ReduceReservation(EntityManager manager, Entity assignmentEntity,
        ref ConstructionSupplyReservation reservation, int remaining)
    {
        // 실물 수집 부족/공급 성공으로 줄어드는 양만 정산한다. 개별 잔량을 늘리거나 합계만 줄여 기록을 분리하지 않는다.
        int next = math.max(0, math.min(reservation.RemainingQuantity, remaining));
        ConstructionSupplyReservationUtility.Release(manager, reservation.Site, reservation.ItemType,
            reservation.RemainingQuantity - next);
        reservation.RemainingQuantity = next;
        manager.SetComponentData(assignmentEntity, reservation);
    }

    public static void RecordDelivered(EntityManager manager, Entity site, ItemTypeEnum type, int quantity)
    {
        var rows = manager.GetBuffer<ConstructionMaterialRequirementElement>(site);
        int remaining = quantity;
        for (int i = 0; i < rows.Length && remaining > 0; i++)
        {
            var row = rows[i];
            if (row.ItemType != type) continue;
            int amount = math.min(remaining, row.RemainingRequired);
            row.DeliveredQuantity += amount;
            rows[i] = row;
            remaining -= amount;
        }
    }
}
