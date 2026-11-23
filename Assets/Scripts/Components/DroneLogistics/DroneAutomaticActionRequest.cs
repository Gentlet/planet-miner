using Unity.Entities;

/// <summary>
/// 자동 픽업/납품/안전 방출 수행부가 소유하는 요청/결과의 수명 표식이다.
/// 요청은 기존 접수 API로 생성하고 EndSimulation의 결과 공개 뒤 같은 수행부가 소비한다.
/// 외부 요청은 이 표식이 없다. 외부 결과의 소유권은 복원에서만 별도 표식으로 인계할 수 있다.
/// </summary>
public struct DroneAutomaticActionRequest : IComponentData
{
}

/// <summary>세션 복원에서 이전 World의 외부 소비자 대신 새 World의 행동 결과 정리를 맡긴 표식이다.</summary>
public struct RestoredDroneActionResultOwner : IComponentData
{
}
