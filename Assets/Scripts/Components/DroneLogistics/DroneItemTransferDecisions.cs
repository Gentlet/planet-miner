using Unity.Entities;

/// <summary>
/// 역할·목적: 행동 요청의 실행 자격과 물류 반영 전 입력으로 산정한 수량·공간 상한을 전달하는 한 틱 파생 계획이다.
/// 부착 엔티티: DroneActionReadyRequest가 있는 행동 요청 엔티티다. 실물·재고·예약의 원본을 대체하지 않는다.
/// 생성: DroneActionRequestUtility.Submit이 기본 상태로 붙이며 Decision의 DroneItemTransferDecisionSystem이 CanExecute를 작성한다.
/// 이용: Execution의 DroneItemTransferExecutionSystem이 Prepared와 수량 상한을 준비하고 StateApply의 DroneTaskLifecycleApplySystem이 현재 원본 재검사 후 적용한다.
/// 제거: Lifecycle이 요청 처리 후 EndStateApply에 컴포넌트 제거를 기록한다. 같은 틱 신규 입고·출고 여유는 계획에 더하지 않는다.
/// </summary>
public struct DroneItemTransferDecision : IComponentData
{
    public bool CanExecute;
    public bool Prepared;
    public int WantedQuantity;
    public int MaximumQuantity;
}

/// <summary>
/// 역할·목적: 물류 반영 전에 해당 행동 요청이 사용할 수 있었던 전체 실물 ID를 보존하는 파생 후보 목록이다.
/// 부착 엔티티: DroneActionReadyRequest와 DroneItemTransferDecision이 있는 행동 요청 엔티티의 버퍼다.
/// 생성: DroneActionRequestUtility.Submit이 빈 버퍼를 붙이고 Execution의 DroneItemTransferExecutionSystem이 기존 실물 후보를 채운다.
/// 이용: StateApply의 DroneTaskLifecycleApplySystem이 이 목록 안의 실물만 생존·현재 Owner·Destroy 여부를 재검사하여 인계한다.
/// 제거: Lifecycle이 요청 처리 후 EndStateApply에 버퍼 제거를 기록한다.
/// 같은 틱 신규 입고를 후보에 추가하지 않으며 재고 원본이나 공급원 예약으로 사용하지 않는다.
/// </summary>
[InternalBufferCapacity(0)]
public struct DroneItemTransferItemDecisionElement : IBufferElementData
{
    public Entity ItemEntity;
}

/// <summary>
/// 역할·목적: 물류 반영 전 목적지 보관 슬롯의 품목·개수를 전달하여 이전 틱 입력 범위 안에서 인계량을 제한한다.
/// 부착 엔티티: DroneActionReadyRequest와 DroneItemTransferDecision이 있는 행동 요청 엔티티의 버퍼다.
/// 생성: DroneActionRequestUtility.Submit이 빈 버퍼를 붙이고 Execution의 DroneItemTransferExecutionSystem이 슬롯 입력을 복사한다.
/// 이용: StateApply의 DroneTaskLifecycleApplySystem이 목적지별 공유 슬롯 예산을 만들고 실제 성공분만 소비한다.
/// 제거: Lifecycle이 요청 처리 후 EndStateApply에 버퍼 제거를 기록한다.
/// 같은 틱 출고로 생긴 여유를 더하지 않는 파생 입력이며 영속 보관 공간 예약이 아니다.
/// </summary>
[InternalBufferCapacity(0)]
public struct DroneItemTransferSlotDecisionElement : IBufferElementData
{
    public int SlotIndex;
    public ItemTypeEnum ItemType;
    public int ItemCount;
}
