using Unity.Jobs;

/// <summary>한 NativeContainer 소유자의 Writer/Reader 의존성 상태. 각 인덱스 컴포넌트가 독립된 값을 보유한다.</summary>
public struct NativeContainerFence
{
    private JobHandle _lastWriter;
    private JobHandle _readers;

    public JobHandle GetReaderDependency() => _lastWriter;

    public void AddReader(JobHandle readerHandle)
    {
        _readers = JobHandle.CombineDependencies(_readers, readerHandle);
    }

    public JobHandle GetWriterDependency()
    {
        return JobHandle.CombineDependencies(_lastWriter, _readers);
    }

    public void SetWriter(JobHandle writerHandle)
    {
        _lastWriter = writerHandle;
        _readers = default;
    }

    /// <summary>전체 등록 접근을 완료한 뒤 상태를 비운다. 변경/재할당/해제 전에 소유자가 호출한다.</summary>
    public void Complete()
    {
        JobHandle.CombineDependencies(_lastWriter, _readers).Complete();
        _lastWriter = default;
        _readers = default;
    }
}
