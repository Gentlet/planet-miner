using System;

[Serializable]
internal sealed class StartingItemConfigFile
{
    public StartingItemConfigEntryFile[] items;
}

[Serializable]
internal sealed class StartingItemConfigEntryFile
{
    public string itemType;
    public int quantity;
}
