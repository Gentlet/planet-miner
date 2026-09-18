using Unity.Burst;
using Unity.Mathematics;

/// <summary>
/// DirectionEnum을 위한 확장 메서드 모음.
/// Burst 컴파일 호환성을 갖춥니다.
/// </summary>
[BurstCompile]
public static class DirectionExtensions
{
    /// <summary>
    /// 방향 열거형을 2D 그리드 오프셋 벡터(int2)로 변환합니다.
    /// </summary>
    [BurstCompile]
    public static int2 ToInt2(this DirectionEnum dir)
    {
        switch (dir)
        {
            case DirectionEnum.Up:
                return new int2(0, 1);
            case DirectionEnum.Right:
                return new int2(1, 0);
            case DirectionEnum.Down:
                return new int2(0, -1);
            case DirectionEnum.Left:
                return new int2(-1, 0);
            default:
                return int2.zero;
        }
    }
}
