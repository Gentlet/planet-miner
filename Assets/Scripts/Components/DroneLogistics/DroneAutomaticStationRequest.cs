using Unity.Entities;

/// <summary>
/// 자동 정거장 요청/결과의 소비 표식과 출고 중재 입력이다. 작업 수량을 확보하는 원본이 아니다.
/// 요청 생성 때 부착되어 결과 소비까지 유지되며, Lifecycle Decision이 존재 여부를 기록해 최종 Apply에 전달한다.
/// </summary>
public struct DroneAutomaticStationRequest : IComponentData
{
    public Entity DemandTask;
    public int Quantity;
}
