using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 드론 행동의 식별번호·배정 revision·수행자 관측·출처/목적지 자격을 공통으로 검사한다.
/// 입력·출력: 행동 요청과 현재 배정/예약/실물을 읽어 처리 가능 여부와 배정 값을 반환한다. 실물/상태/수량은 변경하지 않는다.
/// 이용: DroneItemTransferDecisionSystem의 앞단 자격 판단과 DroneTaskLifecycleApplySystem의 최종 재검사가 공유한다.
/// 수명·경계: 검사 성공이 인계 성공은 아니다. 실물 ID/수량/공간 계획과 실제 성공분 정산은 Execution/StateApply의 각 소유자가 담당한다.
/// </summary>
public static class DroneItemTransferValidationUtility
{
    public static bool TryValidate(EntityManager manager, in DroneActionReadyRequest request,
        out DroneTaskAssignment assignment)
    {
        assignment = default;
        var action = request.Action;
        if (request.ReceiptSequence == 0 || action.AssignmentRevision == 0 || action.Sequence == 0) return false;
        if (!DroneSchedulingUtility.IsActiveEntity(manager, action.Assignment)) return false;
        if (!manager.HasComponent<DroneTaskAssignment>(action.Assignment)) return false;
        assignment = manager.GetComponentData<DroneTaskAssignment>(action.Assignment);
        if (assignment.Revision != action.AssignmentRevision || action.Sequence <= assignment.LastAppliedActionSequence) return false;
        if (assignment.Worker != action.Worker || assignment.ItemType == ItemTypeEnum.None || assignment.AssignedQuantity <= 0) return false;
        if (assignment.NextAction != action.Kind || action.Kind == DroneActionKindEnum.None ||
            (byte)action.Kind >= (byte)DroneActionKindEnum.Count) return false;
        if (!DroneSchedulingUtility.IsActiveEntity(manager, action.Worker)) return false;
        if (!manager.HasComponent<DroneWorker>(action.Worker)) return false;
        if (!manager.HasComponent<DroneWorkerAssignment>(action.Worker)) return false;
        if (!manager.HasComponent<DroneWorkerObservation>(action.Worker)) return false;
        if (!manager.HasComponent<DroneCargoState>(action.Worker)) return false;
        if (!manager.HasBuffer<StoredItemElement>(action.Worker)) return false;
        if (manager.GetComponentData<DroneWorkerAssignment>(action.Worker).Assignment != action.Assignment) return false;
        var observation = manager.GetComponentData<DroneWorkerObservation>(action.Worker);
        if (request.WorkerObservationRevision == 0 || observation.Revision != request.WorkerObservationRevision ||
            !math.all(math.isfinite(observation.Position))) return false;
        // TODO(SoT 연결): 실제 위치 원본을 연결할 때 관측 갱신 경계와 행동 도착 조건도 검증한다.
        // 현재 관측 revision 대조는 실제 이동/도착 검증을 대신하지 않는다.
        if (!DroneItemTransferUtility.CargoHasSingleType(manager, action.Worker, assignment.ItemType)) return false;
        var cargo = manager.GetBuffer<StoredItemElement>(action.Worker, true);
        var cargoOrigin = manager.GetComponentData<DroneCargoState>(action.Worker).Origin;
        if (!cargo.IsEmpty && cargoOrigin != DroneCargoOriginEnum.Supply && cargoOrigin != DroneCargoOriginEnum.Recovery) return false;
        if (manager.HasComponent<ConstructionSupplyReservation>(action.Assignment))
        {
            var reservation = manager.GetComponentData<ConstructionSupplyReservation>(action.Assignment);
            if (reservation.AssignmentRevision != assignment.Revision || reservation.ItemType != assignment.ItemType ||
                reservation.RemainingQuantity < 0) return false;
        }
        // 수집은 아직 열린 상위 작업과 빈 적재를 요구한다. 이미 적재한 화물의 보관/재배정은 그 작업이 닫혀도 별도 자격으로 검사한다.
        bool collection = action.Kind == DroneActionKindEnum.CollectFromStorage || action.Kind == DroneActionKindEnum.RecoverWorldItem;
        if (collection)
        {
            if (action.Target != assignment.Source) return false;
            if (assignment.State != DroneTaskAssignmentStateEnum.MovingToSource && assignment.State != DroneTaskAssignmentStateEnum.AwaitingCollection) return false;
            if (!manager.GetBuffer<StoredItemElement>(action.Worker, true).IsEmpty) return false;
            if (manager.GetComponentData<DroneCargoState>(action.Worker).Origin != DroneCargoOriginEnum.None) return false;
            if (!manager.HasComponent<DroneLogisticsTask>(assignment.Task)) return false;
            var task = manager.GetComponentData<DroneLogisticsTask>(assignment.Task);
            if (task.State != DroneLogisticsTaskStateEnum.Open || task.ItemType != assignment.ItemType) return false;
            if (action.Kind == DroneActionKindEnum.CollectFromStorage)
            {
                if (task.Kind != DroneLogisticsTaskKindEnum.ConstructionSupply || task.Target != assignment.Destination) return false;
                if (!ReservationMatches(manager, action.Assignment, assignment)) return false;
            }
            else if (task.Kind != DroneLogisticsTaskKindEnum.WorldItemRecovery || task.Target != assignment.Source) return false;
        }
        else if (action.Kind == DroneActionKindEnum.DropCargo)
        {
            if (action.Target != Entity.Null) return false;
            if (assignment.State != DroneTaskAssignmentStateEnum.Retargeting &&
                assignment.State != DroneTaskAssignmentStateEnum.MovingToDestination &&
                assignment.State != DroneTaskAssignmentStateEnum.AwaitingDelivery) return false;
            if (!math.all(math.floor(observation.Position.xy) == (float2)request.WorldPosition)) return false;
            if (!math.all(assignment.DropPosition == request.WorldPosition)) return false;
            if (!ConstructionSiteWorldItemUtility.IsWorldPositionAllowed(manager, request.WorldPosition)) return false;
        }
        else
        {
            if (action.Target != assignment.Destination) return false;
            if (assignment.State != DroneTaskAssignmentStateEnum.MovingToDestination && assignment.State != DroneTaskAssignmentStateEnum.AwaitingDelivery) return false;
            if (action.Kind == DroneActionKindEnum.SupplyConstructionSite)
            {
                if (manager.GetComponentData<DroneCargoState>(action.Worker).Origin != DroneCargoOriginEnum.Supply) return false;
                if (!ReservationMatches(manager, action.Assignment, assignment)) return false;
            }
        }
        return true;
    }

    private static bool ReservationMatches(EntityManager manager, Entity entity, in DroneTaskAssignment assignment)
    {
        if (!manager.HasComponent<ConstructionSupplyReservation>(entity)) return false;
        var reservation = manager.GetComponentData<ConstructionSupplyReservation>(entity);
        return reservation.Site == assignment.Destination && reservation.ItemType == assignment.ItemType &&
               reservation.AssignmentRevision == assignment.Revision && reservation.RemainingQuantity >= 0;
    }

}
