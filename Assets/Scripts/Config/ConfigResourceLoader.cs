using UnityEngine;

public static class ConfigResourceLoader
{
    public static bool TryLoadJson(
        string configName,
        string resourcePath,
        out string json)
    {
        TextAsset configAsset = Resources.Load<TextAsset>(resourcePath);

        if (configAsset != null)
        {
            json = configAsset.text;
            return true;
        }

        Debug.LogError(
            $"{configName} config file not found. Path : Resources/{resourcePath}");
        json = null;
        return false;
    }
}
