using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// 역할·목적: 한 셀의 복수 월드 아이템을 조회하는 파생 공간 인덱스다. ECS 좌표/소유권 원본을 대체하지 않는다.
/// 부착 엔티티: ItemSpatialIndexFence와 함께 있는 World 단일 인덱스 엔티티다.
/// 생성: ItemSpatialSyncSystem.OnCreate가 Persistent Native 맵을 생성하여 붙인다.
/// 이용: ItemSpatialSyncSystem(Synchronization)이 매 틱 Clear 후 현재 ECS 원본을 재등록하고 BuildingPlacementCommandSystem(Command), 벨트·출고·라우팅 Decision, BeltDestinationReservationSystem(Reservation), BuildingLifecycleApplySystem(StateApply)이 읽는다.
/// 제거: 엔티티 개별 삭제를 즉시 맵에서 반영하지 않고 다음 Synchronization에 재구축한다. SyncSystem.OnDestroy가 Fence 완료 후 Native 맵을 Dispose한다.
/// 동일 틱 구조 변경은 아직 이 맵에 없을 수 있으며 Reader는 ItemSpatialIndexFence 의존성을 등록해야 한다.
/// </summary>
public struct ItemSpatialIndex : IComponentData
{
    public NativeParallelMultiHashMap<int2, Entity> Map;

    /// <summary>
    /// Job/Burst 내부에서 안전하게 읽기 전용으로 전달할 수 있는 뷰를 반환.
    /// </summary>
    public NativeParallelMultiHashMap<int2, Entity>.ReadOnly AsReadOnly() => Map.AsReadOnly();

    /// <summary>
    /// 해당 좌표에 첫 번째 아이템이 존재하는지 확인하고 이터레이터를 가져옵니다.
    /// </summary>
    public bool TryGetFirstItem(int2 position, out Entity item, out NativeParallelMultiHashMapIterator<int2> iterator)
    {
        return Map.TryGetFirstValue(position, out item, out iterator);
    }

    /// <summary>
    /// 이터레이터를 통해 해당 좌표의 다음 아이템을 가져옵니다.
    /// </summary>
    public bool TryGetNextItem(out Entity item, ref NativeParallelMultiHashMapIterator<int2> iterator)
    {
        return Map.TryGetNextValue(out item, ref iterator);
    }

    /// <summary>
    /// 해당 좌표에 존재하는 아이템의 총 개수를 반환.
    /// </summary>
    public int CountItemsAt(int2 position)
    {
        return Map.CountValuesForKey(position);
    }

    /// <summary>
    /// 해당 좌표에 아이템이 하나라도 존재하는지 여부를 반환.
    /// </summary>
    public bool HasItemAt(int2 position)
    {
        return Map.ContainsKey(position);
    }
}

/// <summary>
/// 역할·목적: ItemSpatialIndex의 Native 맵 접근에 Reader/Writer Job 의존성을 결합한다. ECS 컴포넌트 의존성만으로 맵 접근을 보장하지 않는다.
/// 부착 엔티티: ItemSpatialIndex와 같은 World 단일 인덱스 엔티티다.
/// 생성: ItemSpatialSyncSystem.OnCreate가 기본 JobHandle 상태로 함께 붙인다.
/// 이용: 맵 Reader는 마지막 Writer를 선행으로 읽고 자신의 핸들을 AddReader한다. ItemSpatialSyncSystem(Synchronization)은 이전 Writer와 모든 Reader를 기다린 뒤 SetWriter한다.
/// Complete는 메인 스레드 직접 접근·용량 재할당·Dispose 전에 전체 등록 작업을 완료한다.
/// 제거: Complete/SetWriter가 이미 반영한 Reader 핸들을 초기화한다. 컴포넌트 자체는 인덱스 엔티티/World 수명을 따른다.
/// </summary>
public struct ItemSpatialIndexFence : IComponentData
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
