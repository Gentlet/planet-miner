using Unity.Entities;

/// <summary>
/// 역할·목적: 한 World의 행동 신호 접수 순서를 발급하는 원본이며 배정 내부 Action.Sequence와 구분한다.
/// 부착 엔티티: 행동 요청·수행자와 별도인 World 단일 순번 엔티티다.
/// 생성: 외부 입력 경계의 DroneActionRequestUtility.Submit이 첫 접수 때 NextValue=1로 생성하거나 기존 상태를 재사용한다.
/// 이용: Submit만 번호를 증가시킨다. StateApply의 DroneTaskLifecycleApplySystem은 요청에 복사된 ReceiptSequence 순서로 재고·공간 경합을 처리한다.
/// 제거: 개별 요청·배정 종료로 삭제하지 않으며 World 수명 동안 유지한다. 양수 번호를 재사용하지 않는다.
/// </summary>
public struct DroneActionReceiptSequence : IComponentData
{
    public ulong NextValue;
}
