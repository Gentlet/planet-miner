/// <summary>
/// 자원 설정 로드와 생성 요청에서 패치 반경/매장량의 지원 범위를 함께 검사한다.
/// </summary>
public static class ResourceGenerationConfigLookupUtility
{
    public static bool IsPatchRadiusSupported(int minRadius, int maxRadius)
    {
        return minRadius >= 0 && maxRadius >= minRadius &&
               maxRadius <= GameConstants.MaxResourcePatchRadius;
    }

    public static bool IsAmountRangeSupported(int minAmount, int maxAmount)
    {
        return minAmount > 0 && maxAmount >= minAmount && maxAmount < int.MaxValue;
    }
}
