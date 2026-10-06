using Unity.Entities;

/// <summary>
/// 역할·목적: 벨트 타일의 실제 이동 속도를 제공하여 해당 타일 위 아이템의 진행량을 계산한다.
/// 부착 엔티티: BuildingType=Belt인 완공 건물 엔티티다.
/// 생성: BuildingLifecycleUtility가 설정 속도를 GameConstants.MaxBeltSpeed 상한으로 제한하여 건물 생성 ECB에 붙인다.
/// 이용: BeltSpatialSyncSystem(Synchronization)이 Speed를 인덱스에 복사하고 BeltMovementDecisionSystem(Decision)이 벨트 이동 판단에 읽는다.
/// 제거: BuildingLifecycleApplySystem(StateApply)이 철거를 기록하여 EndStateApply에서 벨트 엔티티와 함께 삭제한다.
/// </summary>
public struct BeltComponent : IComponentData
{
    /// <summary>
    /// 벨트의 기본 이동 속도 (초당 타일 진행 거리, 기본값: 2.0f tiles/sec).
    /// </summary>
    public float Speed;

    public BeltComponent(float speed = 2.0f)
    {
        Speed = speed;
    }
}

/// <summary>
/// 역할·목적: 아이템이 벨트에서 이동 중인지와 현재 타일의 실제 진행도를 소유하는 영속 enableable 상태다.
/// 부착 엔티티: 벨트 건물이 아니라 아이템 실물 엔티티다. 활성은 벨트 이동 중, 비활성은 바닥/수납 상태를 뜻한다.
/// 생성: ItemLifecycleUtility가 아이템 생성 ECB에 비활성으로 준비하고 벨트 입출고·라우팅 인계가 활성화/진행도를 설정한다.
/// 이용: BeltMovementDecisionSystem(Decision)이 읽고 BeltMovementExecutionSystem(Execution)이 진행·다음 셀 이동을 반영한다.
/// BuildingItemStorageApplySystem/ RoutingApplySystem/ItemOwnershipApplySystem(StateApply)은 수납·인계 시 진행도와 enable 상태를 갱신한다.
/// 제거: 이동 종료는 비활성화하며 컴포넌트는 유지한다. 실물 엔티티 삭제 시 함께 제거한다.
/// </summary>
public struct BeltMovementState : IComponentData, IEnableableComponent
{
    /// <summary>
    /// 현재 벨트 타일 내 진행률 (0.0f = 타일 입구, 1.0f = 타일 출구/다음 벨트 인계 지점).
    /// </summary>
    public float Progress;

    public BeltMovementState(float progress = 0.0f)
    {
        Progress = progress;
    }
}
