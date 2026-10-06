using Unity.Entities;

/// <summary>
/// 역할·목적: 공급·회수 작업의 공통 생성 순번을 발급하는 원본이며 현장 PlacementStamp와 구분한다.
/// 부착 엔티티: 작업·현장·수행자와 별도인 World 단일 순번 엔티티다.
/// 생성: DroneTaskExecutionSystem.OnCreate가 DroneTaskCreationUtility.GetOrCreateSequence를 호출하여 NextValue=1로 만들거나 기존 상태를 재사용한다.
/// 이용: Execution의 DroneTaskExecutionSystem만 생성 의도 처리 시 번호를 소비하고 상태를 갱신한다.
/// 제거: 작업 종료로 소비하거나 삭제하지 않으며 World 수명 동안 유지한다. 양수 생성 번호를 재사용하지 않는다.
/// </summary>
public struct DroneTaskSequence : IComponentData
{
    public ulong NextValue;
}
