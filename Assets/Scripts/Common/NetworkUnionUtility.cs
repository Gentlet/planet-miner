using Unity.Collections;

/// <summary>지역 네트워크 부모 배열의 경로를 압축한다. 대표 선택과 배열 수명은 각 호출자가 소유한다.</summary>
internal static class NetworkUnionUtility
{
    internal static int FindRoot(NativeArray<int> parents, int index)
    {
        while (parents[index] != index)
        {
            parents[index] = parents[parents[index]];
            index = parents[index];
        }
        return index;
    }
}
