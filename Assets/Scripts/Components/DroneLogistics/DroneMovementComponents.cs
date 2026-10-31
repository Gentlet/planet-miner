using Unity.Entities;
using Unity.Mathematics;

/// <summary>초기화가 한 번 검증/게시하는 이동 상수다. 실제 충전량은 후속 전력/충전 소유자가 전달한다.</summary>
public struct DroneMovementConfig : IComponentData
{
    public float Speed;
    public float EmergencySpeed;
    public float EnergyPerDistance;
}

public enum DroneFlightPurposeEnum : byte
{
    None,
    Task,
    ChargingStop,
    EmergencyReturn,
    WaitingForCharge,
    WaitingForStation,
    AwaitingAction,
    NormalReturn,
    StationRelocation,
    Count
}

public enum DroneMovementFailureEnum : byte
{
    None,
    InvalidGoal,
    EnergyUnavailable,
    Count
}

/// <summary>이동 Execution이 유지하는 충전 경유/귀환 목적지다. 작업 상태/위치/배터리를 복제하지 않는다.</summary>
public struct DroneMovementState : IComponentData
{
    public Entity ChargingStation;
    public Entity ReturnStation;
    // 정상 귀환은 다음 작업으로 전환할 수 있다. 비상 귀환/작업 충전 경유는 기존 배차 차단을 유지한다.
    public bool IsNormalReturn;
}

/// <summary>일반 Flight와 재배치 Decision이 대상별로 나눠 쓰는 이동 의도다. Execution은 충전/실물 성공을 만들지 않는다.</summary>
public struct DroneMovementDecision : IComponentData
{
    public bool Ready;
    public DroneFlightPurposeEnum Purpose;
    public float2 Target;
    public Entity Station;
    public Entity Assignment;
    public uint AssignmentRevision;
    public DroneActionKindEnum Action;
    public DroneMovementFailureEnum Failure;
    public bool ClearFailure;
}

/// <summary>Execution의 도착/중단 결과다. 중단은 기존 Lifecycle이 확인할 때까지 같은 배정 버전으로 유지한다.</summary>
public struct DroneMovementResult : IComponentData
{
    public Entity Assignment;
    public uint AssignmentRevision;
    public DroneActionKindEnum Action;
    public bool Arrived;
    public DroneMovementFailureEnum Failure;
}

/// <summary>이번 틱의 경로 판단이다. Task Execution은 Retain 대상만 기존 EndSimulation에 공개한다.</summary>
public struct DroneRouteEvaluationDecision : IComponentData
{
    public bool Ready;
    public DroneRouteEvaluationResult Result;
}
