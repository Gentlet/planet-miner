using Unity.Entities;

/// <summary>
/// 역할·목적: 자원 매장량을 차감할지 결정하는 World 공통 모드 계약이다.
/// 부착 엔티티: 자원별 실물이 아닌 World 단일 설정 엔티티를 기대한다.
/// 생성: 현재 제품 코드에 이 설정을 게시하는 Producer는 없다. 테스트/외부 등록 계약과 실제 초기화 구현을 구분한다.
/// 이용: MinerExecutionSystem(Execution)이 존재하면 IsResourceInfinite를 읽고 없으면 유한 모드로 처리한다.
/// 제거: 개별 채굴 완료로 소비하지 않는다. 현재 런타임 제거/변경 Writer는 없으며 World 종료의 수명을 따른다.
/// </summary>
public struct ResourceConfig : IComponentData
{
    public bool IsResourceInfinite; // true일 경우 채굴 시 Amount를 차감하지 않음

    public ResourceConfig(bool isResourceInfinite)
    {
        IsResourceInfinite = isResourceInfinite;
    }
}
