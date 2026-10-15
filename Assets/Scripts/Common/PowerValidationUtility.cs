using Unity.Collections;
using Unity.Entities;

/// <summary>검증 결과 기록과 설정 버퍼 존재 판정. Lookup을 취득하거나 묶지 않는다.</summary>
internal static class PowerValidationUtility
{
    public static bool HasConfiguration(Entity entity, BufferLookup<BuildingConfigElement> configs)
    {
        if (entity == Entity.Null)
        {
            return false;
        }
        return configs.HasBuffer(entity);
    }

    public static void Fail(NativeReference<PowerValidationResult> validation, FixedString128Bytes message)
    {
        if (!validation.Value.HasError)
        {
            validation.Value = new PowerValidationResult { Message = message };
        }
    }
}
