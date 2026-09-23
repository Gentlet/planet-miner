using Unity.Entities;

/// <summary>
/// Architecture V2의 1회성 Transient Command / Request / Event 컴포넌트를 식별하기 위한 마커 인터페이스.
/// 
/// [수명주기 원칙]
/// - 모든 Request 컴포넌트에 Consume-on-Apply 원칙 적용.
/// - 처리 완료 즉시 엔티티 파괴 또는 컴포넌트 비활성화.
/// </summary>
public interface IRequestComponent : IComponentData
{
}

/// <summary>
/// 대상 엔티티에 직접 부착되어 Structural Change(엔티티 생성/삭제) 없이
/// On/Off 토글 방식의 1회성 Request 인터페이스.
/// 
/// [적용 대상]
/// - 기존에 이미 존재하는 엔티티의 상태 변경(예: 아이템 소유권 이전, 이동 요청 등 고빈도 이벤트)
/// </summary>
public interface IEnableableRequest : IRequestComponent, IEnableableComponent
{
}
