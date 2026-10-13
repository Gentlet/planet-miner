using Unity.Entities;

/// <summary>
/// Fence 컴포넌트를 구분하기 위한 마커 인터페이스.
/// 각 구현 타입이 JobHandle 상태와 Reader/Writer 의존성 관리를 소유한다.
/// </summary>
public interface IFenceComponent : IComponentData
{
}
