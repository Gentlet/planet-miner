using System;
using System.Collections.Generic;

[Serializable]
internal sealed class ConstructionConfigFile
{
    public List<ConstructionMaterialConfigData> materials;
}

[Serializable]
internal sealed class ConstructionMaterialConfigData
{
    public string buildingType;
    public string itemType;
    public int quantity;
}
