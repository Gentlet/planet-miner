using Unity.Entities;

/// <summary>
/// 일반 참가자의 이번 틱 공급 전신주 선택. 활성 상태는 준비 완료이며 Null 선택은 공급 범위 밖이다.
/// Decision이 매 틱 비활성화/작성하고 Apply가 같은 ECB의 망에 연결한다. 예약 망 Entity를 담지 않는다.
/// </summary>
public struct PowerGridConnectionDecision : IComponentData, IEnableableComponent
{
    public Entity PowerPole;
}
