using Unity.Entities;

/// <summary>
/// 역할·목적: ECS에 게시된 자원 생성 설정에서 요청 품목의 첫 설정 행을 조회한다.
/// 입력·출력: 읽기 버퍼에서 일치하면 값 복사본, 누락이면 false/default를 반환한다.
/// 이용·수명: 현재 제품 코드의 호출자는 없는 조회 API다. 설정 원본과 별도의 캐시/엔티티를 만들지 않는다.
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
