using Unity.Collections;

/// <summary>건물 설정의 전력 기능. 수요, 공급 및 잔여 에너지는 런타임 컴포넌트가 소유한다.</summary>
public struct BuildingPowerConfig
{
    public BuildingPowerGridConfig Grid;
    public BuildingWorkPowerConfig Work;
    public BuildingGenerationConfig Generation;
}

public struct BuildingPowerGridConfig
{
    public bool IsPresent;
    public int SupplyRange;
    public int ConnectionRange;
}

public struct BuildingWorkPowerConfig
{
    public bool IsPresent;
    public float MaximumConsumption;
}

public struct BuildingGenerationConfig
{
    public bool IsPresent;
    public float MaximumGeneration;
    public BuildingFuelStorageConfig FuelStorage;
}

public struct BuildingFuelStorageConfig
{
    public bool IsPresent;
    public int Slots;
    public bool BeltOutput;
    public FixedList512Bytes<BuildingFuelConfig> Fuels;
}
