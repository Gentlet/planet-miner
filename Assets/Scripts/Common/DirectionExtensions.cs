using Unity.Mathematics;

/// <summary>
/// 역할·목적: 방향 enum을 인접 격자 셀을 찾기 위한 int2 오프셋으로 변환한다.
/// 이용: 벨트 이동·건물 입출고·라우팅의 방향 계산에서 호출한다. Up/Right/Down/Left 이외 값은 int2.zero를 반환한다.
/// 수명·소유권: 값만 반환하는 순수 확장 함수이며 ECS 상태나 메모리 수명을 소유하지 않는다.
/// </summary>
public static class DirectionExtensions
{
    /// <summary>
    /// 방향 열거형을 2D 그리드 오프셋 벡터(int2)로 변환.
    /// </summary>
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
