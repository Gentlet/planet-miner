using Unity.Entities;

/// <summary>자동 정거장 요청/결과의 소비 표식과 출고 중재 입력이다. 작업 수량을 확보하는 원본이 아니다.</summary>
public struct DroneAutomaticStationRequest : IComponentData
{
    public Entity DemandTask;
    public int Quantity;
}
