using Unity.Entities;

/// <summary>후보 singleton의 이번 틱 정상 귀환 경합 결과다. Reservation만 재작성하고 Movement가 소비한다.</summary>
[InternalBufferCapacity(0)]
public struct DroneReturnReservationElement : IBufferElementData
{
    public Entity Worker;
    // 정거장/행동 요청을 처리할 때까지 현재 이동 상태와 위치를 보존한다. 배정/경로 대기는 이동 의도를 종료하고 위치만 유지한다.
    public bool PreserveArrival;
}
