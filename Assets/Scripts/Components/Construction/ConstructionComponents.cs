using System;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 현장의 바닥 정리 대기와 취소 상태를 비트로 구분한다.
/// 부착 엔티티: 열거형 자체는 붙이지 않고 ConstructionSite.Flags에 포함한다.
/// 생성·이용: BuildingPlacementCommandSystem(Command)이 초기 바닥 상태를 설정하고 ConstructionCancelCommandSystem(Command)이 Cancelled를 표시한다. ConstructionLifecycleApplySystem(StateApply)이 현재 월드 실물로 AwaitingItemClearance를 매 틱 설정/해제한다.
/// 제거: 플래그 변경은 상태 값 갱신이며 현장 엔티티 삭제 시 함께 사라진다. 취소 현장은 완공 대상에서 제외한다.
/// </summary>
[Flags]
public enum ConstructionSiteFlags : byte
{
    None = 0,

    /// <summary>
    /// 공사 현장 바닥에 월드 아이템이 잔류하여 드론의 회수 완료를 대기 중인 상태.
    /// 회수가 완료되기 전까지는 완공으로 전환되지 않음.
    /// </summary>
    AwaitingItemClearance = 1 << 0,

    /// <summary>
    /// 공사 현장이 취소되어 파괴 진행 중인 상태. 완공 전환 대상에서 즉시 제외됨.
    /// </summary>
    Cancelled = 1 << 1
}

/// <summary>
/// 역할·목적: 완공 전 현장의 목표 건물 종류와 취소/바닥 정리 대기 상태를 보관한다.
/// 부착 엔티티: 공사 현장. BuildingType(ConstructionSite), 위치·방향·기본 footprint, 자재 요구/실물 버퍼와 함께 사용한다.
/// 생성: BuildingPlacementCommandSystem이 승인한 배치 후보로 만들며 EndCommand에서 실체화한다.
/// 이용: DroneTaskDecisionSystem은 공급·회수 대상을 찾고, ConstructionCancelCommandSystem은 취소를 표시한다.
/// ConstructionLifecycleApplySystem(StateApply)은 현재 월드 실물로 차단 플래그를 갱신하고 도착 자재의 충족 여부로 완공을 판단한다.
/// BuildingSpatialSyncSystem(Synchronization)은 현장도 건물 크기만큼 점유 등록한다. 진행도/도착 비율은 보관하지 않는다.
/// 제거: 취소 시 EndCommand, 완공 건물 생성 성공 시 자재와 함께 EndBuilding에서 현장을 삭제한다. 생성 실패 시 보존한다.
/// </summary>
public struct ConstructionSite : IComponentData
{
    /// <summary>
    /// 완공 시 생성될 건물의 목표 타입.
    /// </summary>
    public BuildingTypeEnum TargetBuildingType;

    /// <summary>
    /// 공사 현장 상태 플래그.
    /// </summary>
    public ConstructionSiteFlags Flags;

    public ConstructionSite(BuildingTypeEnum targetBuildingType, ConstructionSiteFlags flags = ConstructionSiteFlags.None)
    {
        TargetBuildingType = targetBuildingType;
        Flags = flags;
    }
}

/// <summary>
/// 역할·목적: 품목별 완공 요구량과 실제 도착량, 공급이 약속된 예약량을 구분해 중복 공급을 막는다.
/// 부착 엔티티: 공사 현장의 버퍼. 실물 엔티티 참조와 Owner는 StoredItemElement/ItemOwnership이 별도로 보관한다.
/// 생성: BuildingPlacementCommandSystem이 건물 설정의 자재 요구량을 복사하며 도착/예약량은 0으로 시작한다.
/// 이용: DroneTaskDecisionSystem(Decision)은 남은 수량을 읽고 ConstructionSupplyReservationSystem(Reservation)은 예약을 확보/해제한다.
/// DroneTaskAssignmentPublishSystem(StateApply)은 미공개 예약을 롤백하고 DroneTaskLifecycleApplySystem은 인계 성공분의 도착/예약량을 정산한다.
/// ConstructionLifecycleApplySystem(StateApply)은 IsSatisfied를 읽는다. 예약만으로 자재가 도착하거나 완공한 것으로 판단하지 않는다.
/// 제거: 취소/완공으로 현장이 삭제될 때 버퍼도 함께 제거한다. 수량 갱신만으로 실물 엔티티를 소비하지 않는다.
/// </summary>
public struct ConstructionMaterialRequirementElement : IBufferElementData
{
    public ItemTypeEnum ItemType;
    public int RequiredQuantity;  // 완공에 필요한 총 수량
    public int DeliveredQuantity; // 현장에 도착/수령된 실제 수량
    public int ReservedQuantity;  // 활성 개별 예약 + 아직 배정으로 인계되지 않은 공개 대기 현장 예약 합계.

    // 순수 계산 프로퍼티 (상태 부작용 없음)
    public int RemainingRequired => math.max(0, RequiredQuantity - DeliveredQuantity);
    public int RemainingToReserve => math.max(0, RequiredQuantity - (DeliveredQuantity + ReservedQuantity));
    public bool IsSatisfied => DeliveredQuantity >= RequiredQuantity;
    public bool IsFullyReserved => (DeliveredQuantity + ReservedQuantity) >= RequiredQuantity;

    public ConstructionMaterialRequirementElement(
        ItemTypeEnum itemType,
        int requiredQuantity,
        int deliveredQuantity = 0,
        int reservedQuantity = 0)
    {
        ItemType = itemType;
        RequiredQuantity = requiredQuantity;
        DeliveredQuantity = deliveredQuantity;
        ReservedQuantity = reservedQuantity;
    }
}
