using Unity.Entities;

/// <summary>시작 화면 또는 복원 준비가 소유한다. 설정과 SubScene은 준비하되 새 게임 부트스트랩과 게임 틱은 대기한다.</summary>
public struct GameSessionStartPending : IComponentData
{
}
