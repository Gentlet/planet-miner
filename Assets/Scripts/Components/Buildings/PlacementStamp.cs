using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 설치 Tick·World 접수 순서·요청 내부 후보 Order를 유지하는 불변 메타데이터다. Entity 생성 순서와 구분한다.
/// 부착 엔티티: 승인된 공사 현장과 해당 현장에서 생성한 완공 건물이다. SpawnBuildingRequest에도 값으로 전달할 수 있다.
/// 생성: 배치 Command는 개별 RequestTick→헤더 Tick→공통 현재 Tick과 접수번호·원래 후보 인덱스를 기록한다. 직접 생성의 생략값도 같은 번호 원본에서 발급한다.
/// 이용: 완공은 전체 값을 승계한다. 물류·라우터 연결·드론은 Compare로 표식 유무→Tick→접수 순서→Order→좌표(x, y)를 비교한다.
/// 제거: 설치 후 값을 변경하지 않으며 현장/건물 삭제 시 함께 제거한다. 접수번호 0인 명시 기록도 보존하고 같은 Tick에서는 양수 번호 뒤에 둔다.
/// </summary>
public struct PlacementStamp : IComponentData
{
    public ulong Tick;
    public uint Order;
    public ulong ReceiptSequence;

    public PlacementStamp(ulong tick, uint order, ulong receiptSequence = 0)
    {
        Tick = tick;
        Order = order;
        ReceiptSequence = receiptSequence;
    }

    public bool IsDefault => Tick == 0 && Order == 0 && ReceiptSequence == 0;

    public int CompareTo(in PlacementStamp other)
    {
        int comparison = Tick.CompareTo(other.Tick);
        if (comparison != 0) return comparison;

        bool hasReceipt = ReceiptSequence > 0;
        bool otherHasReceipt = other.ReceiptSequence > 0;
        if (hasReceipt != otherHasReceipt) return hasReceipt ? -1 : 1;

        comparison = ReceiptSequence.CompareTo(other.ReceiptSequence);
        if (comparison != 0) return comparison;

        return Order.CompareTo(other.Order);
    }

    /// <summary>공개된 설치 기록과 좌표만 비교한다. 같은 좌표까지 같으면 동순위이며 Entity 값으로 보완하지 않는다.</summary>
    public static int Compare(
        bool hasLeft, in PlacementStamp left, int2 leftPosition,
        bool hasRight, in PlacementStamp right, int2 rightPosition)
    {
        if (hasLeft != hasRight) return hasLeft ? -1 : 1;
        if (hasLeft)
        {
            int comparison = left.CompareTo(right);
            if (comparison != 0) return comparison;
        }

        int coordinateComparison = leftPosition.x.CompareTo(rightPosition.x);
        if (coordinateComparison != 0) return coordinateComparison;
        return leftPosition.y.CompareTo(rightPosition.y);
    }

    /// <summary>
    /// 다른 배치보다 먼저 확정된 배치인지 비교.
    /// </summary>
    public bool IsEarlierThan(in PlacementStamp other)
    {
        return CompareTo(other) < 0;
    }
}
