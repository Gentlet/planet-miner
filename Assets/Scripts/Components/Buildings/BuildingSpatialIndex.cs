using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// 특정 그리드 좌표에 위치한 건물의 요약 정보 (Unmanaged).
/// </summary>
public struct BuildingInfo
{
    public Entity Entity;
    public BuildingTypeEnum Type;
    public DirectionEnum Direction;

    public BuildingInfo(Entity entity, BuildingTypeEnum type, DirectionEnum direction)
    {
        Entity = entity;
        Type = type;
        Direction = direction;
    }
}

/// <summary>
/// 건물의 그리드 공간 좌표(GridPosition -> BuildingInfo) 고속 조회를 위한 Unmanaged 싱글톤 인덱스.
/// 
/// [책임]
/// - 다중 타일을 점유하는 건물의 각 좌표(int2)를 키로 하여 해당 위치의 건물 정보를 O(1)에 조회.
/// - 순수 Resource(Map)만을 관리하며, 의존성 제어는 BuildingSpatialIndexFence에 위임.
/// </summary>
public struct BuildingSpatialIndex : IComponentData
{
    public NativeParallelHashMap<int2, BuildingInfo> Map;

    /// <summary>
    /// Job/Burst 내부에서 안전하게 읽기 전용으로 전달할 수 있는 뷰를 반환.
    /// </summary>
    public NativeParallelHashMap<int2, BuildingInfo>.ReadOnly AsReadOnly() => Map.AsReadOnly();

    /// <summary>
    /// 해당 좌표에 건물이 존재하는지 확인하고 건물 정보를 가져옵니다.
    /// </summary>
    public bool TryGetBuilding(int2 position, out BuildingInfo info)
    {
        return Map.TryGetValue(position, out info);
    }

    /// <summary>
    /// 해당 좌표에 건물이 존재하는지 여부를 반환.
    /// </summary>
    public bool HasBuildingAt(int2 position)
    {
        return Map.ContainsKey(position);
    }
}

/// <summary>
/// BuildingSpatialIndex에 대한 동시성 Job 의존성을 제어하는 싱글톤 컴포넌트.
/// 
/// [책임]
/// - Reader-Writer Lock 모델을 기반으로 JobHandle 의존성을 관리.
/// - Reader: 마지막 Writer JobHandle(_lastWriter)을 선행 의존성으로 취하고, 자신의 JobHandle을 _readers에 결합.
/// - Writer: 이전 Writer 및 모든 Readers의 완료(_lastWriter + _readers)를 선행 의존성으로 취하고, 새 Writer JobHandle로 갱신하며 이전 _readers를 초기화.
/// - Complete(): Capacity 재할당 등 메인 스레드의 즉시 접근이 필요한 경우에 한해 제한적으로 호출.
/// </summary>
public struct BuildingSpatialIndexFence : IComponentData
{
    private JobHandle _lastWriter;
    private JobHandle _readers;

    /// <summary>
    /// Reader Job이 대기해야 하는 마지막 Writer JobHandle을 반환.
    /// </summary>
    public JobHandle GetReaderDependency() => _lastWriter;

    /// <summary>
    /// Reader JobHandle을 Fence의 Reader 목록에 결합.
    /// </summary>
    public void AddReader(JobHandle readerHandle)
    {
        _readers = JobHandle.CombineDependencies(_readers, readerHandle);
    }

    /// <summary>
    /// Writer Job이 대기해야 하는 선행 의존성(마지막 Writer + 모든 Readers)을 반환.
    /// </summary>
    public JobHandle GetWriterDependency()
    {
        return JobHandle.CombineDependencies(_lastWriter, _readers);
    }

    /// <summary>
    /// 새 Writer JobHandle을 등록하고 이전 Readers 목록을 초기화.
    /// </summary>
    public void SetWriter(JobHandle writerHandle)
    {
        _lastWriter = writerHandle;
        _readers = default;
    }

    /// <summary>
    /// Capacity 재할당 등 메인 스레드의 직접 조작이 필요한 경우 모든 작업을 동기 완료.
    /// </summary>
    public void Complete()
    {
        JobHandle.CombineDependencies(_lastWriter, _readers).Complete();
        _lastWriter = default;
        _readers = default;
    }
}
