using Unity.Entities;

/// <summary>
/// 자동 픽업/납품/안전 방출 수행부가 소유하는 요청/결과의 수명 표식이다.
/// 요청은 기존 접수 API로 생성하고 EndSimulation의 결과 공개 뒤 같은 수행부가 소비한다.
/// 외부에서 제출한 요청/결과는 이 표식이 없으며 자동 소비하지 않는다.
/// </summary>
public struct DroneAutomaticActionRequest : IComponentData
{
}
