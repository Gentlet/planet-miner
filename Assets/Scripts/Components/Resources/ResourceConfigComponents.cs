using Unity.Entities;

/// <summary>
/// 자원 관련 전역 설정 싱글톤 컴포넌트.
/// 무한 매장량 모드 등의 런타임 규칙을 제공.
/// </summary>
public struct ResourceConfig : IComponentData
{
    public bool IsResourceInfinite; // true일 경우 채굴 시 Amount를 차감하지 않음

    public ResourceConfig(bool isResourceInfinite)
    {
        IsResourceInfinite = isResourceInfinite;
    }
}
