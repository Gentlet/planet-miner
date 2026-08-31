using System;
using System.Collections.Generic;

[Serializable]
internal sealed class BuildingRuntimeConfigFile
{
    public List<BuildingRuntimeConfigData> buildings;
}

[Serializable]
internal sealed class BuildingRuntimeConfigData
{
    public string buildingType;
    public float speed;
    public int storageCapacity;
}
