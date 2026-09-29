using Unity.Collections;
using Unity.Entities;

/// <summary>Initialization 검증을 통과한 월드. 런타임 프리팹 DB는 이후 변경하지 않는다.</summary>
public struct PrefabDatabaseReady : IComponentData
{
}

/// <summary>복구 없이 시뮬레이션을 중단하는 진단 기록. 월드를 다시 초기화해야 한다.</summary>
public struct SimulationFatalError : IComponentData
{
    public FixedString128Bytes Message;
}
