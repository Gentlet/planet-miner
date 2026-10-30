using Unity.Entities;

/// <summary>
/// 드론 한 개체의 현재 배터리량. 최대치는 복제하지 않으며 DroneConfig.maximumBattery와 같은 단위다.
/// 생성 시 DroneSpawnUtility가 호출자가 제공한 최대치로 한 번 초기화한다. 기존 값은 역할 초기화로 충전하지 않는다.
/// Movement가 이동 소비를, Charging이 정박 증가를 반영한다. 전력 배정량/충전율과 구분한다.
/// </summary>
public struct DroneBatteryState : IComponentData
{
    public float CurrentCharge;
    // 이미 전력으로 충전됐지만 CurrentCharge의 다음 float 값에 못 미쳐 기록하지 못한 잔량이다.
    // 이동과 역할 전환은 보존하고 충전 정산만 잔량을 갱신한다. 입력 검증은 범위 확인에 읽는다.
    public double FractionalCharge;
}
