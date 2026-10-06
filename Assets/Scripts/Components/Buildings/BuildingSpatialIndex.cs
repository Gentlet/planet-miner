using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 점유 셀의 건물/현장 엔티티·종류·방향을 공간 조회에 전달하는 요약 값이다.
/// 부착 엔티티: 독립 ECS 컴포넌트가 아니며 BuildingSpatialIndex.Map의 셀별 값이다.
/// 생성·이용: BuildingSpatialSyncSystem(Synchronization)이 현재 ECS 원본에서 회전 footprint 셀마다 등록하고 배치 Command와 건물 입고 Decision이 읽는다.
/// 제거: 매 Synchronization Clear/재등록과 Native 맵 Dispose의 수명을 따른다. 요약 값 수정으로 실제 건물 상태를 바꾸지 않는다.
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
/// 역할·목적: 회전 footprint의 모든 점유 셀에서 건물/현장을 조회하는 파생 공간 인덱스다. ECS 좌표/소유권 원본을 대체하지 않는다.
/// 부착 엔티티: BuildingSpatialIndexFence와 함께 있는 World 단일 인덱스 엔티티다.
/// 생성: BuildingSpatialSyncSystem.OnCreate가 Persistent Native 맵을 생성하여 붙인다.
/// 이용: BuildingSpatialSyncSystem(Synchronization)이 매 틱 Clear 후 현재 ECS 원본을 재등록하고 BuildingPlacementCommandSystem(Command)의 배치 검증과 BuildingItemInputDecisionSystem(Decision)이 읽는다.
/// 제거: 엔티티 개별 삭제를 즉시 맵에서 반영하지 않고 다음 Synchronization에 재구축한다. SyncSystem.OnDestroy가 Fence 완료 후 Native 맵을 Dispose한다.
/// 동일 틱 구조 변경은 아직 이 맵에 없을 수 있으며 Reader는 BuildingSpatialIndexFence 의존성을 등록해야 한다.
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
/// 역할·목적: BuildingSpatialIndex의 Native 맵 접근에 Reader/Writer Job 의존성을 결합한다. ECS 컴포넌트 의존성만으로 맵 접근을 보장하지 않는다.
/// 부착 엔티티: BuildingSpatialIndex와 같은 World 단일 인덱스 엔티티다.
/// 생성: BuildingSpatialSyncSystem.OnCreate가 기본 JobHandle 상태로 함께 붙인다.
/// 이용: 맵 Reader는 마지막 Writer를 선행으로 읽고 자신의 핸들을 AddReader한다. BuildingSpatialSyncSystem(Synchronization)은 이전 Writer와 모든 Reader를 기다린 뒤 SetWriter한다.
/// Complete는 메인 스레드 직접 접근·용량 재할당·Dispose 전에 전체 등록 작업을 완료한다.
/// 제거: Complete/SetWriter가 이미 반영한 Reader 핸들을 초기화한다. 컴포넌트 자체는 인덱스 엔티티/World 수명을 따른다.
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
