using Unity.Entities;

/// <summary>
/// 역할·목적: 실제 BeltMovementState와 분리한 이번 틱의 진행 계획·정체 여부다. 진행 원본이나 일회성 요청이 아니다.
/// 부착 엔티티: BeltMovementState를 가진 아이템 실물 엔티티다.
/// 생성: ItemLifecycleUtility가 아이템 생성 ECB에 준비하고 BeltMovementDecisionSystem(Decision)이 매 틱 계획을 작성한다.
/// 이용: BeltMovementExecutionSystem(Execution)이 계획을 읽어 실제 진행도·좌표를 갱신한다. 대상 여부는 BeltMovementState의 enable 상태로 정한다.
/// 제거: 틱마다 컴포넌트를 삭제하지 않고 다음 Decision이 값을 덮어쓴다. 실물 삭제 시 함께 제거한다.
/// </summary>
public struct BeltMovementDecision : IComponentData
{
    /// <summary>
    /// 이번 프레임에 전진할 확정 진행률 (Progress 스케일 [0.0 ~ 1.0]).
    /// </summary>
    public float PlannedProgress;

    /// <summary>
    /// 앞선 아이템과의 간격 부족이나 벨트 끝 정체(Backpressure)로 인해 전진하지 못한 상태인지 여부.
    /// </summary>
    public bool IsBlocked;

    public BeltMovementDecision(float plannedProgress = 0.0f, bool isBlocked = false)
    {
        PlannedProgress = plannedProgress;
        IsBlocked = isBlocked;
    }
}
