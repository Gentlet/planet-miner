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
/// 시작이 완료된 World의 주 시설 참조다. 새 게임은 초기 지급과 함께 EndBuilding에 게시한다.
/// 복원은 저장한 시설과 재고로 별도 World에 구성한다. 재진입은 이 참조로 생성과 지급의 중복을 막는다.
/// </summary>
public struct GameBootstrapState : IComponentData
{
    public Entity MainFacility;
}
