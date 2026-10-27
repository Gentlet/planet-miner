using Unity.Entities;

public enum DroneTaskRequestOperationEnum : byte
{
    None,
    Create,
    Cancel
}

public enum DroneTaskRequestStatusEnum : byte
{
    None,
    Created,
    Existing,
    Cancelled,
    Busy,
    Rejected,
    Unsupported
}

/// <summary>외부 Submit의 불변 입력. 접수 순번은 작업 CreationSequence와 별개이며 EndSimulation에 소비한다.</summary>
public struct DroneTaskRequest : IRequestComponent
{
    public DroneTaskRequestOperationEnum Operation;
    public DroneLogisticsTaskKindEnum Kind;
    public Entity Target;
    public ItemTypeEnum ItemType;
    public int Quantity;
    public Entity Task;
    public ulong Sequence;
}

/// <summary>Decision만 작성하는 요청 판단. 실제 생성/결과 기록이나 영속 사용자 차단 상태와 구분한다.</summary>
public struct DroneTaskRequestDecision : IComponentData
{
    public DroneTaskRequestStatusEnum Status;
    public Entity Task;
    public Entity CreatingRequest;
    public DroneRecoveryReasonEnum RecoveryReason;
}

/// <summary>Execution만 작성한다. 생성/기존 작업 결과의 ECB 기록 완료이며 실제 공개 완료가 아니다. Lifecycle이 차단 해제에 소비한다.</summary>
public struct DroneTaskRequestExecution : IComponentData
{
    public bool CreationRecorded;
}

/// <summary>처리한 요청 Entity에 EndSimulation 공개. Created/Existing의 Task는 생성 ECB 안에서 remap된다.</summary>
public struct DroneTaskRequestResult : IComponentData
{
    public DroneTaskRequestOperationEnum Operation;
    public DroneTaskRequestStatusEnum Status;
    public Entity Task;
}

public struct DroneTaskRequestSequence : IComponentData
{
    public ulong NextValue;
}

/// <summary>공급/일반 운반은 종류/대상/품목, 회수는 실물 Entity가 키다. 회수 ItemType은 None으로 정규화한다.</summary>
public struct DroneTaskGenerationKey
{
    public DroneLogisticsTaskKindEnum Kind;
    public Entity Target;
    public ItemTypeEnum ItemType;
    public bool Equals(DroneTaskGenerationKey other) => Kind == other.Kind && Target == other.Target && ItemType == other.ItemType;
    public static DroneTaskGenerationKey From(DroneLogisticsTaskKindEnum kind, Entity target, ItemTypeEnum itemType) => new DroneTaskGenerationKey
    {
        Kind = kind,
        Target = target,
        ItemType = kind == DroneLogisticsTaskKindEnum.WorldItemRecovery ? ItemTypeEnum.None : itemType
    };
}

/// <summary>후보 singleton의 사용자 취소 원본. Lifecycle만 EndSimulation에 게시/해제하며 대상 소멸 시 회수한다.</summary>
[InternalBufferCapacity(0)]
public struct DroneTaskGenerationBlockElement : IBufferElementData
{
    public DroneTaskGenerationKey Key;
}

/// <summary>Decision이 매 틱 재작성하는 제어 의도. 현재 틱 생성/후보 판단에만 쓰며 영속 차단을 대신하지 않는다.</summary>
[InternalBufferCapacity(0)]
public struct DroneTaskGenerationDecisionElement : IBufferElementData
{
    public DroneTaskGenerationKey Key;
    public bool Blocked;
    public Entity CancelledTask;
}
