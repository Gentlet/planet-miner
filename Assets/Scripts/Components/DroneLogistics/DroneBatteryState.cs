using Unity.Entities;

/// <summary>
/// 드론 한 개체의 현재 배터리량. 최대치는 복제하지 않으며 DroneConfig.maximumBattery와 같은 단위다.
/// 생성 시 DroneSpawnUtility가 호출자가 제공한 최대치로 한 번 초기화한다. 기존 값은 역할 초기화로 충전하지 않는다.
/// 실제 이동 소비·충전 Writer는 Task 9.10/9.14 후속이며 전력 배정량/충전율과 구분한다.
/// </summary>
public struct DroneBatteryState : IComponentData
{
    public float CurrentCharge;
}
