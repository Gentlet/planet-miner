using Unity.Entities;

/// <summary>
/// 주 시설·완공 드론 정거장의 역할 마커. 공통 건물 생성이 부착하고 건물 삭제와 함께 제거한다.
/// 위치·방향·footprint·범위 설정은 기존 건물 원본을 사용하며 전력 공급 여부와 독립적으로 참가한다.
/// </summary>
public struct DroneStation : IComponentData { }

/// <summary>
/// 이번 틱 연결 판단. DroneStationNetworkDecisionSystem만 작성하며 enable은 유효한 참가 정거장의 준비 완료다.
/// 같은 틱 DroneDecision 소비자는 이 값을 읽는다. Apply는 비활성 결과를 미연결로 공개한다.
/// </summary>
public struct DroneStationNetworkDecision : IComponentData, IEnableableComponent
{
    public Entity RepresentativeStation;
}

/// <summary>
/// 정거장의 공개 망 연결. DroneStationNetworkApplySystem이 EndSimulation에 설정하고 Sync가 수집한다.
/// 대표는 PlacementStamp 비교로 선택한 정거장 Entity이며 제거·병합·분리 시 바뀔 수 있다. Null은 미참가다.
/// </summary>
public struct DroneStationNetwork : IComponentData
{
    public Entity RepresentativeStation;
}
