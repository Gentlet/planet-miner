using Unity.Entities;

/// <summary>초기화가 검증/게시하는 드론당 충전 속도와 최대 소비 전력이다.</summary>
public struct DroneChargingConfig : IComponentData
{
    public float ChargePerSecond;
    public float PowerPerDrone;
}

/// <summary>정거장의 이번 틱 충전 입력. Preparation만 다시 작성하고 수요 및 충전 Execution이 읽는다.</summary>
public struct DroneChargingPreparationElement : IBufferElementData
{
    public Entity Drone;
    public float DesiredCharge;
    public float RequestedEnergy;
}
