using Unity.Entities;

/// <summary>
/// 역할·목적: 명시적 소비 경계를 가진 일회성 요청 타입을 구분하는 인터페이스다. 프레임 Decision과 구분한다.
/// 부착 엔티티: 인터페이스 자체를 붙이지 않으며 이를 구현한 요청 컴포넌트가 독립 요청 또는 대상 엔티티에 붙는다.
/// 생성·이용: 각 Producer와 Command/StateApply Consumer가 구체 요청의 계약대로 작성·소비한다. 인터페이스 자체가 자동 처리하지 않는다.
/// 제거: 구체 타입에 따라 엔티티 삭제·컴포넌트 제거·enable 비활성화를 수행한다. 준비 대기·공개 결과는 각 타입의 수명 규칙을 따른다.
/// </summary>
public interface IRequestComponent : IComponentData
{
}

/// <summary>
/// 역할·목적: 기존 대상에 미리 준비한 요청을 enable 상태로 발행·소비하는 마커다.
/// 부착 엔티티: 인터페이스 자체는 부착하지 않는다. DestroyItemRequest·TransferOwnershipRequest 등 구현 컴포넌트가 아이템에 붙는다.
/// 생성·이용: 초기화가 요청 컴포넌트를 비활성으로 준비하고 Producer가 활성화한다. ItemLifecycleApplySystem/ItemOwnershipApplySystem(StateApply)이 활성 요청을 처리한다.
/// 제거: Transfer는 컴포넌트를 유지한 채 비활성화하고 Destroy는 실물 엔티티를 EndStateApply에 삭제한다. 토글과 구조 삭제를 구분한다.
/// </summary>
public interface IEnableableRequest : IRequestComponent, IEnableableComponent
{
}
