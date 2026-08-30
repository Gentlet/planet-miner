using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public static class BuildingDefinitionUtility
{
    public static bool TryGetDefinition(
        this DynamicBuffer<BuildingPrefabElement> definitions,
        BuildingTypeEnum type,
        out BuildingPrefabElement definition)
    {
        for (int i = 0; i < definitions.Length; i++)
        {
            if (definitions[i].type != type)
                continue;

            definition = definitions[i];
            return true;
        }

        definition = default;
        return false;
    }

    public static int2 GetFootprintSize(
        this DynamicBuffer<BuildingPrefabElement> definitions,
        BuildingTypeEnum type)
    {
        for (int i = 0; i < definitions.Length; i++)
        {
            if (definitions[i].type == type)
                return BuildingFootprintUtility.NormalizeSize(definitions[i].size);
        }

        return new int2(1, 1);
    }

    public static int2 GetFootprintSize(
        this NativeArray<BuildingPrefabElement> definitions,
        BuildingTypeEnum type)
    {
        for (int i = 0; i < definitions.Length; i++)
        {
            if (definitions[i].type == type)
                return BuildingFootprintUtility.NormalizeSize(definitions[i].size);
        }

        return new int2(1, 1);
    }
}
