using Unity.Entities;

/// <summary>
/// Fence 컴포넌트를 구분하기 위한 마커 인터페이스.
/// 각 Fence 컴포넌트는 독립적인 NativeContainerFence State를 보유한다.
/// </summary>
public interface IFenceComponent : IComponentData
{
}
