using Unity.Entities;

/// <summary>
/// DynamicBuffer로 게시된 자원 생성 설정을 Burst 호환 방식으로 조회하기 위한 유틸리티.
/// </summary>
public static class ResourceGenerationConfigLookupUtility
{
    public static bool TryGetConfig(
        in DynamicBuffer<ResourceGenerationConfigElement> buffer,
        ItemTypeEnum resourceType,
        out ResourceGenerationConfigElement result)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i].ResourceType == resourceType)
            {
                result = buffer[i];
                return true;
            }
        }

        result = default;
        return false;
    }
}
