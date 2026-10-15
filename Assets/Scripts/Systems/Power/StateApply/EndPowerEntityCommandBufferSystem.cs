using Unity.Entities;

/// <summary>망 연결과 연료 실물 변경을 Building 이전에 공개한다.</summary>
[UpdateInGroup(typeof(PowerSimulationGroup), OrderLast = true)]
public partial class EndPowerEntityCommandBufferSystem : EntityCommandBufferSystem
{
    protected override void OnUpdate()
    {
        // 검증/소비/공급과 scratch 접근을 모두 완료한 뒤 공통 Fatal·ECB를 공개한다.
        var validationOwner = World.GetOrCreateSystemManaged<PowerValidationInitializationSystem>();
        if (validationOwner.CompleteAndTryConsumeFailure(out var message))
        {
            SimulationFailureUtility.RecordImmediateFailure(EntityManager, message,
                SimulationFailureStopScope.RemainingDomains);
        }
        base.OnUpdate();
    }
}
