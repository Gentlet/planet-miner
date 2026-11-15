using Unity.Mathematics;

/// <summary>
/// 역할·목적: Splitter/Merger의 상대 포트·인접 방향·배치 순서 계산을 같은 규칙으로 처리한다.
/// 입력·출력: forward와 포트/좌표로 방향을 계산하고 성공한 전달 뒤 다음 순환 커서를 반환한다.
/// 이용: 라우팅 Decision/Reservation/Apply가 호출한다. 계산 자체는 후보 선택이나 커서 원본을 쓰지 않는다.
/// 수명: 상태/엔티티/캐시를 만들지 않는 순수 값 계산이며 포트 순서와 커서의 실제 반영은 호출자의 단계가 소유한다.
/// </summary>
public static class RoutingDirectionUtility
{
    public const int RightQuarterTurns = 1;
    public const int LeftQuarterTurns = -1;
    public const int OppositeQuarterTurns = 2;

    /// <summary>양수는 시계, 음수는 반시계 회전이다. 회전량을 먼저 정규화해 모든 int 입력을 처리한다.</summary>
    public static DirectionEnum RotateQuarterTurns(DirectionEnum direction, int clockwiseQuarterTurns)
    {
        int count = (int)DirectionEnum.Count;
        int turns = ((clockwiseQuarterTurns % count) + count) % count;
        return (DirectionEnum)(((int)direction + turns) % count);
    }

    public const byte RoutingPortCount = 3;

    /// <summary>
    /// Splitter 출력 순서: forward -> right -> left.
    /// </summary>
    public static DirectionEnum GetSplitterOutput(DirectionEnum forward, byte portIndex)
    {
        switch (portIndex % RoutingPortCount)
        {
            case 0:
                return forward;
            case 1:
                return RotateQuarterTurns(forward, clockwiseQuarterTurns: RightQuarterTurns);
            default:
                return RotateQuarterTurns(forward, clockwiseQuarterTurns: LeftQuarterTurns);
        }
    }

    /// <summary>
    /// Merger 입력 순서: back -> left -> right.
    /// </summary>
    public static DirectionEnum GetMergerInput(DirectionEnum forward, byte portIndex)
    {
        switch (portIndex % RoutingPortCount)
        {
            case 0:
                return RotateQuarterTurns(forward, clockwiseQuarterTurns: OppositeQuarterTurns);
            case 1:
                return RotateQuarterTurns(forward, clockwiseQuarterTurns: LeftQuarterTurns);
            default:
                return RotateQuarterTurns(forward, clockwiseQuarterTurns: RightQuarterTurns);
        }
    }

    /// <summary>
    /// Splitter 출력 방향으로부터 포트 인덱스(0: forward, 1: right, 2: left) 역산.
    /// </summary>
    public static byte GetSplitterPortIndex(DirectionEnum forward, DirectionEnum outputDirection)
    {
        if (outputDirection == forward)
            return 0;
        if (outputDirection == RotateQuarterTurns(forward, clockwiseQuarterTurns: RightQuarterTurns))
            return 1;
        return 2;
    }

    /// <summary>
    /// Merger 입력 방향으로부터 포트 인덱스(0: back, 1: left, 2: right) 역산.
    /// </summary>
    public static byte GetMergerPortIndex(DirectionEnum forward, DirectionEnum inputDirection)
    {
        if (inputDirection == RotateQuarterTurns(forward, clockwiseQuarterTurns: OppositeQuarterTurns))
            return 0;
        if (inputDirection == RotateQuarterTurns(forward, clockwiseQuarterTurns: LeftQuarterTurns))
            return 1;
        return 2;
    }

    /// <summary>
    /// 두 인접 그리드 좌표 간의 방향 계산.
    /// </summary>
    public static bool TryGetDirection(int2 from, int2 to, out DirectionEnum direction)
    {
        int2 offset = to - from;
        if (offset.x == 0 && offset.y == 1)
        {
            direction = DirectionEnum.Up;
            return true;
        }
        if (offset.x == 1 && offset.y == 0)
        {
            direction = DirectionEnum.Right;
            return true;
        }
        if (offset.x == 0 && offset.y == -1)
        {
            direction = DirectionEnum.Down;
            return true;
        }
        if (offset.x == -1 && offset.y == 0)
        {
            direction = DirectionEnum.Left;
            return true;
        }

        direction = DirectionEnum.Up;
        return false;
    }

    /// <summary>
    /// 성공한 전달 뒤 다음 순환 인덱스 계산.
    /// </summary>
    public static byte AdvanceCursor(byte selectedPortIndex)
    {
        return (byte)((selectedPortIndex + 1) % RoutingPortCount);
    }

    /// <summary>
    /// 인접 벨트가 라우터를 향하는 입력 벨트인지 검사.
    /// </summary>
    public static bool IsIncomingBelt(
        int2 routerPosition,
        int2 beltPosition,
        DirectionEnum beltDirection)
    {
        return math.all(beltPosition + beltDirection.ToInt2() == routerPosition);
    }

    /// <summary>
    /// 인접 벨트가 라우터에서 멀어지는 출력 벨트인지 검사.
    /// </summary>
    public static bool IsOutgoingBelt(
        int2 routerPosition,
        int2 beltPosition,
        DirectionEnum beltDirection)
    {
        return math.all(routerPosition + beltDirection.ToInt2() == beltPosition);
    }
}
