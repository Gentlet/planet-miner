using Unity.Collections;
using Unity.Entities;

/// <summary>
/// 역할·목적: Decision에서 접수된 행동 신호가 현재 배정에 유효한지 판단한다.
/// 입력: 행동 요청 엔티티의 접수 순번·배정/행동 번호, 수행자 관측·적재 상태와 현재 배정/예약.
/// 출력·소유권: 같은 요청 엔티티의 DroneItemTransferDecision.CanExecute만 갱신한다.
/// 이용: Execution이 허용된 요청의 기존 실물·공간을 인계 계획으로 제한하고 Lifecycle Apply가 최종 반영한다.
/// 정리: 요청과 계획은 Lifecycle이 EndStateApply에 제거하며 결과는 외부 소비까지 남긴다.
/// 이번 틱에 새로 수집할 적재품은 다음 틱부터 판단하며 실물·소유권·예약·배정은 여기서 변경하지 않는다.
/// </summary>
[UpdateInGroup(typeof(DecisionGroup))]
public partial struct DroneItemTransferDecisionSystem : ISystem
{
    private EntityQuery _requests;

    public void OnCreate(ref SystemState state)
    {
        _requests = state.GetEntityQuery(ComponentType.ReadOnly<DroneActionReadyRequest>(),
            ComponentType.ReadWrite<DroneItemTransferDecision>());
    }

    public void OnUpdate(ref SystemState state)
    {
        state.CompleteDependency();
        var manager = state.EntityManager;
        using var requests = _requests.ToEntityArray(Allocator.Temp);
        // 식별/배정 검증은 행동 신호의 자격 검사다. 공급원 재고를 확보하거나 실제 도착·인계를 확정하지 않는다.
        // 이미 결과가 있는 요청은 다시 실행하지 않고 외부 소비를 기다린다.
        for (int i = 0; i < requests.Length; i++)
        {
            var request = manager.GetComponentData<DroneActionReadyRequest>(requests[i]);
            bool canExecute = !manager.HasComponent<DroneItemTransferResult>(requests[i]) &&
                DroneItemTransferValidationUtility.TryValidate(manager, request, out _);
            manager.SetComponentData(requests[i], new DroneItemTransferDecision { CanExecute = canExecute });
        }
    }
}
