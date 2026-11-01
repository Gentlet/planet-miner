using Unity.Entities;

/// <summary>
/// 초기화가 필수 준비 후 한 번 접수하며 GameBootstrapApplySystem이 Building StateApply에서 처리한다.
/// 최초 생성 성공 시 시설/실물/완료 기록의 공개와 같은 EndBuilding에 삭제한다.
/// 준비 전에는 접수하지 않고 실패는 공통 Fatal로 중단한다.
/// </summary>
public struct GameBootstrapRequest : IRequestComponent
{
    public PlacementStamp Stamp;
}

/// <summary>
/// 주 시설과 시작 실물이 같은 EndBuilding에 공개됐음을 나타내는 World 수명의 기록이다.
/// 설정/재고/지급 잔량과 구분하며, 재진입은 이 참조를 확인하고 생성이나 지급을 반복하지 않는다.
/// </summary>
public struct GameBootstrapState : IComponentData
{
    public Entity MainFacility;
}
