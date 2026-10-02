using Unity.Entities;

public enum ConstructionMaterialDeliveryStateEnum : byte
{
    PendingRegistration,
    Ready,
    Supplied,
    Rejected,
    Cancelled
}

public enum ConstructionMaterialDeliveryOutcomeEnum : byte
{
    Registered,
    Supplied,
    Cancelled,
    InvalidSite,
    InvalidSource,
    InvalidItem,
    MaterialNotRequired,
    RequirementSatisfied,
    ReservationUnavailable,
    OwnershipMismatch,
    ConflictingTransfer,
    AlreadySupplied,
    DestroyConflict,
    DemolitionConflict,
    SiteClosed,
    ItemAlreadyClaimed
}

/// <summary>
/// 한 실물의 공사 운송 기록. 이 엔티티 자체가 중복 요청을 식별하는 키다.
/// Producer는 목적지/공급원/실물/품목/예약 여부를 생성 시 한 번 지정한다.
/// ConstructionLifecycleApplySystem만 State와 ReservationActive를 변경하고 현장 예약 합계를 적용한다.
/// 공급원은 등록 당시 Storage/DroneStation/MainFacility이며, 운송 중 현재 Owner와 구분한다.
/// Ready는 내부 적용 상태다. 외부 Producer는 EndStateApply 이후 Result를 확인하고 출발/후속 작업한다.
/// 결과 소비자는 ReservationActive가 false인 최종 결과를 확인한 후 기록 엔티티를 삭제한다.
/// 활성 예약 기록을 직접 삭제하지 않고 CancelConstructionMaterialDeliveryRequest를 사용한다.
/// </summary>
public struct ConstructionMaterialDelivery : IComponentData
{
    public Entity TargetSite;
    public Entity SourceBuilding;
    public Entity ItemEntity;
    public ItemTypeEnum ItemType;
    public bool ReserveMaterial;
    public ConstructionMaterialDeliveryStateEnum State;
    public bool ReservationActive;

    public ConstructionMaterialDelivery(
        Entity targetSite, Entity sourceBuilding, Entity itemEntity, ItemTypeEnum itemType, bool reserveMaterial = true)
    {
        TargetSite = targetSite;
        SourceBuilding = sourceBuilding;
        ItemEntity = itemEntity;
        ItemType = itemType;
        ReserveMaterial = reserveMaterial;
        State = ConstructionMaterialDeliveryStateEnum.PendingRegistration;
        ReservationActive = false;
    }
}

/// <summary>
/// 운송 기록에 붙는 적용 결과. 등록 시 시스템이 추가하고 EndStateApply ECB로만 게시/갱신한다.
/// Rejected라도 F-004 충돌이면 ReservationActive가 true일 수 있다. 자동 재시도하지 않는다.
/// Producer는 결과를 관찰할 뿐 Delivered/Reserved를 다시 정산하지 않는다.
/// </summary>
public struct ConstructionMaterialDeliveryResult : IComponentData
{
    public ConstructionMaterialDeliveryStateEnum State;
    public ConstructionMaterialDeliveryOutcomeEnum Outcome;
    public bool ReservationActive;

    public ConstructionMaterialDeliveryResult(
        in ConstructionMaterialDelivery delivery, ConstructionMaterialDeliveryOutcomeEnum outcome)
    {
        State = delivery.State;
        Outcome = outcome;
        ReservationActive = delivery.ReservationActive;
    }
}
