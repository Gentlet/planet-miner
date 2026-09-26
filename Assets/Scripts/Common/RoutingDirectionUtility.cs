using Unity.Mathematics;

/// <summary>
/// Splitter/Merger의 상대 방향 규칙을 계산하는 Burst 호환 유틸리티.
/// </summary>
public static class RoutingDirectionUtility
{
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
                return RotateRight(forward);
            default:
                return RotateLeft(forward);
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
                return Opposite(forward);
            case 1:
                return RotateLeft(forward);
            default:
                return RotateRight(forward);
        }
    }

    /// <summary>
    /// Splitter 출력 방향으로부터 포트 인덱스(0: forward, 1: right, 2: left) 역산.
    /// </summary>
    public static byte GetSplitterPortIndex(DirectionEnum forward, DirectionEnum outputDirection)
    {
        if (outputDirection == forward)
            return 0;
        if (outputDirection == RotateRight(forward))
            return 1;
        return 2;
    }

    /// <summary>
    /// Merger 입력 방향으로부터 포트 인덱스(0: back, 1: left, 2: right) 역산.
    /// </summary>
    public static byte GetMergerPortIndex(DirectionEnum forward, DirectionEnum inputDirection)
    {
        if (inputDirection == Opposite(forward))
            return 0;
        if (inputDirection == RotateLeft(forward))
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
    /// 배치 확정 순서를 비교.
    /// </summary>
    public static bool IsEarlierPlacement(
        in PlacementStamp candidate,
        in PlacementStamp current)
    {
        return candidate.IsEarlierThan(current);
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

    public static DirectionEnum RotateRight(DirectionEnum direction)
    {
        return (DirectionEnum)(((int)direction + 1) % (int)DirectionEnum.Count);
    }

    public static DirectionEnum RotateLeft(DirectionEnum direction)
    {
        return (DirectionEnum)(((int)direction + (int)DirectionEnum.Count - 1) % (int)DirectionEnum.Count);
    }

    public static DirectionEnum Opposite(DirectionEnum direction)
    {
        return (DirectionEnum)(((int)direction + 2) % (int)DirectionEnum.Count);
    }
}
