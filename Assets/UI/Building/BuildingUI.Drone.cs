using Unity.Entities;
using UnityEngine;

public partial class BuildingUI
{
    private void RefreshDrone()
    {
        if (!_entityManager.Exists(_selectedDrone))
        {
            Close();
            return;
        }

        if (!_entityManager.HasComponent<ActiveDrone>(_selectedDrone))
        {
            Close();
            return;
        }

        if (!_entityManager.HasComponent<DroneBattery>(_selectedDrone))
        {
            Close();
            return;
        }

        if (!_entityManager.HasComponent<DroneState>(_selectedDrone))
        {
            Close();
            return;
        }

        SetDroneLayout();

        ActiveDrone activeDrone =
            _entityManager.GetComponentData<ActiveDrone>(_selectedDrone);
        DroneBattery battery =
            _entityManager.GetComponentData<DroneBattery>(_selectedDrone);
        DroneState state =
            _entityManager.GetComponentData<DroneState>(_selectedDrone);

        float batteryPercentage = battery.maximum > 0f
            ? Mathf.Clamp01(battery.current / battery.maximum) * 100f
            : 0f;

        SetStatus(
            $"상태: {GetDroneStateDisplayName(state.value)}",
            GetDroneStatusClass(state.value));

        _droneBatteryProgress.value = batteryPercentage;
        _droneBatteryProgress.title = $"{batteryPercentage:0.#}%";
        _droneBatteryLabel.text =
            $"배터리: {battery.current:0.##} / {battery.maximum:0.##}";
        _droneCapabilityLabel.text =
            $"운반 용량: {activeDrone.carryingCapacity}  ·  이동 속도: {activeDrone.movementSpeed:0.##}";
        _droneCargoLabel.text = GetDroneCargoText(activeDrone.carryingCapacity);
        _droneTaskLabel.text = GetDroneTaskText();
        _droneAssignmentLabel.text = GetDroneAssignmentText();
    }

    private string GetDroneCargoText(int carryingCapacity)
    {
        if (!_entityManager.HasComponent<DroneCargo>(_selectedDrone))
            return $"화물: 없음 (0 / {carryingCapacity})";

        DroneCargo cargo =
            _entityManager.GetComponentData<DroneCargo>(_selectedDrone);

        if (cargo.quantity <= 0)
            return $"화물: 없음 (0 / {carryingCapacity})";

        return
            $"화물: {GetItemDisplayName(cargo.itemType)} ×{cargo.quantity} / {carryingCapacity}";
    }

    private string GetDroneTaskText()
    {
        if (!_entityManager.HasComponent<DroneAssignment>(_selectedDrone))
            return "현재 작업: 없음";

        DroneAssignment assignment =
            _entityManager.GetComponentData<DroneAssignment>(_selectedDrone);

        if (!_entityManager.Exists(assignment.taskEntity))
            return "현재 작업: 없음";

        if (!_entityManager.HasComponent<DroneTask>(assignment.taskEntity))
            return "현재 작업: 없음";

        DroneTask task =
            _entityManager.GetComponentData<DroneTask>(assignment.taskEntity);
        string taskText = $"현재 작업: {GetDroneTaskTypeDisplayName(task.type)}";

        if (_entityManager.HasComponent<DroneTaskStatus>(assignment.taskEntity))
        {
            DroneTaskStatus status =
                _entityManager.GetComponentData<DroneTaskStatus>(assignment.taskEntity);
            taskText += $" · {GetDroneTaskStateDisplayName(status.state)}";
        }

        if (_entityManager.HasComponent<DroneTaskQuantity>(assignment.taskEntity))
        {
            DroneTaskQuantity quantity =
                _entityManager.GetComponentData<DroneTaskQuantity>(assignment.taskEntity);
            taskText += $" · {quantity.deliveredQuantity} / {quantity.totalQuantity}";
        }

        return taskText;
    }

    private string GetDroneAssignmentText()
    {
        if (_entityManager.HasComponent<DroneRecoveryRequest>(_selectedDrone))
        {
            DroneRecoveryRequest recovery =
                _entityManager.GetComponentData<DroneRecoveryRequest>(_selectedDrone);
            return $"복구 사유: {GetDroneRecoveryReasonDisplayName(recovery.reason)}";
        }

        if (!_entityManager.HasComponent<DroneAssignment>(_selectedDrone))
            return "할당 네트워크: 없음";

        DroneAssignment assignment =
            _entityManager.GetComponentData<DroneAssignment>(_selectedDrone);
        return assignment.networkId >= 0
            ? $"할당 네트워크: {assignment.networkId}"
            : "할당 네트워크: 없음";
    }

    private static string GetDroneStatusClass(DroneStateEnum state)
    {
        if (state == DroneStateEnum.EmergencyReturning)
            return "status-error";

        if (state == DroneStateEnum.AwaitingCharge ||
            state == DroneStateEnum.AwaitingStorage)
        {
            return "status-waiting";
        }

        return "status-normal";
    }

    private static string GetDroneStateDisplayName(DroneStateEnum state)
    {
        return state switch
        {
            DroneStateEnum.Stored => "보관 중",
            DroneStateEnum.MovingToPickup => "수거 지점으로 이동 중",
            DroneStateEnum.PickingUp => "화물 수거 중",
            DroneStateEnum.MovingToDelivery => "배송 지점으로 이동 중",
            DroneStateEnum.Delivering => "화물 전달 중",
            DroneStateEnum.MovingToRecoveryStorage => "복구 창고로 이동 중",
            DroneStateEnum.RecoveringCargo => "화물 복구 중",
            DroneStateEnum.Returning => "정거장으로 복귀 중",
            DroneStateEnum.AwaitingCharge => "충전 대기 중",
            DroneStateEnum.EmergencyReturning => "비상 복귀 중",
            DroneStateEnum.MovingToDemolition => "철거 지점으로 이동 중",
            DroneStateEnum.Demolishing => "철거 중",
            DroneStateEnum.MovingToWorldItem => "월드 아이템으로 이동 중",
            DroneStateEnum.PickingUpWorldItem => "월드 아이템 수거 중",
            DroneStateEnum.MovingToWorldItemStorage => "보관소로 이동 중",
            DroneStateEnum.DeliveringWorldItem => "월드 아이템 전달 중",
            DroneStateEnum.AwaitingStorage => "보관 공간 대기 중",
            _ => "알 수 없음"
        };
    }

    private static string GetDroneTaskTypeDisplayName(DroneTaskTypeEnum taskType)
    {
        return taskType switch
        {
            DroneTaskTypeEnum.Construction => "건설 자재 운송",
            DroneTaskTypeEnum.Demolition => "철거",
            DroneTaskTypeEnum.RemoveBuildingItem => "건물 아이템 반출",
            DroneTaskTypeEnum.InsertBuildingItem => "건물 아이템 반입",
            DroneTaskTypeEnum.RecoverWorldItem => "월드 아이템 회수",
            _ => "알 수 없음"
        };
    }

    private static string GetDroneTaskStateDisplayName(DroneTaskStateEnum taskState)
    {
        return taskState switch
        {
            DroneTaskStateEnum.Pending => "대기",
            DroneTaskStateEnum.InProgress => "진행 중",
            DroneTaskStateEnum.Suspended => "중단됨",
            DroneTaskStateEnum.Completed => "완료",
            DroneTaskStateEnum.Cancelled => "취소됨",
            _ => "알 수 없음"
        };
    }

    private static string GetDroneRecoveryReasonDisplayName(
        DroneRecoveryReasonEnum recoveryReason)
    {
        return recoveryReason switch
        {
            DroneRecoveryReasonEnum.TargetUnavailable => "대상 사용 불가",
            DroneRecoveryReasonEnum.InsufficientBattery => "배터리 부족",
            DroneRecoveryReasonEnum.TransferFailed => "화물 전달 실패",
            _ => "알 수 없음"
        };
    }
}
