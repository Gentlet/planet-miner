using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 현장 자재 요구 버퍼의 예약 가능량 계산과 ReservedQuantity 확보/해제를 공유한다.
/// 입력·출력: 품목별 요구·도착·예약 행을 읽으며 Reserve는 실제 확보량을 반환한다. RemainingIncludingOwn은 자신의 예약을 제외해 수령 가능한 잔여량을 계산한다.
/// 이용: ConstructionSupplyReservationSystem, DroneTaskAssignmentPublishSystem, DroneTaskLifecycleApplySystem의 수량 정산 경계가 호출한다.
/// 수명·소유권: 현장 수량만 변경한다. 개별 배정/미공개 후보 기록과의 대응은 호출자가 유지하며 공급원 재고·보관 공간·실물은 예약하지 않는다.
/// </summary>
public static class ConstructionSupplyReservationUtility
{
    public static int Remaining(EntityManager manager, Entity site, ItemTypeEnum type)
    {
        if (!DroneSchedulingUtility.IsSite(manager, site)) return 0;
        var rows = manager.GetBuffer<ConstructionMaterialRequirementElement>(site, true);
        int remaining = 0;
        for (int i = 0; i < rows.Length; i++)
        {
            if (rows[i].ItemType == type) remaining += rows[i].RemainingToReserve;
        }
        return remaining;
    }

    public static int Reserve(EntityManager manager, Entity site, ItemTypeEnum type, int quantity)
    {
        if (!DroneSchedulingUtility.IsSite(manager, site)) return 0;
        var rows = manager.GetBuffer<ConstructionMaterialRequirementElement>(site);
        int remaining = math.max(0, quantity);
        for (int i = 0; i < rows.Length && remaining > 0; i++)
        {
            var row = rows[i];
            if (row.ItemType != type) continue;
            int amount = math.min(remaining, row.RemainingToReserve);
            row.ReservedQuantity += amount;
            rows[i] = row;
            remaining -= amount;
        }
        return math.max(0, quantity) - remaining;
    }

    /// <summary>현재 배정의 예약은 자신을 막지 않도록 제외하되 다른 배정의 예약/실제 도착량은 남긴 잔여 요구량.</summary>
    public static int RemainingIncludingOwn(EntityManager manager, Entity site, ItemTypeEnum type, int ownQuantity)
    {
        if (!DroneSchedulingUtility.IsSite(manager, site)) return 0;
        var rows = manager.GetBuffer<ConstructionMaterialRequirementElement>(site, true);
        int required = 0;
        int delivered = 0;
        int reserved = 0;
        for (int i = 0; i < rows.Length; i++)
        {
            if (rows[i].ItemType != type) continue;
            required += rows[i].RequiredQuantity;
            delivered += rows[i].DeliveredQuantity;
            reserved += rows[i].ReservedQuantity;
        }
        return math.max(0, required - delivered - math.max(0, reserved - ownQuantity));
    }

    public static void Release(EntityManager manager, Entity site, ItemTypeEnum type, int quantity)
    {
        if (site == Entity.Null) return;
        if (!manager.Exists(site)) return;
        if (!manager.HasBuffer<ConstructionMaterialRequirementElement>(site)) return;
        var rows = manager.GetBuffer<ConstructionMaterialRequirementElement>(site);
        int remaining = math.max(0, quantity);
        for (int i = 0; i < rows.Length && remaining > 0; i++)
        {
            var row = rows[i];
            if (row.ItemType != type) continue;
            int amount = math.min(remaining, math.max(0, row.ReservedQuantity));
            row.ReservedQuantity -= amount;
            rows[i] = row;
            remaining -= amount;
        }
    }
}
