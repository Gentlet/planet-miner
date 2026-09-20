using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// 자원 노드의 그리드 공간 좌표(GridPosition -> Resource Entity) 고속 조회를 위한 Unmanaged 싱글톤 인덱스.
/// 
/// [책임]
/// - 채굴기(Miner)가 자신의 Footprint 하부에 있는 자원 노드를 O(1)에 조회할 수 있도록 지원합니다.
/// - 순수 Resource(Map)만을 관리하며, 의존성 제어는 ResourceSpatialIndexFence에 위임합니다.
/// </summary>
public struct ResourceSpatialIndex : IComponentData
{
    public NativeParallelHashMap<int2, Entity> Map;

    /// <summary>
    /// Job/Burst 내부에서 안전하게 읽기 전용으로 전달할 수 있는 뷰를 반환합니다.
    /// </summary>
    public NativeParallelHashMap<int2, Entity>.ReadOnly AsReadOnly() => Map.AsReadOnly();

    /// <summary>
    /// 해당 좌표에 자원 노드가 존재하는지 확인하고 자원 엔티티를 가져옵니다.
    /// </summary>
    public bool TryGetResource(int2 position, out Entity resourceEntity)
    {
        return Map.TryGetValue(position, out resourceEntity);
    }

    /// <summary>
    /// 해당 좌표에 자원 노드가 존재하는지 여부를 반환합니다.
    /// </summary>
    public bool HasResourceAt(int2 position)
    {
        return Map.ContainsKey(position);
    }
}

/// <summary>
/// ResourceSpatialIndex에 대한 동시성 Job 의존성을 제어하는 싱글톤 컴포넌트.
/// 
/// [책임]
/// - Reader-Writer Lock 모델을 기반으로 JobHandle 의존성을 관리합니다.
/// - Reader: 마지막 Writer JobHandle(_lastWriter)을 선행 의존성으로 취하고, 자신의 JobHandle을 _readers에 결합합니다.
/// - Writer: 이전 Writer 및 모든 Readers의 완료(_lastWriter + _readers)를 선행 의존성으로 취하고, 새 Writer JobHandle로 갱신하며 이전 _readers를 초기화합니다.
/// - Complete(): Capacity 재할당 등 메인 스레드의 즉시 접근이 필요한 경우에 한해 제한적으로 호출합니다.
/// </summary>
public struct ResourceSpatialIndexFence : IComponentData
{
    private JobHandle _lastWriter;
    private JobHandle _readers;

    /// <summary>
    /// Reader Job이 대기해야 하는 마지막 Writer JobHandle을 반환합니다.
    /// </summary>
    public JobHandle GetReaderDependency() => _lastWriter;

    /// <summary>
    /// Reader JobHandle을 Fence의 Reader 목록에 결합합니다.
    /// </summary>
    public void AddReader(JobHandle readerHandle)
    {
        _readers = JobHandle.CombineDependencies(_readers, readerHandle);
    }

    /// <summary>
    /// Writer Job이 대기해야 하는 선행 의존성(마지막 Writer + 모든 Readers)을 반환합니다.
    /// </summary>
    public JobHandle GetWriterDependency()
    {
        return JobHandle.CombineDependencies(_lastWriter, _readers);
    }

    /// <summary>
    /// 새 Writer JobHandle을 등록하고 이전 Readers 목록을 초기화합니다.
    /// </summary>
    public void SetWriter(JobHandle writerHandle)
    {
        _lastWriter = writerHandle;
        _readers = default;
    }

    /// <summary>
    /// Capacity 재할당 등 메인 스레드의 직접 조작이 필요한 경우 모든 작업을 동기 완료합니다.
    /// </summary>
    public void Complete()
    {
        JobHandle.CombineDependencies(_lastWriter, _readers).Complete();
        _lastWriter = default;
        _readers = default;
    }
}
