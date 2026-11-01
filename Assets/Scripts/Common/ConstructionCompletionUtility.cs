using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>완공 소유자와 같은 공개 경계의 생성 검사가 동일한 실제 자재/바닥 차단 조건을 사용하게 한다.</summary>
public static class ConstructionCompletionUtility
{
    public static bool AreMaterialsSatisfied(in DynamicBuffer<ConstructionMaterialRequirementElement> requirements)
    {
        for (int i = 0; i < requirements.Length; i++)
        {
            if (!requirements[i].IsSatisfied)
            {
                return false;
            }
        }
        return true;
    }

    public static bool HasBlockingWorldItem(int2 origin, int2 effectiveSize, in NativeArray<int2> worldPositions)
    {
        for (int i = 0; i < worldPositions.Length; i++)
        {
            int2 position = worldPositions[i];
            if (math.all(position >= origin) && math.all(position < origin + effectiveSize))
            {
                return true;
            }
        }
        return false;
    }
}
