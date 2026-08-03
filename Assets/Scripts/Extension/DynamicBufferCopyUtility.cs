using Unity.Collections;
using Unity.Entities;

public static class DynamicBufferCopyUtility
{
    public static NativeArray<T> CreateNativeCopy<T>(
        DynamicBuffer<T> buffer,
        Allocator allocator)
        where T : unmanaged, IBufferElementData
    {
        NativeArray<T> copy = new NativeArray<T>(buffer.Length, allocator);

        for (int i = 0; i < buffer.Length; i++)
            copy[i] = buffer[i];

        return copy;
    }
}
