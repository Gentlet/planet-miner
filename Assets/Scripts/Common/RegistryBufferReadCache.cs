using Unity.Entities;

/// <summary>
/// 읽기 전용 registry 하나를 같은 chunk/호출의 지역값으로 재사용한다.
/// Job 필드로 전달하거나 구조 변경을 넘겨 보관하지 않는다. 각 인스턴스는 하나의 registry에만 사용한다.
/// </summary>
internal struct RegistryBufferReadCache<T> where T : unmanaged, IBufferElementData
{
    private bool _resolved;
    private bool _present;
    private DynamicBuffer<T> _buffer;

    internal DynamicBuffer<T> Read(in BufferLookup<T> lookup, Entity registry)
    {
        if (!_resolved || !_present)
        {
            // 앞선 TryRead의 부재도 필수 indexer 접근의 실패를 대신하지 않는다.
            _buffer = lookup[registry];
            _present = true;
            _resolved = true;
        }
        return _buffer;
    }

    internal bool TryRead(in BufferLookup<T> lookup, Entity registry, out DynamicBuffer<T> buffer)
    {
        if (!_resolved)
        {
            _present = lookup.TryGetBuffer(registry, out _buffer);
            _resolved = true;
        }
        buffer = _buffer;
        return _present;
    }
}
