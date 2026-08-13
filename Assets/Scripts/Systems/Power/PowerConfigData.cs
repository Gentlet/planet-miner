using System;
using System.Collections.Generic;

[Serializable]
internal class PowerConfigFile
{
    public List<PowerPoleConfigData> powerPoles;
    public List<PowerGeneratorConfigData> generators;
    public CoalGeneratorConfigData coalGenerator;
    public List<PowerConsumerConfigData> consumers;
}

[Serializable]
internal class PowerPoleConfigData
{
    public string buildingType;
    public int supplyRangeX;
    public int supplyRangeY;
    public int connectionRangeX;
    public int connectionRangeY;
}

[Serializable]
internal class PowerGeneratorConfigData
{
    public string generatorType;
    public float maximumGeneration;
}

[Serializable]
internal class CoalGeneratorConfigData
{
    public float coalEnergyPerItem;
    public int fuelStorageCapacity;
}

[Serializable]
internal class PowerConsumerConfigData
{
    public string buildingType;
    public float maximumConsumption;
}
