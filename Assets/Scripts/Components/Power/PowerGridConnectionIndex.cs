using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

/// <summary>
/// 참가 Entity → 공개 PowerGridConnection의 파생 조회 자료. 위치/점유 원본을 복제하지 않는다.
/// PowerGridConnectionSyncSystem이 Fence와 같은 singleton에 생성하고 Synchronization에서 재구축/종료 시 해제한다.
/// Reader Job은 Fence 의존성을 결합하고 AddReader로 등록한다. 동기 조회는 마지막 Writer를 완료해야 한다.
/// </summary>
public struct PowerGridConnectionIndex : IComponentData
{
    public NativeParallelHashMap<Entity, PowerGridConnection> Map;

    public NativeParallelHashMap<Entity, PowerGridConnection>.ReadOnly AsReadOnly() => Map.AsReadOnly();
}

/// <summary>공개 연결 맵의 Writer·Reader를 추적한다. ECS 의존성과 별도로 Native 맵 접근을 등록한다.</summary>
public struct PowerGridConnectionIndexFence : IFenceComponent
{
    private JobHandle _lastWriter;
    private JobHandle _readers;

    public JobHandle GetReaderDependency() => _lastWriter;

    public void AddReader(JobHandle handle)
    {
        _readers = JobHandle.CombineDependencies(_readers, handle);
    }

    public JobHandle GetWriterDependency() => JobHandle.CombineDependencies(_lastWriter, _readers);

    public void SetWriter(JobHandle handle)
    {
        _lastWriter = handle;
        _readers = default;
    }

    public void Complete()
    {
        GetWriterDependency().Complete();
        _lastWriter = default;
        _readers = default;
    }
}
