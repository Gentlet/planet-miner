/// <summary>
/// 종류·Tier별 BuildingConfigElement에 포함되는 전력 능력 원본이다.
/// 런타임 수요·공급·연료 잔량은 이 설정에 기록하지 않는다.
/// </summary>
public struct BuildingPowerConfig
{
    public bool HasConfiguration;
    public int SupplyRange;
    public int ConnectionRange;
    public float MaximumGeneration;
    public float MaximumConsumption;
    public float CoalEnergyPerItem;

    public BuildingPowerConfig(
        int supplyRange = 0,
        int connectionRange = 0,
        float maximumGeneration = 0f,
        float maximumConsumption = 0f,
        float coalEnergyPerItem = 0f)
    {
        HasConfiguration = true;
        SupplyRange = supplyRange;
        ConnectionRange = connectionRange;
        MaximumGeneration = maximumGeneration;
        MaximumConsumption = maximumConsumption;
        CoalEnergyPerItem = coalEnergyPerItem;
    }
}
