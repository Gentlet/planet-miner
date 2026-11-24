using Unity.Entities;

/// <summary>드론 데이터의 바이너리 필드 순서를 정의한다.</summary>
internal static class GameDroneBinaryLayout
{
    internal static void Fields(GameSnapshotArchive archive, ref DroneBatteryState value)
    {
        archive.Value(ref value.CurrentCharge);
        archive.Value(ref value.FractionalCharge);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneWorkerObservation value)
    {
        archive.Value(ref value.Position);
        archive.Value(ref value.Revision);
        archive.Value(ref value.CanAcceptTask);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneWorkerAssignment value)
    {
        archive.EntityReference(ref value.Assignment);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneCargoState value)
    {
        archive.EnumValue(ref value.ItemType);
        archive.Value(ref value.Quantity);
        archive.Value(ref value.BlockAutomaticRecoveryOnDrop);
        archive.EnumValue(ref value.Origin);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneActivityState value)
    {
        archive.EnumValue(ref value.Activity);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneMovementState value)
    {
        archive.EntityReference(ref value.ChargingStation);
        archive.EntityReference(ref value.ReturnStation);
        archive.Value(ref value.IsNormalReturn);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneMovementResult value)
    {
        archive.EntityReference(ref value.Assignment);
        archive.Value(ref value.AssignmentRevision);
        archive.EnumValue(ref value.Action);
        archive.Value(ref value.Arrived);
        archive.EnumValue(ref value.Failure);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneDocked value)
    {
        archive.EntityReference(ref value.Station);
        archive.Value(ref value.SlotIndex);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneStationRelocation value)
    {
        archive.EntityReference(ref value.Task);
        archive.Value(ref value.MemberIndex);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneLogisticsTask value)
    {
        archive.EnumValue(ref value.Kind);
        archive.EnumValue(ref value.State);
        archive.EntityReference(ref value.Target);
        archive.EnumValue(ref value.ItemType);
        archive.Value(ref value.CreationSequence);
        archive.EnumValue(ref value.Area);
        archive.EnumValue(ref value.Purpose);
        archive.EntityReference(ref value.DemolitionRoot);
        archive.EnumValue(ref value.RecoveryReason);
        archive.Value(ref value.HasExplicitRecoveryIntent);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneTransportRequirement value)
    {
        archive.Value(ref value.Quantity);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneTransportProgress value)
    {
        archive.Value(ref value.DeliveredQuantity);
        archive.Value(ref value.ReservedQuantity);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneDemolitionProgress value)
    {
        archive.Value(ref value.RemainingStock);
        archive.Value(ref value.RequiredWorkers);
        archive.Value(ref value.AssignedWorkers);
        archive.Value(ref value.ArrivedWorkers);
        archive.Value(ref value.AwaitingRelocation);
        archive.Value(ref value.AwaitingSpace);
    }

    internal static void Fields(GameSnapshotArchive archive, ref BuildingDemolitionExecutionRequest value)
    {
    }

    internal static void Fields(GameSnapshotArchive archive, ref BuildingDemolitionResult value)
    {
        archive.EnumValue(ref value.Status);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneDemolitionRefundElement value)
    {
        archive.EnumValue(ref value.ItemType);
        archive.Value(ref value.Quantity);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneStationRelocationMemberElement value)
    {
        archive.EntityReference(ref value.Drone);
        archive.Value(ref value.SourceSlotIndex);
        archive.Value(ref value.Finished);
        archive.EntityReference(ref value.Source);
        archive.EntityReference(ref value.Task);
        archive.EntityReference(ref value.Destination);
        archive.Value(ref value.SlotIndex);
        archive.EnumValue(ref value.Stage);
        archive.EntityReference(ref value.Request);
        archive.Value(ref value.DeparturePosition);
        archive.Value(ref value.HasDeparted);
    }

    internal static void Fields(GameSnapshotArchive archive, ref GridPosition value)
    {
        archive.Value(ref value.Value);
    }

    internal static void Fields(GameSnapshotArchive archive, ref BuildingFootprint value)
    {
        archive.Value(ref value.Size);
    }

    internal static void Fields(GameSnapshotArchive archive, ref Direction value)
    {
        archive.EnumValue(ref value.dir);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneTaskAssignment value)
    {
        archive.EntityReference(ref value.Worker);
        archive.EntityReference(ref value.Task);
        archive.Value(ref value.OriginalTaskCreationSequence);
        archive.EntityReference(ref value.Source);
        archive.EntityReference(ref value.Destination);
        archive.EnumValue(ref value.SourceArea);
        archive.EnumValue(ref value.DestinationArea);
        archive.EnumValue(ref value.ItemType);
        archive.Value(ref value.AssignedQuantity);
        archive.Value(ref value.Revision);
        archive.Value(ref value.LastAppliedActionSequence);
        archive.EnumValue(ref value.State);
        archive.EnumValue(ref value.NextAction);
        archive.Value(ref value.DropPosition);
    }

    internal static void Fields(GameSnapshotArchive archive, ref ConstructionSupplyReservation value)
    {
        archive.EntityReference(ref value.Site);
        archive.EnumValue(ref value.ItemType);
        archive.Value(ref value.AssignmentRevision);
        archive.Value(ref value.RemainingQuantity);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneTransportReservation value)
    {
        archive.EntityReference(ref value.Task);
        archive.Value(ref value.RemainingQuantity);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneActionReadyRequest value)
    {
        archive.Value(ref value.ReceiptSequence);
        Fields(archive, ref value.Action);
        archive.Value(ref value.WorkerObservationRevision);
        archive.Value(ref value.WorldPosition);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneActionIdentity value)
    {
        archive.EntityReference(ref value.Assignment);
        archive.Value(ref value.AssignmentRevision);
        archive.Value(ref value.Sequence);
        archive.EntityReference(ref value.Worker);
        archive.EnumValue(ref value.Kind);
        archive.EntityReference(ref value.Target);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneItemTransferResult value)
    {
        Fields(archive, ref value.Action);
        archive.EnumValue(ref value.Status);
        archive.Value(ref value.MovedQuantity);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneAutomaticActionRequest value)
    {
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneStationRequest value)
    {
        archive.EntityReference(ref value.Drone);
        archive.EntityReference(ref value.Station);
        archive.EnumValue(ref value.Operation);
        archive.Value(ref value.Sequence);
        archive.Value(ref value.SourceSlotIndex);
        archive.EntityReference(ref value.RelocationTask);
        archive.Value(ref value.RelocationMemberIndex);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneStationResult value)
    {
        archive.EntityReference(ref value.Drone);
        archive.EntityReference(ref value.Station);
        archive.Value(ref value.SlotIndex);
        archive.EnumValue(ref value.Operation);
        archive.EnumValue(ref value.Status);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneAutomaticStationRequest value)
    {
        archive.EntityReference(ref value.DemandTask);
        archive.Value(ref value.Quantity);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneStationRelocationRequest value)
    {
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneTaskRequest value)
    {
        archive.EnumValue(ref value.Operation);
        archive.EnumValue(ref value.Kind);
        archive.EntityReference(ref value.Target);
        archive.EnumValue(ref value.ItemType);
        archive.Value(ref value.Quantity);
        archive.EnumValue(ref value.Area);
        archive.EnumValue(ref value.Purpose);
        archive.EntityReference(ref value.Task);
        archive.Value(ref value.Sequence);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneTaskRequestResult value)
    {
        archive.EnumValue(ref value.Operation);
        archive.EnumValue(ref value.Status);
        archive.EntityReference(ref value.Task);
    }

    internal static void Fields(GameSnapshotArchive archive, ref BuildingDemolitionAutomaticTaskRequest value)
    {
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneRouteEvaluationRequest value)
    {
        archive.EntityReference(ref value.DemolitionRoot);
        archive.EntityReference(ref value.Worker);
        archive.EntityReference(ref value.Assignment);
        archive.Value(ref value.AssignmentRevision);
        archive.Value(ref value.EvaluationRevision);
        archive.Value(ref value.WorkerObservationRevision);
        archive.EnumValue(ref value.Kind);
        archive.Value(ref value.IsDropPositionSearch);
        archive.Value(ref value.OriginPosition);
        archive.EntityReference(ref value.Source);
        archive.Value(ref value.SourcePosition);
        archive.EntityReference(ref value.Destination);
        archive.Value(ref value.DestinationPosition);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneRouteEvaluationResult value)
    {
        archive.Value(ref value.EvaluationRevision);
        archive.EnumValue(ref value.Status);
        archive.Value(ref value.TotalDistance);
        archive.Value(ref value.HasDropPosition);
        archive.Value(ref value.DropPosition);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DemolishBuildingRequest value)
    {
        archive.EntityReference(ref value.TargetBuilding);
    }

    internal static void Fields(GameSnapshotArchive archive, ref PendingBuildingDemolition value)
    {
        archive.EntityReference(ref value.Task);
    }

    internal static void Fields(GameSnapshotArchive archive, ref PreparingStationDemolition value)
    {
        archive.EntityReference(ref value.Task);
        archive.Value(ref value.Started);
        archive.Value(ref value.TotalCount);
        archive.Value(ref value.CompletedCount);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DockedDroneElement value)
    {
        archive.EntityReference(ref value.Drone);
        archive.Value(ref value.SlotIndex);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneStationReservedSlotElement value)
    {
        archive.EntityReference(ref value.Task);
        archive.Value(ref value.MemberIndex);
        archive.Value(ref value.SlotIndex);
        archive.EntityReference(ref value.TransportWorker);
        archive.EnumValue(ref value.ItemType);
        archive.Value(ref value.Quantity);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneRecoveryPending value)
    {
        archive.EnumValue(ref value.Reason);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneTaskGenerationBlockElement value)
    {
        Fields(archive, ref value.Key);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneTaskGenerationKey value)
    {
        archive.EnumValue(ref value.Kind);
        archive.EntityReference(ref value.Target);
        archive.EnumValue(ref value.ItemType);
        archive.EnumValue(ref value.Area);
        archive.EnumValue(ref value.Purpose);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneTaskPendingPublicationElement value)
    {
        Fields(archive, ref value.Candidate);
        archive.Value(ref value.Quantity);
        archive.Value(ref value.CommittedQuantity);
        archive.Value(ref value.HasRetargetStorageClaim);
        archive.Value(ref value.PublicationQueued);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneTaskCandidateDecisionElement value)
    {
        archive.EntityReference(ref value.Worker);
        archive.EntityReference(ref value.Assignment);
        archive.Value(ref value.AssignmentRevision);
        archive.EnumValue(ref value.NextAction);
        archive.Value(ref value.DropPosition);
        archive.EntityReference(ref value.Task);
        archive.EntityReference(ref value.Source);
        archive.EntityReference(ref value.Destination);
        archive.EnumValue(ref value.SourceArea);
        archive.EnumValue(ref value.DestinationArea);
        archive.EntityReference(ref value.RouteRequest);
        archive.EnumValue(ref value.ItemType);
        archive.Value(ref value.WorkerObservationRevision);
        archive.Value(ref value.Quantity);
    }

    internal static void Fields(GameSnapshotArchive archive, ref DroneTransportPendingReservationElement value)
    {
        Fields(archive, ref value.Candidate);
        Fields(archive, ref value.Reservation);
        archive.Value(ref value.PublicationQueued);
    }

}
